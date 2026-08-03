# Horus Mod Starter Roadmap

This roadmap separates released behavior from work that is still under development or requires validation inside Nuclear Option. A feature listed under **Unreleased** is not a published release guarantee.

## Shipped in v1.2.4

- Aircraft movement orders now use the native server autopilot for host-controlled AI.
- The Manage tab can apply standard aircraft loadouts, liveries, and pilot skill after spawn.
- Factory failures report actionable faction, anchor, queue, budget, cap, and spawn status.
- Cursor ownership, RMB context selection, Neutral factory validation, rally orders, and incomplete factory configuration migration were corrected.
- Dedicated/headless control was deliberately kept out of the release.

## Shipped in v1.3.0

### Reliable aircraft customization

- Remove the one-selection/one-spawn delay by deferring IMGUI selection changes until the next safe layout pass.
- Keep independent customization state for the next spawn and for selected aircraft, keyed by the aircraft definition's stable `jsonKey`.
- Route individual, group, duplicate, undo/redo, and factory aircraft creation through the same spawn service.
- Supply loadout, fuel, livery, and pilot skill before an aircraft is published on the network; later changes use a new validated `Networkloadout` value.
- Reject mixed-model batch edits clearly. Ground vehicles and ships retain their fixed armament and may only be rearmed when the game exposes compatible logistics behavior.

### Per-hardpoint loadout editor

- Support `Default`, `Standard Preset`, `Current Session`, `Horus Saved Preset`, `Copy Current Aircraft`, and `Custom Hardpoints` sources.
- Build custom loadouts from the aircraft's native `HardpointSet` data, including paired/symmetric editing, exclusions, HQ restrictions, event restrictions, and read-only preservation of hidden mounts from trusted native sources.
- Support compatible mod aircraft even when they do not expose `StandardLoadouts`.
- Store named, aircraft-specific Horus presets in `BepInEx/config/HorusMod/aircraft_loadouts.json` using stable aircraft and weapon-mount keys.
- Treat `GameManager.aircraftCustomization` only as temporary session data. Horus will not describe it as a persistent Nuclear Option loadout library.

### Expanded catalog and experimental content

- Discover aircraft, vehicles, ships, buildings, scenery, missiles, `otherUnits`, and requested lookup-only definitions.
- Refresh automatically when the Encyclopedia changes and expose a manual **Refresh Catalog** action for content loaded late by other mods.
- Show unnamed definitions such as `???` with their stable key and status badges instead of silently discarding them.
- Classify entries independently by spawn kind, placement surface, network registration, and functional capabilities.
- Keep `WeaponMount` definitions in the loadout editor; they are not standalone world objects.
- Expose lookup-only definitions behind **Force incompatible content**, disabled by default. Each definition requires an explicit per-session confirmation because unregistered prefabs may fail or desynchronize.

### Logistics and live ordnance

- Detect functional supply behavior from components rather than display names: `Rearmer`, `Refueler`, `UnitStorage`, and `WarheadStorage` are reported separately.
- Add `Logistics`, `Ammo`, `Naval Resupply`, `Fuel`, and `Storage` filters plus a `Can resupply ships: yes/no/unknown` diagnostic.
- Offer **Spawn Naval Resupply** only for a definition whose prefab reports naval rearm capability. The selected ship can provide faction and an in-range placement target; Neutral cannot perform functional rearming.
- Expose `MissileDefinition` entries as **Live Ordnance** for individual placement only. They remain excluded from groups, repeat placement, RTS presets, and factory queues.
- Spawn above the clicked point and fire straight down, so the click location is the impact point. Targeted launch modes were subsequently expanded in v1.4.3. No launch confirmation is required.

### Runtime validation still required

- Verify `NavalSupplyContainer1` and `NavalPallet1` with a ship whose ammunition has actually been reduced. A correctly configured generic `Rearmer` can report component-level ship compatibility before this test, but ammunition recovery and any single-use consumption remain unverified until observed without new errors in `LogOutput.log`.
- Validate standard and custom loadout replication with a remote multiplayer client.
- Validate hidden, event, lookup-only, and mod-provided definitions individually. Visibility in the catalog does not prove that a prefab is network-safe.
- A reusable **Naval Ammo Depot** preset will not be added while the available candidates appear to be single-use supplies.

## Shipped in v1.3.1

- Reliable RMB context menus that ignore transparent HUD graphics, distinguish drag by movement rather than hold duration, and allow one-click menu relocation.
- Structured Verbose RMB diagnostics for runtime reports.

## Shipped in v1.4.0

- Tactical Attack Target, Attack-Move, multi-point Patrol, Guard/Escort, and per-unit ROE for compatible host-controlled AI.
- Native-intelligence target validation and automatic aircraft combat/resume behavior.
- Correct Move/Rally/Hold lifecycle for AI aircraft.
- Experimental global conventional-AI bombing correction with a fail-open compatibility switch.

## Shipped in v1.4.3

- Explicit **World Point**, **Track Selected**, and **Impact Selected** modes for Live Ordnance.
- Native seeker tracking from a chosen launch point for guided missiles.
- Target-relative, velocity-led overhead spawning for bombs, rockets, and guided weapons without attaching a projectile to the target or replacing native damage/fuze behavior.

## After v1.4.3 - Dedicated server support

Dedicated/headless control is not supported yet. The intended architecture is:

1. Split UI/camera/input code from an authoritative, headless-safe Horus server runtime.
2. Verify BepInEx startup and mission rotation on the official Windows and Linux dedicated servers, including Workshop missions.
3. Introduce a versioned, read-only Mirage protocol before permitting world mutations.
4. Authenticate Game Masters using the network player's SteamID and a server-side allowlist that is empty by default.
5. Add request IDs, deduplication, rate limits, strict server-side definition/position validation, and audit logging.
6. Enable reversible spawn/delete commands before movement, loadouts, or factory administration.
7. Keep Nuclear Option's loopback remote-command TCP interface limited to server status and mission administration; it will not be repurposed as an unauthenticated Game Master transport.

Normal players should not need the Horus client. Content supplied by third-party asset mods will still require compatible content on every machine that must render or simulate it.

References:

- [Official dedicated server guide](https://github.com/Shockfront-Studios/Nuclear-Option-Server-Tools/blob/main/DedicatedServerGuide.md)
- [Official server command documentation](https://github.com/Shockfront-Studios/Nuclear-Option-Server-Tools/blob/main/ServerCommands/Readme.md)

## Release policy

- v1.3.0 published 2026-07-31; v1.3.1 and v1.4.0 were built as the approved RMB/tactical release sequence on 2026-08-03; v1.4.1 fixes the deferred IMGUI context-menu lifecycle; v1.4.2 adds global-English menus and group target orders; v1.4.3 adds explicit target-relative Live Ordnance modes.
- Do not publish, push, tag, or package a new version without explicit approval.
- Keep runtime-dependent behavior marked experimental or unknown until it passes the acceptance checks above.
