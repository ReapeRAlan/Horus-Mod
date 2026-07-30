using HorusMod.Logging;
using HorusMod.Networking;

namespace HorusMod.Interaction
{
    public static class HorusUnitEditor
    {
        public static bool TrySetLoadout(Aircraft aircraft, int presetIndex)
        {
            if (!HorusPermissions.CanSpawn() || aircraft == null) return false;
            AircraftDefinition definition = aircraft.definition as AircraftDefinition;
            AircraftParameters parameters = definition != null ? definition.aircraftParameters : null;
            if (parameters?.StandardLoadouts == null || presetIndex < 0 || presetIndex >= parameters.StandardLoadouts.Length) return false;
            StandardLoadout preset = parameters.StandardLoadouts[presetIndex];
            if (preset?.loadout == null) return false;
            aircraft.Networkloadout = preset.loadout;
            HorusLog.Verbose("UnitEditor", $"Changed loadout on '{aircraft.unitName}' to '{preset.Name}'.");
            return true;
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
