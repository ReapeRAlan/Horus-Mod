using System;
using System.Collections.Generic;
using HorusMod.Core;
using HorusMod.Data;
using UnityEngine;

namespace HorusMod.UI
{
    public static class UnitBrowser
    {
        private const float RowHeight = 34f;
        private static string search = "";
        private static UnitKind kind = UnitKind.Aircraft;
        private static UnitRole roles;
        private static CatalogFlags capabilityFilter;
        private static bool favoritesOnly;
        private static Vector2 scroll;
        private static UnitEntry selected;
        private static int observedCatalogRevision = -1;
        private static UnitDefinition pendingSelectionDefinition;
        private static bool pendingSelection;
        private static bool pendingRefresh;
        private static string pendingFavoriteKey;

        public static void Reset()
        {
            selected = null;
            scroll = Vector2.zero;
            pendingSelectionDefinition = null;
            pendingSelection = false;
            pendingRefresh = false;
            pendingFavoriteKey = null;
        }

        public static void Draw(HorusManager manager)
        {
            bool layout = Event.current.type == EventType.Layout;
            if (layout && pendingRefresh)
            {
                pendingRefresh = false;
                UnitCatalog.Refresh(MissionManager.AllowEventContent);
            }
            else
            {
                UnitCatalog.EnsureBuilt(MissionManager.AllowEventContent);
            }
            if (layout && !string.IsNullOrEmpty(pendingFavoriteKey))
            {
                HorusPrefs.ToggleFavorite(pendingFavoriteKey);
                pendingFavoriteKey = null;
            }
            if (layout && observedCatalogRevision != UnitCatalog.Revision)
            {
                observedCatalogRevision = UnitCatalog.Revision;
                if (selected != null)
                    selected = UnitCatalog.Find(selected.Key);
            }
            if (layout && pendingSelection)
            {
                pendingSelection = false;
                selected = UnitCatalog.FindByDefinition(pendingSelectionDefinition);
                pendingSelectionDefinition = null;
            }

            DrawRecents(manager);

            GUILayout.BeginHorizontal();
            search = GUILayout.TextField(search, HorusTheme.SearchField, GUILayout.Height(27f));
            if (HorusWidgets.Ghost("×", GUILayout.Width(28f), GUILayout.Height(27f))) search = "";
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawKind(UnitKind.Aircraft, "AIR");
            DrawKind(UnitKind.Ground, "GND");
            DrawKind(UnitKind.Sea, "SEA");
            DrawKind(UnitKind.Building, "BLD");
            DrawKind(UnitKind.Scenery, "SCN");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawKind(UnitKind.Missile, "ORD");
            DrawKind(UnitKind.Other, "PROP");
            DrawKind(UnitKind.All, "ALL");
            if (HorusWidgets.Ghost("Refresh", GUILayout.Width(66f)))
            {
                pendingRefresh = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawRole(UnitRole.AntiSurface, "A-S");
            DrawRole(UnitRole.AntiAir, "A-A");
            DrawRole(UnitRole.AntiMissile, "A-M");
            DrawRole(UnitRole.AntiRadar, "A-R");
            DrawRole(UnitRole.Radar, "RDR");
            if (HorusWidgets.Chip("★", favoritesOnly, GUILayout.Width(36f))) favoritesOnly = !favoritesOnly;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawCapability(CatalogFlags.Logistics, "LOG");
            DrawCapability(CatalogFlags.Ammo, "AMMO");
            DrawCapability(CatalogFlags.NavalResupply, "NAV");
            DrawCapability(CatalogFlags.Fuel, "FUEL");
            DrawCapability(CatalogFlags.Storage, "STORE");
            GUILayout.EndHorizontal();

            IReadOnlyList<UnitEntry> list = UnitCatalog.Query(kind, roles, capabilityFilter, search, favoritesOnly);
            GUILayout.Label($"{list.Count} definitions · catalog r{UnitCatalog.Revision}", HorusTheme.LabelMuted);
            Rect viewport = GUILayoutUtility.GetRect(1f, 225f, GUILayout.ExpandWidth(true));
            float contentHeight = Mathf.Max(viewport.height, list.Count * RowHeight);
            Rect content = new Rect(0f, 0f, Mathf.Max(1f, viewport.width - 18f), contentHeight);
            scroll = GUI.BeginScrollView(viewport, scroll, content);
            int first = Mathf.Max(0, (int)(scroll.y / RowHeight) - 1);
            int visible = Mathf.CeilToInt(viewport.height / RowHeight) + 2;
            int end = Mathf.Min(list.Count, first + visible);
            for (int i = first; i < end; i++) DrawRow(manager, list[i], i, content.width);
            GUI.EndScrollView();

            if (layout && selected == null && manager.ArmedDefinition != null)
                selected = UnitCatalog.FindByDefinition(manager.ArmedDefinition);
            DrawDetails(selected);
        }

        private static void DrawRecents(HorusManager manager)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label("Recent", HorusTheme.LabelMuted, GUILayout.Width(55f));
            foreach (string key in HorusPrefs.Recents)
            {
                UnitEntry entry = UnitCatalog.Find(key);
                if (entry == null) continue;
                if (HorusWidgets.Ghost(HorusWidgets.Ellipsize(entry.Display, HorusTheme.ButtonGhost, 66f), GUILayout.Width(70f), GUILayout.Height(24f)))
                {
                    QueueSelection(manager, entry.Def);
                }
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawKind(UnitKind value, string label)
        {
            if (HorusWidgets.Chip(label, kind == value, GUILayout.ExpandWidth(true))) kind = value;
        }

        private static void DrawRole(UnitRole value, string label)
        {
            bool active = (roles & value) != 0;
            if (HorusWidgets.Chip(label, active, GUILayout.ExpandWidth(true)))
                roles = active ? roles & ~value : roles | value;
        }

        private static void DrawCapability(CatalogFlags value, string label)
        {
            bool active = (capabilityFilter & value) != 0;
            if (HorusWidgets.Chip(label, active, GUILayout.ExpandWidth(true)))
                capabilityFilter = active ? capabilityFilter & ~value : capabilityFilter | value;
        }

        private static IReadOnlyList<UnitEntry> ApplyCapabilityFilter(IReadOnlyList<UnitEntry> source)
        {
            if (capabilityFilter == CatalogFlags.None) return source;
            var result = new List<UnitEntry>();
            for (int i = 0; i < source.Count; i++)
                if ((source[i].Flags & capabilityFilter) == capabilityFilter) result.Add(source[i]);
            return result;
        }

        private static void DrawRow(HorusManager manager, UnitEntry entry, int index, float width)
        {
            Rect row = new Rect(0f, index * RowHeight, width, RowHeight - 2f);
            bool active = selected == entry || manager.ArmedDefinition == entry.Def;
            if (GUI.Button(row, GUIContent.none, active ? HorusTheme.ListRowSelected : HorusTheme.ListRow))
            {
                QueueSelection(manager, entry.Def);
            }
            HorusWidgets.SpriteImage(new Rect(row.x + 5f, row.y + 4f, 24f, 24f), entry.Icon, Color.white);
            GUI.Label(new Rect(row.x + 35f, row.y, row.width - 125f, row.height), HorusWidgets.Ellipsize(entry.Display, HorusTheme.Label, row.width - 135f), HorusTheme.Label);
            GUI.Label(new Rect(row.xMax - 85f, row.y, 54f, row.height), $"${entry.Cost:N0}", HorusTheme.ValueRight);
            if (GUI.Button(new Rect(row.xMax - 28f, row.y + 3f, 25f, 25f), HorusPrefs.IsFavorite(entry.Key) ? "★" : "☆", HorusTheme.IconButton))
                pendingFavoriteKey = entry.Key;
        }

        private static void QueueSelection(HorusManager manager, UnitDefinition definition)
        {
            if (definition == null) return;
            manager?.ArmDefinition(definition);
            pendingSelectionDefinition = definition;
            pendingSelection = true;
        }

        private static void DrawDetails(UnitEntry entry)
        {
            if (entry == null) return;
            GUILayout.BeginVertical(HorusTheme.Card);
            GUILayout.BeginHorizontal();
            Rect icon = GUILayoutUtility.GetRect(48f, 48f, GUILayout.Width(48f), GUILayout.Height(48f));
            HorusWidgets.SpriteImage(icon, entry.Icon, Color.white);
            GUILayout.BeginVertical();
            GUILayout.Label(entry.Display, HorusTheme.TitleText);
            GUILayout.Label(RoleText(entry.Roles), HorusTheme.LabelMuted);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            HorusWidgets.KeyValue("Cost", $"${entry.Cost:N0}");
            HorusWidgets.KeyValue("Spawn", entry.SpawnKind.ToString());
            HorusWidgets.KeyValue("Surface", entry.PlacementSurface.ToString());
            HorusWidgets.KeyValue("Network", entry.IsNetworkRegistered ? "Registered" : "Lookup only");
            HorusWidgets.KeyValue("Dimensions", $"{entry.Def.length:F1} × {entry.Def.width:F1} m");
            HorusWidgets.KeyValue("Editor altitude", $"{entry.MinAlt:F0}–{entry.MaxAlt:F0} m");
            string flags = FlagText(entry.Flags);
            if (!string.IsNullOrEmpty(flags)) GUILayout.Label(flags, HorusTheme.LabelMuted);
            if (entry.Supply != null)
            {
                HorusWidgets.KeyValue("Can rearm aircraft", CapabilityText(entry.Supply.CanRearmAircraft));
                HorusWidgets.KeyValue("Can rearm vehicles", CapabilityText(entry.Supply.CanRearmVehicles));
                HorusWidgets.KeyValue("Can resupply ships", CapabilityText(entry.Supply.CanResupplyShips));
                HorusWidgets.KeyValue("Fuel support", entry.Supply.HasRefueler ? "Yes" : "No");
                HorusWidgets.KeyValue("Unit storage", entry.Supply.HasUnitStorage ? "Yes" : "No");
                HorusWidgets.KeyValue("Warhead storage", entry.Supply.HasWarheadStorage ? "Yes" : "No");
                if (entry.Supply.IsLogistics && entry.Supply.RearmRange.HasValue)
                    HorusWidgets.KeyValue("Rearm range", $"{entry.Supply.RearmRange.Value:F0} m");
                if (entry.Supply.IsLogistics && entry.Supply.RearmCapacity.HasValue)
                    HorusWidgets.KeyValue("Ammo capacity", $"{entry.Supply.RearmCapacity.Value:F0}");
                if (entry.Supply.IsLogistics && entry.Supply.RefuelRange.HasValue)
                    HorusWidgets.KeyValue("Refuel range", $"{entry.Supply.RefuelRange.Value:F0} m");
                if (entry.Supply.IsLogistics && entry.Supply.RearmerSingleUse.HasValue)
                    HorusWidgets.KeyValue("Rearmer single use", entry.Supply.RearmerSingleUse.Value ? "Yes" : "No");
                if (entry.Supply.IsLogistics && entry.Supply.RefuelerSingleUse.HasValue)
                    HorusWidgets.KeyValue("Refueler single use", entry.Supply.RefuelerSingleUse.Value ? "Yes" : "No");
                if (entry.Supply.IsLogistics && !string.IsNullOrEmpty(entry.Supply.Diagnostic))
                    GUILayout.Label(entry.Supply.Diagnostic, HorusTheme.LabelMuted);
            }
            if (entry.IsLookupOnly)
                GUILayout.Label("WARNING: not registered for network serialization.", HorusTheme.LabelWrap);
            if (!string.IsNullOrEmpty(entry.Def.description))
                GUILayout.Label(entry.Def.description, HorusTheme.LabelWrap, GUILayout.MaxHeight(52f));
            GUILayout.EndVertical();
        }

        private static string CapabilityText(CapabilityState state)
        {
            switch (state)
            {
                case CapabilityState.Yes: return "Yes";
                case CapabilityState.Unknown: return "Unknown";
                default: return "No";
            }
        }

        private static string FlagText(CatalogFlags value)
        {
            var labels = new List<string>();
            if ((value & CatalogFlags.Unlabeled) != 0) labels.Add("Unlabeled");
            if ((value & CatalogFlags.Disabled) != 0) labels.Add("Disabled");
            if ((value & CatalogFlags.Event) != 0) labels.Add("Event");
            if ((value & CatalogFlags.LookupOnly) != 0) labels.Add("Lookup only");
            if ((value & CatalogFlags.DuplicateJsonKey) != 0) labels.Add("Duplicate key");
            if ((value & CatalogFlags.LiveOrdnance) != 0) labels.Add("Live ordnance");
            if ((value & CatalogFlags.Nuclear) != 0) labels.Add("Nuclear");
            if ((value & CatalogFlags.Strategic) != 0) labels.Add("Strategic");
            if ((value & CatalogFlags.Logistics) != 0) labels.Add("Logistics");
            if ((value & CatalogFlags.Experimental) != 0) labels.Add("Experimental");
            if ((value & CatalogFlags.Modded) != 0) labels.Add("Modded");
            return string.Join(" · ", labels);
        }

        private static string RoleText(UnitRole value)
        {
            if (value == UnitRole.None) return "No specialized combat role";
            var labels = new List<string>();
            if ((value & UnitRole.AntiSurface) != 0) labels.Add("AntiSurface");
            if ((value & UnitRole.AntiAir) != 0) labels.Add("AntiAir");
            if ((value & UnitRole.AntiMissile) != 0) labels.Add("AntiMissile");
            if ((value & UnitRole.AntiRadar) != 0) labels.Add("AntiRadar");
            if ((value & UnitRole.Radar) != 0) labels.Add("Radar");
            if ((value & UnitRole.Strategic) != 0) labels.Add("Strategic");
            return string.Join(" · ", labels);
        }
    }
}
