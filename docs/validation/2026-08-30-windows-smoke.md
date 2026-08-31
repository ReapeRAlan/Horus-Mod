# v2.0.0-rc.1 Windows smoke and Linux managed-build evidence

Date: 2026-08-29 to 2026-08-30 (America/Mexico_City)

This is implementation evidence, not the complete release acceptance report. It records what was actually executed on this workstation and leaves unexecuted matrix items explicitly pending.

## Environment

- Nuclear Option client installation: Steam app `2168680`.
- Official dedicated server: SteamCMD app `3930080`, installed in the isolated `HorusDedicatedTest/server-win` directory.
- Server advertised game version: `0.34.1`.
- Server Unity runtime: `2022.3.62.7762112`.
- BepInEx: `5.4.22.0`, 64-bit Windows/Mono.
- Server launch flags: `-batchmode -nographics`, hidden server, UDP test ports 17777/17778.
- Dedicated Horus policy: `Enabled=true`, `ModdedServer=true`, empty administrator allowlist, mission-unit deletion disabled.
- Official Linux depot: SteamCMD app `3930080` forced to platform `linux`; runtime execution unavailable on this workstation.

## Passed checks

- `Horus.Shared`, `Horus.Server`, and `Horus.Client` compile with zero warnings and zero errors.
- `Horus.Server` compiles directly against `NuclearOptionServer_Data/Managed` from the official server app.
- `Horus.Server` also compiles with zero warnings/errors against the official Linux depot's distinct `NuclearOptionServer_Data/Managed` assemblies. Decompiled IL for the Windows-reference and Linux-reference builds is identical across 36,855 lines; their raw PE hashes differ only in build metadata.
- All 80 pure logic, protocol, security, paging, state, and audit tests pass.
- BepInEx loads `Horus Dedicated Server 2.0.0`; Horus reports informational version `2.0.0-rc.1`.
- The dedicated Mirage handler registers after server startup.
- The empty SteamID64 allowlist loads as zero administrators (deny-all mutation policy).
- The hidden server logs on to Steam, selects BuiltIn mission `Escalation`, and reaches `Waiting for Players before loading next map`.
- A second dedicated restart loads complete economy/factory configuration without Horus warnings or exceptions.
- `Horus.Server.dll` has no direct reference to UI, IMGUI, legacy input, text rendering, or Rewired assemblies.
- BepInEx loads `Horus Mod Starter 2.0.0`; the client reports informational version `2.0.0-rc.1` and the deployed DLL SHA-256 matches the build output.
- A final client restart loads the complete economy and all six factory presets with `migration_lines=0` and `exception_error_lines=0`.
- The GM, Dedicated, and Full ZIP layouts and embedded `SHA256SUMS` manifests validate, and a second packaging run produces byte-identical ZIP hashes.
- The controlled two-minute Windows runtime check completed at `2026-08-30T23:55:02Z` with `fatalFindingCount=0`. Both required readiness markers (`Horus Dedicated Server` and `Waiting for Players before loading next map`) were present.
- A separate hidden-server instance downloaded public Workshop mission `3725687524` (`Escalation Gambler Edition`) to 100%, verified it as up to date, resolved its JSON, executed mission `AfterLoad`, selected it for the rotation, and reached the player-waiting state with `fatalFindingCount=0`.

## Runtime findings fixed during the smoke

1. BepInEx 5 rejects a SemVer prerelease suffix in `BepInPlugin`. The attribute now uses numeric `2.0.0`; displayed/informational protocol metadata remains `2.0.0-rc.1`.
2. Nuclear Option scene transitions stopped plugin update processing when the BepInEx manager was visible. Dedicated instructions now require `[Chainloader] HideManagerGameObject = true`.
3. Unity `JsonUtility` dropped nested factory/economy fields. The affected persistence paths now use the Newtonsoft.Json assembly distributed with the game/server.
4. Client preset parsing dropped production queues and normalization always treated the Economy preset's intentional zero interval as incomplete. Both causes were corrected and restart-tested.

## Evidence locations on this workstation

- Dedicated BepInEx log: `HorusDedicatedTest/server-win/BepInEx/LogOutput.log`.
- Dedicated Unity logs: `HorusDedicatedTest/server-win/logs/horus_smoke_3.log` through `horus_smoke_5.log` and `horus_client_test.log`.
- Controlled runtime bundle: `HorusDedicatedTest/server-win/runtime-evidence/windows/20260830-235302` (sanitized configuration, binary hashes, sampled process metrics, server log, BepInEx log, and `analysis.json`).
- Workshop runtime bundle: `HorusDedicatedTest/server-win-workshop/runtime-evidence/windows/20260831-005550`.
- Final client BepInEx log: `BepInEx/LogOutput.log`.
- Final client Unity log: `horus_client_factory_clean_2.log`.
- RC packages: `NuclearOptionZeusMod/dist/Horus-*-v2.0.0-rc.1.zip`.

## Still required before release

- Linux official-server execution was not possible because WSL/Linux is not installed on this workstation. The official Linux depot/API compile passed, but headless startup, mission rotation, networking, and soak still require a real Linux host.
- A real allowlisted Steam GM connection, denied GM, ordinary client without Horus, two simultaneous GMs, reconnect, mission change, incompatible protocol, and snapshot resync remain unexecuted.
- The server selected BuiltIn and Workshop missions but did not load either map without a connected player; connected gameplay and two completed rotations remain pending.
- The remote functional matrix (spawn, groups, Live Ordnance, orders, editing, safe delete, undo/redo, RTS, factories, and persistence observed by normal clients) remains pending.
- Abuse testing and the four-hour soak remain pending.
- Visual GM-client screenshots/video remain pending. The Windows automation helper could not run because the installed Node runtime was 22.17.1 while the helper required 22.22.0 or newer.

Do not tag, publish, or announce v2.0.0-rc.1 based only on this smoke report.
