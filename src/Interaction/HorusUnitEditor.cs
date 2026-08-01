using HorusMod.Logging;
using HorusMod.Loadouts;
using HorusMod.Networking;

namespace HorusMod.Interaction
{
    public static class HorusUnitEditor
    {
        public static bool TrySetLoadout(Aircraft aircraft, int presetIndex)
        {
            return TrySetLoadoutDetailed(aircraft, presetIndex).Success;
        }

        public static LoadoutApplyResult TrySetLoadoutDetailed(Aircraft aircraft, int presetIndex)
        {
            LoadoutApplyResult result = HorusLoadoutService.ApplyStandardPreset(aircraft, presetIndex);
            if (!result.Success)
                HorusLog.Verbose("UnitEditor", $"Loadout preset {presetIndex} was not applied: {result.Message}");
            return result;
        }

        public static LoadoutApplyResult TrySetLoadout(Aircraft aircraft, LoadoutDraft draft)
        {
            LoadoutApplyResult result = HorusLoadoutService.ApplyToAircraft(aircraft, draft);
            if (!result.Success)
                HorusLog.Verbose("UnitEditor", $"Custom loadout was not applied: {result.Message}");
            return result;
        }

        public static bool TrySetLivery(Aircraft aircraft, int index)
        {
            if (!HorusPermissions.CanSpawn() || aircraft == null) return false;
            AircraftDefinition definition = aircraft.definition as AircraftDefinition;
            AircraftParameters parameters = definition != null ? definition.aircraftParameters : null;
            if (parameters?.liveries == null || index < 0 || index >= parameters.liveries.Count) return false;
            aircraft.SetLiveryKey(new LiveryKey(index), true);
            return true;
        }

        public static void SetSkill(Unit unit, float skill)
        {
            if (!HorusPermissions.CanSpawn() || unit == null) return;
            skill = UnityEngine.Mathf.Clamp01(skill);
            if (unit is Aircraft aircraft)
            {
                aircraft.skill = skill;
                aircraft.bravery = UnityEngine.Mathf.Clamp01(skill * 0.75f + 0.2f);
            }
            else if (unit is GroundVehicle vehicle)
            {
                vehicle.skill = skill;
            }
            else if (unit is Ship ship)
            {
                ship.skill = skill;
            }
        }
    }
}
