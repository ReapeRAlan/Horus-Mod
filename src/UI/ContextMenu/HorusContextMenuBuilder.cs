using System.Collections.Generic;
using HorusMod.Core;
using HorusMod.Interaction;
using HorusMod.Networking;
using HorusMod.Placement;
using HorusMod.Loadouts;
using HorusMod.Data;

namespace HorusMod.UI.ContextMenu
{
    public static class HorusContextMenuBuilder
    {
        public static List<ContextMenuItem> BuildForUnits(
            HorusManager manager,
            HorusSelection selection,
            HorusOrders orders,
            WorldPick pick)
        {
            var units = new List<Unit>();
            bool targetContext = pick.Unit != null && selection.HasSelection && !selection.Contains(pick.Unit);
            if (targetContext)
                units.AddRange(selection.Units);
            else if (pick.Unit != null && selection.Contains(pick.Unit))
                units.AddRange(selection.Units);
            else if (pick.Unit != null)
                units.Add(pick.Unit);
            else
                units.AddRange(selection.Units);

            bool allowed = HorusPermissions.CanRequestMutation();
            bool movementCompatible = false;
            for (int i = 0; i < units.Count; i++)
                if (HorusOrders.CanCommandUnit(units[i], out _)) { movementCompatible = true; break; }
            bool containsLiveOrdnance = units.Exists(unit => unit != null &&
                (unit.definition is MissileDefinition || HorusManager.FindCatalogEntry(unit.definition)?.IsLiveOrdnance == true));
            string denied = allowed ? null : "Host only";
            var items = new List<ContextMenuItem>
            {
                ContextMenuItem.Header(targetContext
                    ? $"{units.Count} unit(s) -> {pick.Unit.unitName}"
                    : units.Count == 1 ? units[0].unitName : $"{units.Count} selected units")
            };

            if (targetContext)
            {
                bool friendly = false;
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null && units[i].NetworkHQ != null && pick.Unit.NetworkHQ == units[i].NetworkHQ) friendly = true;
                if (friendly)
                {
                    bool canGuard = orders.CanGuardTarget(units, pick.Unit, out string guardReason);
                    items.Add(Gated("Guard / Escort", "", allowed && canGuard, allowed ? guardReason : denied,
                        () => orders.IssueGuard(units, pick.Unit)));
                }
                else
                {
                    bool canAttack = orders.CanAttackTarget(units, pick.Unit, out string attackReason);
                    items.Add(Gated("Attack Target", "", allowed && canAttack, allowed ? attackReason : denied,
                        () => orders.IssueAttackTarget(units, pick.Unit)));
                }
                items.Add(ContextMenuItem.Sep());
            }

            if (!targetContext && movementCompatible && manager.InputRouter != null)
            {
                items.Add(Gated("Move...", "", allowed, denied,
                    () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.Move, units)));
                items.Add(Gated("Attack-Move...", "", allowed, denied,
                    () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.AttackMove, units)));
                items.Add(Gated("Patrol...", "", allowed, denied,
                    () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.Patrol, units)));
                items.Add(Gated("Attack Target...", "", allowed, denied,
                    () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.AttackTarget, units)));
                items.Add(Gated("Guard / Escort...", "", allowed, denied,
                    () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.Guard, units)));
                items.Add(ContextMenuItem.Sep());
            }
            if (movementCompatible)
                items.Add(Gated("Hold Position", "H", allowed, denied, () => orders.SetHold(units, true)));
            items.Add(Gated("Clear Orders", "", allowed, denied, () => orders.ClearOrders(units)));
            items.Add(new ContextMenuItem
            {
                Label = "Rules of Engagement",
                Enabled = allowed,
                DisabledReason = denied,
                Submenu = BuildRules(orders, units, allowed, denied)
            });
            items.Add(ContextMenuItem.Sep());

            List<ContextMenuItem> loadouts = BuildLoadouts(units, allowed, denied);
            if (loadouts.Count > 0) items.Add(new ContextMenuItem { Label = "Change Loadout", Submenu = loadouts, Enabled = allowed, DisabledReason = denied });
            List<ContextMenuItem> liveries = BuildLiveries(units, allowed, denied);
            if (liveries.Count > 0) items.Add(new ContextMenuItem { Label = "Change Livery", Submenu = liveries, Enabled = allowed, DisabledReason = denied });
            items.Add(new ContextMenuItem
            {
                Label = "Pilot Skill",
                Enabled = allowed,
                DisabledReason = denied,
                Submenu = BuildSkills(units, allowed, denied)
            });
            items.Add(ContextMenuItem.Sep());
            items.Add(new ContextMenuItem { Label = "Focus Camera", Shortcut = "F", OnClick = manager.FocusSelection });
            items.Add(Gated("Duplicate", "Ctrl+D", allowed && !containsLiveOrdnance,
                containsLiveOrdnance ? "Live ordnance cannot be duplicated" : denied, manager.DuplicateSelection));
            items.Add(ContextMenuItem.Sep());
            ContextMenuItem delete = Gated("Delete", "Del", HorusPermissions.CanRequestDelete(), "Host or dedicated GM only", manager.DeleteSelection);
            delete.IsDanger = true;
            items.Add(delete);
            return items;
        }

        public static List<ContextMenuItem> BuildForWorld(HorusManager manager, HorusSelection selection, HorusOrders orders, WorldPick pick)
        {
            var items = new List<ContextMenuItem>();
            var units = new List<Unit>(selection.Units);
            if (selection.HasSelection && pick.Valid)
            {
                bool allowed = HorusPermissions.CanRequestMutation();
                bool movementCompatible = false;
                for (int i = 0; i < units.Count; i++)
                    if (HorusOrders.CanCommandUnit(units[i], out _)) { movementCompatible = true; break; }
                GlobalPosition destination = pick.Point.ToGlobalPosition();
                if (movementCompatible)
                {
                    items.Add(new ContextMenuItem { Label = "Move Selection Here", Shortcut = "RMB", Enabled = allowed, DisabledReason = "Host only", OnClick = () => orders.IssueMove(units, destination, manager.CurrentFormation) });
                    items.Add(new ContextMenuItem { Label = "Attack-Move", Enabled = allowed, DisabledReason = "Host only", OnClick = () => orders.IssueAttackMove(units, destination) });
                    items.Add(new ContextMenuItem { Label = "Create Patrol Route...", Enabled = allowed, DisabledReason = "Host only", OnClick = () => manager.InputRouter.BeginPatrolRoute(units, destination) });
                    items.Add(ContextMenuItem.Sep());
                }
            }
            items.Add(new ContextMenuItem { Label = "Cancel Tool", Shortcut = "Esc", OnClick = manager.CancelPlacement });
            return items;
        }

        public static List<ContextMenuItem> BuildFallbackForSelection(
            HorusManager manager,
            HorusSelection selection,
            HorusOrders orders)
        {
            var units = selection != null ? new List<Unit>(selection.Units) : new List<Unit>();
            bool allowed = HorusPermissions.CanRequestMutation();
            string denied = allowed ? null : "Host only";
            var items = new List<ContextMenuItem>
            {
                ContextMenuItem.Header(units.Count == 1 && units[0] != null
                    ? units[0].unitName
                    : $"{units.Count} selected units"),
                Gated("Move...", "", allowed, denied, () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.Move, units)),
                Gated("Attack-Move...", "", allowed, denied, () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.AttackMove, units)),
                Gated("Patrol...", "", allowed, denied, () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.Patrol, units)),
                Gated("Attack Target...", "", allowed, denied, () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.AttackTarget, units)),
                Gated("Guard / Escort...", "", allowed, denied, () => manager.InputRouter.BeginGroupOrderTargeting(HorusGroupOrderTargetMode.Guard, units)),
                ContextMenuItem.Sep(),
                Gated("Hold Position", "H", allowed, denied, () => orders.SetHold(units, true)),
                Gated("Clear Orders", "", allowed, denied, () => orders.ClearOrders(units)),
                new ContextMenuItem
                {
                    Label = "Rules of Engagement",
                    Enabled = allowed,
                    DisabledReason = denied,
                    Submenu = BuildRules(orders, units, allowed, denied)
                },
                ContextMenuItem.Sep(),
                new ContextMenuItem { Label = "Focus Camera", Shortcut = "F", OnClick = manager.FocusSelection }
            };
            return items;
        }

        private static ContextMenuItem Gated(string label, string shortcut, bool enabled, string reason, System.Action action)
        {
            return new ContextMenuItem { Label = label, Shortcut = shortcut, Enabled = enabled, DisabledReason = reason, OnClick = action };
        }

        private static List<ContextMenuItem> BuildRules(HorusOrders orders, List<Unit> units, bool allowed, string denied)
        {
            bool allHold = units.Count > 0;
            for (int i = 0; i < units.Count; i++)
                if (orders.GetRules(units[i]) != HorusRulesOfEngagement.HoldFire) { allHold = false; break; }
            return new List<ContextMenuItem>
            {
                Gated((!allHold ? "[x] " : "") + "Weapons Free", "", allowed, denied, () => orders.SetRules(units, HorusRulesOfEngagement.WeaponsFree)),
                Gated((allHold ? "[x] " : "") + "Hold Fire", "", allowed, denied, () => orders.SetRules(units, HorusRulesOfEngagement.HoldFire))
            };
        }

        private static List<ContextMenuItem> BuildLoadouts(List<Unit> units, bool allowed, string denied)
        {
            var result = new List<ContextMenuItem>();
            if (units.Count == 0 || !(units[0] is Aircraft first)) return result;
            AircraftDefinition definition = first.definition as AircraftDefinition;
            if (definition == null) return result;
            for (int i = 1; i < units.Count; i++)
            {
                if (!(units[i] is Aircraft aircraft) || !ReferenceEquals(aircraft.definition, definition)) return result;
            }

            IReadOnlyList<LoadoutDraft> presets = HorusLoadoutService.GetValidStandardDrafts(definition, first.NetworkHQ);
            for (int i = 0; i < presets.Count; i++)
            {
                LoadoutDraft draft = presets[i].Clone();
                string label = string.IsNullOrEmpty(draft.Name) ? $"Preset {i + 1}" : draft.Name;
                result.Add(Gated(label, "", allowed, denied, () =>
                {
                    foreach (Unit unit in units)
                        HorusUnitEditor.TrySetLoadout((Aircraft)unit, draft);
                }));
            }
            return result;
        }

        private static List<ContextMenuItem> BuildLiveries(List<Unit> units, bool allowed, string denied)
        {
            var result = new List<ContextMenuItem>();
            if (units.Count == 0 || !(units[0] is Aircraft first)) return result;
            AircraftDefinition definition = first.definition as AircraftDefinition;
            if (definition == null) return result;
            var firstLiveries = (first.definition as AircraftDefinition)?.aircraftParameters?.liveries;
            if (firstLiveries == null || firstLiveries.Count == 0) return result;

            int commonLiveryCount = firstLiveries.Count;
            for (int i = 1; i < units.Count; i++)
            {
                if (!(units[i] is Aircraft aircraft) || !ReferenceEquals(aircraft.definition, definition)) return result;
                var liveries = (aircraft.definition as AircraftDefinition)?.aircraftParameters?.liveries;
                if (liveries == null || liveries.Count == 0) return result;
                commonLiveryCount = System.Math.Min(commonLiveryCount, liveries.Count);
            }

            for (int i = 0; i < commonLiveryCount; i++)
            {
                int index = i;
                string label = string.IsNullOrEmpty(firstLiveries[i].name) ? $"Livery {i + 1}" : firstLiveries[i].name;
                result.Add(Gated(label, "", allowed, denied, () =>
                {
                    foreach (Unit unit in units)
                        HorusUnitEditor.TrySetLivery((Aircraft)unit, index);
                }));
            }
            return result;
        }

        private static List<ContextMenuItem> BuildSkills(List<Unit> units, bool allowed, string denied)
        {
            string[] names = { "Rookie", "Regular", "Veteran", "Ace" };
            float[] values = { 0.25f, 0.5f, 0.75f, 1f };
            var result = new List<ContextMenuItem>();
            for (int i = 0; i < names.Length; i++)
            {
                float skill = values[i];
                result.Add(Gated(names[i], "", allowed, denied, () =>
                {
                    foreach (Unit unit in units) HorusUnitEditor.SetSkill(unit, skill);
                }));
            }
            return result;
        }
    }
}
