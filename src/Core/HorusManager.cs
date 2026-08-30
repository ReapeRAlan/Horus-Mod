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
using HorusMod.Spawning;
using HorusMod.Loadouts;
using UnityEngine.SceneManagement;
#if HORUS_CLIENT
using HorusMod.Client;
using HorusMod.Shared;
#endif

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
        private int pendingFactionIndex = -1;
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
        private bool cursorStateCaptured;
        private CursorLockMode savedCursorLockState;
        private bool savedCursorVisible;
        private UnitDefinition armedDefinitionOverride;
        // Catalog buttons run during IMGUI MouseUp. Applying the new definition there
        // changes the GUILayout tree after Layout was calculated and makes the UI appear
        // one selection behind. Queue it and commit from Update before the next Layout.
        private UnitDefinition pendingArmDefinition;
        private Unit lastSpawnedUnit;
        private bool lastPlacementConsumed;
        private bool lastPlacementWasLiveOrdnance;
        private readonly Dictionary<string, string> acknowledgedLookupDefinitions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int pendingForceIncompatible = -1;
        private string pendingLookupAcknowledgement;
        // Live-ordnance launch controls. Targeted modes remain explicit/off by default so
        // an unrelated world selection can never silently redirect a dangerous spawn.
        private float missileLaunchSpeed = 250f;
        private HorusOrdnanceTargetMode ordnanceTargetMode = HorusOrdnanceTargetMode.WorldPoint;
        private float ordnanceImpactHeight = 300f;
        private readonly Queue<Action> pendingUiActions = new Queue<Action>();
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

        private enum PresetUiAction { None, SaveNew, Update, Rename, Duplicate, Delete, Reload }

        private sealed class AircraftEditorUiState
        {
            public AircraftLiveryMode LiveryMode = AircraftLiveryMode.Default;
            public LoadoutSourceKind LoadoutSource = LoadoutSourceKind.Default;
            public int LiveryIndex;
            public int StandardIndex;
            public int SavedPresetIndex;
            public string SavedPresetId = "";
            public string PresetName = "New preset";
            public float Skill = 0.5f;
            public bool ApplyToGroups;
            public bool MirrorSymmetry;
            public bool ShowHardpoints;
            public int PendingLoadoutSourceIndex = -1;
            public int PendingLiveryMode = -1;
            public int PendingShowHardpoints = -1;
            public LoadoutDraft Draft;
            public string Status = "";
            public PresetUiAction PendingPresetAction;
            public LoadoutDraft PendingPresetDraft;
            public string PendingPresetId;
            public string PendingPresetName;
        }

        private readonly Dictionary<string, AircraftEditorUiState> placementAircraftStates = new Dictionary<string, AircraftEditorUiState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AircraftEditorUiState> manageAircraftStates = new Dictionary<string, AircraftEditorUiState>(StringComparer.OrdinalIgnoreCase);

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
        private int pendingGroupEnabled = -1;
        private bool spawnStationary = false;
        private int selectedGroupPresetIndex = 0;
        private int pendingGroupPresetIndex = -1;
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
        private UnitDefinition pendingCustomGroupAdd;
        private int pendingCustomGroupRemoveIndex = -1;
        private bool pendingCustomGroupClear;
        private string pendingCustomGroupSave;
        private string pendingCustomGroupLoad;
        private string pendingCustomGroupDelete;

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
        public bool IsPointerOverHorusUI
        {
            get
            {
                Vector2 mouse = RawScreenToScaledGui(Input.mousePosition);
                return (!hideGUI && windowRect.Contains(mouse)) || HorusContextMenu.ContainsPoint(mouse);
            }
        }
        public FormationKind CurrentFormation => FormationSolver.FromName(formationNames[Mathf.Clamp(selectedFormationIndex, 0, formationNames.Length - 1)]);
        public Rect WindowRect { get => windowRect; set => windowRect = value; }
        public float SpawnAltitude => spawnAltitude;
        public float SpawnYaw => spawnYaw;
        public UnitDefinition ArmedDefinition => GetSelectedDefinition();
        public bool LastPlacementConsumed => lastPlacementConsumed;
        public bool LastPlacementWasLiveOrdnance => lastPlacementWasLiveOrdnance;

        public PlacementOptions CapturePlacementOptions(UnitDefinition definition = null)
        {
            definition ??= GetSelectedDefinition();
            AircraftEditorUiState aircraftState = definition is AircraftDefinition aircraftDefinition
                ? GetAircraftEditorState(aircraftDefinition, manage: false)
                : null;
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
                (int)(aircraftState?.LiveryMode ?? aircraftLiveryMode),
                (int)LegacyLoadoutMode(aircraftState?.LoadoutSource ?? LoadoutSourceKind.Default),
                aircraftState?.LiveryIndex ?? selectedLiveryIndex,
                aircraftState?.StandardIndex ?? selectedStandardLoadoutIndex,
                aircraftState?.Skill ?? selectedAircraftSkill,
                aircraftState?.ApplyToGroups ?? applyCustomizationToGroups,
                aircraftState?.LoadoutSource ?? LoadoutSourceKind.Default,
                aircraftState?.Draft);
        }

        private static AircraftLoadoutMode LegacyLoadoutMode(LoadoutSourceKind source)
        {
            if (source == LoadoutSourceKind.StandardPreset) return AircraftLoadoutMode.StandardPreset;
            if (source == LoadoutSourceKind.RandomStandardPreset) return AircraftLoadoutMode.RandomStandardPreset;
            return AircraftLoadoutMode.Default;
        }

        private AircraftEditorUiState GetAircraftEditorState(AircraftDefinition definition, bool manage)
        {
            if (definition == null) return null;
            CatalogEntry catalogEntry = FindCatalogEntry(definition);
            string key = !string.IsNullOrWhiteSpace(catalogEntry?.Key)
                ? catalogEntry.Key
                : !string.IsNullOrWhiteSpace(definition.jsonKey)
                    ? definition.jsonKey
                    : "instance-" + definition.GetInstanceID();
            Dictionary<string, AircraftEditorUiState> states = manage ? manageAircraftStates : placementAircraftStates;
            if (!states.TryGetValue(key, out AircraftEditorUiState state))
            {
                state = new AircraftEditorUiState
                {
                    Draft = HorusLoadoutService.CreateDefaultDraft(definition)
                };
                states.Add(key, state);
            }
            if (state.Draft == null ||
                (!string.IsNullOrWhiteSpace(state.Draft.AircraftJsonKey) &&
                 !string.Equals(state.Draft.AircraftJsonKey, definition.jsonKey, StringComparison.OrdinalIgnoreCase)))
                state.Draft = HorusLoadoutService.CreateDefaultDraft(definition);
            return state;
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
            if (horusActive) CaptureGameCursorState();
        }

        private void OnDisable()
        {
            inputRouter?.CancelPointerCapture();
            RestoreGameCursorState();
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
            worldOrders?.Reset();
            HorusUndo.Clear();
            HorusContextMenu.Close();
            UnitBrowser.Reset();
            armedDefinitionOverride = null;
            pendingArmDefinition = null;
            armedFactoryPresetName = null;
            acknowledgedLookupDefinitions.Clear();
            HorusSpawnService.ResetAuthorizations();
            pendingUiActions.Clear();
            placementAircraftStates.Clear();
            manageAircraftStates.Clear();
            UnitCatalog.Invalidate();
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

            ApplyPendingArmDefinition();
            ApplyPendingUiActions();

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
            if (HorusPermissions.InMission()) worldOrders?.Tick();

            if (Input.GetKeyDown(HorusPlugin.HotkeyToggleMode.Value))
            {
                HorusLog.Verbose("Input", "Toggle Horus Mode key pressed.");
                ToggleHorusMode();
            }

            if (!horusActive)
            {
                if (cursorStateCaptured) RestoreGameCursorState();
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
                    pendingArmDefinition = null;
                    armedFactoryPresetName = null;
                    pendingUiActions.Clear();
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
            // CameraFreeState.UpdateState is intentionally patched out while Horus is
            // active, so Horus must repair cursor drift itself.
            SetHorusCursorLock(inputRouter != null && inputRouter.Looking);
            HorusPerformanceTracker.EndFrameTrace();
        }

        /// <summary>
        /// IMGUI callbacks can run after Unity has already calculated the current
        /// GUILayout tree. Mutations that alter selection, queues, factories, or
        /// mode state are therefore committed from Update before the next Layout.
        /// </summary>
        private void QueueUiAction(Action action)
        {
            if (action != null) pendingUiActions.Enqueue(action);
        }

        private void ApplyPendingUiActions()
        {
            int pendingCount = pendingUiActions.Count;
            for (int i = 0; i < pendingCount; i++)
            {
                Action action = pendingUiActions.Dequeue();
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    HorusLog.Error("UI", "Deferred UI action failed: " + ex.Message);
                    HorusToasts.Show("Horus action failed; check the log.");
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
            cursorLockedByHorus = locked;
            // Deactivation and scene teardown can call this while normal gameplay owns
            // the cursor. Never overwrite the game's cursor unless Horus captured it.
            if (!horusActive && !cursorStateCaptured) return;
            CursorLockMode desiredLock = locked ? CursorLockMode.Locked : CursorLockMode.None;
            bool desiredVisible = !locked;
            if (Cursor.lockState != desiredLock) Cursor.lockState = desiredLock;
            if (Cursor.visible != desiredVisible) Cursor.visible = desiredVisible;
        }

        private void CaptureGameCursorState()
        {
            if (cursorStateCaptured) return;
            savedCursorLockState = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            cursorStateCaptured = true;
            SetHorusCursorLock(false);
        }

        private void RestoreGameCursorState()
        {
            cursorLockedByHorus = false;
            if (!cursorStateCaptured) return;
            Cursor.lockState = savedCursorLockState;
            Cursor.visible = savedCursorVisible;
            cursorStateCaptured = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!horusActive) return;
            inputRouter?.CancelPointerCapture();
            if (focused) SetHorusCursorLock(false);
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
            // Entering Horus requires a mission. Exiting must always be allowed,
            // especially during scene unload after GameState has already become Menu.
            if (!horusActive &&
                GameManager.gameState != GameState.SinglePlayer &&
                GameManager.gameState != GameState.Multiplayer)
            {
                HorusLog.Warning("Core", $"Cannot activate Horus Mode. Current GameState: {GameManager.gameState}");
                return;
            }

            horusActive = !horusActive;
            HorusLog.Info("Core", $"[HORUS DEBUG] Horus mode toggled: {horusActive}");
            ExitMapSpawnMode();
            if (horusActive) CaptureGameCursorState();
            if (!horusActive)
            {
                ghost.Clear();
                armedFactoryPresetName = null;
                armedDefinitionOverride = null;
                pendingArmDefinition = null;
                pendingUiActions.Clear();
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
                RestoreGameCursorState();
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
            GUILayout.Label($"Runtime status: {(string.IsNullOrEmpty(f.lastStatus) ? "Ready" : f.lastStatus)}");

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
                pendingFactoryDelete = f;
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
            bool selectedFactionUsable = manager.CanUseFactoryFaction(selectedFactionIndex, out string selectedFactionReason);
            if (!selectedFactionUsable)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(1f, 0.65f, 0.25f);
                GUILayout.Label("Placement blocked: " + selectedFactionReason);
                GUI.color = previousColor;
            }

            if (!isHost)
            {
                DrawHostOnlyButton("Create Factory Here");
                DrawHostOnlyButton("Create Factory From Aimed Unit");
                return;
            }

            GUILayout.BeginHorizontal();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && selectedFactionUsable;
            if (GUILayout.Button("Create Factory Here", GUILayout.Height(30)))
            {
                if (CanRunFactoryCreateAction())
                {
                    WorldPick factoryPick = inputRouter != null && inputRouter.Pick.Valid
                        ? inputRouter.Pick
                        : WorldPick.FromScreen(Input.mousePosition);
                    if (factoryPick.Valid)
                    {
                        Vector3 localFactory = GetFactoryPlacementPosition(factoryPick.Point, currentPresetName);
                        float yaw = spawnYaw;
                        var created = manager.CreateFactoryAtPlacement(localFactory, NormalizeAngle(yaw), currentPresetName, selectedFactionIndex);
                        if (created != null)
                        {
                            pendingFactorySelection = created;
                            pendingFactorySelectionSet = true;
                            if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Created {created.displayName}");
                        }
                    }
                    else if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage("Horus: No valid placement point.");
                    }
                }
            }
            GUI.enabled = previousEnabled;
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
                            pendingFactorySelection = created;
                            pendingFactorySelectionSet = true;
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
                    CancelPlacement();
                }
            }
            else
            {
                previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && selectedFactionUsable;
                if (GUILayout.Button("Arm Factory Placement", GUILayout.Height(30)))
                {
                    ArmFactoryPlacement(currentPresetName);
                    if (SceneSingleton<GameplayUI>.i != null)
                    {
                        SceneSingleton<GameplayUI>.i.GameMessage($"Horus: Armed placement for {currentPresetName}. Click in world/map to place.");
                    }
                }
                GUI.enabled = previousEnabled;
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
            pendingArmDefinition = null;
            armedDefinitionOverride = null;
            ghost?.Clear();
            lastSpawnResult = "Unit deselected.";
            HorusLog.Verbose("Selection", "Unit deselected by user.");
        }

        public void ArmDefinition(UnitDefinition definition)
        {
            if (definition == null) return;
            pendingArmDefinition = definition;
        }

        private void ApplyPendingArmDefinition()
        {
            UnitDefinition definition = pendingArmDefinition;
            if (definition == null) return;
            pendingArmDefinition = null;
            armedDefinitionOverride = definition;
            showAircraftCustomizationTools = definition is AircraftDefinition;
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

            if (Event.current.type == EventType.Layout && pendingFactionIndex >= 0)
            {
                int previous = selectedFactionIndex;
                selectedFactionIndex = Mathf.Clamp(pendingFactionIndex, 0, factions.Count);
                pendingFactionIndex = -1;
                if (previous != selectedFactionIndex)
                {
                    selectedGroupPresetIndex = 0;
                    cachedGroupFactionIndex = int.MinValue;
                    ghost.Clear();
                }
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
            GUILayout.Label("Faction", HorusTheme.LabelMuted);
            int nextFaction = GUILayout.SelectionGrid(selectedFactionIndex, cachedFactionLabels, 2);
            if (nextFaction != selectedFactionIndex) pendingFactionIndex = nextFaction;
        }

        private void ArmFactoryPlacement(string presetName)
        {
            pendingArmDefinition = null;
            armedDefinitionOverride = null;
            armedFactoryPresetName = presetName;
            economyManager?.DisarmDeployment();
            ghost.Clear();
            inputRouter?.SetTool(HorusTool.Place);
        }

        public void CancelPlacement()
        {
            if (mapSpawnMode) ExitMapSpawnMode();
            pendingArmDefinition = null;
            armedDefinitionOverride = null;
            armedFactoryPresetName = null;
            ghost.Clear();
            inputRouter.SetTool(HorusTool.Select);
        }

        public void CancelMapPlacement()
        {
            ExitMapSpawnMode();
            pendingArmDefinition = null;
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
            lastPlacementConsumed = false;
            lastPlacementWasLiveOrdnance = false;
            if (!HorusPermissions.CanRequestMutation()) return null;
            if (!string.IsNullOrEmpty(armedFactoryPresetName))
            {
                Vector3 position = GetFactoryPlacementPosition(rawPosition, armedFactoryPresetName);
                float yaw = NormalizeAngle(GetPlacementRotation().eulerAngles.y);
                RtsFactory created = RtsFactoryManager.Instance?.CreateFactoryAtPlacement(position, yaw, armedFactoryPresetName, selectedFactionIndex);
                if (created != null)
                {
                    lastPlacementConsumed = true;
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

            SpawnSelectedUnit(GetFinalPlacementPosition(rawPosition, logDiagnostics: true));
            lastPlacementConsumed = lastSpawnedUnit != null;
            lastPlacementWasLiveOrdnance = lastSpawnedUnit != null &&
                FindCatalogEntry(lastSpawnedUnit.definition)?.IsLiveOrdnance == true;
            return lastSpawnedUnit;
        }

        private Vector3 GetFactoryPlacementPosition(Vector3 rawPosition, string presetName)
        {
            UnitDefinition visualDefinition = null;
            RtsFactoryManager factoryManager = RtsFactoryManager.Instance;
            FactoryPreset preset = factoryManager?.Config?.factoryPresets?
                .FirstOrDefault(p => string.Equals(p?.presetName, presetName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preset?.visualBuilding))
                visualDefinition = factoryManager.ResolveVisualBuildingDefinition(preset.visualBuilding, out _, false);
            if (visualDefinition != null)
                return GetFinalPlacementPosition(rawPosition, visualDefinition, GetUnitCategoryIndex(visualDefinition));

            Vector3 position = ApplyGridSnap(rawPosition);
            if (TrySampleGroundHeight(position, out float groundY)) position.y = groundY;
            return position;
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
            if (!HorusPermissions.CanRequestMutation() || worldSelection == null || Spawner.i == null) return;
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession)
            {
                int requested = 0;
                foreach (Unit source in worldSelection.Units)
                {
                    if (source == null || source.definition == null || FindCatalogEntry(source.definition)?.IsLiveOrdnance == true) continue;
                    var payload = new HorusCommandPayload();
                    payload.UnitIds.Add(source.persistentID.Id);
                    Vector3 offset = source.transform.right * Mathf.Max(25f, source.definition.width * 1.5f);
                    payload.Points.Add(HorusRemoteAuthority.Point((source.transform.position + offset).ToGlobalPosition()));
                    if (HorusRemoteAuthority.TrySubmit(HorusCommandKind.Duplicate, payload)) requested++;
                }
                HorusToasts.Show(requested > 0 ? $"Requested {requested} duplicate(s)" : "No duplicable units selected");
                return;
            }
#endif
            var duplicates = new List<Unit>();
            foreach (Unit source in worldSelection.Units)
            {
                if (source == null || source.definition == null) continue;
                CatalogEntry sourceEntry = FindCatalogEntry(source.definition);
                if (sourceEntry?.IsLiveOrdnance == true)
                {
                    HorusToasts.Show("Live ordnance cannot be duplicated");
                    continue;
                }
                if (!CanAttemptCatalogSpawn(source.definition, out string duplicateDenial))
                {
                    HorusToasts.Show("Duplicate blocked: " + duplicateDenial);
                    continue;
                }
                Vector3 offset = source.transform.right * Mathf.Max(25f, source.definition.width * 1.5f);
                GlobalPosition duplicatePosition = (source.transform.position + offset).ToGlobalPosition();
                Unit duplicate;
                if (source is Ship)
                {
                    int factionIndex = FactionRegistry.factions != null && source.NetworkHQ?.faction != null
                        ? FactionRegistry.factions.IndexOf(source.NetworkHQ.faction)
                        : -1;
                    if (factionIndex < 0) factionIndex = FactionRegistry.factions?.Count ?? 0;
                    duplicate = SpawnShipSafe(source.definition, duplicatePosition, source.transform.eulerAngles.y,
                        factionIndex, false);
                }
                else
                {
                    var request = new HorusSpawnRequest
                    {
                        Definition = source.definition,
                        Position = duplicatePosition,
                        Rotation = source.transform.rotation,
                        HQ = source.NetworkHQ,
                        UniqueName = (source.definition.jsonKey ?? "unit") + "_copy_" + Guid.NewGuid().ToString("N").Substring(0, 6),
                        Skill = source is GroundVehicle sourceGround ? sourceGround.skill : 1f
                    };
                    if (source is Aircraft sourceAircraftForSpawn)
                    {
                        request.Aircraft = new AircraftSpawnOptions
                        {
                            Loadout = sourceAircraftForSpawn.Networkloadout,
                            FuelRatio = sourceAircraftForSpawn.NetworkfuelLevel,
                            Livery = sourceAircraftForSpawn.NetworkLiveryKey,
                            Skill = sourceAircraftForSpawn.skill,
                            Bravery = sourceAircraftForSpawn.bravery
                        };
                    }
                    if (!TryAuthorizeSpawnRequest(request, false, out string authorizationError))
                    {
                        HorusToasts.Show("Duplicate blocked: " + authorizationError);
                        continue;
                    }
                    duplicate = HorusSpawnService.Spawn(request).Unit;
                }
                if (duplicate == null) continue;
                horusSpawnedUnits.Add(duplicate);
                if (source is Aircraft sourceAircraft && duplicate is Aircraft duplicateAircraft)
                {
                    // Aircraft state was supplied before the native network spawn.
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
            if (!HorusPermissions.CanRequestDelete() || units == null) return;
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
                        LoadoutApplyResult applied = HorusLoadoutService.ApplyToAircraft(aircraft, selectedPreset.loadout);
                        if (applied.Success)
                            HorusLog.Verbose("UnitEditor", $"Applied standard loadout '{selectedPreset.Name}' to {aircraft.unitName}.");
                        else
                            HorusLog.Warning("UnitEditor", $"Could not apply standard loadout '{selectedPreset.Name}': {applied.Message}");
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
            bool groundCategory = GetPlacementSurface(unit) == PlacementSurface.Ground;
            if (snapToGround && groundCategory && TrySampleGroundHeight(position, out float groundY))
            {
                position.y = groundY + spawnAltitude;
            }
            return position;
        }

        private Quaternion GetPlacementRotation()
        {
            Quaternion yawRot = Quaternion.Euler(0f, ApplyRotationSnap(spawnYaw), 0f);
            bool groundCategory = GetPlacementSurface(GetSelectedDefinition()) == PlacementSurface.Ground;
            // Experimental: tilt ground units to the surface. Skipped for aircraft/ships and map spawn.
            if (alignToSurface && groundCategory && !mapSpawnMode)
            {
                return Quaternion.FromToRotation(Vector3.up, lastSurfaceNormal) * yawRot;
            }
            return yawRot;
        }

        /// <summary>
        /// Default ghost/placement rotation for Live Ordnance. Targeted ordnance replaces
        /// this with its resolved launch geometry in UpdateGhostAt.
        /// </summary>
        private Quaternion GetGhostPlacementRotation()
        {
            return GetSelectedDefinition() is MissileDefinition
                ? Quaternion.LookRotation(Vector3.down, Vector3.forward)
                : GetPlacementRotation();
        }

        private Unit GetSelectedOrdnanceTarget()
        {
            if (worldSelection == null) return null;
            worldSelection.Purge();
            if (worldSelection.Count != 1) return null;
            Unit target = worldSelection.Units[0];
            return target != null && target.gameObject != null && !target.disabled &&
                target.unitState != Unit.UnitState.Destroyed ? target : null;
        }

        private static bool SupportsNativeOrdnanceTracking(MissileDefinition definition)
        {
            return definition != null && definition.unitPrefab != null &&
                definition.unitPrefab.GetComponentInChildren<MissileSeeker>(true) != null;
        }

        private static Vector3 GetUnitVelocity(Unit unit)
        {
            if (unit == null) return Vector3.zero;
            Rigidbody body = unit.GetComponent<Rigidbody>();
            if (body == null) body = unit.GetComponentInChildren<Rigidbody>();
            return body != null ? body.velocity : Vector3.zero;
        }

        private static float GetTargetClearance(Unit target)
        {
            if (target == null) return 20f;
            float top = target.transform.position.y;
            Collider[] colliders = target.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                top = Mathf.Max(top, collider.bounds.max.y);
            }
            return Mathf.Clamp(top - target.transform.position.y + 10f, 20f, 120f);
        }

        /// <summary>
        /// Resolves a live weapon's spawn pose without parenting it to the target. Track mode
        /// launches from the clicked point and hands a real Unit name to the native seeker.
        /// Impact mode spawns above a linearly predicted target position and preserves native
        /// weapon physics/fuzing; unguided bombs receive lead but do not become homing weapons.
        /// </summary>
        private bool TryResolveOrdnanceLaunch(
            MissileDefinition definition,
            Vector3 worldPoint,
            out Vector3 launchPosition,
            out Quaternion launchRotation,
            out string targetUnitName,
            out string error)
        {
            launchPosition = worldPoint;
            launchRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            targetUnitName = null;
            error = null;

            if (ordnanceTargetMode == HorusOrdnanceTargetMode.WorldPoint) return true;

            Unit target = GetSelectedOrdnanceTarget();
            if (target == null)
            {
                error = "Select exactly one active target unit, or choose World Point.";
                return false;
            }

            bool hasNativeSeeker = SupportsNativeOrdnanceTracking(definition);
            if (ordnanceTargetMode == HorusOrdnanceTargetMode.TrackSelected)
            {
                if (!hasNativeSeeker)
                {
                    error = "This weapon has no native seeker. Use Impact Selected for bombs and unguided ordnance.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(target.UniqueName))
                {
                    error = "The selected unit has no network target name, so native tracking cannot lock it.";
                    return false;
                }

                float speed = Mathf.Max(1f, missileLaunchSpeed);
                Vector3 targetPosition = target.transform.position;
                float leadTime = Mathf.Clamp(Vector3.Distance(worldPoint, targetPosition) / speed, 0f, 8f);
                Vector3 aimPoint = targetPosition + GetUnitVelocity(target) * leadTime;
                Vector3 direction = aimPoint - worldPoint;
                if (direction.sqrMagnitude < 0.01f) direction = Vector3.down;
                Vector3 normalizedDirection = direction.normalized;
                Vector3 upReference = Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.up)) > 0.98f
                    ? Vector3.forward
                    : Vector3.up;
                launchRotation = Quaternion.LookRotation(normalizedDirection, upReference);
                targetUnitName = target.UniqueName;
                return true;
            }

            float height = Mathf.Max(ordnanceImpactHeight, GetTargetClearance(target));
            float fallTime = (float)HorusOrdnanceTargetPolicy.EstimateFallTime(height, missileLaunchSpeed);
            Vector3 predictedTarget = target.transform.position + GetUnitVelocity(target) * Mathf.Clamp(fallTime, 0f, 5f);
            launchPosition = predictedTarget + Vector3.up * height;
            launchRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            // A guided weapon may keep correcting after the initial lead. Bombs/rockets have
            // no seeker and intentionally remain ballistic after the target-relative spawn.
            if (hasNativeSeeker && !string.IsNullOrWhiteSpace(target.UniqueName))
                targetUnitName = target.UniqueName;
            return true;
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
        internal Vector3 GetFinalPlacementPosition(Vector3 rawPosition, UnitDefinition def = null, int cat = -1, bool applySpacing = true, bool logDiagnostics = false)
        {
            if (def == null) def = GetSelectedDefinition();
            if (cat < 0) cat = selectedCategoryIndex;
            PlacementOptions options = CapturePlacementOptions(def);
            CatalogEntry catalogEntry = FindCatalogEntry(def);
            PlacementSurface surface = catalogEntry != null ? catalogEntry.PlacementSurface : GetPlacementSurface(def);

            Vector3 pos = ApplyGridSnap(rawPosition);

            bool isShip = (catalogEntry != null && catalogEntry.SpawnKind == SpawnKind.Ship) || IsShipDefinition(def);
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

            if (surface == PlacementSurface.Sea)
            {
                pos.y = GetOceanLevel() + options.Altitude + (def != null ? def.spawnOffset.y : 0f);
                return pos;
            }
            
            bool useOceanSnap = oceanSnapActive || (autoOceanSnapForShips && cat == 2);
            float preSnapY = pos.y;
            bool groundCategoryForLog = false;
            bool groundSampleHitForLog = false;
            float groundSampleYForLog = 0f;
            if (useOceanSnap)
            {
                pos.y = GetOceanLevel() + options.Altitude;
            }
            else
            {
                pos.y += options.Altitude;
                if (logDiagnostics)
                {
                    groundCategoryForLog = GetPlacementSurface(def) == PlacementSurface.Ground;
                    groundSampleHitForLog = TrySampleGroundHeight(pos, out groundSampleYForLog);
                }
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

            if (logDiagnostics)
            {
                HorusLog.Info("Placement",
                    $"'{def?.jsonKey}': raw=({rawPosition.x:F1},{rawPosition.y:F1},{rawPosition.z:F1}) " +
                    $"surface={surface} groundCategory={groundCategoryForLog} snapToGround={snapToGround} " +
                    $"preSnapY={preSnapY:F1} groundSample(hit={groundSampleHitForLog},y={groundSampleYForLog:F1}) " +
                    $"altitude={options.Altitude:F1} spawnOffsetY={(def != null ? def.spawnOffset.y : 0f):F1} " +
                    $"final=({pos.x:F1},{pos.y:F1},{pos.z:F1}).");
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

        private AircraftSpawnOptions BuildAircraftSpawnOptions(AircraftDefinition definition, FactionHQ hq, PlacementOptions options, bool applyRequestedCustomization)
        {
            AircraftParameters parameters = definition != null ? definition.aircraftParameters : null;
            var result = new AircraftSpawnOptions
            {
                FuelRatio = parameters != null ? parameters.DefaultFuelLevel : 1f,
                Skill = options != null ? options.Skill : 0.5f
            };
            result.Bravery = Mathf.Clamp01(result.Skill * 0.75f + 0.2f);

            if (parameters == null) return result;

            LoadoutApplyResult resolved;
            LoadoutSourceKind source = applyRequestedCustomization && options != null
                ? options.AircraftLoadoutSource
                : LoadoutSourceKind.Default;
            if (source == LoadoutSourceKind.RandomStandardPreset)
                resolved = HorusLoadoutService.ResolveRandomStandardForSpawn(definition, hq);
            else if (applyRequestedCustomization && options?.AircraftLoadoutDraft != null)
                resolved = HorusLoadoutService.ResolveForSpawn(definition, hq, options.AircraftLoadoutDraft);
            else
                resolved = HorusLoadoutService.ResolveDefaultForSpawn(definition, hq);

            if (!resolved.Success && source != LoadoutSourceKind.Default)
            {
                string rejectionSummary = SummarizeLoadoutIssues(resolved);
                HorusLog.Warning("Loadouts", $"Requested loadout for '{definition.unitName}' was invalid: {resolved.Message}. Falling back to default.");
                HorusToasts.Show($"'{definition.unitName}' spawned with the DEFAULT loadout: your configured one was rejected — {rejectionSummary}");
                resolved = HorusLoadoutService.ResolveDefaultForSpawn(definition, hq);
            }
            if (!resolved.Success)
            {
                LoadoutApplyResult validStandard = HorusLoadoutService.ResolveRandomStandardForSpawn(definition, hq);
                if (validStandard.Success) resolved = validStandard;
            }
            if (resolved.Success)
            {
                result.Loadout = resolved.ResolvedLoadout;
                result.FuelRatio = resolved.FuelRatio;
            }
            else
            {
                HorusLog.Warning("Loadouts", $"No valid pre-spawn loadout for '{definition.unitName}': {resolved.Message}");
                HorusToasts.Show($"'{definition.unitName}' has no valid loadout to spawn with: {SummarizeLoadoutIssues(resolved)}");
            }

            if (applyRequestedCustomization && options != null)
            {

                AircraftLiveryMode liveryMode = (AircraftLiveryMode)options.AircraftLiveryMode;
                if (parameters.liveries != null && parameters.liveries.Count > 0)
                {
                    int liveryIndex = 0;
                    if (liveryMode == AircraftLiveryMode.FactionDefault)
                        liveryIndex = parameters.GetFirstLiveryForFaction(hq != null ? hq.faction : null);
                    else if (liveryMode == AircraftLiveryMode.Random)
                        liveryIndex = parameters.GetRandomLiveryForFaction(hq != null ? hq.faction : null);
                    else if (liveryMode == AircraftLiveryMode.Specific)
                        liveryIndex = Mathf.Clamp(options.SelectedLiveryIndex, 0, parameters.liveries.Count - 1);
                    result.Livery = new LiveryKey(liveryIndex);
                }
            }

            return result;
        }

        /// <summary>Short, user-facing summary of why a loadout was rejected (hardpoint conflicts, HQ restrictions, etc.).</summary>
        private static string SummarizeLoadoutIssues(LoadoutApplyResult resolved)
        {
            if (resolved?.Issues == null || resolved.Issues.Count == 0)
                return resolved?.Message ?? "unknown reason";
            var errors = new List<string>();
            foreach (LoadoutValidationIssue issue in resolved.Issues)
            {
                if (issue.Severity != LoadoutIssueSeverity.Error) continue;
                errors.Add(issue.ToString());
                if (errors.Count >= 3) break;
            }
            return errors.Count > 0 ? string.Join("; ", errors) : resolved.Message;
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
            CatalogEntry entry = FindCatalogEntry(def);
            if (entry != null)
            {
                switch (entry.SpawnKind)
                {
                    case SpawnKind.Aircraft: return 0;
                    case SpawnKind.Vehicle: return 1;
                    case SpawnKind.Ship: return 2;
                    case SpawnKind.Building: return 3;
                    case SpawnKind.Scenery: return 4;
                    case SpawnKind.Missile: return 5;
                    default: return 6;
                }
            }
            if (def is AircraftDefinition) return 0;
            if (def is VehicleDefinition) return 1;
            if (def is ShipDefinition) return 2;
            if (def is BuildingDefinition) return 3;
            if (def is SceneryDefinition) return 4;
            if (def is MissileDefinition) return 5;
            return 6;
        }

        internal static CatalogEntry FindCatalogEntry(UnitDefinition definition)
        {
            return UnitCatalog.FindByDefinition(definition);
        }

        private static PlacementSurface GetPlacementSurface(UnitDefinition definition)
        {
            CatalogEntry entry = FindCatalogEntry(definition);
            if (entry != null) return entry.PlacementSurface;
            if (definition is AircraftDefinition) return PlacementSurface.Air;
            if (definition is ShipDefinition) return PlacementSurface.Sea;
            if (definition is VehicleDefinition || definition is BuildingDefinition || definition is SceneryDefinition)
                return PlacementSurface.Ground;
            return PlacementSurface.Free;
        }

        private static string CatalogIdentity(CatalogEntry entry)
        {
            if (entry == null) return "";
            return !string.IsNullOrWhiteSpace(entry.Key) ? entry.Key : entry.JsonKey ?? "";
        }

        private bool CanAttemptCatalogSpawn(UnitDefinition definition, out string reason)
        {
            reason = null;
            CatalogEntry entry = FindCatalogEntry(definition);
            if (entry == null) return true;
            string key = CatalogIdentity(entry);

            if (entry.IsLookupOnly)
            {
                if (HorusPlugin.AllowIncompatibleContent == null || !HorusPlugin.AllowIncompatibleContent.Value)
                {
                    reason = "Enable 'Force incompatible content' in the warning panel first.";
                    return false;
                }
                if (!acknowledgedLookupDefinitions.ContainsKey(key))
                {
                    reason = "Acknowledge this Lookup-only definition for the current session first.";
                    return false;
                }
            }

            if (entry.IsLiveOrdnance)
            {
                if (enableGroupSpawn)
                {
                    reason = "Live ordnance is restricted to individual Sandbox spawns.";
                    return false;
                }
                if (economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander)
                {
                    reason = "Live ordnance is excluded from RTS mode.";
                    return false;
                }
            }

            return true;
        }

        internal bool TryAuthorizeSpawnRequest(HorusSpawnRequest request, bool allowLiveOrdnance, out string reason)
        {
            reason = null;
            if (request == null || request.Definition == null)
            {
                reason = "Spawn request has no definition.";
                return false;
            }

            CatalogEntry entry = FindCatalogEntry(request.Definition);
            request.Surface = entry?.PlacementSurface ?? GetPlacementSurface(request.Definition);
            if (entry == null) return true;
            string key = CatalogIdentity(entry);

            if (entry.IsLookupOnly)
            {
                if (HorusPlugin.AllowIncompatibleContent == null || !HorusPlugin.AllowIncompatibleContent.Value ||
                    !acknowledgedLookupDefinitions.ContainsKey(key))
                {
                    reason = "Lookup-only content has not been acknowledged for this session.";
                    return false;
                }
                request.IncompatibleContentAcknowledgementKey = key;
            }

            // Missiles stay individual-only: they are excluded from group, factory,
            // duplicate and undo routes, but no longer require a launch confirmation.
            if (entry.IsLiveOrdnance && !allowLiveOrdnance)
            {
                reason = "Live ordnance is individual-only and cannot use this spawn route.";
                return false;
            }
            return true;
        }

        private static CatalogEntry FindNavalResupplyCandidate()
        {
            CatalogEntry fallback = null;
            for (int i = 0; i < UnitCatalog.CatalogEntries.Count; i++)
            {
                CatalogEntry entry = UnitCatalog.CatalogEntries[i];
                if (entry?.Supply == null || entry.Supply.CanResupplyShips != CapabilityState.Yes) continue;
                if (entry.Def == null || entry.Def.unitPrefab == null) continue;
                string identity = (entry.JsonKey + " " + entry.Display).ToLowerInvariant();
                if (identity.Contains("navalsupplycontainer1")) return entry;
                if (fallback == null || identity.Contains("navalpallet1")) fallback = entry;
            }
            return fallback;
        }

        internal bool SpawnNavalResupplyQuick()
        {
            if (!HorusPermissions.CanRequestMutation()) return false;
            UnitCatalog.EnsureBuilt(MissionManager.AllowEventContent);
            CatalogEntry entry = FindNavalResupplyCandidate();
            if (entry == null)
            {
                HorusToasts.Show("No component-compatible naval Rearmer definition was found");
                return false;
            }
            if (!CanAttemptCatalogSpawn(entry.Def, out string denial))
            {
                HorusToasts.Show("Naval resupply blocked: " + denial);
                return false;
            }

            Ship selectedShip = null;
            if (worldSelection != null)
            {
                for (int i = 0; i < worldSelection.Units.Count; i++)
                    if (worldSelection.Units[i] is Ship ship) { selectedShip = ship; break; }
            }

            FactionHQ hq = selectedShip != null ? selectedShip.NetworkHQ : GetHQSafe(selectedFactionIndex);
            if (hq == null)
            {
                HorusToasts.Show("Naval resupply requires a playable faction/HQ; Neutral is not supported");
                return false;
            }

            Vector3 localPosition;
            Quaternion rotation;
            if (selectedShip != null)
            {
                float range = entry.Supply.RearmRange ?? 200f;
                float offset = range > 0f
                    ? Mathf.Clamp(range * 0.35f, Mathf.Min(1f, range * 0.1f), Mathf.Max(1f, range * 0.8f))
                    : 25f;
                localPosition = selectedShip.transform.position + selectedShip.transform.right * offset;
                rotation = Quaternion.Euler(0f, selectedShip.transform.eulerAngles.y, 0f);
            }
            else if (inputRouter != null && inputRouter.Pick.Valid)
            {
                localPosition = inputRouter.Pick.Point;
                rotation = Quaternion.Euler(0f, spawnYaw, 0f);
            }
            else
            {
                HorusToasts.Show("Aim at the water or select a ship first");
                return false;
            }
            localPosition.y = GetOceanLevel() + entry.Def.spawnOffset.y;

            var request = new HorusSpawnRequest
            {
                Definition = entry.Def,
                Position = localPosition.ToGlobalPosition(),
                Rotation = rotation,
                HQ = hq,
                UniqueName = (entry.JsonKey ?? "naval_supply") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                // The remote codec converts this stable native name back into a persistent ID.
                // The dedicated server then validates the target is a Ship before requesting rearm.
                TargetUnitName = selectedShip != null ? selectedShip.UniqueName : null
            };
            if (!TryAuthorizeSpawnRequest(request, false, out string authorizationError))
            {
                HorusToasts.Show("Naval resupply blocked: " + authorizationError);
                return false;
            }
            HorusSpawnResult result = HorusSpawnService.Spawn(request);
            if (!result.Success)
            {
                HorusToasts.Show("Naval resupply failed: " + result.Message);
                return false;
            }

            if (result.IsRemotePending)
            {
                HorusPrefs.AddRecent(entry.JsonKey);
                lastSpawnResult = "Requested naval resupply: " + entry.Display;
                HorusToasts.Show(lastSpawnResult);
                return true;
            }

            AddHorusSpawnedUnit(result.Unit);
            HorusUndo.RecordSpawn(result.Unit);
            HorusPrefs.AddRecent(entry.JsonKey);
            if (selectedShip != null)
            {
                try { selectedShip.RequestRearm(); }
                catch (Exception ex) { HorusLog.Warning("Supply", "Could not request ship rearm: " + ex.Message); }
            }
            lastSpawnResult = "Spawned naval resupply: " + entry.Display;
            HorusToasts.Show(lastSpawnResult);
            return true;
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
            if (!HorusPermissions.CanRequestMutation())
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

            for (int i = 0; i < units.Count; i++)
            {
                CatalogEntry groupEntry = FindCatalogEntry(units[i]);
                if (groupEntry != null && groupEntry.IsLiveOrdnance)
                {
                    lastSpawnResult = "Blocked: live ordnance cannot be spawned in groups.";
                    HorusToasts.Show(lastSpawnResult);
                    return;
                }
                if (!CanAttemptCatalogSpawn(units[i], out string groupDenial))
                {
                    lastSpawnResult = "Blocked: " + groupDenial;
                    HorusToasts.Show(lastSpawnResult);
                    return;
                }
            }

            // RTS transaction validation
            RtsTransaction tx = null;
            if (economyManager != null && economyManager.CurrentMode == HorusMode.RtsCommander)
            {
                if (HorusPlugin.RequireDeploymentConfirmation.Value && !IsSameArmedGroup(units))
                {
                    economyManager.ArmGroupDeployment(units, selectedFactionIndex);
                    SceneSingleton<GameplayUI>.i?.GameMessage($"Horus: Group x{units.Count} armed. Click again to deploy.");
                    return;
                }
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
            HorusLog.Verbose("Spawn", $"Spawning group of {units.Count} unit(s).");

            var spawnedUnits = new List<Unit>();
            int remoteRequested = 0;

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

#if HORUS_CLIENT
                if (HorusRemoteAuthority.IsRemoteSession)
                {
                    var remoteRequest = new HorusSpawnRequest
                    {
                        Definition = def,
                        Position = globalPos,
                        Rotation = rot,
                        HQ = hq,
                        UniqueName = (def.jsonKey ?? "unit") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Stationary = spawnStationary
                    };
                    if (def is AircraftDefinition remoteAircraftDefinition)
                    {
                        PlacementOptions remoteOptions = CapturePlacementOptions(def);
                        remoteRequest.Aircraft = BuildAircraftSpawnOptions(remoteAircraftDefinition, hq, remoteOptions,
                            remoteOptions.ApplyAircraftToWholeGroup);
                    }
                    if (!TryAuthorizeSpawnRequest(remoteRequest, false, out string remoteAuthorizationError))
                    {
                        HorusLog.Warning("Spawn", $"Group member '{def.unitName}' blocked: {remoteAuthorizationError}");
                        continue;
                    }
                    HorusSpawnResult remoteResult = HorusSpawnService.Spawn(remoteRequest);
                    if (remoteResult.IsRemotePending) { remoteRequested++; HorusPrefs.AddRecent(def.jsonKey); }
                    continue;
                }
#endif
                if (IsShipDefinition(def) || unitCat == 2) // Ship
                {
                    float shipYaw = NormalizeAngle(rot.eulerAngles.y);
                    spawned = SpawnShipSafe(def, globalPos, shipYaw, selectedFactionIndex, spawnStationary);
                }
                else
                {
                    string uniqueName = (def.jsonKey ?? "unit") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                    var request = new HorusSpawnRequest
                    {
                        Definition = def,
                        Position = globalPos,
                        Rotation = rot,
                        HQ = hq,
                        UniqueName = uniqueName,
                        Stationary = spawnStationary
                    };
                    if (def is AircraftDefinition aircraftDefinition)
                    {
                        PlacementOptions aircraftOptions = CapturePlacementOptions(def);
                        request.Aircraft = BuildAircraftSpawnOptions(aircraftDefinition, hq, aircraftOptions,
                            aircraftOptions.ApplyAircraftToWholeGroup);
                    }
                    if (!TryAuthorizeSpawnRequest(request, false, out string authorizationError))
                    {
                        HorusLog.Warning("Spawn", $"Group member '{def.unitName}' blocked: {authorizationError}");
                        continue;
                    }
                    spawned = HorusSpawnService.Spawn(request).Unit;
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
            if (spawnedUnits.Count > 0)
            {
                lastPlacementConsumed = true;
                HorusToasts.Show($"Spawned group: {spawnedUnits.Count} unit(s)");
            }
            else if (remoteRequested > 0)
            {
                lastPlacementConsumed = true;
                lastSpawnResult = $"Requested group: {remoteRequested} unit(s).";
                HorusToasts.Show(lastSpawnResult);
            }
        }

        private bool IsSameArmedGroup(List<UnitDefinition> units)
        {
            List<UnitDefinition> armed = economyManager?.ArmedGroupDefinitions;
            if (armed == null || units == null || !economyManager.IsDeploymentArmed || armed.Count != units.Count)
                return false;
            for (int i = 0; i < units.Count; i++)
                if (!ReferenceEquals(armed[i], units[i])) return false;
            return true;
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
                    if (unit == null) continue;
                    CatalogEntry entry = FindCatalogEntry(unit);
                    if (unit is MissileDefinition || entry?.IsLiveOrdnance == true)
                    {
                        HorusLog.Warning("Core", $"Custom Group '{name}': live ordnance '{unit.unitName}' was not saved.");
                        continue;
                    }
                    data.unitNames.Add(!string.IsNullOrEmpty(unit.jsonKey) ? unit.jsonKey : unit.unitName);
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
                            CatalogEntry entry = FindCatalogEntry(found);
                            if (found is MissileDefinition || entry?.IsLiveOrdnance == true)
                            {
                                HorusLog.Warning("Core", $"Custom Group '{name}': live ordnance '{uname}' is individual-only. Skipping.");
                                continue;
                            }
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
            UnitCatalog.EnsureBuilt(MissionManager.AllowEventContent);
            UnitEntry exact = UnitCatalog.Find(name);
            if (exact != null) return exact.Def;
            for (int i = 0; i < UnitCatalog.Entries.Count; i++)
            {
                UnitEntry entry = UnitCatalog.Entries[i];
                if (entry?.Def != null && string.Equals(entry.Def.unitName, name, StringComparison.OrdinalIgnoreCase))
                    return entry.Def;
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
            if (!ghostPreviewEnabled || !HorusPermissions.CanRequestMutation())
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

                Vector3 ghostPosition = GetFinalPlacementPosition(pick.Point, applySpacing: false);
                Quaternion ghostRotation = GetGhostPlacementRotation();
                if (def is MissileDefinition missileDefinition &&
                    !TryResolveOrdnanceLaunch(missileDefinition, ghostPosition, out ghostPosition,
                        out ghostRotation, out _, out _))
                {
                    ghost.SetVisible(false);
                    return;
                }
                ghost.UpdateTransform(ghostPosition, ghostRotation);
                ghost.SetVisible(true);
            }
        }

        private void OnDestroy()
        {
            inputRouter?.Deactivate();
            worldOrders?.Reset();
            RestoreGameCursorState();
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
            if (!HorusPermissions.CanRequestMutation())
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
            if (!CanAttemptCatalogSpawn(def, out string catalogDenial))
            {
                lastSpawnResult = "Blocked: " + catalogDenial;
                HorusToasts.Show(lastSpawnResult);
                HorusLog.Warning("Spawn", lastSpawnResult);
                return;
            }
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

            CatalogEntry selectedCatalogEntry = FindCatalogEntry(def);
            if (hq == null && selectedCatalogEntry?.Supply != null &&
                (selectedCatalogEntry.Supply.HasRearmer || selectedCatalogEntry.Supply.HasRefueler))
            {
                lastSpawnResult = "Blocked: functional resupply requires a playable faction/HQ; Neutral cannot rearm units.";
                HorusToasts.Show(lastSpawnResult);
                return;
            }

            GlobalPosition globalPos = position.ToGlobalPosition();
            Quaternion rotation = GetPlacementRotation();

            Unit spawned = null;
            if (IsShipDefinition(def) || selectedCategoryIndex == 2) // Ship
            {
                float shipYaw = NormalizeAngle(rotation.eulerAngles.y);
                spawned = SpawnShipSafe(def, globalPos, shipYaw, selectedFactionIndex, spawnStationary);
            }
            else
            {
                string uniqueName = (def.jsonKey ?? "unit") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                var request = new HorusSpawnRequest
                {
                    Definition = def,
                    Position = globalPos,
                    Rotation = rotation,
                    HQ = hq,
                    UniqueName = uniqueName,
                    Stationary = spawnStationary
                };
                if (def is AircraftDefinition aircraftDefinition)
                    request.Aircraft = BuildAircraftSpawnOptions(aircraftDefinition, hq, placementOptions, true);
                if (def is MissileDefinition missileDefinition)
                {
                    if (!TryResolveOrdnanceLaunch(missileDefinition, position, out Vector3 launchPosition,
                        out Quaternion launchRotation, out string targetUnitName, out string targetingError))
                    {
                        lastSpawnResult = "Blocked: " + targetingError;
                        HorusToasts.Show(lastSpawnResult);
                        HorusLog.Warning("Spawn", lastSpawnResult);
                        return;
                    }
                    request.Position = launchPosition.ToGlobalPosition();
                    request.Rotation = launchRotation;
                    request.MissileLaunchSpeed = missileLaunchSpeed;
                    request.MissileLaunchElevation = 0f;
                    request.TargetUnitName = targetUnitName;
                    globalPos = request.Position;
                    HorusLog.Info("Spawn",
                        $"Live Ordnance mode={ordnanceTargetMode}, launch={request.Position}, target={(string.IsNullOrEmpty(targetUnitName) ? "none" : targetUnitName)}, impactHeight={ordnanceImpactHeight:F0}m.");
                }
                if (!TryAuthorizeSpawnRequest(request, selectedCatalogEntry?.IsLiveOrdnance == true,
                    out string authorizationError))
                {
                    lastSpawnResult = "Blocked: " + authorizationError;
                    HorusToasts.Show(lastSpawnResult);
                    return;
                }
                HorusSpawnResult spawnResult = HorusSpawnService.Spawn(request);
                spawned = spawnResult.Unit;
                if (!spawnResult.Success)
                {
                    lastSpawnResult = "Spawn failed: " + spawnResult.Message;
                    HorusToasts.Show(lastSpawnResult);
                }
                else if (spawnResult.IsRemotePending)
                {
                    HorusPrefs.AddRecent(def.jsonKey);
                    lastPlacementConsumed = true;
                    lastSpawnResult = $"Requested {def.unitName}.";
                    HorusToasts.Show(lastSpawnResult);
                    return;
                }
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
                    // Same diagnostic for every definition type that hit the Rigidbody-desync
                    // bug (missile, pilot, container) so a regression on any of them shows up
                    // in the log the same way instead of needing another repro/report cycle.
                    bool isRigidbodyProneSpawn = def is MissileDefinition ||
                        def.unitPrefab.GetComponent<PilotDismounted>() != null ||
                        def.unitPrefab.GetComponent<Container>() != null;
                    if (isRigidbodyProneSpawn)
                        StartCoroutine(TrackSpawnTrajectory(spawned, globalPos));

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

        /// <summary>
        /// Diagnostic-only: logs a spawned unit's actual position every 0.25s for 5s after
        /// spawn, so a post-spawn snap/drift away from the intended spawn point (Rigidbody
        /// desync, native flight/guidance behavior, etc.) is directly visible in the log
        /// instead of inferred. Used for definition types previously found susceptible to the
        /// native Rigidbody-position-desync bug: missiles, pilots, containers.
        /// </summary>
        private System.Collections.IEnumerator TrackSpawnTrajectory(Unit unit, GlobalPosition intendedPosition)
        {
            string label = unit != null ? unit.unitName : "?";
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForSeconds(0.25f);
                float elapsed = (i + 1) * 0.25f;
                if (unit == null || unit.gameObject == null || unit.disabled)
                {
                    HorusLog.Info("Placement", $"Trajectory '{label}': gone/disabled at t={elapsed:F2}s.");
                    yield break;
                }
                GlobalPosition current = unit.GlobalPosition();
                Vector3 delta = current.AsVector3() - intendedPosition.AsVector3();
                float horizDrift = new Vector2(delta.x, delta.z).magnitude;
                HorusLog.Info("Placement",
                    $"Trajectory '{label}' t={elapsed:F2}s: pos=({current.x:F1},{current.y:F1},{current.z:F1}) " +
                    $"deltaFromSpawn=({delta.x:F1},{delta.y:F1},{delta.z:F1}) horizDrift={horizDrift:F1}.");
            }
        }

        internal Unit SpawnShipSafe(UnitDefinition def, GlobalPosition globalPos, float yaw, int faction,
            bool stationary = false)
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
            var request = new HorusSpawnRequest
            {
                Definition = def,
                Position = spawnGlobalPos,
                Rotation = rot,
                HQ = hq,
                UniqueName = uniqueName,
                Stationary = stationary
            };
            if (!TryAuthorizeSpawnRequest(request, false, out string authorizationError))
            {
                HorusLog.Warning("Spawn", $"SpawnShip blocked: {authorizationError}");
                HorusToasts.Show("Ship spawn blocked: " + authorizationError);
                return null;
            }
            HorusSpawnResult spawnResult = HorusSpawnService.Spawn(request);
            spawned = spawnResult.Unit;
            if (!spawnResult.Success)
                HorusLog.Warning("Spawn", $"SpawnShip failed: {spawnResult.Message}");
            if (spawnResult.IsRemotePending)
            {
                lastPlacementConsumed = true;
                lastSpawnResult = $"Requested {def.unitName}.";
                HorusToasts.Show(lastSpawnResult);
                return null;
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
                if (stationary && spawned is Ship ship)
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
            CatalogEntry entry = FindCatalogEntry(def);
            if (entry != null) return entry.SpawnKind == SpawnKind.Ship;
            return def is ShipDefinition || (def.unitPrefab != null && def.unitPrefab.GetComponent<Ship>() != null);
        }

    }
}
