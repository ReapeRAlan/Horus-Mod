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
        public event Action<Vector3, IReadOnlyList<Unit>> OrderIssued;

        public HorusOrders(MonoBehaviour runner)
        {
            this.runner = runner;
        }

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
            for (int i = 0; i < source.Count; i++)
            {
                Unit unit = source[i];
                if (unit == null || unit.disabled || !(unit is ICommandable)) continue;
                ICommandable commandable = (ICommandable)unit;
                if (commandable.UnitCommand == null) continue;
                units.Add(unit);
                centroid += unit.transform.position;
                if (unit.definition != null) maxLength = Mathf.Max(maxLength, unit.definition.length);
            }
            if (units.Count == 0) yield break;

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
                ICommandable commandable = (ICommandable)units[i];
                UnitCommand command = commandable.UnitCommand;
                previous.Add(command.GetCommandCached().position);
                GlobalPosition destination = (targetLocal + rotation * offsets[i]).ToGlobalPosition();
                destinations.Add(destination);
                SetHold(units[i], false);
                command.SetDestination(destination, playerCommand: true);
                if ((i + 1) % 25 == 0) yield return null;
            }

            HorusUndo.RecordMove(units, previous, destinations);
            OrderIssued?.Invoke(targetLocal, units);
            HorusToasts.Show($"Move order: {units.Count} unit(s)");
            HorusLog.Verbose("Orders", $"Issued move order to {units.Count} unit(s).");
        }

        public void SetHold(IReadOnlyList<Unit> units, bool hold)
        {
            if (!HorusPermissions.CanSpawn() || units == null) return;
            for (int i = 0; i < units.Count; i++) SetHold(units[i], hold);
            HorusToasts.Show(hold ? $"Holding {units.Count} unit(s)" : $"Released {units.Count} unit(s)");
        }

        public void ClearOrders(IReadOnlyList<Unit> units)
        {
            if (!HorusPermissions.CanSpawn() || units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit == null || !(unit is ICommandable commandable) || commandable.UnitCommand == null) continue;
                SetHold(unit, false);
                commandable.UnitCommand.SetDestination(unit.GlobalPosition(), playerCommand: false);
            }
            HorusToasts.Show($"Orders cleared: {units.Count} unit(s)");
        }

        public static void SetHold(Unit unit, bool hold)
        {
            if (unit is GroundVehicle vehicle) vehicle.SetHoldPosition(hold);
            else if (unit is Ship ship) ship.SetHoldPosition(hold);
        }
    }
}
