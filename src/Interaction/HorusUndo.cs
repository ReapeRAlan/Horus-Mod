using System.Collections.Generic;
using HorusMod.Networking;
using Mirage;
using NuclearOption.SavedMission;
using UnityEngine;
using HorusMod.UI;

namespace HorusMod.Interaction
{
    public static class HorusUndo
    {
        private interface IAction
        {
            void Undo();
            void Redo();
        }

        private sealed class MoveAction : IAction
        {
            private readonly List<Unit> units;
            private readonly List<GlobalPosition> before;
            private readonly List<GlobalPosition> after;

            public MoveAction(List<Unit> units, List<GlobalPosition> before, List<GlobalPosition> after)
            {
                this.units = units;
                this.before = before;
                this.after = after;
            }

            public void Undo() => Apply(before);
            public void Redo() => Apply(after);

            private void Apply(List<GlobalPosition> destinations)
            {
                if (!HorusPermissions.CanSpawn()) return;
                for (int i = 0; i < units.Count && i < destinations.Count; i++)
                {
                    Unit unit = units[i];
                    if (unit is ICommandable commandable && commandable.UnitCommand != null)
                        commandable.UnitCommand.SetDestination(destinations[i], playerCommand: false);
                }
            }
        }

        private sealed class SpawnDeleteAction : IAction
        {
            private readonly UnitDefinition definition;
            private readonly GlobalPosition position;
            private readonly Quaternion rotation;
            private readonly FactionHQ hq;
            private readonly bool undoDeletes;
            private readonly bool wasAircraft;
            private readonly bool wasVehicle;
            private readonly bool wasShip;
            private readonly Loadout aircraftLoadout;
            private readonly LiveryKey aircraftLivery;
            private readonly float skill;
            private readonly float bravery;
            private readonly int factionIndex;
            private Unit unit;

            public SpawnDeleteAction(Unit unit, bool undoDeletes)
            {
                this.unit = unit;
                definition = unit != null ? unit.definition : null;
                position = unit != null ? unit.GlobalPosition() : default;
                rotation = unit != null ? unit.transform.rotation : Quaternion.identity;
                hq = unit != null ? unit.NetworkHQ : null;
                this.undoDeletes = undoDeletes;
                wasAircraft = unit is Aircraft;
                wasVehicle = unit is GroundVehicle;
                wasShip = unit is Ship;
                if (unit is Aircraft aircraft)
                {
                    aircraftLoadout = aircraft.Networkloadout;
                    aircraftLivery = aircraft.NetworkLiveryKey;
                    skill = aircraft.skill;
                    bravery = aircraft.bravery;
                }
                else if (unit is GroundVehicle vehicle)
                {
                    skill = vehicle.skill;
                }
                else if (unit is Ship ship)
                {
                    skill = ship.skill;
                }
                factionIndex = FactionRegistry.factions != null && hq?.faction != null
                    ? FactionRegistry.factions.IndexOf(hq.faction)
                    : -1;
                if (factionIndex < 0) factionIndex = FactionRegistry.factions?.Count ?? 0;
            }

            public void Undo()
            {
                if (undoDeletes) Delete();
                else Spawn();
            }

            public void Redo()
            {
                if (undoDeletes) Spawn();
                else Delete();
            }

            private void Spawn()
            {
                if (!HorusPermissions.CanSpawn() || definition == null || Spawner.i == null) return;
                if (wasShip && HorusMod.Core.HorusManager.Instance != null)
                    unit = HorusMod.Core.HorusManager.Instance.SpawnShipSafe(definition, position, rotation.eulerAngles.y, factionIndex);
                else
                    unit = Spawner.i.SpawnFromUnitDefinitionInEditor(
                        definition,
                        position,
                        rotation,
                        hq,
                        (definition.jsonKey ?? "horus") + "_redo_" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
                if (unit == null) return;
                HorusMod.Core.HorusManager.Instance?.AddHorusSpawnedUnit(unit);
                if (wasAircraft && unit is Aircraft aircraft)
                {
                    aircraft.Networkloadout = aircraftLoadout;
                    aircraft.SetLiveryKey(aircraftLivery, true);
                    aircraft.skill = skill;
                    aircraft.bravery = bravery;
                }
                else if (wasVehicle && unit is GroundVehicle vehicle)
                {
                    vehicle.skill = skill;
                }
                else if (wasShip && unit is Ship ship)
                {
                    ship.skill = skill;
                }
            }

            private void Delete()
            {
                if (!HorusPermissions.CanDelete() || unit == null) return;
                if (HorusPermissions.IsMultiplayer()) NetworkServer.Destroy(unit.gameObject);
                else Object.Destroy(unit.gameObject);
            }
        }

        private static readonly Stack<IAction> undo = new Stack<IAction>();
        private static readonly Stack<IAction> redo = new Stack<IAction>();
        private const int Capacity = 50;

        public static int UndoCount => undo.Count;
        public static int RedoCount => redo.Count;

        public static void RecordMove(List<Unit> units, List<GlobalPosition> before, List<GlobalPosition> after)
        {
            Push(new MoveAction(new List<Unit>(units), new List<GlobalPosition>(before), new List<GlobalPosition>(after)));
        }

        public static void RecordSpawn(Unit unit)
        {
            if (unit != null) Push(new SpawnDeleteAction(unit, undoDeletes: true));
        }

        public static void RecordDelete(Unit unit)
        {
            if (unit != null) Push(new SpawnDeleteAction(unit, undoDeletes: false));
        }

        public static void Undo()
        {
            if (undo.Count == 0) return;
            IAction action = undo.Pop();
            action.Undo();
            redo.Push(action);
            HorusToasts.Show($"Undo · {undo.Count} remaining");
        }

        public static void Redo()
        {
            if (redo.Count == 0) return;
            IAction action = redo.Pop();
            action.Redo();
            undo.Push(action);
            HorusToasts.Show($"Redo · {redo.Count} remaining");
        }

        public static void Clear()
        {
            undo.Clear();
            redo.Clear();
        }

        private static void Push(IAction action)
        {
            if (action == null) return;
            if (undo.Count >= Capacity)
            {
                IAction[] actions = undo.ToArray();
                undo.Clear();
                for (int i = actions.Length - 2; i >= 0; i--) undo.Push(actions[i]);
            }
            undo.Push(action);
            redo.Clear();
        }
    }
}
