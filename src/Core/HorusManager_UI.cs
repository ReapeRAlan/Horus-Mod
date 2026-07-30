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
        private AircraftParameters cachedAircraftOptions;
        private string[] cachedLiveryNames;
        private string[] cachedLoadoutNames;

        internal void DrawPlaceConfiguration()
        {
            GUILayout.Space(4f);
            if (Section("Placement", ref showPlacementTools)) DrawPlacementToolsSection();
            if (ArmedDefinition is AircraftDefinition && Section("Aircraft options", ref showAircraftCustomizationTools))
                DrawAircraftCustomizationSection();
            if (Section("Groups & formations", ref showGroupTools)) DrawGroupsSection();
        }

        internal void DrawManageConfiguration()
        {
            GUILayout.Label($"Selection: {(WorldSelection != null ? WorldSelection.Count : 0)}", HorusTheme.TitleText);
            GUILayout.BeginHorizontal();
            GUI.enabled = WorldSelection != null && WorldSelection.HasSelection;
            if (HorusWidgets.Secondary("Focus (F)")) FocusSelection();
            if (HorusWidgets.Secondary("Duplicate (Ctrl+D)")) DuplicateSelection();
            if (HorusWidgets.Danger("Delete (Del)")) DeleteSelection();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (WorldSelection != null)
                foreach (Unit unit in WorldSelection.Units)
                    if (unit != null) GUILayout.Label($"• {unit.unitName}", HorusTheme.LabelSmall);
            GUILayout.Space(8f);
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

        private static bool Section(string title, ref bool open)
        {
            if (GUILayout.Button((open ? "▼ " : "▶ ") + title))
            {
                open = !open;
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
            bool prevGroup = enableGroupSpawn;
            enableGroupSpawn = GUILayout.Toggle(enableGroupSpawn, " Enable Group Spawning");
            if (enableGroupSpawn != prevGroup)
            {
                HorusPlugin.EnableGroupSpawn.Value = enableGroupSpawn;
                ghost.Clear();
            }

            GUILayout.Label("Spawns multiple units. Disabled by default.", HorusTheme.LabelMuted);

            if (!enableGroupSpawn) return;

            GUILayout.Label("Preset Group:");
            string[] presetNames = GetGroupPresetNames();
            int oldPreset = selectedGroupPresetIndex;
            selectedGroupPresetIndex = GUILayout.SelectionGrid(selectedGroupPresetIndex, presetNames, 2);
            if (oldPreset != selectedGroupPresetIndex) OnGroupPresetChanged(oldPreset, selectedGroupPresetIndex);

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
                    customGroupUnits.Add(currentSelected);
                    groupCount = customGroupUnits.Count;
                    ghost.Clear();
                }

                GUILayout.Label($"Units in custom group: ({customGroupUnits.Count})");
                for (int i = 0; i < customGroupUnits.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{i + 1}. {customGroupUnits[i].unitName}");
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        customGroupUnits.RemoveAt(i);
                        groupCount = customGroupUnits.Count;
                        ghost.Clear();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save Group")) SaveCustomGroup(customGroupName);
                if (GUILayout.Button("Clear Group"))
                {
                    customGroupUnits.Clear();
                    groupCount = 0;
                    ghost.Clear();
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
                        LoadCustomGroup(savedCustomGroupNames[selectedSavedGroupIndex]);
                        customGroupName = savedCustomGroupNames[selectedSavedGroupIndex];
                        ghost.Clear();
                    }
                    if (GUILayout.Button("Delete Selected"))
                    {
                        DeleteCustomGroupFile(savedCustomGroupNames[selectedSavedGroupIndex]);
                        RefreshSavedCustomGroups();
                        if (selectedSavedGroupIndex >= savedCustomGroupNames.Count) selectedSavedGroupIndex = 0;
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
            UnitCatalog.EnsureBuilt();
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
            var allManagers = FindObjectsOfType<HorusManager>();
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
                        selectedFactory = factory;
                        selectedFactoryQueueIndex = 0;
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
