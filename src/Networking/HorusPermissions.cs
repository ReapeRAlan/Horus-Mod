namespace HorusMod.Networking
{
    /// <summary>
    /// Centralized, host-authoritative permission model for Horus actions.
    /// Replaces scattered server checks so spawn/delete/move rules live in one place.
    ///
    /// Safety rules:
    /// - Single Player: full access.
    /// - Multiplayer Host (server): full access.
    /// - Multiplayer Client: NO spawn/delete/move by default. Never trust client-side execution.
    /// </summary>
    public static class HorusPermissions
    {
        /// <summary>True when a mission is running (single player or multiplayer).</summary>
        public static bool InMission()
        {
            return GameManager.gameState == GameState.SinglePlayer
                || GameManager.gameState == GameState.Multiplayer;
        }

        public static bool IsLocalSinglePlayer()
        {
            return GameManager.gameState == GameState.SinglePlayer;
        }

        public static bool IsMultiplayer()
        {
            return GameManager.gameState == GameState.Multiplayer;
        }

        /// <summary>True when this instance is the authoritative server/host.</summary>
        public static bool IsServer()
        {
            return Spawner.i != null && Spawner.i.IsServer;
        }

        public static bool IsMultiplayerHost()
        {
            return IsMultiplayer() && IsServer();
        }

        public static bool IsMultiplayerClient()
        {
            return IsMultiplayer() && !IsServer();
        }

        /// <summary>
        /// The Horus free camera and UI are safe to open for anyone in a mission.
        /// Spawning/deleting is gated separately by <see cref="CanSpawn"/> / <see cref="CanDelete"/>.
        /// </summary>
        public static bool CanUseHorus()
        {
            return InMission();
        }

        /// <summary>Only the single-player local game or the multiplayer host may spawn units.</summary>
        public static bool CanSpawn()
        {
            return IsLocalSinglePlayer() || IsMultiplayerHost();
        }

        /// <summary>Only the single-player local game or the multiplayer host may delete units.</summary>
        public static bool CanDelete()
        {
            return CanSpawn();
        }

        public static bool CanRequestMutation()
        {
#if HORUS_CLIENT
            if (HorusMod.Client.HorusRemoteAuthority.IsRemoteSession)
                return HorusMod.Client.HorusRemoteAuthority.IsAuthorized;
#endif
            return CanSpawn();
        }

        public static bool CanRequestDelete()
        {
#if HORUS_CLIENT
            if (HorusMod.Client.HorusRemoteAuthority.IsRemoteSession)
                return HorusMod.Client.HorusRemoteAuthority.IsAuthorized;
#endif
            return CanDelete();
        }

        /// <summary>Short label describing the current Horus mode for the UI.</summary>
        public static string GetModeLabel()
        {
#if HORUS_CLIENT
            if (HorusMod.Client.HorusRemoteAuthority.IsRemoteSession)
                return HorusMod.Client.HorusRemoteAuthority.IsAuthorized ? "Dedicated Server GM" : "Dedicated Server - Awaiting Permission";
#endif
            if (IsLocalSinglePlayer()) return "Single Player";
            if (IsMultiplayerHost()) return "Multiplayer Host";
            if (IsMultiplayerClient()) return "Multiplayer Client - No Permission";
            return "Not in mission";
        }

        /// <summary>Short label describing the current permission level for the UI.</summary>
        public static string GetPermissionLabel()
        {
#if HORUS_CLIENT
            if (HorusMod.Client.HorusRemoteAuthority.IsRemoteSession)
                return HorusMod.Client.HorusRemoteAuthority.IsAuthorized
                    ? "Steam allowlist (full remote authority)"
                    : HorusMod.Client.HorusRemoteAuthority.Status;
#endif
            if (IsLocalSinglePlayer()) return "Single Player (full access)";
            if (IsMultiplayerHost()) return "Host (full access)";
            if (IsMultiplayerClient()) return "Client blocked (host permission required)";
            return "N/A";
        }
    }
}
