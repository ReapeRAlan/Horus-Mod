# Changelog

## [1.2.4] - 2026-07-30

### Added
- Server-side move, hold, clear, formation, undo/redo, and factory rally orders for AI aircraft through the native autopilot.
- Direct loadout, skin/livery, and pilot-skill editing for selected aircraft in the Manage tab.
- Factory runtime status text for invalid faction, anchor, queue, cap, budget, timer, spawn, and production results.

### Fixed
- Cursor ownership is now captured and restored exactly; focus loss, missed RMB release, scene unload, disabling, and destruction cannot leave Horus holding the cursor.
- Horus can always deactivate during mission unload after the game has already switched to the Menu state.
- RMB unit picking now preserves ship hits through the water plane and adds a screen-space unit fallback; context menus render above the editor window.
- Factory creation rejects Neutral or missing-HQ factions without indexing outside the faction list.
- Incomplete economy and factory JSON files are migrated, filled with working defaults, and persisted.
- Default production queues and the Mixed factory preset are restored when absent.
- Factory rally points now issue real ground, naval, or air movement orders.
- Ship destinations are normalized to sea level before native sea-lane pathfinding.
- Entering RTS mode while Neutral is selected now chooses the first playable faction.
- Aircraft loadout/skin controls auto-open when an aircraft is armed, and post-spawn editing is available without the context menu.
- The diagnostics instance count includes the persistent hidden Horus manager instead of reporting a false zero.

## [1.2.3] - 2026-07-30

### Added
- Themed, resizable and persistent tabbed editor with cached IMGUI styles.
- Searchable and virtualized native unit catalog with icons, metadata, role filters, favorites, and recents.
- Mouse selection, hover, marquee selection, RMB move/context actions, formation orders, overlays, and map synchronization.
- Loadout/livery/skill editing, real faction convoy groups, native editor altitude ranges, control groups, duplication, and undo/redo.
- Status toasts, compatibility audit, and expanded visual self-test panel.

### Fixed
- Ship-spacing solver convergence and per-frame ghost/log spam.
- Neutral faction spawning and aircraft loadout replication through `Networkloadout`.
- Window dragging interfering with sliders and RMB click-versus-camera-look ambiguity.
- RTS costs now use native `UnitDefinition.value` with stable `jsonKey` overrides.

### Removed
- Non-functional command/execution/network/server placeholders and their reserved configuration flags.

## [1.2.2] - 2026-07-20

### Added
- **Neutral / Unassigned Spawning**: Added support for spawning neutral/unassigned units safely without crashing the faction array.
- **Diagnostics Panel**: Expanded the debug panel to include lifecycle counters, exact action results, mode states, and explicit reference-clearing tools.
- **Server Architecture Foundation**: Stubs and command patterns (`src/Commands`, `src/Execution`, `src/Networking`) added to prepare for headless dedicated server support. (Note: Dedicated server support is being prepared architecturally, but is not officially supported yet).
- **Multiplayer Clarification**: Explicitly clarified in README and UI that normal players do not need the mod installed, and if they do, they are locked to view-only mode.

### Changed
- **Mission Lifecycle Stability**: Horus now safely re-initializes on mission load and purges all references on unload, allowing continuous gameplay across map reloads without restarting the game.
- **SyncWithFactionBudget (Experimental)**: Explicit logs and safe fallbacks added if in-game budget reflection fails.
- **CreditKillsToSpawner (Experimental)**: Verified and audited. Left disabled due to unsafe memory references, with clear logs indicating failure.

## [1.2.1] - 2026-06-10

### Added
- **UI Reset Hotkey**: Press `Ctrl + F10` to force UI visibility and reset window position.
- **Reset Window Button**: Added a button under "Placement Tools" to reset the UI position.
- **UI Scaling**: Added `UIScale` config option for high-resolution displays (e.g. set to `1.5` for 1440p).
- **RTS Unit Cap Configuration**: Added buttons in the RTS Commander menu to adjust the unit cap for each faction during a match.
- **RTS Budget Synchronization**: Added `SyncWithFactionBudget` configuration option. If enabled, the RTS Commander budget reflects the actual Nuclear Option faction budget.
- **Beginner-Friendly Labels**: Added helpful context labels underneath advanced tools explaining their use.
- **Diagnostics Panel**: Added a `Debug / Diagnostics` panel to view Horus status, spawn counts, factory counts, and to reload configs from disk in-game.

### Changed
- **Menu Reorganization**: Completely overhauled the `Horus Editor` UI to group similar features together chronologically (Status, Unit Selection, Placement Tools, Spawn Actions, Map Spawn, Groups & Formations, RTS, Safe Delete, Diagnostics).
- **Visual Hierarchy**: Added clear `══ SECTION ══` headers, consistent spacing, and status colors (e.g. green for Host, red for Client No Permission) to improve readability.
- Moved "Spawn Ground Units Stationary" toggle from Group Tools to Placement Tools.
- Refactored unit deletion logic into a separate `HorusDeleteManager` for better architecture.
- Refactored the UI rendering out of `HorusManager` into a partial `HorusManager_UI` script to keep the core script lightweight.

### Fixed
- Fixed an issue where the Horus UI window would stop drawing or disappear offscreen, requiring a game restart.

## [1.2.0] - 2026-06-08

### Added
- **RTS / Commander Mode Redesign**: Transformed RTS Mode from a basic paid-spawning system into a real-time commander strategy layer with per-faction budget tracking, unit caps, tick-based income, and deployment confirmation.
- **RTS Factory & Production System**: Added economy, ground, air, naval, and defense factories that generate income and automatically produce units over time using queue looping. Enforces local caps and faction budgets before spawning.
- **Factory Placement & Anchors**: Support for placing Horus Virtual Factories at the camera crosshair, or attaching/anchoring them directly to existing game buildings, airbases, or ship carriers.
- **Automatic Auto-Detect**: Added configuration settings (`autoCreateFactoryForAirbaseUnits` and `autoCreateFactoryForCarriers`) to automatically generate production factories for friendly airbases and carrier units on load.
- **Rally Points**: Added rally point configuration per factory. Units orient themselves toward the rally position upon spawning.
- **Persistent JSON Configs**: Auto-generates and loads `rts_factories.json` for global factory presets and `rts_factory_instances.json` to persist active factories, queue positions, and state across sessions.
- **Factory Visual Resolution Logs**: Factory creation now logs preset, requested visual building, resolved building, UnitDefinition lookup status, spawned visual unit, factory id, faction, and position.
- **Factory Preset Reset**: Added **Reset Factory Presets To Defaults** to regenerate the default preset set, including visual buildings and `Mixed Production`.
- **Factory Instance Save/Load**: Factory instances now persist preset name, anchor destroyed state, rally point, queue, timer, production index, visual building, and anchor metadata.
- **Multiplayer Security**: Locked all factory creation, deleting, queue management, and saving/loading actions behind host-authoritative gates. Non-host clients receive clear blocking notifications.
- **Groups UI Collapsed**: Folded the Groups & Formations section by default (`showGroupTools = false`) and kept group spawning disabled by default to keep the UI clean.
- **Groups & Formations**: Spawn multiple units in standard formations (**Line**, **Column**, **Grid**, **Circle**, **V Formation**).
- **Group Presets**: Instantly load preset groups (Convoy, Armored Group, Squadron, Air Patrol, Naval Group, Anti-Air Battery, and Base Defense). Sane defaults for counts, spacing, formation, stationary behavior, and altitudes (e.g. 1000m for Squadron, 1500m for Air Patrol).
- **Custom Group Editor**: Build custom groups containing a mix of unit types, with a visual list, save/load features using JSON, and an in-game file selector.
- **Group Ghost Previews**: Renders transparent preview meshes of all units in the selected formation, spacing, and height adjustments before placement.
- **Spawn Ground Units Stationary**: Option to lock spawned vehicles and ships in a stationary state using `.SetHoldPosition(true)` (applies to both single and group spawns).
- **Camera/Control Restore**: Saves and restores cockpit/camera state, following unit, and flight controls state (`GameManager.flightControlsEnabled`) when entering/exiting Horus Mode. If the aircraft was destroyed during Horus Mode, it prints a message and handles it safely without crashing.
- **Safety Deletion Toggle**: Added `AllowDeletingOriginalMissionUnits` configuration option to permit deleting built-in map units.
- **Custom Group JSON Validation**: Catch broken/empty custom group JSON files, showing a clear warning, and protecting group spawns from executing with empty groups.
- **Ocean Height & Snapping**: Added automated ocean level snapping for ships, manual ocean snapping toggle, and configurable custom ocean heights.

### Changed
- **Nearest Unit Deletion**: Target nearest unit in range (25m/50m/100m/custom) on middle-click instead of requiring direct hit.
- **Client Permissions Gate**: Audited and strictly restricted all client-side spawn, delete, and economy actions to enforce host-authoritative multiplayer validation.
- Refactored height snapping to calculate elevation individually per unit in group formations.
- Added GUI detection to prevent camera zoom/look movement and unit placing/deleting while hovering the mouse over the F10 configuration window.
- Existing `rts_factories.json` files with missing `visualBuilding` fields are completed in memory, while old invalid names still resolve through alias/fallback mapping.
- Factory production now validates queue entries by factory type, uses dedicated ground/air/naval/defense spawn paths, pauses on budget/cap/anchor failures, and cleans produced-unit tracking every 5 seconds.
- Ground factory production now prefers validated positions in front of the factory exit/door area and samples terrain while ignoring unit colliders, so produced vehicles do not use the factory roof/top as ground.
- Ground and defense production force-correct the spawned transform to the validated terrain position after editor spawn, preventing the spawner from leaving units above the factory visual.
- Defense factory defaults now include multiple real building/defense definitions: `23mm AAA Emplacement`, `IRM-S1 Emplacement`, `AT-145 Emplacement`, `Guard Tower`, `Pillbox`, and `Radar Station`.

### Fixed
- Virtual factory creation now spawns a visible world building using valid encyclopedia entries (`Storage Tank`, `Large Factory`, `Medium Aircraft Hangar`, `Radar Station`) and registers the factory in `activeFactories`.
- Factory income and production stop when an attached or visual anchor is destroyed.
- Loading factory instances replaces the current list instead of duplicating factories on repeated loads.


---

## [1.1.0] - 2026-06-04

### Added
- **Ghost preview**: a local-only, semi-transparent preview of the selected unit at the placement position. It follows the cursor, respects altitude/yaw/snapping, never networks or spawns anything, and is fully cleaned up when Horus Mode exits. Toggle and config (`EnableGhostPreview`).
- **Grid snapping**: align placement to 1m / 5m / 10m / 25m / 50m / 100m or a custom spacing.
- **Rotation snapping**: snap yaw to 1° / 5° / 15° / 45° / 90°.
- **Snap to ground** for ground units and experimental **align to surface normal** (ground units only).
- **Map Spawn Mode**: toggle on, open the map (M key), left-click to spawn units at the map cursor. Auto-opens/closes the map and shows an on-screen hint and crosshair.
- **Placement scroll shortcuts**: `Ctrl + Scroll` = altitude, `Alt + Scroll` = yaw, add `Shift` for larger steps. Configurable normal/large steps and `InvertScrollDirection`.
- **Multiplayer permission model**: centralized host-authoritative checks (`Single Player` / `Multiplayer Host` / `Multiplayer Client - No Permission`). Clients are blocked from spawning/deleting by default, with reserved config flags for a future client whitelist.
- Object yaw rotation controls before spawning (slider, presets, custom input).
- Reset Altitude and Reset Yaw buttons; altitude and yaw shown together in the UI.
- Configurable altitude/rotation steps via BepInEx config.

### Changed
- UI reorganized into collapsible sections (Placement Tools, Map Spawn, Controls) with live status labels for ghost preview, grid snap, rotation snap, and permission mode.
- Spawn and ghost preview now share one placement pipeline so the preview always matches the real spawn.
- Version number shown in the UI title bar.

### Fixed
- **Unsafe middle-click deletion**: delete now walks up only to a gameplay `Unit` root and validates the target. Terrain, roads, static map geometry, the map UI, and original mission units can no longer be deleted. By default only Horus-spawned units are removable (`AllowDeletingNonHorusUnits` to opt in).
- **Scroll shortcut input source**: altitude/yaw shortcuts now read the game's mouse-wheel via the Rewired "Zoom View" axis (with Unity input fallbacks), fixing Ctrl/Alt + Scroll not responding.
- (v1.0.9) GUI scroll and slider not responding due to input being consumed by camera.
- (v1.0.9) Duplicate and unnamed units appearing in unit list.

---

## [1.0.9] - 2026-06-04
### Changed
- Re-branded from Zeus Mod to Horus Mod Starter.
- Completely restructured the project for professional GitHub open-source release.
- Output DLL renamed from `NuclearOptionZeusMod.dll` to `HorusMod.dll`.
- Configurable hotkeys via BepInEx config (`F9`, `F10`).
- Robust Harmony patch guard with try/catch and null checks.
- Spawner.i null-check before spawning to prevent crashes.

### Fixed
- F9 not working in single-player due to incorrect `Spawner.i.IsServer` check.
- PostBuild deploy path fixed for reliable DLL copy.

### Added
- README with installation and uninstall instructions.
- CHANGELOG, LICENSE (MIT), and .gitignore.
- `Directory.Build.props.template` for portable builds.
- Release ZIP packaging for NOMM/NOMNOM compatibility.
