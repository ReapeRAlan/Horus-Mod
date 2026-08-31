# v2.0.0-rc.1 Release Checklist

Record only observed results. Use `PASS`, `FAIL`, `PENDING`, or `BLOCKED`; never infer a runtime PASS from compilation.

## Automated gate

- [x] `./build/validate-release.ps1` returns `HORUS RELEASE VALIDATION: PASS`.
- [x] Shared, Client, and Server builds have zero warnings and zero errors.
- [x] All pure tests pass.
- [x] Server assembly dependency audit passes.
- [x] UTF-8, English, JSON, PowerShell, Bash, Markdown-link, and version checks pass.
- [x] Two packaging runs produce identical hashes.
- [x] Release DLL hashes remain identical across a real commit change and forced revision identifiers.
- [x] Runtime helpers enforce readiness and log-size safeguards while retaining failure evidence.
- [x] ZIP contents and embedded `SHA256SUMS` validate.
- [x] The complete English user manual is included in GM, Dedicated, and Full packages.
- [x] No proprietary or generated DLL/ZIP is tracked by Git.

## Windows official server

- [x] Clean headless start and restart.
- [x] Steam login and Mirage handler registration.
- [x] BuiltIn mission selection.
- [ ] Two rotations.
- [x] Public Workshop mission download, JSON resolution, `AfterLoad`, selection, and readiness.
- [ ] Workshop gameplay with a connected client.
- [ ] Client without Horus joins normally.
- [ ] Same authenticated account is denied outside the allowlist and authorized inside it.
- [ ] Protocol mismatch fails closed.
- [ ] Reconnect, mission change, stale revision, and snapshot resync.
- [ ] Full mutation matrix and normal-client replication.
- [x] Exact frozen-DLL four-hour idle soak with clean shutdown and zero fatal findings.
- [x] Runtime logs, metrics, sanitized configuration, and binary/configuration hashes retained.
- [ ] Connected abuse limits.
- [ ] Command audit evidence, final package hash, and GM screenshots retained.

## Linux official server

- [x] WSL 2 Ubuntu 24.04 reports a native Linux kernel and x86_64 architecture.
- [x] Clean headless start and restart through BepInEx.
- [x] Steam login and Mirage handler registration.
- [x] BuiltIn mission selection.
- [x] Public Workshop mission download, JSON resolution, `AfterLoad`, selection, and readiness.
- [ ] Workshop gameplay with a connected client and two rotations.
- [ ] Authorization, reconnect, resync, full mutation matrix, and native replication.
- [x] Exact frozen-DLL four-hour idle soak with clean shutdown and zero fatal findings.
- [x] Runtime logs, metrics, configuration hash, and binary hashes retained.
- [ ] Connected abuse limits.
- [ ] Command audit evidence, final package hash, and GM screenshots retained.

## Known infrastructure limitation

- [ ] Two simultaneous GMs and two concurrent Steam identities. If unavailable, keep `PENDING – second legitimate Steam identity unavailable` in the matrix, changelog, and release notes.

## GitHub publication

- [x] Authenticate `gh` and fetch/prune branches and tags.
- [x] Confirm the exact remote state and inspect open pull requests.
- [x] Push `release/v2.0.0-rc.1`; never force-push `main` or an existing tag.
- [x] Activate rulesets that require PR/CI for `main` and prevent update/deletion of `v*` tags.
- [ ] Merge only after required CI succeeds.
- [ ] Create an annotated tag on the exact merge commit.
- [ ] Run `./build/create-prerelease-draft.ps1` and confirm its non-mutating preflight passes.
- [ ] Create a draft prerelease with `--verify-tag --prerelease --latest=false --fail-on-no-commits`.
- [ ] Re-run the preflight with `-CreateDraft` only after explicit publication approval; the script must verify all remote asset digests while the release remains a draft.
- [ ] Upload three ZIPs, `SHA256SUMS.txt`, `release-manifest.json`, and English notes.
- [ ] Verify every remote asset digest before publishing the draft.
- [ ] Confirm the public title and first paragraph say TEST, experimental, and not production-certified.
- [ ] Publish with prerelease enabled and Latest disabled; verify `v1.4.3` remains Latest/stable.
- [ ] Confirm immutable-release protection after publication and never replace the tag or assets.
