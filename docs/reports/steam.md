# Steam networking spike — 2026-09-22

Implemented an optional Windows x64 Steam lobby + Steam Networking Sockets path
for Unity 6000.3.2f1 / NGO 2.7.0. This is a code/compile/offline-verified pre-MVP
spike. **Two-account online play and actual SDR routing are not verified.**

Only `Assets/Steam`, `Assets/Scripts/HotelSteam.cs` and this report were changed.
`HotelSession`, packages, scenes and project settings were not modified. The
integrator must wire the API into the UI/session; existing UnityTransport/IP
behavior is unaffected by importing these files.

## Exact public API

Namespace `WorstHotel`, class `HotelSteam` (all entry points static). Call from the
Unity main thread during Play Mode or in a player; await normally on that thread.

```csharp
bool IsAvailable { get; }      // Already initialized by HotelSteam; no side effects
ulong LocalSteamId { get; }
ulong LobbyId { get; }
uint AppId { get; }
bool IsSpacewarTest { get; }
string LastError { get; }
const string Protocol = "WHE-premvp-1";

bool InitializeForTest(out string error); // Explicit UI click only, AppID 480
bool Initialize(uint appId, bool allowSpacewarTest, out string error);
bool Initialize(out string error); // --steam-app-id=ID / --steam-test-480 only
Task<HotelSteam.LobbyResult> CreateLobby();
Task<HotelSteam.LobbyResult> JoinLobby(ulong lobbyId);
bool ConfigureTransport(NetworkManager manager, bool host, ulong hostSteamId, out string error);
bool SetLobbyReady(bool ready, out string error);
void LeaveLobby();
void Shutdown();
// LobbyResult: bool Success; string Error; ulong LobbyId; ulong HostSteamId.
```

Host integration after the explicitly labelled **«Тест Steam (AppID 480)»** click:

1. `InitializeForTest(out error)`; show error and retain the IP option if false.
2. `var result = await CreateLobby()`; check `result.Success`.
3. Create/configure NetworkManager using existing session logic. Keep connection
   approval, password and message handlers. Call
   `ConfigureTransport(manager, true, result.HostSteamId, out error)` **after**
   creating NetworkConfig and **before** `StartHost`.
4. Check `manager.StartHost()`, then `SetLobbyReady(true, out error)`; only now
   expose `result.LobbyId` for the second player. If a step fails, stop the manager
   and `LeaveLobby()`. CreateLobby initially keeps the lobby non-joinable.

Client: `InitializeForTest` → `await JoinLobby(lobbyId)` → check result → existing
NetworkManager/NetworkConfig setup → `ConfigureTransport(manager, false,
result.HostSteamId, out error)` → `StartClient` → existing message registration.

Normal disconnect: request `manager.Shutdown()` and `LeaveLobby()`; do not reuse
that manager until NGO shutdown completes. A future session can retain the same
initialized Steam client. Application exit / explicit Steam reset: `Shutdown()`
stops the attached manager/transport, leaves the lobby and shuts down the owned
Steam client. Init/Shutdown/RunCallbacks must not be called from any other class.

Do not block Unity's main thread using `.Result` / `.Wait()` for lobby operations.
The helper object pumps Steam callbacks in Update even before NGO starts. Keep
the game's background update policy enabled for focus changes during co-op.
Keep the session's connect timeout; consider a distinct Steam message and a
longer timeout than the existing 12-second IP timeout during initial relay setup.

## Behavior and adaptations

- Two-person friends-only lobby. Join by numeric lobby ID; friends/invite/overlay
  UI, lobby browsing and command-line `+connect_lobby` are not implemented.
- Metadata validates game protocol, original host ID and ready state. Lobby
  membership is required before the host accepts a socket. NGO's existing
  password/protocol/capacity approval still applies after transport connection.
- Joining uses Facepunch `Lobby.Join()` and checks `RoomEnter.Success`; the pinned
  Facepunch `SteamMatchmaking.JoinLobbyAsync` ignores that response field.
- Create/join return a failure after 15 seconds, or when LeaveLobby/Shutdown
  cancels them. A late successful lobby is left. To avoid overlapping membership
  changes, retries remain blocked until that native request resolves or Steam is
  explicitly reset with Shutdown. IP can be used immediately.
- Host migration is deliberately unsupported: an ownership change ends the
  transport and leaves the lobby. Steam IDs identify peers; NGO still uses its
  own client IDs, with server/host ID 0.
- `CreateRelaySocket` / `ConnectRelay` use Steam Networking Sockets P2P APIs and
  initialize relay network access. No explicit direct-connection socket or IP
  fallback is created inside this transport. Actual routing is selected by Steam;
  relay-only routing is not forced or claimed to have been observed.
- Reliable deliveries use Steam reliable ordered messages. Unreliable remains
  unreliable. UnreliableSequenced adds wrap-safe sequence filtering; the community
  transport did not implement that guarantee. An 8-byte game-specific header
  means this transport is not wire-compatible with the unmodified community one.
- Maximum NGO payload per Send is **524,280 bytes**, leaving 8 bytes under Steam's
  512 KiB message limit. Large reliable messages are fragmented by Steam. The
  140,000-byte offline roundtrip exceeds the current HotelSession 131,072-byte
  incoming-world limit; normal world messages fit within the transport cap.
- Receive copies use Marshal.Copy, so no project-wide unsafe-code switch or
  Unity Collections dependency was introduced. At most 64 messages are received
  per transport poll. Reliable send failure reports NGO TransportFailure; this
  spike does not maintain an additional resend queue on Steam back pressure.
- Locally requested disconnect does not synchronously re-emit NGO Disconnect:
  NGO 2.7 already handles its cleanup/callback, avoiding duplicate callbacks.
- RTT uses Steam's connection status. Repeated transport shutdown is safe and
  does not call SteamClient.Shutdown. The runtime restores process SteamAppId /
  SteamGameId environment values on owned-client shutdown.

## Dependency provenance and commercial use

- [Facepunch.Steamworks 2.5.2](https://github.com/Facepunch/Facepunch.Steamworks/releases/tag/2.5.2),
  commit `5a22fa22dd8e337e9fa55ce0d18c07c022262063`; managed wrapper is MIT.
  Official release Windows Unity binary is .NET Framework 4.6 and compiles against
  Unity's .NET Standard 2.1 profile plus its supplied compatibility shims.
- [Unity community adapter](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/0fab638470379ace12b0149dfb41c043d23dbce5/Transports/com.community.netcode.transport.facepunch),
  commit `0fab638470379ace12b0149dfb41c043d23dbce5`; MIT. Both MIT licenses allow
  commercial proprietary use with retained notices. Copies are in Assets/Steam/Licenses.
- Valve `steam_api64.dll` is the unmodified redistributable from the same official
  release, **not MIT**. Applicable Valve SDK/distribution agreements and a real
  product AppID are still required for commercial Steam distribution. Their terms
  are not granted by the wrapper's license. See
  [Valve API documentation](https://partner.steamgames.com/doc/sdk/api) and
  [Valve's open-source distribution guidance](https://partner.steamgames.com/doc/sdk/uploading/distributing_opensource).
  No accounts/agreements/security settings were changed or products published.

Exact download locations, binary SHA-256 values, upstream reference source and
license notices are retained under Assets/Steam. The downloaded release archive
hash matched GitHub's published asset digest. Plugin .meta files enable only
Windows x64; no Posix/Win32 duplicate managed assemblies are imported.

AppID **480 identifies Valve Spacewar**, not Worst Hotel Ever. No AppID is enabled
by default, no steam_appid.txt is created, and Initialize(480, false, ...) fails.
InitializeForTest explicitly opts in for the UI-labelled pre-MVP test. No own
product AppID or second Steam account was available for this spike.

## Checks actually executed

`Assets/Steam/Validation/Verify-Steam.ps1` used the installed Editor's Roslyn,
CoreModule and .NET profile plus
`C:/Normal/Programs/Game/Worst Hotel Ever/Library/ScriptAssemblies/Unity.Netcode.Runtime.dll`.

- Windows player and Windows Editor compilation: **passed, warnings as errors**.
- Unsupported-platform compilation without any Facepunch reference: **passed**.
- **29 offline checks passed**, running the actual helper/framing source with
  actual managed dependencies and no native Steam DLL in the test directory:
  no-auto-init/explicit-480/unchanged-environment guards, pre-init API failures,
  repeated shutdown, offset handling, payload boundaries including 140 KB and
  524,280 bytes, stale/duplicate/lost/wrapped sequence handling, interleaved reliable
  data and 10,000 corrupt/truncated packet inputs.
- Unity Editor was **not launched**. SteamClient.Init was **not executed**.
  No claimed Unity player build, import execution, IL2CPP/AOT test, native Steam
  login, lobby create/join, two-account handshake, disconnect/reconnect gameplay,
  WAN relay or NAT traversal verification belongs to this spike.

## Remaining end-to-end validation

On two Windows x64 machines/accounts running Steam, with the same build and
explicit AppID 480 test opt-in (or the same authorized real AppID): host creates
and readies the lobby; a Steam friend joins by lobby ID; verify both NGO callbacks,
world updates, interaction authority, reconnect and host-leave behavior. Exercise
wrong password, full lobby, foreign/version-mismatched lobby, Steam unavailable,
focus changes, WAN/NAT, failed sends and return to IP. Inspect native Steam
connection diagnostics to distinguish successful online connection from actual
SDR use. Validate the copied native DLL and licence notices in the final build.
