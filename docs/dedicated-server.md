# Horus on a dedicated Nuclear Option server

Horus 2.0.0-rc.1 contains the dedicated-server architecture, protocol, authorization, audit trail, and packaging needed for validation. It is a release candidate, not a production-certified release: Windows and Linux runtime acceptance must pass before publishing 2.0.0.

## Package selection

| Machine | Package | Assemblies |
| --- | --- | --- |
| Game Master client | `Horus-GM-v2.0.0-rc.1.zip` | `Horus.Shared.dll`, `Horus.Client.dll` |
| Dedicated server | `Horus-Dedicated-v2.0.0-rc.1.zip` | `Horus.Shared.dll`, `Horus.Server.dll` |
| Single-player or local host | `Horus-Full-v2.0.0-rc.1.zip` | all three assemblies |

Ordinary players do not need Horus when the GM uses only native Nuclear Option content. Content mods that add aircraft, ships, weapons, or other assets must be installed with matching versions wherever those assets must be simulated or rendered.

## Install the official server

The official [Nuclear Option dedicated-server guide](https://github.com/Shockfront-Studios/Nuclear-Option-Server-Tools/blob/main/DedicatedServerGuide.md) is authoritative. The dedicated app ID is `3930080`.

Windows SteamCMD example:

```powershell
steamcmd.exe +force_install_dir C:\NuclearOptionServer +login anonymous +app_update 3930080 validate +quit
```

Linux SteamCMD example:

```bash
./steamcmd.sh +force_install_dir /opt/nuclear-option-server +login anonymous +app_update 3930080 validate +quit
```

### WSL 2 validation host

WSL is suitable for validating the official Linux binary because it runs a real Linux kernel. From an elevated Windows terminal, install Ubuntu 24.04 and restart when Windows requests it:

```powershell
wsl --install -d Ubuntu-24.04
```

After restart, confirm `wsl --list --verbose` reports version 2. Store the active Linux server inside the Linux filesystem (for example `/opt/horus/server-linux`) rather than executing it directly from `/mnt/c`; this avoids Windows-filesystem permission and performance differences. Prefer mirrored WSL networking on supported Windows 11 builds and expose only the configured UDP game/query ports.

The Linux depot may be downloaded directly inside WSL or copied from a SteamCMD download that was explicitly forced to the Linux platform. Verify `NuclearOptionServer.x86_64`, `UnityPlayer.so`, and `NuclearOptionServer_Data/Managed` before installing BepInEx.

Install the BepInEx 5 Mono build appropriate for the operating system into the dedicated-server root. Start the server once to create `BepInEx/config`, stop it, set `HideManagerGameObject = true` under `[Chainloader]` in `BepInEx/config/BepInEx.cfg`, and extract `Horus-Dedicated-v2.0.0-rc.1.zip` into that same root. Nuclear Option scene loading can otherwise remove the BepInEx manager and stop plugin `Update` processing.

For Windows allow inbound UDP 7777 and 7778, or the ports selected in `DedicatedServerConfig.json`. Apply the equivalent firewall policy on Linux. The optional official administrative TCP service is separate from Horus and is never used for unit control.

## Configure deliberate access

1. Merge `DedicatedServerConfig.horus.example.json` into the server's active `DedicatedServerConfig.json`. Replace `MissionDirectory` with an absolute Windows or Linux path, set `"ModdedServer": true`, and configure a valid BuiltIn, User, or Workshop mission rotation.
2. Add one exact SteamID64 per line to `BepInEx/config/HorusMod/dedicated_admins.txt`. Comments begin with `#`. An empty or invalid file denies all Horus mutations.
3. Set `Enabled = true` in `BepInEx/config/Horus.Server.cfg` only after the allowlist has been reviewed.
4. Keep `AllowMissionUnitDelete = false` unless authorized GMs must delete mission-authored units. The default permits deleting only units created through Horus.
5. Install the GM package on the operator's normal game client. The GM joins through Steam as a normal authenticated player and occupies a player slot. Spectator authentication is not used.

Authorization is fail-closed. Horus compares the exact SteamID64 supplied by the game's authenticated `INetworkPlayer.AuthData`; a display name, faction, password, claimed owner, or UDP-only connection never grants Horus permissions.

## Protocol and safety behavior

- The Mirage protocol is versioned and uses manually registered serializers. No Unity object crosses the wire: commands contain stable definition keys, persistent/network IDs, numeric positions, enums, and bounded lists.
- The authoritative server re-resolves catalogs, costs, hardpoints, factions, targets, ownership, placement, and current revision. Client calculations are advisory only.
- Messages are capped at 16 KiB, lists at 64 entities, routes at 32 waypoints, and loadouts at 64 mounts. NaN, infinity, unknown keys, incompatible versions, stale revisions, duplicates, and rate-limit violations are rejected.
- Each mission has a new session ID and monotonic revision. Clients receive paged snapshots and resynchronize after reconnects, mission changes, or revision gaps.
- Accepted and rejected mutations are written as daily JSONL under `BepInEx/config/HorusMod/audit`; the default retention is 14 days.
- Nuclei is an optional soft integration. When present, Horus registers read-only status/diagnostic commands; no Nuclei or TCP command mutates Horus state.

## Build and package from source

From the repository root on Windows:

```powershell
dotnet build .\Horus.Server.csproj -c Release -p:NuclearOptionDir="C:\NuclearOptionServer" -p:NuclearOptionManagedDir="C:\NuclearOptionServer\NuclearOptionServer_Data\Managed"
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -Package All -ServerNuclearOptionDir "C:\NuclearOptionServer" -ServerManagedDir "C:\NuclearOptionServer\NuclearOptionServer_Data\Managed"
```

From Linux, build with a .NET SDK/Mono reference-assembly environment and point the properties at the Linux server install:

```bash
dotnet build ./Horus.Server.csproj -c Release -p:NuclearOptionDir=/opt/nuclear-option-server -p:NuclearOptionManagedDir=/opt/nuclear-option-server/NuclearOptionServer_Data/Managed
pwsh -NoProfile -File ./package.ps1 -Package All -NuclearOptionDir /opt/nuclear-option-client -NuclearOptionManagedDir /opt/nuclear-option-client/NuclearOption_Data/Managed -ServerNuclearOptionDir /opt/nuclear-option-server -ServerManagedDir /opt/nuclear-option-server/NuclearOptionServer_Data/Managed
```

The packaging script performs project builds and the logic test suite unless `-SkipBuild` is explicitly supplied. It creates deterministic ZIPs, embeds per-file SHA-256 values in `SHA256SUMS`, and never deploys into a live game or server installation.

The complete maintainer gate is:

```powershell
./build/validate-release.ps1
```

It performs strict UTF-8/global-English checks, JSON and documentation validation, pure tests, zero-warning builds, the headless dependency audit, two byte-identical packaging runs, archive-layout verification, embedded checksum verification, and `git diff --check`. Runtime helpers are documented in [build/runtime/README.md](../build/runtime/README.md).

## Required release-candidate validation

The following evidence is required before promoting the RC to stable `v2.0.0`. A public prerelease may retain an unavailable infrastructure scenario as `PENDING` only when the release notes and validation matrix state it explicitly:

- BepInEx starts headless with no graphics, IMGUI, camera, input, or Rewired exception, and two mission rotations complete.
- A BuiltIn mission and a Workshop mission both run.
- An allowlisted GM succeeds; a non-allowlisted Steam player and a client without Horus remain normal players; incompatible protocol versions fail closed.
- Reconnection, mission change, two simultaneous GMs, deduplication, stale-revision recovery, and snapshot paging work.
- Spawn, group spawn, Live Ordnance, duplicate, safe delete, movement, attack, patrol, guard, ROE, loadouts, liveries, fuel, skill, undo/redo, RTS budgets, factory queue/rally/start/stop, production, and persistence pass.
- Ordinary clients observe the game's native replicated results without installing Horus.
- Abuse limits pass and a four-hour soak has no unhandled exceptions or unbounded audit/state growth.
- GM-client screenshots or video, clean logs, package hashes, server configuration, and exact game/BepInEx/mod versions are retained with the test report.

Windows user profiles do not create independent Steam identities. If no second legitimate Steam account is available, two simultaneous GMs and two concurrent identities must remain `PENDING – second legitimate Steam identity unavailable`; they must never be simulated by changing a claimed SteamID or network owner field.

Use [the release checklist](validation/release-checklist.md) and update [the machine-readable release matrix](validation/release-matrix.json) after every run. `PASS` means observed evidence exists; successful compilation alone is not runtime evidence.

The official [server command documentation](https://github.com/Shockfront-Studios/Nuclear-Option-Server-Tools/blob/main/ServerCommands/Readme.md) may be used for status and mission administration only; its published JSON protocol does not provide the authenticated player identity required for Horus mutations.

## Remove Horus

Stop the process, remove `BepInEx/plugins/Horus/Horus.Server.dll` and `Horus.Shared.dll`, then restart. Preserve or archive `BepInEx/config/HorusMod/audit` if the audit record is required. Removing the DLLs does not remove units already saved into a mission by other workflows.
