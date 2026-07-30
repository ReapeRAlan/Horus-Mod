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
            private readonly PilotBaseState previousState;
            private GlobalPosition commandedDestination;
            private float cruiseHeight;

            public GlobalPosition CommandedDestination => commandedDestination;

            public HorusAircraftMoveState(Pilot pilot, PilotBaseState previousState, GlobalPosition target)
            {
                this.previousState = previousState;
                commandedDestination = target;
                Initialize(pilot);
                stateDisplayName = "Horus move";
            }

            public void SetDestination(GlobalPosition target)
            {
                commandedDestination = target;
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
                aircraft.pilots[0] == null || aircraft.pilots[0].dead)
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
            Cleanup();
            if (!CanCommand(aircraft, out reason)) return false;

            Pilot pilot = aircraft.pilots[0];
            if (active.TryGetValue(aircraft, out ActiveCommand command))
            {
                command.MoveState.SetDestination(destination);
                if (pilot.currentState != command.MoveState)
                {
                    command.PreviousState = pilot.currentState;
                    pilot.SwitchState(command.MoveState);
                }
                return true;
            }

            PilotBaseState previous = pilot.currentState;
            var state = new HorusAircraftMoveState(pilot, previous, destination);
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
            return TrySetDestination(aircraft, aircraft != null ? aircraft.GlobalPosition() : default, out reason);
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

        private static void Cleanup()
        {
            if (active.Count == 0) return;
            var stale = new List<Aircraft>();
            foreach (var pair in active)
            {
                Aircraft aircraft = pair.Key;
                ActiveCommand command = pair.Value;
                if (aircraft == null || aircraft.gameObject == null || aircraft.disabled ||
                    command?.Pilot == null || command.MoveState == null)
                    stale.Add(aircraft);
            }
            for (int i = 0; i < stale.Count; i++) active.Remove(stale[i]);
        }
    }
}
