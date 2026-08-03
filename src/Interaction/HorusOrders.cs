using System;
using System.Collections;
using System.Collections.Generic;
using HorusMod.Logging;
using HorusMod.Networking;
using HorusMod.Placement;
using HorusMod.UI;
using UnityEngine;

namespace HorusMod.Interaction
{
    public sealed class HorusOrders
    {
        private readonly MonoBehaviour runner;
        private readonly HorusTacticalOrderService tactical;
        public event Action<Vector3, IReadOnlyList<Unit>> OrderIssued;

        public HorusOrders(MonoBehaviour runner)
        {
            this.runner = runner;
            tactical = new HorusTacticalOrderService();
        }

        public int ActiveTacticalOrderCount => tactical.ActiveOrderCount;

        public void Tick() => tactical.Tick();
        public void Reset() => tactical.Reset();
        public HorusOrderKind GetOrderKind(Unit unit) => tactical.GetOrderKind(unit);
        public HorusRulesOfEngagement GetRules(Unit unit) => tactical.GetRules(unit);

        public bool IssueMove(IReadOnlyList<Unit> units, GlobalPosition target, FormationKind formation)
        {
            if (!HorusPermissions.CanSpawn() || units == null || units.Count == 0) return false;
            runner.StartCoroutine(IssueMoveRoutine(units, target, formation));
            return true;
        }

        private IEnumerator IssueMoveRoutine(IReadOnlyList<Unit> source, GlobalPosition target, FormationKind formation)
        {
            var units = new List<Unit>(source.Count);
            float maxLength = 20f;
            Vector3 centroid = Vector3.zero;
            string firstSkipReason = null;
            for (int i = 0; i < source.Count; i++)
            {
                Unit unit = source[i];
                if (!CanCommand(unit, out string skipReason))
                {
                    if (string.IsNullOrEmpty(firstSkipReason)) firstSkipReason = skipReason;
                    continue;
                }
                units.Add(unit);
                centroid += unit.transform.position;
                if (unit.definition != null) maxLength = Mathf.Max(maxLength, unit.definition.length);
            }
            if (units.Count == 0)
            {
                HorusToasts.Show("Move order not sent: " + (firstSkipReason ?? "no commandable units"));
                yield break;
            }

            centroid /= units.Count;
            Vector3 targetLocal = target.ToLocalPosition();
            Vector3 heading = targetLocal - centroid;
            heading.y = 0f;
            Quaternion rotation = heading.sqrMagnitude > 0.01f ? Quaternion.LookRotation(heading.normalized, Vector3.up) : Quaternion.identity;
            float spacing = Mathf.Clamp(maxLength * 1.5f, 30f, 250f);
            List<Vector3> offsets = FormationSolver.GetOffsets(units.Count, spacing, formation);
            var destinations = new List<GlobalPosition>(units.Count);
            var previous = new List<GlobalPosition>(units.Count);

            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                previous.Add(GetCurrentDestination(unit));
                Vector3 destinationLocal = targetLocal + rotation * offsets[i];
                if (unit is Ship) destinationLocal.y = Datum.LocalSeaY;
                GlobalPosition destination = destinationLocal.ToGlobalPosition();
                destinations.Add(destination);
                SetHold(unit, false);
                if (TrySetDestination(unit, destination, playerCommand: true, out _))
                    tactical.RegisterMove(unit, destination);
                if ((i + 1) % 25 == 0) yield return null;
            }

            HorusUndo.RecordMove(units, previous, destinations);
            OrderIssued?.Invoke(targetLocal, units);
            HorusToasts.Show($"Move order: {units.Count} unit(s)");
            HorusLog.Verbose("Orders", $"Issued move order to {units.Count} unit(s).");
            int skipped = source.Count - units.Count;
            if (skipped > 0)
                HorusToasts.Show($"Move order: {units.Count}; skipped {skipped} unavailable/player-controlled unit(s)");
        }

        public void SetHold(IReadOnlyList<Unit> units, bool hold)
        {
            if (!HorusPermissions.CanSpawn() || units == null) return;
            int changed = 0;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (!SetHold(unit, hold)) continue;
                if (hold) tactical.RegisterHold(unit);
                else tactical.ClearOrder(unit, restoreAircraft: false);
                changed++;
            }
            HorusToasts.Show(hold ? $"Holding {changed} unit(s)" : $"Released {changed} unit(s)");
        }

        public void ClearOrders(IReadOnlyList<Unit> units)
        {
            if (!HorusPermissions.CanSpawn() || units == null) return;
            int cleared = 0;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                bool tacticalCleared = tactical.ClearOrder(unit);
                if (unit is Aircraft aircraft)
                {
                    if (tacticalCleared || HorusAircraftOrders.Clear(aircraft)) cleared++;
                    continue;
                }
                if (unit == null || !(unit is ICommandable commandable) || commandable.UnitCommand == null) continue;
                SetHold(unit, false);
                commandable.UnitCommand.SetDestination(unit.GlobalPosition(), playerCommand: false);
                cleared++;
            }
            HorusToasts.Show($"Orders cleared: {cleared} unit(s)");
        }

        public static bool SetHold(Unit unit, bool hold)
        {
            if (unit is GroundVehicle vehicle)
            {
                vehicle.SetHoldPosition(hold);
                return true;
            }
            if (unit is Ship ship)
            {
                ship.SetHoldPosition(hold);
                return true;
            }
            if (unit is Aircraft aircraft)
            {
                if (!hold) return HorusAircraftOrders.Clear(aircraft) || HorusAircraftOrders.CanCommand(aircraft, out _);
                return HorusAircraftOrders.Hold(aircraft, out _);
            }
            return false;
        }

        public static bool TrySetDestination(Unit unit, GlobalPosition destination, bool playerCommand, out string reason)
        {
            if (unit is Aircraft aircraft)
                return HorusAircraftOrders.TrySetDestination(aircraft, destination, out reason);
            if (unit is Ship)
            {
                Vector3 local = destination.ToLocalPosition();
                local.y = Datum.LocalSeaY;
                destination = local.ToGlobalPosition();
            }
            if (unit is ICommandable commandable && commandable.UnitCommand != null)
            {
                commandable.UnitCommand.SetDestination(destination, playerCommand);
                reason = null;
                return true;
            }
            reason = "unit has no movement controller";
            return false;
        }

        public static bool CanCommandUnit(Unit unit, out string reason)
        {
            if (unit == null || unit.gameObject == null || unit.disabled)
            {
                reason = "unit unavailable";
                return false;
            }
            if (unit is Aircraft aircraft)
                return HorusAircraftOrders.CanCommand(aircraft, out reason);
            if (unit is ICommandable commandable && commandable.UnitCommand != null)
            {
                reason = null;
                return true;
            }
            reason = "unit has no movement controller";
            return false;
        }

        private static bool CanCommand(Unit unit, out string reason) => CanCommandUnit(unit, out reason);

        public bool CanAttackTarget(IReadOnlyList<Unit> units, Unit target, out string reason)
        {
            reason = "no commandable unit knows this target";
            if (units == null || target == null) return false;
            for (int i = 0; i < units.Count; i++)
                if (HorusTacticalOrderService.CanAttackKnownTarget(units[i], target, out reason)) return true;
            return false;
        }

        public bool CanGuardTarget(IReadOnlyList<Unit> units, Unit target, out string reason)
        {
            reason = "guard target must be friendly";
            if (units == null || target == null) return false;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit != null && unit != target && unit.NetworkHQ != null && target.NetworkHQ == unit.NetworkHQ &&
                    CanCommandUnit(unit, out _))
                {
                    reason = null;
                    return true;
                }
            }
            return false;
        }

        public bool IsTacticalTarget(IReadOnlyList<Unit> units, Unit target)
        {
            if (units == null || target == null) return false;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit != null && unit != target && unit.NetworkHQ != null) return true;
            }
            return false;
        }

        public bool IssueAttackTarget(IReadOnlyList<Unit> units, Unit target)
        {
            if (!HorusPermissions.CanSpawn() || units == null || target == null) return false;
            int accepted = 0;
            string firstReason = null;
            for (int i = 0; i < units.Count; i++)
            {
                if (tactical.RegisterAttackTarget(units[i], target, out string reason)) accepted++;
                else if (firstReason == null) firstReason = reason;
            }
            HorusToasts.Show(accepted > 0 ? $"Attack target: {accepted} unit(s)" : "Attack rejected: " + (firstReason ?? "target unavailable"));
            return accepted > 0;
        }

        public bool IssueAttackMove(IReadOnlyList<Unit> units, GlobalPosition target)
        {
            if (!HorusPermissions.CanSpawn() || units == null) return false;
            int accepted = 0;
            string firstReason = null;
            for (int i = 0; i < units.Count; i++)
            {
                if (tactical.RegisterAttackMove(units[i], target, out string reason)) accepted++;
                else if (firstReason == null) firstReason = reason;
            }
            if (accepted > 0) OrderIssued?.Invoke(target.ToLocalPosition(), CopyAccepted(units));
            HorusToasts.Show(accepted > 0 ? $"Attack-move: {accepted} unit(s)" : "Attack-move rejected: " + (firstReason ?? "no commandable units"));
            return accepted > 0;
        }

        public bool IssuePatrol(IReadOnlyList<Unit> units, IReadOnlyList<GlobalPosition> waypoints)
        {
            if (!HorusPermissions.CanSpawn() || units == null || waypoints == null || waypoints.Count < 2) return false;
            int accepted = 0;
            string firstReason = null;
            for (int i = 0; i < units.Count; i++)
            {
                if (tactical.RegisterPatrol(units[i], waypoints, out string reason)) accepted++;
                else if (firstReason == null) firstReason = reason;
            }
            HorusToasts.Show(accepted > 0 ? $"Patrol route: {accepted} unit(s), {waypoints.Count} points" : "Patrol rejected: " + (firstReason ?? "no commandable units"));
            return accepted > 0;
        }

        public bool IssueGuard(IReadOnlyList<Unit> units, Unit target)
        {
            if (!HorusPermissions.CanSpawn() || units == null || target == null) return false;
            int accepted = 0;
            string firstReason = null;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                float spacing = unit is Aircraft ? 250f : unit is Ship ? 120f : 50f;
                int rank = i / 2 + 1;
                float lateral = (i % 2 == 0 ? -1f : 1f) * spacing * rank;
                Vector3 offset = new Vector3(lateral, 0f, -spacing * rank);
                if (tactical.RegisterGuard(unit, target, offset, out string reason)) accepted++;
                else if (firstReason == null) firstReason = reason;
            }
            HorusToasts.Show(accepted > 0 ? $"Guard/escort: {accepted} unit(s)" : "Guard rejected: " + (firstReason ?? "invalid friendly target"));
            return accepted > 0;
        }

        public void SetRules(IReadOnlyList<Unit> units, HorusRulesOfEngagement value)
        {
            if (!HorusPermissions.CanSpawn() || units == null) return;
            int changed = 0;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit == null || (unit is Aircraft aircraft && aircraft.Player != null)) continue;
                tactical.SetRules(unit, value);
                changed++;
            }
            HorusToasts.Show($"ROE {value}: {changed} unit(s)");
        }

        private static IReadOnlyList<Unit> CopyAccepted(IReadOnlyList<Unit> units)
        {
            var copy = new List<Unit>();
            if (units != null)
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null) copy.Add(units[i]);
            return copy;
        }

        private static GlobalPosition GetCurrentDestination(Unit unit)
        {
            if (unit is Aircraft aircraft) return HorusAircraftOrders.GetDestination(aircraft);
            if (unit is ICommandable commandable && commandable.UnitCommand != null)
            {
                UnitCommand.Command command = commandable.UnitCommand.GetCommandCached();
                return command.time > 0f ? command.position : unit.GlobalPosition();
            }
            return unit != null ? unit.GlobalPosition() : default;
        }
    }
}
