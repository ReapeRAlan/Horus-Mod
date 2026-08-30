using BepInEx.Configuration;
using BepInEx.Logging;
using HorusMod.Logging;

namespace HorusMod
{
    // Compatibility surface for headless-safe services shared with the client build.
    public static class HorusPlugin
    {
        public static ManualLogSource Logger { get; internal set; }
        public static ConfigEntry<HorusLogLevel> LogVerbosity { get; internal set; }
        public static ConfigEntry<bool> AllowIncompatibleContent { get; internal set; }
        public static ConfigEntry<bool> ImproveAIBombingAccuracy { get; internal set; }
        public static ConfigEntry<bool> EnableRtsIncome { get; internal set; }
        public static ConfigEntry<bool> EnableRtsUnitCap { get; internal set; }
        public static ConfigEntry<bool> SyncWithFactionBudget { get; internal set; }
        public static ConfigEntry<bool> AllowGroupPurchasesInRtsMode { get; internal set; }
        public static ConfigEntry<bool> EnableStrictBaseDeployment { get; internal set; }
        public static ConfigEntry<float> BaseDeploymentRadius { get; internal set; }
        public static ConfigEntry<float> ShipSpawnLift { get; internal set; }
    }
}

namespace HorusMod.UI
{
    internal static class HorusToasts
    {
        public static void Show(string message)
        {
            HorusMod.Logging.HorusLog.Verbose("Server", message ?? "");
        }
    }
}

namespace HorusMod.Interaction
{
    internal static class HorusUndo
    {
        public static void RecordMove(System.Collections.Generic.List<Unit> units,
            System.Collections.Generic.List<GlobalPosition> before,
            System.Collections.Generic.List<GlobalPosition> after) { }
    }
}
