using System;

namespace HorusMod.Networking
{
    public class HorusNetworkBridge
    {
        // MVP stub for future dedicated server headless networking 
        public bool isEnabled = false;
        public string bindAddress = "127.0.0.1";
        public int port = 7780;
        public string authenticationToken = "";
        public int rateLimitPerSecond = 5;

        public void Initialize()
        {
            if (!isEnabled)
            {
                HorusPlugin.Logger.LogInfo("HorusNetworkBridge is disabled in config. Running local logic only.");
                return;
            }
            // TCP server initialization logic goes here
        }
    }
}
