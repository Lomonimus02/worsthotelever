using System;

namespace WorstHotel
{
    // Small framing layer: Steam preserves reliable ordering; unreliable sequencing is ours.
    internal static class SteamPacket
    {
        internal const int HeaderBytes = 8;
        internal const int MaxMessageBytes = 512 * 1024;
        internal const int MaxPayloadBytes = MaxMessageBytes - HeaderBytes;
        internal const byte Unreliable = 0, UnreliableSequenced = 1, Reliable = 2;

        internal static byte[] Encode(ArraySegment<byte> payload, byte mode, uint sequence)
        {
            if (payload.Array == null || payload.Count > MaxPayloadBytes || mode > Reliable)
                throw new ArgumentException("Invalid Steam transport payload.");
            var packet = new byte[HeaderBytes + payload.Count];
            packet[0] = (byte)'W'; packet[1] = (byte)'H'; packet[2] = 1; packet[3] = mode;
            for (int i = 0; i < 4; i++) packet[4 + i] = (byte)(sequence >> (8 * i));
            Buffer.BlockCopy(payload.Array, payload.Offset, packet, HeaderBytes, payload.Count);
            return packet;
        }

        internal static bool TryDecode(byte[] packet, int size, ref bool hasSequence,
            ref uint lastSequence, out ArraySegment<byte> payload)
        {
            payload = default;
            if (packet == null || size < HeaderBytes || size > packet.Length || size > MaxMessageBytes ||
                packet[0] != 'W' || packet[1] != 'H' || packet[2] != 1 || packet[3] > Reliable)
                return false;
            if (packet[3] == UnreliableSequenced)
            {
                uint sequence = 0;
                for (int i = 0; i < 4; i++) sequence |= (uint)packet[4 + i] << (8 * i);
                if (hasSequence && unchecked((int)(sequence - lastSequence)) <= 0) return false;
                hasSequence = true;
                lastSequence = sequence;
            }
            payload = new ArraySegment<byte>(packet, HeaderBytes, size - HeaderBytes);
            return true;
        }
    }
}
