using System;
using System.Collections.Generic;
using System.Text;

namespace HorusMod.Shared
{
    public static class HorusWireText
    {
        private static readonly UTF8Encoding ReplacementUtf8 = new UTF8Encoding(false, false);
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool IsValid(string value, int maxBytes = HorusProtocol.MaxStringBytes)
        {
            if (value == null || maxBytes < 0) return false;
            try
            {
                return StrictUtf8.GetByteCount(value) <= maxBytes;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        public static string Clamp(string value, int maxBytes = HorusProtocol.MaxStringBytes)
        {
            if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            byte[] bytes = ReplacementUtf8.GetBytes(value ?? "");
            if (bytes.Length <= maxBytes) return ReplacementUtf8.GetString(bytes);

            int length = maxBytes;
            while (length > 0 && (bytes[length] & 0xC0) == 0x80) length--;
            return ReplacementUtf8.GetString(bytes, 0, length);
        }

        public static string SanitizeVisible(string value, int maxBytes = HorusProtocol.MaxStringBytes)
        {
            if(value==null)return "";
            var clean=new StringBuilder(value.Length);
            foreach(char character in value)clean.Append(char.IsControl(character)?' ':character);
            return Clamp(clean.ToString(),maxBytes);
        }

        public static bool IsStableKey(string value, int maxBytes = HorusProtocol.MaxStringBytes)
        {
            if(!IsValid(value,maxBytes))return false;
            for(int i=0;i<value.Length;i++)if(char.IsControl(value[i]))return false;
            return true;
        }
    }

    public static class HorusOwnershipPolicy
    {
        public static bool CanMutate(bool isHorusOwned, bool allowOriginalMissionMutation)
            => isHorusOwned || allowOriginalMissionMutation;
    }

    public static class HorusSnapshotPolicy
    {
        public static bool IsValidPageShape(HorusStatePage page)
        {
            if (page == null || page.SessionId == Guid.Empty || page.SnapshotId == Guid.Empty) return false;
            if (page.PageCount < 1 || page.PageCount > HorusProtocol.MaxSnapshotPages) return false;
            if (page.PageIndex < 0 || page.PageIndex >= page.PageCount) return false;
            if((page.RtsMode!=-1&&!Enum.IsDefined(typeof(HorusModeWire),page.RtsMode))||(page.RtsDeployMode!=-1&&!Enum.IsDefined(typeof(HorusDeployModeWire),page.RtsDeployMode)))return false;
            if((page.RtsMode==-1)!=(page.RtsDeployMode==-1))return false;
            if (page.Units.Count > HorusProtocol.MaxSnapshotUnitsPerPage) return false;
            if (page.Factories.Count > HorusProtocol.MaxSnapshotFactoriesPerPage) return false;
            if (page.Budgets.Count > HorusProtocol.MaxSnapshotBudgetsPerPage) return false;
            foreach(HorusUnitState unit in page.Units)
                if(unit==null||unit.UnitId==0||string.IsNullOrWhiteSpace(unit.DefinitionKey)||!HorusWireText.IsStableKey(unit.DefinitionKey)||!HorusWireText.IsStableKey(unit.Name)||!unit.Position.IsFinite||!IsWorldBounded(unit.Position))return false;
            foreach(HorusFactoryState factory in page.Factories)
            {
                if(factory==null||string.IsNullOrWhiteSpace(factory.FactoryId)||string.IsNullOrWhiteSpace(factory.PresetName)||!HorusWireText.IsStableKey(factory.FactoryId)||!HorusWireText.IsStableKey(factory.PresetName)||!HorusWireText.IsStableKey(factory.LastStatus)||factory.FactionIndex<0)return false;
                if(!factory.Position.IsFinite||!factory.RallyPoint.IsFinite||!IsWorldBounded(factory.Position)||!IsWorldBounded(factory.RallyPoint))return false;
                if(!HorusPersistencePolicy.IsFinite(factory.Yaw)||!HorusPersistencePolicy.IsFinite(factory.IncomePerMinute)||!HorusPersistencePolicy.IsFinite(factory.ProductionIntervalSeconds)||!HorusPersistencePolicy.IsFinite(factory.ProductionTimer)||!HorusPersistencePolicy.IsFinite(factory.SpawnRadius))return false;
                if(factory.IncomePerMinute<0f||factory.IncomePerMinute>1000000000f||factory.ProductionIntervalSeconds<0f||factory.ProductionIntervalSeconds>86400f||factory.ProductionTimer<0f||factory.ProductionTimer>86400f||factory.MaxActiveProducedUnits<0||factory.MaxActiveProducedUnits>1000||factory.SpawnRadius<0f||factory.SpawnRadius>100000f)return false;
                if(!HorusPersistencePolicy.IsSafeStringCollection(factory.ProductionKeys,HorusProtocol.MaxMounts,out _))return false;
                if(factory.ProductionKeys.Count==0?factory.CurrentProductionIndex!=0:factory.CurrentProductionIndex<0||factory.CurrentProductionIndex>=factory.ProductionKeys.Count)return false;
            }
            foreach(HorusBudgetState budget in page.Budgets)
                if(budget==null||budget.FactionIndex<0||!HorusPersistencePolicy.IsFinite(budget.Budget)||!HorusPersistencePolicy.IsFinite(budget.IncomePerTick)||budget.Budget<0f||budget.Budget>1000000000f||Math.Abs(budget.IncomePerTick)>1000000000f||budget.UnitCap<0||budget.UnitCap>100000||budget.ActiveUnitCount<0)return false;
            return true;
        }

        public static bool IsCoherentSnapshot(IEnumerable<HorusStatePage> pages)
        {
            if(pages==null)return false;
            var indexes=new HashSet<int>();var units=new HashSet<uint>();var factories=new HashSet<string>(StringComparer.Ordinal);var budgets=new HashSet<int>();
            Guid session=Guid.Empty,snapshot=Guid.Empty;ulong revision=0;int pageCount=0,actualCount=0,headers=0;
            foreach(HorusStatePage page in pages)
            {
                if(!IsValidPageShape(page))return false;
                if(actualCount++==0){session=page.SessionId;snapshot=page.SnapshotId;revision=page.Revision;pageCount=page.PageCount;}
                else if(page.SessionId!=session||page.SnapshotId!=snapshot||page.Revision!=revision||page.PageCount!=pageCount)return false;
                if(!indexes.Add(page.PageIndex))return false;
                if(page.RtsMode>=0)headers++;
                foreach(HorusUnitState unit in page.Units)if(!units.Add(unit.UnitId))return false;
                foreach(HorusFactoryState factory in page.Factories)if(!factories.Add(factory.FactoryId))return false;
                foreach(HorusBudgetState budget in page.Budgets)if(!budgets.Add(budget.FactionIndex))return false;
            }
            return actualCount==pageCount&&indexes.Count==pageCount&&headers==1;
        }

        private enum HorusModeWire { Sandbox=0,RtsCommander=1 }
        private enum HorusDeployModeWire { FreePlacementPaid=0,StrictBaseDeployment=1 }

        private static bool IsWorldBounded(HorusVector3 value)=>Math.Abs(value.X)<=100000000f&&Math.Abs(value.Y)<=100000000f&&Math.Abs(value.Z)<=100000000f;
    }

    public static class HorusPersistencePolicy
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool IsSafePosition(float x, float y, float z)
            => IsFinite(x) && IsFinite(y) && IsFinite(z) &&
               Math.Abs(x) <= 100000000f && Math.Abs(y) <= 100000000f && Math.Abs(z) <= 100000000f;

        public static bool IsSafeStringCollection(IEnumerable<string> values, int maxCount, out int totalBytes)
        {
            totalBytes = 0;
            if (values == null || maxCount < 0) return false;
            int count = 0;
            foreach (string value in values)
            {
                if (++count > maxCount || string.IsNullOrWhiteSpace(value) || !HorusWireText.IsStableKey(value)) return false;
                try { totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(value)); }
                catch (OverflowException) { return false; }
                if (totalBytes > HorusProtocol.MaxStringListBytes) return false;
            }
            return true;
        }
    }

    public static class HorusEconomyPolicy
    {
        public const float MaxBudget = 1000000000f;
        public const float MaxIncomePerTick = 1000000f;
        public const float MaxUnitCost = 1000000000f;
        public const float MaxUnitCostMultiplier = 10000f;
        public const float MaxIncomeTickSeconds = 3600f;
        public const int MaxUnitCap = 100000;
        public const int MaxConfigEntries = 256;
        public const int MaxConfigFileBytes = 1048576;

        public static bool IsValidBudget(float value)
            => HorusPersistencePolicy.IsFinite(value) && value >= 0f && value <= MaxBudget;

        public static bool IsValidIncome(float value)
            => HorusPersistencePolicy.IsFinite(value) && value >= 0f && value <= MaxIncomePerTick;

        public static bool IsValidUnitCost(float value)
            => HorusPersistencePolicy.IsFinite(value) && value >= 0f && value <= MaxUnitCost;

        public static bool IsValidMultiplier(float value)
            => HorusPersistencePolicy.IsFinite(value) && value > 0f && value <= MaxUnitCostMultiplier;

        public static bool IsValidTickSeconds(float value)
            => HorusPersistencePolicy.IsFinite(value) && value >= 1f && value <= MaxIncomeTickSeconds;

        public static bool IsValidUnitCap(int value) => value >= 0 && value <= MaxUnitCap;

        public static bool TryAddBudget(float current, float delta, out float result)
        {
            result = 0f;
            if (!IsValidBudget(current) || !HorusPersistencePolicy.IsFinite(delta)) return false;
            double next = (double)current + delta;
            if (next < 0d || next > MaxBudget) return false;
            result = (float)next;
            return IsValidBudget(result);
        }

        public static bool TryAddUnitCost(float current, float addition, out float result)
        {
            result = 0f;
            if (!IsValidUnitCost(current) || !IsValidUnitCost(addition)) return false;
            double next = (double)current + addition;
            if (next > MaxUnitCost) return false;
            result = (float)next;
            return IsValidUnitCost(result);
        }
    }

    public static class HorusFactoryPolicy
    {
        public const int MaxFactoriesPerFaction = 100;
        public const int MaxPresets = 64;
        public const int MaxActiveProducedUnits = 1000;
        public const float MaxIncomePerMinute = 1000000000f;
        public const float MaxProductionIntervalSeconds = 86400f;
        public const float MaxSpawnRadius = 100000f;

        public static bool IsValidFactoryLimit(int value) => value >= 1 && value <= MaxFactoriesPerFaction;

        public static bool IsValidIncome(float value)
            => HorusPersistencePolicy.IsFinite(value) && value >= 0f && value <= MaxIncomePerMinute;

        public static bool IsValidProduction(float intervalSeconds, int maxActiveProducedUnits, bool producesUnits)
        {
            if (!HorusPersistencePolicy.IsFinite(intervalSeconds) || intervalSeconds < 0f || intervalSeconds > MaxProductionIntervalSeconds) return false;
            if (maxActiveProducedUnits < 0 || maxActiveProducedUnits > MaxActiveProducedUnits) return false;
            return !producesUnits || (intervalSeconds >= 1f && maxActiveProducedUnits >= 1);
        }

        public static bool IsValidRuntimeNumbers(float yaw, float incomePerMinute, float intervalSeconds, float timer,
            int maxActiveProducedUnits, float spawnRadius, bool producesUnits)
            => HorusPersistencePolicy.IsFinite(yaw) && IsValidIncome(incomePerMinute) &&
               IsValidProduction(intervalSeconds, maxActiveProducedUnits, producesUnits) &&
               HorusPersistencePolicy.IsFinite(timer) && timer >= 0f && timer <= MaxProductionIntervalSeconds &&
               HorusPersistencePolicy.IsFinite(spawnRadius) && spawnRadius >= 0f && spawnRadius <= MaxSpawnRadius;
    }

    public static class HorusResponsePolicy
    {
        public static bool IsValidCapabilities(HorusCapabilities value)
        {
            if(value==null||value.ProtocolVersion!=HorusProtocol.Version||value.SessionId==Guid.Empty||!Enum.IsDefined(typeof(HorusResultCode),value.Result)||string.IsNullOrWhiteSpace(value.ServerVersion)||!HorusWireText.IsStableKey(value.ServerVersion)||!HorusWireText.IsStableKey(value.Message))return false;
            return (value.Features & ~HorusCapability.FullParity)==0&&value.Authorized==(value.Result==HorusResultCode.Accepted);
        }

        public static bool IsValidResult(HorusCommandResult value)
        {
            if(value==null||value.RequestId==Guid.Empty||value.SessionId==Guid.Empty||!Enum.IsDefined(typeof(HorusCommandKind),value.Command)||value.Command==HorusCommandKind.None||!Enum.IsDefined(typeof(HorusResultCode),value.Result)||!HorusWireText.IsStableKey(value.Message)||value.AffectedUnitIds.Count>HorusProtocol.MaxEntitiesPerCommand)return false;
            var seen=new HashSet<uint>();foreach(uint id in value.AffectedUnitIds)if(id==0||!seen.Add(id))return false;return true;
        }

        public static bool IsValidEvent(HorusStateEvent value)
            => value!=null&&value.SessionId!=Guid.Empty&&IsValidResult(value.Result)&&value.Result.SessionId==value.SessionId&&value.Result.Revision==value.Revision;
    }
}
