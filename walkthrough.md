# Horus Mod Starter v1.2.0 Walkthrough

## RTS Commander Mode

1. Start a mission as Single Player or Multiplayer Host.
2. Press `F9` to enable Horus Mode.
3. In the Horus window, switch from `Sandbox Mode` to `RTS Commander Mode`.
4. Open `RTS Factories & Production`.

Non-host multiplayer clients can view safe UI state, but real actions show Host-only indicators and are blocked in code.

## Factory Creation

1. Select a preset: `Economy Outpost`, `Ground Vehicle Factory`, `Airbase Production`, `Naval Yard`, `Defense Battery`, or `Mixed Production`.
2. Use `Create Factory Here` to place a virtual factory at the current Horus placement point.
3. Use `Create Factory From Aimed Unit` to attach a factory to the unit under the crosshair without spawning an extra visual building.
4. Use `Save Factories` to persist placed factories.

Virtual factories spawn visible buildings. Defaults are:

- Economy Outpost: `Storage Tank`
- Ground Vehicle Factory: `Large Factory`
- Airbase Production: `Medium Aircraft Hangar`
- Naval Yard: `Large Factory`
- Defense Battery: `Radar Station`
- Mixed Production: `Large Factory`

Older names such as `Solar Array`, `Vehicle Factory`, `Hangar`, and `Warehouse` resolve through alias/fallback mapping.

## Income And Production

- Factory income ticks only in RTS Commander Mode and only on Single Player or Host.
- Disabled factories and factories with destroyed anchors do not generate income.
- Factory production advances only when RTS Mode is active, the factory is enabled, production is enabled, the queue has a valid compatible unit, the active produced-unit cap is not reached, and the faction can afford the unit if paid production is enabled.
- Queue entries loop by default.
- Budget is deducted only after a successful spawn.

Spawn paths are type-specific:

- Ground factories spawn ground units in front of the factory exit/door area and sample terrain height while ignoring unit colliders, so they do not appear on top of the visual building.
- Air factories spawn aircraft at safe altitude near the factory using terrain height as the base.
- Naval factories use the safe ship spawning path and unique names at ocean level.
- Defense factories spawn stationary-compatible defense units and buildings in validated ground positions near the factory. Defaults include `23mm AAA Emplacement`, `IRM-S1 Emplacement`, `AT-145 Emplacement`, `Guard Tower`, `Pillbox`, and `Radar Station`.
- Mixed factories choose the correct path per queued unit type.

## Rally Points

1. Select a factory.
2. Aim at the desired rally point.
3. Click `Set Rally Point From Aim`.
4. Produced units face the rally point. If movement orders are unavailable, the unit is only oriented toward the rally point.
5. Click `Clear Rally Point` to return to default orientation.

## Persistence Files

- `BepInEx/config/HorusMod/rts_factories.json`: factory presets and default production settings.
- `BepInEx/config/HorusMod/rts_factory_instances.json`: placed factories, queues, timers, rally points, anchor state, and visual building metadata.
- `BepInEx/config/HorusMod/rts_economy.json`: budgets, passive income, unit caps, and cost fallbacks.

Use `Load Factories` to replace the current runtime list from disk. Repeated loads do not duplicate the list.

## Safety Defaults

- Sandbox Mode remains separate from RTS Commander Mode.
- Group spawning is disabled by default.
- Ship spawning uses the safe ship path and unique generated names.
- Factory actions, production ticks, budget edits, save/load, reload, and reset are blocked for non-host clients.
