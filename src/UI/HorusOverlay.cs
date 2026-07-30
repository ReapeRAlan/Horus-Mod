using System.Collections.Generic;
using HorusMod.Interaction;
using UnityEngine;

namespace HorusMod.UI
{
    public sealed class HorusOverlay
    {
        private struct OverlayItem
        {
            public Unit Unit;
            public Rect Rect;
            public Color Color;
            public bool Hover;
        }

        private struct OrderMarker
        {
            public Vector3 Position;
            public float Started;
            public Unit[] Sources;
        }

        private const int MaxOverlayUnits = 64;
        private readonly HorusSelection selection;
        private readonly List<OverlayItem> items = new List<OverlayItem>(MaxOverlayUnits);
        private readonly OrderMarker[] markers = new OrderMarker[16];
        private int markerWrite;
        private int hiddenSelectionCount;
        private bool showHelp;

        public int VisibleCount => items.Count;

        public HorusOverlay(HorusSelection selection, HorusOrders orders)
        {
            this.selection = selection;
            orders.OrderIssued += AddMarker;
        }

        public void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) showHelp = !showHelp;
            items.Clear();
            hiddenSelectionCount = 0;
            Camera cam = Camera.main;
            if (cam == null) return;

            for (int i = 0; i < selection.Units.Count && items.Count < MaxOverlayUnits; i++)
                AddUnit(selection.Units[i], false, cam);
            hiddenSelectionCount = Mathf.Max(0, selection.Units.Count - MaxOverlayUnits);
            if (selection.Hover != null && !selection.Contains(selection.Hover) && items.Count < MaxOverlayUnits)
                AddUnit(selection.Hover, true, cam);
        }

        public void Draw(bool marqueeActive, Rect marqueeRawScreen)
        {
            if (Event.current.type != EventType.Repaint) return;
            Texture2D white = HorusTheme.Pixel(Color.white);
            foreach (OverlayItem item in items) DrawBracket(item.Rect, item.Color, item.Hover ? 1f : 2f, white);

            Camera cam = Camera.main;
            if (cam != null)
            {
                for (int i = 0; i < markers.Length; i++)
                {
                    float age = Time.unscaledTime - markers[i].Started;
                    if (markers[i].Started <= 0f || age > 1.5f) continue;
                    Vector3 p = cam.WorldToScreenPoint(markers[i].Position);
                    if (p.z <= 0f) continue;
                    float radius = Mathf.Lerp(34f, 8f, age / 1.5f);
                    Rect ring = new Rect(p.x - radius, Screen.height - p.y - radius, radius * 2f, radius * 2f);
                    Color markerColor = new Color(HorusTheme.Accent.r, HorusTheme.Accent.g, HorusTheme.Accent.b, 1f - age / 1.5f);
                    DrawBracket(ring, markerColor, 2f, white);
                    Unit[] sources = markers[i].Sources;
                    if (sources == null) continue;
                    Vector2 targetGui = new Vector2(p.x, Screen.height - p.y);
                    for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                    {
                        Unit source = sources[sourceIndex];
                        if (source == null || source.disabled) continue;
                        Vector3 sourceScreen = cam.WorldToScreenPoint(source.transform.position);
                        if (sourceScreen.z <= 0f) continue;
                        DrawLine(
                            new Vector2(sourceScreen.x, Screen.height - sourceScreen.y),
                            targetGui,
                            markerColor,
                            1f,
                            white);
                    }
                }
            }

            if (marqueeActive)
            {
                Rect guiRect = RawScreenToGui(marqueeRawScreen);
                Color fill = new Color(HorusTheme.Accent.r, HorusTheme.Accent.g, HorusTheme.Accent.b, 0.12f);
                GUI.color = fill;
                GUI.DrawTexture(guiRect, white);
                GUI.color = HorusTheme.Accent;
                DrawRectOutline(guiRect, 1f, white);
                GUI.color = Color.white;
            }

            if (selection.Hover != null)
            {
                Vector2 mouse = new Vector2(Input.mousePosition.x + 14f, Screen.height - Input.mousePosition.y + 14f);
                Unit unit = selection.Hover;
                string faction = unit.NetworkHQ != null && unit.NetworkHQ.faction != null ? unit.NetworkHQ.faction.factionName : "Neutral";
                GUI.Label(new Rect(mouse.x, mouse.y, 260f, 22f), $"{unit.unitName} · {faction}", HorusTheme.Pill);
            }

            if (showHelp)
            {
                GUI.Box(new Rect(Screen.width - 330f, 24f, 310f, 220f), GUIContent.none, HorusTheme.Card);
                GUI.Label(new Rect(Screen.width - 316f, 35f, 284f, 198f),
                    "<b>HORUS CONTROLS</b>\n" +
                    "LMB select / place · Shift add / repeat place\n" +
                    "Drag LMB box-select · Ctrl remove\n" +
                    "RMB move · Alt+RMB menu · drag to look\n" +
                    "MMB / Del delete · Esc cancel\n" +
                    "F focus · H hold · Ctrl+D duplicate\n" +
                    "Ctrl+A select Horus units\n" +
                    "Ctrl+1–9 assign · 1–9 recall\n" +
                    "Ctrl+Z / Ctrl+Y undo / redo",
                    HorusTheme.Label);
            }

            if (hiddenSelectionCount > 0)
                GUI.Label(new Rect(Screen.width - 130f, Screen.height - 42f, 110f, 24f), $"+{hiddenSelectionCount} more", HorusTheme.Pill);
        }

        private void AddUnit(Unit unit, bool hover, Camera cam)
        {
            if (unit == null || unit.disabled || unit.definition == null) return;
            Vector3 center = cam.WorldToScreenPoint(unit.transform.position);
            if (center.z <= 0f || center.x < -100f || center.x > Screen.width + 100f || center.y < -100f || center.y > Screen.height + 100f) return;
            float half = Mathf.Max(unit.definition.length, unit.definition.width, 5f) * 0.5f;
            Vector3 edge = cam.WorldToScreenPoint(unit.transform.position + cam.transform.right * half);
            float radius = Mathf.Clamp(Vector2.Distance(center, edge), 10f, 300f);
            Color color = unit.NetworkHQ != null && unit.NetworkHQ.faction != null ? unit.NetworkHQ.faction.color : Color.gray;
            if (hover) color.a = 0.55f;
            items.Add(new OverlayItem
            {
                Unit = unit,
                Rect = new Rect(center.x - radius, Screen.height - center.y - radius, radius * 2f, radius * 2f),
                Color = color,
                Hover = hover
            });
        }

        private void AddMarker(Vector3 position, IReadOnlyList<Unit> units)
        {
            int sourceCount = units != null ? Mathf.Min(MaxOverlayUnits, units.Count) : 0;
            var sources = new Unit[sourceCount];
            for (int i = 0; i < sourceCount; i++) sources[i] = units[i];
            markers[markerWrite] = new OrderMarker { Position = position, Started = Time.unscaledTime, Sources = sources };
            markerWrite = (markerWrite + 1) % markers.Length;
        }

        private static void DrawLine(Vector2 from, Vector2 to, Color color, float thickness, Texture2D texture)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 1f) return;
            Matrix4x4 previous = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, length, thickness), texture);
            GUI.matrix = previous;
            GUI.color = Color.white;
        }

        private static void DrawBracket(Rect rect, Color color, float thickness, Texture2D texture)
        {
            float arm = Mathf.Clamp(Mathf.Min(rect.width, rect.height) * 0.22f, 7f, 28f);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, arm, thickness), texture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, arm), texture);
            GUI.DrawTexture(new Rect(rect.xMax - arm, rect.y, arm, thickness), texture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, arm), texture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, arm, thickness), texture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - arm, thickness, arm), texture);
            GUI.DrawTexture(new Rect(rect.xMax - arm, rect.yMax - thickness, arm, thickness), texture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMax - arm, thickness, arm), texture);
            GUI.color = Color.white;
        }

        private static void DrawRectOutline(Rect rect, float thickness, Texture2D texture)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), texture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), texture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), texture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), texture);
        }

        private static Rect RawScreenToGui(Rect raw)
        {
            return new Rect(raw.x, Screen.height - raw.yMax, raw.width, raw.height);
        }
    }
}
