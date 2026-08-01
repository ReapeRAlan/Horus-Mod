using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.Networking;
using Mirage;
using HorusMod.Networking;
using HorusMod.Placement;
using HorusMod.Economy;
using HorusMod.Diagnostics;
using HorusMod.Logging;
using HorusMod.UI;
using HorusMod.Data;
using HorusMod.Compat;
using HorusMod.Interaction;
using HorusMod.Loadouts;
using HorusMod.Spawning;

namespace HorusMod.Core
{
    public partial class HorusManager : MonoBehaviour
    {
        private static readonly string[] logLevelNames = { "Quiet", "Normal", "Verbose", "Trace" };
        private static readonly string[] modeNames = { "Sandbox Mode", "RTS Commander Mode" };
        private static readonly string[] gridLabels = { "1m", "5m", "10m", "25m", "50m", "100m" };
        private static readonly string[] rotationLabels = { "1°", "5°", "15°", "45°", "90°" };
        private static readonly string[] deleteLabels = { "25m", "50m", "100m" };
        private static readonly string[] liveryModeNames = { "Default", "Faction Default", "Random", "Specific" };
        private static readonly string[] loadoutModeNames = { "Default", "Standard Preset", "Random Standard Preset" };
        private static readonly LoadoutSourceKind[] loadoutSourceKinds =
        {
            LoadoutSourceKind.Default,
            LoadoutSourceKind.StandardPreset,
            LoadoutSourceKind.RandomStandardPreset,
            LoadoutSourceKind.CurrentSession,
            LoadoutSourceKind.HorusSavedPreset,
            LoadoutSourceKind.CopyCurrentAircraft,
            LoadoutSourceKind.CustomHardpoints
        };
        private static readonly string[] loadoutSourceNames =
        {
            "Default",
            "Standard preset",
            "Random standard",
            "Current session",
            "Horus saved preset",
            "Copy current aircraft",
            "Custom hardpoints"
        };
        private AircraftParameters cachedAircraftOptions;
        private string[] cachedLiveryNames;
        private string[] cachedLoadoutNames;

        internal void DrawPlaceConfiguration()
        {
            GUILayout.Space(4f);
            DrawNavalResupplyQuickAction();
            DrawDefinitionSafetyControls();
            if (ArmedDefinition is AircraftDefinition && Section("Aircraft loadout, skin & pilot", ref showAircraftCustomizationTools))
                DrawAircraftCustomizationSection();
            if (Section("Placement", ref showPlacementTools)) DrawPlacementToolsSection();
            if (Section("Groups & formations", ref showGroupTools)) DrawGroupsSection();
        }

        private void DrawNavalResupplyQuickAction()
        {
            CatalogEntry supply = FindNavalResupplyCandidate();
            if (supply == null) return;
            GUILayout.BeginHorizontal();
            if (HorusWidgets.Secondary("Spawn Naval Resupply")) SpawnNavalResupplyQuick();
            GUILayout.Label("Experimental · requires matching HQ", HorusTheme.LabelMuted);
            GUILayout.EndHorizontal();
        }

        private void DrawDefinitionSafetyControls()
        {
            if (Event.current.type == EventType.Layout)
            {
                if (pendingForceIncompatible >= 0)
                {
                    if (HorusPlugin.AllowIncompatibleContent != null)
                        HorusPlugin.AllowIncompatibleContent.Value = pendingForceIncompatible == 1;
                    pendingForceIncompatible = -1;
                }
                if (!string.IsNullOrEmpty(pendingLookupAcknowledgement))
                {
                    string acknowledgementKey = pendingLookupAcknowledgement;
                    pendingLookupAcknowledgement = null;
                    string token = HorusSpawnService.IssueIncompatibleContentAuthorization(acknowledgementKey);
                    if (!string.IsNullOrEmpty(token))
                    {
                        acknowledgedLookupDefinitions[acknowledgementKey] = token;
                        HorusToasts.Show("Lookup-only risk acknowledged for this session.");
                    }
                }
            }
            CatalogEntry entry = FindCatalogEntry(ArmedDefinition);
            if (entry == null) return;
            bool show = entry.IsLookupOnly || entry.IsLiveOrdnance || entry.IsDisabled || entry.IsEventContent || entry.HasKeyConflict;
            if (!show) return;

            GUILayout.BeginVertical(HorusTheme.Card);
            GUILayout.Label("EXPERIMENTAL CONTENT", HorusTheme.TitleText);
            if (entry.IsDisabled) GUILayout.Label("This definition is disabled by the game. Horus will still expose it as requested.", HorusTheme.LabelWrap);
            if (entry.IsEventContent) GUILayout.Label("This is event content and may depend on mission settings.", HorusTheme.LabelWrap);
            if (entry.HasKeyConflict) GUILayout.Label("Duplicate jsonKey: verify the selected prefab before spawning.", HorusTheme.LabelWrap);

            string key = CatalogIdentity(entry);
            if (entry.IsLookupOnly)
            {
                GUILayout.Label("Lookup-only definitions are not registered for network serialization and may desync or disconnect clients.", HorusTheme.LabelWrap);
                bool force = HorusPlugin.AllowIncompatibleContent != null && HorusPlugin.AllowIncompatibleContent.Value;
                bool nextForce = GUILayout.Toggle(force, " Force incompatible content");
                if (HorusPlugin.AllowIncompatibleContent != null && nextForce != force)
                    pendingForceIncompatible = nextForce ? 1 : 0;
                if (force && !acknowledgedLookupDefinitions.ContainsKey(key))
                {
                    if (HorusWidgets.Danger("Acknowledge this definition for this session"))
                    {
                        pendingLookupAcknowledgement = key;
                    }
                }
                else if (acknowledgedLookupDefinitions.ContainsKey(key))
                {
                    GUILayout.Label("Risk acknowledged for this session.", HorusTheme.LabelMuted);
                }
            }

            if (entry.IsLiveOrdnance)
            {
                GUILayout.Label("LIVE ORDNANCE · individual Sandbox spawn only", HorusTheme.LabelWrap);
                GUILayout.Label("Spawns above the clicked point (raise/lower with the altitude control) and drops straight down onto it — wherever you click is where it lands.", HorusTheme.LabelWrap);

                bool hasSingleSelection = worldSelection != null && worldSelection.Count == 1 && worldSelection.Units[0] != null;
                bool previousGuideEnabled = GUI.enabled;
                GUI.enabled = previousGuideEnabled && hasSingleSelection;
                missileGuideToSelectedTarget = GUILayout.Toggle(missileGuideToSelectedTarget && hasSingleSelection,
                    hasSingleSelection
                        ? $" Guide toward selected unit ({worldSelection.Units[0].unitName}) instead of the click point"
                        : " Guide toward selected unit (select exactly one unit first)");
                GUI.enabled = previousGuideEnabled;
                if (missileGuideToSelectedTarget && hasSingleSelection)
                    GUILayout.Label("Native guidance will steer toward that unit's actual position — it may land away from where you click, especially if the unit moves.", HorusTheme.LabelMuted);

                GUILayout.Label($"Launch speed: {missileLaunchSpeed:0} m/s");
                missileLaunchSpeed = Mathf.Round(GUILayout.HorizontalSlider(missileLaunchSpeed, 0f, 1000f) / 10f) * 10f;

                if ((entry.Flags & (CatalogFlags.Nuclear | CatalogFlags.Strategic)) != 0)
                    GUILayout.Label("Nuclear/strategic warhead: spawns live. Handle with care.", HorusTheme.LabelWrap);
            }
            GUILayout.EndVertical();
        }

        internal void DrawManageConfiguration()
        {
            GUILayout.Label($"Selection: {(WorldSelection != null ? WorldSelection.Count : 0)}", HorusTheme.TitleText);
            GUILayout.BeginHorizontal();
            GUI.enabled = WorldSelection != null && WorldSelection.HasSelection;
            if (HorusWidgets.Secondary("Focus (F)")) FocusSelection();
            if (HorusWidgets.Secondary("Duplicate (Ctrl+D)")) QueueUiAction(DuplicateSelection);
            if (HorusWidgets.Danger("Delete (Del)")) QueueUiAction(DeleteSelection);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (WorldSelection != null)
                foreach (Unit unit in WorldSelection.Units)
                    if (unit != null) GUILayout.Label($"• {unit.unitName}", HorusTheme.LabelSmall);
            GUILayout.Space(8f);
            DrawSelectedAircraftEditor();
            GUILayout.Label("DANGER ZONE", HorusTheme.TitleText);
            DrawSafeDeleteSection();
        }

        internal void DrawRtsConfiguration()
        {
            DrawStatusModeSection();
            DrawRtsCommanderUI();
            DrawRtsFactoriesUI();
        }

        internal void DrawSettingsConfiguration()
        {
            DrawSettingsSection();
        }

        internal void DrawDebugConfiguration()
        {
            DrawDebugSection();
        }

        private void DrawHorusWindow(int windowID)
        {
            HorusWindowRoot.Draw(this, windowID);
        }

        private static readonly Dictionary<string, bool> pendingSectionStates = new Dictionary<string, bool>();

        private static bool Section(string title, ref bool open)
        {
            if (Event.current.type == EventType.Layout && pendingSectionStates.TryGetValue(title, out bool pending))
            {
                open = pending;
                pendingSectionStates.Remove(title);
            }
            if (GUILayout.Button((open ? "▼ " : "▶ ") + title))
            {
                pendingSectionStates[title] = !open;
            }
            return open;
        }

        private void DrawStatusModeSection()
        {
            GUILayout.Box("══ STATUS & MODE ══");

            if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)
            {
                GUILayout.Label("Status: Not in mission (Game State is " + GameManager.gameState + ")");
            }

            if (Encyclopedia.i == null)
            {
                Color prev = GUI.color;
                GUI.color = Color.red;
                GUILayout.Label("Error: Encyclopedia not loaded yet.");
                GUI.color = prev;
                return;
            }

            // --- Permission Label ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("Permission: ", GUILayout.Width(80));
            Color c = GUI.color;
            if (HorusPermissions.IsMultiplayerClient())
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                GUILayout.Label("Client — View Only");
            }
            else if (HorusPermissions.IsMultiplayerHost())
            {
                GUI.color = new Color(0.4f, 1f, 0.4f);
                GUILayout.Label("Local Multiplayer Host");
            }
            else
            {
                GUI.color = new Color(0.4f, 1f, 0.4f);
                GUILayout.Label("Single Player");
            }
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Server: ", GUILayout.Width(80));
            GUI.color = new Color(1f, 0.6f, 0.2f);
            GUILayout.Label("Dedicated Server — Unsupported");
            GUI.color = c;
            GUILayout.EndHorizontal();

            // --- Current Mode Label ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode: ", GUILayout.Width(80));
            GUILayout.Label(HorusPermissions.GetModeLabel());
            GUILayout.EndHorizontal();

            // --- Economy Mode Selector ---
            GUILayout.Space(5);
            int oldMode = (economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander) ? 1 : 0;
            int newMode = GUILayout.SelectionGrid(oldMode, modeNames, 2);
            if (newMode != oldMode && economyManager != null)
            {
                if (newMode == 1)
                {
                    int factionCount = FactionRegistry.factions?.Count ?? 0;
                    if (selectedFactionIndex < 0 || selectedFactionIndex >= factionCount)
                    {
                        selectedFactionIndex = 0;
                        HorusToasts.Show("RTS mode selected the first playable faction");
                    }
                    economyManager.CurrentMode = HorusMode.RtsCommander;
                    economyManager.InitializeMatch();
                    HorusLog.Info("UI", "[RTS Economy] Switched to RTS Commander Mode.");
                }
                else
                {
                    economyManager.CurrentMode = HorusMode.Sandbox;
                    economyManager.ResetMatch();
                    HorusLog.Info("UI", "[RTS Economy] Switched to Sandbox Mode.");
                }
            }
            
            // Helper text
            if (newMode == 0) GUILayout.Label("Sandbox: Free spawning, no budget.", HorusTheme.LabelMuted);
            else GUILayout.Label("RTS Commander: Units cost money. Factories produce units.", HorusTheme.LabelMuted);
        }

        private void DrawPlacementToolsSection()
        {
            Vector2 altitudeRange = GetAltitudeRange();

            // Quick Actions
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Altitude")) SetSpawnAltitude(0f);
            if (GUILayout.Button("Reset Yaw")) { spawnYaw = 0f; yawInputText = "0"; }
            if (GUILayout.Button("Reset Window")) windowRect = HorusPrefs.ResetWindow();
            GUILayout.EndHorizontal();

            // Altitude & Yaw sliders
            GUILayout.Space(5);
            GUILayout.Label($"Altitude: {spawnAltitude:F0} m  |  Yaw: {spawnYaw:F0}°");

            GUILayout.Label($"Native range: {altitudeRange.x:F0}–{altitudeRange.y:F0} m", HorusTheme.LabelMuted);
            float newAlt = GUILayout.HorizontalSlider(spawnAltitude, altitudeRange.x, altitudeRange.y);
            if (Mathf.Abs(newAlt - spawnAltitude) > 0.01f)
            {
                SetSpawnAltitude(Mathf.Round(newAlt / 50f) * 50f);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Alt:", GUILayout.Width(30));
            altitudeInputText = GUILayout.TextField(altitudeInputText, GUILayout.Width(70));
            if (GUILayout.Button("Set", GUILayout.Width(35)))
            {
                if (float.TryParse(altitudeInputText, out float parsed))
                {
                    SetSpawnAltitude(parsed);
                }
            }
            GUILayout.Label("Yaw:", GUILayout.Width(30));
            yawInputText = GUILayout.TextField(yawInputText, GUILayout.Width(50));
            if (GUILayout.Button("Set", GUILayout.Width(35)))
            {
                if (float.TryParse(yawInputText, out float parsed))
                {
                    spawnYaw = ApplyRotationSnap(NormalizeAngle(parsed));
                    yawInputText = spawnYaw.ToString("0");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0m")) SetSpawnAltitude(0f);
            if (GUILayout.Button("1k")) SetSpawnAltitude(1000f);
            if (GUILayout.Button("3k")) SetSpawnAltitude(3000f);
            if (GUILayout.Button("5k")) SetSpawnAltitude(5000f);
            GUILayout.EndHorizontal();

            GUILayout.Space(3);
            float newYaw = GUILayout.HorizontalSlider(spawnYaw, 0f, 360f);
            if (Mathf.Abs(newYaw - spawnYaw) > 0.01f)
            {
                spawnYaw = ApplyRotationSnap(newYaw);
                yawInputText = spawnYaw.ToString("0");
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0°")) { spawnYaw = 0f; yawInputText = "0"; }
            if (GUILayout.Button("45°")) { spawnYaw = 45f; yawInputText = "45"; }
            if (GUILayout.Button("90°")) { spawnYaw = 90f; yawInputText = "90"; }
            if (GUILayout.Button("180°")) { spawnYaw = 180f; yawInputText = "180"; }
            if (GUILayout.Button("270°")) { spawnYaw = 270f; yawInputText = "270"; }
            GUILayout.EndHorizontal();

            // Toggles
            GUILayout.Space(5);
            ghostPreviewEnabled = GUILayout.Toggle(ghostPreviewEnabled, " Ghost Preview");
            GUILayout.Label("Shows where the unit will spawn before placing it.", HorusTheme.LabelMuted);
            
            bool prevStationary = spawnStationary;
            spawnStationary = GUILayout.Toggle(spawnStationary, " Spawn Ground Units Stationary");
            if (spawnStationary != prevStationary) HorusPlugin.SpawnGroundUnitsStationary.Value = spawnStationary;
            GUILayout.Label("Ground units and ships will hold position after spawning.", HorusTheme.LabelMuted);

            snapToGround = GUILayout.Toggle(snapToGround, " Snap to Ground");
            alignToSurface = GUILayout.Toggle(alignToSurface, " Align to Surface Normal");
            autoOceanSnapForShips = GUILayout.Toggle(autoOceanSnapForShips, " Auto Ocean Snap for Ships");
            oceanSnapActive = GUILayout.Toggle(oceanSnapActive, " Snap to Ocean Level");
            
            GUILayout.Space(3);
            gridSnapEnabled = GUILayout.Toggle(gridSnapEnabled, " Grid Snap");
            if (gridSnapEnabled)
            {
                int gi = IndexOf(gridSizeOptions, gridSize);
                int newGi = GUILayout.SelectionGrid(gi < 0 ? 2 : gi, gridLabels, 3);
                if (newGi != gi && newGi >= 0 && newGi < gridSizeOptions.Length)
                {
                    gridSize = gridSizeOptions[newGi];
                    gridSizeInputText = gridSize.ToString("0");
                }
            }

            GUILayout.Space(3);
            rotationSnapEnabled = GUILayout.Toggle(rotationSnapEnabled, " Rotation Snap");
            if (rotationSnapEnabled)
            {
                int ri = IndexOf(rotationSnapOptions, rotationSnapStep);
                int newRi = GUILayout.SelectionGrid(ri < 0 ? 2 : ri, rotationLabels, 5);
                if (newRi != ri && newRi >= 0 && newRi < rotationSnapOptions.Length)
                {
                    rotationSnapStep = rotationSnapOptions[newRi];
                    spawnYaw = ApplyRotationSnap(spawnYaw);
                    yawInputText = spawnYaw.ToString("0");
                }
            }
        }

        private void DrawGroupsSection()
        {
            if (Event.current.type == EventType.Layout) ApplyPendingGroupUiActions();

            bool nextGroup = GUILayout.Toggle(enableGroupSpawn, " Enable Group Spawning");
            if (nextGroup != enableGroupSpawn)
            {
                pendingGroupEnabled = nextGroup ? 1 : 0;
            }

            GUILayout.Label("Spawns multiple units. Disabled by default.", HorusTheme.LabelMuted);

            if (!enableGroupSpawn) return;

            GUILayout.Label("Preset Group:");
            string[] presetNames = GetGroupPresetNames();
            int nextPreset = GUILayout.SelectionGrid(selectedGroupPresetIndex, presetNames, 2);
            if (nextPreset != selectedGroupPresetIndex) pendingGroupPresetIndex = nextPreset;

            int customPresetIndex = presetNames.Length - 1;
            if (selectedGroupPresetIndex == customPresetIndex)
            {
                GUILayout.Space(5);
                GUILayout.Box("CUSTOM GROUP EDITOR");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Group Name:", GUILayout.Width(80));
                customGroupName = GUILayout.TextField(customGroupName, GUILayout.Width(150));
                GUILayout.EndHorizontal();

                UnitDefinition currentSelected = GetSelectedDefinition();
                if (currentSelected != null && GUILayout.Button($"Add Selected Unit ({currentSelected.unitName})"))
                {
                    if (FindCatalogEntry(currentSelected)?.IsLiveOrdnance == true || currentSelected is MissileDefinition)
                        HorusToasts.Show("Live ordnance is individual-only and cannot join groups");
                    else
                        pendingCustomGroupAdd = currentSelected;
                }

                GUILayout.Label($"Units in custom group: ({customGroupUnits.Count})");
                for (int i = 0; i < customGroupUnits.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{i + 1}. {customGroupUnits[i].unitName}");
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        pendingCustomGroupRemoveIndex = i;
                        break;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save Group")) pendingCustomGroupSave = customGroupName;
                if (GUILayout.Button("Clear Group"))
                {
                    pendingCustomGroupClear = true;
                }
                GUILayout.EndHorizontal();

                if (savedCustomGroupNames.Count > 0)
                {
                    if (selectedSavedGroupIndex >= savedCustomGroupNames.Count) selectedSavedGroupIndex = 0;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Saved Group:");
                    if (GUILayout.Button("<", GUILayout.Width(25))) selectedSavedGroupIndex = (selectedSavedGroupIndex - 1 + savedCustomGroupNames.Count) % savedCustomGroupNames.Count;
                    GUILayout.Label(savedCustomGroupNames[selectedSavedGroupIndex], GUILayout.ExpandWidth(true));
                    if (GUILayout.Button(">", GUILayout.Width(25))) selectedSavedGroupIndex = (selectedSavedGroupIndex + 1) % savedCustomGroupNames.Count;
                    GUILayout.EndHorizontal();
                    
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Load Selected"))
                    {
                        pendingCustomGroupLoad = savedCustomGroupNames[selectedSavedGroupIndex];
                    }
                    if (GUILayout.Button("Delete Selected"))
                    {
                        pendingCustomGroupDelete = savedCustomGroupNames[selectedSavedGroupIndex];
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                if (TryGetSelectedConvoy(out Faction.ConvoyGroup convoy))
                {
                    GUILayout.Label($"Native faction preset · {groupCount} units · {convoy.GetCost():N0} credits", HorusTheme.LabelMuted);
                    if (convoy.Constituents != null)
                    {
                        foreach (Faction.ConvoyUnit constituent in convoy.Constituents)
                        {
                            if (constituent?.Type != null)
                                GUILayout.Label($"{constituent.Count}× {constituent.Type.unitName}", HorusTheme.LabelSmall);
                        }
                    }
                }
                else
                {
                    GUILayout.Label($"Unit Count: {groupCount}");
                    groupCount = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(groupCount, 1f, 20f)), 1, 20);
                }

                GUILayout.Label($"Spacing: {groupSpacing:F0}m");
                float newSp = GUILayout.HorizontalSlider(groupSpacing, 5f, 200f);
                if (Mathf.Abs(newSp - groupSpacing) > 0.01f)
                {
                    groupSpacing = Mathf.Round(newSp / 5f) * 5f;
                    groupSpacingInputText = groupSpacing.ToString("0");
                    ghost.Clear();
                }
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("Custom Spacing:", GUILayout.Width(110));
                groupSpacingInputText = GUILayout.TextField(groupSpacingInputText, GUILayout.Width(60));
                if (GUILayout.Button("Set", GUILayout.Width(40)))
                {
                    if (float.TryParse(groupSpacingInputText, out float sv) && sv >= 5f)
                    {
                        groupSpacing = sv;
                        ghost.Clear();
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Label("Formation:");
                int oldForm = selectedFormationIndex;
                selectedFormationIndex = GUILayout.SelectionGrid(selectedFormationIndex, formationNames, 3);
                if (oldForm != selectedFormationIndex) ghost.Clear();
            }
        }

        private void DrawSafeDeleteSection()
        {
            HorusPlugin.AllowDeletingNonHorusUnits.Value = GUILayout.Toggle(HorusPlugin.AllowDeletingNonHorusUnits.Value, " Allow Deleting Non-Horus Units");
            if (HorusPlugin.AllowDeletingOriginalMissionUnits != null)
            {
                HorusPlugin.AllowDeletingOriginalMissionUnits.Value = GUILayout.Toggle(HorusPlugin.AllowDeletingOriginalMissionUnits.Value, " Allow Deleting Original Mission Units");
            }
            
            GUILayout.Space(5);
            Color deleteHintColor = GUI.contentColor;
            GUI.contentColor = HorusTheme.Danger;
            GUILayout.Label("Middle-click a unit to delete it.", HorusTheme.LabelMuted);
            GUI.contentColor = deleteHintColor;
            GUILayout.Label($"Delete Search Range: {deleteRange:F0}m");
            
            int di = IndexOf(deleteRangeOptions, deleteRange);
            int newDi = GUILayout.SelectionGrid(di < 0 ? 1 : di, deleteLabels, 3);
            if (newDi != di && newDi >= 0 && newDi < deleteRangeOptions.Length)
            {
                deleteRange = deleteRangeOptions[newDi];
                deleteRangeInputText = deleteRange.ToString("0");
                HorusPlugin.DeleteRange.Value = deleteRange;
            }
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Custom:", GUILayout.Width(55));
            deleteRangeInputText = GUILayout.TextField(deleteRangeInputText, GUILayout.Width(60));
            if (GUILayout.Button("Set", GUILayout.Width(40)))
            {
                if (float.TryParse(deleteRangeInputText, out float dv) && dv > 0f)
                {
                    deleteRange = dv;
                    HorusPlugin.DeleteRange.Value = deleteRange;
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawAircraftCustomizationSection()
        {
            AircraftDefinition definition = ArmedDefinition as AircraftDefinition;
            if (definition == null)
            {
                GUILayout.Label("Select an aircraft to configure the next spawn.", HorusTheme.LabelMuted);
                return;
            }

            AircraftEditorUiState state = GetAircraftEditorState(definition, manage: false);
            if (Event.current.type == EventType.Layout) ApplyPendingPresetAction(definition, state);
            GUILayout.Label("NEXT AIRCRAFT SPAWN", HorusTheme.TitleText);
            GUILayout.Label("State is stored separately for each aircraft model.", HorusTheme.LabelMuted);
            DrawLoadoutSourceEditor(definition, GetHQSafe(selectedFactionIndex), state, FindSelectedAircraft(definition));
            DrawHardpointAndPresetEditor(definition, GetHQSafe(selectedFactionIndex), state);

            AircraftParameters parameters = definition.aircraftParameters;
            if (parameters?.liveries != null && parameters.liveries.Count > 0)
            {
                GUILayout.Space(5f);
                GUILayout.Label("Livery source");
                if (Event.current.type == EventType.Layout && state.PendingLiveryMode >= 0)
                {
                    state.LiveryMode = (AircraftLiveryMode)state.PendingLiveryMode;
                    state.PendingLiveryMode = -1;
                }
                int liveryMode = GUILayout.SelectionGrid((int)state.LiveryMode, liveryModeNames, 2);
                if (liveryMode != (int)state.LiveryMode) state.PendingLiveryMode = liveryMode;
                if (state.LiveryMode == AircraftLiveryMode.Specific)
                {
                    string[] names = GetLiveryNames(parameters);
                    state.LiveryIndex = Mathf.Clamp(state.LiveryIndex, 0, names.Length - 1);
                    state.LiveryIndex = GUILayout.SelectionGrid(state.LiveryIndex, names, 1);
                }
            }
            else
            {
                state.LiveryMode = AircraftLiveryMode.Default;
                GUILayout.Label("This aircraft exposes no alternate liveries.", HorusTheme.LabelMuted);
            }

            GUILayout.Space(5f);
            GUILayout.Label($"Pilot skill: {state.Skill:P0}");
            state.Skill = GUILayout.HorizontalSlider(state.Skill, 0f, 1f);
            state.ApplyToGroups = GUILayout.Toggle(state.ApplyToGroups, " Apply aircraft customization to group spawns");
            if (!string.IsNullOrEmpty(state.Status)) GUILayout.Label(state.Status, HorusTheme.LabelMuted);
        }

        private void DrawSelectedAircraftEditor()
        {
            if (WorldSelection == null || !WorldSelection.HasSelection) return;

            Aircraft first = null;
            bool containsFixedArmamentUnit = false;
            for (int i = 0; i < WorldSelection.Units.Count; i++)
            {
                Unit unit = WorldSelection.Units[i];
                if (unit is Aircraft aircraft)
                {
                    if (first == null) first = aircraft;
                    else if (!AreCompatibleAircraftModels(
                                 first.definition as AircraftDefinition,
                                 aircraft.definition as AircraftDefinition,
                                 out string incompatibilityReason))
                    {
                        DrawMixedAircraftSelectionMessage(incompatibilityReason);
                        return;
                    }
                }
                else if (unit != null)
                {
                    containsFixedArmamentUnit = true;
                }
            }

            if (first == null)
            {
                if (containsFixedArmamentUnit)
                {
                    GUILayout.Space(8f);
                    GUILayout.Label("ARMAMENT", HorusTheme.TitleText);
                    GUILayout.Label("Ground vehicles and ships use fixed prefab armament. They can be resupplied, but do not expose interchangeable aircraft loadouts.", HorusTheme.LabelWrap);
                }
                return;
            }
            if (containsFixedArmamentUnit)
            {
                DrawMixedAircraftSelectionMessage();
                return;
            }

            AircraftDefinition definition = first.definition as AircraftDefinition;
            if (definition == null) return;
            AircraftEditorUiState state = GetAircraftEditorState(definition, manage: true);
            if (Event.current.type == EventType.Layout) ApplyPendingPresetAction(definition, state);

            GUILayout.Space(8f);
            GUILayout.Label("SELECTED AIRCRAFT LOADOUT & SKIN", HorusTheme.TitleText);
            GUILayout.Label($"Editing {WorldSelection.Count} aircraft of the same model.", HorusTheme.LabelMuted);
            DrawLoadoutSourceEditor(definition, first.NetworkHQ, state, first);
            DrawHardpointAndPresetEditor(definition, first.NetworkHQ, state);

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && HorusPermissions.CanSpawn();
            if (GUILayout.Button("Apply loadout to selected aircraft"))
            {
                int applied = 0;
                string error = null;
                foreach (Unit unit in WorldSelection.Units)
                {
                    Aircraft aircraft = unit as Aircraft;
                    if (aircraft == null) continue;
                    LoadoutApplyResult result = ApplyEditorLoadout(aircraft, state);
                    if (result.Success) applied++;
                    else if (error == null) error = result.Message;
                }
                state.Status = error == null
                    ? $"Loadout applied to {applied} aircraft."
                    : $"Applied to {applied}; first failure: {error}";
                HorusToasts.Show(state.Status);
            }

            AircraftParameters parameters = definition.aircraftParameters;
            if (parameters?.liveries != null && parameters.liveries.Count > 0)
            {
                GUILayout.Space(5f);
                string[] names = GetLiveryNames(parameters);
                state.LiveryIndex = Mathf.Clamp(state.LiveryIndex, 0, names.Length - 1);
                GUILayout.Label("Skin / livery");
                state.LiveryIndex = GUILayout.SelectionGrid(state.LiveryIndex, names, 1);
                if (GUILayout.Button($"Apply skin: {names[state.LiveryIndex]}"))
                {
                    int changed = 0;
                    foreach (Unit unit in WorldSelection.Units)
                        if (HorusUnitEditor.TrySetLivery((Aircraft)unit, state.LiveryIndex)) changed++;
                    HorusToasts.Show($"Skin applied to {changed} aircraft");
                }
            }
            else
            {
                GUILayout.Label("This aircraft exposes no alternate liveries.", HorusTheme.LabelMuted);
            }

            GUILayout.Space(5f);
            GUILayout.Label($"Pilot skill: {state.Skill:P0}");
            state.Skill = GUILayout.HorizontalSlider(state.Skill, 0f, 1f);
            if (GUILayout.Button("Apply pilot skill"))
            {
                foreach (Unit unit in WorldSelection.Units) HorusUnitEditor.SetSkill(unit, state.Skill);
                HorusToasts.Show($"Pilot skill applied to {WorldSelection.Count} aircraft");
            }
            GUI.enabled = previousEnabled;
            if (!HorusPermissions.CanSpawn()) GUILayout.Label("Host only: editing is disabled for multiplayer clients.", HorusTheme.LabelMuted);
            if (!string.IsNullOrEmpty(state.Status)) GUILayout.Label(state.Status, HorusTheme.LabelWrap);
        }

        private static void DrawMixedAircraftSelectionMessage(string reason = null)
        {
            GUILayout.Space(8f);
            GUILayout.Label("AIRCRAFT LOADOUT & SKIN", HorusTheme.TitleText);
            GUILayout.Label("Select only aircraft of the same model to edit them together.", HorusTheme.LabelMuted);
            if (!string.IsNullOrWhiteSpace(reason)) GUILayout.Label(reason, HorusTheme.LabelWrap);
        }

        private Aircraft FindSelectedAircraft(AircraftDefinition definition)
        {
            if (WorldSelection == null) return null;
            for (int i = 0; i < WorldSelection.Units.Count; i++)
                if (WorldSelection.Units[i] is Aircraft aircraft &&
                    AreCompatibleAircraftModels(definition, aircraft.definition as AircraftDefinition, out _)) return aircraft;
            return null;
        }

        private static bool AreCompatibleAircraftModels(
            AircraftDefinition first,
            AircraftDefinition second,
            out string reason)
        {
            reason = null;
            if (first == null || second == null)
            {
                reason = "At least one selected aircraft has no AircraftDefinition metadata.";
                return false;
            }
            if (ReferenceEquals(first, second)) return true;
            if (string.IsNullOrWhiteSpace(first.jsonKey) ||
                !string.Equals(first.jsonKey, second.jsonKey, StringComparison.OrdinalIgnoreCase))
            {
                reason = "The selection contains different aircraft jsonKeys.";
                return false;
            }
            if (UnitCatalog.FindAll(first.jsonKey).Count > 1)
            {
                reason = "This aircraft jsonKey is duplicated in the catalog, so the model is ambiguous.";
                return false;
            }

            HardpointSet[] firstSets = first.unitPrefab != null
                ? first.unitPrefab.GetComponent<Aircraft>()?.weaponManager?.hardpointSets
                : null;
            HardpointSet[] secondSets = second.unitPrefab != null
                ? second.unitPrefab.GetComponent<Aircraft>()?.weaponManager?.hardpointSets
                : null;
            firstSets ??= Array.Empty<HardpointSet>();
            secondSets ??= Array.Empty<HardpointSet>();
            if (firstSets.Length != secondSets.Length)
            {
                reason = "Aircraft with the same jsonKey expose different hardpoint counts.";
                return false;
            }
            for (int i = 0; i < firstSets.Length; i++)
            {
                var firstKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var secondKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (firstSets[i]?.weaponOptions != null)
                    foreach (WeaponMount mount in firstSets[i].weaponOptions)
                        if (mount != null && !string.IsNullOrWhiteSpace(mount.jsonKey)) firstKeys.Add(mount.jsonKey);
                if (secondSets[i]?.weaponOptions != null)
                    foreach (WeaponMount mount in secondSets[i].weaponOptions)
                        if (mount != null && !string.IsNullOrWhiteSpace(mount.jsonKey)) secondKeys.Add(mount.jsonKey);
                if (!firstKeys.SetEquals(secondKeys))
                {
                    reason = $"Aircraft with the same jsonKey expose incompatible options on hardpoint {i + 1}.";
                    return false;
                }
            }
            return true;
        }

        private void DrawLoadoutSourceEditor(AircraftDefinition definition, FactionHQ hq, AircraftEditorUiState state, Aircraft copySource)
        {
            GUILayout.Space(5f);
            GUILayout.Label("Loadout source");
            if (Event.current.type == EventType.Layout && state.PendingLoadoutSourceIndex >= 0)
            {
                int pending = Mathf.Clamp(state.PendingLoadoutSourceIndex, 0, loadoutSourceKinds.Length - 1);
                state.PendingLoadoutSourceIndex = -1;
                SelectLoadoutSource(definition, hq, state, loadoutSourceKinds[pending], copySource);
            }
            int sourceIndex = SourceIndex(state.LoadoutSource);
            int nextSourceIndex = GUILayout.SelectionGrid(sourceIndex, loadoutSourceNames, 2);
            if (nextSourceIndex != sourceIndex)
                state.PendingLoadoutSourceIndex = nextSourceIndex;

            if (state.LoadoutSource == LoadoutSourceKind.StandardPreset)
            {
                IReadOnlyList<LoadoutDraft> standards = HorusLoadoutService.GetValidStandardDrafts(definition, hq);
                if (standards.Count == 0)
                {
                    GUILayout.Label("No valid standard presets exist for this aircraft and faction/HQ.", HorusTheme.LabelMuted);
                }
                else
                {
                    string[] names = new string[standards.Count];
                    for (int i = 0; i < standards.Count; i++) names[i] = standards[i].Name;
                    state.StandardIndex = Mathf.Clamp(state.StandardIndex, 0, standards.Count - 1);
                    int next = GUILayout.SelectionGrid(state.StandardIndex, names, 1);
                    LoadoutDraft chosen = standards[next];
                    if (next != state.StandardIndex || state.Draft == null ||
                        state.Draft.Source != LoadoutSourceKind.StandardPreset ||
                        !string.Equals(state.Draft.SourceId, chosen.SourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        state.StandardIndex = next;
                        state.Draft = chosen.Clone();
                    }
                }
            }
            else if (state.LoadoutSource == LoadoutSourceKind.HorusSavedPreset)
            {
                IReadOnlyList<HorusLoadoutPreset> presets = HorusLoadoutPresetStore.GetPresets(definition.jsonKey);
                if (presets.Count == 0)
                {
                    GUILayout.Label("No named Horus presets have been saved for this aircraft.", HorusTheme.LabelMuted);
                }
                else
                {
                    string[] names = new string[presets.Count];
                    for (int i = 0; i < presets.Count; i++) names[i] = presets[i].Name;
                    state.SavedPresetIndex = Mathf.Clamp(state.SavedPresetIndex, 0, presets.Count - 1);
                    int next = GUILayout.SelectionGrid(state.SavedPresetIndex, names, 1);
                    HorusLoadoutPreset selectedPreset = presets[next];
                    if (next != state.SavedPresetIndex || !string.Equals(state.SavedPresetId, selectedPreset.PresetId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        state.SavedPresetIndex = next;
                        state.SavedPresetId = selectedPreset.PresetId;
                        if (!HorusLoadoutPresetStore.TryCreateDraft(definition, selectedPreset.PresetId, out state.Draft, out string error))
                            state.Status = error;
                        else
                            state.PresetName = selectedPreset.Name;
                    }
                }
            }
            else if (state.LoadoutSource == LoadoutSourceKind.CurrentSession)
            {
                GUILayout.Label("Reads GameManager.aircraftCustomization for this mission only.", HorusTheme.LabelMuted);
                if (GUILayout.Button("Reload current-session customization"))
                {
                    if (!HorusLoadoutService.TryCreateSessionDraft(definition, out state.Draft, out string error)) state.Status = error;
                    else state.Status = "Current-session customization loaded.";
                }
            }
            else if (state.LoadoutSource == LoadoutSourceKind.CopyCurrentAircraft)
            {
                if (copySource == null)
                {
                    GUILayout.Label("Select an existing aircraft of this model to copy its loadout.", HorusTheme.LabelMuted);
                }
                else if (GUILayout.Button("Copy loadout from selected aircraft"))
                {
                    if (!HorusLoadoutService.TryCreateCurrentAircraftDraft(copySource, out state.Draft, out string error)) state.Status = error;
                    else state.Status = "Selected aircraft loadout copied.";
                }
            }

            if (state.LoadoutSource != LoadoutSourceKind.CustomHardpoints && state.LoadoutSource != LoadoutSourceKind.RandomStandardPreset)
            {
                if (GUILayout.Button("Customize these hardpoints"))
                {
                    state.PendingLoadoutSourceIndex = SourceIndex(LoadoutSourceKind.CustomHardpoints);
                }
            }
        }

        private void ApplyPendingGroupUiActions()
        {
            if (pendingGroupEnabled >= 0)
            {
                enableGroupSpawn = pendingGroupEnabled == 1;
                pendingGroupEnabled = -1;
                HorusPlugin.EnableGroupSpawn.Value = enableGroupSpawn;
                ghost.Clear();
            }
            if (pendingGroupPresetIndex >= 0)
            {
                int oldPreset = selectedGroupPresetIndex;
                selectedGroupPresetIndex = pendingGroupPresetIndex;
                pendingGroupPresetIndex = -1;
                OnGroupPresetChanged(oldPreset, selectedGroupPresetIndex);
            }
            if (pendingCustomGroupAdd != null)
            {
                customGroupUnits.Add(pendingCustomGroupAdd);
                pendingCustomGroupAdd = null;
                groupCount = customGroupUnits.Count;
                ghost.Clear();
            }
            if (pendingCustomGroupRemoveIndex >= 0)
            {
                if (pendingCustomGroupRemoveIndex < customGroupUnits.Count)
                    customGroupUnits.RemoveAt(pendingCustomGroupRemoveIndex);
                pendingCustomGroupRemoveIndex = -1;
                groupCount = customGroupUnits.Count;
                ghost.Clear();
            }
            if (pendingCustomGroupClear)
            {
                pendingCustomGroupClear = false;
                customGroupUnits.Clear();
                groupCount = 0;
                ghost.Clear();
            }
            if (pendingCustomGroupSave != null)
            {
                string name = pendingCustomGroupSave;
                pendingCustomGroupSave = null;
                SaveCustomGroup(name);
            }
            if (pendingCustomGroupLoad != null)
            {
                string name = pendingCustomGroupLoad;
                pendingCustomGroupLoad = null;
                LoadCustomGroup(name);
                customGroupName = name;
                ghost.Clear();
            }
            if (pendingCustomGroupDelete != null)
            {
                string name = pendingCustomGroupDelete;
                pendingCustomGroupDelete = null;
                DeleteCustomGroupFile(name);
                RefreshSavedCustomGroups();
                if (selectedSavedGroupIndex >= savedCustomGroupNames.Count) selectedSavedGroupIndex = 0;
            }
        }

        private void SelectLoadoutSource(AircraftDefinition definition, FactionHQ hq, AircraftEditorUiState state, LoadoutSourceKind source, Aircraft copySource)
        {
            state.LoadoutSource = source;
            state.Status = "";
            if (source != LoadoutSourceKind.HorusSavedPreset && source != LoadoutSourceKind.CustomHardpoints)
                state.SavedPresetId = "";
            switch (source)
            {
                case LoadoutSourceKind.Default:
                    state.Draft = HorusLoadoutService.CreateDefaultDraft(definition);
                    break;
                case LoadoutSourceKind.StandardPreset:
                    IReadOnlyList<LoadoutDraft> standards = HorusLoadoutService.GetValidStandardDrafts(definition, hq);
                    state.StandardIndex = 0;
                    state.Draft = standards.Count > 0 ? standards[0].Clone() : HorusLoadoutService.CreateDefaultDraft(definition);
                    if (standards.Count == 0) state.Status = "No valid standard presets are exposed.";
                    break;
                case LoadoutSourceKind.RandomStandardPreset:
                    IReadOnlyList<LoadoutDraft> randomCandidates = HorusLoadoutService.GetValidStandardDrafts(definition, hq);
                    state.Draft = randomCandidates.Count > 0
                        ? randomCandidates[UnityEngine.Random.Range(0, randomCandidates.Count)].Clone()
                        : HorusLoadoutService.CreateDefaultDraft(definition);
                    if (state.Draft != null) state.Draft.Source = LoadoutSourceKind.RandomStandardPreset;
                    if (randomCandidates.Count == 0) state.Status = "No valid standard presets are exposed.";
                    break;
                case LoadoutSourceKind.CurrentSession:
                    if (!HorusLoadoutService.TryCreateSessionDraft(definition, out state.Draft, out string sessionError))
                    {
                        state.Draft = HorusLoadoutService.CreateDefaultDraft(definition);
                        state.Status = sessionError;
                    }
                    break;
                case LoadoutSourceKind.HorusSavedPreset:
                    IReadOnlyList<HorusLoadoutPreset> saved = HorusLoadoutPresetStore.GetPresets(definition.jsonKey);
                    if (saved.Count > 0)
                    {
                        state.SavedPresetIndex = 0;
                        state.SavedPresetId = saved[0].PresetId;
                        state.PresetName = saved[0].Name;
                        if (!HorusLoadoutPresetStore.TryCreateDraft(definition, saved[0].PresetId, out state.Draft, out string savedError)) state.Status = savedError;
                    }
                    else
                    {
                        state.Draft = HorusLoadoutService.CreateDefaultDraft(definition);
                        state.Status = "No named Horus presets exist for this aircraft.";
                    }
                    break;
                case LoadoutSourceKind.CopyCurrentAircraft:
                    if (copySource == null)
                    {
                        state.Draft = HorusLoadoutService.CreateDefaultDraft(definition);
                        state.Status = "Select an existing aircraft of this model first.";
                    }
                    else if (!HorusLoadoutService.TryCreateCurrentAircraftDraft(copySource, out state.Draft, out string copyError))
                    {
                        state.Draft = HorusLoadoutService.CreateDefaultDraft(definition);
                        state.Status = copyError;
                    }
                    break;
                case LoadoutSourceKind.CustomHardpoints:
                    state.Draft = state.Draft?.Clone() ?? HorusLoadoutService.CreateCustomDraft(definition);
                    state.Draft.Source = LoadoutSourceKind.CustomHardpoints;
                    state.ShowHardpoints = true;
                    break;
            }
        }

        private void DrawHardpointAndPresetEditor(AircraftDefinition definition, FactionHQ hq, AircraftEditorUiState state)
        {
            if (state.Draft == null)
            {
                GUILayout.Label("No loadout data is available for this source.", HorusTheme.LabelMuted);
                return;
            }

            if (state.LoadoutSource == LoadoutSourceKind.CustomHardpoints)
            {
                state.ShowHardpoints = true;
            }
            else
            {
                if (Event.current.type == EventType.Layout && state.PendingShowHardpoints >= 0)
                {
                    state.ShowHardpoints = state.PendingShowHardpoints == 1;
                    state.PendingShowHardpoints = -1;
                }
                if (GUILayout.Button((state.ShowHardpoints ? "Hide" : "Inspect") + " hardpoints"))
                    state.PendingShowHardpoints = state.ShowHardpoints ? 0 : 1;
            }

            if (state.ShowHardpoints)
            {
                WeaponManager manager = definition.unitPrefab != null ? definition.unitPrefab.GetComponent<Aircraft>()?.weaponManager : null;
                HardpointSet[] sets = manager?.hardpointSets;
                if (sets == null || sets.Length == 0)
                {
                    GUILayout.Label("This aircraft exposes no editable hardpoint sets.", HorusTheme.LabelMuted);
                }
                else
                {
                    state.MirrorSymmetry = GUILayout.Toggle(state.MirrorSymmetry, " Mirror linked symmetry hardpoints");
                    GUILayout.Label("One hardpoint set may represent multiple physical pylons.", HorusTheme.LabelMuted);
                    for (int i = 0; i < sets.Length; i++) DrawHardpointRow(definition, hq, state, sets[i], i);
                }
            }

            if (state.LoadoutSource != LoadoutSourceKind.RandomStandardPreset)
                DrawLoadoutValidationPreview(definition, hq, state.Draft);

            GUILayout.Space(5f);
            if (state.LoadoutSource == LoadoutSourceKind.RandomStandardPreset)
                GUILayout.Label("Random is resolved independently at spawn. Choose Custom Hardpoints before saving a deterministic preset.", HorusTheme.LabelMuted);
            bool ambiguousAircraftKey = UnitCatalog.FindAll(definition.jsonKey).Count > 1;
            if (ambiguousAircraftKey)
                GUILayout.Label("Duplicate aircraft jsonKey: named preset writes are disabled because the persistent key is ambiguous.", HorusTheme.LabelMuted);
            state.PresetName = GUILayout.TextField(state.PresetName ?? "New preset");
            GUILayout.BeginHorizontal();
            bool previous = GUI.enabled;
            GUI.enabled = previous && !ambiguousAircraftKey && state.LoadoutSource != LoadoutSourceKind.RandomStandardPreset;
            if (GUILayout.Button("Save as new"))
                QueuePresetAction(state, PresetUiAction.SaveNew);
            GUI.enabled = previous && !ambiguousAircraftKey && state.LoadoutSource != LoadoutSourceKind.RandomStandardPreset &&
                !string.IsNullOrWhiteSpace(state.SavedPresetId);
            if (GUILayout.Button("Update selected"))
                QueuePresetAction(state, PresetUiAction.Update);
            GUI.enabled = previous;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = previous && !ambiguousAircraftKey && !string.IsNullOrWhiteSpace(state.SavedPresetId);
            if (GUILayout.Button("Rename")) QueuePresetAction(state, PresetUiAction.Rename);
            if (GUILayout.Button("Duplicate")) QueuePresetAction(state, PresetUiAction.Duplicate);
            if (HorusWidgets.Danger("Delete")) QueuePresetAction(state, PresetUiAction.Delete);
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Reload presets from disk"))
                QueuePresetAction(state, PresetUiAction.Reload);
        }

        /// <summary>
        /// Live preview of whether the current draft would actually be accepted at spawn.
        /// Hardpoint conflicts are already prevented while editing, but this also catches
        /// HQ/mission-level rejections the per-hardpoint UI can't see (e.g. an HQ that
        /// can't supply a chosen warhead), so a loadout doesn't silently fall back to the
        /// aircraft's default with no explanation at spawn time.
        /// </summary>
        private static void DrawLoadoutValidationPreview(AircraftDefinition definition, FactionHQ hq, LoadoutDraft draft)
        {
            if (draft == null) return;
            LoadoutApplyResult result = HorusLoadoutService.ResolveForSpawn(definition, hq, draft);
            if (result.Success) return;

            Color previous = GUI.color;
            GUI.color = HorusTheme.Danger;
            GUILayout.Label("This loadout will be rejected at spawn and replaced with the default:", HorusTheme.LabelWrap);
            int shown = 0;
            foreach (LoadoutValidationIssue issue in result.Issues)
            {
                if (issue.Severity != LoadoutIssueSeverity.Error) continue;
                GUILayout.Label("• " + issue, HorusTheme.LabelWrap);
                if (++shown >= 4) break;
            }
            if (shown == 0) GUILayout.Label("• " + result.Message, HorusTheme.LabelWrap);
            GUI.color = previous;
        }

        private static void QueuePresetAction(AircraftEditorUiState state, PresetUiAction action)
        {
            state.PendingPresetAction = action;
            state.PendingPresetDraft = state.Draft?.Clone();
            state.PendingPresetId = state.SavedPresetId;
            state.PendingPresetName = state.PresetName;
        }

        private static void ApplyPendingPresetAction(AircraftDefinition definition, AircraftEditorUiState state)
        {
            PresetUiAction action = state.PendingPresetAction;
            if (action == PresetUiAction.None) return;
            LoadoutDraft draft = state.PendingPresetDraft;
            string presetId = state.PendingPresetId;
            string presetName = state.PendingPresetName;
            state.PendingPresetAction = PresetUiAction.None;
            state.PendingPresetDraft = null;
            state.PendingPresetId = null;
            state.PendingPresetName = null;

            string error;
            HorusLoadoutPreset saved;
            switch (action)
            {
                case PresetUiAction.SaveNew:
                    if (!HorusLoadoutService.CanPersistAsHorusPreset(definition, draft, out error))
                    {
                        state.Status = error;
                        break;
                    }
                    if (HorusLoadoutPresetStore.SaveDraft(draft, presetName, out saved, out error))
                    {
                        state.SavedPresetId = saved.PresetId;
                        state.PresetName = saved.Name;
                        state.Status = "Saved preset '" + saved.Name + "'.";
                    }
                    else state.Status = error;
                    break;
                case PresetUiAction.Update:
                    if (!HorusLoadoutService.CanPersistAsHorusPreset(definition, draft, out error))
                    {
                        state.Status = error;
                        break;
                    }
                    if (HorusLoadoutPresetStore.Update(presetId, draft, out saved, out error))
                        state.Status = "Updated preset '" + saved.Name + "'.";
                    else state.Status = error;
                    break;
                case PresetUiAction.Rename:
                    if (HorusLoadoutPresetStore.Rename(presetId, presetName, out error)) state.Status = "Preset renamed.";
                    else state.Status = error;
                    break;
                case PresetUiAction.Duplicate:
                    if (!HorusLoadoutPresetStore.TryCreateDraft(definition, presetId, out LoadoutDraft duplicateDraft, out error) ||
                        !HorusLoadoutService.CanPersistAsHorusPreset(definition, duplicateDraft, out error))
                    {
                        state.Status = error;
                        break;
                    }
                    if (HorusLoadoutPresetStore.Duplicate(presetId, presetName, out saved, out error))
                    {
                        state.SavedPresetId = saved.PresetId;
                        state.PresetName = saved.Name;
                        state.Status = "Preset duplicated.";
                    }
                    else state.Status = error;
                    break;
                case PresetUiAction.Delete:
                    if (HorusLoadoutPresetStore.Delete(presetId, out error))
                    {
                        state.SavedPresetId = "";
                        state.SavedPresetIndex = 0;
                        state.Status = "Preset deleted.";
                        if (state.LoadoutSource == LoadoutSourceKind.HorusSavedPreset)
                        {
                            IReadOnlyList<HorusLoadoutPreset> remaining = HorusLoadoutPresetStore.GetPresets(definition.jsonKey);
                            if (remaining.Count > 0)
                            {
                                state.SavedPresetId = remaining[0].PresetId;
                                state.PresetName = remaining[0].Name;
                                HorusLoadoutPresetStore.TryCreateDraft(definition, remaining[0].PresetId, out state.Draft, out _);
                            }
                            else state.Draft = HorusLoadoutService.CreateDefaultDraft(definition);
                        }
                    }
                    else state.Status = error;
                    break;
                case PresetUiAction.Reload:
                    HorusLoadoutPresetStore.Reload();
                    state.Status = HorusLoadoutPresetStore.LastLoadError ?? "Presets reloaded.";
                    if (!string.IsNullOrWhiteSpace(state.SavedPresetId) &&
                        !HorusLoadoutPresetStore.TryCreateDraft(definition, state.SavedPresetId, out state.Draft, out _))
                        state.SavedPresetId = "";
                    break;
            }
        }

        private void DrawHardpointRow(AircraftDefinition definition, FactionHQ hq, AircraftEditorUiState state, HardpointSet set, int index)
        {
            if (index < 0 || index >= state.Draft.WeaponMountJsonKeys.Count) return;

            string setName = !string.IsNullOrWhiteSpace(set?.name) ? set.name : "Hardpoint " + (index + 1);
            if (set != null && set.SymmetryWithPrev && !string.IsNullOrWhiteSpace(set.SymmetryName)) setName = set.SymmetryName;

            // Occupied precluding hardpoint: keep this one empty and locked, exactly
            // like the native loadout menu, so the draft is always conflict-free.
            if (HorusLoadoutService.IsHardpointBlocked(definition, state.Draft, index, out int blockingIndex))
            {
                if (!string.IsNullOrEmpty(state.Draft.WeaponMountJsonKeys[index]))
                    state.Draft.WeaponMountJsonKeys[index] = "";
                GUILayout.Label($"{index + 1}. {setName} — locked (conflicts with hardpoint {blockingIndex + 1})", HorusTheme.LabelMuted);
                return;
            }

            IReadOnlyList<WeaponMount> mounts = HorusLoadoutService.GetLegalMounts(definition, index, hq);
            string currentKey = state.Draft.WeaponMountJsonKeys[index] ?? "";
            int choice = string.IsNullOrEmpty(currentKey) ? 0 : -1;
            for (int i = 0; i < mounts.Count; i++)
                if (string.Equals(mounts[i].jsonKey, currentKey, System.StringComparison.OrdinalIgnoreCase)) { choice = i + 1; break; }

            GUILayout.Label($"{index + 1}. {setName}", HorusTheme.LabelSmall);
            GUILayout.BeginHorizontal();
            int nextChoice = choice;
            int count = mounts.Count + 1;
            if (GUILayout.Button("<", GUILayout.Width(28f))) nextChoice = choice < 0 ? 0 : (choice - 1 + count) % count;
            string display = choice < 0 ? currentKey + " [unavailable]" : choice == 0 ? "Empty" : WeaponMountName(mounts[choice - 1]);
            GUILayout.Label(display, HorusTheme.LabelWrap);
            if (GUILayout.Button(">", GUILayout.Width(28f))) nextChoice = choice < 0 ? 0 : (choice + 1) % count;
            GUILayout.EndHorizontal();
            if (nextChoice != choice)
            {
                string nextKey = nextChoice == 0 ? "" : mounts[nextChoice - 1].jsonKey;
                if (!HorusLoadoutService.TrySetHardpoint(state.Draft, definition, index, nextKey, state.MirrorSymmetry, out string error))
                    state.Status = error;
                else
                    state.PendingLoadoutSourceIndex = SourceIndex(LoadoutSourceKind.CustomHardpoints);
            }
        }

        private static string WeaponMountName(WeaponMount mount)
        {
            if (mount == null) return "Empty";
            if (!string.IsNullOrWhiteSpace(mount.mountName)) return mount.mountName;
            if (mount.info != null && !string.IsNullOrWhiteSpace(mount.info.weaponName)) return mount.info.weaponName;
            return string.IsNullOrWhiteSpace(mount.jsonKey) ? "Unnamed mount" : mount.jsonKey;
        }

        private static int SourceIndex(LoadoutSourceKind source)
        {
            for (int i = 0; i < loadoutSourceKinds.Length; i++) if (loadoutSourceKinds[i] == source) return i;
            return 0;
        }

        private static string[] GetLiveryNames(AircraftParameters parameters)
        {
            int count = parameters?.liveries?.Count ?? 0;
            var names = new string[count];
            for (int i = 0; i < count; i++)
                names[i] = string.IsNullOrWhiteSpace(parameters.liveries[i].name) ? "Livery " + (i + 1) : parameters.liveries[i].name;
            return names;
        }

        private static LoadoutApplyResult ApplyEditorLoadout(Aircraft aircraft, AircraftEditorUiState state)
        {
            if (state.LoadoutSource == LoadoutSourceKind.RandomStandardPreset)
            {
                LoadoutApplyResult random = HorusLoadoutService.ResolveRandomStandardForSpawn(aircraft.definition, aircraft.NetworkHQ);
                return random.Success ? HorusLoadoutService.ApplyToAircraft(aircraft, random.ResolvedLoadout) : random;
            }
            return HorusUnitEditor.TrySetLoadout(aircraft, state.Draft);
        }

        private void DrawAircraftCustomizationSectionLegacy()
        {
            GUILayout.Box("══ AIRCRAFT CUSTOMIZATION (Patch 0.33.4) ══");

            UnitDefinition selectedDef = GetSelectedDefinition();
            if (selectedDef == null)
            {
                GUILayout.Label("No unit selected. Select an aircraft to customize.");
                return;
            }

            AircraftDefinition acDef = selectedDef as AircraftDefinition;
            AircraftParameters acParams = acDef != null ? acDef.aircraftParameters : null;

            if (acParams == null && selectedDef.unitPrefab != null)
            {
                Aircraft acComp = selectedDef.unitPrefab.GetComponent<Aircraft>();
                if (acComp != null && acComp.definition is AircraftDefinition acDef2)
                {
                    acParams = acDef2.aircraftParameters;
                }
            }

            if (acParams == null)
            {
                Color prevC = GUI.color;
                GUI.color = Color.yellow;
                GUILayout.Label("Selected unit is not an aircraft. Equipment customization is aircraft-only.");
                GUI.color = prevC;
                return;
            }

            // --- Livery Selection ---
            GUILayout.Space(5);
            GUILayout.Label("Livery Mode:");
            int currentLiveryMode = (int)aircraftLiveryMode;

            if (acParams.liveries == null || acParams.liveries.Count == 0)
            {
                aircraftLiveryMode = AircraftLiveryMode.Default;
                GUILayout.Label("No aircraft liveries available for this unit.");
            }
            else
            {
                int newLiveryMode = GUILayout.SelectionGrid(currentLiveryMode, liveryModeNames, 2);
                if (newLiveryMode != currentLiveryMode)
                {
                    aircraftLiveryMode = (AircraftLiveryMode)newLiveryMode;
                }

                if (aircraftLiveryMode == AircraftLiveryMode.Specific)
                {
                    GUILayout.Label($"Specific Livery ({acParams.liveries.Count}):");
                    EnsureAircraftOptionNames(acParams);
                    if (selectedLiveryIndex >= acParams.liveries.Count) selectedLiveryIndex = 0;
                    selectedLiveryIndex = GUILayout.SelectionGrid(selectedLiveryIndex, cachedLiveryNames, 1);
                }
            }

            // --- Loadout Selection ---
            GUILayout.Space(5);
            GUILayout.Label("Loadout Mode:");
            int currentLoadoutMode = (int)aircraftLoadoutMode;

            if (acParams.StandardLoadouts == null || acParams.StandardLoadouts.Length == 0)
            {
                aircraftLoadoutMode = AircraftLoadoutMode.Default;
                GUILayout.Label("No standard loadouts available for this unit.");
            }
            else
            {
                int newLoadoutMode = GUILayout.SelectionGrid(currentLoadoutMode, loadoutModeNames, 2);
                if (newLoadoutMode != currentLoadoutMode)
                {
                    aircraftLoadoutMode = (AircraftLoadoutMode)newLoadoutMode;
                }

                if (aircraftLoadoutMode == AircraftLoadoutMode.StandardPreset)
                {
                    GUILayout.Label($"Standard Loadout Preset ({acParams.StandardLoadouts.Length}):");
                    EnsureAircraftOptionNames(acParams);
                    if (selectedStandardLoadoutIndex >= acParams.StandardLoadouts.Length) selectedStandardLoadoutIndex = 0;
                    selectedStandardLoadoutIndex = GUILayout.SelectionGrid(selectedStandardLoadoutIndex, cachedLoadoutNames, 1);
                }
            }

            GUILayout.Space(5);
            GUILayout.Label($"Pilot skill: {selectedAircraftSkill:P0}");
            selectedAircraftSkill = GUILayout.HorizontalSlider(selectedAircraftSkill, 0f, 1f);
            GUILayout.Space(5);
            applyCustomizationToGroups = GUILayout.Toggle(applyCustomizationToGroups, "Apply customization to group spawns");
        }

        private void DrawSelectedAircraftEditorLegacy()
        {
            if (WorldSelection == null || !WorldSelection.HasSelection) return;
            Aircraft first = WorldSelection.Units[0] as Aircraft;
            if (first == null) return;

            for (int i = 1; i < WorldSelection.Units.Count; i++)
            {
                if (!(WorldSelection.Units[i] is Aircraft aircraft) || aircraft.definition != first.definition)
                {
                    GUILayout.Space(8f);
                    GUILayout.Label("AIRCRAFT LOADOUT & SKIN", HorusTheme.TitleText);
                    GUILayout.Label("Select aircraft of the same type to edit them together.", HorusTheme.LabelMuted);
                    return;
                }
            }

            AircraftParameters parameters = (first.definition as AircraftDefinition)?.aircraftParameters;
            if (parameters == null) return;
            EnsureAircraftOptionNames(parameters);

            GUILayout.Space(8f);
            GUILayout.Label("AIRCRAFT LOADOUT & SKIN", HorusTheme.TitleText);
            GUILayout.Label($"Editing {WorldSelection.Count} selected aircraft. You can also right-click an aircraft.", HorusTheme.LabelMuted);

            bool allowed = HorusPermissions.CanSpawn();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && allowed;

            if (parameters.StandardLoadouts != null && parameters.StandardLoadouts.Length > 0)
            {
                selectedStandardLoadoutIndex = Mathf.Clamp(selectedStandardLoadoutIndex, 0, parameters.StandardLoadouts.Length - 1);
                GUILayout.Label("Loadout preset");
                selectedStandardLoadoutIndex = GUILayout.SelectionGrid(selectedStandardLoadoutIndex, cachedLoadoutNames, 1);
                if (GUILayout.Button($"Apply loadout: {cachedLoadoutNames[selectedStandardLoadoutIndex]}"))
                {
                    int changed = 0;
                    foreach (Unit unit in WorldSelection.Units)
                        if (HorusUnitEditor.TrySetLoadout((Aircraft)unit, selectedStandardLoadoutIndex)) changed++;
                    HorusToasts.Show($"Loadout applied to {changed} aircraft");
                }
            }
            else
            {
                GUILayout.Label("This aircraft has no standard loadout presets.", HorusTheme.LabelMuted);
            }

            if (parameters.liveries != null && parameters.liveries.Count > 0)
            {
                GUILayout.Space(5f);
                selectedLiveryIndex = Mathf.Clamp(selectedLiveryIndex, 0, parameters.liveries.Count - 1);
                GUILayout.Label("Skin / livery");
                selectedLiveryIndex = GUILayout.SelectionGrid(selectedLiveryIndex, cachedLiveryNames, 1);
                if (GUILayout.Button($"Apply skin: {cachedLiveryNames[selectedLiveryIndex]}"))
                {
                    int changed = 0;
                    foreach (Unit unit in WorldSelection.Units)
                        if (HorusUnitEditor.TrySetLivery((Aircraft)unit, selectedLiveryIndex)) changed++;
                    HorusToasts.Show($"Skin applied to {changed} aircraft");
                }
            }
            else
            {
                GUILayout.Label("This aircraft has no alternate skins / liveries.", HorusTheme.LabelMuted);
            }

            GUILayout.Space(5f);
            GUILayout.Label($"Pilot skill: {selectedAircraftSkill:P0}");
            selectedAircraftSkill = GUILayout.HorizontalSlider(selectedAircraftSkill, 0f, 1f);
            if (GUILayout.Button("Apply pilot skill"))
            {
                foreach (Unit unit in WorldSelection.Units)
                    HorusUnitEditor.SetSkill(unit, selectedAircraftSkill);
                HorusToasts.Show($"Pilot skill applied to {WorldSelection.Count} aircraft");
            }
            GUI.enabled = previousEnabled;
            if (!allowed) GUILayout.Label("Host only: editing is disabled for multiplayer clients.", HorusTheme.LabelMuted);
        }

        private void EnsureAircraftOptionNames(AircraftParameters parameters)
        {
            int liveryCount = parameters?.liveries?.Count ?? 0;
            int loadoutCount = parameters?.StandardLoadouts?.Length ?? 0;
            if (cachedAircraftOptions == parameters &&
                cachedLiveryNames?.Length == liveryCount &&
                cachedLoadoutNames?.Length == loadoutCount) return;

            cachedAircraftOptions = parameters;
            cachedLiveryNames = new string[liveryCount];
            for (int i = 0; i < liveryCount; i++)
                cachedLiveryNames[i] = $"{i}: {(string.IsNullOrEmpty(parameters.liveries[i].name) ? "Livery " + i : parameters.liveries[i].name)}";
            cachedLoadoutNames = new string[loadoutCount];
            for (int i = 0; i < loadoutCount; i++)
                cachedLoadoutNames[i] = $"{i}: {(string.IsNullOrEmpty(parameters.StandardLoadouts[i].Name) ? "Preset " + i : parameters.StandardLoadouts[i].Name)}";
        }

        private void DrawSettingsSection()
        {
            GUILayout.Label("Diagnostics", HorusTheme.TitleText);
            HorusPlugin.ShowDebugTab.Value = GUILayout.Toggle(HorusPlugin.ShowDebugTab.Value, "Show Debug tab");
            GUILayout.Label("Log verbosity", HorusTheme.LabelMuted);
            int logLevel = GUILayout.SelectionGrid((int)HorusPlugin.LogVerbosity.Value,
                logLevelNames, 4);
            HorusPlugin.LogVerbosity.Value = (HorusLogLevel)logLevel;

            GUILayout.Space(8f);
            GUILayout.Label("Experimental content safety", HorusTheme.TitleText);
            if (HorusPlugin.AllowIncompatibleContent != null)
                HorusPlugin.AllowIncompatibleContent.Value = GUILayout.Toggle(
                    HorusPlugin.AllowIncompatibleContent.Value,
                    " Allow Lookup-only spawn acknowledgements");
            GUILayout.Label("Lookup-only objects can desync or disconnect multiplayer clients. Each definition still requires a per-session acknowledgement.", HorusTheme.LabelWrap);
            if (GUILayout.Button("Clear incompatible-content acknowledgements"))
            {
                acknowledgedLookupDefinitions.Clear();
                HorusSpawnService.ResetAuthorizations();
                HorusToasts.Show("Experimental content acknowledgements cleared");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Interface", HorusTheme.TitleText);
            GUILayout.Label($"UI Scale: {HorusPlugin.UIScale.Value:F2}x");
            float newScale = GUILayout.HorizontalSlider(HorusPlugin.UIScale.Value, 0.5f, 2.5f);
            if (Mathf.Abs(newScale - HorusPlugin.UIScale.Value) > 0.05f)
            {
                HorusPlugin.UIScale.Value = Mathf.Round(newScale * 20f) / 20f;
            }

            GUILayout.Space(5);
            if (GUILayout.Button("Reset Window Position"))
            {
                windowRect = HorusPrefs.ResetWindow();
            }
            GUILayout.Label("Hint: Press Ctrl+F10 to emergency reset UI.", HorusTheme.LabelMuted);
        }

        private void DrawDebugSection()
        {
            DrawSelfTestPanel();
            GUILayout.Space(8f);
            GUILayout.Label($"Horus Version: {HorusPlugin.PluginVersion}");
            GUILayout.Label($"Current Mode: {HorusPermissions.GetModeLabel()}");
            GUILayout.Label($"Current Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            GUILayout.Label($"Reload Count: {sceneReloadCount}");
            GUILayout.Label($"Spawned Units: {horusSpawnedUnits.Count}");
            if (RtsFactoryManager.Instance != null)
                GUILayout.Label($"Active Factories: {RtsFactoryManager.Instance.activeFactories.Count}");
            
            GUILayout.Label($"RTS Mode Status: {(economyManager?.CurrentMode.ToString())}");
            bool isNeutral = FactionRegistry.factions != null && selectedFactionIndex >= FactionRegistry.factions.Count;
            GUILayout.Label($"Neutral Mode Status: {(isNeutral ? "Active" : "Inactive")}");
            
            GUILayout.Label($"Last Spawn: {lastSpawnResult}");
            GUILayout.Label($"Last Delete: {lastDeleteResult}");
            GUILayout.Label($"Last Blocked Action: {lastBlockedAction}");
            GUILayout.Label($"Last Lifecycle Event: {lastLifecycleEvent}");
            
            GUILayout.Space(5);
            GUILayout.Label("--- Performance & Throttling (0.33.4) ---");
            GUILayout.Label(HorusPerformanceTracker.GetDiagnosticSummary());
            GUILayout.Space(5);

            if (GUILayout.Button("Print Diagnostics to Log"))
            {
                HorusLog.Info("UI", "--- Horus Diagnostics ---");
                HorusLog.Info("UI", $"Version: {HorusPlugin.PluginVersion}");
                HorusLog.Info("UI", $"Mode: {HorusPermissions.GetModeLabel()}");
                HorusLog.Info("UI", $"Current Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
                HorusLog.Info("UI", $"Economy Mode: {(economyManager?.CurrentMode.ToString())}");
                HorusLog.Info("UI", $"Units spawned: {horusSpawnedUnits.Count}");
                HorusLog.Info("UI", $"Scene reloads: {sceneReloadCount}");
                HorusLog.Info("UI", $"Instance ID: {horusManagerInstanceId}");
            }
            
            if (GUILayout.Button("Reset UI"))
            {
                showPlacementTools = false;
                showGroupTools = false;
                showFactoryTools = false;
            }

            if (GUILayout.Button("Reload RTS Economy Config"))
            {
                if (economyManager != null)
                {
                    economyManager.LoadOrCreateConfig();
                    if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: RTS economy config reloaded.");
                }
            }
            
            if (GUILayout.Button("Reload Factory Config"))
            {
                if (RtsFactoryManager.Instance != null)
                {
                    RtsFactoryManager.Instance.ReloadConfig();
                    if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory config reloaded.");
                }
            }

            if (GUILayout.Button("Force Refresh Game References"))
            {
                InitializeMissionState();
                if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: Forced game reference refresh.");
            }
            
            if (GUILayout.Button("Clear Stale Horus References"))
            {
                int before = horusSpawnedUnits.Count;
                horusSpawnedUnits.RemoveWhere(u => u == null);
                int after = horusSpawnedUnits.Count;
                HorusLog.Info("UI", $"Cleared {before - after} stale spawned unit references.");
                if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Cleared {before - after} stale refs.");
            }

            if (GUILayout.Button("▶ Run Horus Self-Test"))
            {
                RunSelfTest();
            }
        }

        private void DrawSelfTestPanel()
        {
            UnitCatalog.EnsureBuilt(MissionManager.AllowEventContent);
            GUILayout.BeginVertical(HorusTheme.Card);
            GUILayout.Label("SELF-TEST", HorusTheme.TitleText);
            DiagnosticRow("Spawner.i", Spawner.i != null);
            DiagnosticRow("Encyclopedia.i", Encyclopedia.i != null);
            DiagnosticRow("FactionRegistry", FactionRegistry.factions != null && FactionRegistry.factions.Count > 0);
            DiagnosticRow("DynamicMap.i", SceneSingleton<DynamicMap>.i != null);
            DiagnosticRow("GameplayUI.i", SceneSingleton<GameplayUI>.i != null);
            DiagnosticRow("Compatibility audit", GameApi.Ready);
            DiagnosticRow("Theme built", HorusTheme.Built);
            DiagnosticRow("GUIStyle allocations this frame = 0", HorusTheme.StylesAllocatedThisFrame == 0,
                HorusTheme.StylesAllocatedThisFrame.ToString());

            int aircraft = UnitCatalog.Count(UnitKind.Aircraft);
            int ground = UnitCatalog.Count(UnitKind.Ground);
            int sea = UnitCatalog.Count(UnitKind.Sea);
            int building = UnitCatalog.Count(UnitKind.Building);
            int scenery = UnitCatalog.Count(UnitKind.Scenery);
            int ordnance = UnitCatalog.Count(UnitKind.Missile);
            int other = UnitCatalog.Count(UnitKind.Other);
            GUILayout.Label($"v1.3 catalog: ORD {ordnance} · PROP {other} · conflicts {UnitCatalog.Conflicts.Count}", HorusTheme.LabelSmall);
            HorusLoadoutPresetStore.EnsureLoaded();
            DiagnosticRow("Loadout preset store readable", string.IsNullOrEmpty(HorusLoadoutPresetStore.LastLoadError), HorusLoadoutPresetStore.LastLoadError);
            GUILayout.Label($"Catalog: AIR {aircraft} · GND {ground} · SEA {sea} · BLD {building} · SCN {scenery}", HorusTheme.LabelSmall);
            GUILayout.Label($"Overlay {worldOverlay?.VisibleCount ?? 0}/64 · Selection {worldSelection?.Count ?? 0} · Pick {(inputRouter != null && inputRouter.Pick.Valid ? inputRouter.Pick.Distance.ToString("F1") + " m" : "none")}", HorusTheme.LabelSmall);
            GUILayout.Label($"GC delta/60f: {HorusPerformanceTracker.GcDelta60Frames / 1024f:F1} KiB", HorusTheme.LabelSmall);
            GUILayout.EndVertical();
        }

        private static void DiagnosticRow(string name, bool success, string detail = null)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = success ? HorusTheme.Success : HorusTheme.Danger;
            GUILayout.Label($"{(success ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : " · " + detail)}", HorusTheme.LabelSmall);
            GUI.contentColor = previous;
        }

        private void RunSelfTest()
        {
            HorusLog.Info("UI", "=== HORUS SELF-TEST BEGIN ===");
            HorusLog.Info("UI", $"  Version: {HorusPlugin.PluginVersion}");
            HorusLog.Info("UI", $"  Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            
            // Instance count check
            var allManagers = Resources.FindObjectsOfTypeAll<HorusManager>();
            HorusLog.Info("UI", $"  HorusManager instances: {allManagers.Length} (expected: 1)");
            if (allManagers.Length > 1)
                HorusLog.Warning("UI", "  WARNING: Multiple HorusManager instances detected!");
            HorusLog.Info("UI", $"  Instance ID: {horusManagerInstanceId}");
            
            // Game services
            bool spawnerReady = Spawner.i != null;
            bool encyclopediaReady = Encyclopedia.i != null;
            var factions = FactionRegistry.factions;
            bool factionsReady = factions != null && factions.Count > 0;
            int factionCount = factions?.Count ?? 0;
            HorusLog.Info("UI", $"  Spawner.i: {(spawnerReady ? "READY" : "NOT READY")}");
            HorusLog.Info("UI", $"  Encyclopedia.i: {(encyclopediaReady ? "READY" : "NOT READY")}");
            HorusLog.Info("UI", $"  FactionRegistry: {(factionsReady ? $"READY ({factionCount} factions)" : "NOT READY")}");
            HorusLog.Info("UI", $"  GameManager.gameState: {GameManager.gameState}");
            
            // Selected faction / unit validity
            bool isNeutral = factionsReady && selectedFactionIndex >= factionCount;
            bool factionValid = factionsReady && (selectedFactionIndex < factionCount || isNeutral);
            HorusLog.Info("UI", $"  Selected faction index: {selectedFactionIndex} (valid={factionValid}, neutral={isNeutral})");
            
            UnitDefinition selectedDef = GetSelectedDefinition();
            HorusLog.Info("UI", $"  Selected unit: {(selectedDef != null ? selectedDef.unitName : "NONE")}");
            UnitCatalog.Refresh(MissionManager.AllowEventContent);
            HorusLog.Info("UI", $"  Catalog: revision={UnitCatalog.Revision} entries={UnitCatalog.Entries.Count} conflicts={UnitCatalog.Conflicts.Count} fingerprint={UnitCatalog.Fingerprint}");
            HorusLog.Info("UI", $"  Catalog categories: aircraft={UnitCatalog.Count(UnitKind.Aircraft)} ground={UnitCatalog.Count(UnitKind.Ground)} sea={UnitCatalog.Count(UnitKind.Sea)} missile={UnitCatalog.Count(UnitKind.Missile)} other={UnitCatalog.Count(UnitKind.Other)}");
            CatalogEntry navalSupply = FindNavalResupplyCandidate();
            HorusLog.Info("UI", navalSupply != null
                ? $"  Naval resupply candidate: {navalSupply.Display} range={navalSupply.Supply?.RearmRange?.ToString("F0") ?? "unknown"} singleUse={navalSupply.Supply?.RearmerSingleUse?.ToString() ?? "unknown"}"
                : "  Naval resupply candidate: NONE (runtime validation unavailable)");
            HorusLoadoutPresetStore.EnsureLoaded();
            HorusLog.Info("UI", $"  Loadout preset store: path={HorusLoadoutPresetStore.FilePath} presets={HorusLoadoutPresetStore.GetAllPresets().Count} readOnly={HorusLoadoutPresetStore.IsReadOnly} error={HorusLoadoutPresetStore.LastLoadError ?? "none"}");
            
            // Economy / Factory
            bool economyReady = economyManager != null;
            bool factoryReady = RtsFactoryManager.Instance != null;
            HorusLog.Info("UI", $"  RtsEconomyManager: {(economyReady ? $"READY (mode={economyManager.CurrentMode})" : "NOT READY")}");
            HorusLog.Info("UI", $"  RtsFactoryManager: {(factoryReady ? $"READY ({RtsFactoryManager.Instance.activeFactories.Count} factories)" : "NOT READY")}");
            
            // Neutral support
            HorusLog.Info("UI", $"  Neutral spawn support: enabled (HQ intentionally null)");
            
            // Lifecycle
            HorusLog.Info("UI", $"  Scene reload count: {sceneReloadCount}");
            HorusLog.Info("UI", $"  Scene loaded subscriptions: {sceneLoadedSubscriptions}");
            HorusLog.Info("UI", $"  Scene unloaded subscriptions: {sceneUnloadedSubscriptions}");
            HorusLog.Info("UI", $"  Last lifecycle event: {lastLifecycleEvent}");
            HorusLog.Info("UI", $"  Last spawn result: {lastSpawnResult}");
            HorusLog.Info("UI", $"  Last delete result: {lastDeleteResult}");
            HorusLog.Info("UI", $"  Spawned units tracked: {horusSpawnedUnits.Count}");
            
            HorusLog.Info("UI", "=== HORUS SELF-TEST END ===");
            
            if (SceneSingleton<GameplayUI>.i != null) 
                SceneSingleton<GameplayUI>.i.GameMessage("Horus: Self-test complete. Check BepInEx log.");
        }
        /// <summary>
        /// Draws the detailed RTS Commander Mode panel with budgets, income, caps,
        /// deployment confirmation, and host cheats.
        /// </summary>
        private void DrawRtsCommanderUI()
        {
            GUILayout.Space(5);
            GUILayout.Box("═══ RTS COMMANDER MODE ═══");

            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0) return;

            // Per-faction economy display
            for (int i = 0; i < factions.Count; i++)
            {
                var state = economyManager.GetFactionState(i);
                if (state == null) continue;

                string fname = state.FactionName;
                Color prevColor = GUI.color;

                // Highlight selected faction
                if (i == selectedFactionIndex)
                {
                    GUI.color = new Color(0.6f, 1f, 0.6f);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{fname}:", GUILayout.Width(70));
                GUILayout.Label($"${state.Budget:F0}", GUILayout.Width(65));

                bool incomeEnabled = HorusPlugin.EnableRtsIncome != null && HorusPlugin.EnableRtsIncome.Value;
                if (incomeEnabled)
                {
                    GUILayout.Label($"+{state.IncomePerTick:F0}/tick", GUILayout.Width(65));
                }

                bool capsEnabled = HorusPlugin.EnableRtsUnitCap != null && HorusPlugin.EnableRtsUnitCap.Value;
                if (capsEnabled)
                {
                    Color capColor = state.IsOverCap ? new Color(1f, 0.4f, 0.4f) : GUI.color;
                    Color savedColor = GUI.color;
                    GUI.color = capColor;
                    GUILayout.Label($"[{state.ActiveUnitCount}/{state.UnitCap}]", GUILayout.Width(55));
                    GUI.color = savedColor;
                }
                GUILayout.EndHorizontal();

                GUI.color = prevColor;

                // Host-only budget cheats
                if (HorusPermissions.CanSpawn() && i == selectedFactionIndex)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("-1000", GUILayout.Width(50))) economyManager.AdjustBudget(i, -1000f);
                    if (GUILayout.Button("-500", GUILayout.Width(40))) economyManager.AdjustBudget(i, -500f);
                    if (GUILayout.Button("+500", GUILayout.Width(40))) economyManager.AdjustBudget(i, 500f);
                    if (GUILayout.Button("+1000", GUILayout.Width(50))) economyManager.AdjustBudget(i, 1000f);
                    if (GUILayout.Button("+5000", GUILayout.Width(50))) economyManager.AdjustBudget(i, 5000f);
                    GUILayout.EndHorizontal();
                    
                    if (capsEnabled)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Cap:", GUILayout.Width(35));
                        if (GUILayout.Button("-5", GUILayout.Width(35))) economyManager.AdjustUnitCap(i, -5);
                        if (GUILayout.Button("-1", GUILayout.Width(35))) economyManager.AdjustUnitCap(i, -1);
                        if (GUILayout.Button("+1", GUILayout.Width(35))) economyManager.AdjustUnitCap(i, 1);
                        if (GUILayout.Button("+5", GUILayout.Width(35))) economyManager.AdjustUnitCap(i, 5);
                        GUILayout.EndHorizontal();
                    }
                }
            }

            // Selected unit cost preview
            GUILayout.Space(3);
            UnitDefinition selectedDef = GetSelectedDefinition();
            if (selectedDef != null)
            {
                float cost;
                if (enableGroupSpawn)
                {
                    int tmpCat;
                    var groupUnits = GetGroupUnitsToSpawn(out tmpCat);
                    cost = GetGroupTotalCost(groupUnits);
                }
                else
                {
                    cost = GetUnitCost(selectedDef);
                }

                float currentBudget = economyManager.GetBudget(selectedFactionIndex);
                float remaining = currentBudget - cost;

                GUILayout.Label($"Selected Cost: {cost:F0}");
                if (remaining < 0)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 0.5f, 0.5f);
                    GUILayout.Label($"⚠ Insufficient Budget! (Missing: {-remaining:F0})");
                    GUI.color = prev;
                }
                else
                {
                    GUILayout.Label($"Remaining after spawn: {remaining:F0}");
                }
            }

            // Deployment confirmation status
            if (HorusPlugin.RequireDeploymentConfirmation.Value)
            {
                GUILayout.Space(3);
                if (economyManager.IsDeploymentArmed)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 0.9f, 0.3f);
                    GUILayout.Label(economyManager.ArmedStatusText);
                    GUI.color = prev;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Click to deploy or:");
                    if (GUILayout.Button("Cancel", GUILayout.Width(60)))
                    {
                        economyManager.DisarmDeployment();
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("Click to arm deployment. Click again to deploy.");
                    if (!string.IsNullOrEmpty(economyManager.ArmedStatusText))
                    {
                        Color prev = GUI.color;
                        GUI.color = new Color(1f, 0.5f, 0.5f);
                        GUILayout.Label(economyManager.ArmedStatusText);
                        GUI.color = prev;
                    }
                }
            }

            // Match controls (host only)
            if (HorusPermissions.CanSpawn())
            {
                GUILayout.Space(3);
                if (GUILayout.Button("↺ Reset Match Economy"))
                {
                    economyManager.InitializeMatch();
                    HorusLog.Info("UI", "[RTS Economy] Match economy reset by host.");
                    if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage("Horus: RTS economy reset to defaults.");
                    }
                }
            }
        }

        private bool showFactoryTools = true;
        private RtsFactory selectedFactory = null;
        private RtsFactory pendingFactorySelection;
        private bool pendingFactorySelectionSet;
        private RtsFactory pendingFactoryDelete;
        private int selectedPresetIndex = 0;
        private int selectedFactoryQueueIndex = 0;
        private float lastFactoryCreateActionTime = -999f;
        private List<FactoryPreset> cachedFactoryPresetSource;
        private string[] cachedFactoryPresetNames;

        private string[] GetFactoryPresetNames(List<FactoryPreset> presets)
        {
            if (cachedFactoryPresetSource == presets && cachedFactoryPresetNames?.Length == presets.Count)
                return cachedFactoryPresetNames;
            cachedFactoryPresetSource = presets;
            cachedFactoryPresetNames = new string[presets.Count];
            for (int i = 0; i < presets.Count; i++)
                cachedFactoryPresetNames[i] = presets[i]?.presetName ?? $"Preset {i + 1}";
            return cachedFactoryPresetNames;
        }

        private void DrawRtsFactoriesUI()
        {
            if (RtsFactoryManager.Instance == null || RtsFactoryManager.Instance.Config == null)
            {
                GUILayout.Label("Factories manager not initialized.");
                return;
            }

            var manager = RtsFactoryManager.Instance;
            if (Event.current.type == EventType.Layout)
            {
                if (pendingFactoryDelete != null)
                {
                    RtsFactory deleting = pendingFactoryDelete;
                    pendingFactoryDelete = null;
                    manager.DeleteFactory(deleting);
                    if (ReferenceEquals(selectedFactory, deleting)) selectedFactory = null;
                }
                if (pendingFactorySelectionSet)
                {
                    selectedFactory = pendingFactorySelection;
                    pendingFactorySelection = null;
                    pendingFactorySelectionSet = false;
                    selectedFactoryQueueIndex = 0;
                }
                if (selectedFactory != null && !manager.activeFactories.Contains(selectedFactory))
                    selectedFactory = null;
            }
            var presets = manager.Config.factoryPresets;
            bool isHost = HorusPermissions.CanSpawn();

            GUILayout.Space(5);
            if (!Section("RTS Factories & Production", ref showFactoryTools)) return;

            GUILayout.Label($"Factories System: {(manager.Config.settings.enableFactories ? "Enabled" : "Disabled")}");
            if (!isHost)
            {
                GUILayout.Label("Host only: factory edits, spawning, save/load, config changes, and production ticks are blocked.");
            }

            if (manager.activeFactories.Count == 0)
            {
                GUILayout.Label("No active factories.");
            }
            else
            {
                GUILayout.Label("Active Factories:");
                for (int i = 0; i < manager.activeFactories.Count; i++)
                {
                    var factory = manager.activeFactories[i];
                    string prefix = selectedFactory == factory ? "* " : "  ";
                    string status = factory.enabled ? "ON" : "OFF";
                    if (GUILayout.Button($"{prefix}{i + 1}. {factory.displayName} ({factory.factionName}) - {status}"))
                    {
                        pendingFactorySelection = factory;
                        pendingFactorySelectionSet = true;
                    }
                }
            }

            GUILayout.Space(5);
            DrawSelectedFactoryPanel(manager, isHost);
            GUILayout.Space(5);
            DrawFactoryCreationPanel(manager, presets, isHost);
            GUILayout.Space(5);
            DrawFactoryBulkPanel(manager, isHost);
        }
    }
}
