using UnityEngine;

namespace HorusMod.Data
{
    public readonly struct FactionSlot
    {
        public int Index { get; }
        public bool IsNeutral { get; }
        public bool IsValid { get; }
        public Faction Faction { get; }
        public FactionHQ HQ { get; }
        public Color Color { get; }
        public string DisplayName { get; }

        private FactionSlot(int index, bool neutral, bool valid, Faction faction, FactionHQ hq, Color color, string displayName)
        {
            Index = index;
            IsNeutral = neutral;
            IsValid = valid;
            Faction = faction;
            HQ = hq;
            Color = color;
            DisplayName = displayName;
        }

        public static FactionSlot Resolve(int index)
        {
            var factions = FactionRegistry.factions;
            int count = factions != null ? factions.Count : 0;
            if (index == count)
            {
                return new FactionSlot(index, true, true, null, null, new Color(0.72f, 0.74f, 0.78f), "Neutral (Unassigned)");
            }

            if (factions == null || index < 0 || index >= count)
            {
                return new FactionSlot(index, false, false, null, null, Color.gray, "Invalid");
            }

            Faction faction = factions[index];
            return new FactionSlot(
                index,
                false,
                faction != null,
                faction,
                faction != null ? FactionRegistry.HQFromFaction(faction) : null,
                faction != null ? faction.color : Color.gray,
                faction != null ? faction.factionName : $"Faction {index}");
        }
    }
}
