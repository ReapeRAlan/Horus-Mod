using System;
using System.IO;
using System.Text;
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
        private ConfigEntry<bool> allowMissionMutationEntry;
        private ConfigEntry<int> auditRetentionEntry;
        private HorusAdminAllowlist allowlist=new HorusAdminAllowlist();
        private DateTime allowlistWriteTime;
        private HorusServerState state;
        private HorusServerCommandExecutor executor;
        private HorusServerTransport transport;
        private bool wasMissionReady;
        private float nextNucleiRetry;
        private bool lastEnabled;
        private float nextAuthorizationRefresh;
        private bool runtimeActive;
        private float nextActivationRetry;
        private HarmonyLib.Harmony harmony;

        private void Awake()
        {
            HorusMod.HorusPlugin.Logger=base.Logger;
            serverConfig=new ConfigFile(Path.Combine(Paths.ConfigPath,"Horus.Server.cfg"),true);
            enabledEntry=serverConfig.Bind("Server","Enabled",false,"Enable authenticated Horus dedicated-server control.");
            HorusMod.HorusPlugin.ServerEnabled=enabledEntry;
            lastEnabled=enabledEntry.Value;
            allowlistPathEntry=serverConfig.Bind("Security","AdminAllowlistPath",Path.Combine(Paths.ConfigPath,"HorusMod","dedicated_admins.txt"),"UTF-8 file with one exact SteamID64 per line.");
            allowMissionDeleteEntry=serverConfig.Bind("Safety","AllowMissionUnitDelete",false,"Allow deletion of units not created by Horus.");
            allowMissionMutationEntry=serverConfig.Bind("Safety","AllowMissionUnitMutation",false,"Allow orders and editing on units not created by Horus.");
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
            NormalizeBoundedConfiguration();
            EnsureAllowlistFile();
            HorusLog.Info("Server",$"{PluginName} {PluginVersion} loaded dormant. Runtime activation requires headless mode, batch mode, or native server authority.");
        }

        private void Update()
        {
            bool shouldRun=Application.isBatchMode||GameManager.IsHeadless||HorusMod.Networking.HorusPermissions.IsServer();
            if(!shouldRun){if(runtimeActive)DeactivateRuntime();return;}
            if(!runtimeActive)
            {
                if(Time.unscaledTime<nextActivationRetry)return;
                try{ActivateRuntime();}
                catch(Exception ex){DeactivateRuntime();nextActivationRetry=Time.unscaledTime+5f;HorusLog.Error("Server","Server runtime activation failed closed: "+ex.Message);return;}
            }
            ReloadAllowlist(false);
            if(lastEnabled!=enabledEntry.Value){lastEnabled=enabledEntry.Value;transport.RefreshAuthorization(true);}
            bool missionReady=HorusMod.Networking.HorusPermissions.InMission()&&!NuclearOption.Networking.NetworkManagerNuclearOption.IsLoadingScene&&Spawner.i!=null;
            if(missionReady&&!wasMissionReady){transport.ResetMission();HorusLog.Info("Server","Mission session initialized: "+state.SessionId);}
            wasMissionReady=missionReady;
            transport.Tick();
            if(Time.unscaledTime>=nextAuthorizationRefresh){nextAuthorizationRefresh=Time.unscaledTime+1f;transport.RefreshAuthorization();}
            if(enabledEntry.Value&&missionReady)executor.Tick();
            if(Time.unscaledTime>=nextNucleiRetry){nextNucleiRetry=Time.unscaledTime+5f;HorusNucleiBridge.TryRegister(BuildNucleiStatus,BuildNucleiDiagnostics);}
        }

        private void ActivateRuntime()
        {
            ReloadAllowlist(true);GameApi.Initialize();
            harmony=new HarmonyLib.Harmony(PluginGuid);HorusTacticalHarmonyPatches.Apply(harmony);HorusBombingCorrection.Apply(harmony);HorusServerOrdnancePatches.Apply(harmony);
            state=new HorusServerState();executor=new HorusServerCommandExecutor(state,this,allowMissionDeleteEntry.Value,allowMissionMutationEntry.Value);
            var audit=new HorusAuditWriter(Path.Combine(Paths.ConfigPath,"HorusMod","audit"),auditRetentionEntry.Value);
            transport=new HorusServerTransport(state,executor,()=>enabledEntry.Value,()=>allowlist,audit);
            lastEnabled=enabledEntry.Value;wasMissionReady=false;nextAuthorizationRefresh=0f;runtimeActive=true;
            HorusLog.Info("Server",$"{PluginName} {PluginVersion} runtime activated. headless={GameManager.IsHeadless}, batch={Application.isBatchMode}, enabled={enabledEntry.Value}, admins={allowlist.Count}.");
        }

        private void DeactivateRuntime()
        {
            try{transport?.Unregister();}catch{}
            try{executor?.ResetMission();executor?.Dispose();}catch{}
            try{harmony?.UnpatchSelf();}catch{}
            transport=null;executor=null;state=null;harmony=null;runtimeActive=false;wasMissionReady=false;
        }

        private void OnDestroy(){DeactivateRuntime();}

        private void NormalizeBoundedConfiguration()
        {
            if(!Enum.IsDefined(typeof(HorusLogLevel),HorusMod.HorusPlugin.LogVerbosity.Value))HorusMod.HorusPlugin.LogVerbosity.Value=HorusLogLevel.Normal;
            float radius=HorusMod.HorusPlugin.BaseDeploymentRadius.Value;
            if(!HorusPersistencePolicy.IsFinite(radius)||radius<1f||radius>100000f)HorusMod.HorusPlugin.BaseDeploymentRadius.Value=3000f;
            float lift=HorusMod.HorusPlugin.ShipSpawnLift.Value;
            if(!HorusPersistencePolicy.IsFinite(lift)||lift<0f||lift>1000f)HorusMod.HorusPlugin.ShipSpawnLift.Value=3f;
            if(auditRetentionEntry.Value<1||auditRetentionEntry.Value>365)auditRetentionEntry.Value=14;
        }

        private void EnsureAllowlistFile()
        {
            string path=allowlistPathEntry.Value;string directory=Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
            if(!File.Exists(path))File.WriteAllText(path,"# Horus dedicated administrators\r\n# One exact SteamID64 per line. Empty means deny all mutations.\r\n");
        }
        private void ReloadAllowlist(bool force)
        {
            string path=allowlistPathEntry.Value;
            try
            {
                DateTime write=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;
                if(!force&&write==allowlistWriteTime)return;
                if(File.Exists(path)&&new FileInfo(path).Length>HorusEconomyPolicy.MaxConfigFileBytes)throw new InvalidDataException("Allowlist file is oversized.");
                string[] lines=File.Exists(path)?File.ReadAllLines(path,new UTF8Encoding(false,true)):Array.Empty<string>();
                allowlist=HorusAdminAllowlist.Parse(lines,out var errors);allowlistWriteTime=write;
                foreach(string error in errors)HorusLog.Warning("Security","Allowlist: "+error);
                HorusLog.Info("Security","Loaded "+allowlist.Count+" dedicated administrator(s).");
                transport?.RefreshAuthorization();
            }
            catch(Exception ex){allowlist=new HorusAdminAllowlist();try{allowlistWriteTime=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;}catch{}HorusLog.Error("Security","Allowlist failed closed: "+ex.Message);}
        }
        private string BuildNucleiStatus()=>"Horus "+PluginVersion+" | enabled="+enabledEntry.Value+" | mission="+(MissionManager.IsRunning?MissionManager.CurrentMission?.Name??"loading":"none")+" | revision="+(state?.Revision??0)+" | authorized GM="+(transport?.AuthorizedClientCount??0);
        private string BuildNucleiDiagnostics()=>"Horus protocol="+HorusProtocol.Version+" | session="+(state?.SessionId??Guid.Empty)+" | clients="+(transport?.ConnectedClientCount??0)+" | queued="+(transport?.PendingCommandCount??0)+" | admins="+allowlist.Count+" | headless="+GameManager.IsHeadless;
    }
}
