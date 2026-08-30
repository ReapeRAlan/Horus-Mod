# Security Policy

## Supported versions

`v1.4.3` remains the latest stable release. `v2.0.0-rc.1` is a prerelease dedicated-server candidate and must not be represented as production-certified.

Security fixes are applied to the newest supported line. A critical issue in an older release may be fixed by publishing a new patch rather than replacing an existing tag or asset.

## Dedicated-server defaults

- Horus dedicated control is disabled by default.
- An empty administrator allowlist denies every mutation.
- Only an authenticated individual SteamID64 supplied by the game's authenticated connection can receive GM authority.
- Display names, factions, passwords, claimed ownership, host flags, and UDP-only identity never grant Horus access.
- Deletion is limited to Horus-created entities unless the server operator deliberately changes the policy.
- The official TCP administration endpoint is not a Horus gameplay-control transport.

Never commit a real administrator allowlist, Steam credential, server password, private mission, audit log, or proprietary Nuclear Option assembly.

## Reporting a vulnerability

Use GitHub private vulnerability reporting from the repository Security tab when it is available. If it is unavailable, open a minimal issue asking the maintainer for a private contact channel; do not include exploit instructions, credentials, SteamIDs, private server addresses, or sensitive logs in the public issue.

Include the affected Horus version, Nuclear Option build, operating system, reproduction conditions, expected impact, and the smallest sanitized log excerpt needed to confirm the issue.

## Release integrity

Official release assets are accompanied by `SHA256SUMS.txt` and `release-manifest.json`. Existing release tags and assets are never replaced. A corrected candidate receives a new tag such as `v2.0.0-rc.2`.
