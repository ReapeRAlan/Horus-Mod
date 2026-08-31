# v2.0.0-rc.1 Linux/WSL runtime evidence

Date: 2026-08-31 UTC

This report records observed Linux headless results. It does not convert unexecuted multiplayer, connected Workshop gameplay, mutation, rotation, or abuse scenarios into PASS results. The later exact-artifact soak is summarized in [the exact-RC runtime report](2026-08-31-exact-rc-runtime.md).

## Environment

- Host: WSL 2 with Ubuntu 24.04.4 LTS and the Microsoft Linux `6.18.33.2` x86_64 kernel.
- Official Nuclear Option dedicated-server Linux depot: Steam app `3930080`.
- Server game version: `0.34.1`.
- Unity runtime: `2022.3.62f2`.
- BepInEx: official Linux x64 `5.4.23.5` package.
- BepInEx archive SHA-256: `e538560be65739f562519ab518a75f9c65b3f57f87457403ae7cde683c12dab7`.
- Native test root: `/opt/horus/server-linux`.
- Launch flags: `-batchmode -nographics` with isolated UDP ports `18777` and `18778`.
- Horus policy: enabled, empty SteamID64 administrator allowlist, mission-unit deletion disabled.

## Passed checks

- The official executable is an x86-64 ELF binary and all directly linked libraries resolve on Ubuntu 24.04.
- The native WSL copies of `Horus.Server.dll` and `Horus.Shared.dll` match the final Windows build outputs by SHA-256.
- BepInEx reports `Bits64, Linux`, loads exactly one plugin, and starts `Horus Dedicated Server 2.0.0`.
- Horus reports informational version `2.0.0-rc.1`, loads zero administrators, applies its server patches, loads RTS configuration, and registers the Mirage handler.
- The official server loads the 64-bit Steam runtime, logs on anonymously, selects BuiltIn mission `Escalation`, and reaches `Waiting for Players before loading next map`.
- A separate instance downloaded public Workshop mission `3725687524` (`Escalation Gambler Edition`) to 100%, verified it as current, resolved the downloaded JSON, executed mission `AfterLoad`, selected it, and reached the player-waiting state.
- Two sequential headless runs completed successfully. The second run contains no literal `[Error]` marker, unhandled exception, Horus load failure, or missing readiness marker.
- The server was terminated by the controlled helper after each observation interval; hashes and CPU/RSS samples were retained.

## Runtime finding fixed

The Linux helper originally omitted the `linux64` loader directory when launching through BepInEx. The depot root also contains a 32-bit `steamclient.so`, so Steam rejected it with `ELFCLASS32` and the server exited. The helper now verifies that `linux64/steamclient.so` is a 64-bit ELF library and exports the same `LD_LIBRARY_PATH` used by the official `RunServer.sh` before launch.

## Expected headless output

Unity emits unsupported-shader messages while using its null graphics device under `-nographics`. These messages originate before Horus initializes and did not produce an unhandled exception or stop the server. Missing banlist noise was removed before the second run by creating the empty files referenced by the isolated server configuration.

## Evidence locations on this workstation

- First successful run: `/opt/horus/server-linux/runtime-evidence/linux/20260831-004047`.
- Clean restart: `/opt/horus/server-linux/runtime-evidence/linux/20260831-004409`.
- Workshop download/selection: `/opt/horus/server-linux-workshop/runtime-evidence/linux/20260831-005211`.
- Exact-artifact four-hour idle soak: `/opt/horus/server-linux-workshop/runtime-evidence/linux-final-rc-soak/20260831-035606` (PASS; 1,440 samples, frozen DLL hashes, zero fatal findings).

## Still pending

- BuiltIn/Workshop gameplay with a connected client and completed mission rotations beyond server-side mission selection.
- Allowlisted and denied real Steam GM sessions, ordinary client without Horus, protocol mismatch, reconnect, mission change, and resynchronization.
- The complete mutation/replication/abuse matrix and visual GM evidence.
- Two simultaneous GMs and two concurrent legitimate Steam identities.
