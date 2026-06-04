# Changelog

## [1.0.9] - 2026-06-04
### Changed
- Re-branded from Zeus Mod to Horus Mod Starter.
- Completely restructured the project for professional GitHub open-source release.
- Added `Directory.Build.props.template` and clean `.csproj` for easy building.
- Re-architected project structure into `src/` directory.

### Fixed
- Fixed a P0 bug where F9 would not toggle the mod in Single Player mode due to `Spawner.i.IsServer` checks aggressively blocking interaction.
- Allowed local single-player destruction and spawning.
- Blocked multiplayer clients without server privileges from spawning/deleting.

### Added
- Integrated BepInEx Config binding for F9 and F10 hotkeys.
- Added robust BepInEx `ManualLogSource` logging.

## [1.0.8]
### Added
- Initial Steam guide release of Zeus Mod.
