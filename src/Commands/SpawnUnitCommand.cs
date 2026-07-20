using System;

namespace HorusMod.Commands
{
    [Serializable]
    public class SpawnUnitCommand
    {
        public string unitKey;
        public int factionIndex;
        public float posX;
        public float posY;
        public float posZ;
        public float yaw;
        public float altitude;
        public bool stationary;
    }
}
