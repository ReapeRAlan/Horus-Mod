using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.Interaction
{
    /// <summary>
    /// Server-side movement adapter for AI aircraft. Nuclear Option exposes
    /// UnitCommand for ground units and ships, but Aircraft is not ICommandable.
    /// This state delegates flight control to the aircraft's native autopilot.
    /// </summary>
    internal static class HorusAircraftOrders
    {
        private sealed class ActiveCommand
        {
            public Pilot Pilot;
            public PilotBaseState PreviousState;
            public HorusAircraftMoveState MoveState;
        }

        private sealed class HorusAircraftMoveState : PilotBaseState
        {
            private PilotBaseState previousState;
            private GlobalPosition commandedDestination;
            private float cruiseHeight;
            private readonly bool persistentHold;

            public GlobalPosition CommandedDestination => commandedDestination;
            public bool Completed { get; private set; }
            public bool PersistentHold => persistentHold;

            public HorusAircraftMoveState(Pilot pilot, PilotBaseState previousState, GlobalPosition target, bool persistentHold)
            {
                this.previousState = previousState;
                this.persistentHold = persistentHold;
                commandedDestination = target;
                Initialize(pilot);
                stateDisplayName = persistentHold ? "Horus hold" : "Horus move";
            }

            public void SetDestination(GlobalPosition target)
            {
                commandedDestination = target;
                Completed = false;
            }

            public void SetPreviousState(PilotBaseState state)
            {
                if (state != null && state != this) previousState = state;
            }

            public override void EnterState(Pilot value)
            {
                pilot = value;
                aircraft = value != null ? value.aircraft : null;
                if (aircraft == null) return;
                controlInputs = aircraft.GetInputs();
                bool plane = aircraft.autopilot is AutopilotPlane;
                cruiseHeight = plane
                    ? Mathf.Clamp(Mathf.Max(aircraft.radarAlt, 800f), 400f, 3000f)
                    : Mathf.Clamp(Mathf.Max(aircraft.radarAlt, 180f), 80f, 1000f);
                aircraft.SetFlightAssist(enabled: true);
            }

            public override void UpdateState(Pilot value)
            {
            }

            public override void FixedUpdateState(Pilot value)
            {
                if (aircraft == null || aircraft.disabled || aircraft.autopilot == null || aircraft.Player != null)
                {
                    if (value != null && value.currentState == this) value.SwitchState(previousState);
                    return;
                }

                Vector3 delta = commandedDestination - aircraft.GlobalPosition();
                Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
                Vector3 aimDirection = horizontal.sqrMagnitude > 1f ? horizontal.normalized : aircraft.transform.forward;

                if (!persistentHold)
                {
                    float arrivalRadius = aircraft.autopilot is AutopilotPlane
                        ? Mathf.Max(350f, aircraft.speed * 1.5f)
                        : Mathf.Max(90f, aircraft.definition != null ? aircraft.definition.length * 3f : 90f);
                    if (horizontal.magnitude <= arrivalRadius)
                    {
                        Completed = true;
                        if (value != null && value.currentState == this && previousState != null)
                            value.SwitchState(previousState);
                        return;
                    }
                }

                if (aircraft.autopilot is AutopilotPlane)
                {
                    aircraft.autopilot.AutoAim(
                        commandedDestination,
                        aimVelocity: true,
                        ignoreCollisions: false,
                        runwayAlign: false,
                        effort: 1f,
                        bankAllowed: 180f,
                        followTerrain: true,
                        altitudeHold: cruiseHeight,
                        targetVelocity: Vector3.zero);
                }
                else if (horizontal.magnitude < 150f)
                {
                    aircraft.autopilot.Hover(commandedDestination, cruiseHeight, aimDirection);
                }
                else
                {
                    aircraft.autopilot.AutoAim(commandedDestination, cruiseHeight, aimDirection, Vector3.zero, followTerrain: true);
                }
            }

            public override void LeaveState()
            {
            }
        }

        private static readonly Dictionary<Aircraft, ActiveCommand> active = new Dictionary<Aircraft, ActiveCommand>();

        public static bool CanCommand(Aircraft aircraft, out string reason)
        {
            if (aircraft == null || aircraft.gameObject == null || aircraft.disabled)
            {
                reason = "aircraft unavailable";
                return false;
            }
            if (!aircraft.IsServer)
            {
                reason = "aircraft orders must be issued by the host";
                return false;
            }
            if (aircraft.Player != null)
            {
                reason = "player-controlled aircraft are never overridden";
                return false;
            }
            if (aircraft.autopilot == null || aircraft.pilots == null || aircraft.pilots.Length == 0 ||
                aircraft.pilots[0] == null || aircraft.pilots[0].dead || aircraft.pilots[0].currentState == null)
            {
                reason = "aircraft autopilot is not ready";
                return false;
            }
            if (aircraft.autopilot is AutopilotPlane && aircraft.radarAlt < 2f)
            {
                reason = "landed planes must take off before receiving a move order";
                return false;
            }
            reason = null;
            return true;
        }

        public static bool TrySetDestination(Aircraft aircraft, GlobalPosition destination, out string reason)
        {
            return TrySetDestination(aircraft, destination, persistentHold: false, out reason);
        }

        private static bool TrySetDestination(Aircraft aircraft, GlobalPosition destination, bool persistentHold, out string reason)
        {
            Cleanup();
            if (!CanCommand(aircraft, out reason)) return false;

            Pilot pilot = aircraft.pilots[0];
            if (active.TryGetValue(aircraft, out ActiveCommand command))
            {
                if (command.MoveState.Completed || command.MoveState.PersistentHold != persistentHold)
                {
                    if (pilot.currentState == command.MoveState && command.PreviousState != null)
                        pilot.SwitchState(command.PreviousState);
                    active.Remove(aircraft);
                }
                else
                {
                    command.MoveState.SetDestination(destination);
                    if (pilot.currentState != command.MoveState)
                    {
                        command.PreviousState = pilot.currentState;
                        command.MoveState.SetPreviousState(pilot.currentState);
                        pilot.SwitchState(command.MoveState);
                    }
                    return true;
                }
            }

            PilotBaseState previous = pilot.currentState;
            var state = new HorusAircraftMoveState(pilot, previous, destination, persistentHold);
            active[aircraft] = new ActiveCommand
            {
                Pilot = pilot,
                PreviousState = previous,
                MoveState = state
            };
            pilot.SwitchState(state);
            return true;
        }

        public static GlobalPosition GetDestination(Aircraft aircraft)
        {
            Cleanup();
            return aircraft != null && active.TryGetValue(aircraft, out ActiveCommand command)
                ? command.MoveState.CommandedDestination
                : aircraft != null ? aircraft.GlobalPosition() : default;
        }

        public static bool Hold(Aircraft aircraft, out string reason)
        {
            return TrySetDestination(aircraft, aircraft != null ? aircraft.GlobalPosition() : default, persistentHold: true, out reason);
        }

        public static bool IsActive(Aircraft aircraft)
        {
            Cleanup();
            return aircraft != null && active.TryGetValue(aircraft, out ActiveCommand command) &&
                !command.MoveState.Completed && command.Pilot != null && command.Pilot.currentState == command.MoveState;
        }

        public static bool Clear(Aircraft aircraft)
        {
            Cleanup();
            if (aircraft == null || !active.TryGetValue(aircraft, out ActiveCommand command)) return false;
            active.Remove(aircraft);
            if (command.Pilot != null && command.Pilot.currentState == command.MoveState)
                command.Pilot.SwitchState(command.PreviousState);
            return true;
        }

        public static void Reset()
        {
            var aircraft = new List<Aircraft>(active.Keys);
            for (int i = 0; i < aircraft.Count; i++) Clear(aircraft[i]);
            active.Clear();
        }

        private static void Cleanup()
        {
            if (active.Count == 0) return;
            var stale = new List<Aircraft>();
            foreach (var pair in active)
            {
                Aircraft aircraft = pair.Key;
                ActiveCommand command = pair.Value;
                if (aircraft == null || aircraft.gameObject == null || aircraft.disabled ||
                    command?.Pilot == null || command.MoveState == null || command.MoveState.Completed)
                    stale.Add(aircraft);
            }
            for (int i = 0; i < stale.Count; i++) active.Remove(stale[i]);
        }
    }
}
