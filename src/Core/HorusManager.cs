using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.Networking;
using Mirage;
using HorusMod.Networking;
using HorusMod.Placement;

namespace HorusMod.Core
{
    public class HorusManager : MonoBehaviour
    {
        public static HorusManager Instance { get; private set; }
        public bool IsHorusActive => horusActive;

        private bool horusActive = false;
        private Rect windowRect = new Rect(20, 20, 340, 700);
        
        private int selectedFactionIndex = 0;
        private int selectedCategoryIndex = 0;
        private int selectedUnitIndex = 0;
        
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
        private Vector2 mainScroll;

        private Vector2 scrollPosition;

        private float rotationX = 0f;
        private float rotationY = 0f;

        // Cached deduplicated unit lists
        private int cachedCategoryIndex = -1;
        private List<UnitDefinition> cachedUnitList;

        private void Awake()
        {
            Instance = this;
            ghostPreviewEnabled = HorusPlugin.EnableGhostPreview.Value;
            HorusPlugin.Logger.LogInfo("HorusManager created.");
        }

        private void Update()
        {
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
            }
            HorusPlugin.Logger.LogInfo($"Horus Mode toggled: {horusActive}");
            
            if (horusActive && CameraStateManager.i != null)
            {
                CameraStateManager.i.SwitchState(CameraStateManager.i.freeState);
                CameraStateManager.i.SetFollowingUnit(null);
                rotationX = CameraStateManager.i.transform.eulerAngles.y;
                rotationY = CameraStateManager.i.transform.eulerAngles.x;
            }

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
                selectedUnitIndex = GUILayout.SelectionGrid(selectedUnitIndex, unitNames, 1);
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

        /// <summary>
        /// Unified placement pipeline: grid-snap XZ, add altitude, then ground-snap for ground
        /// categories. Used by both the ghost preview and the real spawn so they always match.
        /// </summary>
        private Vector3 GetFinalPlacementPosition(Vector3 rawPosition)
        {
            Vector3 pos = ApplyGridSnap(rawPosition);
            pos.y += spawnAltitude;
            pos = ApplyGroundSnap(pos, GetSelectedDefinition(), selectedCategoryIndex);
            if (selectedCategoryIndex == 1 && spawnAltitude == 0f) pos.y += 2f; // small vehicle clearance
            return pos;
        }

        // --- Placement sources ---

        private bool TryGet3DPlacement(out Vector3 position)
        {
            position = Vector3.zero;
            Camera cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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

            UnitDefinition def = GetSelectedDefinition();
            if (def == null)
            {
                if (ghost.IsBuilt) ghost.Clear();
                return;
            }

            if (ghost.BuiltDefinition != def)
            {
                // Don't retry a unit we already failed to build (e.g. no prefab) every frame.
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

        private void OnDestroy()
        {
            ghost.Dispose();
        }

        // --- Spawn via Raycast (free camera) ---
        private void HandleSpawnClick()
        {
            if (TryGet3DPlacement(out Vector3 rawPos))
            {
                SpawnSelectedUnit(GetFinalPlacementPosition(rawPos));
            }
        }

        // --- Spawn via Map Click ---
        private void HandleMapSpawnClick()
        {
            try
            {
                if (!TryGetMapPlacement(out Vector3 rawPos)) return;
                Vector3 finalPos = GetFinalPlacementPosition(rawPos);
                HorusPlugin.Logger.LogInfo($"Map spawn at local {finalPos}");
                SpawnSelectedUnit(finalPos);
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
        private bool TrySampleGroundHeight(Vector3 localPos, out float groundY)
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
            
            if (!TryGet3DPlacement(out var localPos)) return;
            var globalPos = localPos.ToGlobalPosition();
            DeleteNearestUnit(globalPos);
        }
        
        /// <summary>Destroys a validated unit in a network-safe way and untracks it.</summary>
        private void DeleteNearestUnit(GlobalPosition pos)
        {
            UnitRegistry.TryGetNearestUnit(pos, out var go, 100f);
            string unitName = go.unitName;
            
            if (!IsSafeDeleteTarget(go.gameObject))
            {
                string reason = IsBuiltinMapUnit(go)
                    ? "original map unit is protected"
                    : "not spawned by Horus (enable Safety/AllowDeletingNonHorusUnits to remove other units)";
                HorusPlugin.Logger.LogInfo($"Horus: target is not deletable ({reason}): '{go.unitName}'.");
                return;
            }
            Destroy(go);
            HorusPlugin.Logger.LogInfo($"Deleted unit {unitName}");
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
            if (HorusPlugin.AllowDeletingNonHorusUnits.Value
                && HasGameplayUnitComponent(target)
                && !IsBuiltinMapUnit(FindUnitRoot(target)))
            {
                return true;
            }
            return false;
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

            Faction faction = factions[selectedFactionIndex];
            FactionHQ hq = FactionRegistry.HQFromFaction(faction);

            GlobalPosition globalPos = position.ToGlobalPosition();
            Quaternion rotation = GetPlacementRotation();

            Unit spawned = Spawner.i.SpawnFromUnitDefinitionInEditor(def, globalPos, rotation, hq, "");
            if (spawned != null)
            {
                horusSpawnedUnits.Add(spawned);
            }
            HorusPlugin.Logger.LogInfo($"HorusMod: Spawned {def.unitName} at {globalPos} yaw={spawnYaw:F0}° (tracked={spawned != null})");
        }
    }
}
