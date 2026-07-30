using UnityEngine;

namespace HorusMod.UI
{
    public static class HorusWidgets
    {
        private static readonly GUIContent measurement = new GUIContent();
        public static bool Primary(string label, params GUILayoutOption[] options) => GUILayout.Button(label, HorusTheme.ButtonPrimary, options);
        public static bool Secondary(string label, params GUILayoutOption[] options) => GUILayout.Button(label, HorusTheme.ButtonSecondary, options);
        public static bool Danger(string label, params GUILayoutOption[] options) => GUILayout.Button(label, HorusTheme.ButtonDanger, options);
        public static bool Ghost(string label, params GUILayoutOption[] options) => GUILayout.Button(label, HorusTheme.ButtonGhost, options);

        public static bool Chip(string label, bool active, params GUILayoutOption[] options)
        {
            return GUILayout.Button(label, active ? HorusTheme.ChipActive : HorusTheme.Chip, options);
        }

        public static void KeyValue(string key, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, HorusTheme.LabelMuted);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, HorusTheme.ValueRight);
            GUILayout.EndHorizontal();
        }

        public static void Separator()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(rect, HorusTheme.Pixel(HorusTheme.Border));
        }

        public static string Ellipsize(string value, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(value) || Measure(style, value) <= maxWidth) return value;
            const string ellipsis = "…";
            int low = 0;
            int high = value.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Measure(style, value.Substring(0, mid) + ellipsis) <= maxWidth) low = mid;
                else high = mid - 1;
            }
            return value.Substring(0, low) + ellipsis;
        }

        private static float Measure(GUIStyle style, string value)
        {
            measurement.text = value;
            return style.CalcSize(measurement).x;
        }

        public static void SpriteImage(Rect rect, Sprite sprite, Color tint)
        {
            if (sprite == null || sprite.texture == null || (sprite.packed && sprite.packingMode == SpritePackingMode.Tight))
            {
                GUI.DrawTexture(rect, HorusTheme.Pixel(tint));
                return;
            }
            Rect tr = sprite.textureRect;
            Rect uv = new Rect(tr.x / sprite.texture.width, tr.y / sprite.texture.height, tr.width / sprite.texture.width, tr.height / sprite.texture.height);
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
            GUI.color = old;
        }
    }
}
