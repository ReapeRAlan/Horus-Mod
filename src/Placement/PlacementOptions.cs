namespace HorusMod.Placement
{
    /// <summary>
    /// Immutable placement state captured at the boundary between UI/input and spawning.
    /// This prevents a spawn operation from observing half-updated editor fields.
    /// </summary>
    public sealed class PlacementOptions
    {
        public UnitDefinition Definition { get; }
        public int FactionIndex { get; }
        public float Altitude { get; }
        public float Yaw { get; }
        public bool GridSnap { get; }
        public float GridSize { get; }
        public bool GroundSnap { get; }
        public bool SurfaceAlign { get; }
        public bool Stationary { get; }
        public FormationKind Formation { get; }
        public float FormationSpacing { get; }
        public int AircraftLiveryMode { get; }
        public int AircraftLoadoutMode { get; }
        public int SelectedLiveryIndex { get; }
        public int SelectedLoadoutIndex { get; }
        public float Skill { get; }
        public bool ApplyAircraftToWholeGroup { get; }

        public PlacementOptions(
            UnitDefinition definition,
            int factionIndex,
            float altitude,
            float yaw,
            bool gridSnap,
            float gridSize,
            bool groundSnap,
            bool surfaceAlign,
            bool stationary,
            FormationKind formation,
            float formationSpacing,
            int aircraftLiveryMode,
            int aircraftLoadoutMode,
            int selectedLiveryIndex,
            int selectedLoadoutIndex,
            float skill,
            bool applyAircraftToWholeGroup)
        {
            Definition = definition;
            FactionIndex = factionIndex;
            Altitude = altitude;
            Yaw = yaw;
            GridSnap = gridSnap;
            GridSize = gridSize;
            GroundSnap = groundSnap;
            SurfaceAlign = surfaceAlign;
            Stationary = stationary;
            Formation = formation;
            FormationSpacing = formationSpacing;
            AircraftLiveryMode = aircraftLiveryMode;
            AircraftLoadoutMode = aircraftLoadoutMode;
            SelectedLiveryIndex = selectedLiveryIndex;
            SelectedLoadoutIndex = selectedLoadoutIndex;
            Skill = UnityEngine.Mathf.Clamp01(skill);
            ApplyAircraftToWholeGroup = applyAircraftToWholeGroup;
        }
    }
}
