using System;
using System.IO;
using HorusMod.Interaction;
using HorusMod.Spawning;

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

        Console.WriteLine($"Horus logic tests passed: {passed}");
    }

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
