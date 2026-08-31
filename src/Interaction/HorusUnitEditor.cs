using HorusMod.Logging;
using HorusMod.Loadouts;
using HorusMod.Networking;
#if HORUS_CLIENT
using HorusMod.Client;
using HorusMod.Shared;
#endif

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
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession && aircraft != null)
            {
                var payload=new HorusCommandPayload{SecondaryKey="standard",IntValue=presetIndex};payload.UnitIds.Add(aircraft.persistentID.Id);
                bool sent=HorusRemoteAuthority.TrySubmit(HorusCommandKind.SetLoadout,payload);
                return new LoadoutApplyResult(sent?LoadoutApplyStatus.Success:LoadoutApplyStatus.NotAuthorized,sent?"Dedicated loadout request sent":HorusRemoteAuthority.Status,null,aircraft.NetworkfuelLevel,-1);
            }
#endif
            LoadoutApplyResult result = HorusLoadoutService.ApplyStandardPreset(aircraft, presetIndex);
            if (!result.Success)
                HorusLog.Verbose("UnitEditor", $"Loadout preset {presetIndex} was not applied: {result.Message}");
            return result;
        }

        public static LoadoutApplyResult TrySetLoadout(Aircraft aircraft, LoadoutDraft draft)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession && aircraft != null && draft != null)
            {
                var payload=new HorusCommandPayload{FloatValue=draft.FuelRatio,IntValue=draft.LiveryIndex};
                payload.UnitIds.Add(aircraft.persistentID.Id);payload.MountKeys.AddRange(draft.WeaponMountJsonKeys);
                bool sent=HorusRemoteAuthority.TrySubmit(HorusCommandKind.SetLoadout,payload);
                return new LoadoutApplyResult(sent?LoadoutApplyStatus.Success:LoadoutApplyStatus.NotAuthorized,sent?"Dedicated loadout request sent":HorusRemoteAuthority.Status,null,draft.FuelRatio,draft.LiveryIndex);
            }
#endif
            LoadoutApplyResult result = HorusLoadoutService.ApplyToAircraft(aircraft, draft);
            if (!result.Success)
                HorusLog.Verbose("UnitEditor", $"Custom loadout was not applied: {result.Message}");
            return result;
        }

        public static bool TrySetLivery(Aircraft aircraft, int index)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession && aircraft != null) { var payload=new HorusCommandPayload{IntValue=index};payload.UnitIds.Add(aircraft.persistentID.Id);return HorusRemoteAuthority.TrySubmit(HorusCommandKind.SetLivery,payload); }
#endif
            if (!HorusPermissions.CanSpawn() || aircraft == null) return false;
            AircraftDefinition definition = aircraft.definition as AircraftDefinition;
            AircraftParameters parameters = definition != null ? definition.aircraftParameters : null;
            if (parameters?.liveries == null || index < 0 || index >= parameters.liveries.Count) return false;
            aircraft.SetLiveryKey(new LiveryKey(index), true);
            return true;
        }

        public static void SetSkill(Unit unit, float skill)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession && unit != null) { var payload=new HorusCommandPayload{FloatValue=skill};payload.UnitIds.Add(unit.persistentID.Id);HorusRemoteAuthority.TrySubmit(HorusCommandKind.SetSkill,payload);return; }
#endif
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
