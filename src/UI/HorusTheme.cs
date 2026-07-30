using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.UI
{
    public static class HorusTheme
    {
        public static readonly Color BgWindow = new Color(0.055f, 0.07f, 0.085f, 0.98f);
        public static readonly Color BgPanel = new Color(0.075f, 0.095f, 0.115f, 0.98f);
        public static readonly Color BgRow = new Color(0.095f, 0.12f, 0.145f, 1f);
        public static readonly Color BgRowAlt = new Color(0.08f, 0.105f, 0.13f, 1f);
        public static readonly Color BgHover = new Color(0.13f, 0.19f, 0.23f, 1f);
        public static readonly Color BgActive = new Color(0.08f, 0.33f, 0.42f, 1f);
        public static readonly Color Border = new Color(0.20f, 0.29f, 0.34f, 1f);
        public static readonly Color Accent = new Color(0.15f, 0.78f, 0.92f, 1f);
        public static readonly Color Success = new Color(0.28f, 0.78f, 0.43f, 1f);
        public static readonly Color Warning = new Color(1f, 0.68f, 0.20f, 1f);
        public static readonly Color Danger = new Color(0.95f, 0.28f, 0.30f, 1f);
        public static readonly Color TextHi = new Color(0.93f, 0.97f, 0.98f, 1f);
        public static readonly Color TextMid = new Color(0.70f, 0.78f, 0.82f, 1f);
        public static readonly Color TextLow = new Color(0.47f, 0.56f, 0.61f, 1f);

        private static readonly List<Texture2D> textures = new List<Texture2D>();
        private static readonly Dictionary<Color, Texture2D> pixels = new Dictionary<Color, Texture2D>();
        private static bool built;
        private static int skinId = -1;
        private static int allocationFrame = -1;
        private static bool skinScopeActive;
        private static GUIStyle previousLabel;
        private static GUIStyle previousButton;
        private static GUIStyle previousBox;
        private static GUIStyle previousToggle;
        private static GUIStyle previousTextField;

        public static bool Built => built;
        public static int StylesAllocatedThisFrame { get; private set; }
        public static GUIStyle Window { get; private set; }
        public static GUIStyle TitleBar { get; private set; }
        public static GUIStyle TitleText { get; private set; }
        public static GUIStyle TabActive { get; private set; }
        public static GUIStyle TabInactive { get; private set; }
        public static GUIStyle SectionHeader { get; private set; }
        public static GUIStyle Card { get; private set; }
        public static GUIStyle Label { get; private set; }
        public static GUIStyle LabelSmall { get; private set; }
        public static GUIStyle LabelMuted { get; private set; }
        public static GUIStyle LabelWrap { get; private set; }
        public static GUIStyle ValueRight { get; private set; }
        public static GUIStyle ButtonPrimary { get; private set; }
        public static GUIStyle ButtonSecondary { get; private set; }
        public static GUIStyle ButtonDanger { get; private set; }
        public static GUIStyle ButtonGhost { get; private set; }
        public static GUIStyle IconButton { get; private set; }
        public static GUIStyle ToggleRow { get; private set; }
        public static GUIStyle ListRow { get; private set; }
        public static GUIStyle ListRowSelected { get; private set; }
        public static GUIStyle SearchField { get; private set; }
        public static GUIStyle Chip { get; private set; }
        public static GUIStyle ChipActive { get; private set; }
        public static GUIStyle Pill { get; private set; }
        public static GUIStyle MenuPanel { get; private set; }
        public static GUIStyle MenuItem { get; private set; }
        public static GUIStyle MenuItemDanger { get; private set; }
        public static GUIStyle MenuShortcut { get; private set; }
        public static GUIStyle Tooltip { get; private set; }
        public static GUIStyle StatusBar { get; private set; }
        public static GUIStyle ResizeGrip { get; private set; }

        public static void BeginSkinScope()
        {
            if (skinScopeActive || GUI.skin == null || !built) return;
            previousLabel = GUI.skin.label;
            previousButton = GUI.skin.button;
            previousBox = GUI.skin.box;
            previousToggle = GUI.skin.toggle;
            previousTextField = GUI.skin.textField;
            GUI.skin.label = Label;
            GUI.skin.button = ButtonSecondary;
            GUI.skin.box = Card;
            GUI.skin.toggle = ToggleRow;
            GUI.skin.textField = SearchField;
            skinScopeActive = true;
        }

        public static void EndSkinScope()
        {
            if (!skinScopeActive || GUI.skin == null) return;
            GUI.skin.label = previousLabel;
            GUI.skin.button = previousButton;
            GUI.skin.box = previousBox;
            GUI.skin.toggle = previousToggle;
            GUI.skin.textField = previousTextField;
            skinScopeActive = false;
        }

        public static void EnsureBuilt()
        {
            if (allocationFrame != Time.frameCount)
            {
                allocationFrame = Time.frameCount;
                StylesAllocatedThisFrame = 0;
            }
            if (GUI.skin == null) return;
            int currentSkin = GUI.skin.GetInstanceID();
            if (built && skinId == currentSkin) return;
            Dispose();
            skinId = currentSkin;

            Label = TextStyle(13, TextHi);
            LabelSmall = TextStyle(11, TextMid);
            LabelMuted = TextStyle(11, TextLow);
            LabelWrap = TextStyle(11, TextMid);
            LabelWrap.wordWrap = true;
            ValueRight = TextStyle(12, TextHi, TextAnchor.MiddleRight);

            Window = BoxStyle(BgWindow, Border, 0);
            Window.padding = new RectOffset(0, 0, 0, 0);
            TitleBar = BoxStyle(new Color(0.045f, 0.12f, 0.15f, 1f), Accent, 0);
            TitleBar.padding = new RectOffset(10, 8, 3, 2);
            TitleText = TextStyle(14, TextHi, TextAnchor.MiddleLeft, FontStyle.Bold);
            TabActive = ButtonStyle(BgActive, BgHover, BgActive, Accent, TextHi, 12, FontStyle.Bold);
            TabInactive = ButtonStyle(BgPanel, BgHover, BgActive, Border, TextMid, 12);
            SectionHeader = ButtonStyle(BgPanel, BgHover, BgActive, Border, TextHi, 12, FontStyle.Bold);
            Card = BoxStyle(BgPanel, Border, 8);
            Card.margin = new RectOffset(4, 4, 3, 3);
            ButtonPrimary = ButtonStyle(BgActive, new Color(0.1f, 0.42f, 0.52f), new Color(0.05f, 0.25f, 0.32f), Accent, TextHi, 12, FontStyle.Bold);
            ButtonSecondary = ButtonStyle(BgRow, BgHover, BgActive, Border, TextHi, 12);
            ButtonDanger = ButtonStyle(new Color(0.32f, 0.09f, 0.10f), new Color(0.48f, 0.11f, 0.12f), new Color(0.24f, 0.06f, 0.07f), Danger, TextHi, 12, FontStyle.Bold);
            ButtonGhost = ButtonStyle(BgPanel, BgHover, BgActive, BgPanel, TextMid, 11);
            IconButton = new GUIStyle(ButtonSecondary) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(2, 2, 2, 2) };
            ToggleRow = new GUIStyle(GUI.skin.toggle) { fontSize = 12, richText = true, normal = { textColor = TextMid }, onNormal = { textColor = TextHi } };
            ListRow = ButtonStyle(BgRow, BgHover, BgActive, Border, TextHi, 12);
            ListRow.alignment = TextAnchor.MiddleLeft;
            ListRowSelected = ButtonStyle(BgActive, BgHover, BgActive, Accent, TextHi, 12, FontStyle.Bold);
            ListRowSelected.alignment = TextAnchor.MiddleLeft;
            SearchField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                padding = new RectOffset(8, 8, 5, 4),
                normal = { background = Framed(BgRow, Border), textColor = TextHi },
                focused = { background = Framed(BgPanel, Accent), textColor = TextHi }
            };
            SearchField.border = new RectOffset(1, 1, 1, 1);
            Chip = ButtonStyle(BgRow, BgHover, BgActive, Border, TextMid, 10, FontStyle.Bold);
            ChipActive = ButtonStyle(BgActive, BgHover, BgActive, Accent, TextHi, 10, FontStyle.Bold);
            Pill = new GUIStyle(ChipActive) { alignment = TextAnchor.MiddleCenter };
            MenuPanel = BoxStyle(BgWindow, Border, 4);
            MenuItem = ButtonStyle(BgWindow, BgHover, BgActive, BgWindow, TextHi, 12);
            MenuItem.alignment = TextAnchor.MiddleLeft;
            MenuItemDanger = new GUIStyle(MenuItem);
            MenuItemDanger.normal.textColor = Danger;
            MenuItemDanger.hover.textColor = Color.white;
            MenuShortcut = TextStyle(11, TextLow, TextAnchor.MiddleRight);
            Tooltip = BoxStyle(new Color(0.025f, 0.035f, 0.045f, 0.98f), Accent, 6);
            Tooltip.wordWrap = true;
            Tooltip.normal.textColor = TextHi;
            Tooltip.fontSize = 11;
            StatusBar = BoxStyle(new Color(0.045f, 0.075f, 0.09f, 1f), Border, 4);
            StatusBar.normal.textColor = TextMid;
            StatusBar.alignment = TextAnchor.MiddleLeft;
            ResizeGrip = TextStyle(13, TextLow, TextAnchor.LowerRight);
            StylesAllocatedThisFrame = 30;
            built = true;
        }

        private static GUIStyle TextStyle(int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle fontStyle = FontStyle.Normal)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                richText = true,
                wordWrap = false,
                normal = { textColor = color },
                hover = { textColor = color },
                active = { textColor = color },
                focused = { textColor = color }
            };
        }

        private static GUIStyle BoxStyle(Color fill, Color border, int padding)
        {
            Texture2D background = Framed(fill, border);
            return new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(padding, padding, padding, padding),
                normal = { background = background, textColor = TextHi },
                hover = { background = background, textColor = TextHi },
                active = { background = background, textColor = TextHi }
            };
        }

        private static GUIStyle ButtonStyle(Color normal, Color hover, Color active, Color border, Color text, int size, FontStyle fontStyle = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(7, 7, 4, 4),
                normal = { background = Framed(normal, border), textColor = text },
                hover = { background = Framed(hover, Accent), textColor = TextHi },
                active = { background = Framed(active, Accent), textColor = TextHi },
                focused = { background = Framed(normal, Accent), textColor = text },
                onNormal = { background = Framed(active, Accent), textColor = TextHi },
                onHover = { background = Framed(hover, Accent), textColor = TextHi },
                onActive = { background = Framed(active, Accent), textColor = TextHi }
            };
            return style;
        }

        private static Texture2D Framed(Color fill, Color border)
        {
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    texture.SetPixel(x, y, x == 1 && y == 1 ? fill : border);
            texture.Apply(false, true);
            textures.Add(texture);
            return texture;
        }

        public static Texture2D Pixel(Color color)
        {
            if (pixels.TryGetValue(color, out Texture2D cached) && cached != null) return cached;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            textures.Add(texture);
            pixels[color] = texture;
            return texture;
        }

        public static void Dispose()
        {
            EndSkinScope();
            foreach (Texture2D texture in textures)
                if (texture != null) Object.Destroy(texture);
            textures.Clear();
            pixels.Clear();
            built = false;
            skinId = -1;
        }
    }
}
