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
    [System.Serializable]
    public class CustomGroupData
    {
        public string groupName;
        public System.Collections.Generic.List<string> unitNames = new System.Collections.Generic.List<string>();
        public float spacing = 30f;
        public string formation = "Column";
        public bool stationary = false;
        public float altitude = 0f;
    }

    public class HorusManager : MonoBehaviour
    {
        public static HorusManager Instance { get; private set; }
        public bool IsHorusActive => horusActive;

        private bool horusActive = false;
        private Rect windowRect = new Rect(20, 20, 340, 700);
        
        private int selectedFactionIndex = 0;
        private int selectedCategoryIndex = 0;
        private int selectedUnitIndex = 0;
        private string armedFactoryPresetName = null;
        
        private float spawnAltitude = 0f;
        private float spawnYaw = 0f;
        private string altitudeInputText = "0";
        private string yawInputText = "0";
        private bool hideGUI = false;
        private bool isMouseOverGUI = false;
        private bool mapSpawnMode = false;
        private bool mapOpenedByHorus = false;
        private bool mapGhostNoticeLogged = false;
        private bool snapToGround = true;
        private bool alignToSurface = false;
        private Vector3 lastSurfaceNormal = Vector3.up;

        // Ghost preview (local-only, non-networked)
        private readonly GhostPreview ghost = new GhostPreview();
        private bool ghostPreviewEnabled = true;
        private UnitDefinition ghostBuildFailedDef;

        // Units spawned by Horus this session (for safe deletion)
        private readonly HashSet<Unit> horusSpawnedUnits = new HashSet<Unit>();
        private bool scrollAxisAvailable = true;

        // Grid snapping
        private bool gridSnapEnabled = false;
        private float gridSize = 10f;
        private static readonly float[] gridSizeOptions = { 1f, 5f, 10f, 25f, 50f, 100f };
        private string gridSizeInputText = "10";

        // Rotation snapping
        private bool rotationSnapEnabled = false;
        private float rotationSnapStep = 15f;
        private static readonly float[] rotationSnapOptions = { 1f, 5f, 15f, 45f, 90f };

        // UI section foldouts
        private bool showPlacementTools = true;
        private bool showMapTools = true;
        private bool showControls = false;
        private bool showDeletionTools = true;
        private bool showGroupTools = false;
        private Vector2 mainScroll;

        private Vector2 scrollPosition;

        private float rotationX = 0f;
        private float rotationY = 0f;

        // Ocean snap settings
        private bool autoOceanSnapForShips = true;
        private bool oceanSnapActive = false;

        // Camera restore state
        private CameraBaseState savedCameraState = null;
        private Unit savedFollowingUnit = null;
        private bool savedFlightControlsEnabled = true;

        // Economy: delegated to RtsEconomyManager
        private RtsEconomyManager economyManager;

        // Deletion settings
        private float deleteRange = 50f;
        private string deleteRangeInputText = "50";
        private static readonly float[] deleteRangeOptions = { 25f, 50f, 100f };

        // Groups & Formations
        private bool enableGroupSpawn = false;
        private bool spawnStationary = false;
        private int selectedGroupPresetIndex = 0;
        private readonly string[] groupPresetNames = { "Selected Unit", "Convoy", "Armored Group", "Squadron", "Air Patrol", "Naval Group", "Anti-Air Battery", "Base Defense", "Custom Group" };
        
        private int groupCount = 4;
        private float groupSpacing = 30f;
        private string groupSpacingInputText = "30";
        private int selectedFormationIndex = 1; // Default to Column
        private readonly string[] formationNames = { "Line", "Column", "Grid", "Circle", "V Formation" };
        
        // Custom group state
        private string customGroupName = "New Group";
        private readonly System.Collections.Generic.List<UnitDefinition> customGroupUnits = new System.Collections.Generic.List<UnitDefinition>();
        private readonly System.Collections.Generic.List<string> savedCustomGroupNames = new System.Collections.Generic.List<string>();
        private int selectedSavedGroupIndex = 0;

        // Cached deduplicated unit lists
        private int cachedCategoryIndex = -1;
        private List<UnitDefinition> cachedUnitList;

        private void Awake()
        {
            Instance = this;
            ghostPreviewEnabled = HorusPlugin.EnableGhostPreview.Value;
            autoOceanSnapForShips = HorusPlugin.AutoOceanSnapForShips.Value;
            oceanSnapActive = HorusPlugin.OceanSnapActive.Value;
            deleteRange = HorusPlugin.DeleteRange.Value;
            deleteRangeInputText = deleteRange.ToString("0");
            enableGroupSpawn = HorusPlugin.EnableGroupSpawn.Value;
            spawnStationary = HorusPlugin.SpawnGroundUnitsStationary.Value;
            
            // Initialize economy manager
            economyManager = new RtsEconomyManager();

            RefreshSavedCustomGroups();
            HorusPlugin.Logger.LogInfo("HorusManager created.");
        }

        private void Update()
        {
            // Tick economy manager (income, cleanup) even if Horus overlay is not active
            economyManager?.Tick();

            if (Input.GetKeyDown(HorusPlugin.HotkeyToggleMode.Value))
            {
                HorusPlugin.Logger.LogInfo("Toggle Horus Mode key pressed.");
                ToggleHorusMode();
            }

            if (!horusActive) return;

            // If the mission ended while active, tear down the preview safely.
            if (!HorusPermissions.InMission())
            {
                ghost.Clear();
                horusSpawnedUnits.Clear();
                return;
            }

            if (Input.GetKeyDown(HorusPlugin.HotkeyToggleUI.Value))
            {
                hideGUI = !hideGUI;
            }

            bool mapOpen = mapSpawnMode && DynamicMap.mapMaximized;

            // Read placement scroll shortcuts FIRST, before anything could consume the delta.
            HandleScrollShortcuts(mapOpen);

            if (mapOpen)
            {
                // The map is a fullscreen overlay, so the world ghost is not shown here.
                ghost.SetVisible(false);
                if (!mapGhostNoticeLogged)
                {
                    HorusPlugin.Logger.LogInfo("Ghost preview is hidden while the map is open; the unit spawns at the map cursor on click.");
                    mapGhostNoticeLogged = true;
                }

                // Only spawn when clicking on the map itself, not over the Horus window
                if (Input.GetMouseButtonDown(0) && !isMouseOverGUI)
                {
                    HandleMapSpawnClick();
                }
                return; // Don't process camera/world input while map is open
            }

            mapGhostNoticeLogged = false;

            // If the map was closed (e.g. user pressed M), leave map spawn mode
            if (mapSpawnMode && !DynamicMap.mapMaximized)
            {
                mapSpawnMode = false;
                mapOpenedByHorus = false;
            }

            // Only process camera/world input when NOT hovering over the GUI window
            if (!isMouseOverGUI)
            {
                ManageCameraAndInput();
                UpdateGhost();

                if (Input.GetMouseButtonDown(0))
                {
                    HandleSpawnClick();
                }

                if (Input.GetMouseButtonDown(2))
                {
                    HandleDeleteClick();
                }
            }
            else
            {
                // Mouse over the Horus window: keep the last ghost visible but don't move it.
                if (Input.GetMouseButton(1))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        /// <summary>
        /// Reads mouse-wheel placement shortcuts. Reads the wheel via the game's own Rewired
        /// "Zoom View" axis first (reliable here even when Input.mouseScrollDelta reports 0), runs
        /// before any code that could reset input axes, and never fights the free camera (its zoom
        /// is suppressed by the Harmony patch while Horus is active).
        /// Ctrl = altitude, Alt = yaw, Shift = larger step. No modifier leaves placement untouched.
        /// </summary>
        private void HandleScrollShortcuts(bool mapOpen)
        {
            // Over the Horus window: let GUI scroll views use the wheel.
            if (isMouseOverGUI) return;

            // While the map is open, only act if allowed (the map also zooms with the wheel).
            if (mapOpen && !HorusPlugin.AllowScrollWhileMapOpen.Value) return;

            float scroll = ReadScrollDelta();
            if (Mathf.Approximately(scroll, 0f)) return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (!ctrl && !alt) return; // plain scroll should not change placement

            float dir = scroll > 0f ? 1f : -1f;

            if (ctrl)
            {
                float step = shift ? HorusPlugin.AltitudeStepLarge.Value : HorusPlugin.AltitudeStep.Value;
                spawnAltitude = Mathf.Clamp(Mathf.Round(spawnAltitude + dir * step), 0f, 50000f);
                altitudeInputText = spawnAltitude.ToString("0");
                HorusPlugin.Logger.LogInfo($"Horus scroll: altitude -> {spawnAltitude:F0} m");
            }
            else // alt
            {
                float step = shift ? HorusPlugin.RotationStepLarge.Value : HorusPlugin.RotationStep.Value;
                spawnYaw = ApplyRotationSnap(NormalizeAngle(spawnYaw + dir * step));
                yawInputText = spawnYaw.ToString("0");
                HorusPlugin.Logger.LogInfo($"Horus scroll: yaw -> {spawnYaw:F0} deg");
            }
        }

        /// <summary>
        /// Robust mouse-wheel reader. Primary source is the game's Rewired "Zoom View" axis (the
        /// mouse wheel in this game); falls back to Unity's mouseScrollDelta and the legacy axis.
        /// </summary>
        private float ReadScrollDelta()
        {
            float scroll = 0f;

            if (GameManager.playerInput != null)
            {
                try { scroll = GameManager.playerInput.GetAxis("Zoom View"); }
                catch { /* action not present; fall through to Unity input */ }
            }

            if (Mathf.Approximately(scroll, 0f))
            {
                scroll = Input.mouseScrollDelta.y;
            }

            if (Mathf.Approximately(scroll, 0f) && scrollAxisAvailable)
            {
                try { scroll = Input.GetAxis("Mouse ScrollWheel"); }
                catch { scrollAxisAvailable = false; }
            }

            if (HorusPlugin.InvertScrollDirection.Value) scroll = -scroll;
            return scroll;
        }

        private void ManageCameraAndInput()
        {
            if (Input.GetMouseButton(1))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                
                if (CameraStateManager.i != null && GameManager.playerInput != null)
                {
                    Transform camT = CameraStateManager.i.transform;
                    float pan = GameManager.playerInput.GetAxis("Pan View");
                    float tilt = GameManager.playerInput.GetAxis("Tilt View");
                    
                    if (pan == 0f && tilt == 0f)
                    {
                        pan = Input.GetAxis("Mouse X") * 0.5f;
                        tilt = Input.GetAxis("Mouse Y") * 0.5f;
                    }

                    float sens = 0.5f * PlayerSettings.viewSensitivity;
                    float invertPitch = PlayerSettings.viewInvertPitch ? -1f : 1f;

                    rotationX += pan * sens * 3f;
                    rotationY += tilt * sens * 3f * invertPitch;
                    
                    rotationY = Mathf.Clamp(rotationY, -89f, 89f);
                    camT.rotation = Quaternion.Euler(rotationY, rotationX, 0);
                }
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (CameraStateManager.i != null)
            {
                Transform camT = CameraStateManager.i.transform;
                Vector3 dir = Vector3.zero;
                
                if (GameManager.playerInput != null)
                {
                    float fwd = GameManager.playerInput.GetAxis("Move Longitudinal");
                    float right = GameManager.playerInput.GetAxis("Move Lateral");
                    float up = GameManager.playerInput.GetAxis("Move Vertical");
                    
                    if (fwd == 0f && right == 0f && up == 0f)
                    {
                        if (Input.GetKey(KeyCode.W)) fwd = 1f;
                        if (Input.GetKey(KeyCode.S)) fwd = -1f;
                        if (Input.GetKey(KeyCode.A)) right = -1f;
                        if (Input.GetKey(KeyCode.D)) right = 1f;
                        if (Input.GetKey(KeyCode.E)) up = 1f;
                        if (Input.GetKey(KeyCode.Q)) up = -1f;
                    }
                    
                    dir = camT.forward * fwd + camT.right * right + camT.up * up;
                }
                else
                {
                    if (Input.GetKey(KeyCode.W)) dir += camT.forward;
                    if (Input.GetKey(KeyCode.S)) dir -= camT.forward;
                    if (Input.GetKey(KeyCode.A)) dir -= camT.right;
                    if (Input.GetKey(KeyCode.D)) dir += camT.right;
                    if (Input.GetKey(KeyCode.E)) dir += camT.up;
                    if (Input.GetKey(KeyCode.Q)) dir -= camT.up;
                }

                float currentSpeed = 800f;
                if (Input.GetKey(KeyCode.LeftShift)) currentSpeed *= 4f;

                camT.position += dir * currentSpeed * Time.unscaledDeltaTime;
            }
        }

        private void ToggleHorusMode()
        {
            if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)
            {
                HorusPlugin.Logger.LogWarning($"Cannot activate Horus Mode. Current GameState: {GameManager.gameState}");
                return;
            }

            horusActive = !horusActive;
            ExitMapSpawnMode();
            if (!horusActive)
            {
                ghost.Clear();
                armedFactoryPresetName = null;
                
                // Restore flight controls
                GameManager.flightControlsEnabled = savedFlightControlsEnabled;

                // Camera / control restore when exiting Horus Mode
                if (CameraStateManager.i != null)
                {
                    bool hasValidUnit = false;
                    try
                    {
                        hasValidUnit = savedFollowingUnit != null && savedFollowingUnit.gameObject != null && !savedFollowingUnit.disabled;
                    }
                    catch { }

                    if (hasValidUnit)
                    {
                        CameraStateManager.i.SetFollowingUnit(savedFollowingUnit);
                        if (savedCameraState != null && savedCameraState != CameraStateManager.i.freeState)
                        {
                            CameraStateManager.i.SwitchState(savedCameraState);
                        }
                    }
                    else
                    {
                        if (savedFollowingUnit != null)
                        {
                            if (SceneSingleton<GameplayUI>.i != null)
                            {
                                SceneSingleton<GameplayUI>.i.GameMessage("Horus Mod: Previous unit was destroyed, camera remains free.");
                            }
                            HorusPlugin.Logger.LogWarning("Horus camera restore: Saved unit was destroyed while in Horus Mode.");
                        }
                        
                        CameraStateManager.i.SetFollowingUnit(null);
                        CameraStateManager.i.SwitchState(CameraStateManager.i.freeState);
                    }
                }
                savedCameraState = null;
                savedFollowingUnit = null;
            }
            
            if (horusActive && CameraStateManager.i != null)
            {
                // Save previous controlled unit/camera state before entering Horus Mode
                savedCameraState = CameraStateManager.i.currentState;
                savedFollowingUnit = CameraStateManager.i.followingUnit;

                // Temporarily disable flight controls to prevent input fighting with the aircraft
                savedFlightControlsEnabled = GameManager.flightControlsEnabled;
                GameManager.flightControlsEnabled = false;

                CameraStateManager.i.SwitchState(CameraStateManager.i.freeState);
                CameraStateManager.i.SetFollowingUnit(null);
                rotationX = CameraStateManager.i.transform.eulerAngles.y;
                rotationY = CameraStateManager.i.transform.eulerAngles.x;
            }
            
            HorusPlugin.Logger.LogInfo($"Horus Mode toggled: {horusActive}");

            cachedCategoryIndex = -1;
        }

        /// <summary>
        /// Enables map spawn mode and opens the in-game map so the user can click to place units.
        /// </summary>
        private void EnterMapSpawnMode()
        {
            mapSpawnMode = true;
            HorusPlugin.Logger.LogInfo("Map Spawn Mode: ON");

            try
            {
                var map = SceneSingleton<DynamicMap>.i;
                if (map != null && !DynamicMap.mapMaximized)
                {
                    map.Maximize();
                    mapOpenedByHorus = true;
                }
            }
            catch (Exception ex)
            {
                HorusPlugin.Logger.LogWarning($"Could not auto-open map: {ex.Message}");
            }
        }

        /// <summary>
        /// Disables map spawn mode and closes the map again if Horus opened it.
        /// </summary>
        private void ExitMapSpawnMode()
        {
            if (mapSpawnMode)
            {
                HorusPlugin.Logger.LogInfo("Map Spawn Mode: OFF");
            }

            try
            {
                if (mapOpenedByHorus && DynamicMap.mapMaximized)
                {
                    var map = SceneSingleton<DynamicMap>.i;
                    if (map != null)
                    {
                        map.Minimize();
                    }
                }
            }
            catch (Exception ex)
            {
                HorusPlugin.Logger.LogWarning($"Could not auto-close map: {ex.Message}");
            }

            mapSpawnMode = false;
            mapOpenedByHorus = false;
        }

        private void OnGUI()
        {
            if (!horusActive || hideGUI) return;

            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            isMouseOverGUI = windowRect.Contains(mouseScreenPos);

            if (mapSpawnMode && DynamicMap.mapMaximized)
            {
                DrawMapSpawnOverlay();
            }

            windowRect = GUI.Window(999, windowRect, DrawHorusWindow, $"⚡ Horus Editor v{HorusPlugin.PluginVersion}");
        }

        /// <summary>
        /// Draws an on-screen hint banner and a crosshair while placing units from the map.
        /// </summary>
        private void DrawMapSpawnOverlay()
        {
            Color prevColor = GUI.color;

            // Top hint banner (GUI.Box centers its text by default)
            Rect banner = new Rect(Screen.width * 0.5f - 230f, 12f, 460f, 28f);
            GUI.Box(banner, "MAP SPAWN — left-click the map to place the selected unit");

            // Crosshair at the cursor (only over the map, not the Horus window)
            if (!isMouseOverGUI)
            {
                float mx = Input.mousePosition.x;
                float my = Screen.height - Input.mousePosition.y;
                GUI.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                GUI.DrawTexture(new Rect(mx - 10f, my - 1f, 20f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(mx - 1f, my - 10f, 2f, 20f), Texture2D.whiteTexture);
            }

            GUI.color = prevColor;
        }

        private void DrawHorusWindow(int windowID)
        {
            mainScroll = GUILayout.BeginScrollView(mainScroll);

            if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)
            {
                GUILayout.Label("Status: Not in mission (Game State is " + GameManager.gameState + ")");
            }

            if (Encyclopedia.i == null)
            {
                GUILayout.Label("Error: Encyclopedia not loaded yet.");
                GUILayout.EndScrollView();
                GUI.DragWindow();
                return;
            }

            // --- Permission / mode status ---
            GUILayout.Label("Mode: " + HorusPermissions.GetModeLabel());
            if (HorusPermissions.IsMultiplayerClient())
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                GUILayout.Label("Permission: host permission required. Spawning disabled.");
                GUI.color = prev;
            }

            // --- Faction Selection ---
            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0)
            {
                GUILayout.Label("Status: No playable factions found.");
            }
            else
            {
                if (selectedFactionIndex >= factions.Count) selectedFactionIndex = 0;
                GUILayout.Label("Faction:");
                string[] factionNames = factions.Select(f => f.factionName).ToArray();
                selectedFactionIndex = GUILayout.SelectionGrid(selectedFactionIndex, factionNames, 2);
            }

            // --- Economy Mode Selector ---
            GUILayout.Space(5);
            GUILayout.Label("Economy Mode:");
            int oldMode = (economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander) ? 1 : 0;
            int newMode = GUILayout.SelectionGrid(oldMode, new string[] { "Sandbox Mode", "RTS Commander Mode" }, 2);
            if (newMode != oldMode)
            {
                if (economyManager != null)
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
            }

            if (economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander)
            {
                DrawRtsCommanderUI();
                DrawRtsFactoriesUI();
            }

            // --- Category Selection ---
            GUILayout.Space(5);
            GUILayout.Label("Category:");
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

            // --- Unit List ---
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

            // --- Placement values ---
            GUILayout.Space(5);
            GUILayout.Label($"Altitude: {spawnAltitude:F0} m  |  Yaw: {spawnYaw:F0}°");

            // Altitude slider
            float newAlt = GUILayout.HorizontalSlider(spawnAltitude, 0f, 15000f);
            if (Mathf.Abs(newAlt - spawnAltitude) > 0.01f)
            {
                spawnAltitude = Mathf.Round(newAlt / 50f) * 50f;
                altitudeInputText = spawnAltitude.ToString("0");
            }

            // Custom altitude / yaw input
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

            // Preset altitude buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0m")) { spawnAltitude = 0f; altitudeInputText = "0"; }
            if (GUILayout.Button("100m")) { spawnAltitude = 100f; altitudeInputText = "100"; }
            if (GUILayout.Button("1k")) { spawnAltitude = 1000f; altitudeInputText = "1000"; }
            if (GUILayout.Button("3k")) { spawnAltitude = 3000f; altitudeInputText = "3000"; }
            if (GUILayout.Button("5k")) { spawnAltitude = 5000f; altitudeInputText = "5000"; }
            GUILayout.EndHorizontal();

            // Rotation yaw slider
            GUILayout.Space(3);
            float newYaw = GUILayout.HorizontalSlider(spawnYaw, 0f, 360f);
            if (Mathf.Abs(newYaw - spawnYaw) > 0.01f)
            {
                spawnYaw = ApplyRotationSnap(newYaw);
                yawInputText = spawnYaw.ToString("0");
            }

            // Preset rotation buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0°")) { spawnYaw = 0f; yawInputText = "0"; }
            if (GUILayout.Button("45°")) { spawnYaw = 45f; yawInputText = "45"; }
            if (GUILayout.Button("90°")) { spawnYaw = 90f; yawInputText = "90"; }
            if (GUILayout.Button("180°")) { spawnYaw = 180f; yawInputText = "180"; }
            if (GUILayout.Button("270°")) { spawnYaw = 270f; yawInputText = "270"; }
            GUILayout.EndHorizontal();

            // Reset buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Altitude"))
            {
                spawnAltitude = 0f;
                altitudeInputText = "0";
            }
            if (GUILayout.Button("Reset Yaw"))
            {
                spawnYaw = 0f;
                yawInputText = "0";
            }
            GUILayout.EndHorizontal();

            // --- Placement Tools (preview / snapping) ---
            GUILayout.Space(5);
            if (Section("Placement Tools", ref showPlacementTools))
            {
                ghostPreviewEnabled = GUILayout.Toggle(ghostPreviewEnabled, " Ghost Preview");
                snapToGround = GUILayout.Toggle(snapToGround, " Snap to Ground (ground units on terrain)");
                alignToSurface = GUILayout.Toggle(alignToSurface, " Align to Surface Normal (experimental)");
                autoOceanSnapForShips = GUILayout.Toggle(autoOceanSnapForShips, " Auto Ocean Snap for Ships");
                oceanSnapActive = GUILayout.Toggle(oceanSnapActive, " Snap to Ocean Level");
                GUILayout.Label($"Sea Level: {GetOceanLevel():F1}m");

                // Grid snapping
                GUILayout.Space(3);
                gridSnapEnabled = GUILayout.Toggle(gridSnapEnabled, " Grid Snap (aligns position to spacing)");
                if (gridSnapEnabled)
                {
                    GUILayout.Label("Grid size:");
                    int gi = IndexOf(gridSizeOptions, gridSize);
                    string[] gridLabels = { "1m", "5m", "10m", "25m", "50m", "100m" };
                    int newGi = GUILayout.SelectionGrid(gi < 0 ? 2 : gi, gridLabels, 3);
                    if (newGi != gi && newGi >= 0 && newGi < gridSizeOptions.Length)
                    {
                        gridSize = gridSizeOptions[newGi];
                        gridSizeInputText = gridSize.ToString("0");
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Custom:", GUILayout.Width(55));
                    gridSizeInputText = GUILayout.TextField(gridSizeInputText, GUILayout.Width(60));
                    if (GUILayout.Button("Set", GUILayout.Width(40)))
                    {
                        if (float.TryParse(gridSizeInputText, out float gv) && gv > 0f)
                        {
                            gridSize = gv;
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                // Rotation snapping
                GUILayout.Space(3);
                rotationSnapEnabled = GUILayout.Toggle(rotationSnapEnabled, " Rotation Snap (aligns yaw to increments)");
                if (rotationSnapEnabled)
                {
                    GUILayout.Label("Rotation step:");
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

                // Status lines
                GUILayout.Space(3);
                GUILayout.Label("Ghost Preview: " + (ghostPreviewEnabled ? "ON" : "OFF"));
                GUILayout.Label("Grid Snap: " + (gridSnapEnabled ? gridSize.ToString("0") + "m" : "OFF"));
                GUILayout.Label("Rotation Snap: " + (rotationSnapEnabled ? rotationSnapStep.ToString("0") + "°" : "OFF"));
                GUILayout.Label("Ocean Snap: " + (oceanSnapActive || (autoOceanSnapForShips && selectedCategoryIndex == 2) ? "ON" : "OFF"));
            }

            // --- Safety & Deletion ---
            GUILayout.Space(5);
            if (Section("Safety & Deletion", ref showDeletionTools))
            {
                HorusPlugin.AllowDeletingNonHorusUnits.Value = GUILayout.Toggle(HorusPlugin.AllowDeletingNonHorusUnits.Value, " Allow Deleting Non-Horus Units");
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

            // --- Groups & Formations ---
            GUILayout.Space(5);
            if (Section("Groups & Formations", ref showGroupTools))
            {
                bool prevGroup = enableGroupSpawn;
                enableGroupSpawn = GUILayout.Toggle(enableGroupSpawn, " Enable Group Spawning");
                if (enableGroupSpawn != prevGroup)
                {
                    HorusPlugin.EnableGroupSpawn.Value = enableGroupSpawn;
                    ghost.Clear(); // force redraw
                }

                if (enableGroupSpawn)
                {
                    bool prevStationary = spawnStationary;
                    spawnStationary = GUILayout.Toggle(spawnStationary, " Spawn Ground Units Stationary");
                    if (spawnStationary != prevStationary)
                    {
                        HorusPlugin.SpawnGroundUnitsStationary.Value = spawnStationary;
                    }

                    // Preset selection
                    GUILayout.Label("Preset Group:");
                    int oldPreset = selectedGroupPresetIndex;
                    selectedGroupPresetIndex = GUILayout.SelectionGrid(selectedGroupPresetIndex, groupPresetNames, 3);
                    if (oldPreset != selectedGroupPresetIndex)
                    {
                        OnGroupPresetChanged(oldPreset, selectedGroupPresetIndex);
                    }

                    if (selectedGroupPresetIndex == 8) // Custom Group Editor
                    {
                        GUILayout.Space(5);
                        GUILayout.Box("CUSTOM GROUP EDITOR");
                        
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Group Name:", GUILayout.Width(80));
                        customGroupName = GUILayout.TextField(customGroupName, GUILayout.Width(150));
                        GUILayout.EndHorizontal();

                        UnitDefinition currentSelected = GetSelectedDefinition();
                        if (currentSelected != null)
                        {
                            if (GUILayout.Button($"Add Selected Unit ({currentSelected.unitName})"))
                            {
                                customGroupUnits.Add(currentSelected);
                                groupCount = customGroupUnits.Count;
                                ghost.Clear();
                            }
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
                        if (GUILayout.Button("Save Group"))
                        {
                            SaveCustomGroup(customGroupName);
                        }
                        if (GUILayout.Button("Clear Group"))
                        {
                            customGroupUnits.Clear();
                            groupCount = 0;
                            ghost.Clear();
                        }
                        GUILayout.EndHorizontal();

                        // Saved custom groups loading
                        if (savedCustomGroupNames.Count > 0)
                        {
                            if (selectedSavedGroupIndex >= savedCustomGroupNames.Count) selectedSavedGroupIndex = 0;
                            
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Saved Group:");
                            if (GUILayout.Button("<", GUILayout.Width(25)))
                            {
                                selectedSavedGroupIndex = (selectedSavedGroupIndex - 1 + savedCustomGroupNames.Count) % savedCustomGroupNames.Count;
                            }
                            GUILayout.Label(savedCustomGroupNames[selectedSavedGroupIndex], GUILayout.ExpandWidth(true));
                            if (GUILayout.Button(">", GUILayout.Width(25)))
                            {
                                selectedSavedGroupIndex = (selectedSavedGroupIndex + 1) % savedCustomGroupNames.Count;
                            }
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
                        // Standard sliders for homogenous/presets
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
                        if (oldForm != selectedFormationIndex)
                        {
                            ghost.Clear();
                        }
                    }
                }
            }

            // --- Map Spawn ---
            GUILayout.Space(5);
            if (Section("Map Spawn", ref showMapTools))
            {
                string mapBtnLabel = mapSpawnMode ? "■ Map Spawn: ON" : "▶ Map Spawn: OFF";
                if (GUILayout.Button(mapBtnLabel))
                {
                    if (mapSpawnMode) ExitMapSpawnMode();
                    else EnterMapSpawnMode();
                }
                if (mapSpawnMode)
                {
                    GUILayout.Label("Left-click the map to spawn at the cursor.");
                    GUILayout.Label("Press M to open/close the map.");
                }
            }

            // --- Controls ---
            GUILayout.Space(5);
            if (Section("Controls", ref showControls))
            {
                GUILayout.Label("Left Click: Spawn  |  Mid Click: Delete");
                GUILayout.Label("Ctrl+Scroll: Altitude");
                GUILayout.Label("Alt+Scroll: Yaw");
                GUILayout.Label("Shift: Larger step (with Ctrl/Alt)");
                GUILayout.Label("RMB: Camera look  |  WASD/QE: Move");
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        /// <summary>Draws a foldout button and returns whether the section is open.</summary>
        private static bool Section(string title, ref bool open)
        {
            if (GUILayout.Button((open ? "▼ " : "▶ ") + title))
            {
                open = !open;
            }
            return open;
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

        private void DrawSelectedFactoryPanel(RtsFactoryManager manager, bool isHost)
        {
            if (selectedFactory == null) return;
            if (!manager.activeFactories.Contains(selectedFactory))
            {
                selectedFactory = null;
                return;
            }

            var f = selectedFactory;
            int selectedIndex = manager.activeFactories.IndexOf(f);
            int activeProduced = f.activeProducedUnits != null ? f.activeProducedUnits.Count : 0;
            float nextIn = Mathf.Max(0f, f.productionIntervalSeconds - f.productionTimer);
            string rallyText = f.useRallyPoint ? $"Set ({f.rallyX:F0}, {f.rallyZ:F0})" : "Not Set";

            GUILayout.Box($"Factory {selectedIndex + 1}: {f.displayName}");
            GUILayout.Label($"Selected factory index/name: {selectedIndex + 1}/{manager.activeFactories.Count} - {f.displayName}");
            GUILayout.Label($"Faction: {f.factionName}");
            GUILayout.Label($"Type: {f.factoryType}");
            GUILayout.Label($"Enabled/disabled: {(f.enabled ? "Enabled" : "Disabled")}");
            GUILayout.Label($"Income per minute: +{f.incomePerMinute:F0}");
            GUILayout.Label($"Production enabled/disabled: {(f.produceUnits ? "Enabled" : "Disabled")}");
            GUILayout.Label($"Production interval: {f.productionIntervalSeconds:F0}s");
            GUILayout.Label($"Next production timer: {nextIn:F1}s");
            GUILayout.Label($"Active produced units count: {activeProduced}");
            GUILayout.Label($"Max active produced units: {f.maxActiveProducedUnits}");
            GUILayout.Label($"Rally point status: {rallyText}");
            GUILayout.Label($"Anchor status: {manager.GetAnchorStatus(f)}");
            GUILayout.Label($"Consumes budget: {(f.consumeBudgetForProduction ? "Yes" : "No")}");

            GUILayout.Label($"Production queue (current index: {f.currentProductionIndex}):");
            if (f.productionUnitKeys == null || f.productionUnitKeys.Count == 0)
            {
                GUILayout.Label("  [Empty]");
            }
            else
            {
                if (selectedFactoryQueueIndex >= f.productionUnitKeys.Count) selectedFactoryQueueIndex = 0;
                for (int qi = 0; qi < f.productionUnitKeys.Count; qi++)
                {
                    string current = qi == f.currentProductionIndex ? "> " : "  ";
                    string selected = qi == selectedFactoryQueueIndex ? "* " : "  ";
                    if (GUILayout.Button($"{selected}{current}{qi + 1}. {f.productionUnitKeys[qi]}"))
                    {
                        selectedFactoryQueueIndex = qi;
                    }
                }
            }

            if (!isHost)
            {
                DrawHostOnlyButton("Enable / Disable Factory");
                DrawHostOnlyButton("Delete Selected Factory");
                DrawHostOnlyButton("Add Selected Unit To Production Queue");
                DrawHostOnlyButton("Set Rally Point From Aim");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(f.enabled ? "Disable Factory" : "Enable Factory"))
            {
                manager.SetFactoryEnabled(f, !f.enabled);
            }
            if (GUILayout.Button(f.produceUnits ? "Production OFF" : "Production ON"))
            {
                manager.SetFactoryProductionEnabled(f, !f.produceUnits);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(f.consumeBudgetForProduction ? "Free Production" : "Paid Production"))
            {
                manager.SetFactoryConsumesBudget(f, !f.consumeBudgetForProduction);
            }
            if (GUILayout.Button("Delete Selected Factory"))
            {
                manager.DeleteFactory(f);
                selectedFactory = null;
                return;
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Set Rally Point From Aim"))
            {
                if (TryGetCurrentPlacement(out Vector3 localRally, out _))
                {
                    manager.SetRallyPoint(f, localRally);
                    if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Rally point set for {f.displayName}");
                }
                else if (SceneSingleton<GameplayUI>.i != null)
                {
                    SceneSingleton<GameplayUI>.i.GameMessage("Horus: No valid rally point.");
                }
            }

            if (GUILayout.Button("Clear Rally Point"))
            {
                manager.ClearRallyPoint(f);
            }

            UnitDefinition currentSelected = GetSelectedDefinition();
            if (currentSelected != null && GUILayout.Button($"Add Selected Unit To Production Queue ({currentSelected.unitName})"))
            {
                manager.AddUnitToProductionQueue(f, currentSelected);
            }

            if (f.productionUnitKeys != null && f.productionUnitKeys.Count > 0 && GUILayout.Button("Remove Selected Queue Item"))
            {
                manager.RemoveProductionQueueItem(f, selectedFactoryQueueIndex);
            }

            if (f.productionUnitKeys != null && f.productionUnitKeys.Count > 0 && GUILayout.Button("Clear Queue"))
            {
                manager.ClearProductionQueue(f);
            }
        }

        private void DrawFactoryCreationPanel(RtsFactoryManager manager, List<FactoryPreset> presets, bool isHost)
        {
            GUILayout.Box("Create Factory");
            if (presets == null || presets.Count == 0)
            {
                GUILayout.Label("No factory presets configured.");
                return;
            }

            GUILayout.Label("Select Preset:");
            string[] presetNames = presets.Select(p => p.presetName).ToArray();
            if (selectedPresetIndex >= presetNames.Length) selectedPresetIndex = 0;
            selectedPresetIndex = GUILayout.SelectionGrid(selectedPresetIndex, presetNames, 2);
            string currentPresetName = presetNames[selectedPresetIndex];

            if (!isHost)
            {
                DrawHostOnlyButton("Create Factory Here");
                DrawHostOnlyButton("Create Factory From Aimed Unit");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Factory Here", GUILayout.Height(30)))
            {
                if (CanRunFactoryCreateAction())
                {
                    if (TryGetCurrentPlacement(out Vector3 localFactory, out float yaw))
                    {
                        var created = manager.CreateFactoryAtPlacement(localFactory, NormalizeAngle(yaw), currentPresetName, selectedFactionIndex);
                        if (created != null)
                        {
                            selectedFactory = created;
                            if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Created {created.displayName}");
                        }
                    }
                    else if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage("Horus: No valid placement point.");
                    }
                }
            }
            if (GUILayout.Button("Create Factory From Aimed Unit", GUILayout.Height(30)))
            {
                if (CanRunFactoryCreateAction())
                {
                    Unit aimed = GetAimedUnit();
                    if (aimed == null)
                    {
                        HorusPlugin.Logger.LogWarning("[HORUS RTS] Create Factory From Aimed Unit failed: invalid target.");
                        if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: Aim at a valid unit first.");
                    }
                    else
                    {
                        var created = manager.CreateFactoryFromUnit(aimed, currentPresetName);
                        if (created != null)
                        {
                            selectedFactory = created;
                            if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Attached {created.displayName} to {aimed.unitName}");
                        }
                    }
                }
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(armedFactoryPresetName) && string.Equals(armedFactoryPresetName, currentPresetName, StringComparison.OrdinalIgnoreCase))
            {
                if (GUILayout.Button("Cancel Factory Placement", GUILayout.Height(30)))
                {
                    armedFactoryPresetName = null;
                    ghost.Clear();
                }
            }
            else if (GUILayout.Button("Arm Factory Placement", GUILayout.Height(30)))
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

        private void DrawFactoryBulkPanel(RtsFactoryManager manager, bool isHost)
        {
            GUILayout.Box("Bulk / Config Operations");
            if (!isHost)
            {
                DrawHostOnlyButton("Save Factories");
                DrawHostOnlyButton("Load Factories");
                DrawHostOnlyButton("Start All Factories");
                DrawHostOnlyButton("Stop All Factories");
                DrawHostOnlyButton("Reload Factory Config");
                DrawHostOnlyButton("Reset Factory Presets To Defaults");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Factories"))
            {
                manager.SaveInstances();
                if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory instances saved.");
            }
            if (GUILayout.Button("Load Factories"))
            {
                manager.LoadInstances();
                if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory instances loaded.");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start All Factories"))
            {
                manager.StartAllFactories();
            }
            if (GUILayout.Button("Stop All Factories"))
            {
                manager.StopAllFactories();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload Factory Config"))
            {
                manager.ReloadConfig();
                if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory config reloaded.");
            }
            if (GUILayout.Button("Reset Factory Presets To Defaults"))
            {
                manager.ResetPresetsToDefaults();
                if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage("Horus: Factory presets reset to defaults.");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawHostOnlyButton(string label)
        {
            bool previous = GUI.enabled;
            GUI.enabled = false;
            GUILayout.Button(label + " (Host only)");
            GUI.enabled = previous;
        }

        private bool CanRunFactoryCreateAction()
        {
            if (Time.unscaledTime - lastFactoryCreateActionTime < 0.25f)
            {
                return false;
            }
            lastFactoryCreateActionTime = Time.unscaledTime;
            return true;
        }

        private static int IndexOf(float[] arr, float value)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (Mathf.Approximately(arr[i], value)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Returns a deduplicated, sorted list of units for the current category.
        /// </summary>
        private List<UnitDefinition> GetCurrentList()
        {
            if (cachedCategoryIndex == selectedCategoryIndex && cachedUnitList != null)
            {
                return cachedUnitList;
            }

            List<UnitDefinition> rawList;
            switch (selectedCategoryIndex)
            {
                case 0: rawList = Encyclopedia.i.aircraft.Cast<UnitDefinition>().ToList(); break;
                case 1: rawList = Encyclopedia.i.vehicles.Cast<UnitDefinition>().ToList(); break;
                case 2: rawList = Encyclopedia.i.ships.Cast<UnitDefinition>().ToList(); break;
                case 3: rawList = Encyclopedia.i.buildings.Cast<UnitDefinition>().ToList(); break;
                case 4: rawList = Encyclopedia.i.scenery.Cast<UnitDefinition>().ToList(); break;
                default: rawList = new List<UnitDefinition>(); break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<UnitDefinition>();
            foreach (var unit in rawList)
            {
                string name = unit.unitName;
                if (string.IsNullOrEmpty(name) || name == "???")
                    continue;
                if (seen.Add(name))
                {
                    deduped.Add(unit);
                }
            }
            deduped.Sort((a, b) => string.Compare(a.unitName, b.unitName, StringComparison.OrdinalIgnoreCase));

            cachedUnitList = deduped;
            cachedCategoryIndex = selectedCategoryIndex;
            return cachedUnitList;
        }

        // --- Selection helper ---
        private UnitDefinition GetSelectedDefinition()
        {
            var list = GetCurrentList();
            if (list == null || selectedUnitIndex < 0 || selectedUnitIndex >= list.Count) return null;
            return list[selectedUnitIndex];
        }

        // --- Placement math (applied consistently to the ghost AND the real spawn) ---

        private static float NormalizeAngle(float a)
        {
            a %= 360f;
            if (a < 0f) a += 360f;
            return a;
        }

        private float ApplyRotationSnap(float yaw)
        {
            if (!rotationSnapEnabled || rotationSnapStep <= 0f) return NormalizeAngle(yaw);
            float snapped = Mathf.Round(yaw / rotationSnapStep) * rotationSnapStep;
            return NormalizeAngle(snapped);
        }

        private Vector3 ApplyGridSnap(Vector3 position)
        {
            if (!gridSnapEnabled || gridSize <= 0f) return position;
            position.x = Mathf.Round(position.x / gridSize) * gridSize;
            position.z = Mathf.Round(position.z / gridSize) * gridSize;
            return position;
        }

        private Vector3 ApplyGroundSnap(Vector3 position, UnitDefinition unit, int category)
        {
            bool groundCategory = category == 1 || category == 3 || category == 4;
            if (snapToGround && groundCategory && TrySampleGroundHeight(position, out float groundY))
            {
                position.y = groundY + spawnAltitude;
            }
            return position;
        }

        private Quaternion GetPlacementRotation()
        {
            Quaternion yawRot = Quaternion.Euler(0f, ApplyRotationSnap(spawnYaw), 0f);
            bool groundCategory = selectedCategoryIndex == 1 || selectedCategoryIndex == 3 || selectedCategoryIndex == 4;
            // Experimental: tilt ground units to the surface. Skipped for aircraft/ships and map spawn.
            if (alignToSurface && groundCategory && !mapSpawnMode)
            {
                return Quaternion.FromToRotation(Vector3.up, lastSurfaceNormal) * yawRot;
            }
            return yawRot;
        }

        private float GetOceanLevel()
        {
            float overrideVal = HorusPlugin.OceanHeightOverride.Value;
            if (overrideVal > -9990f)
            {
                return overrideVal;
            }
            return Datum.LocalSeaY;
        }

        /// <summary>
        /// Unified placement pipeline: grid-snap XZ, add altitude, then ground-snap for ground
        /// categories. Used by both the ghost preview and the real spawn so they always match.
        /// </summary>
        internal Vector3 GetFinalPlacementPosition(Vector3 rawPosition, UnitDefinition def = null, int cat = -1)
        {
            if (def == null) def = GetSelectedDefinition();
            if (cat < 0) cat = selectedCategoryIndex;

            Vector3 pos = ApplyGridSnap(rawPosition);

            bool isShip = IsShipDefinition(def) || (cat == 2);
            // Dedicated safe ship placement path
            if (isShip)
            {
                float lift = HorusPlugin.ShipSpawnLift.Value;
                pos.y = GetOceanLevel() + spawnAltitude + (def != null ? def.spawnOffset.y : 0f) + lift;

                // Enforce safe distance between ships to prevent overlap/sinking
                float thisShipLength = (def != null) ? def.length : 150f;
                if (thisShipLength <= 0f) thisShipLength = 150f;

                if (UnitRegistry.allUnits != null)
                {
                    for (int pass = 0; pass < 5; pass++)
                    {
                        bool adjusted = false;
                        foreach (var otherUnit in UnitRegistry.allUnits)
                        {
                            if (otherUnit == null || otherUnit.gameObject == null || otherUnit.disabled) continue;
                            if (!(otherUnit is Ship otherShip)) continue;

                            float otherShipLength = (otherShip.definition != null) ? otherShip.definition.length : 150f;
                            if (otherShipLength <= 0f) otherShipLength = 150f;

                            float minSafeDistance = (thisShipLength + otherShipLength) * 0.5f + 25f;

                            Vector3 otherPos = otherShip.transform.position;
                            float distXZ = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(otherPos.x, otherPos.z));

                            if (distXZ < minSafeDistance)
                            {
                                HorusPlugin.Logger.LogInfo($"[Horus Spacing DEBUG] Overlap detected: placing ship at ({pos.x:F1}, {pos.z:F1}) too close to existing ship '{otherShip.unitName}' at ({otherPos.x:F1}, {otherPos.z:F1}). distXZ={distXZ:F1}m, minSafeDistance={minSafeDistance:F1}m (thisShipLength={thisShipLength:F1}m, otherShipLength={otherShipLength:F1}m). Pushing away...");
                                Vector3 diff = pos - otherPos;
                                diff.y = 0f;
                                Vector3 pushDir = diff.sqrMagnitude > 0.01f ? diff.normalized : otherShip.transform.right;
                                Vector3 newPos = otherPos + pushDir * minSafeDistance;
                                
                                if (float.IsNaN(newPos.x) || float.IsNaN(newPos.z) || float.IsInfinity(newPos.x) || float.IsInfinity(newPos.z))
                                {
                                    HorusPlugin.Logger.LogError($"[Horus Spacing DEBUG] Pushed position has NaN or Infinity! raw_diff={diff}, pushDir={pushDir}, minSafeDistance={minSafeDistance}. Skipping adjustment to avoid sinking.");
                                }
                                else
                                {
                                    pos.x = newPos.x;
                                    pos.z = newPos.z;
                                    HorusPlugin.Logger.LogInfo($"[Horus Spacing DEBUG] New adjusted position: ({pos.x:F1}, {pos.z:F1})");
                                    adjusted = true;
                                }
                            }
                        }
                        if (!adjusted) break;
                    }
                }
                return pos;
            }
            
            bool useOceanSnap = oceanSnapActive || (autoOceanSnapForShips && cat == 2);
            if (useOceanSnap)
            {
                pos.y = GetOceanLevel() + spawnAltitude;
            }
            else
            {
                pos.y += spawnAltitude;
                pos = ApplyGroundSnap(pos, def, cat);
            }

            // Raise position by the unit's spawnOffset so it sits correctly on the ground/water surface
            if (def != null)
            {
                pos.y += def.spawnOffset.y;
            }

            // Small vehicle clearance for ground vehicles
            if (cat == 1 && spawnAltitude == 0f)
            {
                pos.y += 2f;
            }

            return pos;
        }

        private System.Collections.Generic.List<UnitDefinition> GetGroupUnitsToSpawn(out int category)
        {
            category = selectedCategoryIndex;
            var list = new System.Collections.Generic.List<UnitDefinition>();
            UnitDefinition currentSelected = GetSelectedDefinition();
            
            if (selectedGroupPresetIndex == 0) // Homogeneous (Selected Unit)
            {
                if (currentSelected != null)
                {
                    for (int i = 0; i < groupCount; i++) list.Add(currentSelected);
                }
            }
            else if (selectedGroupPresetIndex == 8) // Custom Group
            {
                list.AddRange(customGroupUnits);
                if (list.Count > 0 && list[0] != null)
                {
                    category = GetUnitCategoryIndex(list[0]);
                }
            }
            else // Presets (Convoy, Armored Group, Squadron, etc.)
            {
                if (currentSelected != null)
                {
                    for (int i = 0; i < groupCount; i++) list.Add(currentSelected);
                }
            }
            return list;
        }

        private int GetUnitCategoryIndex(UnitDefinition def)
        {
            if (def == null) return -1;
            if (def is AircraftDefinition) return 0;
            if (def is VehicleDefinition) return 1;
            if (def is ShipDefinition) return 2;
            if (def is BuildingDefinition) return 3;
            if (def is SceneryDefinition) return 4;
            return -1;
        }

        private System.Collections.Generic.List<Vector3> GetFormationOffsets(int count, float spacing, string formation)
        {
            var offsets = new System.Collections.Generic.List<Vector3>();
            if (count <= 0) return offsets;

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            for (int i = 0; i < count; i++)
            {
                Vector3 relativeOffset = Vector3.zero;
                switch (formation)
                {
                    case "Line":
                        relativeOffset = right * ((i - (count - 1) / 2f) * spacing);
                        break;
                    case "Column":
                        relativeOffset = forward * (-i * spacing);
                        break;
                    case "Grid":
                        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
                        int row = i / cols;
                        int col = i % cols;
                        float xOffset = (col - (cols - 1) / 2f) * spacing;
                        float zOffset = -row * spacing;
                        relativeOffset = right * xOffset + forward * zOffset;
                        break;
                    case "Circle":
                        float angle = i * (360f / count) * Mathf.Deg2Rad;
                        float radius = spacing * count / (2f * Mathf.PI);
                        if (count <= 1) radius = 0f;
                        else if (count <= 4) radius = spacing * 0.7f;
                        relativeOffset = right * Mathf.Cos(angle) * radius + forward * Mathf.Sin(angle) * radius;
                        break;
                    case "V Formation":
                        int depth = (i + 1) / 2;
                        int side = (i % 2 == 0) ? 1 : -1;
                        if (i == 0)
                        {
                            relativeOffset = Vector3.zero;
                        }
                        else
                        {
                            relativeOffset = (side * right - forward) * (depth * spacing * 0.7f);
                        }
                        break;
                }
                offsets.Add(relativeOffset);
            }
            return offsets;
        }

        private void SpawnGroup(Vector3 centerPos)
        {
            if (!HorusPermissions.CanSpawn())
            {
                HorusPlugin.Logger.LogWarning("Horus: host permission required. Cannot spawn.");
                return;
            }

            if (Spawner.i == null)
            {
                HorusPlugin.Logger.LogError("HorusMod: Spawner.i is null. Cannot spawn unit.");
                return;
            }

            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0) return;

            int cat;
            var units = GetGroupUnitsToSpawn(out cat);
            if (units == null || units.Count == 0)
            {
                if (SceneSingleton<GameplayUI>.i != null)
                {
                    SceneSingleton<GameplayUI>.i.GameMessage("Horus Mod: Group is empty! Cannot spawn.");
                }
                HorusPlugin.Logger.LogWarning("Horus: Group spawn blocked because the group has 0 units.");
                return;
            }

            // RTS transaction validation
            RtsTransaction tx = null;
            if (economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander)
            {
                tx = economyManager.CreateGroupTransaction(units, selectedFactionIndex);
                if (!tx.IsValid)
                {
                    if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage($"Horus Mod: {tx.DenialReason}");
                    }
                    HorusPlugin.Logger.LogWarning($"Horus: group spawn blocked. {tx.DenialReason}");
                    return;
                }
            }

            Faction faction = factions[selectedFactionIndex];
            FactionHQ hq = FactionRegistry.HQFromFaction(faction);
            
            Quaternion rot = GetPlacementRotation();
            var offsets = GetFormationOffsets(units.Count, groupSpacing, formationNames[selectedFormationIndex]);

            HorusPlugin.Logger.LogInfo($"HorusMod: Spawning group of {units.Count} units.");

            var spawnedUnits = new List<Unit>();

            for (int i = 0; i < units.Count; i++)
            {
                var def = units[i];
                if (def == null) continue;
                
                Vector3 localOffset = rot * offsets[i];
                Vector3 rawPos = centerPos + localOffset;
                Vector3 finalPos = GetFinalPlacementPosition(rawPos, def, GetUnitCategoryIndex(def));
                
                GlobalPosition globalPos = finalPos.ToGlobalPosition();
                Unit spawned;
                int unitCat = GetUnitCategoryIndex(def);

                if (IsShipDefinition(def) || unitCat == 2) // Ship
                {
                    float shipYaw = NormalizeAngle(rot.eulerAngles.y);
                    spawned = SpawnShipSafe(def, globalPos, shipYaw, selectedFactionIndex);
                }
                else
                {
                    string uniqueName = (def.jsonKey ?? "unit") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                    spawned = Spawner.i.SpawnFromUnitDefinitionInEditor(def, globalPos, rot, hq, uniqueName);
                    if (spawned != null)
                    {
                        horusSpawnedUnits.Add(spawned);
                        if (spawnStationary)
                        {
                            if (spawned is GroundVehicle vehicle)
                            {
                                vehicle.SetHoldPosition(true);
                            }
                        }
                    }
                }

                if (spawned != null) spawnedUnits.Add(spawned);
            }

            // Commit RTS transaction after all units spawned
            if (tx != null && economyManager != null)
            {
                economyManager.CommitGroupTransaction(tx, spawnedUnits);
                if (HorusPlugin.AutoDisarmAfterPurchase.Value)
                {
                    economyManager.DisarmDeployment();
                }
            }
        }

        private void RefreshSavedCustomGroups()
        {
            savedCustomGroupNames.Clear();
            try
            {
                string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "HorusMod", "groups");
                if (System.IO.Directory.Exists(dir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
                    {
                        savedCustomGroupNames.Add(System.IO.Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch (System.Exception ex)
            {
                HorusPlugin.Logger.LogError($"Failed to list custom groups: {ex.Message}");
            }
        }

        // Cost resolution is now delegated to RtsEconomyManager.
        // These helper wrappers are kept for convenience.
        private float GetUnitCost(UnitDefinition def)
        {
            return economyManager?.GetUnitCost(def) ?? 0f;
        }

        private float GetGroupTotalCost(List<UnitDefinition> definitions)
        {
            return economyManager?.GetGroupTotalCost(definitions) ?? 0f;
        }

        private void SaveCustomGroup(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "HorusMod", "groups");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, name + ".json");
                
                var data = new CustomGroupData();
                data.groupName = name;
                data.spacing = groupSpacing;
                data.formation = formationNames[selectedFormationIndex];
                data.stationary = spawnStationary;
                data.altitude = spawnAltitude;
                
                foreach (var unit in customGroupUnits)
                {
                    if (unit != null) data.unitNames.Add(unit.unitName);
                }
                
                string json = UnityEngine.JsonUtility.ToJson(data, true);
                System.IO.File.WriteAllText(path, json);
                HorusPlugin.Logger.LogInfo($"Saved custom group '{name}' to {path}");
                RefreshSavedCustomGroups();
            }
            catch (System.Exception ex)
            {
                HorusPlugin.Logger.LogError($"Failed to save custom group: {ex.Message}");
            }
        }

        private void LoadCustomGroup(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "HorusMod", "groups");
                string path = System.IO.Path.Combine(dir, name + ".json");
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    if (string.IsNullOrEmpty(json.Trim()))
                    {
                        throw new Exception("JSON file is empty");
                    }
                    var data = UnityEngine.JsonUtility.FromJson<CustomGroupData>(json);
                    if (data == null || data.unitNames == null || data.unitNames.Count == 0)
                    {
                        throw new Exception("Invalid or empty group data");
                    }
                    
                    groupSpacing = data.spacing;
                    groupSpacingInputText = groupSpacing.ToString("0");
                    spawnStationary = data.stationary;
                    spawnAltitude = data.altitude;
                    altitudeInputText = spawnAltitude.ToString("0");
                    
                    int fi = System.Array.IndexOf(formationNames, data.formation);
                    if (fi >= 0) selectedFormationIndex = fi;
                    
                    customGroupUnits.Clear();
                    foreach (var uname in data.unitNames)
                    {
                        UnitDefinition found = FindUnitDefinitionByName(uname);
                        if (found != null)
                        {
                            customGroupUnits.Add(found);
                        }
                        else
                        {
                            HorusPlugin.Logger.LogWarning($"Custom Group '{name}': UnitDefinition '{uname}' not found in Encyclopedia. Skipping.");
                        }
                    }
                    groupCount = customGroupUnits.Count;
                    HorusPlugin.Logger.LogInfo($"Loaded custom group '{name}' with {customGroupUnits.Count} units.");
                    
                    if (customGroupUnits.Count == 0)
                    {
                        if (SceneSingleton<GameplayUI>.i != null)
                        {
                            SceneSingleton<GameplayUI>.i.GameMessage("Horus Mod: Loaded group has 0 valid units!");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                string errMsg = $"Failed to load custom group '{name}': {ex.Message}";
                HorusPlugin.Logger.LogError(errMsg);
                if (SceneSingleton<GameplayUI>.i != null)
                {
                    SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Broken custom group JSON! ({ex.Message})");
                }
            }
        }

        private void DeleteCustomGroupFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "HorusMod", "groups");
                string path = System.IO.Path.Combine(dir, name + ".json");
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    HorusPlugin.Logger.LogInfo($"Deleted custom group file: {path}");
                }
            }
            catch (System.Exception ex)
            {
                HorusPlugin.Logger.LogError($"Failed to delete custom group file: {ex.Message}");
            }
        }

        internal UnitDefinition FindUnitDefinitionByName(string name)
        {
            if (Encyclopedia.i == null) return null;
            var lists = new System.Collections.Generic.List<System.Collections.IEnumerable> {
                Encyclopedia.i.aircraft,
                Encyclopedia.i.vehicles,
                Encyclopedia.i.ships,
                Encyclopedia.i.buildings,
                Encyclopedia.i.scenery
            };
            foreach (var list in lists)
            {
                foreach (var obj in list)
                {
                    if (obj is UnitDefinition def && string.Equals(def.unitName, name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return def;
                    }
                }
            }
            return null;
        }

        public void AddHorusSpawnedUnit(Unit unit)
        {
            if (unit != null)
            {
                horusSpawnedUnits.Add(unit);
            }
        }

        public bool TryGetCurrentPlacement(out Vector3 localPos, out float yaw)
        {
            yaw = spawnYaw;
            if (TryGet3DPlacement(out Vector3 rawPos))
            {
                localPos = GetFinalPlacementPosition(rawPos);
                return true;
            }
            localPos = Vector3.zero;
            return false;
        }

        public Unit GetAimedUnit()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                GameObject hitObject = hit.collider != null ? hit.collider.gameObject : null;
                if (hitObject != null)
                {
                    Unit unitRoot = FindUnitRoot(hitObject);
                    if (unitRoot != null) return unitRoot;
                }
            }
            return null;
        }

        // --- Placement sources ---

        private bool IsPlacingShip()
        {
            if (!string.IsNullOrEmpty(armedFactoryPresetName)) return false;

            UnitDefinition currentSelected = GetSelectedDefinition();
            if (!enableGroupSpawn)
            {
                return IsShipDefinition(currentSelected);
            }
            else
            {
                int cat;
                var units = GetGroupUnitsToSpawn(out cat);
                if (units != null)
                {
                    for (int i = 0; i < units.Count; i++)
                    {
                        if (IsShipDefinition(units[i]))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool TryGet3DPlacement(out Vector3 position)
        {
            position = Vector3.zero;
            Camera cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (IsPlacingShip())
            {
                // Intersect ray with flat sea level plane directly to bypass terrain/seabed raycast
                float seaLevel = GetOceanLevel();
                float denom = ray.direction.y;
                if (Mathf.Abs(denom) > 0.0001f)
                {
                    float t = (seaLevel - ray.origin.y) / denom;
                    if (t > 0f)
                    {
                        position = ray.origin + t * ray.direction;
                        lastSurfaceNormal = Vector3.up;
                        return true;
                    }
                }
                return false;
            }

            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                position = hit.point;
                lastSurfaceNormal = hit.normal;
                return true;
            }
            return false;
        }

        private bool TryGetMapPlacement(out Vector3 position)
        {
            position = Vector3.zero;
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null) return false;
            if (!map.TryGetCursorCoordinates(out GlobalPosition mapPos)) return false;
            position = new GlobalPosition(mapPos.x, 0f, mapPos.z).ToLocalPosition();
            return true;
        }

        // --- Ghost preview ---

        private void UpdateGhost()
        {
            if (!ghostPreviewEnabled || !HorusPermissions.CanSpawn())
            {
                if (ghost.IsBuilt) ghost.Clear();
                return;
            }

            if (!string.IsNullOrEmpty(armedFactoryPresetName))
            {
                var preset = RtsFactoryManager.Instance.Config?.factoryPresets?.FirstOrDefault(p => string.Equals(p.presetName, armedFactoryPresetName, StringComparison.OrdinalIgnoreCase));
                string visualBuildingName = preset?.visualBuilding;
                if (!string.IsNullOrEmpty(visualBuildingName))
                {
                    string resolvedVisual;
                    var def = RtsFactoryManager.Instance.ResolveVisualBuildingDefinition(visualBuildingName, out resolvedVisual, false);
                    if (def != null)
                    {
                        if (ghost.BuiltDefinition != def || !ghost.IsBuilt)
                        {
                            if (!ghost.Build(def)) return;
                        }
                        if (TryGet3DPlacement(out Vector3 rawPos))
                        {
                            ghost.UpdateTransform(GetFinalPlacementPosition(rawPos, def, 3), GetPlacementRotation());
                            ghost.SetVisible(true);
                        }
                        else
                        {
                            ghost.SetVisible(false);
                        }
                        return;
                    }
                }
            }

            if (enableGroupSpawn)
            {
                int cat;
                var units = GetGroupUnitsToSpawn(out cat);
                if (units == null || units.Count == 0)
                {
                    if (ghost.IsBuilt) ghost.Clear();
                    return;
                }

                if (ghost.BuiltDefinition != units[0] || !ghost.IsBuilt)
                {
                    if (!ghost.BuildGroup(units))
                    {
                        return;
                    }
                }

                if (!TryGet3DPlacement(out Vector3 rawPos))
                {
                    ghost.SetVisible(false);
                    return;
                }

                Quaternion rot = GetPlacementRotation();
                var offsets = GetFormationOffsets(units.Count, groupSpacing, formationNames[selectedFormationIndex]);
                
                var rotatedOffsets = new System.Collections.Generic.List<Vector3>();
                foreach (var offset in offsets)
                {
                    rotatedOffsets.Add(rot * offset);
                }

                ghost.UpdateTransformGroup(rawPos, rot, rotatedOffsets, units, (pos, def) => GetFinalPlacementPosition(pos, def, GetUnitCategoryIndex(def)));
                ghost.SetVisible(true);
            }
            else
            {
                UnitDefinition def = GetSelectedDefinition();
                if (def == null)
                {
                    if (ghost.IsBuilt) ghost.Clear();
                    return;
                }

                if (ghost.BuiltDefinition != def || !ghost.IsBuilt)
                {
                    if (def == ghostBuildFailedDef)
                    {
                        ghost.SetVisible(false);
                        return;
                    }
                    if (!ghost.Build(def))
                    {
                        ghostBuildFailedDef = def;
                        return;
                    }
                    ghostBuildFailedDef = null;
                }

                if (!TryGet3DPlacement(out Vector3 rawPos))
                {
                    ghost.SetVisible(false);
                    return;
                }

                ghost.UpdateTransform(GetFinalPlacementPosition(rawPos), GetPlacementRotation());
                ghost.SetVisible(true);
            }
        }

        private void OnDestroy()
        {
            ghost.Dispose();
        }

        // --- Spawn via Raycast (free camera) ---
        private void HandleSpawnClick()
        {
            if (!string.IsNullOrEmpty(armedFactoryPresetName))
            {
                if (TryGet3DPlacement(out Vector3 factoryRawPos))
                {
                    Vector3 pos = GetFinalPlacementPosition(factoryRawPos);
                    float yaw = NormalizeAngle(GetPlacementRotation().eulerAngles.y);
                    var created = RtsFactoryManager.Instance.CreateFactoryAtPlacement(pos, yaw, armedFactoryPresetName, selectedFactionIndex);
                    if (created != null)
                    {
                        selectedFactory = created;
                        if (SceneSingleton<GameplayUI>.i != null)
                        {
                            SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Created {created.displayName}");
                        }
                        armedFactoryPresetName = null;
                        ghost.Clear();
                    }
                }
                return;
            }

            if (TryGet3DPlacement(out Vector3 rawPos))
            {
                if (enableGroupSpawn)
                {
                    SpawnGroup(rawPos);
                }
                else
                {
                    SpawnSelectedUnit(GetFinalPlacementPosition(rawPos));
                }
            }
        }

        // --- Spawn via Map Click ---
        private void HandleMapSpawnClick()
        {
            try
            {
                if (!TryGetMapPlacement(out Vector3 rawPos)) return;

                if (!string.IsNullOrEmpty(armedFactoryPresetName))
                {
                    Vector3 pos = GetFinalPlacementPosition(rawPos);
                    float yaw = NormalizeAngle(GetPlacementRotation().eulerAngles.y);
                    var created = RtsFactoryManager.Instance.CreateFactoryAtPlacement(pos, yaw, armedFactoryPresetName, selectedFactionIndex);
                    if (created != null)
                    {
                        selectedFactory = created;
                        if (SceneSingleton<GameplayUI>.i != null)
                        {
                            SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Created {created.displayName}");
                        }
                        armedFactoryPresetName = null;
                        ghost.Clear();
                    }
                    return;
                }

                if (enableGroupSpawn)
                {
                    SpawnGroup(rawPos);
                }
                else
                {
                    Vector3 finalPos = GetFinalPlacementPosition(rawPos);
                    HorusPlugin.Logger.LogInfo($"Map spawn at local {finalPos}");
                    SpawnSelectedUnit(finalPos);
                }
            }
            catch (Exception ex)
            {
                HorusPlugin.Logger.LogError($"Map spawn failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Best-effort terrain height sampling at a local x/z by raycasting straight down.
        /// Uses the terrain layer mask (layer 6) like the game's own ground checks.
        /// Returns false if no terrain collider is loaded at that location.
        /// </summary>
        internal bool TrySampleGroundHeight(Vector3 localPos, out float groundY)
        {
            const int terrainMask = 1 << 6; // layer 6 = terrain
            Vector3 origin = new Vector3(localPos.x, 30000f, localPos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 60000f, terrainMask))
            {
                groundY = hit.point.y;
                return true;
            }
            groundY = 0f;
            return false;
        }

        private void HandleDeleteClick()
        {
            // Permission gate.
            if (!HorusPermissions.CanDelete())
            {
                HorusPlugin.Logger.LogWarning("Horus: host permission required. Delete blocked.");
                return;
            }

            // Never delete while placing from the map.
            if (mapSpawnMode)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Unit unitToDelete = null;

            // 1. Try direct click first
            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                GameObject hitObject = hit.collider != null ? hit.collider.gameObject : null;
                if (hitObject != null)
                {
                    Unit unitRoot = FindUnitRoot(hitObject);
                    if (unitRoot != null)
                    {
                        unitToDelete = unitRoot;
                    }
                }
            }

            // 2. If no direct unit clicked, look for nearest unit in range of the raycast hit point
            if (unitToDelete == null)
            {
                if (Physics.Raycast(ray, out RaycastHit hit2, 100000f))
                {
                    Vector3 localHitPos = hit2.point;
                    GlobalPosition globalHitPos = localHitPos.ToGlobalPosition();
                    
                    if (UnitRegistry.TryGetNearestUnit(globalHitPos, out Unit nearestUnit, deleteRange))
                    {
                        unitToDelete = nearestUnit;
                    }
                }
            }

            if (unitToDelete == null)
            {
                HorusPlugin.Logger.LogInfo("Horus: No unit found to delete (direct click or within range).");
                return;
            }

            // Validate the unit
            if (!IsSafeDeleteTarget(unitToDelete.gameObject))
            {
                string reason = "";
                if (IsBuiltinMapUnit(unitToDelete))
                {
                    reason = "original map unit is protected (enable Safety/AllowDeletingOriginalMissionUnits to remove)";
                }
                else
                {
                    reason = "not spawned by Horus (enable Safety/AllowDeletingNonHorusUnits to remove)";
                }
                HorusPlugin.Logger.LogInfo($"Horus: target is not deletable ({reason}): '{unitToDelete.unitName}'.");
                return;
            }

            DeleteUnit(unitToDelete);
        }

        /// <summary>Destroys a validated unit in a network-safe way and untracks it.</summary>
        private void DeleteUnit(Unit unit)
        {
            if (unit == null) return;
            GameObject go = unit.gameObject;
            string unitName = unit.unitName;
            bool wasHorus = horusSpawnedUnits.Remove(unit);

            if (HorusPermissions.IsMultiplayer())
            {
                NetworkServer.Destroy(go);
            }
            else
            {
                Destroy(go);
            }
            HorusPlugin.Logger.LogInfo($"Horus: deleted {(wasHorus ? "Horus-spawned " : "")}unit '{unitName}'.");
        }

        /// <summary>Finds the nearest gameplay unit root by walking UP the hierarchy only.</summary>
        private static Unit FindUnitRoot(GameObject target)
        {
            return target == null ? null : target.GetComponentInParent<Unit>();
        }

        private bool HasGameplayUnitComponent(GameObject target)
        {
            return FindUnitRoot(target) != null;
        }

        private bool WasSpawnedByHorus(GameObject target)
        {
            Unit u = FindUnitRoot(target);
            return u != null && horusSpawnedUnits.Contains(u);
        }

        private bool IsMapOrEnvironmentObject(GameObject target)
        {
            if (target == null) return true;
            // No Unit anywhere up the hierarchy => terrain/road/static geometry/map prop.
            if (FindUnitRoot(target) == null) return true;
            // DynamicMap / map UI objects.
            if (target.GetComponentInParent<DynamicMap>() != null) return true;
            return false;
        }

        private static bool IsBuiltinMapUnit(Unit unit)
        {
            if (unit == null) return false;
            string uniqueName = unit.UniqueName;
            return !string.IsNullOrEmpty(uniqueName)
                && uniqueName.StartsWith(Unit.BUILTIN_UNIT_PREFIX, StringComparison.Ordinal);
        }

        /// <summary>
        /// True only for objects that are safe to delete: a real gameplay unit that was either
        /// spawned by Horus, or (when explicitly allowed) any non-builtin gameplay unit. Map and
        /// environment objects can never pass this check.
        /// </summary>
        private bool IsSafeDeleteTarget(GameObject target)
        {
            if (target == null) return false;
            if (IsMapOrEnvironmentObject(target)) return false;
            if (WasSpawnedByHorus(target)) return true;
            
            Unit u = FindUnitRoot(target);
            if (u == null) return false;

            bool isBuiltin = IsBuiltinMapUnit(u);
            if (isBuiltin)
            {
                return HorusPlugin.AllowDeletingOriginalMissionUnits.Value;
            }
            else
            {
                return HorusPlugin.AllowDeletingNonHorusUnits.Value;
            }
        }

        private void SpawnSelectedUnit(Vector3 position)
        {
            if (!HorusPermissions.CanSpawn())
            {
                HorusPlugin.Logger.LogWarning("Horus: host permission required. Cannot spawn.");
                return;
            }

            if (Spawner.i == null)
            {
                HorusPlugin.Logger.LogError("HorusMod: Spawner.i is null. Cannot spawn unit.");
                return;
            }

            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0) return;

            UnitDefinition def = GetSelectedDefinition();
            if (def == null) return;

            // RTS Commander Mode: transaction validation
            RtsTransaction tx = null;
            bool isRtsMode = economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander;

            if (isRtsMode)
            {
                // Deployment confirmation gate
                if (HorusPlugin.RequireDeploymentConfirmation.Value)
                {
                    if (!economyManager.IsDeploymentArmed || economyManager.ArmedDefinition != def)
                    {
                        // Auto-arm on first click attempt
                        economyManager.ArmDeployment(def, selectedFactionIndex);
                        if (SceneSingleton<GameplayUI>.i != null)
                        {
                            SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Deployment armed for {def.unitName}. Click again to deploy.");
                        }
                        return; // Require second click to actually deploy
                    }
                }

                tx = economyManager.CreateTransaction(def, selectedFactionIndex);
                if (!tx.IsValid)
                {
                    if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage($"Horus Mod: {tx.DenialReason}");
                    }
                    HorusPlugin.Logger.LogWarning($"Horus: spawn blocked. {tx.DenialReason}");
                    return;
                }
            }

            Faction faction = factions[selectedFactionIndex];
            FactionHQ hq = FactionRegistry.HQFromFaction(faction);

            GlobalPosition globalPos = position.ToGlobalPosition();
            Quaternion rotation = GetPlacementRotation();

            Unit spawned = null;
            if (IsShipDefinition(def) || selectedCategoryIndex == 2) // Ship
            {
                float shipYaw = NormalizeAngle(rotation.eulerAngles.y);
                spawned = SpawnShipSafe(def, globalPos, shipYaw, selectedFactionIndex);
            }
            else
            {
                string uniqueName = (def.jsonKey ?? "unit") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                spawned = Spawner.i.SpawnFromUnitDefinitionInEditor(def, globalPos, rotation, hq, uniqueName);
                if (spawned != null)
                {
                    horusSpawnedUnits.Add(spawned);
                    if (spawnStationary)
                    {
                        if (spawned is GroundVehicle vehicle)
                        {
                            vehicle.SetHoldPosition(true);
                        }
                    }
                }
                HorusPlugin.Logger.LogInfo($"HorusMod: Spawned {def.unitName} at {globalPos} yaw={spawnYaw:F0}° (tracked={spawned != null})");
            }

            // Commit RTS transaction after successful spawn
            if (spawned != null && tx != null && economyManager != null)
            {
                economyManager.CommitTransaction(tx, spawned);
                if (HorusPlugin.AutoDisarmAfterPurchase.Value)
                {
                    economyManager.DisarmDeployment();
                }
            }
        }

        internal Unit SpawnShipSafe(UnitDefinition def, GlobalPosition globalPos, float yaw, int faction)
        {
            UnityEngine.Debug.Log("[HORUS SHIP SAFE] ENTER SpawnShipSafe()");
            if (Spawner.i == null)
            {
                HorusPlugin.Logger.LogError("HorusMod: Spawner.i is null. Cannot spawn ship.");
                return null;
            }

            var factions = FactionRegistry.factions;
            if (factions == null || faction < 0 || faction >= factions.Count)
            {
                HorusPlugin.Logger.LogError($"HorusMod: Invalid faction index {faction}.");
                return null;
            }

            Faction factionObj = factions[faction];
            FactionHQ hq = FactionRegistry.HQFromFaction(factionObj);

            // Calculate local position for ships: ocean level only + spawnOffset.y + ShipSpawnLift
            float lift = HorusPlugin.ShipSpawnLift.Value;
            float targetY = Datum.LocalSeaY + def.spawnOffset.y + lift;

            // Prepare local position
            Vector3 localPos = globalPos.ToLocalPosition();
            localPos.y = targetY;

            // Convert back to global position for the Spawner
            GlobalPosition spawnGlobalPos = localPos.ToGlobalPosition();
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner] Preparing safe spawn for '{def.unitName}'");
            HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner]   def.spawnOffset={def.spawnOffset}, Datum.LocalSeaY={Datum.LocalSeaY:F2}");
            HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner]   Target Local Position=({localPos.x:F2}, {localPos.y:F2}, {localPos.z:F2})");

            string uniqueName = (def.jsonKey ?? "ship") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            Unit spawned = null;
            try
            {
                spawned = Spawner.i.SpawnShip(def.unitPrefab, spawnGlobalPos, rot, hq, uniqueName, 1f, false);
                HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner] Spawned ship via Spawner.i.SpawnShip directly with name '{uniqueName}'.");
            }
            catch (Exception ex)
            {
                HorusPlugin.Logger.LogWarning($"[Horus Ship Spawner] Spawner.i.SpawnShip direct call failed: {ex.Message}. Falling back to editor spawn.");
                spawned = Spawner.i.SpawnFromUnitDefinitionInEditor(def, spawnGlobalPos, rot, hq, uniqueName);
            }

            if (spawned != null)
            {
                // Immediately correct position and rotation
                Vector3 origPos = spawned.transform.position;
                Vector3 correctedPos = new Vector3(origPos.x, targetY, origPos.z);
                spawned.transform.position = correctedPos;
                spawned.transform.rotation = rot;

                Rigidbody rb = spawned.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.MovePosition(correctedPos);
                    rb.MoveRotation(rot);
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.ResetCenterOfMass();
                    rb.ResetInertiaTensor();
                }

                HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner]   Spawned ship transform pos before={origPos} after={spawned.transform.position}");
                if (rb != null)
                {
                    HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner]   Rigidbody velocity={rb.velocity} angularVelocity={rb.angularVelocity}");
                }

                // Add to tracked units
                horusSpawnedUnits.Add(spawned);

                // Handle stationary if toggled
                if (spawnStationary && spawned is Ship ship)
                {
                    ship.SetHoldPosition(true);
                }

                // Start stabilization coroutine if enabled
                if (HorusPlugin.StabilizeShipsAfterSpawn.Value)
                {
                    StartCoroutine(StabilizeShipAfterSpawn(spawned, def, yaw));
                }

                // Start slow update check for disabled state in the next frame
                StartCoroutine(LogShipStatusAfterFrames(spawned, 1));
                
                // Monitor ship state every frame for the first 5 seconds
                StartCoroutine(LogShipStateForSeconds(spawned, def, 5f));
            }
            else
            {
                HorusPlugin.Logger.LogError($"[Horus Ship Spawner]   Spawner.i.SpawnFromUnitDefinitionInEditor returned null for '{def.unitName}'");
            }

            return spawned;
        }

        private System.Collections.IEnumerator StabilizeShipAfterSpawn(Unit ship, UnitDefinition def, float yaw)
        {
            if (ship == null) yield break;

            float lift = HorusPlugin.ShipSpawnLift.Value;
            float targetY = Datum.LocalSeaY + def.spawnOffset.y + lift;
            Quaternion targetRot = Quaternion.Euler(0f, yaw, 0f);

            Rigidbody rb = ship.GetComponent<Rigidbody>();

            HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner] Starting stabilization coroutine for '{ship.unitName}'");

            // Stabilize for 3 FixedUpdate frames
            for (int frame = 0; frame < 3; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (ship == null || ship.gameObject == null) yield break;

                // Keep correct sea-level height and upright rotation
                Vector3 currentPos = ship.transform.position;
                Vector3 correctedPos = new Vector3(currentPos.x, targetY, currentPos.z);
                ship.transform.position = correctedPos;
                ship.transform.rotation = targetRot;

                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.MovePosition(correctedPos);
                    rb.MoveRotation(targetRot);
                }
            }

            HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner] Stabilization complete for '{ship.unitName}'");
        }

        private System.Collections.IEnumerator LogShipStatusAfterFrames(Unit ship, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            if (ship != null)
            {
                HorusPlugin.Logger.LogInfo($"[Horus Ship Spawner] Status after {frames} frames: disabled={ship.disabled}, unitState={ship.unitState}");
            }
        }

        private void OnGroupPresetChanged(int oldVal, int newVal)
        {
            if (oldVal == newVal) return;
            
            switch (newVal)
            {
                case 1: // Convoy
                    groupCount = 5;
                    groupSpacing = 30f;
                    selectedFormationIndex = 1; // Column
                    spawnStationary = false;
                    spawnAltitude = 0f;
                    altitudeInputText = "0";
                    break;
                case 2: // Armored Group
                    groupCount = 4;
                    groupSpacing = 25f;
                    selectedFormationIndex = 4; // V Formation
                    spawnStationary = false;
                    spawnAltitude = 0f;
                    altitudeInputText = "0";
                    break;
                case 3: // Squadron
                    groupCount = 4;
                    groupSpacing = 50f;
                    selectedFormationIndex = 4; // V Formation
                    spawnStationary = false;
                    spawnAltitude = 1000f;
                    altitudeInputText = "1000";
                    break;
                case 4: // Air Patrol
                    groupCount = 2;
                    groupSpacing = 80f;
                    selectedFormationIndex = 0; // Line
                    spawnStationary = false;
                    spawnAltitude = 1500f;
                    altitudeInputText = "1500";
                    break;
                case 5: // Naval Group
                    groupCount = 3;
                    groupSpacing = 120f;
                    selectedFormationIndex = 1; // Column
                    spawnStationary = false;
                    spawnAltitude = 0f;
                    altitudeInputText = "0";
                    break;
                case 6: // Anti-Air Battery
                    groupCount = 3;
                    groupSpacing = 20f;
                    selectedFormationIndex = 0; // Line
                    spawnStationary = true;
                    spawnAltitude = 0f;
                    altitudeInputText = "0";
                    break;
                case 7: // Base Defense
                    groupCount = 4;
                    groupSpacing = 15f;
                    selectedFormationIndex = 3; // Circle
                    spawnStationary = true;
                    spawnAltitude = 0f;
                    altitudeInputText = "0";
                    break;
            }
            groupSpacingInputText = groupSpacing.ToString("0");
            ghost.Clear(); // force redraw
        }

        internal bool IsShipDefinition(UnitDefinition def)
        {
            if (def == null) return false;

            if (def.unitPrefab != null)
            {
                if (def.unitPrefab.GetComponent<Ship>() != null) return true;
                if (def.unitPrefab.GetComponentInChildren<Ship>(true) != null) return true;
            }

            string n = (def.unitName ?? def.name ?? "").ToLowerInvariant();
            if (n.Contains("ship") || n.Contains("boat") || n.Contains("naval")) return true;

            return false;
        }

        private System.Collections.IEnumerator LogShipStateForSeconds(Unit ship, UnitDefinition def, float duration)
        {
            if (ship == null) yield break;
            
            float elapsed = 0f;
            Rigidbody rb = ship.GetComponent<Rigidbody>();
            Ship shipComponent = ship as Ship;

            HorusPlugin.Logger.LogInfo($"[HORUS SHIP STATE MONITOR] Start monitoring '{ship.unitName}'");

            while (elapsed < duration)
            {
                if (ship == null || ship.gameObject == null)
                {
                    HorusPlugin.Logger.LogWarning($"[HORUS SHIP STATE MONITOR] Ship '{def.unitName}' GameObject became null/destroyed after {elapsed:F2} seconds!");
                    yield break;
                }

                Vector3 pos = ship.transform.position;
                Vector3 rot = ship.transform.rotation.eulerAngles;
                float upDot = Vector3.Dot(ship.transform.up, Vector3.up);
                float distToSea = pos.y - Datum.LocalSeaY;
                Vector3 vel = rb != null ? rb.velocity : Vector3.zero;
                Vector3 angVel = rb != null ? rb.angularVelocity : Vector3.zero;
                bool isDead = ship.unitState == Unit.UnitState.Destroyed;
                bool isDisabled = ship.disabled;
                string state = ship.unitState.ToString();
                
                bool isFlooding = false;
                if (shipComponent != null)
                {
                    try {
                        var floodedField = typeof(Ship).GetField("flooded", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (floodedField != null) isFlooding = (bool)floodedField.GetValue(shipComponent);
                    } catch {}
                }

                HorusPlugin.Logger.LogInfo(
                    $"[HORUS SHIP STATE MONITOR] {elapsed:F2}s | pos={pos} | rot={rot} | upDot={upDot:F3} | distToSea={distToSea:F2}m | " +
                    $"vel={vel} | angVel={angVel} | dead={isDead} | disabled={isDisabled} | state={state} | flooding={isFlooding}"
                );

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (ship != null)
            {
                HorusPlugin.Logger.LogInfo($"[HORUS SHIP STATE MONITOR] Finished monitoring '{ship.unitName}'. Final status: dead={(ship.unitState == Unit.UnitState.Destroyed)}, disabled={ship.disabled}");
            }
        }
    }
}
