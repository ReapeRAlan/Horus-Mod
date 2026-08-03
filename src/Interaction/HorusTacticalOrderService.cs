using System.Collections.Generic;
using HorusMod.Logging;
using UnityEngine;

namespace HorusMod.Interaction
{
    /// <summary>
    /// Host-authoritative tactical registry. Mission AI is untouched until a unit
    /// is explicitly registered here by a Horus order.
    /// </summary>
    internal sealed class HorusTacticalOrderService
    {
        private sealed class ActiveOrder
        {
            public Unit Unit;
            public HorusOrderKind Kind;
            public GlobalPosition Destination;
            public List<GlobalPosition> Waypoints;
            public int WaypointIndex;
            public Unit Target;
            public Vector3 GuardLocalOffset;
            public bool MovementIssued;
            public bool Engaging;
            public float LastThreatSeen;
            public float NextMovementRefresh;
        }

        private readonly Dictionary<Unit, ActiveOrder> orders = new Dictionary<Unit, ActiveOrder>();
        private readonly Dictionary<Unit, HorusRulesOfEngagement> rules = new Dictionary<Unit, HorusRulesOfEngagement>();
        private readonly List<Unit> staleUnits = new List<Unit>();
        private float nextThink;

        public static HorusTacticalOrderService Active { get; private set; }
        public int ActiveOrderCount => orders.Count;

        public HorusTacticalOrderService()
        {
            Active = this;
        }

        public void RegisterMove(Unit unit, GlobalPosition destination)
        {
            if (!IsUsable(unit)) return;
            orders[unit] = new ActiveOrder
            {
                Unit = unit,
                Kind = HorusOrderKind.Move,
                Destination = destination,
                MovementIssued = true
            };
        }

        public void RegisterHold(Unit unit)
        {
            if (!IsUsable(unit)) return;
            orders[unit] = new ActiveOrder { Unit = unit, Kind = HorusOrderKind.Hold, Destination = unit.GlobalPosition(), MovementIssued = true };
        }

        public bool RegisterAttackTarget(Unit unit, Unit target, out string reason)
        {
            if (!CanAttackKnownTarget(unit, target, out reason)) return false;
            if (unit is Aircraft aircraft) HorusAircraftOrders.Clear(aircraft);
            orders[unit] = new ActiveOrder
            {
                Unit = unit,
                Kind = HorusOrderKind.AttackTarget,
                Target = target
            };
            return true;
        }

        public bool RegisterAttackMove(Unit unit, GlobalPosition destination, out string reason)
        {
            if (!HorusOrders.CanCommandUnit(unit, out reason)) return false;
            var order = new ActiveOrder { Unit = unit, Kind = HorusOrderKind.AttackMove, Destination = destination };
            orders[unit] = order;
            IssueMovement(order, destination);
            return true;
        }

        public bool RegisterPatrol(Unit unit, IReadOnlyList<GlobalPosition> waypoints, out string reason)
        {
            if (!HorusOrders.CanCommandUnit(unit, out reason)) return false;
            if (waypoints == null || waypoints.Count < 2)
            {
                reason = "patrol requires at least two waypoints";
                return false;
            }
            var copy = new List<GlobalPosition>(waypoints.Count);
            for (int i = 0; i < waypoints.Count; i++) copy.Add(waypoints[i]);
            var order = new ActiveOrder
            {
                Unit = unit,
                Kind = HorusOrderKind.Patrol,
                Waypoints = copy,
                WaypointIndex = 0,
                Destination = copy[0]
            };
            orders[unit] = order;
            IssueMovement(order, order.Destination);
            return true;
        }

        public bool RegisterGuard(Unit unit, Unit target, Vector3 localOffset, out string reason)
        {
            if (!HorusOrders.CanCommandUnit(unit, out reason)) return false;
            if (!IsUsable(target) || unit == target || unit.NetworkHQ == null || target.NetworkHQ != unit.NetworkHQ)
            {
                reason = "guard target must be a living friendly unit";
                return false;
            }
            var order = new ActiveOrder
            {
                Unit = unit,
                Kind = HorusOrderKind.Guard,
                Target = target,
                GuardLocalOffset = localOffset
            };
            orders[unit] = order;
            RefreshGuardDestination(order, force: true);
            return true;
        }

        public void SetRules(Unit unit, HorusRulesOfEngagement value)
        {
            if (!IsUsable(unit) || (unit is Aircraft aircraft && aircraft.Player != null)) return;
            if (value == HorusRulesOfEngagement.WeaponsFree) rules.Remove(unit);
            else rules[unit] = value;
        }

        public HorusRulesOfEngagement GetRules(Unit unit)
        {
            return unit != null && rules.TryGetValue(unit, out HorusRulesOfEngagement value)
                ? value
                : HorusRulesOfEngagement.WeaponsFree;
        }

        public HorusOrderKind GetOrderKind(Unit unit)
        {
            return unit != null && orders.TryGetValue(unit, out ActiveOrder order) ? order.Kind : HorusOrderKind.None;
        }

        public bool ClearOrder(Unit unit, bool restoreAircraft = true)
        {
            if (unit == null) return false;
            if (orders.TryGetValue(unit, out ActiveOrder existing) && existing.Kind == HorusOrderKind.AttackTarget && !(unit is Aircraft))
                ClearForcedStationTarget(unit);
            bool removed = orders.Remove(unit);
            if (restoreAircraft && unit is Aircraft aircraft)
                removed = HorusAircraftOrders.Clear(aircraft) || removed;
            return removed;
        }

        public void Reset()
        {
            foreach (Unit unit in new List<Unit>(orders.Keys)) ClearOrder(unit);
            orders.Clear();
            rules.Clear();
            HorusAircraftOrders.Reset();
            HorusBombingCorrection.Reset();
            Active = this;
        }

        public void Tick()
        {
            if (Time.unscaledTime < nextThink) return;
            nextThink = Time.unscaledTime + 0.2f;
            staleUnits.Clear();
            foreach (KeyValuePair<Unit, ActiveOrder> pair in orders)
            {
                ActiveOrder order = pair.Value;
                if (!IsUsable(pair.Key))
                {
                    staleUnits.Add(pair.Key);
                    continue;
                }
                TickOrder(order);
            }
            for (int i = 0; i < staleUnits.Count; i++)
                ClearOrder(staleUnits[i]);

            var staleRules = new List<Unit>();
            foreach (KeyValuePair<Unit, HorusRulesOfEngagement> pair in rules)
                if (!IsUsable(pair.Key)) staleRules.Add(pair.Key);
            for (int i = 0; i < staleRules.Count; i++) rules.Remove(staleRules[i]);
        }

        private void TickOrder(ActiveOrder order)
        {
            switch (order.Kind)
            {
                case HorusOrderKind.Move:
                    if (HasArrived(order.Unit, order.Destination)) staleUnits.Add(order.Unit);
                    break;
                case HorusOrderKind.Hold:
                    break;
                case HorusOrderKind.AttackTarget:
                    if (!CanAttackKnownTarget(order.Unit, order.Target, out _)) staleUnits.Add(order.Unit);
                    else if (!(order.Unit is Aircraft)) ApplyForcedStationTarget(order);
                    break;
                case HorusOrderKind.AttackMove:
                    TickNavigatingOrder(order, loop: false);
                    break;
                case HorusOrderKind.Patrol:
                    TickNavigatingOrder(order, loop: true);
                    break;
                case HorusOrderKind.Guard:
                    TickGuard(order);
                    break;
            }
        }

        private void TickNavigatingOrder(ActiveOrder order, bool loop)
        {
            if (HandleAircraftEngagement(order)) return;
            if (HasArrived(order.Unit, order.Destination))
            {
                if (!loop)
                {
                    staleUnits.Add(order.Unit);
                    return;
                }
                order.WaypointIndex = TacticalRouteCursor.Next(order.WaypointIndex, order.Waypoints.Count, loop: true);
                order.Destination = order.Waypoints[order.WaypointIndex];
                order.MovementIssued = false;
            }
            if (!order.MovementIssued || (order.Unit is Aircraft aircraft && !HorusAircraftOrders.IsActive(aircraft)))
                IssueMovement(order, order.Destination);
        }

        private void TickGuard(ActiveOrder order)
        {
            if (!IsUsable(order.Target) || order.Unit.NetworkHQ == null || order.Target.NetworkHQ != order.Unit.NetworkHQ)
            {
                staleUnits.Add(order.Unit);
                return;
            }
            if (HandleAircraftEngagement(order)) return;
            RefreshGuardDestination(order, force: false);
        }

        private bool HandleAircraftEngagement(ActiveOrder order)
        {
            if (!(order.Unit is Aircraft aircraft)) return false;
            bool threatVisible = HasKnownEngageableThreat(aircraft);
            TacticalEngagementAction action = TacticalEngagementPolicy.Decide(
                order.Engaging, threatVisible, Time.unscaledTime - order.LastThreatSeen, 4f);
            if (threatVisible) order.LastThreatSeen = Time.unscaledTime;
            if (action == TacticalEngagementAction.EnterCombat)
            {
                HorusAircraftOrders.Clear(aircraft);
                order.Engaging = true;
                order.MovementIssued = false;
                return true;
            }
            if (action == TacticalEngagementAction.StayInCombat) return true;
            if (action == TacticalEngagementAction.ResumeNavigation)
            {
                order.Engaging = false;
                order.MovementIssued = false;
            }
            return false;
        }

        private void RefreshGuardDestination(ActiveOrder order, bool force)
        {
            if (!force && Time.unscaledTime < order.NextMovementRefresh) return;
            order.NextMovementRefresh = Time.unscaledTime + 1f;
            Vector3 local = order.Target.transform.position + order.Target.transform.TransformDirection(order.GuardLocalOffset);
            if (order.Unit is Ship) local.y = Datum.LocalSeaY;
            GlobalPosition destination = local.ToGlobalPosition();
            float refreshDistance = order.Unit is Aircraft ? 150f : order.Unit is Ship ? 80f : 30f;
            if (force || !order.MovementIssued || HorizontalDistance(order.Destination, destination) > refreshDistance)
            {
                order.Destination = destination;
                IssueMovement(order, destination);
            }
        }

        private static void IssueMovement(ActiveOrder order, GlobalPosition destination)
        {
            if (HorusOrders.TrySetDestination(order.Unit, destination, playerCommand: true, out string reason))
            {
                order.MovementIssued = true;
                order.Destination = destination;
            }
            else
            {
                order.MovementIssued = false;
                HorusLog.Trace("Orders", "MoveRetry:" + order.Unit.GetInstanceID(),
                    $"Tactical {order.Kind} movement deferred for {order.Unit.unitName}: {reason}", 3f);
            }
        }

        private static void ApplyForcedStationTarget(ActiveOrder order)
        {
            if (order.Unit?.weaponStations == null || order.Target == null) return;
            for (int i = 0; i < order.Unit.weaponStations.Count; i++)
            {
                WeaponStation station = order.Unit.weaponStations[i];
                if (station == null || station.WeaponInfo == null || station.Ammo <= 0) continue;
                if (station.Weapons != null)
                    for (int weaponIndex = 0; weaponIndex < station.Weapons.Count; weaponIndex++)
                        station.Weapons[weaponIndex]?.SetTarget(order.Target);
                if (station.Turrets != null)
                    for (int turretIndex = 0; turretIndex < station.Turrets.Count; turretIndex++)
                        station.Turrets[turretIndex]?.SetTarget(order.Target.persistentID, station.Number);
            }
        }

        private static void ClearForcedStationTarget(Unit unit)
        {
            if (unit?.weaponStations == null) return;
            for (int i = 0; i < unit.weaponStations.Count; i++)
            {
                WeaponStation station = unit.weaponStations[i];
                if (station == null) continue;
                if (station.Weapons != null)
                    for (int weaponIndex = 0; weaponIndex < station.Weapons.Count; weaponIndex++)
                        station.Weapons[weaponIndex]?.SetTarget(null);
                if (station.Turrets != null)
                    for (int turretIndex = 0; turretIndex < station.Turrets.Count; turretIndex++)
                        station.Turrets[turretIndex]?.SetTarget(PersistentID.None, station.Number);
            }
        }

        private static bool HasArrived(Unit unit, GlobalPosition destination)
        {
            float distance = HorizontalDistance(unit.GlobalPosition(), destination);
            if (unit is Aircraft aircraft)
            {
                float threshold = aircraft.autopilot is AutopilotPlane
                    ? Mathf.Max(350f, aircraft.speed * 1.5f)
                    : Mathf.Max(90f, aircraft.definition != null ? aircraft.definition.length * 3f : 90f);
                return distance <= threshold;
            }
            if (unit is Ship)
                return distance <= Mathf.Max(100f, unit.definition != null ? unit.definition.length * 2f : 100f);
            return distance <= Mathf.Max(30f, unit.definition != null ? unit.definition.length * 2f : 30f);
        }

        private static float HorizontalDistance(GlobalPosition a, GlobalPosition b)
        {
            Vector3 delta = b.ToLocalPosition() - a.ToLocalPosition();
            delta.y = 0f;
            return delta.magnitude;
        }

        public static bool CanAttackKnownTarget(Unit unit, Unit target, out string reason)
        {
            if (!IsUsable(unit) || !IsUsable(target) || unit == target)
            {
                reason = "unit or target unavailable";
                return false;
            }
            if (unit.NetworkHQ == null || target.NetworkHQ == unit.NetworkHQ)
            {
                reason = "target is not an enemy";
                return false;
            }
            TrackingInfo tracking = unit.NetworkHQ.GetTrackingData(target.persistentID);
            if (tracking == null || !unit.NetworkHQ.TryGetKnownPosition(target, out _))
            {
                reason = "target is not known to this faction";
                return false;
            }
            if (!HasCompatibleWeapon(unit, tracking, requireRange: false))
            {
                reason = "no compatible weapon";
                return false;
            }
            reason = null;
            return true;
        }

        private static bool HasKnownEngageableThreat(Unit unit)
        {
            if (unit?.NetworkHQ == null || unit.NetworkHQ.trackingDatabase == null) return false;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in unit.NetworkHQ.trackingDatabase)
            {
                TrackingInfo tracking = pair.Value;
                if (tracking == null || !tracking.TryGetUnit(out Unit target) || !IsUsable(target) || target.NetworkHQ == unit.NetworkHQ)
                    continue;
                if (HasCompatibleWeapon(unit, tracking, requireRange: true)) return true;
            }
            return false;
        }

        private static bool HasCompatibleWeapon(Unit unit, TrackingInfo tracking, bool requireRange)
        {
            if (unit.weaponStations == null) return false;
            float distance = FastMath.Distance(unit.GlobalPosition(), tracking.GetPosition());
            for (int i = 0; i < unit.weaponStations.Count; i++)
            {
                WeaponStation station = unit.weaponStations[i];
                if (station == null || station.WeaponInfo == null || station.Ammo <= 0) continue;
                if (requireRange && distance > station.WeaponInfo.targetRequirements.maxRange * 1.2f) continue;
                if (CombatAI.AnalyzeTarget(station, unit, tracking, 0f, distance, 100f).opportunity > 0f) return true;
            }
            return false;
        }

        public static bool OverrideForcedTarget(Unit searcher, List<WeaponStation> stations, ref CombatAI.TargetSearchResults result)
        {
            HorusTacticalOrderService service = Active;
            if (service == null || searcher == null || !service.orders.TryGetValue(searcher, out ActiveOrder order) || order.Kind != HorusOrderKind.AttackTarget)
                return false;

            Unit target = order.Target;
            TrackingInfo tracking = target != null && searcher.NetworkHQ != null ? searcher.NetworkHQ.GetTrackingData(target.persistentID) : null;
            WeaponStation bestStation = null;
            float bestOpportunity = 0f;
            if (tracking != null && stations != null && searcher.NetworkHQ.IsTargetPositionAccurate(target, 1000f))
            {
                float distance = FastMath.Distance(searcher.GlobalPosition(), tracking.GetPosition());
                for (int i = 0; i < stations.Count; i++)
                {
                    WeaponStation station = stations[i];
                    if (station == null || station.WeaponInfo == null || station.Ammo <= 0) continue;
                    float opportunity = CombatAI.AnalyzeTarget(station, searcher, tracking, 0f, distance, 100f).opportunity;
                    if (opportunity > bestOpportunity)
                    {
                        bestOpportunity = opportunity;
                        bestStation = station;
                    }
                }
            }
            result = new CombatAI.TargetSearchResults(bestStation != null ? target : null, bestStation, bestOpportunity, result.outOfAmmo);
            return true;
        }

        public static bool IsFireSuppressed(Unit owner)
        {
            HorusTacticalOrderService service = Active;
            if (service == null || !IsUsable(owner)) return false;
            if (owner is Aircraft aircraft && aircraft.Player != null) return false;
            return service.rules.TryGetValue(owner, out HorusRulesOfEngagement value) && value == HorusRulesOfEngagement.HoldFire;
        }

        private static bool IsUsable(Unit unit)
        {
            return unit != null && unit.gameObject != null && !unit.disabled && unit.unitState != Unit.UnitState.Destroyed;
        }
    }
}
