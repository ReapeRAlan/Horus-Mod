# Exact RC Runtime and Runner-Safeguard Evidence

## Scope

This report records observed results for the commit-independent `v2.0.0-rc.1` release assemblies after protocol, rejection-audit, and authoritative command-shape hardening in source commit `29db88b6ef208724e3aab24be9bffacfe4cdc75f`. It does not convert connected-client, mutation, mission-rotation, abuse, or multi-account scenarios into PASS results.

## Exact artifacts

- Pre-evidence Dedicated package SHA-256 at the frozen binary source commit: `9e5adcf865513969e8e39500389a81c9538e6c3b39f51fa7a8c2c83942950d71`. This is not the final documentation-bearing release asset; the final sidecar is authoritative.
- `Horus.Server.dll` SHA-256: `5b855a7fa40fd00e37b60d9202d376ce0b8defefbb7a8b1e0b78c8d4a55e026f`.
- `Horus.Shared.dll` SHA-256: `08825ce99b62936d324f713d806b6e0e19a544bc711c1962d378fd32f68b6db0`.
- `Horus.Client.dll` SHA-256: `4e8f8d9541787caee90ebe788f834e4f0bf2c6f7129d6afbb78f0ff56f99c7c8`.
- Clean-tree release gate: PASS with 138 tests, zero build errors, zero build warnings, headless dependency audit, two identical packaging runs, and valid embedded checksums.
- GitHub PR #3 checks on this source commit: `public-validation` PASS and GitGuardian PASS.

## Revision-independent reproducibility

The release gate rebuilds Shared, Server, and Client with two distinct forced `SourceRevisionId` values and rejects any assembly hash difference. The unchanged Shared and Client hashes also match the earlier cross-commit proof. The hardened Server hash above is rechecked after evidence-only commits so distributed code remains independent of Git revision metadata.

Release builds omit PDB generation so SourceLink cannot couple a distributed DLL to a Git SHA through its debug-directory identifier. Debug builds retain their normal development behavior. The exact source commit remains recorded in `release-manifest.json`.

## Linux exact-DLL smoke

- Official Linux depot with BepInEx 5 under Ubuntu 24.04 WSL 2.
- Workshop mission: `3725687524`.
- Evidence directory: `/opt/horus/server-linux-workshop/runtime-evidence/linux-final-rc-smoke/20260831-035328`.
- Requested duration: two minutes.
- Ready marker observed: PASS.
- Runtime failure: empty.
- Readiness limit: 300 seconds.
- Unity log safety limit: 16 MiB.
- Final sampled RSS: 409,796 KiB.
- Fatal runtime scan: no unhandled exception, null reference, stack overflow, out-of-memory event, or Horus load failure.
- Evidence SHA256SUMS contains the exact Server and Shared hashes listed above.

## Windows exact-DLL smoke

- Official Windows depot with BepInEx 5.
- Workshop mission: `3725687524`.
- Evidence directory: `HorusDedicatedTest/server-win-workshop/runtime-evidence/windows-fixed-rc-smoke/20260831-043251`.
- Started: `2026-08-31T04:32:51Z`; completed: `2026-08-31T04:34:51Z`.
- Requested duration: two minutes.
- Ready marker observed: PASS.
- Runtime failure: empty.
- Readiness limit: 300 seconds.
- Unity log safety limit: 16 MiB.
- Metrics: 12 samples; final working set 388.70 MiB; final private memory 516.44 MiB.
- Fatal runtime scan: zero findings; both Horus-load and server-ready patterns were present.
- Evidence hashes contain the exact Server and Shared hashes listed above.

## Linux exact-DLL four-hour soak

- Official Linux depot with BepInEx 5 under Ubuntu 24.04 WSL 2.
- Workshop mission: `3725687524`.
- Evidence directory: `/opt/horus/server-linux-workshop/runtime-evidence/linux-final-rc-soak/20260831-035606`.
- Started: `2026-08-31T03:56:06Z`; completed: `2026-08-31T07:56:15Z`.
- Requested duration: 240 minutes; helper exit code: 0.
- Ready marker observed: PASS; runtime failure: empty.
- Readiness limit: 300 seconds; Unity log safety limit: 16 MiB.
- Metrics: 1,440 samples spanning 14,397 seconds.
- RSS: 414,688 KiB first, 421,980 KiB last, 414,688 KiB minimum, 424,120 KiB maximum. RSS rose during warm-up, plateaued, and later dropped before the final value; no monotonic growth was observed.
- Fatal runtime scan: zero findings; both Horus-load and server-ready patterns were present.
- BepInEx errors and warnings: zero.
- The official Steam runtime logged one initial SDR configuration HTTP 504 and recovered before readiness; no Horus failure accompanied it.
- Evidence SHA256SUMS contains the exact Server and Shared hashes listed above.
- The Linux server process was absent after the helper completed.

## Windows exact-DLL four-hour soak

- Official Windows depot with BepInEx 5.
- Workshop mission: `3725687524`.
- Evidence directory: `HorusDedicatedTest/server-win-workshop/runtime-evidence/windows-fixed-rc-soak/20260831-043451`.
- Started: `2026-08-31T04:34:51Z`; completed: `2026-08-31T08:34:54Z`.
- Requested duration: 240 minutes; sequence result: PASS.
- Ready marker observed: PASS; runtime failure: empty.
- Readiness limit: 300 seconds; Unity log safety limit: 16 MiB.
- Metrics: 1,438 samples spanning 14,392.15 seconds.
- Working set: 388.51 MiB first, 126.11 MiB last, 124.39 MiB minimum, 389.54 MiB maximum.
- Private memory: 518.16 MiB first, 517.47 MiB last, 516.46 MiB minimum, 518.48 MiB maximum.
- Fatal runtime scan: zero findings; both Horus-load and server-ready patterns were present.
- The official runtime logged one recovered SDR configuration HTTP 504, one 2.1 ms Steam networking-lock warning, and one 1.065-second update-loop warning; no Horus failure accompanied them.
- Evidence hashes contain the exact Server and Shared hashes listed above.
- The Windows server and sequence processes were absent after completion.

## Baseline soak context

These baselines used earlier RC assemblies and therefore are stability context only. They are not exact-artifact certification and are not counted as the final soaks.

- Windows baseline evidence: `HorusDedicatedTest/server-win/runtime-evidence/windows/20260831-003236`.
  - 1,438 samples spanning 14,397.36 seconds.
  - Working set: 390.03 MiB first, 48.71 MiB last, 391.71 MiB maximum.
  - Private memory: 520.38 MiB first, 521.41 MiB last, 521.60 MiB maximum.
  - Runtime analysis: PASS with zero fatal findings and no missing required patterns.
  - The official server emitted three `PerformanceTracker` update-loop warnings; no Horus failure accompanied them.
- Linux baseline evidence: `/opt/horus/server-linux/runtime-evidence/linux/20260831-004610`.
  - 1,339 samples spanning 14,399 seconds.
  - RSS: 403,520 KiB first, 412,204 KiB last, 412,204 KiB maximum.
  - Fatal-pattern scan: zero findings; Horus-load and server-ready patterns were present.
  - No `PerformanceTracker` warning was found.

## Runner safeguard

A controlled concurrent-Windows-instance failure was used to validate the new evidence safeguard. The runner stopped its own child after the Unity log exceeded a temporary 1 MiB test limit, retained the sanitized configuration, metrics, log, four binary/config hashes, `runtime-status.json`, and `analysis.json`, surfaced the log-limit cause, and left the baseline server process alive. This is a runner PASS, not a Horus runtime PASS and not evidence that concurrent Windows server instances are supported.

For certification, only one official Windows dedicated-server process is used in the Windows/Steam session. After the baseline exited, the exact stable-RC Windows smoke ran alone and passed before the final soak started.

## Active and pending evidence

- Three earlier exact-DLL Linux soaks were stopped when subsequent security reviews changed `Horus.Server.dll`; their partial evidence remains preserved and is not a PASS result.
- Linux final exact-DLL four-hour idle soak: PASS. This validates headless stability only, not connected gameplay or command parity.
- Windows final exact-DLL four-hour idle soak: PASS. This validates headless stability only, not connected gameplay or command parity.
- Exact-DLL smoke tests on Windows and Linux are PASS. Both baseline soaks completed without fatal findings but remain context-only because they used earlier assemblies.
- Connected clients, full command parity, replication, reconnect/resynchronization, mission rotations, abuse traffic, and visual GM evidence remain `PENDING`.
- Two simultaneous GMs and two concurrent Steam identities remain `PENDING - second legitimate Steam identity unavailable`.
