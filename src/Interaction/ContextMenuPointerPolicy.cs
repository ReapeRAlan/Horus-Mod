namespace HorusMod.Interaction
{
    public enum ContextMenuOutsideClickAction
    {
        None = 0,
        DismissAndConsume,
        DismissAndContinue
    }

    /// <summary>Pure outside-click ownership rule for the IMGUI context menu.</summary>
    public static class ContextMenuPointerPolicy
    {
        public static ContextMenuOutsideClickAction Classify(bool menuOpen, bool pointerInside, bool leftDown, bool rightDown)
        {
            if (!menuOpen || pointerInside) return ContextMenuOutsideClickAction.None;
            if (leftDown) return ContextMenuOutsideClickAction.DismissAndConsume;
            if (rightDown) return ContextMenuOutsideClickAction.DismissAndContinue;
            return ContextMenuOutsideClickAction.None;
        }
    }
}
