using System;

namespace HorusMod.Shared
{
    public static class HorusPaging
    {
        public static int ComputePageCount(int unitCount, int factoryCount, int pageSize)
        {
            if (unitCount < 0) throw new ArgumentOutOfRangeException(nameof(unitCount));
            if (factoryCount < 0) throw new ArgumentOutOfRangeException(nameof(factoryCount));
            if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
            int unitPages = (unitCount + pageSize - 1) / pageSize;
            int factoryPages = (factoryCount + pageSize - 1) / pageSize;
            return Math.Max(1, Math.Max(unitPages, factoryPages));
        }
    }
}
