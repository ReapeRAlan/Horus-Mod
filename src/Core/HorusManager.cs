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

    public partial class HorusManager : MonoBehaviour
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
        private bool showDebugTools = false;
        private bool showSettingsTools = false;
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
            HorusPlugin.Logger.LogInfo("[HORUS DEBUG] HorusManager Awake");
        }

        private void Start()
        {
            HorusPlugin.Logger.LogInfo("[HORUS DEBUG] HorusManager Start");
        }

        private bool hasLoggedUpdate = false;
        private void Update()
        {
            if (!hasLoggedUpdate)
            {
                HorusPlugin.Logger.LogInfo("[HORUS DEBUG] HorusManager Update running");
                hasLoggedUpdate = true;
            }

            // Tick economy manager (income, cleanup) even if Horus overlay is not active
            economyManager?.Tick();

            if (Input.GetKeyDown(HorusPlugin.HotkeyToggleMode.Value))
            {
                HorusPlugin.Logger.LogInfo("[HORUS DEBUG] F9 pressed");
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
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    horusActive = true;
                    hideGUI = false;
                    windowRect = new Rect(20, 20, 340, 700);
                    mainScroll = Vector2.zero;
                    scrollPosition = Vector2.zero;
                    if (HorusPlugin.UIScale != null) HorusPlugin.UIScale.Value = 1.0f;
                    HorusPlugin.Logger.LogWarning("Emergency UI Reset performed.");
                }
                else
                {
                    hideGUI = !hideGUI;
                    HorusPlugin.Logger.LogInfo($"UI Toggled. hideGUI = {hideGUI}");
                }
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
            HorusPlugin.Logger.LogInfo($"[HORUS DEBUG] Horus mode toggled: {horusActive}");
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

        private bool hasLoggedOnGUI = false;
        private void OnGUI()
        {
            if (!hasLoggedOnGUI)
            {
                HorusPlugin.Logger.LogInfo("[HORUS DEBUG] OnGUI running");
                hasLoggedOnGUI = true;
            }
            if (!horusActive || hideGUI) return;

            ClampWindowRect();

            float scale = HorusPlugin.UIScale != null ? HorusPlugin.UIScale.Value : 1.0f;
            if (scale <= 0f) scale = 1.0f;
            
            Matrix4x4 originalMatrix = GUI.matrix;
            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            if (Mathf.Abs(scale - 1.0f) > 0.01f)
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                mouseScreenPos /= scale;
            }

            isMouseOverGUI = windowRect.Contains(mouseScreenPos);

            if (mapSpawnMode && DynamicMap.mapMaximized)
            {
                DrawMapSpawnOverlay();
            }

            try
            {
                windowRect = GUI.Window(999, windowRect, DrawHorusWindow, $"⚡ Horus Editor v{HorusPlugin.PluginVersion}");
            }
            catch (Exception ex)
            {
                HorusPlugin.Logger.LogError($"[Horus UI] Error drawing window: {ex.Message}");
            }
            finally
            {
                GUI.matrix = originalMatrix;
            }
        }

        private void ClampWindowRect()
        {
            float scale = HorusPlugin.UIScale != null ? HorusPlugin.UIScale.Value : 1.0f;
            if (scale <= 0f) scale = 1.0f;
            
            float screenW = Screen.width / scale;
            float screenH = Screen.height / scale;

            if (float.IsNaN(windowRect.x) || float.IsInfinity(windowRect.x)) windowRect.x = 20;
            if (float.IsNaN(windowRect.y) || float.IsInfinity(windowRect.y)) windowRect.y = 20;

            windowRect.x = Mathf.Clamp(windowRect.x, 0, Mathf.Max(0, screenW - 50));
            windowRect.y = Mathf.Clamp(windowRect.y, 0, Mathf.Max(0, screenH - 50));
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
            HorusDeleteManager.HandleDeleteClick(mapSpawnMode, deleteRange, horusSpawnedUnits);
        }

        /// <summary>Finds the nearest gameplay unit root by walking UP the hierarchy only.</summary>
        internal static Unit FindUnitRoot(GameObject target)
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

        internal static bool IsBuiltinMapUnit(Unit unit)
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
        internal bool IsSafeDeleteTarget(GameObject target)
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
                    
                    if (HorusPlugin.CreditKillsToSpawner.Value)
                    {
                        HorusPlugin.Logger.LogWarning("[Horus] CreditKillsToSpawner is enabled but currently marked as experimental/unsafe. Skipping assignment.");
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
