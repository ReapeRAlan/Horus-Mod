namespace HorusMod.Interaction
{
    /// <summary>
    /// Pure RMB gesture policy shared by the world and map input paths.
    /// A stationary press remains a click no matter how long it is held; only
    /// deliberate pointer movement converts it into camera look.
    /// </summary>
    public static class RmbGestureClassifier
    {
        public const float DragThresholdPixels = 10f;

        public static bool IsDrag(float deltaX, float deltaY)
        {
            return deltaX * deltaX + deltaY * deltaY > DragThresholdPixels * DragThresholdPixels;
        }
    }
}
