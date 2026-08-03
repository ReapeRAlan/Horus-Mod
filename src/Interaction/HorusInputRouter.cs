using System.Collections.Generic;
using HorusMod.Core;
using HorusMod.Logging;
using HorusMod.Placement;
using HorusMod.UI;
using HorusMod.UI.ContextMenu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HorusMod.Interaction
{
    public sealed class HorusInputRouter
    {
        private const float ClickMaxPixels = 6f;
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
        private bool lmbSuppressedByMenu;
        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>(16);
        private Vector2 lmbDownPos;
        private Unit lmbDownUnit;
        private bool lmbTracking;
        private float lastClickTime;
        private UnitDefinition lastClickDefinition;
        private readonly List<GlobalPosition> patrolDraft = new List<GlobalPosition>();
        private readonly List<Unit> patrolUnits = new List<Unit>();
        private bool patrolPlanning;
        private readonly List<Unit> groupOrderUnits = new List<Unit>();
        private HorusGroupOrderTargetMode groupOrderTargetMode;

        public HorusTool Tool { get; private set; } = HorusTool.Select;
        public WorldPick Pick { get; private set; }
        public bool MarqueeActive { get; private set; }
        public Rect MarqueeRawScreen { get; private set; }
        public bool Looking => rmbLookEngaged;
        public bool PatrolPlanning => patrolPlanning;
        public HorusGroupOrderTargetMode GroupOrderTargetMode => groupOrderTargetMode;

        public HorusInputRouter(HorusManager owner, HorusSelection selection, HorusOrders orders, HorusOverlay overlay)
        {
            this.owner = owner;
            this.selection = selection;
            this.orders = orders;
            this.overlay = overlay;
        }

        public void SetTool(HorusTool tool)
        {
            if (patrolPlanning) CancelPatrolRoute(showToast: false);
            if (groupOrderTargetMode != HorusGroupOrderTargetMode.None) CancelGroupOrderTargeting(showToast: false);
            Tool = tool;
            if (tool == HorusTool.Select) owner.HideGhost();
        }

        public void Update()
        {
            bool mapOpen = DynamicMap.mapMaximized;
            if (groupOrderTargetMode != HorusGroupOrderTargetMode.None && Input.GetKeyDown(KeyCode.Escape))
            {
                CancelGroupOrderTargeting(showToast: true);
                overlay.Update();
                return;
            }
            if (patrolPlanning && HandlePatrolKeyboard())
            {
                overlay.Update();
                return;
            }
            if (patrolPlanning && mapOpen)
                CancelPatrolRoute(showToast: true);
            if (groupOrderTargetMode != HorusGroupOrderTargetMode.None && mapOpen)
                CancelGroupOrderTargeting(showToast: true);
            Vector2 scaledGuiMouse = owner.RawScreenToScaledGui(Input.mousePosition);
            bool overContextMenu = HorusContextMenu.ContainsPoint(scaledGuiMouse);

            // LMB outside a menu dismisses it without leaking through to world
            // selection. RMB outside dismisses and continues, allowing a context
            // menu to be relocated with one click instead of two.
            ContextMenuOutsideClickAction outsideMenuAction = ContextMenuPointerPolicy.Classify(
                HorusContextMenu.IsOpen, overContextMenu, Input.GetMouseButtonDown(0), Input.GetMouseButtonDown(1));
            if (outsideMenuAction == ContextMenuOutsideClickAction.DismissAndConsume)
            {
                HorusContextMenu.Close();
                lmbSuppressedByMenu = true;
            }
            if (lmbSuppressedByMenu && Input.GetMouseButtonUp(0)) lmbSuppressedByMenu = false;
            if (outsideMenuAction == ContextMenuOutsideClickAction.DismissAndContinue)
                HorusContextMenu.Close();

            bool overHorusUi = owner.IsPointerOverHorusUI;
            bool overGameUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool overSelectionGui = overHorusUi || overGameUi;
            bool inspectRmbUi = Input.GetMouseButtonDown(1) || Input.GetMouseButtonUp(1) || rmbTracking;
            bool overRmbGui = overHorusUi || (inspectRmbUi && IsPointerOverInteractiveGameUi(Input.mousePosition));

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

            bool tacticalTargeting = patrolPlanning || groupOrderTargetMode != HorusGroupOrderTargetMode.None;
            bool overTacticalGui = overHorusUi || (tacticalTargeting && IsPointerOverInteractiveGameUi(Input.mousePosition));
            if (!overSelectionGui || (tacticalTargeting && !overTacticalGui))
            {
                Pick = WorldPick.FromScreen(Input.mousePosition);
                selection.SetHover(Tool == HorusTool.Select ? Pick.Unit : null);
                if (Tool == HorusTool.Place) owner.UpdateGhostAt(Pick);
            }
            else
            {
                selection.SetHover(null);
            }

            if (groupOrderTargetMode != HorusGroupOrderTargetMode.None)
            {
                if (!overTacticalGui && Input.GetMouseButtonDown(1))
                {
                    CancelGroupOrderTargeting(showToast: true);
                }
                else if (!overTacticalGui && Input.GetMouseButtonDown(0))
                {
                    WorldPick targetPick = HorusGroupOrderTargetPolicy.RequiresUnit(groupOrderTargetMode)
                        ? WorldPick.WithScreenUnitFallback(Pick, Input.mousePosition)
                        : Pick;
                    ExecuteGroupOrderTarget(targetPick);
                }
                overlay.Update();
                return;
            }

            if (patrolPlanning)
            {
                overlay.SetPatrolDraft(patrolDraft, Pick.Valid, Pick.Point.ToGlobalPosition());
                if (!overTacticalGui && Input.GetMouseButtonDown(0) && Pick.Valid)
                {
                    patrolDraft.Add(Pick.Point.ToGlobalPosition());
                    HorusToasts.Show($"Patrol point {patrolDraft.Count} added");
                }
                overlay.Update();
                return;
            }

            UpdateRightMouse(overRmbGui);
            if (rmbLookEngaged && !Input.GetMouseButton(1) && !Input.GetMouseButtonUp(1))
            {
                // Focus changes can swallow MouseUp. Never let a stale drag keep the
                // game cursor locked after the physical button has been released.
                CancelPointerCapture();
            }
            owner.UpdateFreeCamera(rmbLookEngaged);

            if (!overSelectionGui && !rmbLookEngaged && !lmbSuppressedByMenu)
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
            CancelPatrolRoute(showToast: false);
            CancelGroupOrderTargeting(showToast: false);
        }

        public void CancelPointerCapture()
        {
            rmbTracking = false;
            rmbLookEngaged = false;
            rmbStartedOverGui = false;
            lmbSuppressedByMenu = false;
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
                        // Keep the designated target selected after a live-ordnance shot so
                        // the operator can inspect it or arm another weapon without finding
                        // the target again. Ordinary placement still selects the new unit.
                        if (spawned != null && !owner.LastPlacementWasLiveOrdnance)
                            selection.Select(spawned, false, false);
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
                Vector2 delta = (Vector2)Input.mousePosition - rmbDownPos;
                if (RmbGestureClassifier.IsDrag(delta.x, delta.y))
                {
                    rmbLookEngaged = true;
                    owner.SetHorusCursorLock(true);
                }
            }
            else if (rmbTracking && Input.GetMouseButtonUp(1))
            {
                Vector2 delta = (Vector2)Input.mousePosition - rmbDownPos;
                float duration = Time.unscaledTime - rmbDownTime;
                string outcome;
                int itemCount = 0;
                WorldPick releasedPick = default;
                if (rmbLookEngaged)
                {
                    owner.SetHorusCursorLock(false);
                    outcome = "camera-look";
                }
                else if (!overGui && !rmbStartedOverGui)
                {
                    releasedPick = WorldPick.FromScreen(rmbDownPos);
                    releasedPick = WorldPick.WithScreenUnitFallback(releasedPick, rmbDownPos);
                    outcome = OnRightClick(releasedPick, out itemCount);
                }
                else outcome = "blocked-by-interactive-ui";
                HorusLog.Verbose("Input",
                    $"RMB outcome={outcome} duration={duration:F3}s distance={delta.magnitude:F1}px " +
                    $"startedBlocked={rmbStartedOverGui} releasedBlocked={overGui} " +
                    $"pickValid={releasedPick.Valid} unit={(releasedPick.Unit != null ? releasedPick.Unit.unitName : "none")} menuItems={itemCount}.");
                rmbTracking = false;
                rmbLookEngaged = false;
                rmbStartedOverGui = false;
            }
        }

        private string OnRightClick(WorldPick pick, out int itemCount)
        {
            itemCount = 0;
            if (Tool == HorusTool.Place)
            {
                owner.CancelPlacement();
                return "cancel-placement";
            }

            bool forceMenu = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (pick.Unit != null)
            {
                bool useAsTacticalTarget = selection.HasSelection && !selection.Contains(pick.Unit) &&
                    orders.IsTacticalTarget(selection.Units, pick.Unit);
                if (!selection.Contains(pick.Unit) && !useAsTacticalTarget) selection.Select(pick.Unit, false, false);
                List<ContextMenuItem> items;
                try
                {
                    items = HorusContextMenuBuilder.BuildForUnits(owner, selection, orders, pick);
                }
                catch (System.Exception ex)
                {
                    HorusLog.Error("Input", $"RMB menu builder failed; opening tactical fallback. {ex}");
                    items = HorusContextMenuBuilder.BuildFallbackForSelection(owner, selection, orders);
                }
                itemCount = items.Count;
                HorusContextMenu.Open(owner.RawScreenToScaledGui(rmbDownPos), items);
                HorusLog.Info("Input", $"RMB unit menu opened for '{pick.Unit.unitName}' with {itemCount} option(s).");
                return "unit-menu";
            }
            else if (forceMenu)
            {
                List<ContextMenuItem> items = HorusContextMenuBuilder.BuildForWorld(owner, selection, orders, pick);
                itemCount = items.Count;
                HorusContextMenu.Open(owner.RawScreenToScaledGui(rmbDownPos), items);
                return "world-menu";
            }
            else if (selection.HasSelection && pick.Valid)
            {
                orders.IssueMove(selection.Units, pick.Point.ToGlobalPosition(), owner.CurrentFormation);
                return "move-order";
            }
            else
            {
                List<ContextMenuItem> items = HorusContextMenuBuilder.BuildForWorld(owner, selection, orders, pick);
                itemCount = items.Count;
                HorusContextMenu.Open(owner.RawScreenToScaledGui(rmbDownPos), items);
                return "world-menu";
            }
        }

        public bool BeginPatrolRoute(IReadOnlyList<Unit> units, GlobalPosition firstPoint)
        {
            if (units == null || units.Count == 0) return false;
            patrolUnits.Clear();
            for (int i = 0; i < units.Count; i++)
                if (units[i] != null) patrolUnits.Add(units[i]);
            if (patrolUnits.Count == 0) return false;
            patrolDraft.Clear();
            patrolDraft.Add(firstPoint);
            patrolPlanning = true;
            CancelPointerCapture();
            HorusContextMenu.Close();
            overlay.SetPatrolDraft(patrolDraft, false, default);
            HorusToasts.Show("Patrol route started: add points, then press Enter");
            return true;
        }

        public bool BeginGroupOrderTargeting(HorusGroupOrderTargetMode mode, IReadOnlyList<Unit> units)
        {
            if (mode == HorusGroupOrderTargetMode.None || units == null || units.Count == 0) return false;
            if (Tool != HorusTool.Select) owner.CancelPlacement();
            if (patrolPlanning) CancelPatrolRoute(showToast: false);
            groupOrderUnits.Clear();
            for (int i = 0; i < units.Count; i++)
                if (units[i] != null && !units[i].disabled) groupOrderUnits.Add(units[i]);
            if (groupOrderUnits.Count == 0) return false;
            groupOrderTargetMode = mode;
            lmbTracking = false;
            MarqueeActive = false;
            CancelPointerCapture();
            HorusContextMenu.Close();
            overlay.SetGroupOrderTargeting(mode, groupOrderUnits.Count);
            HorusToasts.Show(HorusGroupOrderTargetPolicy.Prompt(mode));
            HorusLog.Info("Orders", $"Group {mode} targeting armed for {groupOrderUnits.Count} unit(s).");
            return true;
        }

        private void ExecuteGroupOrderTarget(WorldPick pick)
        {
            HorusGroupOrderTargetMode mode = groupOrderTargetMode;
            if (mode == HorusGroupOrderTargetMode.None) return;
            if (HorusGroupOrderTargetPolicy.RequiresUnit(mode) && pick.Unit == null)
            {
                HorusToasts.Show(mode == HorusGroupOrderTargetMode.AttackTarget
                    ? "Select a known enemy unit"
                    : "Select a friendly unit to guard");
                return;
            }
            if (!HorusGroupOrderTargetPolicy.RequiresUnit(mode) && !pick.Valid)
            {
                HorusToasts.Show("Select a valid world destination");
                return;
            }

            var units = new List<Unit>(groupOrderUnits);
            bool accepted = false;
            switch (mode)
            {
                case HorusGroupOrderTargetMode.Move:
                    accepted = orders.IssueMove(units, pick.Point.ToGlobalPosition(), owner.CurrentFormation);
                    break;
                case HorusGroupOrderTargetMode.AttackMove:
                    accepted = orders.IssueAttackMove(units, pick.Point.ToGlobalPosition());
                    break;
                case HorusGroupOrderTargetMode.Patrol:
                    CancelGroupOrderTargeting(showToast: false);
                    BeginPatrolRoute(units, pick.Point.ToGlobalPosition());
                    return;
                case HorusGroupOrderTargetMode.AttackTarget:
                    accepted = orders.IssueAttackTarget(units, pick.Unit);
                    break;
                case HorusGroupOrderTargetMode.Guard:
                    accepted = orders.IssueGuard(units, pick.Unit);
                    break;
            }
            if (accepted) CancelGroupOrderTargeting(showToast: false);
        }

        private void CancelGroupOrderTargeting(bool showToast)
        {
            if (groupOrderTargetMode == HorusGroupOrderTargetMode.None && groupOrderUnits.Count == 0) return;
            groupOrderTargetMode = HorusGroupOrderTargetMode.None;
            groupOrderUnits.Clear();
            overlay.ClearGroupOrderTargeting();
            if (showToast) HorusToasts.Show("Group order targeting cancelled");
        }

        private bool HandlePatrolKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPatrolRoute(showToast: true);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                if (patrolDraft.Count > 1) patrolDraft.RemoveAt(patrolDraft.Count - 1);
                HorusToasts.Show($"Patrol route: {patrolDraft.Count} point(s)");
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (patrolDraft.Count < 2)
                {
                    HorusToasts.Show("Patrol needs at least two points");
                    return true;
                }
                orders.IssuePatrol(patrolUnits, patrolDraft);
                CancelPatrolRoute(showToast: false);
                return true;
            }
            return false;
        }

        private void CancelPatrolRoute(bool showToast)
        {
            if (!patrolPlanning && patrolDraft.Count == 0) return;
            patrolPlanning = false;
            patrolDraft.Clear();
            patrolUnits.Clear();
            overlay.ClearPatrolDraft();
            if (showToast) HorusToasts.Show("Patrol route cancelled");
        }

        private bool IsPointerOverInteractiveGameUi(Vector2 rawScreenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            var pointer = new PointerEventData(eventSystem) { position = rawScreenPosition };
            uiRaycastResults.Clear();
            eventSystem.RaycastAll(pointer, uiRaycastResults);
            for (int i = 0; i < uiRaycastResults.Count; i++)
            {
                GameObject target = uiRaycastResults[i].gameObject;
                if (target == null) continue;
                Selectable selectable = target.GetComponentInParent<Selectable>();
                if (selectable != null && selectable.IsActive() && selectable.IsInteractable()) return true;
                if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) != null ||
                    ExecuteEvents.GetEventHandler<IBeginDragHandler>(target) != null ||
                    ExecuteEvents.GetEventHandler<IDragHandler>(target) != null ||
                    ExecuteEvents.GetEventHandler<IScrollHandler>(target) != null)
                    return true;
            }
            return false;
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
                Vector2 delta = (Vector2)Input.mousePosition - rmbDownPos;
                if (!RmbGestureClassifier.IsDrag(delta.x, delta.y))
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
