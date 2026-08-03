using HorusMod.Core;
using HorusMod.Data;
using HorusMod.Networking;
using HorusMod.UI.Tabs;
using UnityEngine;

namespace HorusMod.UI
{
    public enum HorusTab
    {
        Place,
        Manage,
        Rts,
        Settings,
        Debug
    }

    public static class HorusWindowRoot
    {
        private static HorusTab activeTab;
        private static HorusTab? pendingActiveTab;
        private static bool resizing;
        private static bool resizeRequested;
        private static Vector2 requestedSize;
        private static Vector2 resizeStartMouse;
        private static Vector2 resizeStartSize;
        private static readonly Vector2[] tabScroll = new Vector2[5];

        public static HorusTab ActiveTab => activeTab;
        public static void ResetActiveTab()
        {
            activeTab = HorusTab.Place;
            pendingActiveTab = null;
        }

        public static void ApplyRequestedSize(ref Rect rect)
        {
            if (!resizeRequested) return;
            rect.size = requestedSize;
        }

        public static void Draw(HorusManager manager, int windowId)
        {
            if (Event.current.type == EventType.Layout && pendingActiveTab.HasValue)
            {
                activeTab = pendingActiveTab.Value;
                pendingActiveTab = null;
            }

            HorusTheme.BeginSkinScope();
            Rect rect = manager.WindowRect;
            Rect title = new Rect(0f, 0f, rect.width, 30f);
            GUI.Box(title, GUIContent.none, HorusTheme.TitleBar);
            GUI.Label(new Rect(10f, 1f, rect.width - 64f, 28f), $"⚡ HORUS  v{HorusPlugin.PluginVersion}", HorusTheme.TitleText);
            if (GUI.Button(new Rect(rect.width - 29f, 3f, 24f, 24f), "×", HorusTheme.IconButton))
                manager.ToggleUiVisibility();
            GUI.DragWindow(title);

            float y = 32f;
            DrawTabs(manager, new Rect(6f, y, rect.width - 12f, 28f));
            y += 31f;

            GUILayout.BeginArea(new Rect(7f, y, rect.width - 14f, Mathf.Max(100f, rect.height - y - 31f)));
            int tabIndex = (int)activeTab;
            tabScroll[tabIndex] = GUILayout.BeginScrollView(tabScroll[tabIndex], false, true);
            switch (activeTab)
            {
                case HorusTab.Place: PlaceTab.Draw(manager); break;
                case HorusTab.Manage: ManageTab.Draw(manager); break;
                case HorusTab.Rts: RtsTab.Draw(manager); break;
                case HorusTab.Settings: SettingsTab.Draw(manager); break;
                case HorusTab.Debug: DebugTab.Draw(manager); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            Rect status = new Rect(0f, rect.height - 25f, rect.width, 25f);
            GUI.Box(status, GUIContent.none, HorusTheme.StatusBar);
            string permission = HorusPermissions.IsMultiplayerClient() ? "View Only" : "Host";
            string tool = manager.InputRouter == null
                ? "SELECT"
                : manager.InputRouter.GroupOrderTargetMode != HorusMod.Interaction.HorusGroupOrderTargetMode.None
                    ? "TARGET " + manager.InputRouter.GroupOrderTargetMode.ToString().ToUpperInvariant()
                    : manager.InputRouter.PatrolPlanning
                        ? "PATROL"
                        : manager.InputRouter.Tool.ToString().ToUpperInvariant();
            int selection = manager.WorldSelection != null ? manager.WorldSelection.Count : 0;
            string toast = HorusToasts.Current;
            string statusText = string.IsNullOrEmpty(toast)
                ? $"● {permission}  |  {tool}  |  Sel {selection}  |  Alt {manager.SpawnAltitude:F0}  Yaw {manager.SpawnYaw:F0}°"
                : $"● {toast}";
            GUI.Label(new Rect(8f, status.y + 2f, rect.width - 28f, 21f), statusText, HorusTheme.LabelSmall);

            Rect grip = new Rect(rect.width - 16f, rect.height - 16f, 16f, 16f);
            GUI.Label(grip, "◢", HorusTheme.ResizeGrip);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                resizing = true;
                resizeStartMouse = e.mousePosition;
                resizeStartSize = rect.size;
                e.Use();
            }
            else if (resizing && e.type == EventType.MouseDrag)
            {
                Vector2 delta = e.mousePosition - resizeStartMouse;
                float scale = HorusPlugin.UIScale != null ? Mathf.Max(0.1f, HorusPlugin.UIScale.Value) : 1f;
                float scaledScreenWidth = Screen.width / scale;
                float scaledScreenHeight = Screen.height / scale;
                rect.width = Mathf.Clamp(resizeStartSize.x + delta.x, 360f, Mathf.Max(360f, scaledScreenWidth - rect.x));
                rect.height = Mathf.Clamp(resizeStartSize.y + delta.y, 320f, Mathf.Max(320f, scaledScreenHeight - rect.y));
                requestedSize = rect.size;
                resizeRequested = true;
                e.Use();
            }
            else if (resizing && e.type == EventType.MouseUp)
            {
                resizing = false;
                requestedSize = rect.size;
                resizeRequested = true;
                manager.WindowRect = rect;
                HorusPrefs.SaveWindow(rect);
                e.Use();
            }
            if (!resizing && e.type != EventType.MouseUp) resizeRequested = false;
            HorusTheme.EndSkinScope();
        }

        private static void DrawTabs(HorusManager manager, Rect rect)
        {
            int count = HorusPlugin.ShowDebugTab != null && HorusPlugin.ShowDebugTab.Value ? 5 : 4;
            float width = rect.width / count;
            DrawTab(rect.x + 0 * width, rect.y, width, HorusTab.Place, "PLACE");
            DrawTab(rect.x + 1 * width, rect.y, width, HorusTab.Manage, "MANAGE");
            DrawTab(rect.x + 2 * width, rect.y, width, HorusTab.Rts, "RTS");
            DrawTab(rect.x + 3 * width, rect.y, width, HorusTab.Settings, "SETTINGS");
            if (count == 5) DrawTab(rect.x + 4 * width, rect.y, width, HorusTab.Debug, "DEBUG");
            else if (activeTab == HorusTab.Debug) pendingActiveTab = HorusTab.Settings;
        }

        private static void DrawTab(float x, float y, float width, HorusTab tab, string label)
        {
            if (GUI.Button(new Rect(x, y, width - 2f, 27f), label, activeTab == tab ? HorusTheme.TabActive : HorusTheme.TabInactive))
                pendingActiveTab = tab;
        }
    }
}
