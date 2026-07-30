using System.Collections.Generic;
using HorusMod.Core;
using HorusMod.Interaction;
using HorusMod.Networking;
using HorusMod.Placement;

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
            if (pick.Unit != null && selection.Contains(pick.Unit))
                units.AddRange(selection.Units);
            else if (pick.Unit != null)
                units.Add(pick.Unit);
            else
                units.AddRange(selection.Units);

            bool allowed = HorusPermissions.CanSpawn();
            string denied = allowed ? null : "Solo host";
            var items = new List<ContextMenuItem>
            {
                ContextMenuItem.Header(units.Count == 1 ? units[0].unitName : $"{units.Count} unidades seleccionadas")
            };

            if (pick.Valid)
            {
                items.Add(Gated("Mover aqui", "RMB", allowed, denied,
                    () => orders.IssueMove(units, pick.Point.ToGlobalPosition(), manager.CurrentFormation)));
            }
            items.Add(Gated("Mantener posicion", "H", allowed, denied, () => orders.SetHold(units, true)));
            items.Add(Gated("Limpiar ordenes", "", allowed, denied, () => orders.ClearOrders(units)));
            items.Add(ContextMenuItem.Sep());

            List<ContextMenuItem> loadouts = BuildLoadouts(units, allowed, denied);
            if (loadouts.Count > 0) items.Add(new ContextMenuItem { Label = "Cambiar loadout", Submenu = loadouts, Enabled = allowed, DisabledReason = denied });
            List<ContextMenuItem> liveries = BuildLiveries(units, allowed, denied);
            if (liveries.Count > 0) items.Add(new ContextMenuItem { Label = "Cambiar livery", Submenu = liveries, Enabled = allowed, DisabledReason = denied });
            items.Add(new ContextMenuItem
            {
                Label = "Nivel de piloto",
                Enabled = allowed,
                DisabledReason = denied,
                Submenu = BuildSkills(units, allowed, denied)
            });
            items.Add(ContextMenuItem.Sep());
            items.Add(new ContextMenuItem { Label = "Enfocar camara", Shortcut = "F", OnClick = manager.FocusSelection });
            items.Add(Gated("Duplicar", "Ctrl+D", allowed, denied, manager.DuplicateSelection));
            items.Add(ContextMenuItem.Sep());
            ContextMenuItem delete = Gated("Borrar", "Del", HorusPermissions.CanDelete(), "Solo host", manager.DeleteSelection);
            delete.IsDanger = true;
            items.Add(delete);
            return items;
        }

        public static List<ContextMenuItem> BuildForWorld(HorusManager manager, HorusSelection selection, HorusOrders orders, WorldPick pick)
        {
            var items = new List<ContextMenuItem>();
            if (selection.HasSelection && pick.Valid)
                items.Add(new ContextMenuItem { Label = "Mover seleccion aqui", Shortcut = "RMB", Enabled = HorusPermissions.CanSpawn(), DisabledReason = "Solo host", OnClick = () => orders.IssueMove(selection.Units, pick.Point.ToGlobalPosition(), manager.CurrentFormation) });
            items.Add(new ContextMenuItem { Label = "Cancelar herramienta", Shortcut = "Esc", OnClick = manager.CancelPlacement });
            return items;
        }

        private static ContextMenuItem Gated(string label, string shortcut, bool enabled, string reason, System.Action action)
        {
            return new ContextMenuItem { Label = label, Shortcut = shortcut, Enabled = enabled, DisabledReason = reason, OnClick = action };
        }

        private static List<ContextMenuItem> BuildLoadouts(List<Unit> units, bool allowed, string denied)
        {
            var result = new List<ContextMenuItem>();
            if (units.Count == 0 || !(units[0] is Aircraft first)) return result;
            StandardLoadout[] firstPresets = (first.definition as AircraftDefinition)?.aircraftParameters?.StandardLoadouts;
            if (firstPresets == null || firstPresets.Length == 0) return result;

            int commonPresetCount = firstPresets.Length;
            for (int i = 1; i < units.Count; i++)
            {
                if (!(units[i] is Aircraft aircraft)) return result;
                StandardLoadout[] presets = (aircraft.definition as AircraftDefinition)?.aircraftParameters?.StandardLoadouts;
                if (presets == null || presets.Length == 0) return result;
                commonPresetCount = System.Math.Min(commonPresetCount, presets.Length);
            }

            for (int i = 0; i < commonPresetCount; i++)
            {
                int index = i;
                string label = string.IsNullOrEmpty(firstPresets[i]?.Name) ? $"Preset {i + 1}" : firstPresets[i].Name;
                result.Add(Gated(label, "", allowed, denied, () =>
                {
                    foreach (Unit unit in units)
                        HorusUnitEditor.TrySetLoadout((Aircraft)unit, index);
                }));
            }
            return result;
        }

        private static List<ContextMenuItem> BuildLiveries(List<Unit> units, bool allowed, string denied)
        {
            var result = new List<ContextMenuItem>();
            if (units.Count == 0 || !(units[0] is Aircraft first)) return result;
            var firstLiveries = (first.definition as AircraftDefinition)?.aircraftParameters?.liveries;
            if (firstLiveries == null || firstLiveries.Count == 0) return result;

            int commonLiveryCount = firstLiveries.Count;
            for (int i = 1; i < units.Count; i++)
            {
                if (!(units[i] is Aircraft aircraft)) return result;
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
