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
        /// <summary>Volumes nobody may walk into (Deadlock's vent tunnels): no navigation nodes there.</summary>
        public int[] NoWalk = System.Array.Empty<int>();       // min x, y, z, max x, y, z per box
        /// <summary>Openings to seal with an invisible wall (the vents' entrances).</summary>
        public int[] Seal = System.Array.Empty<int>();
        /// <summary>Deadlock's shops inside the outline (spots for interactables).</summary>
        public int[] Shops = System.Array.Empty<int>();
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
            NoWalk = SunkenPlazaData.VentTunnels,
            Seal = SunkenPlazaData.VentOpenings,
            Shops = SunkenPlazaData.Shops,
        };
    }

    internal static class SunkenPlazaData
    {
        public static readonly float[] Outline = {-8010, 576, -7869, 1256, -7757, 2042, -7528, 2883, -6921, 3447, -6347, 4221, -6262, 4769, -6010, 5393, -5403, 5764, -5036, 5933, -4453, 6300, -1800, 6300, -1800, 5000, 830, 5000, 823, 3898, 776, 3628, 667, 3488, -413, 2842, -578, 1729, -578, 695, -473, 100, -8230, 100 };
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
        public static readonly int[] Shops = { // min x, y, z, max x, y, z
            -1078, 1104, 376, -762, 1376, 504,
            -6600, 1616, 248, -6284, 1888, 376,
            -4088, 976, -144, -3772, 1248, -16,
        };
    }
}
