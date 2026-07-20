using System;

namespace HorusMod.Networking
{
    public class HorusAdminSession
    {
        public string SessionId { get; private set; }
        public DateTime ConnectedAt { get; private set; }
        
        public HorusAdminSession()
        {
            SessionId = Guid.NewGuid().ToString();
            ConnectedAt = DateTime.UtcNow;
        }
    }
}
