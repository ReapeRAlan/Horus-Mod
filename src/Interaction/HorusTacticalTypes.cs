using System;

namespace HorusMod.Interaction
{
    public enum HorusOrderKind
    {
        None = 0,
        Move,
        Hold,
        AttackTarget,
        AttackMove,
        Patrol,
        Guard
    }

    public enum HorusRulesOfEngagement
    {
        WeaponsFree = 0,
        HoldFire
    }

    public enum HorusGroupOrderTargetMode
    {
        None = 0,
        Move,
        AttackMove,
        Patrol,
        AttackTarget,
        Guard
    }

    public static class HorusGroupOrderTargetPolicy
    {
        public static bool RequiresUnit(HorusGroupOrderTargetMode mode)
        {
            return mode == HorusGroupOrderTargetMode.AttackTarget ||
                mode == HorusGroupOrderTargetMode.Guard;
        }

        public static string Prompt(HorusGroupOrderTargetMode mode)
        {
            switch (mode)
            {
                case HorusGroupOrderTargetMode.Move: return "MOVE: LMB a destination | RMB/Esc cancel";
                case HorusGroupOrderTargetMode.AttackMove: return "ATTACK-MOVE: LMB a destination | RMB/Esc cancel";
                case HorusGroupOrderTargetMode.Patrol: return "PATROL: LMB the first waypoint | RMB/Esc cancel";
                case HorusGroupOrderTargetMode.AttackTarget: return "ATTACK TARGET: LMB a known enemy | RMB/Esc cancel";
                case HorusGroupOrderTargetMode.Guard: return "GUARD / ESCORT: LMB a friendly unit | RMB/Esc cancel";
                default: return string.Empty;
            }
        }
    }

    /// <summary>Pure route progression policy used by runtime orders and logic tests.</summary>
    public static class TacticalRouteCursor
    {
        public static int Next(int currentIndex, int waypointCount, bool loop)
        {
            if (waypointCount <= 0) return -1;
            int next = currentIndex + 1;
            if (next < waypointCount) return next;
            return loop ? 0 : waypointCount - 1;
        }
    }

    public enum TacticalEngagementAction
    {
        MaintainNavigation = 0,
        EnterCombat,
        StayInCombat,
        ResumeNavigation
    }

    public static class TacticalEngagementPolicy
    {
        public static TacticalEngagementAction Decide(bool engaging, bool threatVisible, float secondsSinceThreat, float resumeGraceSeconds)
        {
            if (threatVisible) return engaging ? TacticalEngagementAction.StayInCombat : TacticalEngagementAction.EnterCombat;
            if (!engaging) return TacticalEngagementAction.MaintainNavigation;
            return secondsSinceThreat < resumeGraceSeconds
                ? TacticalEngagementAction.StayInCombat
                : TacticalEngagementAction.ResumeNavigation;
        }
    }
}
