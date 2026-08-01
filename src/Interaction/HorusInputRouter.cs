using System.Collections.Generic;
using HorusMod.Core;
using HorusMod.Logging;
using HorusMod.Placement;
using HorusMod.UI;
using HorusMod.UI.ContextMenu;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HorusMod.Interaction
{
    public sealed class HorusInputRouter
    {
        private const float ClickMaxPixels = 6f;
        private const float RmbClickMaxSeconds = 0.25f;
        private const float DoubleClickSeconds = 0.32f;

        private readonly HorusManager owner;
        private readonly HorusSelection selection;
        private readonly HorusOrders orders;
        private readonly HorusOverlay overlay;

        private Vector2 rmbDownPos;
        private float rmbDownTime;
        private bool rmbLookEngaged;
        private bool rmbStartedOverGui;
        private bool rmbTracking;
        private Vector2 lmbDownPos;
        private Unit lmbDownUnit;
        private bool lmbTracking;
        private float lastClickTime;
        private UnitDefinition lastClickDefinition;

        public HorusTool Tool { get; private set; } = HorusTool.Select;
        public WorldPick Pick { get; private set; }
        public bool MarqueeActive { get; private set; }
        public Rect MarqueeRawScreen { get; private set; }
        public bool Looking => rmbLookEngaged;

        public HorusInputRouter(HorusManager owner, HorusSelection selection, HorusOrders orders, HorusOverlay overlay)
        {
            this.owner = owner;
            this.selection = selection;
            this.orders = orders;
            this.overlay = overlay;
        }

        public void SetTool(HorusTool tool)
        {
            Tool = tool;
            if (tool == HorusTool.Select) owner.HideGhost();
        }

        public void Update()
        {
            bool mapOpen = DynamicMap.mapMaximized;
            bool overHorusUi = owner.IsPointerOverHorusUI;
            bool overGameUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool overGui = overHorusUi || overGameUi;

            HandleEmergencyAndShortcuts(mapOpen);
            if (mapOpen)
            {
                CancelPointerCapture();
                // DynamicMap is itself a Unity UI surface, so EventSystem reports it as
                // "pointer over UI". Only Horus-owned chrome should block map commands.
                UpdateMap(overHorusUi);
                overlay.Update();
                return;
            }

            if (!overGui)
            {
                Pick = WorldPick.FromScreen(Input.mousePosition);
                selection.SetHover(Tool == HorusTool.Select ? Pick.Unit : null);
                if (Tool == HorusTool.Place) owner.UpdateGhostAt(Pick);
            }
            else
            {
                selection.SetHover(null);
            }

            UpdateRightMouse(overGui);
            if (rmbLookEngaged && !Input.GetMouseButton(1) && !Input.GetMouseButtonUp(1))
            {
                // Focus changes can swallow MouseUp. Never let a stale drag keep the
                // game cursor locked after the physical button has been released.
                CancelPointerCapture();
            }
            owner.UpdateFreeCamera(rmbLookEngaged);

            if (!overGui && !rmbLookEngaged)
            {
                UpdateLeftMouse();
                if (Input.GetMouseButtonDown(2)) DeleteUnderCursor();
            }

            overlay.Update();
        }

        public void Deactivate()
        {
            CancelPointerCapture();
            lmbTracking = false;
            MarqueeActive = false;
            HorusContextMenu.Close();
        }

        public void CancelPointerCapture()
        {
            rmbTracking = false;
            rmbLookEngaged = false;
            rmbStartedOverGui = false;
            owner.SetHorusCursorLock(false);
        }

        public void Reset()
        {
            Deactivate();
            selection.Clear();
            Tool = HorusTool.Select;
        }

        private void UpdateLeftMouse()
        {
            if (Tool == HorusTool.Place)
            {
                if (Input.GetMouseButtonDown(0) && Pick.Valid)
                {
                    bool repeat = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    LogPickDiagnostics();
                    Unit spawned = owner.PlaceAtWorld(Pick.Point);
                    // Keep placement armed when a validation/confirmation gate rejects
                    // the click (RTS two-step, live ordnance, lookup-only content, etc.).
                    if (owner.LastPlacementConsumed && (!repeat || owner.LastPlacementWasLiveOrdnance))
                    {
                        if (spawned != null) selection.Select(spawned, false, false);
                        owner.CancelPlacement();
                    }
                }
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                lmbDownPos = Input.mousePosition;
                lmbDownUnit = Pick.Unit;
                lmbTracking = true;
                MarqueeActive = lmbDownUnit == null;
                MarqueeRawScreen = new Rect(lmbDownPos, Vector2.zero);
            }

            if (lmbTracking && Input.GetMouseButton(0))
            {
                Vector2 current = Input.mousePosition;
                if ((current - lmbDownPos).sqrMagnitude > ClickMaxPixels * ClickMaxPixels)
                {
                    MarqueeActive = true;
                    MarqueeRawScreen = Rect.MinMaxRect(
                        Mathf.Min(lmbDownPos.x, current.x),
                        Mathf.Min(lmbDownPos.y, current.y),
                        Mathf.Max(lmbDownPos.x, current.x),
                        Mathf.Max(lmbDownPos.y, current.y));
                }
            }

            if (lmbTracking && Input.GetMouseButtonUp(0))
            {
                Vector2 current = Input.mousePosition;
                bool dragged = (current - lmbDownPos).sqrMagnitude > ClickMaxPixels * ClickMaxPixels;
                bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool remove = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (dragged)
                {
                    selection.SelectInScreenRect(MarqueeRawScreen, add, remove, Camera.main);
                }
                else
                {
                    Unit clicked = Pick.Unit ?? lmbDownUnit;
                    if (clicked != null && clicked.definition == lastClickDefinition && Time.unscaledTime - lastClickTime <= DoubleClickSeconds)
                    {
                        selection.SelectDefinitionOnScreen(clicked.definition, Camera.main);
                        lastClickDefinition = null;
                    }
                    else
                    {
                        selection.Select(clicked, add, remove);
                        lastClickDefinition = clicked != null ? clicked.definition : null;
                        lastClickTime = Time.unscaledTime;
                    }
                }
                lmbTracking = false;
                MarqueeActive = false;
            }
        }

        /// <summary>
        /// One-shot diagnostic for "placement lands somewhere other than the click point":
        /// dumps exactly what the pick ray found (or didn't) at the moment of the click,
        /// including whether the water-plane fallback substituted a different point.
        /// </summary>
        private void LogPickDiagnostics()
        {
            Camera cam = Camera.main;
            Vector3 mouse = Input.mousePosition;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            float distCamToPoint = Vector3.Distance(camPos, Pick.Point);
            float waterEnter = 0f;
            bool waterPlaneHit = cam != null && Datum.origin != null &&
                Datum.WaterPlane().Raycast(cam.ScreenPointToRay(mouse), out waterEnter);
            HorusLog.Info("Placement",
                $"Click pick: cam={(cam != null ? cam.name : "null")} camPos={FormatV(camPos)} " +
                $"mouse={mouse.x:F0},{mouse.y:F0} pickValid={Pick.Valid} pickPoint={FormatV(Pick.Point)} " +
                $"pickDistance={Pick.Distance:F1} hitCollider={(Pick.Hit.collider != null ? Pick.Hit.collider.name : "none")} " +
                $"camToPickDistance={distCamToPoint:F1} datumOrigin={(Datum.origin != null ? FormatV(Datum.origin.position) : "null")} " +
                $"waterPlaneWouldHit={waterPlaneHit} waterEnter={(waterPlaneHit ? waterEnter.ToString("F1") : "n/a")}.");
        }

        private static string FormatV(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";

        private void UpdateRightMouse(bool overGui)
        {
            if (Input.GetMouseButtonDown(1))
            {
                rmbDownPos = Input.mousePosition;
                rmbDownTime = Time.unscaledTime;
                rmbStartedOverGui = overGui;
                rmbLookEngaged = false;
                rmbTracking = true;
            }
            else if (rmbTracking && Input.GetMouseButton(1) && !rmbLookEngaged && !rmbStartedOverGui)
            {
                bool moved = ((Vector2)Input.mousePosition - rmbDownPos).sqrMagnitude > ClickMaxPixels * ClickMaxPixels;
                bool holding = Time.unscaledTime - rmbDownTime > RmbClickMaxSeconds;
                if (moved || holding)
                {
                    rmbLookEngaged = true;
                    owner.SetHorusCursorLock(true);
                }
            }
            else if (rmbTracking && Input.GetMouseButtonUp(1))
            {
                if (rmbLookEngaged)
                {
                    owner.SetHorusCursorLock(false);
                }
                else if (!overGui && !rmbStartedOverGui)
                {
                    WorldPick releasedPick = WorldPick.FromScreen(rmbDownPos);
                    releasedPick = WorldPick.WithScreenUnitFallback(releasedPick, rmbDownPos);
                    OnRightClick(releasedPick);
                }
                rmbTracking = false;
                rmbLookEngaged = false;
                rmbStartedOverGui = false;
            }
        }

        private void OnRightClick(WorldPick pick)
        {
            if (Tool == HorusTool.Place)
            {
                owner.CancelPlacement();
                return;
            }

            bool forceMenu = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (pick.Unit != null)
            {
                if (!selection.Contains(pick.Unit)) selection.Select(pick.Unit, false, false);
                HorusContextMenu.Open(owner.RawScreenToScaledGui(rmbDownPos), HorusContextMenuBuilder.BuildForUnits(owner, selection, orders, pick));
            }
            else if (forceMenu)
            {
                HorusContextMenu.Open(owner.RawScreenToScaledGui(rmbDownPos), HorusContextMenuBuilder.BuildForWorld(owner, selection, orders, pick));
            }
            else if (selection.HasSelection && pick.Valid)
            {
                orders.IssueMove(selection.Units, pick.Point.ToGlobalPosition(), owner.CurrentFormation);
            }
            else
            {
                HorusContextMenu.Open(owner.RawScreenToScaledGui(rmbDownPos), HorusContextMenuBuilder.BuildForWorld(owner, selection, orders, pick));
            }
        }

        private void UpdateMap(bool overGui)
        {
            owner.HideGhost();
            if (overGui) return;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !map.TryGetCursorCoordinates(out GlobalPosition mapPosition)) return;

            if (Input.GetMouseButtonDown(0))
            {
                if (Tool == HorusTool.Place || Tool == HorusTool.MapPlace)
                {
                    bool repeat = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    Unit spawned = owner.PlaceAtMap(mapPosition);
                    if (owner.LastPlacementConsumed && (!repeat || owner.LastPlacementWasLiveOrdnance))
                    {
                        if (spawned != null) selection.Select(spawned, false, false);
                        owner.CancelPlacement();
                    }
                }
                else
                {
                    Unit nearest = FindNearestMapUnit(mapPosition, 500f);
                    bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    bool remove = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    selection.Select(nearest, add, remove);
                }
            }

            if (Input.GetMouseButtonDown(1))
            {
                rmbDownPos = Input.mousePosition;
                rmbDownTime = Time.unscaledTime;
            }
            else if (Input.GetMouseButtonUp(1))
            {
                float distanceSq = ((Vector2)Input.mousePosition - rmbDownPos).sqrMagnitude;
                float duration = Time.unscaledTime - rmbDownTime;
                if (distanceSq <= ClickMaxPixels * ClickMaxPixels && duration <= RmbClickMaxSeconds)
                {
                    if (selection.HasSelection) orders.IssueMove(selection.Units, mapPosition, owner.CurrentFormation);
                    else owner.CancelPlacement();
                }
            }
        }

        private static Unit FindNearestMapUnit(GlobalPosition target, float radius)
        {
            if (UnitRegistry.allUnits == null) return null;
            Unit nearest = null;
            float best = radius * radius;
            Vector3 targetVector = target.AsVector3();
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled) continue;
                Vector3 p = unit.GlobalPosition().AsVector3();
                float dx = p.x - targetVector.x;
                float dz = p.z - targetVector.z;
                float distance = dx * dx + dz * dz;
                if (distance < best)
                {
                    best = distance;
                    nearest = unit;
                }
            }
            return nearest;
        }

        private void DeleteUnderCursor()
        {
            if (Pick.Unit == null) return;
            if (selection.Contains(Pick.Unit)) owner.DeleteSelection();
            else owner.DeleteUnits(new[] { Pick.Unit });
        }

        private void HandleEmergencyAndShortcuts(bool mapOpen)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (HorusPlugin.HotkeyDeselectUnit != null && Input.GetKeyDown(HorusPlugin.HotkeyDeselectUnit.Value))
            {
                owner.CancelPlacement();
                selection.Clear();
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (HorusContextMenu.IsOpen) HorusContextMenu.Close();
                else if (MarqueeActive) { MarqueeActive = false; lmbTracking = false; }
                else if (Tool != HorusTool.Select) owner.CancelPlacement();
                else selection.Clear();
            }
            if (Input.GetKeyDown(KeyCode.Delete)) owner.DeleteSelection();
            if (Input.GetKeyDown(KeyCode.F)) owner.FocusSelection();
            if (Input.GetKeyDown(KeyCode.H)) orders.SetHold(selection.Units, true);
            if (ctrl && Input.GetKeyDown(KeyCode.D)) owner.DuplicateSelection();
            if (ctrl && Input.GetKeyDown(KeyCode.A)) selection.SelectAll(owner.HorusSpawnedUnits);
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) HorusUndo.Undo();
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) HorusUndo.Redo();
            if (Input.GetKeyDown(KeyCode.R) && Tool == HorusTool.Place) owner.ResetPlacementYaw();
            if (Input.GetKeyDown(KeyCode.Tab) && Tool == HorusTool.Place) owner.CycleFormation(shift ? -1 : 1);

            for (int i = 0; i < 9; i++)
            {
                KeyCode key = (KeyCode)((int)KeyCode.Alpha1 + i);
                if (!Input.GetKeyDown(key)) continue;
                if (ctrl) selection.AssignControlGroup(i);
                else selection.RecallControlGroup(i);
            }

            if (mapOpen && Input.GetKeyDown(KeyCode.M)) owner.CancelMapPlacement();
        }
    }
}
