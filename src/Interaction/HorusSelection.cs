using System;
using System.Collections.Generic;
using UnityEngine;
using HorusMod.UI;

namespace HorusMod.Interaction
{
    public sealed class HorusSelection
    {
        private readonly List<Unit> selected = new List<Unit>();
        private readonly List<Unit>[] controlGroups = new List<Unit>[9];
        private Unit hover;

        public IReadOnlyList<Unit> Units => selected;
        public Unit Hover => hover;
        public int Count => selected.Count;
        public bool HasSelection => selected.Count > 0;

        public HorusSelection()
        {
            for (int i = 0; i < controlGroups.Length; i++) controlGroups[i] = new List<Unit>();
        }

        public void SetHover(Unit unit)
        {
            hover = IsUsable(unit) ? unit : null;
        }

        public bool Contains(Unit unit) => unit != null && selected.Contains(unit);

        public void Select(Unit unit, bool add, bool remove)
        {
            Purge();
            if (!add && !remove) selected.Clear();
            if (!IsUsable(unit))
            {
                SyncMap();
                return;
            }

            if (remove)
            {
                selected.Remove(unit);
            }
            else if (add)
            {
                if (selected.Contains(unit)) selected.Remove(unit);
                else selected.Add(unit);
            }
            else
            {
                selected.Add(unit);
            }
            SyncMap();
        }

        public void SelectMany(IEnumerable<Unit> units, bool add, bool remove)
        {
            Purge();
            if (!add && !remove) selected.Clear();
            if (units != null)
            {
                foreach (Unit unit in units)
                {
                    if (!IsUsable(unit)) continue;
                    if (remove) selected.Remove(unit);
                    else if (!selected.Contains(unit)) selected.Add(unit);
                }
            }
            SyncMap();
        }

        public void SelectDefinitionOnScreen(UnitDefinition definition, Camera cam)
        {
            selected.Clear();
            if (definition != null && cam != null && UnitRegistry.allUnits != null)
            {
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (!IsUsable(unit) || unit.definition != definition) continue;
                    Vector3 p = cam.WorldToScreenPoint(unit.transform.position);
                    if (p.z > 0f && p.x >= 0f && p.x <= Screen.width && p.y >= 0f && p.y <= Screen.height)
                        selected.Add(unit);
                }
            }
            SyncMap();
        }

        public void SelectInScreenRect(Rect rawScreenRect, bool add, bool remove, Camera cam)
        {
            if (cam == null || UnitRegistry.allUnits == null) return;
            var matches = new List<Unit>();
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!IsUsable(unit)) continue;
                Vector3 p = cam.WorldToScreenPoint(unit.transform.position);
                if (p.z > 0f && rawScreenRect.Contains(new Vector2(p.x, p.y))) matches.Add(unit);
            }
            SelectMany(matches, add, remove);
        }

        public void SelectAll(IEnumerable<Unit> units)
        {
            selected.Clear();
            if (units != null)
                foreach (Unit unit in units)
                    if (IsUsable(unit) && !selected.Contains(unit)) selected.Add(unit);
            SyncMap();
        }

        public void Clear()
        {
            selected.Clear();
            hover = null;
            SyncMap();
        }

        public void AssignControlGroup(int index)
        {
            if (index < 0 || index >= controlGroups.Length) return;
            controlGroups[index].Clear();
            Purge();
            controlGroups[index].AddRange(selected);
            HorusToasts.Show($"Control group {index + 1}: {selected.Count} unit(s)");
        }

        public void RecallControlGroup(int index)
        {
            if (index < 0 || index >= controlGroups.Length) return;
            SelectAll(controlGroups[index]);
            HorusToasts.Show($"Recalled group {index + 1}: {selected.Count} unit(s)");
        }

        public Vector3 Centroid()
        {
            Purge();
            if (selected.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (Unit unit in selected) sum += unit.transform.position;
            return sum / selected.Count;
        }

        public void Purge()
        {
            selected.RemoveAll(unit => !IsUsable(unit));
            if (!IsUsable(hover)) hover = null;
            for (int i = 0; i < controlGroups.Length; i++)
                controlGroups[i].RemoveAll(unit => !IsUsable(unit));
        }

        private static bool IsUsable(Unit unit)
        {
            return unit != null && unit.gameObject != null && !unit.disabled && unit.unitState != Unit.UnitState.Destroyed;
        }

        private void SyncMap()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            try
            {
                map.DeselectAllIcons();
                foreach (Unit unit in selected) map.SelectIcon(unit);
            }
            catch (Exception)
            {
                // Map services can disappear during scene transitions.
            }
        }
    }
}
