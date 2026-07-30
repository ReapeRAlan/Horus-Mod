using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.UI.ContextMenu
{
    public static class HorusContextMenu
    {
        private sealed class MenuLevel
        {
            public List<ContextMenuItem> Items;
            public Rect Rect;
            public int HoverIndex = -1;
            public float HoverStart;
        }

        private const float RowHeight = 24f;
        private const float SeparatorHeight = 7f;
        private static readonly List<MenuLevel> levels = new List<MenuLevel>();
        private static readonly GUIContent measurement = new GUIContent();

        public static bool IsOpen => levels.Count > 0;

        public static void Open(Vector2 guiPosition, List<ContextMenuItem> items)
        {
            Close();
            if (items == null || items.Count == 0) return;
            HorusTheme.EnsureBuilt();
            levels.Add(CreateLevel(guiPosition, items));
        }

        public static void Close() => levels.Clear();

        public static bool ContainsPoint(Vector2 guiPoint)
        {
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].Rect.Contains(guiPoint)) return true;
            return false;
        }

        public static void Draw()
        {
            if (!IsOpen) return;
            Event e = Event.current;
            Vector2 mouse = e.mousePosition;

            bool insideAny = ContainsPoint(mouse);
            if (e.type == EventType.MouseDown && !insideAny)
            {
                Close();
                return;
            }

            for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
            {
                MenuLevel level = levels[levelIndex];
                GUI.Box(level.Rect, GUIContent.none, HorusTheme.MenuPanel);
                float y = level.Rect.y + 4f;
                for (int itemIndex = 0; itemIndex < level.Items.Count; itemIndex++)
                {
                    ContextMenuItem item = level.Items[itemIndex];
                    float height = item.IsSeparator ? SeparatorHeight : RowHeight;
                    Rect row = new Rect(level.Rect.x + 4f, y, level.Rect.width - 8f, height);
                    y += height;

                    if (item.IsSeparator)
                    {
                        if (e.type == EventType.Repaint)
                            GUI.DrawTexture(new Rect(row.x, row.center.y, row.width, 1f), HorusTheme.Pixel(HorusTheme.Border));
                        continue;
                    }

                    bool hovered = row.Contains(mouse);
                    if (item.IsHeader)
                    {
                        GUI.Label(row, item.Label ?? "", HorusTheme.LabelMuted);
                        continue;
                    }

                    GUI.enabled = item.Enabled;
                    GUIStyle style = item.IsDanger ? HorusTheme.MenuItemDanger : HorusTheme.MenuItem;
                    GUI.Label(row, item.Label + (item.Submenu != null ? "  ›" : ""), style);
                    if (!string.IsNullOrEmpty(item.Shortcut))
                        GUI.Label(new Rect(row.xMax - 80f, row.y, 76f, row.height), item.Shortcut, HorusTheme.MenuShortcut);
                    GUI.enabled = true;

                    if (hovered)
                    {
                        if (level.HoverIndex != itemIndex)
                        {
                            level.HoverIndex = itemIndex;
                            level.HoverStart = Time.unscaledTime;
                        }
                        if (item.Submenu != null && Time.unscaledTime - level.HoverStart >= 0.15f)
                        {
                            while (levels.Count > levelIndex + 1) levels.RemoveAt(levels.Count - 1);
                            if (levels.Count == levelIndex + 1)
                                levels.Add(CreateLevel(new Vector2(level.Rect.xMax - 4f, row.y), item.Submenu));
                        }
                        else if (item.Submenu == null && levels.Count > levelIndex + 1)
                        {
                            while (levels.Count > levelIndex + 1) levels.RemoveAt(levels.Count - 1);
                        }

                        if (!item.Enabled && !string.IsNullOrEmpty(item.DisabledReason))
                        {
                            measurement.text = item.DisabledReason;
                            Vector2 size = HorusTheme.Tooltip.CalcSize(measurement);
                            GUI.Box(new Rect(mouse.x + 12f, mouse.y + 14f, Mathf.Min(260f, size.x + 14f), size.y + 10f), item.DisabledReason, HorusTheme.Tooltip);
                        }
                    }

                    if (item.Enabled && e.type == EventType.MouseDown && e.button == 0 && hovered)
                    {
                        e.Use();
                        if (item.Submenu == null)
                        {
                            item.OnClick?.Invoke();
                            Close();
                            return;
                        }
                    }
                }
            }
        }

        private static MenuLevel CreateLevel(Vector2 position, List<ContextMenuItem> items)
        {
            float width = 170f;
            float height = 8f;
            foreach (ContextMenuItem item in items)
            {
                height += item.IsSeparator ? SeparatorHeight : RowHeight;
                if (!item.IsSeparator)
                {
                    measurement.text = item.Label ?? "";
                    float labelWidth = HorusTheme.MenuItem.CalcSize(measurement).x;
                    measurement.text = item.Shortcut ?? "";
                    float candidate = labelWidth + HorusTheme.MenuShortcut.CalcSize(measurement).x + 42f;
                    width = Mathf.Max(width, candidate);
                }
            }
            width = Mathf.Clamp(width, 170f, 340f);
            float scale = HorusPlugin.UIScale != null ? Mathf.Max(0.1f, HorusPlugin.UIScale.Value) : 1f;
            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;
            if (position.x + width > screenWidth) position.x -= width;
            if (position.y + height > screenHeight) position.y -= height;
            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, screenWidth - width));
            position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, screenHeight - height));
            return new MenuLevel { Items = items, Rect = new Rect(position.x, position.y, width, height) };
        }
    }
}
