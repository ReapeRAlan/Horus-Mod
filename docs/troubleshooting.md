# Troubleshooting

## Horus does not load

1. Check `BepInEx/LogOutput.log` before changing configuration.
2. Verify that BepInEx 5 is installed for the correct operating system and architecture.
3. Remove every legacy or duplicate Horus DLL.
4. Verify the package layout under `BepInEx/plugins/Horus/`.
5. On a dedicated server, set `[Chainloader] HideManagerGameObject = true` in `BepInEx/config/BepInEx.cfg`.

## Dedicated GM is view-only

- Confirm `Horus.Server.cfg` has `Enabled = true` on the server.
- Confirm the allowlist contains one uncommented individual SteamID64 per line.
- Confirm the client joined through an authenticated Steam connection.
- Confirm client and server both report `2.0.0-rc.1` and protocol version 2.
- Reconnect after changing the allowlist or mission.

Names, factions, passwords, local Windows users, and UDP endpoints cannot grant Horus authority.

## Protocol mismatch or resync request

Install matching GM and Dedicated packages. A mission change rotates the Horus session ID. Stale commands are rejected by design; reconnect or request a fresh snapshot instead of retrying the old command.

## Normal clients cannot see a spawned asset

Native Nuclear Option content should replicate through the game's normal networking. Third-party aircraft, ships, weapons, or scenery must be installed with compatible versions on every machine that needs to render or simulate them. `AllowIncompatibleContent` cannot make a missing prefab network-safe.

## Server starts but does not rotate missions

Keep at least one authenticated player connected when the active configuration requires players to load a mission. Verify that `MissionDirectory` is absolute and that Workshop entries use a current published file ID. Consult the official server log for mission-download or load errors.

## Audit or persistence errors

Verify write permissions for `BepInEx/config/HorusMod/`. Audit records are daily UTF-8 JSONL files. Do not edit active persistence files while the server is running. Restore the most recent known-good backup if a manual edit produced invalid JSON.

## Reporting a problem

Include the Horus version, game/server build, operating system, selected package, sanitized configuration, exact reproduction steps, and relevant log excerpt. Remove SteamIDs, passwords, IP addresses, tokens, and private mission data.
