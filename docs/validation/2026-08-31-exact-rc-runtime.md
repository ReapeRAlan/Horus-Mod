# Exact RC Runtime and Runner-Safeguard Evidence

## Scope

This report records observed results for the commit-independent `v2.0.0-rc.1` release assemblies built from source commit `66aee1956f813fd8873b6ab43eafaacfa1315320`. It does not convert connected-client, mutation, mission-rotation, abuse, or multi-account scenarios into PASS results.

## Exact artifacts

- Dedicated package SHA-256: `1081a8637a88bfdbf92fd283797df7e6fec02e78f84f73270887f691d53c20e5`.
- `Horus.Server.dll` SHA-256: `a181503a4a390bf4d74fe6dca7e72422a6eecf46d5950b79919351fb7b9f6884`.
- `Horus.Shared.dll` SHA-256: `08825ce99b62936d324f713d806b6e0e19a544bc711c1962d378fd32f68b6db0`.
- `Horus.Client.dll` SHA-256: `4e8f8d9541787caee90ebe788f834e4f0bf2c6f7129d6afbb78f0ff56f99c7c8`.
- Clean-tree release gate: PASS with 134 tests, zero build errors, zero build warnings, headless dependency audit, two identical packaging runs, and valid embedded checksums.
- GitHub PR #3 checks on this source commit: `public-validation` PASS and GitGuardian PASS.

## Cross-commit reproducibility

Before commit `66aee1956f813fd8873b6ab43eafaacfa1315320` was created, release builds at parent commit `d24d276a06dfb6710c908f0d482671c9a4ad7a1d` produced the same Shared, Server, and Client hashes listed above. Rebuilding after the real Git commit changed preserved all three hashes. The release gate also rebuilds with two distinct forced `SourceRevisionId` values and rejects any assembly hash difference.

Release builds omit PDB generation so SourceLink cannot couple a distributed DLL to a Git SHA through its debug-directory identifier. Debug builds retain their normal development behavior. The exact source commit remains recorded in `release-manifest.json`.

## Linux exact-DLL smoke

- Official Linux depot with BepInEx 5 under Ubuntu 24.04 WSL 2.
- Workshop mission: `3725687524`.
- Evidence directory: `/opt/horus/server-linux-workshop/runtime-evidence/linux-stable-rc-smoke/20260831-024739`.
- Requested duration: two minutes.
- Ready marker observed: PASS.
- Runtime failure: empty.
- Readiness limit: 300 seconds.
- Unity log safety limit: 16 MiB.
- Final sampled RSS: 391,500 KiB.
- Fatal runtime scan: no unhandled exception, null reference, stack overflow, out-of-memory event, or Horus load failure.
- Evidence SHA256SUMS contains the exact Server and Shared hashes listed above.

## Runner safeguard

A controlled concurrent-Windows-instance failure was used to validate the new evidence safeguard. The runner stopped its own child after the Unity log exceeded a temporary 1 MiB test limit, retained the sanitized configuration, metrics, log, four binary/config hashes, `runtime-status.json`, and `analysis.json`, surfaced the log-limit cause, and left the baseline server process alive. This is a runner PASS, not a Horus runtime PASS and not evidence that concurrent Windows server instances are supported.

For certification, only one official Windows dedicated-server process is used in the Windows/Steam session. The exact stable-RC Windows smoke therefore remains pending until the baseline soak exits.

## Active and pending evidence

- Linux exact-DLL four-hour soak started in `/opt/horus/server-linux-workshop/runtime-evidence/linux-stable-rc-soak/20260831-025012`; status remains `PENDING` until the controlled helper exits and its evidence is analyzed.
- Windows baseline four-hour soak remains `PENDING` until its helper exits and evidence is analyzed.
- Windows exact-DLL smoke and four-hour soak remain `PENDING` until they run as the only Windows dedicated-server instance.
- Connected clients, full command parity, replication, reconnect/resynchronization, mission rotations, abuse traffic, and visual GM evidence remain `PENDING`.
- Two simultaneous GMs and two concurrent Steam identities remain `PENDING - second legitimate Steam identity unavailable`.
