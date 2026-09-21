# Optional Steam spike

Windows x64 only. No UPM changes, asmdef, unsafe-code setting, scene component,
`steam_appid.txt`, or automatic Steam initialization is required. The two plugin
importers enable only Windows x64 Editor / Win64 player; all other platforms keep
the `HotelSteam` API as a graceful unsupported stub.

`HotelSteam` is the sole SteamClient owner. `HotelSteamTransport` adapts the MIT
Unity community Facepunch transport for the current NGO session. The caller still
owns NetworkManager, game state, password approval, and the IP fallback UI.

Pinned dependencies (retrieved 2026-09-22):

- Facepunch.Steamworks **2.5.2**, source commit
  `5a22fa22dd8e337e9fa55ce0d18c07c022262063`.
  [Official release](https://github.com/Facepunch/Facepunch.Steamworks/releases/tag/2.5.2),
  [pinned source](https://github.com/Facepunch/Facepunch.Steamworks/tree/5a22fa22dd8e337e9fa55ce0d18c07c022262063).
  Managed binary comes from `Release/Unity/Facepunch.Steamworks.Win64.dll`.
  Native binary comes from `Release/Unity/redistributable_bin/win64/steam_api64.dll`.
  The release SHA-256 matches the digest published by GitHub's release asset API.
- Unity community transport source commit
  `0fab638470379ace12b0149dfb41c043d23dbce5`.
  [Original source](https://github.com/Unity-Technologies/multiplayer-community-contributions/blob/0fab638470379ace12b0149dfb41c043d23dbce5/Transports/com.community.netcode.transport.facepunch/Runtime/FacepunchTransport.cs).
  Only the adapted transport is compiled. `SourceReference/*.cs.txt` are unmodified
  audit copies of the upstream transport and Facepunch APIs used here, not a second
  build of Facepunch. The full source remains pinned by the link above.

SHA-256:

```text
Release ZIP:          83ef0b8b07bd5545c3732c65011f0baa9bf003cb53c2279c56397270368bca22
Facepunch Win64 DLL:  834f3d6670ac49cb0ba8d899c0c6c63689328c7188ca0534798b2cafa4938716
steam_api64.dll:      670d654aa3255c5061cf0236a1cdbb2f5076cbd5ae44f613b6f4921558e83e2d
```

MIT notices are retained in `Licenses`. Valve's native redistributable has separate
proprietary SDK terms, described without relicensing it in `Valve-Steamworks-NOTICE.txt`.
Copy these notices with distributions containing the dependencies; Unity does not
automatically copy arbitrary text assets into a player build.

Run `Validation/Verify-Steam.ps1` with installed Unity 6000.3.2f1 and the existing
NGO 2.7.0 assembly to reproduce compiler and offline checks. This never launches
Unity or initializes Steam. See `docs/reports/steam.md` at the repository root for
integration order and the remaining two-account online checks.
