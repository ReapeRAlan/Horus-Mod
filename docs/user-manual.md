# Horus v2.0.0-rc.1 User Manual

> [!CAUTION]
> **TEST RELEASE — EXPERIMENTAL PRERELEASE — NOT PRODUCTION-CERTIFIED**
>
> This release candidate is published for dedicated-server field testing. The automated suite and four-hour headless soaks pass on Windows and Linux, but connected Game Master gameplay, ordinary-client replication, mission rotations, reconnect/resynchronization, abuse testing, and two simultaneous legitimate Steam identities remain pending. Read [Test status and reporting](#test-status-and-reporting) before using it on a public server.

Horus is a visual Game Master and free-camera mod for Nuclear Option. It can run in single player, on a player-hosted game, or with the official dedicated server. World-changing actions are authoritative: the server validates the authenticated caller, content keys, positions, costs, ownership, revisions, and command limits before it changes the mission.

## 1. Choose the correct package

| Machine or role | Package | Installed assemblies |
|---|---|---|
| Game Master connecting to a dedicated server | `Horus-GM-v2.0.0-rc.1.zip` | `Horus.Shared.dll`, `Horus.Client.dll` |
| Official dedicated server | `Horus-Dedicated-v2.0.0-rc.1.zip` | `Horus.Shared.dll`, `Horus.Server.dll` |
| Single player or player-hosted game | `Horus-Full-v2.0.0-rc.1.zip` | Shared, Client, and Server |

Do not mix package versions. Remove the legacy `NuclearOptionZeusMod.dll` before installing 2.0. Do not install the GM package on a headless server or the Dedicated package as a visual GM client.

## 2. Installation

### Single player or local host

1. Install BepInEx 5 for Nuclear Option and start the game once.
2. Stop the game.
3. Extract `Horus-Full-v2.0.0-rc.1.zip` into the Nuclear Option root. Confirm these files exist:
   - `BepInEx/plugins/Horus/Horus.Shared.dll`
   - `BepInEx/plugins/Horus/Horus.Client.dll`
   - `BepInEx/plugins/Horus/Horus.Server.dll`
4. Start the game and inspect `BepInEx/LogOutput.log`. Horus must load without an exception.
5. Enter a mission and press **F9**.

### Dedicated server operator

1. Install the official Nuclear Option dedicated server (Steam app `3930080`) and the correct BepInEx 5 Mono package for the operating system.
2. Start and stop the server once so BepInEx creates its configuration files.
3. In `BepInEx/config/BepInEx.cfg`, set this under `[Chainloader]`:

   ```ini
   HideManagerGameObject = true
   ```

4. Extract `Horus-Dedicated-v2.0.0-rc.1.zip` into the server root.
5. In the Nuclear Option dedicated configuration, set `ModdedServer` to `true`.
6. Edit `BepInEx/config/HorusMod/dedicated_admins.txt` as UTF-8. Add one real SteamID64 per line. Blank lines and lines beginning with `#` are allowed. The packaged file is deliberately empty, which denies all Horus mutations.
7. Review `BepInEx/config/Horus.Server.cfg`. Keep these safer defaults unless a test specifically requires otherwise:

   ```ini
   Enabled = false
   AllowMissionUnitMutation = false
   AllowMissionUnitDelete = false
   ```

8. After reviewing the allowlist and policy, deliberately set `Enabled = true`.
9. Start the server and confirm that the log reports Horus Server, Mirage handler registration, and the active deny/allow state without graphical or headless exceptions.

Use the complete [Windows, Linux, and WSL dedicated-server guide](dedicated-server.md) for SteamCMD commands, mission configuration, helper scripts, firewall guidance, evidence capture, and runtime checks.

### Game Master client for a dedicated server

1. Install the same BepInEx 5 generation used by the supported client setup.
2. Extract `Horus-GM-v2.0.0-rc.1.zip` into the game root.
3. Join through Steam as a normal authenticated player whose exact SteamID64 appears in the server allowlist. The GM occupies a normal player slot; spectator authentication is not used.
4. Enter the running mission and press **F9**.
5. Confirm the Horus status indicates authorized dedicated authority before issuing a mutation. If it does not, stop and follow [Permission troubleshooting](#permission-troubleshooting).

## 3. Essential controls

| Input | Action |
|---|---|
| **F9** | Enter or leave Horus Mode |
| **F10** | Hide or show the Horus interface |
| **Ctrl + F10** | Restore the interface and reset its window position |
| **Left Click** | Select a unit or place an armed unit/group |
| **Shift + Left Click** | Add to selection or repeat an armed placement |
| **Left-drag** | Box-select units |
| **Middle Click** | Safely delete a Horus-created unit under the configured policy |
| **Right Click on terrain** | Move the selected units |
| **Right Click on a unit** | Open the contextual command menu |
| **Alt + Right Click on terrain** | Open Move, Attack-Move, and Patrol options |
| **Right-drag** | Rotate the free camera |
| **W/A/S/D/Q/E** | Move the free camera |
| **Left Shift** | Increase camera speed |
| **F / H / Delete / Esc** | Focus / hold / delete / cancel |
| **Ctrl+D / Ctrl+A / Ctrl+Z / Ctrl+Y** | Duplicate / select Horus units / undo / redo |
| **Ctrl+1–9 / 1–9** | Assign / recall control groups |
| **Ctrl + Scroll** | Change placement altitude |
| **Alt + Scroll** | Change placement yaw |
| **Shift + Scroll** | Use larger altitude or rotation steps |
| **M** | Open or close the strategic map |

During patrol planning, use left click to add a waypoint, Backspace to remove the latest point, Enter to confirm, and Esc to cancel. Hotkeys can be changed through the generated BepInEx client configuration.

## 4. First safe test

1. Use a private BuiltIn mission with no uninformed players.
2. Confirm the version, protocol, authority, faction, and budget shown by Horus.
3. Open **Place**, select a native ground unit, and inspect its definition and cost.
4. Move the ghost preview to a clear position. Adjust grid, ground snap, yaw, and altitude if needed.
5. Arm placement, then left-click once.
6. Select the new unit and issue a short move order.
7. Press **Ctrl+Z** to undo, then **Ctrl+Y** to redo.
8. Delete the created unit and confirm that an original mission unit cannot be deleted with the default policy.
9. On a dedicated server, inspect the audit JSONL record before enabling more complex tests.

If any step desynchronizes, produces a graphical/headless exception, or affects an original mission unit unexpectedly, stop the test and preserve the logs.

## 5. Placement and spawning

- Use **Place** to search native and compatible registered definitions by category, role, favorites, or recent use.
- A ghost preview is client-only. It does not create or replicate an entity.
- **Snap to Ground** is intended for ground units, buildings, and scenery. Surface-normal alignment remains experimental.
- Grid and rotation snapping make structured bases easier to reproduce.
- Map Spawn uses the strategic map; verify the resolved location before testing dangerous content.
- Group placement supports Column, Line, Grid, Circle, and V formations. A single command is limited to 64 entities; the visual group editor uses a safer limit of 20.
- Native faction groups use the faction's registered convoy definitions and native cost. Custom groups are expanded into stable definition keys for authoritative validation.
- Live Ordnance is experimental and limited to single placement. Keep guidance toward a selected unit off unless that behavior is intentional. Do not place it near players during initial validation.
- Lookup-only or forced incompatible content can fail or desynchronize. It requires explicit confirmation and should not be used in a public test.

The server re-resolves definitions, positions, hardpoints, costs, faction membership, and targets. A client preview or displayed calculation is never authority.

## 6. Selection and tactical orders

- Select one unit with left click, add units with Shift + left click, or drag a selection box.
- Use right click for a formation-aware move.
- Right-click a known enemy to issue **Attack Target**. The target must already be known to the unit's native faction HQ.
- Right-click a friendly to issue **Guard/Escort**.
- Use Alt + right click or **Manage > Tactical Orders** for Move, Attack-Move, Patrol, Hold, Clear Orders, Weapons Free, and Hold Fire.
- Player-controlled aircraft are protected from Horus AI orders.
- Original mission units cannot be ordered or edited while `AllowMissionUnitMutation = false`.
- A command can contain at most 32 waypoints. Oversized, unknown, stale, non-finite, or out-of-range input is rejected.

## 7. Aircraft editing

**Next Spawn** and **Selected Aircraft** are separate editing targets.

- **Default** resolves the native default loadout.
- **Standard Preset** uses a compatible preset registered by the aircraft.
- **Horus Preset** loads a saved local preset using stable definition keys.
- **Session** reflects the current compatible aircraft state.
- **Custom Hardpoints** permits only mounts and stores validated by the aircraft, mission, faction HQ, exclusion rules, and escalation policy.

You can also edit compatible liveries, fuel, pilot skill, and exposed properties. The dedicated server validates the requested aircraft identity and all values again. If the server rejects the edit, refresh state instead of repeatedly sending the same stale command.

## 8. Safe deletion, duplication, and history

- Default deletion applies only to entities registered as Horus-created.
- `AllowMissionUnitMutation` and `AllowMissionUnitDelete` are separate policies. Enabling mutation does not silently enable deletion.
- Duplicate creates a new authoritative entity; it does not transfer a Unity object over the network.
- Undo and redo are maintained by the authority for the current mission session. A mission change creates a new session and invalidates stale history.
- Request a fresh snapshot after reconnecting or after Horus reports a session/revision mismatch.

## 9. RTS economy and factories

The **RTS** tab provides faction budgets, income, unit caps, deployment, factories, production queues, and rally points.

1. Select a playable faction and confirm its displayed budget.
2. Review the native unit value and any configured stable-key override.
3. Arm deployment and place the unit. Budget is charged only after an accepted spawn.
4. Create a factory at a validated location or from a compatible aimed unit.
5. Choose a factory type, add compatible queue entries, set a rally point, and start production.
6. Confirm produced units, budget changes, queue progress, and audit records.
7. Save factory state, restart in a controlled test, and verify recovery before relying on persistence.

Invalid faction, queue type, content key, cost, position, cap, anchor, or persistence input is rejected. Keep backups of `BepInEx/config/HorusMod/` before changing versions.

## 10. Dedicated-server security model

- Only an already authenticated Steam connection can obtain Horus authority.
- Authorization uses the exact SteamID64 derived from the live network player's authentication data.
- Display names, passwords, faction, claimed ownership, host labels, and UDP connections do not grant Horus authority.
- An empty allowlist denies all mutations. Any invalid allowlist entry causes the complete file to be rejected.
- The external TCP administration endpoint is not a Horus gameplay-control channel.
- Commands are capped at 16 KiB, rate-limited per SteamID, deduplicated by request ID, session-bound, and revision-checked.
- Accepted and rejected structured commands are written as sanitized daily JSONL audit records. The default retention is 14 days.
- Use a real Steam player slot for the GM. Do not attempt to bypass the incomplete spectator authentication path.

Never publish a real administrator allowlist, Steam credential, server password, private IP address, authentication token, or unsanitized audit file.

## 11. Logs, audit, and persistence

The primary diagnostic log is `BepInEx/LogOutput.log`. Horus server configuration and data are under `BepInEx/config/HorusMod/`; daily audit JSONL files use the configured audit path. Factory and economy persistence are also stored under the Horus configuration directory.

Before a test:

1. Stop the game/server.
2. Back up `BepInEx/config/HorusMod/` and the active Horus configuration.
3. Record the package SHA-256, game/server build, BepInEx version, operating system, mission ID, and Horus protocol version.
4. Start with fresh logs when practical.

After a test, stop the process cleanly and retain sanitized logs, audit records, metrics, screenshots, and exact reproduction steps.

## 12. Permission troubleshooting

### F9 does not open Horus

- Confirm the GM or Full package is installed on the visual client.
- Confirm BepInEx loaded `Horus.Client.dll` without an exception.
- Check for another mod using F9.
- Press Ctrl + F10 to restore an off-screen interface.

### The GM is view-only or denied

- Confirm `Enabled = true` on the dedicated server.
- Confirm `ModdedServer` is `true`.
- Confirm the exact authenticated SteamID64 is the only content on its allowlist line.
- Reject and repair the entire allowlist if any line is invalid.
- Confirm the GM joined through Steam, not UDP.
- Confirm GM and server packages use the same Horus protocol version.
- Reconnect after an allowlist or mission-session change.

### Protocol, session, or revision mismatch

Install matching packages. Do not retry an old envelope after a mission change. Reconnect or request a fresh snapshot so Horus can use the current session ID and revision.

### Headless graphical exception

Confirm only Shared and Server assemblies are installed on the headless server. Verify `HideManagerGameObject = true`. Capture the complete stack trace; `Horus.Server.dll` is validated not to reference Horus UI, IMGUI, camera/input code, or Rewired.

### Other players cannot load or see content

Ordinary clients are expected to observe native replicated content without installing Horus, but that connected scenario is still pending for this test RC. Any aircraft, ship, weapon, or asset supplied by another mod must be installed at a matching version on the server and every client that needs that content.

See [Troubleshooting](troubleshooting.md) for additional recovery steps.

## 13. Test status and reporting

### Verified for this test release

- 138 automated logic, protocol, authorization, validation, persistence, paging, audit, language, and packaging checks.
- Zero-warning Shared, Client, and Server builds against the installed official assemblies.
- Headless assembly dependency audit.
- Official Windows and Linux/WSL startup, Steam login, Mirage registration, BuiltIn and Workshop selection checks.
- Exact frozen-DLL four-hour idle headless soaks on Windows and Linux with no fatal runtime findings.
- Byte-identical repeated packaging with embedded and external SHA-256 records.

### Still pending — do not report these as passed

- An allowed GM and a denied GM exercising the complete connected command path.
- Ordinary clients observing all native replicated mutations without Horus.
- Protocol mismatch, reconnect, mission change, snapshot, and resynchronization behavior in connected gameplay.
- Two mission rotations and the complete spawn/order/edit/delete/RTS/factory matrix.
- Malformed-payload and command-burst abuse tests through a real connected client.
- Two simultaneous GMs and two concurrent legitimate Steam identities.
- Visual evidence from the GM client for the complete dedicated workflow.

Check the machine-readable [release matrix](validation/release-matrix.json) before every test. `PENDING` means unexecuted, not successful.

### Report a result

Open a [GitHub issue](https://github.com/ReapeRAlan/Horus-Mod/issues/new) and begin the title with `[v2.0.0-rc.1 TEST]`. Include:

- PASS or FAIL and the exact scenario.
- Windows or Linux server, game/server build, BepInEx version, mission type/ID, and package name.
- Package SHA-256 and Horus protocol version.
- Whether the Steam identity was allowlisted; redact the actual SteamID64.
- Exact steps, expected result, actual result, and reproducibility.
- Sanitized `LogOutput.log`, Horus audit excerpt, metrics, screenshots, or video as appropriate.

Do not upload credentials, administrator allowlists, tokens, IP addresses, or private mission data.

## 14. Update and rollback

Read [Upgrade from v1.4.3](upgrade-from-v1.4.3.md) before changing an existing installation.

To roll back:

1. Stop the game/server.
2. Back up Horus configuration and evidence.
3. Remove `BepInEx/plugins/Horus/` so no 2.0 assembly remains.
4. Reinstall the unchanged `v1.4.3` release for supported local-host use.
5. Do not rename 2.0 files to 1.4.3 names or mix assemblies from both versions.

The `v1.4.3` tag and assets remain unchanged. Any correction to this RC will be released under a new version such as `v2.0.0-rc.2`; published files are not replaced in place.

## 15. Verify downloaded files

Download `SHA256SUMS.txt` and `release-manifest.json` from the same GitHub prerelease as the ZIP. In PowerShell:

```powershell
Get-FileHash .\Horus-GM-v2.0.0-rc.1.zip -Algorithm SHA256
Get-FileHash .\Horus-Dedicated-v2.0.0-rc.1.zip -Algorithm SHA256
Get-FileHash .\Horus-Full-v2.0.0-rc.1.zip -Algorithm SHA256
```

Compare each result with `SHA256SUMS.txt`. The manifest must name version `2.0.0-rc.1`, protocol `2`, the tagged source commit, archive sizes, and the same hashes. Each ZIP also contains its own `SHA256SUMS` for all extracted files.

For exact evidence and limitations, read the [release notes](releases/v2.0.0-rc.1.md) and [release checklist](validation/release-checklist.md).
