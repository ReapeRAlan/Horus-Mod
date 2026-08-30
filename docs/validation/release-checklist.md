# v2.0.0-rc.1 Release Checklist

Record only observed results. Use `PASS`, `FAIL`, `PENDING`, or `BLOCKED`; never infer a runtime PASS from compilation.

## Automated gate

- [ ] `./build/validate-release.ps1` returns `HORUS RELEASE VALIDATION: PASS`.
- [ ] Shared, Client, and Server builds have zero warnings and zero errors.
- [ ] All pure tests pass.
- [ ] Server assembly dependency audit passes.
- [ ] UTF-8, English, JSON, PowerShell, Markdown-link, and version checks pass.
- [ ] Two packaging runs produce identical hashes.
- [ ] ZIP contents and embedded `SHA256SUMS` validate.
- [ ] No proprietary or generated DLL/ZIP is tracked by Git.

## Windows official server

- [ ] Clean headless start and restart.
- [ ] Steam login and Mirage handler registration.
- [ ] BuiltIn mission and two rotations.
- [ ] Workshop mission download and load.
- [ ] Client without Horus joins normally.
- [ ] Same authenticated account is denied outside the allowlist and authorized inside it.
- [ ] Protocol mismatch fails closed.
- [ ] Reconnect, mission change, stale revision, and snapshot resync.
- [ ] Full mutation matrix and normal-client replication.
- [ ] Abuse limits and four-hour soak.
- [ ] Logs, audit, metrics, configuration hash, package hash, and screenshots retained.

## Linux official server

- [ ] WSL 2 Ubuntu 24.04 reports a native Linux kernel and x86_64 architecture.
- [ ] Clean headless start and restart through BepInEx.
- [ ] Steam login and Mirage handler registration.
- [ ] BuiltIn mission, Workshop mission, and two rotations.
- [ ] Authorization, reconnect, resync, full mutation matrix, and native replication.
- [ ] Abuse limits and four-hour soak.
- [ ] Logs, audit, metrics, configuration hash, package hash, and screenshots retained.

## Known infrastructure limitation

- [ ] Two simultaneous GMs and two concurrent Steam identities. If unavailable, keep `PENDING – second legitimate Steam identity unavailable` in the matrix, changelog, and release notes.

## GitHub publication

- [ ] Authenticate `gh` and fetch/prune branches and tags.
- [ ] Confirm the exact remote state and inspect open pull requests.
- [ ] Push `release/v2.0.0-rc.1`; never force-push `main` or an existing tag.
- [ ] Merge only after required CI succeeds.
- [ ] Create an annotated tag on the exact merge commit.
- [ ] Create a draft prerelease with `--verify-tag --prerelease --latest=false --fail-on-no-commits`.
- [ ] Upload three ZIPs, `SHA256SUMS.txt`, `release-manifest.json`, and English notes.
- [ ] Verify every remote asset digest before publishing the draft.
