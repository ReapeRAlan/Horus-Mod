# Contributing to Horus

## Language and formatting

All user-visible UI, logs, errors, configuration descriptions, documentation, scripts, CI output, and release notes must use clear international English and valid UTF-8. Intentional non-English negative test vectors are allowed only inside tests.

The repository uses `.editorconfig` and `.gitattributes`. Do not commit generated `bin`, `obj`, `dist`, runtime evidence, game assemblies, BepInEx binaries, Steam credentials, or administrator allowlists.

## Portable validation

The public checks do not require Nuclear Option:

```powershell
./build/validate-release.ps1 -PublicCi
```

This builds `Horus.Shared`, runs the pure logic/protocol/security suite, validates English/UTF-8 text, parses JSON and PowerShell, checks documentation links, and rejects tracked binaries.

## Full local validation

The client and server projects reference assemblies from a legitimate local Nuclear Option installation. From the repository root:

```powershell
./build/validate-release.ps1
```

Use the path parameters documented by `Get-Help ./build/validate-release.ps1 -Detailed` when the client or dedicated server is installed elsewhere. Never copy those proprietary reference assemblies into this repository or a CI artifact.

## Pull requests

- Branch from the latest `main`.
- Keep protocol changes explicit and versioned.
- Add or update tests for every security or compatibility change.
- State which Windows/Linux runtime scenarios were actually executed.
- Mark unexecuted scenarios as pending; do not turn assumptions into PASS results.
- Do not move, recreate, or overwrite an existing version tag.

## Maintainer prerelease drafts

After the release PR is merged, rebuild the assets on the exact clean `main` commit, create and push the annotated tag, then run the non-mutating preflight:

```powershell
./build/create-prerelease-draft.ps1
```

The preflight refuses an unmerged branch, a non-annotated or mismatched tag, pending Windows/Linux runtime gates, divergent manifest/checksums, an existing release, or any state in which `v1.4.3` is no longer GitHub Latest. Only after explicit publication approval, re-run it with `-CreateDraft`. That switch creates and verifies an unpublished prerelease draft; it never publishes the release or replaces an existing tag or asset.
