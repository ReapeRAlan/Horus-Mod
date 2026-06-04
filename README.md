# Horus Mod Starter

A Game Master/Free Camera utility mod for Nuclear Option (formerly known as Zeus Mod). Horus allows the host or local player to spawn aircraft, vehicles, ships, and buildings in real time.

## Features
- Toggle Horus Mode (Free Camera + UI) with **F9**.
- Toggle UI visibility with **F10**.
- Select Faction and Unit Category.
- Spawn selected units at crosshair with **Left Click**.
- Delete units with **Middle Click**.
- **Object yaw rotation before spawning** (slider, presets, and `Alt + Scroll`).
- **Altitude control** (slider, presets, custom input, and `Ctrl + Scroll`).
- **Map Spawn Mode**: open the map and click anywhere to place units at that location.
- Reset buttons for altitude and rotation.
- Host-authoritative: Remote clients cannot spawn units unless running a server.

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
- **Middle Click**: Delete unit under crosshair
- **Right Click (Hold)**: Rotate Camera
- **W/A/S/D/Q/E**: Move Camera
- **Left Shift**: Boost Camera Speed
- **Ctrl + Scroll**: Adjust spawn altitude
- **Alt + Scroll**: Rotate spawn yaw
- **Shift + Scroll**: Larger altitude/rotation increments
- **M**: Open/close the map (use with Map Spawn Mode)

### Map Spawn Mode
1. Enable **Map Spawn: ON** in the Horus window (this also opens the map).
2. Left-click anywhere on the map to spawn the selected unit at that location.
3. Press **M** to toggle the map, or turn Map Spawn off to close it.
4. Ground units (vehicles, buildings, scenery) snap to terrain by default; toggle this off to use the exact altitude.

## Troubleshooting
- **"F9 does nothing"**: Verify BepInEx 5 is installed correctly and `LogOutput.log` shows Horus Mod Starter loaded. Check if another mod uses F9. You can change the hotkeys using BepInEx Configuration Manager.
- **"Cannot spawn units"**: In multiplayer, only the Host can spawn or delete units.

## Compatibility
Compatible with NOMM/NOMNOM mod managers.

## Credits
Original Zeus Mod concept. Re-branded and updated for GitHub release.
