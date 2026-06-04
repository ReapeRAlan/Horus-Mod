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
        private Rect windowRect = new Rect(20, 20, 340, 600);
        
        private int selectedFactionIndex = 0;
        private int selectedCategoryIndex = 0;
        private int selectedUnitIndex = 0;
        
        private float spawnAltitude = 0f;
        private string altitudeInputText = "0";
        private bool hideGUI = false;
        private bool isMouseOverGUI = false;

        private Vector2 scrollPosition;

        private float rotationX = 0f;
        private float rotationY = 0f;

        // Cached deduplicated unit lists to avoid rebuilding every frame
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

            if (horusActive)
            {
                if (Input.GetKeyDown(HorusPlugin.HotkeyToggleUI.Value))
                {
                    hideGUI = !hideGUI;
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
                    // Still allow camera rotation with right-click even over UI
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
            HorusPlugin.Logger.LogInfo($"Horus Mode toggled: {horusActive}");
            
            if (horusActive && CameraStateManager.i != null)
            {
                CameraStateManager.i.SwitchState(CameraStateManager.i.freeState);
                CameraStateManager.i.SetFollowingUnit(null);
                rotationX = CameraStateManager.i.transform.eulerAngles.y;
                rotationY = CameraStateManager.i.transform.eulerAngles.x;
            }

            // Invalidate cached list when toggling mode
            cachedCategoryIndex = -1;
        }

        private void OnGUI()
        {
            if (!horusActive || hideGUI) return;

            // Consume scroll wheel events when mouse is over the GUI window
            // This prevents the game from eating them before IMGUI can use them
            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            isMouseOverGUI = windowRect.Contains(mouseScreenPos);

            if (isMouseOverGUI)
            {
                // Eat the scroll wheel event so the game doesn't process it
                Input.ResetInputAxes();
            }

            windowRect = GUI.Window(999, windowRect, DrawHorusWindow, $"⚡ Horus Editor ({HorusPlugin.HotkeyToggleMode.Value} to exit, {HorusPlugin.HotkeyToggleUI.Value} to hide UI)");
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

            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0)
            {
                GUILayout.Label("Status: No playable factions found. Spawning will not work.");
            }
            else
            {
                if (selectedFactionIndex >= factions.Count) selectedFactionIndex = 0;

                GUILayout.Label("Select Faction:");
                string[] factionNames = factions.Select(f => f.factionName).ToArray();
                selectedFactionIndex = GUILayout.SelectionGrid(selectedFactionIndex, factionNames, 2);
            }

            GUILayout.Space(10);
            GUILayout.Label("Category:");
            string[] categories = { "Aircraft", "Vehicles", "Ships", "Buildings", "Scenery" };
            int oldCat = selectedCategoryIndex;
            selectedCategoryIndex = GUILayout.SelectionGrid(selectedCategoryIndex, categories, 3);
            if (oldCat != selectedCategoryIndex) 
            {
                selectedUnitIndex = 0;
                cachedCategoryIndex = -1; // Invalidate cache on category change
                if (selectedCategoryIndex == 0) spawnAltitude = 3000f;
                else spawnAltitude = 0f;
                altitudeInputText = spawnAltitude.ToString("0");
            }

            List<UnitDefinition> currentList = GetCurrentList();

            GUILayout.Space(10);
            GUILayout.Label($"Unit to Spawn: ({currentList.Count} units)");
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            
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
            GUILayout.Space(10);
            GUILayout.Label($"Spawn Altitude: {spawnAltitude:F0} m");
            
            // Slider
            float newAltitude = GUILayout.HorizontalSlider(spawnAltitude, 0f, 15000f);
            if (Mathf.Abs(newAltitude - spawnAltitude) > 0.01f)
            {
                spawnAltitude = Mathf.Round(newAltitude / 50f) * 50f;
                altitudeInputText = spawnAltitude.ToString("0");
            }

            // Custom input field
            GUILayout.BeginHorizontal();
            GUILayout.Label("Custom:", GUILayout.Width(55));
            altitudeInputText = GUILayout.TextField(altitudeInputText, GUILayout.Width(100));
            if (GUILayout.Button("Set", GUILayout.Width(45)))
            {
                if (float.TryParse(altitudeInputText, out float parsed))
                {
                    spawnAltitude = Mathf.Clamp(parsed, 0f, 50000f);
                    altitudeInputText = spawnAltitude.ToString("0");
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

            GUILayout.Space(10);
            GUILayout.Label("Controls:");
            GUILayout.Label("- Left Click: Spawn unit");
            GUILayout.Label("- Middle Click: Delete unit (Network safe)");
            GUILayout.Label("- Right Click (Hold): Rotate camera");
            GUILayout.Label("- WASD / Q E: Move camera. SHIFT to boost");

            GUI.DragWindow();
        }

        /// <summary>
        /// Returns a deduplicated list of units for the current category.
        /// Uses a cache to avoid rebuilding the list every OnGUI frame.
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

            // Deduplicate: keep first occurrence of each unit name, skip unnamed/empty
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

            // Sort alphabetically for easier browsing
            deduped.Sort((a, b) => string.Compare(a.unitName, b.unitName, StringComparison.OrdinalIgnoreCase));

            cachedUnitList = deduped;
            cachedCategoryIndex = selectedCategoryIndex;
            return cachedUnitList;
        }

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

        private void HandleDeleteClick()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            
            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                // In single player, we can just destroy the object.
                // In multiplayer, we check if we are the server.
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
            // Bug Fix: Check if we're in multiplayer and NOT a server. If so, block.
            // If we're in single player, allow it.
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
            Quaternion rotation = Quaternion.identity;
            
            Spawner.i.SpawnFromUnitDefinitionInEditor(def, globalPos, rotation, hq, "");
            HorusPlugin.Logger.LogInfo($"HorusMod: Spawned {def.unitName} at {globalPos}");
        }
    }
}
