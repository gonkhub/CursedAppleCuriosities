namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// A playable piece of a Deadlock map. Everything is in Deadlock (Source) units and axes: x/y on the ground, z up,
    /// 1 unit = 0.0254 m. The outline bounds the playable area; the rest of the map is converted as a render-only
    /// backdrop behind a barrier.
    /// </summary>
    public class MapSectionDef
    {
        public string Id = "";
        public string Map = "dl_midtown";
        public float[] Outline = System.Array.Empty<float>();   // x0, y0, x1, y1, ... (closed implicitly)
        /// <summary>Where the section's Unity origin sits (Deadlock units). Fixed, so marks and placements stay put when the outline changes.</summary>
        public float[] Origin = System.Array.Empty<float>();
        /// <summary>Volumes nobody may walk into (Deadlock's vent tunnels): no navigation nodes there.</summary>
        public int[] NoWalk = System.Array.Empty<int>();       // min x, y, z, max x, y, z per box
        /// <summary>Openings to seal with an invisible wall (the vents' entrances).</summary>
        public int[] Seal = System.Array.Empty<int>();
        /// <summary>Deadlock's shops inside the outline (spots for interactables).</summary>
        public int[] Shops = System.Array.Empty<int>();
        /// <summary>Deadlock materials left out entirely (drawn and solid), e.g. a prop to be replaced; their biggest piece's bounds are kept.</summary>
        public string[] HiddenMaterials = System.Array.Empty<string>();
        /// <summary>Where interactables (chests, shrines...) may spawn: near these points (x, y, z units), Deadlock's neutral camps.</summary>
        public int[] InteractableSpots = System.Array.Empty<int>();
        /// <summary>Doorways that send whoever walks in to the teleporter (min x, y, z, max x, y, z units).</summary>
        public int[] SubwayTeleports = System.Array.Empty<int>();
        /// <summary>Newt altars: x, y, z, yaw per altar, in the section's Unity space (from cac_map_mark).</summary>
        public float[] NewtAltars = System.Array.Empty<float>();
        /// <summary>Lowest point of the playable area (units): anything below falls out of the map.</summary>
        public float FloorZ = -800f;

        /// <summary>
        /// Sunken Plaza: the Archmother side between the yellow lane (included) and the blue lane (excluded), from the
        /// trench up to below the base. Numbers read from the 2026-10-09 dl_midtown (lane ziplines, tunnel and shop
        /// triggers); see tools/mapstage.
        /// </summary>
        public static readonly MapSectionDef SunkenPlaza = new MapSectionDef
        {
            Id = "sunkenplaza",
            Outline = SunkenPlazaData.Outline,
            Origin = new[] { -3700f, 3200f, 0f }, // the first outline's centre (2026-10-13): every mark is relative to it
            NoWalk = SunkenPlazaData.VentTunnels,
            Seal = SunkenPlazaData.VentOpenings,
            Shops = SunkenPlazaData.Shops,
            NewtAltars = SunkenPlazaData.NewtAltars,
            InteractableSpots = SunkenPlazaData.InteractableSpots,
            SubwayTeleports = SunkenPlazaData.SubwayTeleports,
        };
    }

    internal static class SunkenPlazaData
    {
        // Outline (Deadlock units, x/y), from the yellow and blue lane ziplines (tools/mapstage/selpoly.py), widened by the
        // user 2026-10-13: the street outside the yellow-side base approach, the whole NYSE block, the strip west of the yellow lane; south edge on the railing over the trench;
        // then redrawn by the user (west and north to the map's natural edge, no kink at the Bank).
        public static readonly float[] Outline = {-9508, 704, -9508, 1875, -9388, 3057, -9439, 3197, -9388, 3301, -9422, 3927, -9009, 4031, -8854, 4796, -8441, 4935, -8406, 5979, -7976, 6187, -7149, 6222, -6443, 6535, -6340, 6674, -5789, 6709, -5479, 6918, -4859, 7126, -4773, 7231, -4670, 7196, -4188, 7103, -3964, 7103, -3499, 7578, -3034, 7648, -2311, 7891, -1571, 7926, -865, 7752, -779, 6709, -830, 6639, -796, 5074, -727, 5040, -555, 5144, -314, 5144, 380, 5364, 392, 5109, 581, 5109, 847, 4898, 771, 4796, 788, 3892, 702, 3683, 375, 3475, -193, 3266, -486, 2953, -434, 1075, -469, 658, -589, 414, -1123, 484, -4033, 484, -6220, 414, -6650, 449, -6874, 693, -7029, 762, -7339, 727, -7511, 623, -7511, 519, -7614, 449, -7563, 310, -7718, 67, -8137, 443, -7890, 554, -7907, 693, -9095, 797 };
        public static readonly int[] VentTunnels = { // min x, y, z, max x, y, z
            -1328, 1312, -640, 832, 1696, -184,
            -1328, 1056, -640, -640, 1312, -448,
            -2848, 1184, 112, -2688, 1376, 368,
            -1280, 1152, 112, -960, 1696, 320,
            -640, 1312, 112, 256, 1536, 320,
            -896, 1152, 112, -704, 1536, 318,
            -2592, 1152, 112, -1664, 1408, 352,
            -1664, 1152, 112, -1600, 1408, 320,
            -768, 1312, 128, -576, 1536, 320,
            -1536, 992, 128, -1344, 1184, 320,
            -3008, 1184, 256, -2848, 1376, 368,
            -5156, 2432, 0, -5012, 2560, 392,
            -4544, 2272, -160, -3760, 2704, 160,
            -4544, 1648, -160, -4416, 2080, 160,
            -2132, 320, -672, -1504, 576, -176,
            -5184, 2560, 256, -5040, 2620, 392,
            -2816, 512, -512, -2560, 960, -128,
            -2368, 320, -672, -2112, 576, -176,
            -2560, 320, -672, -2368, 768, -128,
            -2816, 960, -640, -1328, 1248, -16,
            -1504, 320, -672, -1056, 448, -544,
            -2976, 960, -144, -2824, 1248, -16,
            288, 3584, 112, 768, 3712, 272,
            -784, 3392, 112, -624, 3520, 480,
            596, 3584, 48, 1024, 3712, 272,
            -512, 3392, 112, -16, 3520, 336,
            -32, 3392, 112, 288, 3712, 336,
            -816, 3392, 384, -720, 3520, 480,
        };
        public static readonly int[] VentOpenings = { // min x, y, z, max x, y, z
            -1048, 336, -640, -968, 432, -576,
            -2936, 1232, 256, -2856, 1328, 320,
            -3064, 1104, -144, -2984, 1200, -80,
            -4520, 1472, -144, -4440, 1568, -80,
            -5152, 2620, 256, -5072, 2716, 320,
            -872, 3408, 384, -792, 3504, 448,
        };
        /// <summary>Deadlock's neutral trooper camps and spawns and item crates (x, y, z units): where interactables may stand.</summary>
        public static readonly int[] InteractableSpots = {
            -8792, 4096, 640, -8765, 4337, 640, -8745, 4540, 640, -8624, 4264, 648,
            -8470, 4429, 640, -8464, 4136, 640, -8096, 1891, 259, -7968, 2304, 259,
            -7959, 2040, 262, -7824, 1808, 259, -7790, 2272, 256, -7776, 2096, 259,
            -5843, 1581, 928, -5728, 1088, 256, -5728, 1216, 256, -5696, 1664, 936,
            -5696, 1792, 928, -5549, 1581, 928, -4672, 2816, 256, -4640, 2560, 256,
            -4544, 2560, 656, -4496, 1792, -144, -4496, 1920, -144, -4480, 2240, 256,
            -4480, 2560, 264, -4448, 1856, -144, -4384, 2432, 256, -4384, 2688, 256,
            -3936, 3896, 384, -3808, 4024, 384, -3680, 3896, 384, -3680, 4152, 384,
            -3104, 2432, -44, -3104, 2688, -44, -2976, 2560, -44, -2816, 2560, -44,
            -2592, 2560, -44, -2464, 1120, -256, -2432, 2560, -44, -2400, 1168, -256,
            -2348, 1024, 392, -2336, 1120, -256, -2304, 2432, -44, -2304, 2672, -44,
            -2220, 1024, 392, -2176, 1984, -32, -2176, 2080, -32, -2176, 3040, -32,
            -2176, 3136, -32, -2092, 928, 384, -2092, 1120, 384, -1388, 800, 384,
            -1297, 928, 384, -1201, 800, 384, -1201, 1088, 384, -960, 3840, 384,
            -832, 3904, 384, -832, 4024, 384, -720, 1584, -640, -704, 3840, 384,
            -640, 1472, -640, -560, 1584, -640,
        };
        /// <summary>Deadlock's subway "TELEPORTER" doorway (min x, y, z, max x, y, z units): walking in sends you to the teleporter.</summary>
        public static readonly int[] SubwayTeleports = { -3960, 1736, -144, -3848, 1848, -16 };
        /// <summary>Hidden by hand in-game (cac_map_mark newt): x, y, z, yaw in section space.</summary>
        public static readonly float[] NewtAltars = {
            60.09f, 30.79f, -4.78f, -89.7f,
            15.35f, 29.38f, -43.29f, -117.3f,
            -24.37f, 26.02f, 31.50f, -115.2f,
            45.55f, -3.24f, 19.36f, -28.3f,
        };
        public static readonly int[] Shops = { // min x, y, z, max x, y, z
            -1078, 1104, 376, -762, 1376, 504,
            -6600, 1616, 248, -6284, 1888, 376,
            -4088, 976, -144, -3772, 1248, -16,
        };
    }
}
