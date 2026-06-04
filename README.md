# Horus Mod Starter

A Game Master/Free Camera utility mod for Nuclear Option (formerly known as Zeus Mod). Horus allows the host or local player to spawn aircraft, vehicles, ships, and buildings in real time.

## Features
- Toggle Horus Mode (Free Camera + UI) with **F9**.
- Toggle UI visibility with **F10**.
- Select Faction and Unit Category.
- Spawn selected units at crosshair with **Left Click**.
- **Safe middle-click delete**: only removes units spawned by Horus (terrain, roads, buildings and original map objects are protected).
- **Ghost preview**: see a semi-transparent preview of the selected unit at the placement position before spawning.
- **Object yaw rotation before spawning** (slider, presets, and `Alt + Scroll`).
- **Altitude control** (slider, presets, custom input, and `Ctrl + Scroll`).
- **Larger steps** with `Shift + Scroll`.
- **Grid snapping** (1m / 5m / 10m / 25m / 50m / 100m, plus custom) for aligned layouts.
- **Rotation snapping** (1° / 5° / 15° / 45° / 90°).
- **Snap to ground** for ground units and experimental **align to surface**.
- **Map Spawn Mode**: open the map and click anywhere to place units at that location.
- Reset buttons for altitude and yaw.
- Collapsible UI sections with live status labels.
- **Host-authoritative multiplayer**: clients are blocked by default and the UI shows the current permission status.

## Installation
> [!IMPORTANT]
> If you are updating from an older version, please **DELETE `NuclearOptionZeusMod.dll`** from your plugins folder first to prevent conflicts.

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) for Nuclear Option.
2. Download the latest release `.zip` from the GitHub Releases page.
3. Extract the contents into your `Nuclear Option/` directory. The structure should be:
   `Nuclear Option/BepInEx/plugins/HorusMod/HorusMod.dll`

## Controls
- **F9** (configurable): Toggle Horus Mode
- **F10** (configurable): Hide/Show UI
- **Left Click**: Spawn selected unit (or place at map cursor in Map Spawn Mode)
- **Middle Click**: Delete a unit spawned by Horus (safe delete — map/environment is protected)
- **Right Click (Hold)**: Rotate Camera
- **W/A/S/D/Q/E**: Move Camera
- **Left Shift**: Boost Camera Speed
- **Ctrl + Scroll**: Adjust spawn altitude
- **Alt + Scroll**: Rotate spawn yaw
- **Shift + Scroll**: Larger altitude/rotation increments
- **M**: Open/close the map (use with Map Spawn Mode)

### Placement Tools
Open the **Placement Tools** section in the Horus window to control how units are placed:
- **Ghost Preview**: shows a semi-transparent preview of the selected unit before you spawn it. It follows the cursor and respects altitude, yaw and snapping. It is local-only and never spawns or networks anything.
- **Snap to Ground**: places ground units (vehicles, buildings, scenery) on the terrain.
- **Align to Surface Normal** (experimental): tilts ground units to match the terrain slope.
- **Grid Snap**: aligns the placement position to a configurable spacing (1–100m or custom) for tidy rows and bases.
- **Rotation Snap**: snaps yaw to fixed increments (1° / 5° / 15° / 45° / 90°).

### Safe Delete
Middle-click delete only removes **units that Horus spawned this session**. Terrain, roads, static map geometry, the map UI, and original mission units are never deletable. If you middle-click something protected, Horus does nothing and logs the reason to `LogOutput.log`.

Advanced: set `Safety / AllowDeletingNonHorusUnits = true` in the BepInEx config to also delete other real gameplay units. Even then, map/environment objects and original map-baked units remain protected.

### Multiplayer Permissions
Horus is host-authoritative. The UI shows your current mode:
- **Single Player** — full access.
- **Multiplayer Host** — full access.
- **Multiplayer Client - No Permission** — spawning and deleting are blocked by default.

Client request/whitelist support is reserved for a future update (config flags `AllowClientHorusRequests` and `EnableExperimentalWhitelist`, both off by default).

### Map Spawn Mode
1. Enable **Map Spawn: ON** in the Horus window (this also opens the map).
2. Left-click anywhere on the map to spawn the selected unit at that location.
3. Press **M** to toggle the map, or turn Map Spawn off to close it.
4. Ground units (vehicles, buildings, scenery) snap to terrain by default; toggle this off to use the exact altitude.

## Troubleshooting
- **"F9 does nothing"**: Verify BepInEx 5 is installed correctly and `LogOutput.log` shows Horus Mod Starter loaded. Check if another mod uses F9. You can change the hotkeys using BepInEx Configuration Manager.
- **"Cannot spawn units"**: In multiplayer, only the Host can spawn or delete units.
- **"Ctrl/Alt + Scroll does nothing"**: The shortcuts read the game's mouse-wheel (Rewired "Zoom View") input. If the direction feels reversed, set `Placement / InvertScrollDirection = true` in the BepInEx config.
- **"Middle click won't delete a unit"**: By design, only units spawned by Horus are deletable. See **Safe Delete** above.

## Compatibility
Compatible with NOMM/NOMNOM mod managers.

## Credits
Original Zeus Mod concept. Re-branded and updated for GitHub release.
