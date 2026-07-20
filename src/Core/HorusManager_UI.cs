using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.Networking;
using Mirage;
using HorusMod.Networking;
using HorusMod.Placement;
using HorusMod.Economy;

namespace HorusMod.Core
{
    public partial class HorusManager : MonoBehaviour
    {
        private bool hasLoggedDrawHorusWindow = false;
        private void DrawHorusWindow(int windowID)
        {
            if (!hasLoggedDrawHorusWindow)
            {
                HorusPlugin.Logger.LogInfo("[HORUS DEBUG] DrawHorusWindow called");
                hasLoggedDrawHorusWindow = true;
            }
            mainScroll = GUILayout.BeginScrollView(mainScroll);

            DrawStatusModeSection();
            DrawUnitSelectionSection();

            GUILayout.Space(5);
            if (Section("Placement Tools", ref showPlacementTools))
                DrawPlacementToolsSection();

            GUILayout.Space(5);
            if (Section("Spawn Actions", ref showControls))
                DrawSpawnActionsSection();

            GUILayout.Space(5);
            if (Section("Map Spawn", ref showMapTools))
                DrawMapSpawnSection();

            GUILayout.Space(5);
            if (Section("Groups & Formations", ref showGroupTools))
                DrawGroupsSection();

            if (economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander)
            {
                DrawRtsCommanderUI();
                DrawRtsFactoriesUI();
            }

            GUILayout.Space(5);
            if (Section("Safe Delete", ref showDeletionTools))
                DrawSafeDeleteSection();

            GUILayout.Space(5);
            if (Section("Settings / UI", ref showSettingsTools))
                DrawSettingsSection();

            GUILayout.Space(5);
            if (Section("Debug / Diagnostics", ref showDebugTools))
                DrawDebugSection();

            GUILayout.EndScrollView();
            GUI.DragWindow();
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
            int newMode = GUILayout.SelectionGrid(oldMode, new string[] { "Sandbox Mode", "RTS Commander Mode" }, 2);
            if (newMode != oldMode && economyManager != null)
            {
                if (newMode == 1)
                {
                    economyManager.CurrentMode = HorusMode.RtsCommander;
                    economyManager.InitializeMatch();
                    HorusPlugin.Logger.LogInfo("[RTS Economy] Switched to RTS Commander Mode.");
                }
                else
                {
                    economyManager.CurrentMode = HorusMode.Sandbox;
                    economyManager.ResetMatch();
                    HorusPlugin.Logger.LogInfo("[RTS Economy] Switched to Sandbox Mode.");
                }
            }
            
            // Helper text
            if (newMode == 0) GUILayout.Label("Sandbox: Free spawning, no budget.", new GUIStyle(GUI.skin.label) { fontSize = 11 });
            else GUILayout.Label("RTS Commander: Units cost money. Factories produce units.", new GUIStyle(GUI.skin.label) { fontSize = 11 });
        }

        private void DrawUnitSelectionSection()
        {
            GUILayout.Space(5);
            GUILayout.Box("══ UNIT SELECTION ══");

            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0)
            {
                GUILayout.Label("Status: No playable factions found.");
            }
            else
            {
                if (selectedFactionIndex > factions.Count) selectedFactionIndex = 0; // > because Count is the index for Neutral
                List<string> factionNames = factions.Select(f => f.factionName).ToList();
                factionNames.Add("Neutral (Unassigned)");
                selectedFactionIndex = GUILayout.SelectionGrid(selectedFactionIndex, factionNames.ToArray(), 2);
            }

            GUILayout.Space(5);
            string[] categories = { "Aircraft", "Vehicles", "Ships", "Buildings", "Scenery" };
            int oldCat = selectedCategoryIndex;
            selectedCategoryIndex = GUILayout.SelectionGrid(selectedCategoryIndex, categories, 3);
            if (oldCat != selectedCategoryIndex)
            {
                selectedUnitIndex = 0;
                cachedCategoryIndex = -1;
                if (selectedCategoryIndex == 0) { spawnAltitude = 3000f; }
                else { spawnAltitude = 0f; }
                altitudeInputText = spawnAltitude.ToString("0");
                armedFactoryPresetName = null;
                ghost.Clear();
            }

            List<UnitDefinition> currentList = GetCurrentList();
            GUILayout.Space(5);
            GUILayout.Label($"Unit to Spawn: ({currentList.Count})");
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));
            if (currentList != null && currentList.Count > 0)
            {
                string[] unitNames = currentList.Select(u => u.unitName).ToArray();
                if (selectedUnitIndex >= currentList.Count) selectedUnitIndex = 0;
                int oldUnitIndex = selectedUnitIndex;
                selectedUnitIndex = GUILayout.SelectionGrid(selectedUnitIndex, unitNames, 1);
                if (selectedUnitIndex != oldUnitIndex)
                {
                    armedFactoryPresetName = null;
                    ghost.Clear();
                }
            }
            else
            {
                GUILayout.Label("No units in this category.");
            }
            GUILayout.EndScrollView();
        }

        private void DrawPlacementToolsSection()
        {
            // Quick Actions
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Altitude")) { spawnAltitude = 0f; altitudeInputText = "0"; }
            if (GUILayout.Button("Reset Yaw")) { spawnYaw = 0f; yawInputText = "0"; }
            if (GUILayout.Button("Reset Window")) { windowRect = new Rect(20, 20, 340, 700); }
            GUILayout.EndHorizontal();

            // Altitude & Yaw sliders
            GUILayout.Space(5);
            GUILayout.Label($"Altitude: {spawnAltitude:F0} m  |  Yaw: {spawnYaw:F0}°");

            float newAlt = GUILayout.HorizontalSlider(spawnAltitude, 0f, 15000f);
            if (Mathf.Abs(newAlt - spawnAltitude) > 0.01f)
            {
                spawnAltitude = Mathf.Round(newAlt / 50f) * 50f;
                altitudeInputText = spawnAltitude.ToString("0");
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Alt:", GUILayout.Width(30));
            altitudeInputText = GUILayout.TextField(altitudeInputText, GUILayout.Width(70));
            if (GUILayout.Button("Set", GUILayout.Width(35)))
            {
                if (float.TryParse(altitudeInputText, out float parsed))
                {
                    spawnAltitude = Mathf.Clamp(parsed, 0f, 50000f);
                    altitudeInputText = spawnAltitude.ToString("0");
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
            if (GUILayout.Button("0m")) { spawnAltitude = 0f; altitudeInputText = "0"; }
            if (GUILayout.Button("1k")) { spawnAltitude = 1000f; altitudeInputText = "1000"; }
            if (GUILayout.Button("3k")) { spawnAltitude = 3000f; altitudeInputText = "3000"; }
            if (GUILayout.Button("5k")) { spawnAltitude = 5000f; altitudeInputText = "5000"; }
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
            GUILayout.Label("Shows where the unit will spawn before placing it.", new GUIStyle(GUI.skin.label) { fontSize = 11 });
            
            bool prevStationary = spawnStationary;
            spawnStationary = GUILayout.Toggle(spawnStationary, " Spawn Ground Units Stationary");
            if (spawnStationary != prevStationary) HorusPlugin.SpawnGroundUnitsStationary.Value = spawnStationary;
            GUILayout.Label("Ground units and ships will hold position after spawning.", new GUIStyle(GUI.skin.label) { fontSize = 11 });

            snapToGround = GUILayout.Toggle(snapToGround, " Snap to Ground");
            alignToSurface = GUILayout.Toggle(alignToSurface, " Align to Surface Normal");
            autoOceanSnapForShips = GUILayout.Toggle(autoOceanSnapForShips, " Auto Ocean Snap for Ships");
            oceanSnapActive = GUILayout.Toggle(oceanSnapActive, " Snap to Ocean Level");
            
            GUILayout.Space(3);
            gridSnapEnabled = GUILayout.Toggle(gridSnapEnabled, " Grid Snap");
            if (gridSnapEnabled)
            {
                int gi = IndexOf(gridSizeOptions, gridSize);
                string[] gridLabels = { "1m", "5m", "10m", "25m", "50m", "100m" };
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
                string[] rotLabels = { "1°", "5°", "15°", "45°", "90°" };
                int newRi = GUILayout.SelectionGrid(ri < 0 ? 2 : ri, rotLabels, 5);
                if (newRi != ri && newRi >= 0 && newRi < rotationSnapOptions.Length)
                {
                    rotationSnapStep = rotationSnapOptions[newRi];
                    spawnYaw = ApplyRotationSnap(spawnYaw);
                    yawInputText = spawnYaw.ToString("0");
                }
            }
        }

        private void DrawSpawnActionsSection()
        {
            GUILayout.Label("Left Click: Spawn");
            GUILayout.Label("Mid Click: Delete");
            GUILayout.Label("Ctrl+Scroll: Altitude");
            GUILayout.Label("Alt+Scroll: Yaw");
            GUILayout.Label("Shift: Larger step (with Ctrl/Alt)");
            GUILayout.Label("RMB: Camera look  |  WASD/QE: Move");
            GUILayout.Space(5);
            GUILayout.Label("Hint: Press Ctrl+F10 to emergency reset UI.", new GUIStyle(GUI.skin.label) { fontSize = 11 });
        }

        private void DrawMapSpawnSection()
        {
            string mapBtnLabel = mapSpawnMode ? "■ Map Spawn: ON" : "▶ Map Spawn: OFF";
            if (GUILayout.Button(mapBtnLabel, GUILayout.Height(30)))
            {
                if (mapSpawnMode) ExitMapSpawnMode();
                else EnterMapSpawnMode();
            }
            if (mapSpawnMode)
            {
                GUILayout.Label("Left-click the map to spawn at the cursor.");
                GUILayout.Label("Press M to open/close the map.");
            }
            GUILayout.Label("Open the map and click to place units.", new GUIStyle(GUI.skin.label) { fontSize = 11 });
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

            GUILayout.Label("Spawns multiple units. Disabled by default.", new GUIStyle(GUI.skin.label) { fontSize = 11 });

            if (!enableGroupSpawn) return;

            GUILayout.Label("Preset Group:");
            int oldPreset = selectedGroupPresetIndex;
            selectedGroupPresetIndex = GUILayout.SelectionGrid(selectedGroupPresetIndex, groupPresetNames, 3);
            if (oldPreset != selectedGroupPresetIndex) OnGroupPresetChanged(oldPreset, selectedGroupPresetIndex);

            if (selectedGroupPresetIndex == 8)
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
                GUILayout.Label($"Unit Count: {groupCount}");
                groupCount = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(groupCount, 1f, 20f)), 1, 20);

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
            GUILayout.Label("Middle-click a unit to delete it.", new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(1f, 0.7f, 0.7f) } });
            GUILayout.Label($"Delete Search Range: {deleteRange:F0}m");
            
            int di = IndexOf(deleteRangeOptions, deleteRange);
            string[] deleteLabels = { "25m", "50m", "100m" };
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

        private void DrawSettingsSection()
        {
            GUILayout.Label($"UI Scale: {HorusPlugin.UIScale.Value:F2}x");
            float newScale = GUILayout.HorizontalSlider(HorusPlugin.UIScale.Value, 0.5f, 2.5f);
            if (Mathf.Abs(newScale - HorusPlugin.UIScale.Value) > 0.05f)
            {
                HorusPlugin.UIScale.Value = Mathf.Round(newScale * 20f) / 20f;
            }

            GUILayout.Space(5);
            if (GUILayout.Button("Reset Window Position"))
            {
                windowRect = new Rect(20, 20, 340, 700);
            }
            GUILayout.Label("Hint: Press Ctrl+F10 to emergency reset UI.", new GUIStyle(GUI.skin.label) { fontSize = 11 });
            
            GUILayout.Space(5);
            GUILayout.Label("Advanced Mode: Under Construction", new GUIStyle(GUI.skin.label) { fontSize = 11 });
        }

        private void DrawDebugSection()
        {
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
            
            GUILayout.Label($"Command Executor Mode: local");

            if (GUILayout.Button("Print Diagnostics to Log"))
            {
                HorusPlugin.Logger.LogInfo("--- Horus Diagnostics ---");
                HorusPlugin.Logger.LogInfo($"Version: {HorusPlugin.PluginVersion}");
                HorusPlugin.Logger.LogInfo($"Mode: {HorusPermissions.GetModeLabel()}");
                HorusPlugin.Logger.LogInfo($"Current Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
                HorusPlugin.Logger.LogInfo($"Economy Mode: {(economyManager?.CurrentMode.ToString())}");
                HorusPlugin.Logger.LogInfo($"Units spawned: {horusSpawnedUnits.Count}");
                HorusPlugin.Logger.LogInfo($"Scene reloads: {sceneReloadCount}");
                HorusPlugin.Logger.LogInfo($"Instance ID: {horusManagerInstanceId}");
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
                HorusPlugin.Logger.LogInfo($"Cleared {before - after} stale spawned unit references.");
                if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Cleared {before - after} stale refs.");
            }

            if (GUILayout.Button("▶ Run Horus Self-Test"))
            {
                RunSelfTest();
            }
        }

        private void RunSelfTest()
        {
            HorusPlugin.Logger.LogInfo("=== HORUS SELF-TEST BEGIN ===");
            HorusPlugin.Logger.LogInfo($"  Version: {HorusPlugin.PluginVersion}");
            HorusPlugin.Logger.LogInfo($"  Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            
            // Instance count check
            var allManagers = FindObjectsOfType<HorusManager>();
            HorusPlugin.Logger.LogInfo($"  HorusManager instances: {allManagers.Length} (expected: 1)");
            if (allManagers.Length > 1)
                HorusPlugin.Logger.LogWarning("  WARNING: Multiple HorusManager instances detected!");
            HorusPlugin.Logger.LogInfo($"  Instance ID: {horusManagerInstanceId}");
            
            // Game services
            bool spawnerReady = Spawner.i != null;
            bool encyclopediaReady = Encyclopedia.i != null;
            var factions = FactionRegistry.factions;
            bool factionsReady = factions != null && factions.Count > 0;
            int factionCount = factions?.Count ?? 0;
            HorusPlugin.Logger.LogInfo($"  Spawner.i: {(spawnerReady ? "READY" : "NOT READY")}");
            HorusPlugin.Logger.LogInfo($"  Encyclopedia.i: {(encyclopediaReady ? "READY" : "NOT READY")}");
            HorusPlugin.Logger.LogInfo($"  FactionRegistry: {(factionsReady ? $"READY ({factionCount} factions)" : "NOT READY")}");
            HorusPlugin.Logger.LogInfo($"  GameManager.gameState: {GameManager.gameState}");
            
            // Selected faction / unit validity
            bool isNeutral = factionsReady && selectedFactionIndex >= factionCount;
            bool factionValid = factionsReady && (selectedFactionIndex < factionCount || isNeutral);
            HorusPlugin.Logger.LogInfo($"  Selected faction index: {selectedFactionIndex} (valid={factionValid}, neutral={isNeutral})");
            
            UnitDefinition selectedDef = GetSelectedDefinition();
            HorusPlugin.Logger.LogInfo($"  Selected unit: {(selectedDef != null ? selectedDef.unitName : "NONE")}");
            
            // Economy / Factory
            bool economyReady = economyManager != null;
            bool factoryReady = RtsFactoryManager.Instance != null;
            HorusPlugin.Logger.LogInfo($"  RtsEconomyManager: {(economyReady ? $"READY (mode={economyManager.CurrentMode})" : "NOT READY")}");
            HorusPlugin.Logger.LogInfo($"  RtsFactoryManager: {(factoryReady ? $"READY ({RtsFactoryManager.Instance.activeFactories.Count} factories)" : "NOT READY")}");
            
            // Command executor
            HorusPlugin.Logger.LogInfo($"  Command executor: stub (v1.3.0 architecture, not wired)");
            
            // Neutral support
            HorusPlugin.Logger.LogInfo($"  Neutral spawn support: experimental (hq=null, may not work for all unit types)");
            
            // Dedicated bridge
            bool bridgeEnabled = HorusPlugin.DedicatedServerBridgeEnabled?.Value ?? false;
            HorusPlugin.Logger.LogInfo($"  Dedicated server bridge: {(bridgeEnabled ? "ENABLED (WARNING)" : "disabled")}");
            
            // Lifecycle
            HorusPlugin.Logger.LogInfo($"  Scene reload count: {sceneReloadCount}");
            HorusPlugin.Logger.LogInfo($"  Scene loaded subscriptions: {sceneLoadedSubscriptions}");
            HorusPlugin.Logger.LogInfo($"  Scene unloaded subscriptions: {sceneUnloadedSubscriptions}");
            HorusPlugin.Logger.LogInfo($"  Last lifecycle event: {lastLifecycleEvent}");
            HorusPlugin.Logger.LogInfo($"  Last spawn result: {lastSpawnResult}");
            HorusPlugin.Logger.LogInfo($"  Last delete result: {lastDeleteResult}");
            HorusPlugin.Logger.LogInfo($"  Spawned units tracked: {horusSpawnedUnits.Count}");
            
            HorusPlugin.Logger.LogInfo("=== HORUS SELF-TEST END ===");
            
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
                    HorusPlugin.Logger.LogInfo("[RTS Economy] Match economy reset by host.");
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

        private void DrawRtsFactoriesUILegacy()
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

            // 1. Factory System Info / Status
            GUILayout.Label($"Factories System: {(manager.Config.settings.enableFactories ? "Enabled" : "Disabled")}");

            // 2. Factory Selector
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
                    string prefix = (selectedFactory == factory) ? "● " : "○ ";
                    string status = factory.enabled ? "ON" : "OFF";
                    if (GUILayout.Button($"{prefix}{factory.displayName} ({factory.factionName}) - {status}"))
                    {
                        selectedFactory = factory;
                    }
                }
            }

            GUILayout.Space(5);

            // 3. Selected Factory Details
            if (selectedFactory != null)
            {
                if (!manager.activeFactories.Contains(selectedFactory))
                {
                    selectedFactory = null;
                }
                else
                {
                    var f = selectedFactory;
                    GUILayout.Box($"══ Factory: {f.displayName} ══");
                    GUILayout.Label($"Faction: {f.factionName}");
                    GUILayout.Label($"Type: {f.factoryType}");
                    GUILayout.Label($"Status: {(f.enabled ? "ACTIVE" : "INACTIVE")}");
                    GUILayout.Label($"Income: +{f.incomePerMinute:F0}/min");
                    GUILayout.Label($"Production: {(f.produceUnits ? "ON" : "OFF")}");
                    GUILayout.Label($"Consumes Budget: {(f.consumeBudgetForProduction ? "YES" : "NO")}");
                    
                    if (f.produceUnits && f.productionUnitKeys.Count > 0)
                    {
                        GUILayout.Label($"Interval: {f.productionIntervalSeconds}s");
                        float nextIn = Mathf.Max(0f, f.productionIntervalSeconds - f.productionTimer);
                        GUILayout.Label($"Next Unit In: {nextIn:F1}s");
                        GUILayout.Label($"Active Produced Units: {f.activeProducedUnits.Count}/{f.maxActiveProducedUnits}");
                    }
                    else
                    {
                        GUILayout.Label("Production: None");
                    }

                    string rallyText = f.useRallyPoint ? $"Set ({f.rallyX:F0}, {f.rallyZ:F0})" : "Not Set";
                    GUILayout.Label($"Rally Point: {rallyText}");

                    // Queue
                    GUILayout.Label($"Queue (Current index: {f.currentProductionIndex}):");
                    if (f.productionUnitKeys.Count == 0)
                    {
                        GUILayout.Label("  [Empty]");
                    }
                    else
                    {
                        for (int qi = 0; qi < f.productionUnitKeys.Count; qi++)
                        {
                            GUILayout.BeginHorizontal();
                            string arrow = (qi == f.currentProductionIndex) ? "➔ " : "   ";
                            GUILayout.Label($"{arrow}{qi + 1}. {f.productionUnitKeys[qi]}");
                            if (isHost)
                            {
                                if (GUILayout.Button("X", GUILayout.Width(20)))
                                {
                                    f.productionUnitKeys.RemoveAt(qi);
                                    if (f.currentProductionIndex >= f.productionUnitKeys.Count)
                                    {
                                        f.currentProductionIndex = 0;
                                    }
                                    manager.SaveInstances();
                                    break;
                                }
                            }
                            GUILayout.EndHorizontal();
                        }
                    }

                    if (isHost)
                    {
                        GUILayout.Space(3);
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(f.enabled ? "Disable Factory" : "Enable Factory"))
                        {
                            f.enabled = !f.enabled;
                            manager.SaveInstances();
                        }
                        if (GUILayout.Button(f.produceUnits ? "Production OFF" : "Production ON"))
                        {
                            f.produceUnits = !f.produceUnits;
                            manager.SaveInstances();
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(f.consumeBudgetForProduction ? "Free Production" : "Paid Production"))
                        {
                            f.consumeBudgetForProduction = !f.consumeBudgetForProduction;
                            manager.SaveInstances();
                        }
                        if (GUILayout.Button("Delete Factory"))
                        {
                            manager.DeleteFactory(f);
                            selectedFactory = null;
                            return;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Space(3);
                        
                        if (GUILayout.Button("Set Rally Point From Aim"))
                        {
                            if (TryGetCurrentPlacement(out Vector3 localRally, out _))
                            {
                                var globalRally = localRally.ToGlobalPosition();
                                f.useRallyPoint = true;
                                f.rallyX = globalRally.x;
                                f.rallyY = globalRally.y;
                                f.rallyZ = globalRally.z;
                                manager.SaveInstances();
                                if (SceneSingleton<GameplayUI>.i != null)
                                {
                                    SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Rally point set for {f.displayName}");
                                }
                            }
                        }

                        if (f.useRallyPoint && GUILayout.Button("Clear Rally Point"))
                        {
                            f.useRallyPoint = false;
                            manager.SaveInstances();
                        }

                        UnitDefinition currentSelected = GetSelectedDefinition();
                        if (currentSelected != null)
                        {
                            if (GUILayout.Button($"Add Selected to Queue ({currentSelected.unitName})"))
                            {
                                f.productionUnitKeys.Add(currentSelected.unitName);
                                manager.SaveInstances();
                            }
                        }

                        if (f.productionUnitKeys.Count > 0 && GUILayout.Button("Clear Queue"))
                        {
                            f.productionUnitKeys.Clear();
                            f.currentProductionIndex = 0;
                            manager.SaveInstances();
                        }
                    }
                    else
                    {
                        GUILayout.Label("Editing restricted to Host.");
                    }
                }
            }

            GUILayout.Space(5);

            // 4. Creation (Host only)
            if (isHost)
            {
                GUILayout.Box("══ Create Factory ══");
                if (presets == null || presets.Count == 0)
                {
                    GUILayout.Label("No factory presets configured.");
                }
                else
                {
                    GUILayout.Label("Select Preset:");
                    string[] presetNames = presets.Select(p => p.presetName).ToArray();
                    if (selectedPresetIndex >= presetNames.Length) selectedPresetIndex = 0;
                    selectedPresetIndex = GUILayout.SelectionGrid(selectedPresetIndex, presetNames, 2);

                    string currentPresetName = presetNames[selectedPresetIndex];

                    if (!string.IsNullOrEmpty(armedFactoryPresetName) && string.Equals(armedFactoryPresetName, currentPresetName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (GUILayout.Button("Cancel Factory Placement", GUILayout.Height(30)))
                        {
                            armedFactoryPresetName = null;
                            ghost.Clear();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Arm Factory Placement", GUILayout.Height(30)))
                        {
                            armedFactoryPresetName = currentPresetName;
                            if (economyManager != null) economyManager.DisarmDeployment();
                            ghost.Clear();
                            if (SceneSingleton<GameplayUI>.i != null)
                            {
                                SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Armed placement for {currentPresetName}. Click in world/map to place.");
                            }
                        }
                    }

                    Unit aimed = GetAimedUnit();
                    if (aimed != null)
                    {
                        if (GUILayout.Button($"Create Factory From Selected: {aimed.unitName}"))
                        {
                            var created = manager.CreateFactoryFromUnit(aimed, currentPresetName);
                            if (created != null)
                            {
                                selectedFactory = created;
                                if (SceneSingleton<GameplayUI>.i != null)
                                {
                                    SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Attached {created.displayName} to {aimed.unitName}");
                                }
                            }
                        }
                    }
                }

                GUILayout.Space(5);
                GUILayout.Box("══ Bulk / Config Operations ══");
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save Factories"))
                {
                    manager.SaveInstances();
                    if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory instances saved.");
                    }
                }
                if (GUILayout.Button("Load Factories"))
                {
                    manager.LoadInstances();
                    manager.AutoDetectFactories();
                    if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory instances loaded.");
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Start All"))
                {
                    foreach (var f in manager.activeFactories) f.enabled = true;
                    manager.SaveInstances();
                }
                if (GUILayout.Button("Stop All"))
                {
                    foreach (var f in manager.activeFactories) f.enabled = false;
                    manager.SaveInstances();
                }
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Reload Factory Config"))
                {
                    manager.ReloadConfig();
                    if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory config reloaded.");
                    }
                }
            }
            else
            {
                GUILayout.Label("Creation & bulk operations restricted to Host.");
            }
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
