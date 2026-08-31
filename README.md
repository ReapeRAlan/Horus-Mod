# Horus Mod Starter

A Game Master/Free Camera utility mod for Nuclear Option (formerly known as Zeus Mod). Horus allows the host or local player to spawn aircraft, vehicles, ships, and buildings in real time.

> [!NOTE]
> v1.4.3 is the latest published release. This branch prepares **v2.0.0-rc.1**, including dedicated-server support, but the RC is not production-certified until the required Windows and Linux runtime matrix passes. Lookup-only content, live ordnance, mod-provided definitions, naval resupply, and corrected AI bombing remain experimental where noted.

## Features
- Toggle Horus Mode (Free Camera + UI) with **F9**.
- Toggle UI visibility with **F10**.
- Search and filter the native unit catalog by category, role, favorites, and recent use.
- Spawn selected units at the mouse cursor with **Left Click**.
- Select units with **Left Click**, add with **Shift + Left Click**, or drag a selection box.
- Issue formation-aware move orders with a quick **Right Click**; drag right mouse at least 10 pixels to rotate the camera. A stationary hold remains a click.
- Open a contextual unit menu with **Right Click on a unit** (or **Alt + Right Click** anywhere) for orders, loadouts, skins/liveries, skill, duplication, focus, and deletion.
- Customize aircraft for the next spawn or after selection with independent state, native standard presets, named Horus presets, session loadouts, and per-hardpoint weapon choices.
- Save aircraft-specific Horus loadout presets in `BepInEx/config/HorusMod/aircraft_loadouts.json` using stable definition keys.
- Browse aircraft, vehicles, ships, buildings, scenery, containers/other units, and experimental live ordnance; refresh the catalog when another mod registers content late.
- Inspect logistics capabilities such as ammunition rearming, naval resupply, refueling, storage, and warhead storage without treating decorative props as functional supply objects.
- Move ground units, ships, and host-controlled AI aircraft in formations; aircraft orders use their native server autopilot and never override a player-controlled aircraft.
- Issue Attack Target, Attack-Move, multi-waypoint Patrol, and Guard/Escort orders to compatible AI. Target orders only use contacts already known to the unit's native faction HQ.
- Set per-unit **Weapons Free** or **Hold Fire** rules from the contextual menu. Hold Fire does not disable evasion or countermeasures and never affects player-controlled aircraft.
- Correct conventional AI bomb release for moving targets and weapon rail/ejection delay with skill-dependent dispersion; set `AI.ImproveAIBombingAccuracy=false` to restore fully native release behavior.
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
- **Single-player, local-host, and dedicated authority**: all mutating editor actions use an authoritative gateway. Dedicated control authenticates a normal Steam player against a server-side SteamID64 allowlist and fails closed by default.
- **Camera/Control Restore**: saves and restores aircraft control, camera, cursor lock, and cursor visibility states. Focus loss and scene changes release Horus pointer capture safely.

## Installation
> [!IMPORTANT]
> If you are updating from an older version, please **DELETE `NuclearOptionZeusMod.dll`** from your plugins folder first to prevent conflicts.

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) for Nuclear Option.
2. Choose the package for the machine:
   - `Horus-GM-v2.0.0-rc.1.zip`: `Horus.Shared.dll` + `Horus.Client.dll` for a dedicated Game Master client.
   - `Horus-Dedicated-v2.0.0-rc.1.zip`: `Horus.Shared.dll` + `Horus.Server.dll` for the official headless server.
   - `Horus-Full-v2.0.0-rc.1.zip`: all three assemblies for single player or a local host.
3. Extract the package into the game or server root. Assemblies install under `BepInEx/plugins/Horus/`.
4. Dedicated operators must follow [the dedicated-server guide](docs/dedicated-server.md), set BepInEx `HideManagerGameObject = true`, enable `ModdedServer`, create the SteamID64 allowlist, and deliberately set `Enabled = true` in `Horus.Server.cfg`.

### Documentation

- [Dedicated server: Windows, Linux, and WSL](docs/dedicated-server.md)
- [Upgrade from v1.4.3](docs/upgrade-from-v1.4.3.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Security policy](SECURITY.md)
- [v2.0.0-rc.1 release notes](docs/releases/v2.0.0-rc.1.md)
- [Release validation checklist](docs/validation/release-checklist.md)

## Controls
- **F9** (configurable): Toggle Horus Mode
- **F10** (configurable): Hide/Show UI
- **Left Click**: Select a unit, or place the armed unit/group
- **Shift + Left Click**: Add to selection, or repeat placement without leaving Place mode
- **Left-drag**: Box-select units
- **Middle Click**: Delete a unit spawned by Horus (safe delete — map/environment is protected)
- **Right Click on terrain**: Move the current selection to the mouse cursor
- **Right Click on selected/neutral unit**: Open the visual unit command menu
- **Right Click on a known enemy while units are selected**: Attack Target
- **Right Click on a friendly while units are selected**: Guard/Escort
- **Alt + Right Click on terrain**: Open Move, Attack-Move, and Patrol options
- **Right Click (Drag)**: Rotate Camera (10px movement threshold; holding still remains a click)
- **Patrol planning**: LMB add point / Backspace undo / Enter confirm / Esc cancel
- **Manage > Tactical Orders**: Open the same visual menu or issue Hold, Clear Orders, Weapons Free, and Hold Fire directly
- **Group target orders**: After selecting several units, choose Move, Attack-Move, Patrol, Attack Target, or Guard/Escort from the unit menu or **Manage > Tactical Orders**, then LMB one destination/target to command the entire selection; RMB or Esc cancels.
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

### Aircraft Loadouts, Liveries, and Hardpoints

Aircraft customization has two separate targets: **Next Spawn** controls aircraft that have not been placed yet, while **Selected Aircraft** edits compatible aircraft already in the mission. Changing one target does not silently replace the other target's draft.

Available loadout sources are:

- **Default**: resolve Nuclear Option's native default into a fresh loadout before the aircraft is published on the network.
- **Standard Preset**: use a valid preset exposed by `AircraftParameters.StandardLoadouts`.
- **Current Session**: copy Nuclear Option's temporary `GameManager.aircraftCustomization` selection when available. This is session data, not a persistent game-wide custom-loadout library.
- **Horus Saved Preset**: load a named, aircraft-specific preset from `BepInEx/config/HorusMod/aircraft_loadouts.json`.
- **Copy Current Aircraft**: start from the loadout currently installed on the selected aircraft.
- **Custom Hardpoints**: choose a compatible weapon for each native `HardpointSet`.

A hardpoint entry can represent more than one visible pylon, including a symmetric pair. Manual and Horus-saved choices are limited to the mounts advertised by that `HardpointSet`; exclusions, HQ restrictions, event restrictions, and nuclear escalation rules are validated before application. Trusted native presets/session data may preserve hidden native mounts as read-only choices. Compatible mod aircraft can use the hardpoint editor even when they expose no standard presets.

Named Horus presets are unique per aircraft `jsonKey` and can be created, duplicated, renamed, deleted, and applied. Applying to several selected aircraft is allowed only when every aircraft uses the same definition; a mixed-model selection is rejected instead of applying a partial loadout.

Ground vehicles and ships have fixed weapon installations in the current game API. Horus can diagnose or use supported rearming behavior, but it does not present those units as having interchangeable aircraft-style loadouts.

### Expanded Catalog, Props, and Experimental Content

The catalog distinguishes an object's spawn kind, placement surface, registration state, and functional capabilities. It includes the native aircraft, vehicle, ship, building, scenery, missile, and `otherUnits` collections, plus requested lookup-only definitions. **Network Registered** means that the definition is present in the game's runtime `IndexLookup`; it does not guarantee that every remote client owns compatible third-party assets.

- Use **Refresh Catalog** after enabling or loading another content mod if its definitions do not appear immediately.
- Unnamed definitions are shown as `??? · jsonKey` and carry status badges such as `Unlabeled`, `Experimental`, `Disabled`, `Event`, `Modded`, or `Lookup Only` when detectable.
- `WeaponMount` definitions are choices for the hardpoint editor; they are not independent world props and are never offered as spawnable units.
- **Force incompatible content** is disabled by default. Enabling it allows a confirmed per-session attempt to spawn lookup-only definitions, but cannot make an unregistered prefab network-safe. Such objects may fail, throw game errors, or desynchronize multiplayer clients.

#### Logistics and Naval Resupply

Horus identifies supply objects by their actual prefab components rather than words such as "naval" or "ammo" in their names:

- `Rearmer` reports ammunition support, linked unit, range, capacity, and single-use behavior. The current 0.34 API accepts a generic `Unit`; older/modded variants with per-surface flags are honored when present.
- `Refueler` reports fuel support.
- `UnitStorage` reports unit storage/deployment and is not classified as ammunition rearming.
- `WarheadStorage` reports warhead stock and is not classified as ordinary naval ammunition.

Catalog filters include **Logistics**, **Ammo**, **Naval Resupply**, **Fuel**, and **Storage**. The detail panel reports `Can resupply ships: yes`, `no`, or `unknown`; `yes` means the prefab exposes an operational generic `Rearmer` (or an explicit naval flag on older/modded APIs), not that a live rearm cycle has already been proven. **Spawn Naval Resupply** is offered only for that component-level capability. With a ship selected, Horus inherits its non-neutral HQ/faction, places the supply inside the detected `Rearmer` range, and asks the ship to `RequestRearm()` after spawning the object. Neutral supply is rejected.

`NavalSupplyContainer1` and `NavalPallet1` are component-compatible validation candidates, not guaranteed end-to-end resupply objects. Their catalog result may be `yes` because the current game exposes a generic `Rearmer.ProcessRearmRequest(Unit)` path and their serialized configuration passes inspection, while actual ammunition recovery and any single-use consumption remain unverified until an in-game test completes without new errors in `LogOutput.log`.

#### Live Ordnance

Native `MissileDefinition` entries appear as **Live Ordnance**. They can be placed only as individual Sandbox spawns and are excluded from groups, repeat placement, RTS/factory queues, saved group presets, duplication, and undo/redo. This remains experimental, especially for lookup-only definitions and multiplayer.

Choose one explicit target mode in the Live Ordnance panel:

- **World Point**: the weapon spawns above the clicked point and travels straight down.
- **Track Selected**: first select exactly one active unit, choose a missile with a native seeker, then left-click the desired launch point. Horus aims with motion lead and the native seeker continues following the unit.
- **Impact Selected**: first select exactly one active unit, choose the target-relative height, then left-click anywhere to confirm. Horus spawns above the predicted unit position. Guided weapons may keep tracking; bombs and rockets without a seeker receive initial motion lead but remain ballistic.

Horus does not parent live weapons to units or force an explosion inside their collider. Native physics, collision, fuze/arming, network behavior, and damage remain in control of the game.

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
- **Rally Points**: Set a rally point using targeted aim. Produced ground, naval, and AI air units receive a real movement order after spawning.
- **Playable Factions**: Factories require a real faction with an active HQ. Neutral remains available for Sandbox unit placement but is rejected for factories with an explicit status instead of an index error.
- **Config Migration**: Compatible incomplete economy/factory JSON files are filled with current defaults and saved automatically. Invalid, oversized, non-finite, or unsupported files are rejected and replaced with bounded defaults.
- **Persistent Instances**: Local/listen-host factory presets and instances use `rts_factories.json` and `rts_factory_instances.json`. Dedicated presets and instances use the isolated `rts_factories_server_config.json` and `rts_factories_server.json` files in `BepInEx/config/HorusMod/`.
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
Horus is server-authoritative. Normal players do not need Horus installed when the Game Master uses native Nuclear Option content. Third-party content definitions still require matching asset mods on every machine that must simulate or render them.

If you do have Horus installed, the UI shows your current mode:
- **Single Player** — full access.
- **Multiplayer Host** — full access.
- **Client (View Only)** — spawning, deleting, and budget adjustments are blocked by default.

- **Dedicated Server GM** - full remote access only after authenticated SteamID64 allowlist approval.

The v2.0.0-rc.1 dedicated transport uses versioned Mirage messages, manually registered bounded serializers, per-SteamID request deduplication and rate limits, authoritative catalog/position/loadout/cost validation, validated revisioned snapshots, original-unit mutation policies, and JSONL audit logs. The allowlist is empty, any invalid allowlist entry rejects the complete file, and `Horus.Server.cfg` is disabled by default. Nuclear Option's loopback server-command TCP endpoint is not used as a gameplay-control channel. See [the dedicated-server guide](docs/dedicated-server.md) for installation, security, and the mandatory release-candidate test matrix.

### Map Spawn Mode
1. Enable **Map Spawn: ON** in the Horus window (this also opens the map).
2. Left-click anywhere on the map to spawn the selected unit at that location.
3. Press **M** to toggle the map, or turn Map Spawn off to close it.
4. Ground units (vehicles, buildings, scenery) snap to terrain by default; toggle this off to use the exact altitude.


## Troubleshooting
- **"F9 does nothing"**: Verify BepInEx 5 is installed correctly and `LogOutput.log` shows Horus Mod Starter loaded. Check if another mod uses F9. You can change the hotkeys using BepInEx Configuration Manager.
- **"Cannot spawn units"**: A local multiplayer client is view-only. On a dedicated server, the GM must complete the matching protocol handshake through an authenticated Steam connection and appear in the server-side SteamID64 allowlist.
- **"Ctrl/Alt + Scroll does nothing"**: The shortcuts read the game's mouse-wheel (Rewired "Zoom View") input. If the direction feels reversed, set `Placement / InvertScrollDirection = true` in the BepInEx config.
- **"Middle click won't delete a unit"**: By design, only units spawned by Horus are deletable. See **Safe Delete** above.
- **"My aircraft has no standard loadouts"**: Try **Custom Hardpoints**. Some aircraft, especially mod aircraft, expose compatible hardpoint sets but no `StandardLoadouts`; others have fixed or incomplete weapon data and cannot be edited safely.
- **"A mod aircraft or `???` is missing"**: Click **Refresh Catalog**. If it appears only as `Lookup Only`, spawning requires **Force incompatible content** and its per-session warning; this does not guarantee multiplayer compatibility.
- **"The naval supply object does not rearm my ship"**: Check `Can resupply ships`. `unknown` means the prefab has not been functionally verified, while `no` means no naval `Rearmer` capability was detected. Storage or decorative containers do not automatically supply ammunition.
- **"Can Horus control a dedicated server?"**: The v2.0.0-rc.1 packages implement it for validation. It is not a production-certified release until the documented Windows and Linux runtime matrix passes. Use the dedicated package on the server, the GM package on the allowlisted Steam player's client, and follow [the dedicated-server guide](docs/dedicated-server.md).

For additional diagnostics and safe rollback instructions, see [Troubleshooting](docs/troubleshooting.md).

## Validation from source

Public CI and contributors can run the portable checks without proprietary game assemblies:

```powershell
./build/validate-release.ps1 -PublicCi
```

Maintainers with legitimate local client and dedicated-server installations run the complete build, dependency audit, deterministic packaging, checksum, English/UTF-8, and repository gate:

```powershell
./build/validate-release.ps1
```

Runtime helpers and their evidence behavior are documented in [build/runtime/README.md](build/runtime/README.md). Compiled Nuclear Option, Unity, Steam, Mirage, Rewired, BepInEx, and other proprietary dependencies are never committed or uploaded as CI artifacts.

## Compatibility
Compatible with NOMM/NOMNOM mod managers.

## Credits
Original Zeus Mod concept. Re-branded and updated for GitHub release.
