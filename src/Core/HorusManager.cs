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
        private Rect windowRect = new Rect(20, 20, 320, 550);
        
        private int selectedFactionIndex = 0;
        private int selectedCategoryIndex = 0;
        private int selectedUnitIndex = 0;
        
        private float spawnAltitude = 0f;
        private bool hideGUI = false;

        private Vector2 scrollPosition;

        private float rotationX = 0f;
        private float rotationY = 0f;

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

                ManageCameraAndInput();

                if (Input.GetMouseButtonDown(0))
                {
                    Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                    if (hideGUI || !windowRect.Contains(mousePos))
                    {
                        HandleSpawnClick();
                    }
                }

                if (Input.GetMouseButtonDown(2))
                {
                    HandleDeleteClick();
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
        }

        private void OnGUI()
        {
            if (!horusActive || hideGUI) return;
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
                if (selectedCategoryIndex == 0) spawnAltitude = 3000f;
                else spawnAltitude = 0f;
            }

            List<UnitDefinition> currentList = GetCurrentList();

            GUILayout.Space(10);
            GUILayout.Label("Unit to Spawn:");
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(180));
            
            if (currentList != null && currentList.Count > 0)
            {
                string[] unitNames = currentList.Select(u => u.unitName).ToArray();
                selectedUnitIndex = GUILayout.SelectionGrid(selectedUnitIndex, unitNames, 1);
            }
            else
            {
                GUILayout.Label("No units in this category.");
            }
            
            GUILayout.EndScrollView();

            GUILayout.Space(10);
            GUILayout.Label($"Spawn Altitude Offset: {spawnAltitude} m");
            spawnAltitude = GUILayout.HorizontalSlider(spawnAltitude, 0f, 15000f);
            spawnAltitude = Mathf.Round(spawnAltitude / 100f) * 100f;

            if (GUILayout.Button("Reset Altitude to Ground (0m)"))
            {
                spawnAltitude = 0f;
            }

            GUILayout.Space(10);
            GUILayout.Label("Controls:");
            GUILayout.Label("- Left Click: Spawn unit");
            GUILayout.Label("- Middle Click: Delete unit (Network safe)");
            GUILayout.Label("- Right Click (Hold): Rotate camera");
            GUILayout.Label("- WASD / Q E: Move camera. SHIFT to boost");

            GUI.DragWindow();
        }

        private List<UnitDefinition> GetCurrentList()
        {
            switch (selectedCategoryIndex)
            {
                case 0: return Encyclopedia.i.aircraft.Cast<UnitDefinition>().ToList();
                case 1: return Encyclopedia.i.vehicles.Cast<UnitDefinition>().ToList();
                case 2: return Encyclopedia.i.ships.Cast<UnitDefinition>().ToList();
                case 3: return Encyclopedia.i.buildings.Cast<UnitDefinition>().ToList();
                case 4: return Encyclopedia.i.scenery.Cast<UnitDefinition>().ToList();
                default: return new List<UnitDefinition>();
            }
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
