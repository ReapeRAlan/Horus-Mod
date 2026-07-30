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
        private static bool favoritesOnly;
        private static Vector2 scroll;
        private static UnitEntry selected;

        public static void Reset()
        {
            selected = null;
            scroll = Vector2.zero;
        }

        public static void Draw(HorusManager manager)
        {
            UnitCatalog.EnsureBuilt();
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
            DrawRole(UnitRole.AntiSurface, "A-S");
            DrawRole(UnitRole.AntiAir, "A-A");
            DrawRole(UnitRole.AntiMissile, "A-M");
            DrawRole(UnitRole.AntiRadar, "A-R");
            DrawRole(UnitRole.Radar, "RDR");
            if (HorusWidgets.Chip("★", favoritesOnly, GUILayout.Width(36f))) favoritesOnly = !favoritesOnly;
            GUILayout.EndHorizontal();

            IReadOnlyList<UnitEntry> list = UnitCatalog.Query(kind, roles, search, favoritesOnly);
            GUILayout.Label($"{list.Count} unidades", HorusTheme.LabelMuted);
            Rect viewport = GUILayoutUtility.GetRect(1f, 225f, GUILayout.ExpandWidth(true));
            float contentHeight = Mathf.Max(viewport.height, list.Count * RowHeight);
            Rect content = new Rect(0f, 0f, Mathf.Max(1f, viewport.width - 18f), contentHeight);
            scroll = GUI.BeginScrollView(viewport, scroll, content);
            int first = Mathf.Max(0, (int)(scroll.y / RowHeight) - 1);
            int visible = Mathf.CeilToInt(viewport.height / RowHeight) + 2;
            int end = Mathf.Min(list.Count, first + visible);
            for (int i = first; i < end; i++) DrawRow(manager, list[i], i, content.width);
            GUI.EndScrollView();

            if (selected == null && manager.ArmedDefinition != null)
                selected = UnitCatalog.Find(manager.ArmedDefinition.jsonKey);
            DrawDetails(selected);
        }

        private static void DrawRecents(HorusManager manager)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label("Recientes", HorusTheme.LabelMuted, GUILayout.Width(55f));
            foreach (string key in HorusPrefs.Recents)
            {
                UnitEntry entry = UnitCatalog.Find(key);
                if (entry == null) continue;
                if (HorusWidgets.Ghost(HorusWidgets.Ellipsize(entry.Display, HorusTheme.ButtonGhost, 66f), GUILayout.Width(70f), GUILayout.Height(24f)))
                {
                    selected = entry;
                    manager.ArmDefinition(entry.Def);
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

        private static void DrawRow(HorusManager manager, UnitEntry entry, int index, float width)
        {
            Rect row = new Rect(0f, index * RowHeight, width, RowHeight - 2f);
            bool active = selected == entry || manager.ArmedDefinition == entry.Def;
            if (GUI.Button(row, GUIContent.none, active ? HorusTheme.ListRowSelected : HorusTheme.ListRow))
            {
                selected = entry;
                manager.ArmDefinition(entry.Def);
            }
            HorusWidgets.SpriteImage(new Rect(row.x + 5f, row.y + 4f, 24f, 24f), entry.Icon, Color.white);
            GUI.Label(new Rect(row.x + 35f, row.y, row.width - 125f, row.height), HorusWidgets.Ellipsize(entry.Display, HorusTheme.Label, row.width - 135f), HorusTheme.Label);
            GUI.Label(new Rect(row.xMax - 85f, row.y, 54f, row.height), $"${entry.Cost:N0}", HorusTheme.ValueRight);
            if (GUI.Button(new Rect(row.xMax - 28f, row.y + 3f, 25f, 25f), HorusPrefs.IsFavorite(entry.Key) ? "★" : "☆", HorusTheme.IconButton))
                HorusPrefs.ToggleFavorite(entry.Key);
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
            HorusWidgets.KeyValue("Coste", $"${entry.Cost:N0}");
            HorusWidgets.KeyValue("Dimensiones", $"{entry.Def.length:F1} × {entry.Def.width:F1} m");
            HorusWidgets.KeyValue("Altitud editor", $"{entry.MinAlt:F0} – {entry.MaxAlt:F0} m");
            if (!string.IsNullOrEmpty(entry.Def.description))
                GUILayout.Label(entry.Def.description, HorusTheme.LabelWrap, GUILayout.MaxHeight(52f));
            GUILayout.EndVertical();
        }

        private static string RoleText(UnitRole value)
        {
            if (value == UnitRole.None) return "Sin rol especializado";
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
