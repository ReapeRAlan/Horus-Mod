# Changelog

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
