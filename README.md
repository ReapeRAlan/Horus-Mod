# Horus Mod Starter

A Game Master/Free Camera utility mod for Nuclear Option (formerly known as Zeus Mod). Horus allows the host or local player to spawn aircraft, vehicles, ships, and buildings in real time.

## Features
- Toggle Horus Mode (Free Camera + UI) with **F9**.
- Toggle UI visibility with **F10**.
- Search and filter the native unit catalog by category, role, favorites, and recent use.
- Spawn selected units at the mouse cursor with **Left Click**.
- Select units with **Left Click**, add with **Shift + Left Click**, or drag a selection box.
- Issue formation-aware move orders with a quick **Right Click**; hold/drag right mouse to rotate the camera.
- Open a contextual unit menu with **Alt + Right Click** for orders, loadouts, liveries, skill, duplication, focus, and deletion.
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
- Resizable, persistent themed editor with Place, Manage, RTS, Settings, and optional Debug tabs.
- **Groups & Formations**: spawn groups of up to 20 units in Column, Line, Grid, Circle, or V formations with adjustable spacing and Group Ghost Previews.
- **Native faction groups**: uses each faction's real `GetConvoyGroups()` definitions, composition, and native cost.
- **Custom Group Editor**: build and save heterogeneous groups of mixed unit types, serialized to JSON configuration files.
- **Spawn Stationary**: option to set ground vehicles/ships to hold position on spawn (applies to both single and group spawns).
- **RTS / Budget Mode**: uses each unit's native `UnitDefinition.value`, with optional stable `jsonKey` overrides and a global multiplier in `BepInEx/config/HorusMod/rts_economy.json`.
- **RTS Factories & Production**: host-created visible factory buildings that generate income, loop production queues, use type-correct spawn rules, support rally points, and persist to JSON.
- **Host-authoritative multiplayer**: clients are blocked by default and the UI shows the current permission status. Normal players do not need the mod installed, and if they do, they are locked to view-only mode. All spawn, delete, and budget modifications are validated server-side.
- **Single-player / local-host authority**: all mutating editor actions are gated; dedicated/headless server control is not part of this release.
- **Camera/Control Restore**: saves and restores aircraft control and camera view states. Temporarily suspends flight controls during Horus Mode to avoid input fighting.

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
- **Left Click**: Select a unit, or place the armed unit/group
- **Shift + Left Click**: Add to selection, or repeat placement without leaving Place mode
- **Left-drag**: Box-select units
- **Middle Click**: Delete a unit spawned by Horus (safe delete — map/environment is protected)
- **Right Click**: Move the current selection at the mouse cursor
- **Alt + Right Click**: Open the context menu
- **Right Click (Hold/Drag)**: Rotate Camera (6px / 250ms click disambiguation)
- **F / H / Delete / Esc**: Focus / hold / delete / cancel
- **Ctrl+D / Ctrl+A / Ctrl+Z / Ctrl+Y**: Duplicate / select all Horus units / undo / redo
- **Ctrl+1–9 / 1–9**: Assign / recall control groups
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

### Groups & Formations
Open the **Groups & Formations** section in the Horus window to control group spawning:
- **Enable Group Spawning**: Spawns a group of units in formation instead of a single unit. Renders a multi-unit Ghost Preview showing exactly where each unit will spawn.
- **Spawn Ground Units Stationary**: Forces ground vehicles/ships to hold position on spawn rather than patrolling or driving off.
- **Formation**: Choose from Column, Line, Grid, Circle, or V Formation.
- **Unit Count**: 1 to 20 units.
- **Spacing**: 5m to 200m distance between units.
- **Custom Groups**:
  1. Go to **Groups & Formations** and select the **Custom Group** preset.
  2. Click **Add Selected Unit** to add the currently active unit type to the group list.
  3. Manage the list by clicking `X` next to any unit to remove it.
  4. Enter a name and click **Save Group** to serialize it to `BepInEx/config/HorusMod/groups/[GroupName].json`.
  5. Use `<` and `>` to cycle through saved groups, and click **Load Selected** to use them.

### Safe Delete
Middle-click delete only removes **units that Horus spawned this session**. Terrain, roads, static map geometry, the map UI, and original mission units are protected by default. If you middle-click something protected, Horus does nothing and logs the reason to `LogOutput.log`.

Advanced config:
- Set `Safety / AllowDeletingNonHorusUnits = true` in the BepInEx config to allow deleting non-Horus spawned gameplay units.
- Set `Safety / AllowDeletingOriginalMissionUnits = true` in the BepInEx config to also allow deleting original builtin map-baked mission units.
Even with these enabled, terrain, roads, static geometry, and map structures remain strictly protected from deletion.

### RTS / Commander Mode
You can toggle between **Sandbox Mode** (free spawns, default) and **RTS / Commander Mode** in the Horus window:
- **Sandbox Mode**: Spawning is free and unrestricted.
- **RTS / Commander Mode**: Spawning units/groups and factory production deduct costs from the active faction's budget (**Primeva** or **Boscali**).
  - **Manual Deployment**: Click "Arm Deployment" first, view the cost preview, then left-click in the world to spawn the unit. The budget is deducted only if placement succeeds.
  - **Budget & Caps**: Starting budgets, tick income, and unit caps are loaded from `BepInEx/config/HorusMod/rts_economy.json` (auto-created on startup).
  - **Unit Costs**: Native `UnitDefinition.value` multiplied by `unitCostMultiplier`; optional overrides use stable `jsonKey` values.
  
#### RTS Factories & Production
Factories, bases, or carriers automatically generate income and produce units over time in RTS Mode:
- **Factory Types**: 
  - `Economy`: Focuses on budget generation (e.g., +300/min).
  - `GroundProduction`: Produces ground vehicles in front of the factory exit/door area, with validated terrain height that ignores the factory roof/top.
  - `AirProduction`: Spawns aircraft at safe flying altitudes (e.g., every 120s).
  - `NavalProduction`: Spawns ships at ocean level with safe positioning and unique name fixes.
  - `DefenseProduction`: Spawns stationary defense units, buildings, and batteries in validated ground positions near the factory. Default queue includes `23mm AAA Emplacement`, `IRM-S1 Emplacement`, `AT-145 Emplacement`, `Guard Tower`, `Pillbox`, and `Radar Station`.
  - `MixedProduction`: Allows mixed queues and chooses the correct ground, air, naval, or defense spawn path per unit.
- **Visual Factory Buildings**: Virtual factories spawn visible buildings in the world. Defaults are `Storage Tank`, `Large Factory`, `Medium Aircraft Hangar`, and `Radar Station`. Older config names such as `Solar Array`, `Vehicle Factory`, `Hangar`, and `Warehouse` are resolved through aliases.
- **Queue Loop**: Add units or compatible buildings to a factory's queue; it loops through the production list automatically. If budget or unit cap is insufficient, production pauses and reports the reason.
- **Rally Points**: Set a rally point using targeted aim. Produced units spawn facing the rally direction automatically.
- **Persistent Instances**: Factory presets are saved in `BepInEx/config/HorusMod/rts_factories.json`; placed factory instances are saved in `BepInEx/config/HorusMod/rts_factory_instances.json`.
- **Economy Config**: Budgets, passive income, caps, and costs are saved in `BepInEx/config/HorusMod/rts_economy.json`. Global passive income is optional and lower than factory income in default configs.
- **How to Create Factories**:
  1. Under the **RTS Factories & Production** panel, select a preset (e.g. Ground Vehicle Factory).
  2. Click **Create Factory Here** to spawn a virtual factory with a visible building at the current Horus placement point.
  3. Or click **Create Factory From Aimed Unit** while aiming at an existing valid unit to attach a factory without spawning an extra visual building.
- **How to Manage Queue**:
  1. Select an active factory from the panel list.
  2. Select any unit in the unit browser list and click **Add Selected Unit To Production Queue** to append it.
  3. Select a queue entry and click **Remove Selected Queue Item**, or click **Clear Queue** to empty it.
- **Factory Controls**: Create Factory Here, Create Factory From Aimed Unit, Delete Selected Factory, Enable/Disable Factory, Start All Factories, Stop All Factories, Add/Remove/Clear Queue, Set/Clear Rally Point, Save/Load Factories, Reload Factory Config, and Reset Factory Presets To Defaults.
- **Group Spawning**: Group spawning remains disabled by default. RTS/Commander Mode is separate from Sandbox Mode.
- **Multiplayer Safety**: Creation, deletion, editing, queue updates, production ticks, budget changes, save/load, reload, and reset actions are restricted to Single Player or Multiplayer Host. Clients can view safe UI state and see Host-only indicators.

### Multiplayer Permissions
Horus is host-authoritative. Normal players do not need Horus Mod installed to play on a server hosted by a Game Master using Horus Mod. 

If you do have Horus installed, the UI shows your current mode:
- **Single Player** — full access.
- **Multiplayer Host** — full access.
- **Client (View Only)** — spawning, deleting, and budget adjustments are blocked by default.

Dedicated/headless command transport is intentionally out of scope; this release does not ship placeholder client/server command APIs.

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
