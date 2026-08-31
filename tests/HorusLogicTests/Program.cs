using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using HorusMod.Interaction;
using HorusMod.Server;
using HorusMod.Spawning;
using HorusMod.Shared;

internal static class Program
{
    private static int passed;

    private static void Main()
    {
        Check("stationary RMB is a click", !RmbGestureClassifier.IsDrag(0f, 0f));
        Check("exact threshold is a click", !RmbGestureClassifier.IsDrag(10f, 0f));
        Check("movement beyond threshold is look", RmbGestureClassifier.IsDrag(10.01f, 0f));
        Check("diagonal movement uses radial threshold", RmbGestureClassifier.IsDrag(8f, 8f));

        Check("inside menu retains ownership",
            ContextMenuPointerPolicy.Classify(true, true, true, false) == ContextMenuOutsideClickAction.None);
        Check("outside LMB dismisses and consumes",
            ContextMenuPointerPolicy.Classify(true, false, true, false) == ContextMenuOutsideClickAction.DismissAndConsume);
        Check("outside RMB dismisses and continues",
            ContextMenuPointerPolicy.Classify(true, false, false, true) == ContextMenuOutsideClickAction.DismissAndContinue);

        string contextMenuSource = ReadRepoFile("src", "UI", "ContextMenu", "HorusContextMenu.cs");
        int openStart = contextMenuSource.IndexOf("public static void Open", StringComparison.Ordinal);
        int drawStart = contextMenuSource.IndexOf("public static void Draw", StringComparison.Ordinal);
        string openImplementation = contextMenuSource.Substring(openStart, drawStart - openStart);
        Check("context menu open defers IMGUI work",
            openImplementation.Contains("CreatePendingLevel", StringComparison.Ordinal) &&
            !openImplementation.Contains("HorusTheme.EnsureBuilt();", StringComparison.Ordinal));

        string menuBuilderSource = ReadRepoFile("src", "UI", "ContextMenu", "HorusContextMenuBuilder.cs");
        string[] localizedMenuLeaks = { "Solo host", "Mover", "Mantener", "Limpiar", "Reglas de combate", "Guardar", "Atacar", "Enfocar", "Duplicar", "Borrar", "Cancelar herramienta", "unidades seleccionadas" };
        bool menuIsGlobalEnglish = true;
        for (int i = 0; i < localizedMenuLeaks.Length; i++)
            if (menuBuilderSource.Contains(localizedMenuLeaks[i], StringComparison.OrdinalIgnoreCase)) menuIsGlobalEnglish = false;
        Check("context menu uses global English labels", menuIsGlobalEnglish);

        Check("patrol advances", TacticalRouteCursor.Next(0, 3, true) == 1);
        Check("patrol loops", TacticalRouteCursor.Next(2, 3, true) == 0);
        Check("non-looping route stops", TacticalRouteCursor.Next(2, 3, false) == 2);
        Check("empty route is invalid", TacticalRouteCursor.Next(0, 0, true) == -1);

        Check("navigation enters combat on threat",
            TacticalEngagementPolicy.Decide(false, true, 0f, 4f) == TacticalEngagementAction.EnterCombat);
        Check("combat remains active while threat is visible",
            TacticalEngagementPolicy.Decide(true, true, 0f, 4f) == TacticalEngagementAction.StayInCombat);
        Check("combat observes resume grace",
            TacticalEngagementPolicy.Decide(true, false, 2f, 4f) == TacticalEngagementAction.StayInCombat);
        Check("route resumes after grace",
            TacticalEngagementPolicy.Decide(true, false, 4f, 4f) == TacticalEngagementAction.ResumeNavigation);

        Check("group attack and guard require unit targets",
            HorusGroupOrderTargetPolicy.RequiresUnit(HorusGroupOrderTargetMode.AttackTarget) &&
            HorusGroupOrderTargetPolicy.RequiresUnit(HorusGroupOrderTargetMode.Guard));
        Check("group movement orders accept world targets",
            !HorusGroupOrderTargetPolicy.RequiresUnit(HorusGroupOrderTargetMode.Move) &&
            !HorusGroupOrderTargetPolicy.RequiresUnit(HorusGroupOrderTargetMode.AttackMove) &&
            !HorusGroupOrderTargetPolicy.RequiresUnit(HorusGroupOrderTargetMode.Patrol));
        Check("every group target mode has an operator prompt",
            !string.IsNullOrWhiteSpace(HorusGroupOrderTargetPolicy.Prompt(HorusGroupOrderTargetMode.Move)) &&
            !string.IsNullOrWhiteSpace(HorusGroupOrderTargetPolicy.Prompt(HorusGroupOrderTargetMode.AttackMove)) &&
            !string.IsNullOrWhiteSpace(HorusGroupOrderTargetPolicy.Prompt(HorusGroupOrderTargetMode.Patrol)) &&
            !string.IsNullOrWhiteSpace(HorusGroupOrderTargetPolicy.Prompt(HorusGroupOrderTargetMode.AttackTarget)) &&
            !string.IsNullOrWhiteSpace(HorusGroupOrderTargetPolicy.Prompt(HorusGroupOrderTargetMode.Guard)));

        Check("world-point ordnance does not require a unit",
            !HorusOrdnanceTargetPolicy.RequiresSelectedUnit(HorusOrdnanceTargetMode.WorldPoint));
        Check("both targeted ordnance modes require one unit",
            HorusOrdnanceTargetPolicy.RequiresSelectedUnit(HorusOrdnanceTargetMode.TrackSelected) &&
            HorusOrdnanceTargetPolicy.RequiresSelectedUnit(HorusOrdnanceTargetMode.ImpactSelected));
        Check("only tracking requires a native seeker",
            HorusOrdnanceTargetPolicy.RequiresNativeSeeker(HorusOrdnanceTargetMode.TrackSelected) &&
            !HorusOrdnanceTargetPolicy.RequiresNativeSeeker(HorusOrdnanceTargetMode.ImpactSelected));
        Check("only impact mode relocates the spawn to the target",
            HorusOrdnanceTargetPolicy.UsesTargetRelativeSpawn(HorusOrdnanceTargetMode.ImpactSelected) &&
            !HorusOrdnanceTargetPolicy.UsesTargetRelativeSpawn(HorusOrdnanceTargetMode.TrackSelected));
        double fallTime = HorusOrdnanceTargetPolicy.EstimateFallTime(300d, 250d);
        Check("impact lead fall time is positive and bounded", fallTime > 1d && fallTime < 1.2d);

        string inputRouterSource = ReadRepoFile("src", "Interaction", "HorusInputRouter.cs");
        Check("live ordnance preserves the designated selection after firing",
            inputRouterSource.Contains("spawned != null && !owner.LastPlacementWasLiveOrdnance", StringComparison.Ordinal));

        TestDedicatedProtocol();

        Console.WriteLine($"Horus logic tests passed: {passed}");
    }

    private static void TestDedicatedProtocol()
    {
        var command = new HorusCommandEnvelope
        {
            SessionId = Guid.NewGuid(),
            ExpectedRevision = 12,
            RequestId = Guid.NewGuid(),
            Command = HorusCommandKind.Patrol
        };
        command.Payload.UnitIds.Add(17);
        command.Payload.UnitIds.Add(18);
        command.Payload.Points.Add(new HorusVector3(10f, 20f, 30f));
        command.Payload.Points.Add(new HorusVector3(40f, 50f, 60f));
        command.Payload.DefinitionKey = "TEST_DEF";
        command.Payload.MountKeys.Add("TEST_MOUNT");

        byte[] encoded = HorusWireCodec.Encode(HorusPacketKind.Command, command);
        var decoded = (HorusCommandEnvelope)HorusWireCodec.Decode(encoded, out HorusPacketKind kind);
        Check("dedicated command codec preserves packet kind", kind == HorusPacketKind.Command);
        Check("dedicated command codec round-trips identity",
            decoded.RequestId == command.RequestId && decoded.SessionId == command.SessionId && decoded.Command == command.Command);
        Check("dedicated command codec round-trips bounded collections",
            decoded.Payload.UnitIds.Count == 2 && decoded.Payload.Points.Count == 2 && decoded.Payload.MountKeys.Count == 1);
        Check("dedicated command validator accepts a valid command", HorusCommandValidator.TryValidate(decoded, out _));

        var incompatible = new HorusCommandEnvelope { ProtocolVersion = (ushort)(HorusProtocol.Version + 1), RequestId = Guid.NewGuid(), Command = HorusCommandKind.Move };
        Check("dedicated command validator rejects incompatible protocol versions", !HorusCommandValidator.TryValidate(incompatible, out _));
        var unknown = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = (HorusCommandKind)ushort.MaxValue };
        Check("dedicated command validator rejects unknown commands", !HorusCommandValidator.TryValidate(unknown, out _));

        decoded.Payload.Points[0] = new HorusVector3(float.NaN, 0f, 0f);
        Check("dedicated command validator rejects non-finite coordinates", !HorusCommandValidator.TryValidate(decoded, out _));
        decoded.Payload.Points[0] = new HorusVector3(200000000f, 0f, 0f);
        Check("dedicated command validator rejects out-of-world coordinates", !HorusCommandValidator.TryValidate(decoded, out _));

        var allowlist = HorusAdminAllowlist.Parse(new[] { "# admins", "76561198000000001 # gm", "bad", "76561198000000001" }, out var errors);
        Check("Steam allowlist fails closed when any line is invalid",
            allowlist.Count == 0 && errors.Count == 1);
        var validAllowlist = HorusAdminAllowlist.Parse(new[] { "76561198000000001", "76561198000000001" }, out var validErrors);
        Check("Steam allowlist is exact and deduplicated when the complete file is valid",
            validAllowlist.Count == 1 && validAllowlist.Contains(76561198000000001UL) && !validAllowlist.Contains(76561198000000002UL) && validErrors.Count == 0);

        var dedup = new HorusRequestDeduplicator(4, 10d);
        Guid request = Guid.NewGuid();
        Check("request deduplicator accepts first request", dedup.TryRemember(request, 0d));
        Check("request deduplicator rejects replay", !dedup.TryRemember(request, 1d));
        Check("request deduplicator expires old request", dedup.TryRemember(request, 11d));

        var bucket = new HorusTokenBucket(2d, 2d, 0d);
        Check("rate limiter permits configured burst", bucket.TryConsume(0d) && bucket.TryConsume(0d));
        Check("rate limiter blocks excess burst", !bucket.TryConsume(0d));
        Check("rate limiter refills over time", bucket.TryConsume(0.5d));

        var state = new HorusStatePage
        {
            SessionId = Guid.NewGuid(), SnapshotId = Guid.NewGuid(), Revision = 44, PageIndex = 0, PageCount = 1
        };
        state.Units.Add(new HorusUnitState { UnitId = 3, DefinitionKey = "AIRCRAFT", Name = "Horus_3", FactionIndex = 1, Position = new HorusVector3(1, 2, 3), HorusOwned = true });
        var factory = new HorusFactoryState { FactoryId = "factory-1", PresetName = "Air Factory", FactionIndex = 1, Enabled = true, ProductionEnabled = true, ConsumesBudget = true, Position = new HorusVector3(4, 5, 6), Yaw = 90f, GeneratesIncome = true, IncomePerMinute = 15f, CurrentProductionIndex = 1, ProductionIntervalSeconds = 60f, ProductionTimer = 12f, MaxActiveProducedUnits = 4, UsesRallyPoint = true, RallyPoint = new HorusVector3(7, 8, 9), SpawnRadius = 50f, LastStatus = "Building" };
        factory.ProductionKeys.Add("AIRCRAFT_A");factory.ProductionKeys.Add("AIRCRAFT_B");state.Factories.Add(factory);
        state.Budgets.Add(new HorusBudgetState { FactionIndex = 1, Budget = 9000f, IncomePerTick = 5f, UnitCap = 30, ActiveUnitCount = 2 });
        byte[] stateBytes = HorusWireCodec.Encode(HorusPacketKind.StatePage, state);
        var decodedState = (HorusStatePage)HorusWireCodec.Decode(stateBytes, out HorusPacketKind stateKind);
        Check("state snapshot codec round-trips", stateKind == HorusPacketKind.StatePage && decodedState.Units.Count == 1 && decodedState.Units[0].HorusOwned);
        Check("factory snapshot round-trips full production state", decodedState.Factories.Count == 1 && decodedState.Factories[0].ProductionKeys.Count == 2 && decodedState.Factories[0].UsesRallyPoint && decodedState.Factories[0].Yaw == 90f);
        Check("economy snapshot round-trips caps and income", decodedState.Budgets.Count == 1 && decodedState.Budgets[0].UnitCap == 30 && decodedState.Budgets[0].IncomePerTick == 5f);

        var hello = RoundTrip<HorusHello>(HorusPacketKind.Hello, new HorusHello { ClientVersion = "2.0.0-rc.1" });
        Check("hello codec round-trips", hello.ProtocolVersion == HorusProtocol.Version && hello.ClientVersion == "2.0.0-rc.1");
        var capabilities = RoundTrip<HorusCapabilities>(HorusPacketKind.Capabilities, new HorusCapabilities { ServerVersion = "2.0.0-rc.1", SessionId = state.SessionId, Revision = 7, Features = HorusCapability.FullParity, Authorized = true, Result = HorusResultCode.Accepted, Message = "ok" });
        Check("capabilities codec round-trips", capabilities.Authorized && capabilities.SessionId == state.SessionId && capabilities.Features == HorusCapability.FullParity);
        Check("capabilities response policy accepts the current bounded contract", HorusResponsePolicy.IsValidCapabilities(capabilities));
        var result = new HorusCommandResult { RequestId = command.RequestId, Command = command.Command, Result = HorusResultCode.Accepted, SessionId = command.SessionId, Revision = 13, Message = "accepted" };result.AffectedUnitIds.Add(17);
        var decodedResult = RoundTrip<HorusCommandResult>(HorusPacketKind.CommandResult, result);
        Check("command result codec round-trips", decodedResult.Result == HorusResultCode.Accepted && decodedResult.AffectedUnitIds.Count == 1);
        Check("command result response policy accepts unique nonzero affected ids", HorusResponsePolicy.IsValidResult(decodedResult));
        var requestState = RoundTrip<HorusStateRequest>(HorusPacketKind.StateRequest, new HorusStateRequest { SessionId = state.SessionId, KnownRevision = 44 });
        Check("state request codec round-trips", requestState.SessionId == state.SessionId && requestState.KnownRevision == 44);
        var stateEvent = RoundTrip<HorusStateEvent>(HorusPacketKind.StateEvent, new HorusStateEvent { SessionId = state.SessionId, Revision = 45, Result = result });
        Check("state event codec round-trips", stateEvent.Revision == 45 && stateEvent.Result.RequestId == command.RequestId);
        Check("state event policy rejects mismatched nested session and revision", !HorusResponsePolicy.IsValidEvent(stateEvent));
        var validEventResult = new HorusCommandResult { RequestId = Guid.NewGuid(), Command = HorusCommandKind.Move, Result = HorusResultCode.Accepted, SessionId = state.SessionId, Revision = 45, Message = "accepted" };
        Check("state event policy accepts coherent nested results", HorusResponsePolicy.IsValidEvent(new HorusStateEvent { SessionId = state.SessionId, Revision = 45, Result = validEventResult }));

        var oversizedString = new HorusHello { ClientVersion = new string('x', HorusProtocol.MaxStringBytes + 1) };
        Check("codec rejects oversized strings", Throws<InvalidDataException>(() => HorusWireCodec.Encode(HorusPacketKind.Hello, oversizedString)));
        var oversizedPacket = new HorusCommandEnvelope { SessionId = Guid.NewGuid(), RequestId = Guid.NewGuid(), Command = HorusCommandKind.SetLoadout };
        for (int i = 0; i < HorusProtocol.MaxMounts; i++) oversizedPacket.Payload.MountKeys.Add(new string('x', HorusProtocol.MaxStringBytes));
        Check("codec enforces the aggregate string-list limit", Throws<InvalidDataException>(() => HorusWireCodec.Encode(HorusPacketKind.Command, oversizedPacket)));
        var maximumPacket = new HorusStatePage { SessionId = Guid.NewGuid(), SnapshotId = Guid.NewGuid(), PageIndex = 0, PageCount = 1 };
        for (uint i = 0; i < HorusProtocol.MaxSnapshotUnitsPerPage; i++) maximumPacket.Units.Add(new HorusUnitState { UnitId = i + 1, DefinitionKey = new string('d', HorusProtocol.MaxStringBytes), Name = new string('n', HorusProtocol.MaxStringBytes) });
        var maximumFactory = new HorusFactoryState { FactoryId = new string('f', HorusProtocol.MaxStringBytes), PresetName = new string('p', HorusProtocol.MaxStringBytes), LastStatus = new string('s', HorusProtocol.MaxStringBytes) };
        for (int i = 0; i < 16; i++) maximumFactory.ProductionKeys.Add(new string('k', HorusProtocol.MaxStringBytes));
        maximumPacket.Factories.Add(maximumFactory);
        Check("codec independently enforces the 16 KiB packet limit", Throws<InvalidDataException>(() => HorusWireCodec.Encode(HorusPacketKind.StatePage, maximumPacket)));
        Check("decoder rejects trailing data", Throws<InvalidDataException>(() => HorusWireCodec.Decode(AppendByte(encoded), out _)));

        Check("decoder rejects null packets", Throws<InvalidDataException>(() => HorusWireCodec.Decode(null, out _)));
        Check("decoder rejects truncated packets", Throws<EndOfStreamException>(() => HorusWireCodec.Decode(Truncate(encoded, encoded.Length - 1), out _)));
        Check("decoder rejects invalid packet magic", Throws<InvalidDataException>(() => HorusWireCodec.Decode(ReplaceByte(encoded, 0, 0), out _)));
        Check("decoder rejects unknown packet kinds", Throws<InvalidDataException>(() => HorusWireCodec.Decode(ReplaceByte(encoded, 4, byte.MaxValue), out _)));
        byte[] helloBytes = HorusWireCodec.Encode(HorusPacketKind.Hello, new HorusHello { ClientVersion = "ok" });
        helloBytes[9] = 0xC3;
        helloBytes[10] = 0x28;
        Check("decoder rejects invalid UTF-8", Throws<DecoderFallbackException>(() => HorusWireCodec.Decode(helloBytes, out _)));

        var emptyRequest = new HorusCommandEnvelope { Command = HorusCommandKind.Move };
        Check("validator rejects an empty request id", !HorusCommandValidator.TryValidate(emptyRequest, out _));
        var duplicateIds = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.Move };duplicateIds.Payload.UnitIds.Add(7);duplicateIds.Payload.UnitIds.Add(7);
        Check("validator rejects duplicate or replayed unit identities inside one command", !HorusCommandValidator.TryValidate(duplicateIds, out _));
        var controlText = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.Spawn };
        controlText.Payload.DefinitionKey = "AIR\nCRAFT";
        Check("validator rejects control characters in stable keys", !HorusCommandValidator.TryValidate(controlText, out _));
        var nonFiniteScalar = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.SetFuel };
        nonFiniteScalar.Payload.FloatValue = float.PositiveInfinity;
        Check("validator rejects non-finite scalar values", !HorusCommandValidator.TryValidate(nonFiniteScalar, out _));
        var maxEntities = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.Delete };
        for (uint i = 1; i <= HorusProtocol.MaxEntitiesPerCommand; i++) maxEntities.Payload.UnitIds.Add(i);
        Check("validator accepts the exact entity limit", HorusCommandValidator.TryValidate(maxEntities, out _));
        maxEntities.Payload.UnitIds.Add(999);
        Check("codec rejects entity lists above the limit", Throws<InvalidDataException>(() => HorusWireCodec.Encode(HorusPacketKind.Command, maxEntities)));
        var maxWaypoints = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.Patrol };
        for (int i = 0; i < HorusProtocol.MaxWaypointsPerCommand; i++) maxWaypoints.Payload.Points.Add(new HorusVector3(i, 0, i));
        Check("validator accepts the exact waypoint limit", HorusCommandValidator.TryValidate(maxWaypoints, out _));
        maxWaypoints.Payload.Points.Add(new HorusVector3(0, 0, 0));
        Check("codec rejects waypoint lists above the limit", Throws<InvalidDataException>(() => HorusWireCodec.Encode(HorusPacketKind.Command, maxWaypoints)));

        var oversizedKey = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.Spawn };
        oversizedKey.Payload.DefinitionKey = new string('x', HorusProtocol.MaxStringBytes + 1);
        Check("validator rejects stable keys above their UTF-8 byte limit", !HorusCommandValidator.TryValidate(oversizedKey, out _));
        var oversizedMountBytes = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.SetLoadout };
        for (int i = 0; i < 17; i++) oversizedMountBytes.Payload.MountKeys.Add(new string('m', HorusProtocol.MaxStringBytes));
        Check("validator rejects oversized aggregate mount bytes", !HorusCommandValidator.TryValidate(oversizedMountBytes, out _));
        Check("UTF-8 clamping never splits a multibyte character", HorusWireText.Clamp("éé", 3) == "é");
        Check("strict wire text rejects an unpaired surrogate", !HorusWireText.IsValid("\ud800"));
        Check("visible wire text removes control characters before display", HorusWireText.SanitizeVisible("line\nvalue") == "line value");

        Check("SteamID64 format accepts an individual account", HorusAdminAllowlist.IsIndividualSteamId64(76561198000000001UL));
        Check("SteamID64 format rejects arbitrary integers", !HorusAdminAllowlist.IsIndividualSteamId64(123UL));
        var strictAllowlist = HorusAdminAllowlist.Parse(new[] { "123", "0", "76561198000000001" }, out var strictErrors);
        Check("allowlist fails closed for non-individual identifiers", strictAllowlist.Count == 0 && strictErrors.Count == 2);

        var boundedDedup = new HorusRequestDeduplicator(2, 60d);
        Guid first = Guid.NewGuid(); Guid second = Guid.NewGuid(); Guid third = Guid.NewGuid();
        Check("deduplicator enforces bounded capacity", boundedDedup.TryRemember(first, 0d) && boundedDedup.TryRemember(second, 0d) && boundedDedup.TryRemember(third, 0d) && boundedDedup.TryRemember(first, 1d));
        Check("rate limiter rejects invalid token amounts", !bucket.TryConsume(1d, 0d) && !bucket.TryConsume(1d, 3d));

        Check("snapshot paging always emits one page", HorusPaging.ComputePageCount(0, 0, 8) == 1);
        Check("snapshot paging covers unequal collections", HorusPaging.ComputePageCount(17, 9, 8) == 3);
        Check("snapshot paging rejects invalid counts", Throws<ArgumentOutOfRangeException>(() => HorusPaging.ComputePageCount(-1, 0, 8)));
        state.RtsMode = 0;state.RtsDeployMode = 0;
        Check("snapshot policy accepts a bounded page", HorusSnapshotPolicy.IsValidPageShape(state));
        Check("snapshot policy accepts a complete coherent snapshot", HorusSnapshotPolicy.IsCoherentSnapshot(new[] { state }));
        state.Units.Add(new HorusUnitState { UnitId = 3, DefinitionKey = "AIRCRAFT_2", Name = "Duplicate", Position = new HorusVector3(1, 2, 3) });
        Check("snapshot policy rejects duplicate stable identities", !HorusSnapshotPolicy.IsCoherentSnapshot(new[] { state }));
        state.Units.RemoveAt(state.Units.Count - 1);
        var invalidPage = new HorusStatePage { SessionId = Guid.NewGuid(), SnapshotId = Guid.NewGuid(), PageIndex = 0, PageCount = HorusProtocol.MaxSnapshotPages + 1 };
        Check("snapshot policy rejects excessive page counts", !HorusSnapshotPolicy.IsValidPageShape(invalidPage));
        Check("snapshot codec rejects excessive page counts", Throws<InvalidDataException>(() => HorusWireCodec.Encode(HorusPacketKind.StatePage, invalidPage)));
        var tooManyUnitsPage = new HorusStatePage { SessionId = Guid.NewGuid(), SnapshotId = Guid.NewGuid(), PageIndex = 0, PageCount = 1 };
        for (uint i = 0; i <= HorusProtocol.MaxSnapshotUnitsPerPage; i++) tooManyUnitsPage.Units.Add(new HorusUnitState { UnitId = i + 1 });
        Check("snapshot codec rejects excessive per-page units", Throws<InvalidDataException>(() => HorusWireCodec.Encode(HorusPacketKind.StatePage, tooManyUnitsPage)));

        Check("ownership policy protects original mission units by default", !HorusOwnershipPolicy.CanMutate(false, false));
        Check("ownership policy permits Horus-owned or explicitly enabled units", HorusOwnershipPolicy.CanMutate(true, false) && HorusOwnershipPolicy.CanMutate(false, true));
        Check("persistence policy rejects non-finite positions", !HorusPersistencePolicy.IsSafePosition(float.NaN, 0f, 0f));
        Check("persistence policy accepts bounded queue keys", HorusPersistencePolicy.IsSafeStringCollection(new[] { "AIRCRAFT_A", "AIRCRAFT_B" }, HorusProtocol.MaxEntitiesPerCommand, out int persistedBytes) && persistedBytes > 0);
        var persistedOversize = new List<string>();for(int i=0;i<17;i++)persistedOversize.Add(new string('q',HorusProtocol.MaxStringBytes));
        Check("persistence policy rejects oversized queue bytes", !HorusPersistencePolicy.IsSafeStringCollection(persistedOversize, HorusProtocol.MaxEntitiesPerCommand, out _));

        Check("economy policy rejects non-finite and out-of-range budgets",
            !HorusEconomyPolicy.IsValidBudget(float.NaN) && !HorusEconomyPolicy.IsValidBudget(float.PositiveInfinity) &&
            !HorusEconomyPolicy.IsValidBudget(-1f) && !HorusEconomyPolicy.IsValidBudget(HorusEconomyPolicy.MaxBudget + 128f));
        Check("economy policy rejects negative and non-finite income",
            !HorusEconomyPolicy.IsValidIncome(-1f) && !HorusEconomyPolicy.IsValidIncome(float.NegativeInfinity));
        Check("economy policy bounds multipliers and tick intervals",
            HorusEconomyPolicy.IsValidMultiplier(1f) && !HorusEconomyPolicy.IsValidMultiplier(float.NaN) &&
            HorusEconomyPolicy.IsValidTickSeconds(5f) && !HorusEconomyPolicy.IsValidTickSeconds(HorusEconomyPolicy.MaxIncomeTickSeconds + 1f));
        Check("economy policy rejects invalid unit costs",
            !HorusEconomyPolicy.IsValidUnitCost(float.NaN) && !HorusEconomyPolicy.IsValidUnitCost(-0.01f));
        Check("economy budget addition preserves a finite authoritative result",
            HorusEconomyPolicy.TryAddBudget(100f, 25f, out float safeBudget) && safeBudget == 125f);
        Check("economy budget addition rejects overflow and overdraft",
            !HorusEconomyPolicy.TryAddBudget(HorusEconomyPolicy.MaxBudget, 1f, out _) &&
            !HorusEconomyPolicy.TryAddBudget(10f, -11f, out _));
        Check("economy cost aggregation preserves a finite authoritative result",
            HorusEconomyPolicy.TryAddUnitCost(100f, 50f, out float safeCost) && safeCost == 150f);
        Check("economy cost aggregation rejects overflow",
            !HorusEconomyPolicy.TryAddUnitCost(HorusEconomyPolicy.MaxUnitCost, 1f, out _));
        string economySource = ReadRepoFile("src", "Economy", "RtsEconomyManager.cs");
        Check("RTS economy bounds config input and re-resolves committed native unit costs",
            economySource.Contains("HorusEconomyPolicy.MaxConfigFileBytes", StringComparison.Ordinal) &&
            economySource.Contains("GetUnitCost(spawnedUnit.definition)", StringComparison.Ordinal) &&
            economySource.Contains("HorusEconomyPolicy.TryAddBudget", StringComparison.Ordinal) &&
            economySource.Contains("UnitMatchesFaction", StringComparison.Ordinal));
        Check("factory policy bounds per-faction and preset counts",
            HorusFactoryPolicy.IsValidFactoryLimit(10) && !HorusFactoryPolicy.IsValidFactoryLimit(0) &&
            !HorusFactoryPolicy.IsValidFactoryLimit(HorusFactoryPolicy.MaxFactoriesPerFaction + 1));
        Check("factory policy rejects non-finite and negative income",
            !HorusFactoryPolicy.IsValidIncome(float.NaN) && !HorusFactoryPolicy.IsValidIncome(-1f));
        Check("factory production requires a bounded interval and active-unit limit",
            HorusFactoryPolicy.IsValidProduction(90f, 10, true) && !HorusFactoryPolicy.IsValidProduction(0f, 10, true) &&
            !HorusFactoryPolicy.IsValidProduction(90f, 0, true) && HorusFactoryPolicy.IsValidProduction(0f, 0, false));
        Check("factory runtime policy rejects non-finite timers and coordinates",
            !HorusFactoryPolicy.IsValidRuntimeNumbers(0f, 100f, 90f, float.PositiveInfinity, 10, 50f, true));
        string dedicatedFactorySource = ReadRepoFile("src", "Server", "HeadlessRtsFactoryManager.cs");
        string dedicatedPluginSource = ReadRepoFile("src", "Server", "HorusServerPlugin.cs");
        Check("dedicated factory config is isolated and bounded",
            dedicatedFactorySource.Contains("rts_factories_server_config.json", StringComparison.Ordinal) &&
            dedicatedFactorySource.Contains("ValidateFactoryConfig", StringComparison.Ordinal) &&
            dedicatedFactorySource.Contains("MaxConfigFileBytes", StringComparison.Ordinal));
        Check("server runtime remains dormant without native authority",
            dedicatedPluginSource.Contains("loaded dormant", StringComparison.Ordinal) &&
            dedicatedPluginSource.Contains("if(!shouldRun){if(runtimeActive)DeactivateRuntime();return;}", StringComparison.Ordinal));
        Check("server configuration normalizes non-finite placement and bounded retention values",
            dedicatedPluginSource.Contains("NormalizeBoundedConfiguration", StringComparison.Ordinal) &&
            dedicatedPluginSource.Contains("!HorusPersistencePolicy.IsFinite(radius)", StringComparison.Ordinal) &&
            dedicatedPluginSource.Contains("auditRetentionEntry.Value<1||auditRetentionEntry.Value>365", StringComparison.Ordinal));
        Check("server allowlist input is strict UTF-8 and size bounded",
            dedicatedPluginSource.Contains("new UTF8Encoding(false,true)", StringComparison.Ordinal) &&
            dedicatedPluginSource.Contains("Allowlist file is oversized", StringComparison.Ordinal));
        string nucleiBridgeSource = ReadRepoFile("src", "Server", "HorusNucleiBridge.cs");
        string serverProjectSource = ReadRepoFile("Horus.Server.csproj");
        Check("optional Nuclei bridge matches the v1.3.3 command API without a binary dependency",
            nucleiBridgeSource.Contains("Nuclei.Features.Commands.ICommand", StringComparison.Ordinal) &&
            nucleiBridgeSource.Contains("method.Name==\"RegisterCommand\"&&method.GetParameters().Length==1", StringComparison.Ordinal) &&
            nucleiBridgeSource.Contains("Enum.Parse(permissionType,\"Everyone\"", StringComparison.Ordinal) &&
            nucleiBridgeSource.Contains("Enum.Parse(permissionType,\"Admin\"", StringComparison.Ordinal) &&
            nucleiBridgeSource.Contains("SendPrivateChatMessage", StringComparison.Ordinal) &&
            !serverProjectSource.Contains("MaxWasUnavailable.Nuclei", StringComparison.Ordinal));
        string windowsRuntimeRunner = ReadRepoFile("build", "runtime", "run-windows-dedicated.ps1");
        string linuxRuntimeRunner = ReadRepoFile("build", "runtime", "run-linux-dedicated.sh");
        Check("Windows runtime validation fails closed on readiness timeout and log flooding",
            windowsRuntimeRunner.Contains("ReadyTimeoutSeconds = 300", StringComparison.Ordinal) &&
            windowsRuntimeRunner.Contains("MaxLogBytes = 16777216", StringComparison.Ordinal) &&
            windowsRuntimeRunner.Contains("runtime-status.json", StringComparison.Ordinal));
        Check("Linux runtime validation fails closed on readiness timeout and log flooding",
            linuxRuntimeRunner.Contains("HORUS_READY_TIMEOUT_SECONDS:-300", StringComparison.Ordinal) &&
            linuxRuntimeRunner.Contains("HORUS_MAX_LOG_BYTES:-16777216", StringComparison.Ordinal) &&
            linuxRuntimeRunner.Contains("runtime-status.json", StringComparison.Ordinal));
        string deterministicBuildProps = ReadRepoFile("Directory.Build.props");
        Check("release projects preserve the default local game root and isolate restore assets",
            deterministicBuildProps.Contains("<NuclearOptionDir Condition=", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("$(MSBuildThisFileDirectory)..", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("<BaseIntermediateOutputPath>obj\\$(MSBuildProjectName)\\</BaseIntermediateOutputPath>", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("<MSBuildProjectExtensionsPath>$(BaseIntermediateOutputPath)</MSBuildProjectExtensionsPath>", StringComparison.Ordinal));
        Check("release assemblies do not embed the source commit identifier",
            deterministicBuildProps.Contains("<Deterministic>true</Deterministic>", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("<DebugType Condition=", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains(">none</DebugType>", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("<DebugSymbols Condition=", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains(">false</DebugSymbols>", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("<SourceRevisionId Condition=", StringComparison.Ordinal) &&
            deterministicBuildProps.Contains("0000000000000000000000000000000000000000", StringComparison.Ordinal));
        string clientFactorySource = ReadRepoFile("src", "Economy", "RtsFactoryManager.cs");
        Check("local factories use authoritative transactions and atomic persistence replacement",
            clientFactorySource.Contains("CreateTransaction(def, factory.factionId)", StringComparison.Ordinal) &&
            clientFactorySource.Contains("CommitTransaction(transaction, spawned)", StringComparison.Ordinal) &&
            clientFactorySource.Contains("current state was preserved", StringComparison.Ordinal));

        var serverState = new HorusServerState();
        Guid initialSession = serverState.SessionId;
        serverState.RecordSpawn(10); serverState.RecordSpawn(0); serverState.AdvanceRevision();
        Check("server state tracks Horus ownership and revisions", serverState.IsHorusOwned(10) && !serverState.IsHorusOwned(0) && serverState.Revision == 1);
        serverState.RecordDelete(10);
        Check("server state removes deleted ownership", !serverState.IsHorusOwned(10));
        serverState.RecordSpawn(11); serverState.BeginMission();
        Check("mission reset rotates session and clears state", serverState.SessionId != initialSession && serverState.Revision == 0 && !serverState.IsHorusOwned(11));

        var auditCommand = new HorusCommandEnvelope { RequestId = Guid.NewGuid(), Command = HorusCommandKind.Spawn };
        auditCommand.Payload.DefinitionKey = "DEF\"\\\n";
        auditCommand.Payload.FloatValue = float.NaN;
        var auditResult = new HorusCommandResult { RequestId = auditCommand.RequestId, Command = auditCommand.Command, Result = HorusResultCode.InvalidPayload, Revision = 9, Message = "bad\tvalue\nrejected" };
        string auditLine = HorusAuditFormatter.FormatJsonLine(new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc), 76561198000000001UL, "Mission\nName", auditCommand, auditResult);
        using (JsonDocument auditJson = JsonDocument.Parse(auditLine))
        {
            Check("audit formatter emits valid single-line JSON", !auditLine.Contains("\n", StringComparison.Ordinal) && auditJson.RootElement.GetProperty("revision").GetUInt64() == 9);
            Check("audit formatter serializes invalid numbers as null", auditJson.RootElement.GetProperty("parameters").GetProperty("floatValue").ValueKind == JsonValueKind.Null);
            Check("audit formatter includes sanitized parameter metadata", auditJson.RootElement.GetProperty("parameters").GetProperty("definitionKey").GetString() == "DEF\"\\\n");
        }
        DateTime now = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        Check("audit retention deletes records older than the policy", HorusAuditFormatter.ShouldDeleteAuditFile(now.AddDays(-15), now, 14));
        Check("audit retention keeps records on the boundary", !HorusAuditFormatter.ShouldDeleteAuditFile(now.AddDays(-14), now, 14));

        string serverTransportSource = ReadRepoFile("src", "Server", "HorusServerTransport.cs");
        string clientTransportSource = ReadRepoFile("src", "Client", "HorusClientTransport.cs");
        string serverExecutorSource = ReadRepoFile("src", "Server", "HorusServerCommandExecutor.cs");
        bool everyCommandMapped = true;
        foreach (HorusCommandKind commandKind in Enum.GetValues(typeof(HorusCommandKind)))
        {
            if (commandKind == HorusCommandKind.None) continue;
            string caseToken = "case HorusCommandKind." + commandKind;
            everyCommandMapped &= serverExecutorSource.Contains(caseToken, StringComparison.Ordinal) &&
                                  clientTransportSource.Contains(caseToken, StringComparison.Ordinal);
        }
        Check("every advertised command has server execution and client capability routing", everyCommandMapped);
        Check("server rate limiting and deduplication are keyed by authenticated Steam principal",
            serverTransportSource.Contains("GetPrincipal(client.SteamId", StringComparison.Ordinal) &&
            serverTransportSource.Contains("principal.Deduplicator", StringComparison.Ordinal));
        Check("server authority requires the native Steam session to remain valid",
            serverTransportSource.Contains("!auth.SteamSessionOk", StringComparison.Ordinal));
        Check("server and client revoke cached authority after live authentication loss",
            serverTransportSource.Contains("SendCurrentCapabilities(pair.Key", StringComparison.Ordinal) &&
            clientTransportSource.Contains("value.Result==HorusResultCode.SteamRequired", StringComparison.Ordinal));
        string serverStateSource = ReadRepoFile("src", "Server", "HorusServerState.cs");
        Check("server persists and rechecks the negotiated protocol before granting authority",
            serverStateSource.Contains("HelloProtocolVersion", StringComparison.Ordinal) &&
            serverTransportSource.Contains("client.HelloProtocolVersion=hello.ProtocolVersion", StringComparison.Ordinal) &&
            serverTransportSource.Contains("client.HelloProtocolVersion!=HorusProtocol.Version", StringComparison.Ordinal) &&
            !serverTransportSource.Contains("pair.Value.HelloReceived=true", StringComparison.Ordinal));
        Check("structured rejection auditing is bounded by trusted Steam principal",
            serverTransportSource.Contains("RejectionAuditRate.TryConsume", StringComparison.Ordinal) &&
            serverTransportSource.Contains("command!=null&&steamId!=0", StringComparison.Ordinal) &&
            serverTransportSource.Contains("!auth.SteamSessionOk", StringComparison.Ordinal) &&
            serverTransportSource.Contains("HorusAdminAllowlist.IsIndividualSteamId64", StringComparison.Ordinal));
        Check("stale state requests recover through a fresh capability session",
            serverTransportSource.Contains("request.SessionId!=state.SessionId", StringComparison.Ordinal) &&
            serverTransportSource.Contains("Mission session refreshed.", StringComparison.Ordinal));
        string serverPluginSource = ReadRepoFile("src", "Server", "HorusServerPlugin.cs");
        Check("dedicated safety exposes a separate original-unit mutation policy",
            serverPluginSource.Contains("AllowMissionUnitMutation", StringComparison.Ordinal));
        string serverCompatibilitySource = ReadRepoFile("src", "Server", "HorusServerCompatibility.cs");
        string ordnancePatchSource = ReadRepoFile("src", "Server", "HorusServerOrdnancePatches.cs");
        string bombingPatchSource = ReadRepoFile("src", "Interaction", "HorusBombingCorrection.cs");
        string tacticalPatchSource = ReadRepoFile("src", "Interaction", "HorusTacticalHarmonyPatches.cs");
        Check("dedicated gameplay patches preserve native behavior while Horus is disabled",
            serverPluginSource.Contains("HorusMod.HorusPlugin.ServerEnabled=enabledEntry", StringComparison.Ordinal) &&
            serverCompatibilitySource.Contains("IsRuntimeEnabled => ServerEnabled?.Value == true", StringComparison.Ordinal) &&
            ordnancePatchSource.Contains("IsRuntimeEnabled)return true", StringComparison.Ordinal) &&
            bombingPatchSource.Contains("if (!HorusPlugin.IsRuntimeEnabled) return true", StringComparison.Ordinal) &&
            tacticalPatchSource.Contains("if (!HorusPlugin.IsRuntimeEnabled) return true", StringComparison.Ordinal));
        Check("snapshot requests coalesce and recover after a dropped rate-limited response",
            clientTransportSource.Contains("snapshotNeeded=true", StringComparison.Ordinal) &&
            clientTransportSource.Contains("now-snapshotRequestSentTime<5f", StringComparison.Ordinal) &&
            clientTransportSource.Contains("nextSnapshotRequestTime=now+0.55f", StringComparison.Ordinal));
        string headlessFactorySource = ReadRepoFile("src", "Server", "HeadlessRtsFactoryManager.cs");
        Check("headless factories replicate and remove validated native visual anchors",
            headlessFactorySource.Contains("SpawnFactoryVisual", StringComparison.Ordinal) &&
            headlessFactorySource.Contains("NetworkServer.Destroy", StringComparison.Ordinal) &&
            headlessFactorySource.Contains("UnitProduced?.Invoke(anchor)", StringComparison.Ordinal));
    }

    private static T RoundTrip<T>(HorusPacketKind kind, object value) => (T)HorusWireCodec.Decode(HorusWireCodec.Encode(kind, value), out _);
    private static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    private static byte[] AppendByte(byte[] source) { var result = new byte[source.Length + 1]; Buffer.BlockCopy(source, 0, result, 0, source.Length); result[result.Length - 1] = 1; return result; }
    private static byte[] Truncate(byte[] source, int length) { var result = new byte[length]; Buffer.BlockCopy(source, 0, result, 0, length); return result; }
    private static byte[] ReplaceByte(byte[] source, int index, byte value) { var result = (byte[])source.Clone(); result[index] = value; return result; }

    private static string ReadRepoFile(params string[] parts)
    {
        string root = Directory.GetCurrentDirectory();
        for (int i = 0; i < 8; i++)
        {
            string candidate = root;
            for (int part = 0; part < parts.Length; part++) candidate = Path.Combine(candidate, parts[part]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            DirectoryInfo parent = Directory.GetParent(root);
            if (parent == null) break;
            root = parent.FullName;
        }
        throw new FileNotFoundException("Could not locate repository test input", Path.Combine(parts));
    }

    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        passed++;
    }
}
