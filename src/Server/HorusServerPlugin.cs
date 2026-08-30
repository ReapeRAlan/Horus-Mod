using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HorusMod.Compat;
using HorusMod.Interaction;
using HorusMod.Logging;
using HorusMod.Shared;
using UnityEngine;

namespace HorusMod.Server
{
    [BepInPlugin(PluginGuid,PluginName,BepInVersion)]
    [BepInDependency("MaxWasUnavailable.Nuclei",BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class HorusServerPlugin:BaseUnityPlugin
    {
        public const string PluginGuid="com.reaperalan.horusmod.server";
        public const string PluginName="Horus Dedicated Server";
        // BepInEx 5 requires a numeric System.Version here; retain the RC label separately.
        public const string BepInVersion="2.0.0";
        public const string PluginVersion="2.0.0-rc.1";
        private ConfigFile serverConfig;
        private ConfigEntry<bool> enabledEntry;
        private ConfigEntry<string> allowlistPathEntry;
        private ConfigEntry<bool> allowMissionDeleteEntry;
        private ConfigEntry<int> auditRetentionEntry;
        private HorusAdminAllowlist allowlist=new HorusAdminAllowlist();
        private DateTime allowlistWriteTime;
        private HorusServerState state;
        private HorusServerCommandExecutor executor;
        private HorusServerTransport transport;
        private bool wasMissionReady;
        private float nextNucleiRetry;

        private void Awake()
        {
            HorusMod.HorusPlugin.Logger=base.Logger;
            serverConfig=new ConfigFile(Path.Combine(Paths.ConfigPath,"Horus.Server.cfg"),true);
            enabledEntry=serverConfig.Bind("Server","Enabled",false,"Enable authenticated Horus dedicated-server control.");
            allowlistPathEntry=serverConfig.Bind("Security","AdminAllowlistPath",Path.Combine(Paths.ConfigPath,"HorusMod","dedicated_admins.txt"),"UTF-8 file with one exact SteamID64 per line.");
            allowMissionDeleteEntry=serverConfig.Bind("Safety","AllowMissionUnitDelete",false,"Allow deletion of units not created by Horus.");
            auditRetentionEntry=serverConfig.Bind("Audit","RetentionDays",14,"Daily JSONL audit retention.");
            HorusMod.HorusPlugin.LogVerbosity=serverConfig.Bind("Diagnostics","LogVerbosity",HorusLogLevel.Normal,"Quiet, Normal, Verbose, or Trace.");
            HorusMod.HorusPlugin.AllowIncompatibleContent=serverConfig.Bind("Safety","AllowIncompatibleContent",false,"Allow lookup-only definitions; unsafe for clients without matching content.");
            HorusMod.HorusPlugin.ImproveAIBombingAccuracy=serverConfig.Bind("Gameplay","ImproveAIBombingAccuracy",true,"Apply server-authoritative AI bombing correction.");
            HorusMod.HorusPlugin.EnableRtsIncome=serverConfig.Bind("RTS","EnableIncome",true,"Enable RTS income.");
            HorusMod.HorusPlugin.EnableRtsUnitCap=serverConfig.Bind("RTS","EnableUnitCap",true,"Enable RTS unit caps.");
            HorusMod.HorusPlugin.SyncWithFactionBudget=serverConfig.Bind("RTS","SyncWithFactionBudget",false,"Sync Horus RTS budgets with faction budgets.");
            HorusMod.HorusPlugin.AllowGroupPurchasesInRtsMode=serverConfig.Bind("RTS","AllowGroupPurchases",false,"Allow group purchases.");
            HorusMod.HorusPlugin.EnableStrictBaseDeployment=serverConfig.Bind("RTS","StrictBaseDeployment",false,"Require friendly base proximity.");
            HorusMod.HorusPlugin.BaseDeploymentRadius=serverConfig.Bind("RTS","BaseDeploymentRadius",3000f,"Strict deployment radius.");
            HorusMod.HorusPlugin.ShipSpawnLift=serverConfig.Bind("Placement","ShipSpawnLift",3f,"Ship spawn lift.");
            EnsureAllowlistFile();ReloadAllowlist(true);GameApi.Initialize();
            try{var harmony=new HarmonyLib.Harmony(PluginGuid);HorusTacticalHarmonyPatches.Apply(harmony);HorusBombingCorrection.Apply(harmony);HorusServerOrdnancePatches.Apply(harmony);}catch(Exception ex){HorusLog.Warning("Server","Gameplay patch setup failed open: "+ex.Message);}
            state=new HorusServerState();executor=new HorusServerCommandExecutor(state,this,allowMissionDeleteEntry.Value);
            var audit=new HorusAuditWriter(Path.Combine(Paths.ConfigPath,"HorusMod","audit"),auditRetentionEntry.Value);
            transport=new HorusServerTransport(state,executor,()=>enabledEntry.Value,()=>allowlist,audit);
            HorusLog.Info("Server",$"{PluginName} {PluginVersion} loaded. headless={GameManager.IsHeadless}, batch={Application.isBatchMode}, enabled={enabledEntry.Value}, admins={allowlist.Count}.");
        }

        private void Update()
        {
            ReloadAllowlist(false);
            // A full/local package may contain Horus.Server.dll, but it must never
            // register a server transport from an ordinary remote game client.
            if(!Application.isBatchMode&&!GameManager.IsHeadless&&!HorusMod.Networking.HorusPermissions.IsServer())return;
            bool missionReady=HorusMod.Networking.HorusPermissions.InMission()&&!NuclearOption.Networking.NetworkManagerNuclearOption.IsLoadingScene&&Spawner.i!=null;
            if(missionReady&&!wasMissionReady){transport.ResetMission();HorusLog.Info("Server","Mission session initialized: "+state.SessionId);}
            wasMissionReady=missionReady;
            transport.Tick();
            if(enabledEntry.Value&&missionReady)executor.Tick();
            if(Time.unscaledTime>=nextNucleiRetry){nextNucleiRetry=Time.unscaledTime+5f;HorusNucleiBridge.TryRegister(BuildNucleiStatus,BuildNucleiDiagnostics);}
        }
        private void OnDestroy(){transport?.Unregister();}

        private void EnsureAllowlistFile()
        {
            string path=allowlistPathEntry.Value;string directory=Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
            if(!File.Exists(path))File.WriteAllText(path,"# Horus dedicated administrators\r\n# One exact SteamID64 per line. Empty means deny all mutations.\r\n");
        }
        private void ReloadAllowlist(bool force)
        {
            try
            {
                string path=allowlistPathEntry.Value;DateTime write=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;
                if(!force&&write==allowlistWriteTime)return;
                allowlist=HorusAdminAllowlist.Parse(File.Exists(path)?File.ReadAllLines(path):Array.Empty<string>(),out var errors);allowlistWriteTime=write;
                foreach(string error in errors)HorusLog.Warning("Security","Allowlist: "+error);
                HorusLog.Info("Security","Loaded "+allowlist.Count+" dedicated administrator(s).");
            }
            catch(Exception ex){allowlist=new HorusAdminAllowlist();HorusLog.Error("Security","Allowlist failed closed: "+ex.Message);}
        }
        private string BuildNucleiStatus()=>"Horus "+PluginVersion+" | enabled="+enabledEntry.Value+" | mission="+(MissionManager.IsRunning?MissionManager.CurrentMission?.Name??"loading":"none")+" | revision="+state.Revision+" | authorized GM="+transport.AuthorizedClientCount;
        private string BuildNucleiDiagnostics()=>"Horus protocol="+HorusProtocol.Version+" | session="+state.SessionId+" | clients="+transport.ConnectedClientCount+" | queued="+transport.PendingCommandCount+" | admins="+allowlist.Count+" | headless="+GameManager.IsHeadless;
    }
}
