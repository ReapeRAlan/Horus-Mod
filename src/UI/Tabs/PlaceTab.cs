using HorusMod.Core;

namespace HorusMod.UI.Tabs
{
    public static class PlaceTab
    {
        public static void Draw(HorusManager manager)
        {
            manager.DrawFactionSelector();
            UnitBrowser.Draw(manager);
            manager.DrawPlaceConfiguration();
        }
    }
}
