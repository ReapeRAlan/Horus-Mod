# Upgrade from v1.4.3

Horus 2.0 uses separate client, server, and shared assemblies. Do not leave a legacy Horus DLL beside the new package.

## Before upgrading

1. Stop Nuclear Option and every dedicated-server process.
2. Back up `BepInEx/config/HorusMod/` if it contains custom groups, aircraft loadouts, economy settings, or factory presets.
3. Remove these legacy plugin files if present:
   - `BepInEx/plugins/NuclearOptionZeusMod.dll`
   - `BepInEx/plugins/HorusMod/HorusMod.dll`
   - any earlier standalone `Horus.Client.dll`, `Horus.Server.dll`, or `Horus.Shared.dll`
4. Do not delete your configuration backup.

## Choose one package

- `Horus-GM-v2.0.0-rc.1.zip`: visual GM client for a dedicated server.
- `Horus-Dedicated-v2.0.0-rc.1.zip`: headless dedicated server.
- `Horus-Full-v2.0.0-rc.1.zip`: single player or local multiplayer host.

Extract the selected archive into the game or server root. The assemblies must end up in `BepInEx/plugins/Horus/`.

## Dedicated-server migration

The dedicated package deliberately installs an empty administrator allowlist and a disabled `Horus.Server.cfg`. Add the real SteamID64 only to the local server copy, set `ModdedServer` to `true`, set BepInEx `HideManagerGameObject` to `true`, and then enable Horus deliberately. Follow the [dedicated-server guide](dedicated-server.md).

## Verify the upgrade

- Confirm that exactly the expected Horus assemblies are present for the selected package.
- Confirm the BepInEx log reports informational version `2.0.0-rc.1`.
- Confirm there is no duplicate-plugin warning.
- Confirm the displayed menus, logs, and configuration descriptions are English.
- Compare the downloaded ZIP hash with `SHA256SUMS.txt` from the GitHub release.

## Roll back

Stop the game/server, remove `BepInEx/plugins/Horus/`, restore the backed-up configuration if required, and reinstall the unchanged `v1.4.3` asset. Do not rename a 2.0 package to a 1.4.3 filename and do not mix assemblies from both lines.
