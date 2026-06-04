using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.Networking;
using Mirage;

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
        private bool snapToGround = true;

        private Vector2 scrollPosition;

        private float rotationX = 0f;
        private float rotationY = 0f;

        // Cached deduplicated unit lists
        private int cachedCategoryIndex = -1;
        private List<UnitDefinition> cachedUnitList;

        private void Awake()
        {
            Instance = this;
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

            if (Input.GetKeyDown(HorusPlugin.HotkeyToggleUI.Value))
            {
                hideGUI = !hideGUI;
            }

            // Handle scroll wheel modifiers for altitude and rotation
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f && !isMouseOverGUI)
            {
                float multiplier = Input.GetKey(KeyCode.LeftShift) ? 5f : 1f;

                if (Input.GetKey(KeyCode.LeftControl))
                {
                    // Ctrl + Scroll = altitude
                    spawnAltitude += scroll * HorusPlugin.AltitudeStep.Value * multiplier * 10f;
                    spawnAltitude = Mathf.Clamp(spawnAltitude, 0f, 50000f);
                    spawnAltitude = Mathf.Round(spawnAltitude);
                    altitudeInputText = spawnAltitude.ToString("0");
                }
                else if (Input.GetKey(KeyCode.LeftAlt))
                {
                    // Alt + Scroll = yaw rotation
                    spawnYaw += scroll * HorusPlugin.RotationStep.Value * multiplier * 10f;
                    spawnYaw = spawnYaw % 360f;
                    if (spawnYaw < 0f) spawnYaw += 360f;
                    yawInputText = spawnYaw.ToString("0");
                }
            }

            // Map spawn mode: when map is open and Horus is active, left click spawns at map cursor
            if (mapSpawnMode && DynamicMap.mapMaximized)
            {
                // Only spawn when clicking on the map itself, not over the Horus window
                if (Input.GetMouseButtonDown(0) && !isMouseOverGUI)
                {
                    HandleMapSpawnClick();
                }
                return; // Don't process camera/world input while map is open
            }

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

            if (isMouseOverGUI)
            {
                Input.ResetInputAxes();
            }

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
            if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)
            {
                GUILayout.Label("Status: Not in mission (Game State is " + GameManager.gameState + ")");
            }

            if (Encyclopedia.i == null)
            {
                GUILayout.Label("Error: Encyclopedia not loaded yet.");
                GUI.DragWindow();
                return;
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

            // --- Altitude Controls ---
            GUILayout.Space(5);
            GUILayout.Label($"Altitude: {spawnAltitude:F0} m  |  Yaw: {spawnYaw:F0}°");

            // Altitude slider
            float newAlt = GUILayout.HorizontalSlider(spawnAltitude, 0f, 15000f);
            if (Mathf.Abs(newAlt - spawnAltitude) > 0.01f)
            {
                spawnAltitude = Mathf.Round(newAlt / 50f) * 50f;
                altitudeInputText = spawnAltitude.ToString("0");
            }

            // Custom altitude input
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
                    spawnYaw = parsed % 360f;
                    if (spawnYaw < 0f) spawnYaw += 360f;
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
                spawnYaw = Mathf.Round(newYaw / 5f) * 5f;
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
            if (GUILayout.Button("Reset Rotation"))
            {
                spawnYaw = 0f;
                yawInputText = "0";
            }
            GUILayout.EndHorizontal();

            // --- Map Spawn Mode ---
            GUILayout.Space(5);
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
            snapToGround = GUILayout.Toggle(snapToGround, " Snap ground units to terrain");

            // --- Controls Legend ---
            GUILayout.Space(5);
            GUILayout.Label("Controls:");
            GUILayout.Label("Left Click: Spawn | Mid Click: Delete");
            GUILayout.Label("Ctrl+Scroll: Altitude | Alt+Scroll: Yaw");
            GUILayout.Label("Shift+Scroll: Faster | RMB: Camera");

            GUI.DragWindow();
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

        // --- Spawn via Raycast (free camera) ---
        private void HandleSpawnClick()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            
            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                Vector3 finalPos = hit.point + new Vector3(0, spawnAltitude, 0);
                if (spawnAltitude == 0f && selectedCategoryIndex == 1) finalPos.y += 2f; 
                SpawnSelectedUnit(finalPos);
            }
        }

        // --- Spawn via Map Click ---
        private void HandleMapSpawnClick()
        {
            try
            {
                if (SceneSingleton<DynamicMap>.i == null) return;
                if (!SceneSingleton<DynamicMap>.i.TryGetCursorCoordinates(out GlobalPosition mapPos)) return;

                // mapPos provides x/z from the map (global space); y is 0. Apply altitude.
                GlobalPosition spawnGlobalPos = new GlobalPosition(mapPos.x, spawnAltitude, mapPos.z);

                // Convert to a local Vector3 for spawning / ground sampling.
                Vector3 localPos = spawnGlobalPos.ToLocalPosition();

                // Snap ground-based units (vehicles, buildings, scenery) onto the terrain
                // so they are not buried at sea level when the cursor is over land.
                // Aircraft (0) and ships (2) keep their altitude / sea level.
                bool groundCategory = selectedCategoryIndex == 1 || selectedCategoryIndex == 3 || selectedCategoryIndex == 4;
                if (snapToGround && groundCategory && TrySampleGroundHeight(localPos, out float groundY))
                {
                    localPos.y = groundY + spawnAltitude;
                }

                HorusPlugin.Logger.LogInfo($"Map spawn at GlobalPos: {spawnGlobalPos}, LocalPos: {localPos}");
                SpawnSelectedUnit(localPos);
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
            Camera cam = Camera.main;
            if (cam == null) return;
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            
            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                bool isMultiplayer = GameManager.gameState == GameState.Multiplayer;
                bool isServer = Spawner.i != null && Spawner.i.IsServer;

                if (isMultiplayer && !isServer)
                {
                    HorusPlugin.Logger.LogWarning("HorusMod: Cannot delete units as a multiplayer client!");
                    return;
                }

                NetworkIdentity netId = hit.collider.GetComponentInParent<NetworkIdentity>();
                if (netId != null)
                {
                    if (isMultiplayer && isServer)
                    {
                        NetworkServer.Destroy(netId.gameObject);
                        HorusPlugin.Logger.LogInfo("HorusMod: Deleted networked unit " + netId.name);
                    }
                    else if (!isMultiplayer)
                    {
                        Destroy(netId.gameObject);
                        HorusPlugin.Logger.LogInfo("HorusMod: Deleted local networked unit " + netId.name);
                    }
                }
                else
                {
                    Destroy(hit.transform.root.gameObject);
                    HorusPlugin.Logger.LogInfo("HorusMod: Force deleted local root object.");
                }
            }
        }

        private void SpawnSelectedUnit(Vector3 position)
        {
            bool isMultiplayer = GameManager.gameState == GameState.Multiplayer;
            bool isServer = Spawner.i != null && Spawner.i.IsServer;

            if (isMultiplayer && !isServer)
            {
                HorusPlugin.Logger.LogWarning("HorusMod: Must be server to spawn units in multiplayer!");
                return;
            }

            if (Spawner.i == null)
            {
                HorusPlugin.Logger.LogError("HorusMod: Spawner.i is null. Cannot spawn unit.");
                return;
            }

            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0) return;

            var list = GetCurrentList();
            if (list == null || selectedUnitIndex < 0 || selectedUnitIndex >= list.Count) return;

            UnitDefinition def = list[selectedUnitIndex];
            Faction faction = factions[selectedFactionIndex];
            FactionHQ hq = FactionRegistry.HQFromFaction(faction);

            GlobalPosition globalPos = position.ToGlobalPosition();
            Quaternion rotation = Quaternion.Euler(0f, spawnYaw, 0f);
            
            Spawner.i.SpawnFromUnitDefinitionInEditor(def, globalPos, rotation, hq, "");
            HorusPlugin.Logger.LogInfo($"HorusMod: Spawned {def.unitName} at {globalPos} yaw={spawnYaw:F0}°");
        }
    }
}
