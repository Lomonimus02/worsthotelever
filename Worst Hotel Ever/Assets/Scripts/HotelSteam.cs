using System;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using Steamworks;
using Steamworks.Data;
#endif

namespace WorstHotel
{
    /// <summary>
    /// Optional Steam entry point. Call all methods on Unity's main thread.
    /// Owns Init, the callback pump, and Shutdown; the transport never owns SteamClient.
    /// Nothing initializes automatically, so the existing IP path needs no Steam account.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HotelSteam : MonoBehaviour
    {
        // Keep standalone Steam validation independent of the rest of the game assembly.
        // HotelPresentationTests asserts equality with the session protocol.
        public const string Protocol = "WHE-premvp-2";
        const string ProtocolKey = "whe.protocol", HostKey = "whe.host", ReadyKey = "whe.ready";
        const string Unsupported = "Steam поддерживается только в Windows x64. Доступно подключение по IP.";
        public static string LastError { get; private set; } = "";
        public static uint AppId { get; private set; }
        public static bool IsSpacewarTest => AppId == 480;

        public readonly struct LobbyResult
        {
            public bool Success { get; }
            public string Error { get; }
            public ulong LobbyId { get; }
            public ulong HostSteamId { get; }
            internal LobbyResult(string error, ulong lobbyId = 0, ulong hostSteamId = 0)
            { Error = error; Success = string.IsNullOrEmpty(error); LobbyId = lobbyId; HostSteamId = hostSteamId; }
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        static HotelSteam instance;
        static bool ownsClient;
        static Lobby? lobby;
        static ulong lobbyHost;
        static Task<Lobby?> pendingRequest;
        static TaskCompletionSource<bool> cancelRequest;
        static int runtimeVersion;
        static HotelSteamTransport transport;
        static string previousAppId, previousGameId;
        public static bool IsAvailable => ownsClient && SteamClient.IsValid;
        public static ulong LocalSteamId => IsAvailable ? (ulong)SteamClient.SteamId : 0;
        public static ulong LobbyId => lobby.HasValue ? (ulong)lobby.Value.Id : 0;
#else
        public static bool IsAvailable => false;
        public static ulong LocalSteamId => 0;
        public static ulong LobbyId => 0;
#endif

        // Explicit command line only: --steam-app-id=YOUR_ID, or --steam-test-480 for pre-MVP testing.
        // No steam_appid.txt is generated and 480 is never a default.
        public static bool Initialize(out string error)
        {
            uint appId = 0;
            bool allowTest = false;
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg == "--steam-test-480") allowTest = true;
                if (arg.StartsWith("--steam-app-id=", StringComparison.Ordinal) &&
                    !uint.TryParse(arg.Substring(15), NumberStyles.None, CultureInfo.InvariantCulture, out appId))
                    return Error("Неверный --steam-app-id.", out error);
            }
            if (allowTest && appId == 0) appId = 480;
            return Initialize(appId, allowTest, out error);
        }

        public static bool Initialize(uint appId, bool allowSpacewarTest, out string error)
        {
            if (appId == 0) return Error("Steam AppID не задан. Для явного pre-MVP теста используйте --steam-test-480; доступен IP.", out error);
            if (appId == 480 && !allowSpacewarTest)
                return Error("AppID 480 — Spacewar, только явный pre-MVP тест, не AppID Worst Hotel Ever.", out error);
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (IntPtr.Size != 8) return Error(Unsupported, out error);
            if (IsAvailable)
            {
                if (AppId != appId) return Error("Перед сменой Steam AppID вызовите Shutdown.", out error);
                error = ""; return true;
            }
            if (!Application.isPlaying) return Error("Инициализируйте Steam в Play Mode или Windows player.", out error);
            if (SteamClient.IsValid) return Error("SteamClient уже принадлежит другому компоненту. Нужен один владелец: HotelSteam.", out error);
            try
            {
                previousAppId = Environment.GetEnvironmentVariable("SteamAppId");
                previousGameId = Environment.GetEnvironmentVariable("SteamGameId");
                ownsClient = true;
                SteamClient.Init(appId, false);
                if (!SteamClient.IsValid) throw new InvalidOperationException("SteamClient.Init did not initialize Steam.");
                AppId = appId;
                runtimeVersion++;
                SteamNetworkingUtils.InitRelayNetworkAccess();
                var go = new GameObject("Hotel Steam callbacks");
                instance = go.AddComponent<HotelSteam>();
                DontDestroyOnLoad(go);
                if (appId == 480) Debug.LogWarning("WHE STEAM TEST: AppID 480 (Spacewar), pre-MVP only; not a product AppID.");
                LastError = error = "";
                return true;
            }
            catch (Exception e)
            {
                Shutdown();
                return Error("Steam недоступен: " + e.Message + " Можно использовать IP.", out error);
            }
#else
            return Error(Unsupported, out error);
#endif
        }

        // UI must label the button "Тест Steam (AppID 480)". Call only on an explicit click.
        public static bool InitializeForTest(out string error) => Initialize(480, true, out error);

        public static Task<LobbyResult> CreateLobby()
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            return ChangeLobby(true, 0);
#else
            return Task.FromResult(Failure(Unsupported));
#endif
        }

        public static Task<LobbyResult> JoinLobby(ulong lobbyId)
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (lobbyId == 0) return Task.FromResult(Failure("Укажите Steam Lobby ID."));
            return ChangeLobby(false, lobbyId);
#else
            return Task.FromResult(Failure(Unsupported));
#endif
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        static async Task<Lobby?> AcquireLobby(bool create, ulong id)
        {
            if (create) return await SteamMatchmaking.CreateLobbyAsync(2);
            // Facepunch 2.5.2 JoinLobbyAsync omits RoomEnter validation; use Lobby.Join instead.
            var joined = new Lobby(id);
            var result = await joined.Join();
            if (result != RoomEnter.Success) throw new InvalidOperationException("Steam lobby: " + result);
            return joined;
        }

        static async Task<LobbyResult> ChangeLobby(bool create, ulong id)
        {
            if (!IsAvailable) return Failure("Сначала инициализируйте Steam. Доступен IP.");
            if (lobby.HasValue) return Failure("Сначала отключитесь и вызовите LeaveLobby.");
            if (pendingRequest != null) return Failure("Steam ещё обрабатывает запрос лобби. Для сброса вызовите Shutdown.");
            int version = runtimeVersion;
            var cancel = new TaskCompletionSource<bool>();
            cancelRequest = cancel;
            var request = AcquireLobby(create, id);
            pendingRequest = request;
            Lobby? acquired = null;
            try
            {
                var completed = await Task.WhenAny(request, Task.Delay(15000), cancel.Task);
                if (completed != request || cancel.Task.IsCompleted || version != runtimeVersion || !IsAvailable)
                {
                    // Observe and leave a late successful lobby. Do not start overlapping requests.
                    _ = DiscardLateLobby(request, version);
                    return Failure(cancel.Task.IsCompleted ? "Запрос Steam отменён." : "Steam не ответил за 15 секунд. Доступен IP.");
                }
                acquired = await request;
                if (!acquired.HasValue) return Failure("Steam не создал лобби.");
                var value = acquired.Value;
                ulong host = value.Owner.Id;
                if (create)
                {
                    host = LocalSteamId;
                    if (!value.SetFriendsOnly() || !value.SetJoinable(false) ||
                        !value.SetData(ProtocolKey, Protocol) ||
                        !value.SetData(HostKey, host.ToString(CultureInfo.InvariantCulture)) ||
                        !value.SetData(ReadyKey, "0"))
                        throw new InvalidOperationException("Не удалось настроить Steam lobby.");
                }
                else
                {
                    if (value.GetData(ProtocolKey) != Protocol || value.GetData(ReadyKey) != "1" ||
                        !ulong.TryParse(value.GetData(HostKey), out var advertisedHost) ||
                        advertisedHost != host || host == LocalSteamId || !ContainsMember(value, LocalSteamId))
                        throw new InvalidOperationException("Лобби другой версии, хост ушёл или ещё не готов.");
                }
                lobby = value; lobbyHost = host;
                LastError = "";
                return new LobbyResult("", value.Id, host);
            }
            catch (Exception e)
            {
                if (acquired.HasValue && version == runtimeVersion && IsAvailable) acquired.Value.Leave();
                return Failure(e.Message);
            }
            finally
            {
                if (request.IsCompleted && ReferenceEquals(pendingRequest, request)) pendingRequest = null;
                if (ReferenceEquals(cancelRequest, cancel)) cancelRequest = null;
            }
        }

        static async Task DiscardLateLobby(Task<Lobby?> request, int version)
        {
            try
            {
                var abandoned = await request;
                if (version == runtimeVersion && IsAvailable && abandoned.HasValue) abandoned.Value.Leave();
            }
            catch (Exception) { /* Observed; the UI has already received timeout/cancellation. */ }
            finally { if (ReferenceEquals(pendingRequest, request)) pendingRequest = null; }
        }

        static bool ContainsMember(Lobby value, ulong steamId)
        {
            foreach (var member in value.Members) if ((ulong)member.Id == steamId) return true;
            return false;
        }

        internal static bool IsLobbyMember(ulong steamId) => IsAvailable && lobby.HasValue &&
            lobbyHost == LocalSteamId && steamId != LocalSteamId && ContainsMember(lobby.Value, steamId);
#endif

        public static bool ConfigureTransport(NetworkManager manager, bool host, ulong hostSteamId, out string error)
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (!IsAvailable) return Error("Steam не инициализирован. Используйте IP.", out error);
            if (manager == null || manager.NetworkConfig == null || manager.IsListening || manager.ShutdownInProgress)
                return Error("Настройте Steam transport до StartHost/StartClient на остановленном NetworkManager.", out error);
            if (!lobby.HasValue || hostSteamId != lobbyHost ||
                host != (hostSteamId == LocalSteamId) || (ulong)lobby.Value.Owner.Id != lobbyHost)
                return Error("Сначала создайте/войдите в Steam lobby; нужен HostSteamId из LobbyResult.", out error);
            if (transport != null && transport.Manager != manager && transport.IsActive)
                return Error("Уже работает другая Steam-сессия.", out error);
            var selected = manager.GetComponent<HotelSteamTransport>();
            if (selected == null) selected = manager.gameObject.AddComponent<HotelSteamTransport>();
            selected.HostMode = host;
            selected.TargetSteamId = hostSteamId;
            selected.Manager = manager;
            manager.NetworkConfig.NetworkTransport = selected;
            transport = selected;
            LastError = error = "";
            return true;
#else
            return Error(Unsupported, out error);
#endif
        }

        // Call with true only after StartHost succeeded; publishing an unready lobby is avoided.
        public static bool SetLobbyReady(bool ready, out string error)
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (!IsAvailable || !lobby.HasValue || lobbyHost != LocalSteamId)
                return Error("Нет собственного Steam lobby.", out error);
            if (ready && (transport == null || !transport.IsActive || transport.Manager == null || !transport.Manager.IsHost))
                return Error("Сначала успешно запустите StartHost.", out error);
            var value = lobby.Value;
            if (!value.SetData(ReadyKey, ready ? "1" : "0") || !value.SetJoinable(ready))
                return Error("Steam не обновил доступность лобби.", out error);
            LastError = error = ""; return true;
#else
            return Error(Unsupported, out error);
#endif
        }

        // Stop the NetworkManager before leaving a connected lobby.
        public static void LeaveLobby()
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            cancelRequest?.TrySetResult(true);
            var previous = lobby;
            lobby = null; lobbyHost = 0;
            if (IsAvailable && previous.HasValue)
            {
                if (previous.Value.IsOwnedBy(LocalSteamId)) previous.Value.SetJoinable(false);
                previous.Value.Leave();
            }
#endif
        }

        public static void Shutdown()
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            runtimeVersion++;
            cancelRequest?.TrySetResult(true);
            cancelRequest = null; pendingRequest = null;
            try
            {
                if (transport != null)
                {
                    if (transport.Manager != null && transport.Manager.IsListening) transport.Manager.Shutdown();
                    transport.Shutdown();
                }
                LeaveLobby();
            }
            catch (Exception e) { LastError = e.Message; }
            finally
            {
                transport = null; lobby = null; lobbyHost = 0;
                if (ownsClient)
                {
                    try { SteamClient.Shutdown(); }
                    catch (Exception e) { LastError = e.Message; }
                    Environment.SetEnvironmentVariable("SteamAppId", previousAppId);
                    Environment.SetEnvironmentVariable("SteamGameId", previousGameId);
                }
                ownsClient = false; AppId = 0;
                var previous = instance; instance = null;
                if (previous != null) Destroy(previous.gameObject);
            }
#endif
        }

        static bool Error(string message, out string error) { LastError = error = message; return false; }
        static LobbyResult Failure(string message) { LastError = message; return new LobbyResult(message); }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        void Update()
        {
            if (instance != this || !IsAvailable) return;
            try
            {
                SteamClient.RunCallbacks();
                // Steam may transfer lobby ownership. This spike intentionally has no host migration.
                if (lobby.HasValue && (ulong)lobby.Value.Owner.Id != lobbyHost)
                {
                    if (transport != null) transport.Fail("Steam lobby host left; host migration is unsupported.");
                    LeaveLobby();
                }
            }
            catch (Exception e)
            {
                LastError = "Ошибка Steam: " + e.Message;
                if (transport != null) transport.Fail(LastError);
                Shutdown();
            }
        }
        void OnApplicationQuit() { if (instance == this) Shutdown(); }
        void OnDestroy() { if (instance == this) { instance = null; Shutdown(); } }
#endif
    }
}
