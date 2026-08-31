# Changelog

## [Unreleased]

### Added - v2.0.0-rc.1 candidate

- Split artifacts into pure `Horus.Shared`, headless-safe `Horus.Server`, and visual `Horus.Client` assemblies with a common `IHorusCommandGateway` contract.
- Added a versioned, manually serialized Mirage protocol for authenticated commands, capabilities, results, state requests, paged snapshots, and sequenced state events.
- Added authoritative dedicated-server execution for spawn, duplicate, safe delete, movement and tactical orders, ROE, aircraft editing, RTS budgets/modes/caps, factory administration and persistence, and undo/redo.
- Added exact SteamID64 allowlisting sourced only from authenticated `INetworkPlayer.AuthData`, with an empty deny-all template and an opt-in `Horus.Server.cfg`.
- Added daily sanitized JSONL audit logs with 14-day default retention and optional read-only Nuclei status/diagnostic commands.
- Added reproducible, parameterized GM, Dedicated, and Full packages with embedded SHA-256 manifests and no automatic deployment.
- Added a dedicated-server installation/security guide for the official Windows and Linux app and its required acceptance matrix.
- Added a single fail-fast release validator, public dependency-free CI, UTF-8/global-English checks, deterministic release sidecars, runtime evidence helpers, security policy, upgrade guide, troubleshooting guide, release checklist, and English prerelease notes.

### Security

- Added per-SteamID token-bucket rate limits, request-ID deduplication, session/revision checks, 16 KiB message limits, 64-entity and 32-waypoint limits, and rejection of unknown keys, NaN, infinity, invalid factions, invalid costs, incompatible protocol versions, and stale commands.
- Tightened allowlist parsing to accept only individual SteamID64 values, reject control characters in stable keys, reject malformed UTF-8 instead of replacing it silently, and emit strictly escaped single-line JSON audit records with bounded parameter metadata.
- Server-side code re-resolves Unity catalogs and native state; no `Unit`, `Loadout`, `FactionHQ`, or other Unity object is accepted over the network.
- Deletion is limited to Horus-owned units by default. UDP-only connections, display names, factions, passwords, and claimed owner IDs never grant GM authority.

### Changed

- Client visual state, selection, camera, previews, hotkeys, favorites, and preset editing remain local; every world mutation uses the same local or remote authoritative gateway.
- The dedicated factory runtime now shares the six client presets, validates type-correct queues, persists full runtime state, and delegates produced-unit replication to native game APIs.
- Targeted naval resupply now carries a stable ship identity so the dedicated server can validate the ship and issue its native rearm request after spawning a compatible supply object.
- Project targets are aligned to deterministic .NET Framework reference assemblies, resolving the former `System.IO.Compression` assembly-version conflict instead of suppressing it.

### Validation

- The first official Windows headless smoke test exposed and fixed BepInEx 5 rejecting a prerelease suffix in the plugin attribute; the BepInEx metadata now uses numeric `2.0.0` while Horus retains `2.0.0-rc.1` as its displayed/informational version.
- Dedicated installation now explicitly requires BepInEx `HideManagerGameObject = true` so Nuclear Option scene transitions preserve the plugin runtime and its Mirage handler.
- The Windows smoke tests also exposed Unity `JsonUtility` dropping nested factory/economy fields. Client and dedicated economy persistence plus dedicated factory persistence now use the game-provided Newtonsoft.Json assembly; dedicated reload is runtime-validated after restart.
- Client factory preset parsing now preserves production queues through Newtonsoft as well, eliminating the repeated startup migration caused by dropped list fields.
- Factory normalization no longer marks the non-producing Economy preset as changed merely because its intentional production interval is zero.
- Expanded the pure logic suite from 26 to 80 checks, including every packet round-trip, strict UTF-8, truncation/magic/kind failures, exact boundary limits, individual SteamID64 parsing, deduplication capacity, rate limits, snapshot paging, mission state reset, audit JSON/retention, full factory/economy snapshots, and malformed message handling.
- Added an assembly-reference gate that rejects UI, IMGUI, camera/input module, or Rewired references from `Horus.Server.dll`.
- Validated two sequential official Linux headless starts on WSL 2 Ubuntu 24.04.4. The Linux helper now requires the depot's 64-bit Steam runtime and exports the official `linux64` loader path, preventing accidental selection of the root 32-bit `steamclient.so`.
- Validated native download, update verification, JSON resolution, `AfterLoad`, and server-side selection of public Workshop mission `3725687524` on isolated Windows and Linux dedicated servers.
- Validated the portable gate on a clean GitHub-hosted Ubuntu runner; the gate now restores test assets explicitly instead of relying on a local `obj` directory.
- Production release remains blocked until the documented official Windows and Linux headless runtime matrix, two-client behavior, and four-hour soak pass.
- The prerelease may transparently retain `PENDING – second legitimate Steam identity unavailable`; two simultaneous GMs and two concurrent identities cannot be simulated with Windows user profiles. These pending cases continue to block the stable `v2.0.0` release.

## [1.4.3] - 2026-08-03

### Added

- Live Ordnance now has three explicit English target modes: **World Point**, **Track Selected**, and **Impact Selected**.
- **Track Selected** launches from the clicked point, aims with linear motion lead, passes the selected unit's network name to the native seeker, and is disabled for bombs/rockets without a seeker.
- **Impact Selected** treats the click as confirmation, spawns the weapon at a configurable safe height above the selected unit, and leads its current velocity. Guided weapons continue native tracking; unguided bombs remain ballistic.
- The target-relative launch pose is shown by the existing local ghost preview, and blocked target modes now return an actionable reason instead of silently falling back to the click point.
- Firing Live Ordnance now keeps the designated target selected instead of replacing the selection with the spawned projectile.

### Changed

- Live Ordnance is never parented or teleported onto a target after spawning. Targeted shots retain native rigidbody physics, arming/fuze behavior, collision, damage, and network spawning.

## [1.4.2] - 2026-08-03

### Added

- Multi-selection now exposes group target modes for Move, Attack-Move, Patrol, Attack Target, and Guard/Escort from both the unit context menu and the Manage tab. One LMB target applies the selected order to the whole captured group; RMB or Esc cancels targeting.
- Group target modes show a persistent world overlay and editor status until the destination or unit target is accepted.

### Changed

- Standardized every contextual-menu label, header, tooltip, and fallback action on global English instead of applying Spanish text to every player.

## [1.4.1] - 2026-08-03

### Fixed

- Fixed the unit RMB menu aborting after selection because `GUI.skin` was queried from `Update`, outside Unity's legal `OnGUI` lifecycle. Menu layout is now deferred to the next IMGUI pass.
- Added a safe tactical fallback if optional aircraft/loadout menu construction fails, with the full exception recorded in the BepInEx log.

### Added

- Added a visible **Tactical Orders** card to the Manage tab with an **Open Orders Menu** button, direct Hold/Clear/ROE controls, current order state, and concise instructions for Move, Attack-Move, Patrol, Attack Target, and Guard/Escort.
- Successful RMB unit-menu openings now leave a Normal-level diagnostic with the target unit and option count.

## [1.4.0] - 2026-08-03

### Added

- Host-authoritative tactical order registry with Move, Hold, Attack Target, Attack-Move, multi-waypoint Patrol, and Guard/Escort for compatible aircraft, ground units, and ships.
- Context-sensitive unit actions: right-clicking a known enemy preserves the current selection and offers Attack Target; right-clicking a friendly offers Guard/Escort.
- Multi-waypoint patrol planning with live world preview, LMB to add points, Backspace to undo, Enter to confirm, and Esc to cancel.
- Per-unit Rules of Engagement with Weapons Free and Hold Fire. Hold Fire gates offensive station, mount, and remote fire paths while leaving player aircraft and countermeasures untouched.
- `ImproveAIBombingAccuracy`, enabled by default, corrects conventional AI bomb release for target motion, ballistic fall, and rail/ejection delay while retaining skill-dependent zero-mean dispersion.
- Focused automated logic tests for RMB gesture classification, context-menu ownership, and patrol route progression.

### Changed

- Attack Target only uses contacts already present in the commanding unit's native faction tracking database; Horus does not create or refresh intelligence contacts.
- Attack-Move, Patrol, and Guard aircraft temporarily return to native combat when an engageable known threat is in range, then resume their Horus route after the threat clears.
- Tactical options are hidden when no selected unit supports the required movement controller.

### Fixed

- Aircraft Move and factory Rally states now complete on arrival and restore the previous native pilot state instead of trapping the aircraft permanently in `Horus move`.
- Aircraft Hold is now a distinct persistent state and Clear Orders reliably restores native AI.
- Ground and naval Attack Target orders continuously reassert the selected known target through their native weapons and turret APIs.

## [1.3.1] - 2026-08-03

### Fixed

- Right-click context menus are no longer suppressed by transparent full-screen game HUD graphics. Horus now blocks RMB only over its own visible surfaces or genuinely interactive native UI controls.
- An open context menu no longer captures the entire screen. Right-clicking elsewhere relocates the context action in one gesture, while left-clicking outside dismisses the menu without selecting through it.
- RMB camera look now begins only after 10 pixels of pointer movement; a stationary press remains a click regardless of duration.
- Added one structured Verbose diagnostic for every RMB gesture, including UI ownership, movement, world pick, outcome, and menu item count.

## [1.3.0] - 2026-07-31

### Added

- Aircraft loadout sources for Default, native standard presets, the current Nuclear Option session, named Horus presets, the selected aircraft, and custom hardpoints.
- Per-hardpoint aircraft editing backed by native `HardpointSet` choices, including symmetric editing, exclusions, HQ/event restrictions, and preservation of hidden mounts from trusted native sources.
- Aircraft-specific named preset storage at `BepInEx/config/HorusMod/aircraft_loadouts.json`, keyed by stable aircraft and weapon-mount identifiers.
- Catalog discovery for missiles, `otherUnits`, and requested lookup-only definitions, plus a manual **Refresh Catalog** action for content registered late by other mods.
- Catalog metadata for spawn kind, placement surface, network-registration state, unlabeled/experimental content, and supply capabilities.
- `Logistics`, `Ammo`, `Naval Resupply`, `Fuel`, and `Storage` filters with a `Can resupply ships: yes/no/unknown` diagnostic.
- Experimental individual spawning of native `MissileDefinition` entries as **Live Ordnance**. It now spawns above the clicked point and fires straight down, so the click location is the impact point; speed still controls the launch impulse. An explicit, off-by-default **Guide toward selected unit** toggle designates the single selected unit as a native guidance target (`Missile.SetTarget`) instead, for a moving-target shot that lands on the unit rather than the click point.
- A guarded **Spawn Naval Resupply** action for definitions that expose naval `Rearmer` capability.

### Changed

- Next-spawn aircraft customization and selected-aircraft editing now keep independent drafts keyed by `AircraftDefinition.jsonKey`.
- Aircraft spawn paths share an authoritative request/result service so loadout, fuel, livery, and skill can be supplied before network publication.
- Post-spawn loadout changes use a newly built, validated `Networkloadout` rather than mutating or reusing a shared preset object.
- Catalog refresh uses Encyclopedia content changes instead of a one-time instance check; unnamed `???` and compatible mod definitions are no longer discarded solely because of their display name.
- Props and supply objects are classified by prefab type/components rather than name heuristics; a naval supply container is no longer treated as a ship merely because its name contains "naval".
- Ground vehicles and ships explicitly report fixed armament instead of presenting aircraft-style loadout controls.
- Temporary `GameManager.aircraftCustomization` data is labeled **Current Session** and is not presented as a persistent Nuclear Option loadout library.

### Fixed

- Deferred IMGUI selection changes prevent loadout/livery controls from displaying or applying the previously selected aircraft's values.
- The first aircraft, group member, duplicate, undo/redo restore, and factory-produced aircraft no longer need a later patch-up spawn to receive the intended customization.
- Mixed-model aircraft selections are rejected clearly instead of partially applying an incompatible loadout.
- Mod aircraft and other definitions added after the initial catalog build can appear after automatic or manual refresh.
- Fixed a build-breaking mismatch with the native `Spawner.SpawnSavedMissile` signature (the base game added `hq`, `targetName`, and `guidingUnitName` parameters); Live Ordnance now compiles and spawns again, and the new `targetName` parameter is what powers guided-shot targeting above.
- Fixed native `Missile.LocalStart()` throwing a NullReferenceException on any munition with no `MissileSeeker` component (unguided bombs/rockets): it spawned and was destroyed again the same instant, with no explosion or error toast. Guarded with a Harmony prefix.
- Live Ordnance previously launched along the manual placement-rotation heading (`spawnYaw`), which has nothing to do with the click location, so every shot flew the same direction regardless of where you clicked or what you targeted. It now fires straight down from directly above the click point instead.
- The guidance-target lock was engaging automatically whenever a unit happened to be selected, silently overriding "click = impact" by steering to that unit's actual position instead — including stale selections left over from unrelated earlier actions. It is now off by default and must be explicitly enabled per shot.
- A custom aircraft loadout that fails validation at spawn (hardpoint conflict, HQ/mission restriction, etc.) no longer silently falls back to the aircraft's default loadout with no explanation; the rejection reason is now shown both live while editing hardpoints and as a toast at spawn time.
- Diagnosed with runtime logging (spawn X/Z was already proven pixel-exact to the click) plus a decompiled-source audit of every `MissileSeeker` subclass: `ARHSeeker.SlowChecks()` and `OpticalSeekerCruiseMissile.SlowChecks()`/`PreTerminalMode()` each treat "no target" as a self-destruct or divert-to-cruise-altitude trigger. Native gameplay never exercises that path (a pilot always fires these at something), but Horus's Live Ordnance spawns target-less by default, so active-radar-homing weapons (e.g. `ARH1`) always self-destructed within 2-10s wherever they happened to be, and cruise missiles (e.g. `CruiseMissile20kt`) always climbed toward cruise altitude and detonated mid-air ~2km short instead of diving onto the click point. Guarded with three Harmony prefixes that only change behavior when the seeker has no target — a real target (via **Guide toward selected unit**) still runs the native logic unmodified. Unguided/ballistic and optical/IR-seeker munitions (bombs, `SAM_IR1`, etc.) were already unaffected by this class of bug per the same audit.
- The 2D strategic map's click-to-place path (`DynamicMap`) uses a completely different, never-instrumented screen-to-world conversion than the 3D free-camera path, has no ghost preview, and its only visual cue (`DrawMapSpawnOverlay`) is gated on a different flag than what actually triggers a map spawn — so a click can silently resolve to a map-space position with zero on-screen confirmation of where it will land. Not yet fixed; tracked as a known gap uncovered by this investigation.
- Found the real cause of Live Ordnance landing far from the click point (previous fixes above addressed real but secondary bugs): a per-spawn trajectory logger showed every missile's actual position, 0.25s after spawning, snapping to almost exactly Unity world `(0,0,0)` — reconstructable exactly from the logged `datumOrigin`, proving it wasn't a placement or guidance bug at all. Several native `Spawner.Spawn*` methods (`SpawnSavedMissile`, `SpawnPilot`, `SpawnContainer`) move `transform.position` directly but never sync that into the `Rigidbody` (no `rb.position`/`MovePosition` call, unlike e.g. `SpawnVehicle`, which does); the next physics step then resets the transform back to wherever the Rigidbody's internal position was at `Instantiate` time (the prefab's authored local position, i.e. world origin), teleporting the unit away a fraction of a second after spawning regardless of where it was actually placed. This explained the missile drift and also matched two earlier, separately-reported symptoms with dismounted pilots and logistics containers landing away from the click point. Horus now force-syncs both `Rigidbody.position` and `transform.position` to the intended spawn point for every native spawn path, immediately after the call returns, in one central place (`HorusSpawnService.Spawn`) so any other definition type hitting the same native bug is covered automatically. Ships already had dedicated, more thorough correction logic from an earlier fix and were unaffected.

### Safety

- Lookup-only definitions require **Force incompatible content**, disabled by default, plus explicit per-definition/session confirmation; the UI warns that forced content may fail or desynchronize.
- Live ordnance is limited to single placement, excluded from repeat placement, groups, RTS presets, and factory queues, and no longer requires a launch confirmation.
- RTS Commander Mode's two-step "arm, then click again to deploy" gate (`RequireDeploymentConfirmation`) now defaults to off so placement is a single click; it remains available as an opt-in config toggle.
- `WeaponMount` assets remain loadout choices and are never advertised as spawnable world objects.
- Naval resupply status is derived from `Rearmer`; `Refueler`, `UnitStorage`, and `WarheadStorage` are reported separately so decorative/storage props are not claimed to rearm ships.
- `NavalSupplyContainer1` and `NavalPallet1` are detected as component-compatible candidates through the current generic `Rearmer` API when their linked unit, range, capacity, and active state pass inspection. Actual ammunition recovery and single-use behavior remain experimental until a depleted-ammunition runtime test confirms rearming and clean logs.
- Dedicated/headless Game Master control remains unsupported. v1.3.0 does not add unsafe TCP control or a hardcoded remote-admin path.

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

> Internal test build; it was not published or tagged as a public release.

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
