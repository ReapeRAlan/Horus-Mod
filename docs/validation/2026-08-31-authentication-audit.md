# Dedicated authentication audit

Date: 2026-08-31 UTC

## Scope

The installed official Windows dedicated-server assembly was inspected read-only to verify the identity boundary used by Horus. This is a structural audit of the current official binary, not a substitute for the pending connected-account runtime scenarios.

- Nuclear Option build hash: `cb745d6c44f1`.
- `Assembly-CSharp.dll` SHA-256: `01e2c543cb5de43a01a996d831ee85f8962d076a6f066d75152d20f0c2a3d162`.
- Audited type: `NuclearOption.Networking.Authentication.NetworkAuthenticatorNuclearOption` and its nested `AuthData`.

## Observed boundary

- `AuthData` exposes read-only `UsingSteamTransport`, `SteamID`, and `OwnerID` identity fields plus the live `SteamSessionOk` state.
- The Steam path validates a Steam authentication ticket before creating `AuthData` with `UsingSteamTransport=true` and the authenticated connection SteamID.
- The UDP path creates `AuthData` with `UsingSteamTransport=false` and empty Steam identities.
- The current official authenticator rejects `PlayerType.Spectator` as not implemented.
- A native Steam-session failure changes `SteamSessionOk` before the game's disconnect grace period expires.

## Horus enforcement

Horus reconstructs authority from the authenticated player's `AuthData` for every command and again immediately before queued execution. It requires all of the following:

1. the Mirage player is authenticated;
2. `UsingSteamTransport` is true;
3. `SteamSessionOk` is still true;
4. the authenticated `SteamID` is an individual SteamID64;
5. the exact SteamID64 appears in a completely valid administrator allowlist.

Horus never consults `OwnerID`, the client-reported Steam name, player faction, lobby password, player type, or host claims when assigning GM authority. The per-SteamID rate-limit and request-deduplication state remains in the server process after a network reconnect.

## Result

The static identity boundary is consistent with the documented fail-closed design: UDP and spectator paths cannot receive Horus authority, and a Steam session that becomes invalid loses Horus authority before the native grace-period disconnect. Connected denial/authorization and second-identity scenarios remain `PENDING` until observed with legitimate Steam accounts.
