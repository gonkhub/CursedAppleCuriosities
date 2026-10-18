using UnityEngine;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>One Deadlock district at runtime: its definition, this machine's conversion of it, and its stage.</summary>
    public sealed class District
    {
        public readonly MapSectionDef Def;
        /// <summary>This machine's conversion (null when Deadlock maps are off in the config).</summary>
        public MapConverter? Converter { get; internal set; }
        public DistrictStage Stage { get; internal set; } = null!;

        public District(MapSectionDef def) { Def = def; }

        public string Id => Def.Id;
        public bool Ready => Converter != null && Converter.State == MapState.Ready;

        /// <summary>Where players arrive, in the section's Unity space (the section origin when the district has none).</summary>
        public Vector3 ArrivalLocal(Vector3 originUnits) =>
            Def.Arrival.Length >= 3 ? MapCache.ToUnity(new Vector3(Def.Arrival[0], Def.Arrival[1], Def.Arrival[2]), originUnits) : Vector3.zero;
    }
}
