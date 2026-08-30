using System;
using System.Reflection;
using HarmonyLib;
using HorusMod.Logging;
using UnityEngine;

namespace HorusMod.Server
{
    /// <summary>Headless-safe gameplay patches required by server-spawned Live Ordnance.</summary>
    internal static class HorusServerOrdnancePatches
    {
        public static void Apply(Harmony harmony)
        {
            Patch(harmony,typeof(Missile),"LocalStart",nameof(MissileLocalStartPrefix));
            Patch(harmony,typeof(ARHSeeker),"SlowChecks",nameof(ArhSlowChecksPrefix));
            Patch(harmony,typeof(OpticalSeekerCruiseMissile),"PreTerminalMode",nameof(OpticalPreTerminalPrefix));
            Patch(harmony,typeof(OpticalSeekerCruiseMissile),"SlowChecks",nameof(OpticalSlowChecksPrefix));
        }

        private static void Patch(Harmony harmony,Type type,string method,string prefix)
        {
            MethodInfo original=AccessTools.Method(type,method);
            MethodInfo replacement=typeof(HorusServerOrdnancePatches).GetMethod(prefix,BindingFlags.Static|BindingFlags.NonPublic);
            if(original==null||replacement==null){HorusLog.Warning("Server","Could not patch "+type.Name+"."+method+" for Live Ordnance.");return;}
            harmony.Patch(original,new HarmonyMethod(replacement));
        }

        private static bool MissileLocalStartPrefix(Missile __instance)
        {
            if(__instance==null||__instance.GetComponent<MissileSeeker>()!=null)return true;
            if(GameManager.gameState==GameState.SinglePlayer||GameManager.gameState==GameState.Multiplayer)
            {
                Unit owner=__instance.owner;
                GlobalPosition origin=owner!=null?owner.transform.GlobalPosition():__instance.transform.GlobalPosition();
                Vector3 forward=owner!=null?owner.transform.forward:__instance.transform.forward;
                __instance.SetAimpoint(origin+forward*100000f,Vector3.zero);
            }
            return false;
        }

        private static readonly FieldInfo TargetUnitField=typeof(MissileSeeker).GetField("targetUnit",BindingFlags.NonPublic|BindingFlags.Instance);
        private static readonly FieldInfo MissileField=typeof(MissileSeeker).GetField("missile",BindingFlags.NonPublic|BindingFlags.Instance);
        private static bool HasNoTarget(MissileSeeker seeker)=>seeker!=null&&TargetUnitField!=null&&TargetUnitField.GetValue(seeker)==null;
        private static bool ArhSlowChecksPrefix(ARHSeeker __instance)=>!HasNoTarget(__instance);
        private static bool OpticalPreTerminalPrefix(OpticalSeekerCruiseMissile __instance)=>!HasNoTarget(__instance);
        private static bool OpticalSlowChecksPrefix(OpticalSeekerCruiseMissile __instance)
        {
            if(!HasNoTarget(__instance))return true;
            Missile missile=MissileField?.GetValue(__instance) as Missile;
            if(missile==null||missile.disabled)return false;
            missile.UpdateRadarAlt();
            if(missile.timeSinceSpawn>10f&&(missile.LosingGround()||missile.MissedTarget()||missile.speed<100f))missile.Detonate(missile.rb.velocity,false,false);
            if(!missile.IsTangible()&&missile.timeSinceSpawn>2f)missile.SetTangible(true);
            return false;
        }
    }
}
