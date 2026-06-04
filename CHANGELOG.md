# Changelog

## [1.1.0] - 2026-06-04

### Added
- Object yaw rotation controls before spawning.
- Yaw rotation slider (0°-360° in 5° steps).
- Yaw preset buttons: 0°, 45°, 90°, 180°, 270°.
- Ctrl + Scroll wheel adjusts spawn altitude.
- Alt + Scroll wheel adjusts spawn yaw rotation.
- Shift + Scroll wheel multiplies adjustment speed (5x).
- Map Spawn Mode: toggle on, open the map (M key), left-click to spawn units at map cursor position.
- Map Spawn Mode auto-opens/closes the in-game map and shows an on-screen hint and crosshair.
- Ground units placed from the map snap to terrain by default (toggle available).
- Custom yaw input field with Set button.
- Reset Altitude and Reset Rotation buttons.
- Altitude and yaw displayed together in the UI header.
- Configurable altitude step and rotation step via BepInEx config.
- Version number shown in the UI title bar.

### Changed
- UI layout reorganized for placement controls.
- Controls legend simplified.
- Altitude slider now rounds to 50m increments.

### Fixed
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
