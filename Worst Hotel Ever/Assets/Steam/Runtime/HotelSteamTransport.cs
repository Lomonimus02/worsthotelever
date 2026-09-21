#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
// Adapted from Unity's MIT community Facepunch transport, commit
// 0fab638470379ace12b0149dfb41c043d23dbce5. See ../Licenses/Unity-Community-MIT.txt.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace WorstHotel
{
    [DisallowMultipleComponent]
    public sealed class HotelSteamTransport : NetworkTransport, IConnectionManager, ISocketManager
    {
        sealed class Peer
        {
            internal Connection Connection;
            internal ulong SteamId;
            internal uint SentSequence, ReceivedSequence;
            internal bool HasSequence, Connected;
        }

        readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        ConnectionManager client;
        SocketManager server;
        Peer hostPeer;
        byte[] receiveBuffer = new byte[4096];
        bool active, serverMode;
        internal ulong TargetSteamId;
        internal bool HostMode;
        internal NetworkManager Manager;
        public string LastError { get; private set; } = "";
        public override ulong ServerClientId => 0;
        public override bool IsSupported => IntPtr.Size == 8;
        internal bool IsActive => active;
        const int VirtualPort = 0;

        public override void Initialize(NetworkManager networkManager = null)
        {
            Shutdown();
            Manager = networkManager;
            LastError = "";
        }

        public override bool StartServer()
        {
            if (active || !HostMode || !HotelSteam.IsAvailable) return false;
            try
            {
                server = SteamNetworkingSockets.CreateRelaySocket(VirtualPort, this);
                active = (uint)server.Socket != 0;
                serverMode = true;
                return active;
            }
            catch (Exception e) { LastError = e.Message; Shutdown(); return false; }
        }

        public override bool StartClient()
        {
            if (active || HostMode || !HotelSteam.IsAvailable || TargetSteamId == 0 ||
                TargetSteamId == HotelSteam.LocalSteamId) return false;
            try
            {
                client = SteamNetworkingSockets.ConnectRelay(TargetSteamId, VirtualPort, this);
                hostPeer = new Peer { Connection = client.Connection, SteamId = TargetSteamId };
                active = client.Connection.Id != 0;
                serverMode = false;
                return active;
            }
            catch (Exception e) { LastError = e.Message; Shutdown(); return false; }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0; payload = default; receiveTime = Time.realtimeSinceStartup;
            if (!active) return NetworkEvent.Nothing;
            if (!HotelSteam.IsAvailable) { Fail("Steam client is no longer available."); return NetworkEvent.Nothing; }
            try
            {
                // Bounded work per NGO poll. Steam status callbacks are pumped only by HotelSteam.
                client?.Receive(64, false);
                server?.Receive(64, false);
            }
            catch (Exception e) { Fail(e.Message); }
            return NetworkEvent.Nothing;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery)
        {
            if (!active) return;
            Peer peer = null;
            if (!serverMode && clientId == ServerClientId) peer = hostPeer;
            else if (serverMode) peers.TryGetValue(clientId, out peer);
            if (peer == null || !peer.Connected) return;
            byte mode = delivery == NetworkDelivery.Unreliable ? SteamPacket.Unreliable :
                delivery == NetworkDelivery.UnreliableSequenced ? SteamPacket.UnreliableSequenced : SteamPacket.Reliable;
            try
            {
                var packet = SteamPacket.Encode(payload, mode, mode == SteamPacket.UnreliableSequenced ? ++peer.SentSequence : 0);
                var result = peer.Connection.SendMessage(packet,
                    mode == SteamPacket.Reliable ? SendType.Reliable : SendType.Unreliable);
                // Dropping unreliable packets under back pressure is expected. Reliable loss is fatal.
                if (result != Result.OK && (mode == SteamPacket.Reliable || result != Result.LimitExceeded))
                    Fail("Steam SendMessage failed: " + result);
            }
            catch (Exception e) { Fail(e.Message); }
        }

        void Receive(Peer peer, ulong id, IntPtr data, int size)
        {
            if (!active || peer == null || !peer.Connected) return;
            if (size < SteamPacket.HeaderBytes || size > SteamPacket.MaxMessageBytes || data == IntPtr.Zero)
            { DisconnectPeer(id); return; }
            if (receiveBuffer.Length < size) receiveBuffer = new byte[size];
            Marshal.Copy(data, receiveBuffer, 0, size);
            if (SteamPacket.TryDecode(receiveBuffer, size, ref peer.HasSequence, ref peer.ReceivedSequence, out var payload))
                InvokeOnTransportEvent(NetworkEvent.Data, id, payload, Time.realtimeSinceStartup);
        }

        void DisconnectPeer(ulong id)
        {
            bool notify = active;
            if (serverMode) DisconnectRemoteClient(id); else DisconnectLocalClient();
            if (notify) InvokeOnTransportEvent(NetworkEvent.Disconnect, id, default, Time.realtimeSinceStartup);
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!peers.TryGetValue(clientId, out var peer)) return;
            peers.Remove(clientId);
            if (HotelSteam.IsAvailable) { peer.Connection.Flush(); peer.Connection.Close(true); }
            // NGO already reports a locally requested disconnect; do not re-enter its cleanup here.
        }

        public override void DisconnectLocalClient()
        {
            if (client == null) return;
            var previous = client;
            client = null; hostPeer = null;
            previous.Interface = null;
            if (HotelSteam.IsAvailable) previous.Close();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (!active || !HotelSteam.IsAvailable) return 0;
            Peer peer = null;
            if (!serverMode && clientId == ServerClientId) peer = hostPeer;
            else peers.TryGetValue(clientId, out peer);
            try { return peer == null ? 0 : (ulong)Math.Max(0, peer.Connection.QuickStatus().Ping); }
            catch (Exception) { return 0; }
        }

        public override void Shutdown()
        {
            active = false;
            if (client != null) client.Interface = null;
            if (server != null) server.Interface = null;
            try
            {
                if (HotelSteam.IsAvailable)
                {
                    client?.Close();
                    foreach (var peer in peers.Values) peer.Connection.Close();
                    server?.Close();
                }
            }
            catch (Exception e) { LastError = e.Message; }
            finally { client = null; server = null; hostPeer = null; peers.Clear(); }
        }

        internal void Fail(string error)
        {
            LastError = error;
            bool wasActive = active;
            Shutdown();
            if (wasActive) InvokeOnTransportEvent(NetworkEvent.TransportFailure, 0, default, Time.realtimeSinceStartup);
        }

        void IConnectionManager.OnConnecting(ConnectionInfo info) { }
        void IConnectionManager.OnConnected(ConnectionInfo info)
        {
            if (!active || hostPeer == null) return;
            if ((ulong)info.Identity.SteamId != TargetSteamId) { Fail("Unexpected Steam host identity."); return; }
            hostPeer.Connected = true;
            InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, default, Time.realtimeSinceStartup);
        }
        void IConnectionManager.OnDisconnected(ConnectionInfo info)
        { if (client != null) DisconnectPeer(ServerClientId); }
        void IConnectionManager.OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        { Receive(hostPeer, ServerClientId, data, size); }

        void ISocketManager.OnConnecting(Connection connection, ConnectionInfo info)
        {
            ulong steamId = info.Identity.SteamId;
            // One remote employee. Lobby membership is checked before accepting a Steam socket;
            // NGO still performs its existing password/protocol/gameplay approval afterwards.
            if (!active || peers.Count >= 1 || !HotelSteam.IsLobbyMember(steamId))
            { connection.Close(false, 0, "Lobby member required or hotel full."); return; }
            var result = connection.Accept();
            if (result != Result.OK) { connection.Close(); return; }
            peers[connection.Id] = new Peer { Connection = connection, SteamId = steamId };
        }
        void ISocketManager.OnConnected(Connection connection, ConnectionInfo info)
        {
            if (!active || !peers.TryGetValue(connection.Id, out var peer)) { connection.Close(); return; }
            peer.Connected = true;
            InvokeOnTransportEvent(NetworkEvent.Connect, connection.Id, default, Time.realtimeSinceStartup);
        }
        void ISocketManager.OnDisconnected(Connection connection, ConnectionInfo info)
        {
            bool notify = active && peers.TryGetValue(connection.Id, out var peer) && peer.Connected;
            DisconnectRemoteClient(connection.Id);
            connection.Close();
            if (notify) InvokeOnTransportEvent(NetworkEvent.Disconnect, connection.Id, default, Time.realtimeSinceStartup);
        }
        void ISocketManager.OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size,
            long messageNum, long recvTime, int channel)
        {
            if (peers.TryGetValue(connection.Id, out var peer) && peer.SteamId == (ulong)identity.SteamId)
                Receive(peer, connection.Id, data, size);
        }
        void OnDestroy() { Shutdown(); }
    }
}
#endif
