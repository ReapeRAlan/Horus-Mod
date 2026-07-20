using System;

namespace HorusMod.Commands
{
    [Serializable]
    public class HorusCommandRequest
    {
        public HorusCommandType commandType;
        public string payloadJson;
        public string clientId;
    }
}
