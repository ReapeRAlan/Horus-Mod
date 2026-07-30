using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.Networking;
using Mirage;
using HorusMod.Networking;
using HorusMod.Placement;
using HorusMod.Economy;
using HorusMod.Logging;
using HorusMod.Diagnostics;
using HorusMod.Data;
using HorusMod.Interaction;
using HorusMod.UI;
using HorusMod.UI.ContextMenu;
using UnityEngine.SceneManagement;

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
        private Rect windowRect = HorusPrefs.DefaultWindowRect;
        
        private int selectedFactionIndex = 0;
        private int selectedCategoryIndex = 0;
        private string armedFactoryPresetName = null;
        private int cachedFactionCount = -1;
        private string[] cachedFactionLabels;
        
        private float spawnAltitude = 0f;
        private float spawnYaw = 0f;
        private string altitudeInputText = "0";
        private string yawInputText = "0";
        private bool hideGUI = false;
        private bool isMouseOverGUI = false;
        private bool mapSpawnMode = false;
        private bool mapOpenedByHorus = false;
        private bool snapToGround = true;
        private bool alignToSurface = false;
        private Vector3 lastSurfaceNormal = Vector3.up;

        // Ghost preview (local-only, non-networked)
        private readonly GhostPreview ghost = new GhostPreview();
        private bool ghostPreviewEnabled = true;
        private UnitDefinition ghostBuildFailedDef;

        // Units spawned by Horus this session (for safe deletion)
        private readonly HashSet<Unit> horusSpawnedUnits = new HashSet<Unit>();
        private HorusSelection worldSelection;
        private HorusOrders worldOrders;
        private HorusInputRouter inputRouter;
        private HorusOverlay worldOverlay;
        private bool cursorLockedByHorus;
        private UnitDefinition armedDefinitionOverride;
        private Unit lastSpawnedUnit;
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
        private bool showPlacementTools = false;
        private bool showGroupTools = false;
        private bool showAircraftCustomizationTools = false;
        private Vector2 mainScroll;

        // Performance & Throttled Cleanup
        private float lastPerformanceCleanupTime = 0f;

        // Aircraft Customization (Patch 0.33.4)
        public enum AircraftLiveryMode { Default = 0, FactionDefault = 1, Random = 2, Specific = 3 }
        public enum AircraftLoadoutMode { Default = 0, StandardPreset = 1, RandomStandardPreset = 2 }

        public AircraftLiveryMode aircraftLiveryMode = AircraftLiveryMode.Default;
        public AircraftLoadoutMode aircraftLoadoutMode = AircraftLoadoutMode.Default;
        public bool applyCustomizationToGroups = false;
        public int selectedLiveryIndex = 0;
        public int selectedStandardLoadoutIndex = 0;
        public float selectedAircraftSkill = 0.5f;


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
        private int cachedGroupFactionIndex = int.MinValue;
        private string[] cachedGroupPresetNames;
        
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

        public static int sceneReloadCount = 0;
        public static string horusManagerInstanceId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        public static int sceneLoadedSubscriptions = 0;
        public static int sceneUnloadedSubscriptions = 0;
        public static string lastSpawnResult = "None";
        public static string lastDeleteResult = "None";
        public static string lastBlockedAction = "None";
        public static string lastLifecycleEvent = "None";

        public HorusSelection WorldSelection => worldSelection;
        public HorusInputRouter InputRouter => inputRouter;
        public HorusOverlay WorldOverlay => worldOverlay;
        public IEnumerable<Unit> HorusSpawnedUnits => horusSpawnedUnits;
        public bool IsPointerOverHorusUI => isMouseOverGUI || HorusContextMenu.IsOpen;
        public FormationKind CurrentFormation => FormationSolver.FromName(formationNames[Mathf.Clamp(selectedFormationIndex, 0, formationNames.Length - 1)]);
        public Rect WindowRect { get => windowRect; set => windowRect = value; }
        public float SpawnAltitude => spawnAltitude;
        public float SpawnYaw => spawnYaw;
        public UnitDefinition ArmedDefinition => GetSelectedDefinition();

        public PlacementOptions CapturePlacementOptions(UnitDefinition definition = null)
        {
            definition ??= GetSelectedDefinition();
            return new PlacementOptions(
                definition,
                selectedFactionIndex,
                spawnAltitude,
                ApplyRotationSnap(spawnYaw),
                gridSnapEnabled,
                gridSize,
                snapToGround,
                alignToSurface,
                spawnStationary,
                CurrentFormation,
                groupSpacing,
                (int)aircraftLiveryMode,
                (int)aircraftLoadoutMode,
                selectedLiveryIndex,
                selectedStandardLoadoutIndex,
                selectedAircraftSkill,
                applyCustomizationToGroups);
        }

        public void ToggleUiVisibility()
        {
            hideGUI = !hideGUI;
            if (hideGUI) HorusContextMenu.Close();
        }

        private void Awake()
        {
            // Singleton guard: if another instance already exists, destroy this duplicate
            if (Instance != null && Instance != this)
            {
                HorusLog.Warning("Core", $"[HORUS LIFECYCLE] Duplicate HorusManager detected (existing={HorusManager.horusManagerInstanceId}). Destroying duplicate.");
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            windowRect = HorusPrefs.LoadWindow();
            ghostPreviewEnabled = HorusPlugin.EnableGhostPreview.Value;
            autoOceanSnapForShips = HorusPlugin.AutoOceanSnapForShips.Value;
            oceanSnapActive = HorusPlugin.OceanSnapActive.Value;
            deleteRange = HorusPlugin.DeleteRange.Value;
            deleteRangeInputText = deleteRange.ToString("0");
            enableGroupSpawn = HorusPlugin.EnableGroupSpawn.Value;
            spawnStationary = HorusPlugin.SpawnGroundUnitsStationary.Value;
            
            // Initialize economy manager
            economyManager = new RtsEconomyManager();
            worldSelection = new HorusSelection();
            worldOrders = new HorusOrders(this);
            worldOverlay = new HorusOverlay(worldSelection, worldOrders);
            inputRouter = new HorusInputRouter(this, worldSelection, worldOrders, worldOverlay);

            RefreshSavedCustomGroups();
            HorusLog.Info("Core", $"HorusManager created. Instance ID: {horusManagerInstanceId}");
            HorusLog.Info("Core", "[HORUS DEBUG] HorusManager Awake");
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnMissionLoaded;
            sceneLoadedSubscriptions++;
            SceneManager.sceneUnloaded += OnMissionUnloaded;
            sceneUnloadedSubscriptions++;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnMissionLoaded;
            sceneLoadedSubscriptions--;
            SceneManager.sceneUnloaded -= OnMissionUnloaded;
            sceneUnloadedSubscriptions--;
        }

        private void OnMissionUnloaded(Scene scene)
        {
            lastLifecycleEvent = "Scene unloaded";
            HorusLog.Info("Core", "[HORUS LIFECYCLE] Scene unloaded");
            ResetRuntimeState();
        }

        private void OnMissionLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneReloadCount++;
            lastLifecycleEvent = "Scene loaded";
            HorusLog.Info("Core", "[HORUS LIFECYCLE] Scene loaded");
            HorusLog.Info("Core", "[HORUS LIFECYCLE] Waiting for game services");
            // The actual readiness checks are naturally handled by the game logic / our updates,
            // but we can initialize mission state here.
            InitializeMissionState();
        }

        private void ResetRuntimeState()
        {
            lastLifecycleEvent = "Runtime state reset";
            HorusLog.Info("Core", "[HORUS LIFECYCLE] Runtime state reset");
            
            // Clear any old spawned units tracking
            horusSpawnedUnits.Clear();
            worldSelection?.Clear();
            inputRouter?.Reset();
            HorusUndo.Clear();
            HorusContextMenu.Close();
            UnitBrowser.Reset();
            armedDefinitionOverride = null;
            armedFactoryPresetName = null;
            cachedFactionCount = -1;
            cachedGroupFactionIndex = int.MinValue;
            
            // Destroy ghost preview if it exists
            ghost.Clear();
            
            // Turn off active Horus mode if on
            if (horusActive)
            {
                ToggleHorusMode();
            }
            
            // Clear factory/economy state
            economyManager?.ResetRuntimeState();
            if (RtsFactoryManager.Instance != null)
            {
                RtsFactoryManager.Instance.activeFactories.Clear();
            }

        }

        private void InitializeMissionState()
        {
            // Log readiness of each service individually — these may be null during loading screens
            HorusLog.Info("Core", $"[HORUS LIFECYCLE] Spawner.i: {(Spawner.i != null ? "READY" : "NOT READY")}");
            HorusLog.Info("Core", $"[HORUS LIFECYCLE] Encyclopedia.i: {(Encyclopedia.i != null ? "READY" : "NOT READY")}");
            HorusLog.Info("Core", $"[HORUS LIFECYCLE] FactionRegistry: {(FactionRegistry.factions != null ? $"READY ({FactionRegistry.factions.Count} factions)" : "NOT READY")}");
            HorusLog.Info("Core", $"[HORUS LIFECYCLE] GameManager.gameState: {GameManager.gameState}");
            HorusLog.Info("Core", $"[HORUS LIFECYCLE] RtsEconomyManager: {(economyManager != null ? "READY" : "NOT READY")}");
            HorusLog.Info("Core", $"[HORUS LIFECYCLE] RtsFactoryManager: {(RtsFactoryManager.Instance != null ? "READY" : "NOT READY")}");

            bool allReady = Spawner.i != null && Encyclopedia.i != null && FactionRegistry.factions != null;
            lastLifecycleEvent = allReady ? "Mission services ready" : "Mission loaded (some services pending)";
            HorusLog.Info("Core", $"[HORUS LIFECYCLE] {lastLifecycleEvent}");
        }

        private void Start()
        {
            HorusLog.Info("Core", "[HORUS DEBUG] HorusManager Start");
        }

        private void Update()
        {
            HorusPerformanceTracker.BeginFrameTrace();

            // Periodic 3s cleanup & metric collection (avoids per-frame heavy scans)
            if (Time.timeSinceLevelLoad - lastPerformanceCleanupTime > 3.0f)
            {
                float t0 = Time.realtimeSinceStartup;
                horusSpawnedUnits.RemoveWhere(u => u == null);
                HorusPerformanceTracker.LastCleanupDurationMs = (Time.realtimeSinceStartup - t0) * 1000f;
                HorusPerformanceTracker.ActiveSpawnedUnitsCount = horusSpawnedUnits.Count;
                if (RtsFactoryManager.Instance != null)
                    HorusPerformanceTracker.ActiveFactoriesCount = RtsFactoryManager.Instance.activeFactories.Count;
                lastPerformanceCleanupTime = Time.timeSinceLevelLoad;
            }

            // Tick economy manager (income, cleanup) even if Horus overlay is not active
            economyManager?.Tick();

            if (Input.GetKeyDown(HorusPlugin.HotkeyToggleMode.Value))
            {
                HorusLog.Verbose("Input", "Toggle Horus Mode key pressed.");
                ToggleHorusMode();
            }

            if (!horusActive)
            {
                if (cursorLockedByHorus) SetHorusCursorLock(false);
                HorusPerformanceTracker.EndFrameTrace();
                return;
            }

            // If the mission ended while active, tear down the preview safely.
            if (!HorusPermissions.InMission())
            {
                ghost.Clear();
                horusSpawnedUnits.Clear();
                inputRouter.Reset();
                HorusPerformanceTracker.EndFrameTrace();
                return;
            }

            if (Input.GetKeyDown(HorusPlugin.HotkeyToggleUI.Value))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    horusActive = true;
                    hideGUI = false;
                    windowRect = HorusPrefs.ResetWindow();
                    mainScroll = Vector2.zero;
                    if (HorusPlugin.UIScale != null) HorusPlugin.UIScale.Value = 1.0f;
                    inputRouter.Reset();
                    armedDefinitionOverride = null;
                    armedFactoryPresetName = null;
                    HorusToasts.Clear();
                    ExitMapSpawnMode();
                    HorusWindowRoot.ResetActiveTab();
                    SetHorusCursorLock(false);
                    HorusLog.Warning("Input", "Emergency UI reset performed.");
                }
                else
                {
                    hideGUI = !hideGUI;
                    if (hideGUI) HorusContextMenu.Close();
                    HorusLog.Info("Core", $"UI Toggled. hideGUI = {hideGUI}");
                }
            }

            bool mapOpen = DynamicMap.mapMaximized;

            // Read placement scroll shortcuts FIRST, before anything could consume the delta.
            HandleScrollShortcuts(mapOpen);

            if (mapSpawnMode && !DynamicMap.mapMaximized)
            {
                mapSpawnMode = false;
                mapOpenedByHorus = false;
            }
            inputRouter.Update();
            HorusPerformanceTracker.EndFrameTrace();
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
                SetSpawnAltitude(Mathf.Round(spawnAltitude + dir * step));
                HorusLog.Verbose("Input", $"Altitude -> {spawnAltitude:F0} m.");
            }
            else // alt
            {
                float step = shift ? HorusPlugin.RotationStepLarge.Value : HorusPlugin.RotationStep.Value;
                spawnYaw = ApplyRotationSnap(NormalizeAngle(spawnYaw + dir * step));
                yawInputText = spawnYaw.ToString("0");
                HorusLog.Verbose("Input", $"Yaw -> {spawnYaw:F0}°.");
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

        internal void SetHorusCursorLock(bool locked)
        {
            if (cursorLockedByHorus == locked) return;
            cursorLockedByHorus = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        internal Vector2 RawScreenToScaledGui(Vector2 rawScreen)
        {
            float scale = HorusPlugin.UIScale != null ? Mathf.Max(0.1f, HorusPlugin.UIScale.Value) : 1f;
            return new Vector2(rawScreen.x / scale, (Screen.height - rawScreen.y) / scale);
        }

        internal void UpdateFreeCamera(bool rotateCamera)
        {
            if (rotateCamera)
            {
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
                HorusLog.Warning("Core", $"Cannot activate Horus Mode. Current GameState: {GameManager.gameState}");
                return;
            }

            horusActive = !horusActive;
            HorusLog.Info("Core", $"[HORUS DEBUG] Horus mode toggled: {horusActive}");
            ExitMapSpawnMode();
            if (!horusActive)
            {
                ghost.Clear();
                armedFactoryPresetName = null;
                armedDefinitionOverride = null;
                inputRouter.Reset();
                SetHorusCursorLock(false);
                
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
                            HorusLog.Warning("Core", "Horus camera restore: Saved unit was destroyed while in Horus Mode.");
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
                inputRouter.SetTool(HorusTool.Select);
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
            
            HorusLog.Info("Core", $"Horus Mode toggled: {horusActive}");

        }

        /// <summary>
        /// Enables map spawn mode and opens the in-game map so the user can click to place units.
        /// </summary>
        private void EnterMapSpawnMode()
        {
            mapSpawnMode = true;
            inputRouter.SetTool(HorusTool.MapPlace);
            HorusLog.Info("Core", "Map Spawn Mode: ON");

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
                HorusLog.Warning("Core", $"Could not auto-open map: {ex.Message}");
            }
        }

        /// <summary>
        /// Disables map spawn mode and closes the map again if Horus opened it.
        /// </summary>
        private void ExitMapSpawnMode()
        {
            if (mapSpawnMode)
            {
                HorusLog.Info("Core", "Map Spawn Mode: OFF");
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
                HorusLog.Warning("Core", $"Could not auto-close map: {ex.Message}");
            }

            mapSpawnMode = false;
            mapOpenedByHorus = false;
            if (inputRouter != null && inputRouter.Tool == HorusTool.MapPlace)
                inputRouter.SetTool(armedDefinitionOverride != null ? HorusTool.Place : HorusTool.Select);
        }

        private void OnGUI()
        {
            if (!horusActive) return;
            HorusTheme.EnsureBuilt();

            ClampWindowRect();

            float scale = HorusPlugin.UIScale != null ? HorusPlugin.UIScale.Value : 1.0f;
            if (scale <= 0f) scale = 1.0f;
            
            Matrix4x4 originalMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            worldOverlay?.Draw(inputRouter != null && inputRouter.MarqueeActive, inputRouter != null ? inputRouter.MarqueeRawScreen : default);
            if (mapSpawnMode && DynamicMap.mapMaximized) DrawMapSpawnOverlay();
            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            if (Mathf.Abs(scale - 1.0f) > 0.01f)
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                mouseScreenPos /= scale;
            }

            isMouseOverGUI = (!hideGUI && windowRect.Contains(mouseScreenPos)) || HorusContextMenu.ContainsPoint(mouseScreenPos);

            if (hideGUI)
            {
                HorusContextMenu.Draw();
                GUI.matrix = originalMatrix;
                return;
            }

            try
            {
                Rect nextWindowRect = GUI.Window(999, windowRect, DrawHorusWindow, $"Horus Editor v{HorusPlugin.PluginVersion}", HorusTheme.Window);
                HorusWindowRoot.ApplyRequestedSize(ref nextWindowRect);
                windowRect = nextWindowRect;
                HorusContextMenu.Draw();
            }
            catch (Exception ex)
            {
                HorusLog.Error("Core", $"[Horus UI] Error drawing window: {ex.Message}");
            }
            finally
            {
                HorusTheme.EndSkinScope();
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
            if (float.IsNaN(windowRect.width) || float.IsInfinity(windowRect.width)) windowRect.width = HorusPrefs.DefaultWindowRect.width;
            if (float.IsNaN(windowRect.height) || float.IsInfinity(windowRect.height)) windowRect.height = HorusPrefs.DefaultWindowRect.height;

            windowRect.width = Mathf.Clamp(windowRect.width, 360f, Mathf.Max(360f, screenW));
            windowRect.height = Mathf.Clamp(windowRect.height, 320f, Mathf.Max(320f, screenH));
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
            string[] presetNames = GetFactoryPresetNames(presets);
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
                        HorusLog.Warning("Core", "[HORUS RTS] Create Factory From Aimed Unit failed: invalid target.");
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

        // --- Selection helper ---
        private UnitDefinition GetSelectedDefinition()
        {
            return armedDefinitionOverride;
        }

        public void DeselectSelectedUnit()
        {
            armedDefinitionOverride = null;
            ghost?.Clear();
            lastSpawnResult = "Unit deselected.";
            HorusLog.Verbose("Selection", "Unit deselected by user.");
        }

        public void ArmDefinition(UnitDefinition definition)
        {
            if (definition == null) return;
            armedDefinitionOverride = definition;
            selectedCategoryIndex = GetUnitCategoryIndex(definition);
            SetSpawnAltitude(spawnAltitude, definition);
            armedFactoryPresetName = null;
            ghost.Clear();
            inputRouter.SetTool(HorusTool.Place);
        }

        internal void DrawFactionSelector()
        {
            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0)
            {
                GUILayout.Label("No playable factions loaded.", HorusTheme.LabelMuted);
                return;
            }

            if (selectedFactionIndex < 0 || selectedFactionIndex > factions.Count) selectedFactionIndex = 0;
            if (cachedFactionLabels == null || cachedFactionCount != factions.Count)
            {
                cachedFactionCount = factions.Count;
                cachedFactionLabels = new string[factions.Count + 1];
                for (int i = 0; i < factions.Count; i++)
                    cachedFactionLabels[i] = factions[i] != null ? factions[i].factionName : "Unknown";
                cachedFactionLabels[factions.Count] = "Neutral";
            }
            int previous = selectedFactionIndex;
            GUILayout.Label("Faction", HorusTheme.LabelMuted);
            selectedFactionIndex = GUILayout.SelectionGrid(selectedFactionIndex, cachedFactionLabels, 2);
            if (previous != selectedFactionIndex)
            {
                selectedGroupPresetIndex = 0;
                ghost.Clear();
            }
        }

        public void CancelPlacement()
        {
            if (mapSpawnMode) ExitMapSpawnMode();
            armedDefinitionOverride = null;
            armedFactoryPresetName = null;
            ghost.Clear();
            inputRouter.SetTool(HorusTool.Select);
        }

        public void CancelMapPlacement()
        {
            ExitMapSpawnMode();
            armedDefinitionOverride = null;
            armedFactoryPresetName = null;
            ghost.Clear();
            inputRouter.SetTool(HorusTool.Select);
        }

        public void HideGhost()
        {
            if (ghost.IsBuilt) ghost.SetVisible(false);
        }

        public void ResetPlacementYaw()
        {
            spawnYaw = 0f;
            yawInputText = "0";
        }

        public void CycleFormation(int direction)
        {
            selectedFormationIndex = (selectedFormationIndex + direction) % formationNames.Length;
            if (selectedFormationIndex < 0) selectedFormationIndex += formationNames.Length;
            ghost.Clear();
        }

        internal Vector2 GetAltitudeRange(UnitDefinition definition = null)
        {
            definition ??= GetSelectedDefinition();
            if (definition == null) return new Vector2(0f, 15000f);
            float minimum = definition.minEditorHeight;
            float maximum = Mathf.Max(minimum, definition.maxEditorHeight);
            return new Vector2(minimum, maximum);
        }

        internal void SetSpawnAltitude(float value, UnitDefinition definition = null)
        {
            Vector2 range = GetAltitudeRange(definition);
            spawnAltitude = Mathf.Clamp(value, range.x, range.y);
            altitudeInputText = spawnAltitude.ToString("0");
            ghost.Clear();
        }

        public Unit PlaceAtWorld(Vector3 rawPosition)
        {
            lastSpawnedUnit = null;
            if (!HorusPermissions.CanSpawn()) return null;
            if (!string.IsNullOrEmpty(armedFactoryPresetName))
            {
                Vector3 position = GetFinalPlacementPosition(rawPosition);
                float yaw = NormalizeAngle(GetPlacementRotation().eulerAngles.y);
                RtsFactory created = RtsFactoryManager.Instance?.CreateFactoryAtPlacement(position, yaw, armedFactoryPresetName, selectedFactionIndex);
                if (created != null)
                {
                    selectedFactory = created;
                    armedFactoryPresetName = null;
                    ghost.Clear();
                }
                return null;
            }

            if (enableGroupSpawn)
            {
                SpawnGroup(rawPosition);
                return null;
            }

            SpawnSelectedUnit(GetFinalPlacementPosition(rawPosition));
            return lastSpawnedUnit;
        }

        public Unit PlaceAtMap(GlobalPosition mapPosition)
        {
            Vector3 local = new GlobalPosition(mapPosition.x, 0f, mapPosition.z).ToLocalPosition();
            return PlaceAtWorld(local);
        }

        public void FocusSelection()
        {
            if (worldSelection == null || !worldSelection.HasSelection || CameraStateManager.i == null) return;
            Vector3 centroid = worldSelection.Centroid();
            float maxRadius = 50f;
            foreach (Unit unit in worldSelection.Units)
                if (unit != null && unit.definition != null)
                    maxRadius = Mathf.Max(maxRadius, Mathf.Max(unit.definition.length, unit.definition.width) * 2.5f);
            CameraStateManager.i.FocusPosition(centroid, null, maxRadius);
        }

        public void DuplicateSelection()
        {
            if (!HorusPermissions.CanSpawn() || worldSelection == null || Spawner.i == null) return;
            var duplicates = new List<Unit>();
            foreach (Unit source in worldSelection.Units)
            {
                if (source == null || source.definition == null) continue;
                Vector3 offset = source.transform.right * Mathf.Max(25f, source.definition.width * 1.5f);
                GlobalPosition duplicatePosition = (source.transform.position + offset).ToGlobalPosition();
                Unit duplicate;
                if (source is Ship)
                {
                    int factionIndex = FactionRegistry.factions != null && source.NetworkHQ?.faction != null
                        ? FactionRegistry.factions.IndexOf(source.NetworkHQ.faction)
                        : -1;
                    if (factionIndex < 0) factionIndex = FactionRegistry.factions?.Count ?? 0;
                    duplicate = SpawnShipSafe(source.definition, duplicatePosition, source.transform.eulerAngles.y, factionIndex);
                }
                else
                {
                    duplicate = Spawner.i.SpawnFromUnitDefinitionInEditor(
                        source.definition,
                        duplicatePosition,
                        source.transform.rotation,
                        source.NetworkHQ,
                        (source.definition.jsonKey ?? "unit") + "_copy_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                }
                if (duplicate == null) continue;
                horusSpawnedUnits.Add(duplicate);
                if (source is Aircraft sourceAircraft && duplicate is Aircraft duplicateAircraft)
                {
                    duplicateAircraft.Networkloadout = sourceAircraft.Networkloadout;
                    duplicateAircraft.SetLiveryKey(sourceAircraft.NetworkLiveryKey, true);
                    duplicateAircraft.skill = sourceAircraft.skill;
                    duplicateAircraft.bravery = sourceAircraft.bravery;
                }
                else if (source is GroundVehicle sourceVehicle && duplicate is GroundVehicle duplicateVehicle)
                {
                    duplicateVehicle.skill = sourceVehicle.skill;
                }
                else if (source is Ship sourceShip && duplicate is Ship duplicateShip)
                {
                    duplicateShip.skill = sourceShip.skill;
                }
                HorusUndo.RecordSpawn(duplicate);
                duplicates.Add(duplicate);
            }
            worldSelection.SelectAll(duplicates);
            if (duplicates.Count > 0) HorusToasts.Show($"Duplicated {duplicates.Count} unit(s)");
        }

        public void DeleteSelection()
        {
            if (worldSelection == null || !worldSelection.HasSelection) return;
            DeleteUnits(worldSelection.Units);
            worldSelection.Clear();
        }

        public void DeleteUnits(IEnumerable<Unit> units)
        {
            if (!HorusPermissions.CanDelete() || units == null) return;
            var copy = new List<Unit>(units);
            int deleted = 0;
            foreach (Unit unit in copy)
            {
                if (unit == null || !IsSafeDeleteTarget(unit.gameObject)) continue;
                HorusUndo.RecordDelete(unit);
                HorusDeleteManager.DeleteUnit(unit, horusSpawnedUnits);
                deleted++;
            }
            lastDeleteResult = deleted > 0 ? $"Deleted {deleted} unit(s)." : "No deletable units.";
        }

        public void ApplyAircraftCustomizationIfApplicable(Aircraft aircraft, FactionHQ hq, PlacementOptions options = null)
        {
            if (aircraft == null || aircraft.definition == null) return;
            options ??= CapturePlacementOptions(aircraft.definition);

            AircraftDefinition acDef = aircraft.definition as AircraftDefinition;
            AircraftParameters acParams = acDef != null ? acDef.aircraftParameters : null;
            if (acParams == null) return;

            try
            {
                // 1. Livery Application
                AircraftLiveryMode liveryMode = (AircraftLiveryMode)options.AircraftLiveryMode;
                if (liveryMode != AircraftLiveryMode.Default && acParams.liveries != null && acParams.liveries.Count > 0)
                {
                    int targetLiveryIndex = 0;
                    switch (liveryMode)
                    {
                        case AircraftLiveryMode.FactionDefault:
                            targetLiveryIndex = acParams.GetFirstLiveryForFaction(hq != null ? hq.faction : null);
                            break;
                        case AircraftLiveryMode.Random:
                            targetLiveryIndex = acParams.GetRandomLiveryForFaction(hq != null ? hq.faction : null);
                            break;
                        case AircraftLiveryMode.Specific:
                            if (options.SelectedLiveryIndex >= 0 && options.SelectedLiveryIndex < acParams.liveries.Count)
                                targetLiveryIndex = options.SelectedLiveryIndex;
                            break;
                    }

                    aircraft.SetLiveryKey(new LiveryKey(targetLiveryIndex), true);
                    HorusLog.Verbose("UnitEditor", $"Applied livery index {targetLiveryIndex} to {aircraft.unitName}.");
                }

                // 2. Standard Loadout Preset Application
                AircraftLoadoutMode loadoutMode = (AircraftLoadoutMode)options.AircraftLoadoutMode;
                if (loadoutMode != AircraftLoadoutMode.Default && acParams.StandardLoadouts != null && acParams.StandardLoadouts.Length > 0)
                {
                    StandardLoadout selectedPreset = null;
                    switch (loadoutMode)
                    {
                        case AircraftLoadoutMode.StandardPreset:
                            if (options.SelectedLoadoutIndex >= 0 && options.SelectedLoadoutIndex < acParams.StandardLoadouts.Length)
                                selectedPreset = acParams.StandardLoadouts[options.SelectedLoadoutIndex];
                            break;
                        case AircraftLoadoutMode.RandomStandardPreset:
                            selectedPreset = acParams.GetRandomStandardLoadout(acDef, hq);
                            break;
                    }

                    if (selectedPreset != null && selectedPreset.loadout != null)
                    {
                        aircraft.Networkloadout = selectedPreset.loadout;
                        HorusLog.Verbose("UnitEditor", $"Applied standard loadout '{selectedPreset.Name}' to {aircraft.unitName}.");
                    }
                }

                // Skill is part of the same placement preset and is applied consistently
                // to single aircraft and (when enabled) group aircraft.
                HorusUnitEditor.SetSkill(aircraft, options.Skill);
            }
            catch (Exception ex)
            {
                HorusLog.Error("UnitEditor", $"Error applying customization: {ex.Message}");
            }
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
        internal Vector3 GetFinalPlacementPosition(Vector3 rawPosition, UnitDefinition def = null, int cat = -1, bool applySpacing = true)
        {
            if (def == null) def = GetSelectedDefinition();
            if (cat < 0) cat = selectedCategoryIndex;
            PlacementOptions options = CapturePlacementOptions(def);

            Vector3 pos = ApplyGridSnap(rawPosition);

            bool isShip = IsShipDefinition(def) || (cat == 2);
            // Dedicated safe ship placement path
            if (isShip)
            {
                float lift = HorusPlugin.ShipSpawnLift.Value;
                pos.y = GetOceanLevel() + options.Altitude + (def != null ? def.spawnOffset.y : 0f) + lift;

                // Enforce safe distance between ships to prevent overlap/sinking
                float thisShipLength = (def != null) ? def.length : 150f;
                if (thisShipLength <= 0f) thisShipLength = 150f;

                if (applySpacing && UnitRegistry.allUnits != null)
                {
                    const float SpacingEpsilon = 0.5f;
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

                            if (distXZ < minSafeDistance - 0.01f)
                            {
                                Vector3 diff = pos - otherPos;
                                diff.y = 0f;
                                Vector3 pushDir = diff.sqrMagnitude > 0.01f ? diff.normalized : otherShip.transform.right;
                                Vector3 newPos = otherPos + pushDir * (minSafeDistance + SpacingEpsilon);
                                
                                if (float.IsNaN(newPos.x) || float.IsNaN(newPos.z) || float.IsInfinity(newPos.x) || float.IsInfinity(newPos.z))
                                {
                                    HorusLog.Error("Placement", $"Rejected non-finite spacing result for '{otherShip.unitName}'.");
                                }
                                else
                                {
                                    Vector2 delta = new Vector2(newPos.x - pos.x, newPos.z - pos.z);
                                    if (delta.sqrMagnitude < 0.25f) continue;
                                    pos.x = newPos.x;
                                    pos.z = newPos.z;
                                    HorusLog.Trace("Placement", "ShipSpacing", $"Adjusted ship placement to ({pos.x:F1}, {pos.z:F1}); safe distance {minSafeDistance:F1}m.", 1f);
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
                pos.y = GetOceanLevel() + options.Altitude;
            }
            else
            {
                pos.y += options.Altitude;
                pos = ApplyGroundSnap(pos, def, cat);
            }

            // Raise position by the unit's spawnOffset so it sits correctly on the ground/water surface
            if (def != null)
            {
                pos.y += def.spawnOffset.y;
            }

            // Small vehicle clearance for ground vehicles
            if (cat == 1 && Mathf.Approximately(options.Altitude, 0f))
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
            string[] presetNames = GetGroupPresetNames();
            int customIndex = presetNames.Length - 1;
            
            if (selectedGroupPresetIndex == 0) // Homogeneous (Selected Unit)
            {
                if (currentSelected != null)
                {
                    for (int i = 0; i < groupCount; i++) list.Add(currentSelected);
                }
            }
            else if (selectedGroupPresetIndex == customIndex) // Custom Group
            {
                list.AddRange(customGroupUnits);
                if (list.Count > 0 && list[0] != null)
                {
                    category = GetUnitCategoryIndex(list[0]);
                }
            }
            else if (TryGetSelectedConvoy(out Faction.ConvoyGroup convoy))
            {
                foreach (Faction.ConvoyUnit constituent in convoy.Constituents)
                {
                    if (constituent?.Type == null) continue;
                    for (int i = 0; i < Mathf.Max(0, constituent.Count); i++)
                        list.Add(constituent.Type);
                }
                if (list.Count > 0) category = GetUnitCategoryIndex(list[0]);
            }
            return list;
        }

        internal string[] GetGroupPresetNames()
        {
            if (cachedGroupPresetNames != null && cachedGroupFactionIndex == selectedFactionIndex)
                return cachedGroupPresetNames;

            var names = new List<string> { "Selected Unit" };
            Faction faction = GetFactionSafe(selectedFactionIndex);
            List<Faction.ConvoyGroup> groups = faction?.GetConvoyGroups();
            if (groups != null)
            {
                foreach (Faction.ConvoyGroup group in groups)
                {
                    if (group != null)
                        names.Add(string.IsNullOrWhiteSpace(group.Name) ? "Faction Group" : group.Name);
                }
            }
            names.Add("Custom Group");
            if (selectedGroupPresetIndex >= names.Count) selectedGroupPresetIndex = 0;
            cachedGroupFactionIndex = selectedFactionIndex;
            cachedGroupPresetNames = names.ToArray();
            return cachedGroupPresetNames;
        }

        internal bool TryGetSelectedConvoy(out Faction.ConvoyGroup convoy)
        {
            convoy = null;
            if (selectedGroupPresetIndex <= 0) return false;
            Faction faction = GetFactionSafe(selectedFactionIndex);
            List<Faction.ConvoyGroup> groups = faction?.GetConvoyGroups();
            int convoyIndex = selectedGroupPresetIndex - 1;
            return groups != null && convoyIndex >= 0 && convoyIndex < groups.Count &&
                   (convoy = groups[convoyIndex]) != null;
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

        public static Faction GetFactionSafe(int index)
        {
            return FactionSlot.Resolve(index).Faction;
        }

        public static FactionHQ GetHQSafe(int index)
        {
            return FactionSlot.Resolve(index).HQ;
        }

        private void SpawnGroup(Vector3 centerPos)
        {
            if (!HorusPermissions.CanSpawn())
            {
                HorusLog.Warning("Core", "Horus: host permission required. Cannot spawn.");
                return;
            }

            if (Spawner.i == null)
            {
                HorusLog.Error("Core", "HorusMod: Spawner.i is null. Cannot spawn unit.");
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
                HorusLog.Warning("Core", "Horus: Group spawn blocked because the group has 0 units.");
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
                    HorusLog.Warning("Core", $"Horus: group spawn blocked. {tx.DenialReason}");
                    return;
                }
            }

            FactionHQ hq = GetHQSafe(selectedFactionIndex);
            
            Quaternion rot = GetPlacementRotation();
            var offsets = FormationSolver.GetOffsets(units.Count, groupSpacing, CurrentFormation);
            PlacementOptions groupOptions = CapturePlacementOptions();

            HorusLog.Verbose("Spawn", $"Spawning group of {units.Count} unit(s).");

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
                        if (groupOptions.ApplyAircraftToWholeGroup && spawned is Aircraft acGroup)
                        {
                            ApplyAircraftCustomizationIfApplicable(acGroup, hq, groupOptions);
                        }

                        if (spawnStationary)
                        {
                            if (spawned is GroundVehicle vehicle)
                            {
                                vehicle.SetHoldPosition(true);
                            }
                        }
                    }
                }

                if (spawned != null)
                {
                    spawnedUnits.Add(spawned);
                    HorusUndo.RecordSpawn(spawned);
                    HorusPrefs.AddRecent(def.jsonKey);
                }
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
            if (spawnedUnits.Count > 0) HorusToasts.Show($"Spawned group: {spawnedUnits.Count} unit(s)");
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
                HorusLog.Error("Core", $"Failed to list custom groups: {ex.Message}");
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
                    if (unit != null) data.unitNames.Add(!string.IsNullOrEmpty(unit.jsonKey) ? unit.jsonKey : unit.unitName);
                }
                
                string json = UnityEngine.JsonUtility.ToJson(data, true);
                System.IO.File.WriteAllText(path, json);
                HorusLog.Info("Core", $"Saved custom group '{name}' to {path}");
                RefreshSavedCustomGroups();
            }
            catch (System.Exception ex)
            {
                HorusLog.Error("Core", $"Failed to save custom group: {ex.Message}");
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
                            HorusLog.Warning("Core", $"Custom Group '{name}': UnitDefinition '{uname}' not found in Encyclopedia. Skipping.");
                        }
                    }
                    groupCount = customGroupUnits.Count;
                    HorusLog.Info("Core", $"Loaded custom group '{name}' with {customGroupUnits.Count} units.");
                    
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
                HorusLog.Error("Core", errMsg);
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
                    HorusLog.Info("Core", $"Deleted custom group file: {path}");
                }
            }
            catch (System.Exception ex)
            {
                HorusLog.Error("Core", $"Failed to delete custom group file: {ex.Message}");
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
                    if (obj is UnitDefinition def &&
                        (string.Equals(def.jsonKey, name, System.StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(def.unitName, name, System.StringComparison.OrdinalIgnoreCase)))
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
            WorldPick pick = inputRouter != null && inputRouter.Pick.Valid
                ? inputRouter.Pick
                : WorldPick.FromScreen(Input.mousePosition);
            if (pick.Valid)
            {
                localPos = GetFinalPlacementPosition(pick.Point);
                return true;
            }
            localPos = Vector3.zero;
            return false;
        }

        public Unit GetAimedUnit()
        {
            return inputRouter != null && inputRouter.Pick.Valid
                ? inputRouter.Pick.Unit
                : WorldPick.FromScreen(Input.mousePosition).Unit;
        }

        // --- Ghost preview ---

        internal void UpdateGhostAt(WorldPick pick)
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
                        if (pick.Valid)
                        {
                            ghost.UpdateTransform(GetFinalPlacementPosition(pick.Point, def, 3, false), GetPlacementRotation());
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

                if (!pick.Valid)
                {
                    ghost.SetVisible(false);
                    return;
                }

                Quaternion rot = GetPlacementRotation();
                var offsets = FormationSolver.GetOffsets(units.Count, groupSpacing, CurrentFormation);
                
                var rotatedOffsets = new System.Collections.Generic.List<Vector3>();
                foreach (var offset in offsets)
                {
                    rotatedOffsets.Add(rot * offset);
                }

                ghost.UpdateTransformGroup(pick.Point, rot, rotatedOffsets, units, (pos, def) => GetFinalPlacementPosition(pos, def, GetUnitCategoryIndex(def), false));
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

                if (!pick.Valid)
                {
                    ghost.SetVisible(false);
                    return;
                }

                ghost.UpdateTransform(GetFinalPlacementPosition(pick.Point, applySpacing: false), GetPlacementRotation());
                ghost.SetVisible(true);
            }
        }

        private void OnDestroy()
        {
            inputRouter?.Deactivate();
            ghost.Dispose();
            HorusTheme.Dispose();
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

        /// <summary>Resolves the gameplay unit through the hierarchy or damageable proxy.</summary>
        internal static Unit FindUnitRoot(GameObject target)
        {
            if (target == null) return null;
            Unit unit = target.GetComponentInParent<Unit>();
            if (unit != null) return unit;
            IDamageable damageable = target.GetComponent<IDamageable>();
            return damageable != null ? damageable.GetUnit() : null;
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
                HorusLog.Warning("Core", "Horus: host permission required. Cannot spawn.");
                return;
            }

            if (Spawner.i == null)
            {
                HorusLog.Error("Core", "HorusMod: Spawner.i is null. Cannot spawn unit.");
                return;
            }

            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0) return;

            UnitDefinition def = GetSelectedDefinition();
            if (def == null) return;
            PlacementOptions placementOptions = CapturePlacementOptions(def);

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
                    HorusLog.Warning("Core", $"Horus: spawn blocked. {tx.DenialReason}");
                    return;
                }
            }

            Faction faction = GetFactionSafe(selectedFactionIndex);
            FactionHQ hq = GetHQSafe(selectedFactionIndex);

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
                    if (spawned is Aircraft ac)
                    {
                        ApplyAircraftCustomizationIfApplicable(ac, hq, placementOptions);
                    }

                    if (spawnStationary)
                    {
                        if (spawned is GroundVehicle vehicle)
                        {
                            vehicle.SetHoldPosition(true);
                        }
                    }
                    
                    if (HorusPlugin.CreditKillsToSpawner.Value)
                    {
                        try 
                        {
                            // Implementation removed; marked as unsafe in v1.2.1
                            HorusLog.Warning("Core", "[Horus] CreditKillsToSpawner assignment audited and skipped: unsafe memory references.");
                        }
                        catch (Exception ex)
                        {
                            HorusLog.Error("Core", $"[Horus] CreditKillsToSpawner failed: {ex.Message}");
                        }
                    }
                }
                HorusLog.Verbose("Spawn", $"Spawned {def.unitName} at {globalPos}, yaw={spawnYaw:F0}°, tracked={spawned != null}.");
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
            if (spawned != null)
            {
                lastSpawnedUnit = spawned;
                HorusUndo.RecordSpawn(spawned);
                HorusPrefs.AddRecent(def.jsonKey);
                lastSpawnResult = $"Spawned {def.unitName}.";
                HorusToasts.Show($"Spawned {def.unitName}");
            }
        }

        internal Unit SpawnShipSafe(UnitDefinition def, GlobalPosition globalPos, float yaw, int faction)
        {
            HorusLog.Trace("Spawn", "ShipSafe", "Entering safe ship spawn.", 0.25f);
            if (Spawner.i == null)
            {
                HorusLog.Error("Core", "HorusMod: Spawner.i is null. Cannot spawn ship.");
                return null;
            }

            FactionSlot slot = FactionSlot.Resolve(faction);
            if (!slot.IsValid)
            {
                HorusLog.Error("Spawn", $"Invalid faction slot {faction}.");
                return null;
            }

            FactionHQ hq = slot.HQ;

            // The unified placement solver already applied sea level, editor
            // altitude, spawn offset, and lift. Preserve that exact result.
            Vector3 localPos = globalPos.ToLocalPosition();
            float targetY = localPos.y;
            if (float.IsNaN(targetY) || float.IsInfinity(targetY))
                targetY = GetOceanLevel() + spawnAltitude + def.spawnOffset.y + HorusPlugin.ShipSpawnLift.Value;
            localPos.y = targetY;

            // Convert back to global position for the Spawner
            GlobalPosition spawnGlobalPos = localPos.ToGlobalPosition();
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            HorusLog.Verbose("Spawn", $"Preparing safe ship spawn for '{def.unitName}' at ({localPos.x:F1}, {localPos.y:F1}, {localPos.z:F1}).");

            string uniqueName = (def.jsonKey ?? "ship") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            Unit spawned = null;
            try
            {
                spawned = Spawner.i.SpawnShip(def.unitPrefab, spawnGlobalPos, rot, hq, uniqueName, 1f, false);
                HorusLog.Verbose("Spawn", $"Spawned ship '{uniqueName}' through SpawnShip.");
            }
            catch (Exception ex)
            {
                HorusLog.Warning("Spawn", $"SpawnShip failed: {ex.Message}. Falling back to editor spawn.");
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

                HorusLog.Trace("Spawn", "ShipTransform", $"Ship transform corrected from {origPos} to {spawned.transform.position}.", 0.25f);

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
                    StartCoroutine(StabilizeShipAfterSpawn(spawned, targetY, yaw));
                }

            }
            else
            {
                HorusLog.Error("Spawn", $"Ship spawner returned null for '{def.unitName}'.");
            }

            return spawned;
        }

        private System.Collections.IEnumerator StabilizeShipAfterSpawn(Unit ship, float targetY, float yaw)
        {
            if (ship == null) yield break;

            Quaternion targetRot = Quaternion.Euler(0f, yaw, 0f);

            Rigidbody rb = ship.GetComponent<Rigidbody>();

            HorusLog.Trace("Spawn", "ShipStabilize", $"Stabilizing '{ship.unitName}'.", 0.25f);

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

            HorusLog.Trace("Spawn", "ShipStabilizeDone", $"Stabilization complete for '{ship.unitName}'.", 0.25f);
        }

        private void OnGroupPresetChanged(int oldVal, int newVal)
        {
            if (oldVal == newVal) return;

            if (TryGetSelectedConvoy(out Faction.ConvoyGroup convoy))
            {
                groupCount = convoy.Constituents != null
                    ? convoy.Constituents.Sum(c => c != null ? Mathf.Max(0, c.Count) : 0)
                    : 0;
                groupSpacing = 30f;
                selectedFormationIndex = 1;
                spawnStationary = false;
                spawnAltitude = 0f;
                altitudeInputText = "0";
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

    }
}
