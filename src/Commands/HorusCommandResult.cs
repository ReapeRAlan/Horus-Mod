using System;

namespace HorusMod.Commands
{
    [Serializable]
    public class HorusCommandResult
    {
        public bool success;
        public string message;
        public string commandId;
    }
}
