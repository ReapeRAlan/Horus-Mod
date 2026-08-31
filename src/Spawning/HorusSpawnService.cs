using System;
using System.Collections.Generic;
#if HORUS_CLIENT
using HorusMod.Client;
using HorusMod.Shared;
#endif
using HorusMod.Loadouts;
using HorusMod.Data;
using HorusMod.Logging;
using HorusMod.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace HorusMod.Spawning
{
    public enum HorusSpawnFailure
    {
        None = 0,
        PermissionDenied,
        MissingSpawner,
        InvalidDefinition,
        InvalidPrefab,
        UnsupportedDefinition,
        SafetyConfirmationRequired,
        NeutralLogisticsUnsupported,
        NativeSpawnFailed
    }

    public sealed class AircraftSpawnOptions
    {
        public Loadout Loadout;
        public float FuelRatio = 1f;
        public LiveryKey Livery = default;
        public float Skill = 0.5f;
        public float Bravery = 0.575f;

        public AircraftSpawnOptions Clone()
        {
            return new AircraftSpawnOptions
            {
                Loadout = HorusLoadoutService.CloneLoadout(Loadout),
                FuelRatio = Mathf.Clamp01(FuelRatio),
                Livery = Livery,
                Skill = Mathf.Clamp01(Skill),
                Bravery = Mathf.Clamp01(Bravery)
            };
        }
    }

    /// <summary>
    /// Complete server-side spawn request. It deliberately contains no IMGUI state.
    /// </summary>
    public sealed class HorusSpawnRequest
    {
        public UnitDefinition Definition;
        public GlobalPosition Position;
        public Quaternion Rotation = Quaternion.identity;
        public FactionHQ HQ;
        public PlacementSurface Surface = PlacementSurface.Free;
        public string UniqueName;
        public bool Stationary;
        public float Skill = 1f;
        public AircraftSpawnOptions Aircraft;

        // Missiles spawn with a launch velocity so the operator can aim them. HorusManager
        // resolves the final pose for world-point, native tracking, or target-relative impact.
        public float MissileLaunchSpeed = 250f;
        public float MissileLaunchElevation;

        // Optional UniqueName of a Unit for the missile to guide toward after launch.
        // Empty means an unguided/ballistic shot using only the launch velocity above.
        public string TargetUnitName;

        // Issued by HorusManager only after the per-session UI acknowledgement so the
        // authoritative service enforces lookup-only/incompatible safety for every caller.
        public string IncompatibleContentAcknowledgementKey;
    }

    public sealed class HorusSpawnResult
    {
        public Unit Unit { get; private set; }
        public UnitDefinition Definition { get; private set; }
        public FactionHQ HQ { get; private set; }
        public GlobalPosition Position { get; private set; }
        public PlacementSurface Surface { get; private set; }
        public AircraftSpawnOptions Aircraft { get; private set; }
        public HorusSpawnFailure Failure { get; private set; }
        public string Message { get; private set; }
        public Exception Exception { get; private set; }
        public bool IsRemotePending { get; private set; }
        public bool Success => Failure == HorusSpawnFailure.None && (Unit != null || IsRemotePending);

        public static HorusSpawnResult Ok(Unit unit, HorusSpawnRequest request) => new HorusSpawnResult
        {
            Unit = unit,
            Definition = request?.Definition,
            HQ = request?.HQ,
            Position = request != null ? request.Position : default,
            Surface = request != null ? request.Surface : PlacementSurface.Free,
            Aircraft = request?.Aircraft?.Clone(),
            Failure = HorusSpawnFailure.None,
            Message = "Spawned"
        };

        public static HorusSpawnResult Fail(HorusSpawnFailure failure, string message,
            HorusSpawnRequest request = null, Exception exception = null) => new HorusSpawnResult
        {
            Definition = request?.Definition,
            HQ = request?.HQ,
            Position = request != null ? request.Position : default,
            Surface = request != null ? request.Surface : PlacementSurface.Free,
            Aircraft = request?.Aircraft?.Clone(),
            Failure = failure,
            Message = message,
            Exception = exception
        };

        public static HorusSpawnResult RemotePending(HorusSpawnRequest request) => new HorusSpawnResult
        {
            Definition = request?.Definition,
            HQ = request?.HQ,
            Position = request != null ? request.Position : default,
            Surface = request != null ? request.Surface : PlacementSurface.Free,
            Aircraft = request?.Aircraft?.Clone(),
            Failure = HorusSpawnFailure.None,
            Message = "Dedicated spawn request sent",
            IsRemotePending = true
        };
    }

    /// <summary>
    /// Single authoritative gateway for native unit spawning. Aircraft customization is
    /// supplied before ServerObjectManager.Spawn, avoiding a default/null first snapshot.
    /// </summary>
    public static class HorusSpawnService
    {
        private sealed class AuthRecord
        {
            public string Token;
            public float ExpiresAt; // 0 means it never expires on its own.
        }

        // Session-scoped safety authorizations. The service owns them so every caller
        // (UI, groups, factories, future dedicated routes) is gated by the same state.
        private static readonly Dictionary<string, AuthRecord> IncompatibleAuthorizations =
            new Dictionary<string, AuthRecord>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Grants a per-session acknowledgement for lookup-only/incompatible content.</summary>
        public static string IssueIncompatibleContentAuthorization(string catalogKey)
        {
            if (string.IsNullOrEmpty(catalogKey)) return null;
            string token = Guid.NewGuid().ToString("N");
            IncompatibleAuthorizations[catalogKey] = new AuthRecord { Token = token, ExpiresAt = 0f };
            return token;
        }

        /// <summary>Revokes any incompatible-content authorization matching the token.</summary>
        public static void RevokeAuthorization(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            RemoveByToken(IncompatibleAuthorizations, token);
        }

        /// <summary>Clears every session authorization; called on runtime reset.</summary>
        public static void ResetAuthorizations()
        {
            IncompatibleAuthorizations.Clear();
        }

        private static void RemoveByToken(Dictionary<string, AuthRecord> store, string token)
        {
            string match = null;
            foreach (KeyValuePair<string, AuthRecord> pair in store)
            {
                if (string.Equals(pair.Value.Token, token, StringComparison.Ordinal))
                {
                    match = pair.Key;
                    break;
                }
            }
            if (match != null) store.Remove(match);
        }

        private static bool HasIncompatibleAuthorization(string catalogKey)
        {
            return !string.IsNullOrEmpty(catalogKey) && IncompatibleAuthorizations.ContainsKey(catalogKey);
        }

        public static HorusSpawnResult Spawn(HorusSpawnRequest request)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession)
            {
                if (request == null || request.Definition == null)
                    return HorusSpawnResult.Fail(HorusSpawnFailure.InvalidDefinition, "No unit definition was supplied.", request);
                UnitEntry remoteEntry=UnitCatalog.FindByDefinition(request.Definition);
                var payload = new HorusCommandPayload
                {
                    DefinitionKey = request.Definition.jsonKey ?? "",
                    SecondaryKey = remoteEntry?.Source ?? "",
                    UniqueName = request.UniqueName ?? "",
                    FactionIndex = ResolveFactionIndex(request.HQ),
                    Yaw = request.Rotation.eulerAngles.y,
                    BoolValue = request.Stationary,
                    FloatValue = request.Aircraft != null ? request.Aircraft.Skill : request.Skill,
                    FloatValue2 = request.Aircraft != null ? request.Aircraft.FuelRatio : request.MissileLaunchSpeed,
                    FloatValue3 = request.Aircraft != null ? request.Aircraft.Bravery : request.MissileLaunchElevation,
                    IntValue = request.Aircraft != null ? request.Aircraft.Livery.Index : 0
                };
                payload.Points.Add(HorusRemoteAuthority.Point(request.Position));
                if (request.Aircraft?.Loadout?.weapons != null)
                    foreach (WeaponMount mount in request.Aircraft.Loadout.weapons)
                        payload.MountKeys.Add(mount != null ? mount.jsonKey ?? "" : "");
                if (!string.IsNullOrEmpty(request.TargetUnitName) && UnitRegistry.customIDLookup.TryGetValue(request.TargetUnitName, out Unit target) && target != null)
                    payload.TargetUnitId = target.persistentID.Id;
                return HorusRemoteAuthority.TrySubmit(HorusCommandKind.Spawn, payload)
                    ? HorusSpawnResult.RemotePending(request)
                    : HorusSpawnResult.Fail(HorusSpawnFailure.PermissionDenied, HorusRemoteAuthority.Status, request);
            }
#endif
            if (!HorusPermissions.CanSpawn())
                return HorusSpawnResult.Fail(HorusSpawnFailure.PermissionDenied, "Host authority is required.", request);
            if (request == null || request.Definition == null)
                return HorusSpawnResult.Fail(HorusSpawnFailure.InvalidDefinition, "No unit definition was supplied.", request);
            if (Spawner.i == null)
                return HorusSpawnResult.Fail(HorusSpawnFailure.MissingSpawner, "The native Spawner is not ready.", request);
            if (request.Definition.unitPrefab == null)
                return HorusSpawnResult.Fail(HorusSpawnFailure.InvalidPrefab, "The definition has no unit prefab.", request);

            UnitEntry catalogEntry = UnitCatalog.FindByDefinition(request.Definition);
            if (catalogEntry != null && request.Surface == PlacementSurface.Free &&
                catalogEntry.PlacementSurface != PlacementSurface.Free)
                request.Surface = catalogEntry.PlacementSurface;
            string catalogKey = catalogEntry?.Key ?? request.Definition.jsonKey ?? "";
            if (catalogEntry?.IsLookupOnly == true)
            {
                bool forceEnabled = HorusPlugin.AllowIncompatibleContent != null && HorusPlugin.AllowIncompatibleContent.Value;
                bool acknowledged = string.Equals(request.IncompatibleContentAcknowledgementKey, catalogKey,
                    StringComparison.OrdinalIgnoreCase) && HasIncompatibleAuthorization(catalogKey);
                if (!forceEnabled || !acknowledged)
                {
                    return HorusSpawnResult.Fail(
                        HorusSpawnFailure.SafetyConfirmationRequired,
                        "Lookup-only content requires Force incompatible content and a per-session acknowledgement.",
                        request);
                }
            }
            if (request.HQ == null && catalogEntry?.Supply != null &&
                (catalogEntry.Supply.HasRearmer || catalogEntry.Supply.HasRefueler))
            {
                return HorusSpawnResult.Fail(
                    HorusSpawnFailure.NeutralLogisticsUnsupported,
                    "Functional rearm/refuel objects require a playable faction/HQ; Neutral is not supported.",
                    request);
            }

            string uniqueName = string.IsNullOrWhiteSpace(request.UniqueName)
                ? (request.Definition.jsonKey ?? "horus") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                : request.UniqueName;

            try
            {
                Unit spawned;
                AircraftDefinition aircraftDefinition = request.Definition as AircraftDefinition;
                Aircraft prefabAircraft = request.Definition.unitPrefab.GetComponent<Aircraft>();
                if (aircraftDefinition != null || prefabAircraft != null)
                {
                    if (prefabAircraft == null)
                    {
                        return HorusSpawnResult.Fail(
                            HorusSpawnFailure.InvalidPrefab,
                            $"Aircraft definition '{request.Definition.jsonKey}' has no Aircraft component on its prefab.",
                            request);
                    }
                    if (!TryPrepareAircraftOptions(request, prefabAircraft, out AircraftSpawnOptions options,
                        out string loadoutError))
                    {
                        return HorusSpawnResult.Fail(
                            HorusSpawnFailure.InvalidDefinition,
                            "Aircraft spawn blocked: " + loadoutError,
                            request);
                    }

                    // Keep the validated snapshot on the request/result, while giving
                    // the native SyncVar its own fresh instance.
                    request.Aircraft = options;
                    Loadout networkLoadout = HorusLoadoutService.CloneLoadout(options.Loadout);
                    spawned = Spawner.i.SpawnAircraft(
                        null,
                        request.Definition.unitPrefab,
                        networkLoadout,
                        Mathf.Clamp01(options.FuelRatio),
                        options.Livery,
                        request.Position,
                        request.Rotation,
                        Vector3.zero,
                        null,
                        request.HQ,
                        uniqueName,
                        Mathf.Clamp01(options.Skill),
                        Mathf.Clamp01(options.Bravery));
                }
                else if (request.Definition is ShipDefinition || request.Definition.unitPrefab.GetComponent<Ship>() != null)
                {
                    spawned = Spawner.i.SpawnShip(
                        request.Definition.unitPrefab,
                        request.Position,
                        request.Rotation,
                        request.HQ,
                        uniqueName,
                        Mathf.Clamp01(request.Skill),
                        request.Stationary);
                }
                else if (request.Definition is VehicleDefinition || request.Definition.unitPrefab.GetComponent<GroundVehicle>() != null)
                {
                    spawned = Spawner.i.SpawnVehicle(
                        request.Definition.unitPrefab,
                        request.Position,
                        request.Rotation,
                        Vector3.zero,
                        request.HQ,
                        uniqueName,
                        Mathf.Clamp01(request.Skill),
                        request.Stationary,
                        null);
                }
                else if (request.Definition is BuildingDefinition || request.Definition.unitPrefab.GetComponent<Building>() != null)
                {
                    spawned = Spawner.i.SpawnBuilding(
                        request.Definition.unitPrefab,
                        request.Position,
                        request.Rotation,
                        request.HQ,
                        null,
                        uniqueName,
                        false,
                        null);
                }
                else if (request.Definition is SceneryDefinition || request.Definition.unitPrefab.GetComponent<Scenery>() != null)
                {
                    spawned = Spawner.i.SpawnScenery(
                        request.Definition.unitPrefab,
                        request.Position,
                        request.Rotation,
                        uniqueName);
                }
                else if (request.Definition is MissileDefinition missileDefinition)
                {
                    // Aim the missile: the placement facing sets heading, elevation lofts
                    // it, and speed provides the launch impulse so it actually flies there.
                    // If a target unit was designated, the native seeker/autopilot takes
                    // over and homes in on it instead of flying a pure ballistic arc.
                    float elevation = Mathf.Clamp(request.MissileLaunchElevation, -89f, 89f);
                    Quaternion launchRotation = request.Rotation * Quaternion.Euler(-elevation, 0f, 0f);
                    float speed = Mathf.Max(0f, request.MissileLaunchSpeed);
                    Vector3 velocity = launchRotation * Vector3.forward * speed;
                    HorusLog.Info("Spawn", $"Launching '{missileDefinition.jsonKey}': speed={speed:F0}, elevation={elevation:F0}, hq={(request.HQ != null ? request.HQ.name : "null")}, target={(string.IsNullOrEmpty(request.TargetUnitName) ? "none" : request.TargetUnitName)}.");
                    spawned = Spawner.i.SpawnSavedMissile(
                        missileDefinition.unitPrefab,
                        request.Position,
                        launchRotation,
                        request.HQ,
                        request.TargetUnitName ?? "",
                        "",
                        velocity,
                        uniqueName);
                    HorusLog.Info("Spawn", spawned != null
                        ? $"'{missileDefinition.jsonKey}' spawned OK as '{uniqueName}'."
                        : $"'{missileDefinition.jsonKey}' SpawnSavedMissile returned null.");
                }
                else if (request.Definition.code == "PILOT" || request.Definition.unitPrefab.GetComponent<PilotDismounted>() != null)
                {
                    spawned = Spawner.i.SpawnPilot(
                        request.Definition.unitPrefab,
                        request.Position,
                        request.Rotation,
                        request.HQ,
                        uniqueName);
                }
                else if (request.Definition.unitPrefab.GetComponent<Container>() != null)
                {
                    spawned = Spawner.i.SpawnContainer(
                        request.Definition.unitPrefab,
                        request.Position,
                        request.Rotation,
                        request.HQ,
                        uniqueName);
                }
                else
                {
                    // The native editor route owns Building, Scenery, Missile, Pilot and
                    // Container creation and registers each concrete network object correctly.
                    spawned = Spawner.i.SpawnFromUnitDefinitionInEditor(
                        request.Definition,
                        request.Position,
                        request.Rotation,
                        request.HQ,
                        uniqueName);
                }

                if (spawned == null)
                    return HorusSpawnResult.Fail(HorusSpawnFailure.NativeSpawnFailed, "The native spawner returned null.", request);

                // Several native Spawner.Spawn* methods (confirmed: SpawnSavedMissile,
                // SpawnPilot, SpawnContainer, SpawnShip) move transform.position directly but
                // never sync that into the Rigidbody (no rb.position/MovePosition call, unlike
                // e.g. SpawnVehicle, which does). The next physics step then resets the
                // transform back to wherever the Rigidbody's internal position was at
                // Instantiate time -- the prefab's authored local position, i.e. world origin
                // -- so the unit silently teleports away a fraction of a second after
                // spawning, regardless of where it was actually placed. Force both back in
                // sync here so every spawn path is covered, including any the game changes
                // later. A no-op for definitions with no Rigidbody (buildings/scenery) or
                // whose native spawn already syncs it correctly (aircraft/vehicles).
                Vector3 correctedLocalPosition = request.Position.ToLocalPosition();
                Rigidbody spawnedRb = spawned.GetComponent<Rigidbody>();
                if (spawnedRb != null) spawnedRb.position = correctedLocalPosition;
                spawned.transform.position = correctedLocalPosition;

                return HorusSpawnResult.Ok(spawned, request);
            }
            catch (Exception ex)
            {
                HorusLog.Error("Spawn", $"Native spawn failed for '{request.Definition.jsonKey ?? request.Definition.unitName}': {ex.Message}");
                return HorusSpawnResult.Fail(HorusSpawnFailure.NativeSpawnFailed, ex.Message, request, ex);
            }
        }

#if HORUS_CLIENT
        private static int ResolveFactionIndex(FactionHQ hq)
        {
            if (FactionRegistry.factions == null) return -1;
            if (hq?.faction == null) return FactionRegistry.factions.Count;
            return FactionRegistry.factions.IndexOf(hq.faction);
        }
#endif

        private static bool TryPrepareAircraftOptions(
            HorusSpawnRequest request,
            Aircraft prefabAircraft,
            out AircraftSpawnOptions prepared,
            out string error)
        {
            bool callerSuppliedOptions = request.Aircraft != null;
            prepared = request.Aircraft?.Clone() ?? new AircraftSpawnOptions();
            error = null;

            WeaponManager weaponManager = prefabAircraft.weaponManager ??
                request.Definition.unitPrefab.GetComponent<WeaponManager>();
            HardpointSet[] hardpointSets = weaponManager?.hardpointSets;
            if (hardpointSets == null)
            {
                error = $"'{request.Definition.jsonKey}' exposes no safe hardpoint topology.";
                return false;
            }

            int hardpointCount = hardpointSets.Length;
            if (hardpointCount == 0)
            {
                // Aircraft.Start replaces a zero-length loadout after the initial
                // network spawn. Reject it instead of publishing a transient value.
                error = $"'{request.Definition.jsonKey}' exposes zero hardpoint sets; a stable pre-network loadout cannot be built.";
                return false;
            }

            AircraftDefinition effectiveDefinition = request.Definition as AircraftDefinition ??
                prefabAircraft.definition as AircraftDefinition;
            if (effectiveDefinition != null)
            {
                LoadoutApplyResult resolved;
                if (prepared.Loadout != null)
                {
                    if (prepared.Loadout.weapons == null || prepared.Loadout.weapons.Count != hardpointCount)
                    {
                        error = $"Supplied loadout has {prepared.Loadout.weapons?.Count ?? 0} hardpoints; aircraft requires {hardpointCount}.";
                        return false;
                    }

                    var mountKeys = new string[hardpointCount];
                    for (int i = 0; i < hardpointCount; i++)
                    {
                        WeaponMount mount = prepared.Loadout.weapons[i];
                        mountKeys[i] = mount != null ? mount.jsonKey ?? "" : "";
                    }
                    var draft = new LoadoutDraft(
                        effectiveDefinition.jsonKey,
                        LoadoutSourceKind.CopyCurrentAircraft,
                        mountKeys,
                        Mathf.Clamp01(prepared.FuelRatio),
                        -1,
                        "spawn-request",
                        "Spawn request");
                    resolved = HorusLoadoutService.ResolveForSpawn(effectiveDefinition, request.HQ, draft);
                    if (!resolved.Success)
                    {
                        error = "Supplied loadout is invalid: " + resolved.Message;
                        return false;
                    }
                }
                else
                {
                    resolved = HorusLoadoutService.ResolveDefaultForSpawn(effectiveDefinition, request.HQ);
                    if (!resolved.Success)
                    {
                        // Aircraft without a usable preset can still be spawned
                        // safely and intentionally unarmed when its hardpoints are
                        // known. Resolve the empty draft through the same validator.
                        var emptyKeys = new string[hardpointCount];
                        var emptyDraft = new LoadoutDraft(
                            effectiveDefinition.jsonKey,
                            LoadoutSourceKind.CustomHardpoints,
                            emptyKeys,
                            Mathf.Clamp01(prepared.FuelRatio),
                            -1,
                            "empty-fallback",
                            "Empty fallback");
                        resolved = HorusLoadoutService.ResolveForSpawn(effectiveDefinition, request.HQ, emptyDraft);
                    }
                    if (!resolved.Success)
                    {
                        error = "No safe pre-network loadout could be resolved: " + resolved.Message;
                        return false;
                    }
                    if (!callerSuppliedOptions) prepared.FuelRatio = resolved.FuelRatio;
                }

                prepared.Loadout = HorusLoadoutService.CloneLoadout(resolved.ResolvedLoadout);
            }
            else
            {
                if (prepared.Loadout != null)
                {
                    error = $"Prefab aircraft '{request.Definition.jsonKey}' has no AircraftDefinition, so its supplied mounts cannot be validated safely.";
                    return false;
                }

                // Lookup/mod content sometimes exposes an Aircraft prefab through a
                // base UnitDefinition. With no AircraftDefinition there are no safe
                // mount rules to consult, so publish an explicitly unarmed loadout
                // whose shape still matches the prefab's hardpoint topology.
                var empty = new Loadout();
                for (int i = 0; i < hardpointCount; i++) empty.weapons.Add(null);
                prepared.Loadout = empty;
            }

            if (prepared.Loadout == null || prepared.Loadout.weapons == null ||
                prepared.Loadout.weapons.Count != hardpointCount)
            {
                error = $"Resolved loadout is not dimensioned for all {hardpointCount} hardpoints.";
                return false;
            }

            prepared.Loadout = HorusLoadoutService.CloneLoadout(prepared.Loadout);
            prepared.FuelRatio = Mathf.Clamp01(prepared.FuelRatio);
            prepared.Skill = Mathf.Clamp01(prepared.Skill);
            prepared.Bravery = Mathf.Clamp01(prepared.Bravery);
            return true;
        }
    }
}
