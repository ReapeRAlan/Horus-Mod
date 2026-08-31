# Exact RC Runtime and Runner-Safeguard Evidence

## Scope

This report records observed results for the commit-independent `v2.0.0-rc.1` release assemblies after protocol-negotiation and rejection-audit amplification hardening in source commit `8249471a530b56b8dd03bfd14c0683e3aae0b607`. It does not convert connected-client, mutation, mission-rotation, abuse, or multi-account scenarios into PASS results.

## Exact artifacts

- Dedicated package SHA-256 at the hardening source commit: `1c6e1fa13b0afe44eac3ee83e03ef82eeb9ce47619f2479a1219ad769c152183`.
- `Horus.Server.dll` SHA-256: `8db33fac0ef4c0b928fbdfb4e73742437fb9b3563c08ccbf1e0c43b646c9d1ad`.
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
- Evidence directory: `/opt/horus/server-linux-workshop/runtime-evidence/linux-audit-hardened-rc-smoke/20260831-034037`.
- Requested duration: two minutes.
- Ready marker observed: PASS.
- Runtime failure: empty.
- Readiness limit: 300 seconds.
- Unity log safety limit: 16 MiB.
- Final sampled RSS: 425,084 KiB.
- Fatal runtime scan: no unhandled exception, null reference, stack overflow, out-of-memory event, or Horus load failure.
- Evidence SHA256SUMS contains the exact Server and Shared hashes listed above.

## Runner safeguard

A controlled concurrent-Windows-instance failure was used to validate the new evidence safeguard. The runner stopped its own child after the Unity log exceeded a temporary 1 MiB test limit, retained the sanitized configuration, metrics, log, four binary/config hashes, `runtime-status.json`, and `analysis.json`, surfaced the log-limit cause, and left the baseline server process alive. This is a runner PASS, not a Horus runtime PASS and not evidence that concurrent Windows server instances are supported.

For certification, only one official Windows dedicated-server process is used in the Windows/Steam session. The exact stable-RC Windows smoke therefore remains pending until the baseline soak exits.

## Active and pending evidence

- Two earlier exact-DLL Linux soaks were stopped when subsequent security reviews changed `Horus.Server.dll`; their partial evidence remains preserved and is not a PASS result.
- Linux audit-hardened exact-DLL four-hour soak started in `/opt/horus/server-linux-workshop/runtime-evidence/linux-audit-hardened-rc-soak/20260831-034328`; status remains `PENDING` until the controlled helper exits and its evidence is analyzed.
- Windows baseline four-hour soak remains `PENDING` until its helper exits and evidence is analyzed.
- Windows exact-DLL smoke and four-hour soak remain `PENDING` until they run as the only Windows dedicated-server instance.
- Connected clients, full command parity, replication, reconnect/resynchronization, mission rotations, abuse traffic, and visual GM evidence remain `PENDING`.
- Two simultaneous GMs and two concurrent Steam identities remain `PENDING - second legitimate Steam identity unavailable`.
