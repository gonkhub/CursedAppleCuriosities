namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// A district: a playable piece of a Deadlock map and the RoR2 stage made of it. Everything spatial is in Deadlock
    /// (Source) units and axes: x/y on the ground, z up, 1 unit = 0.0254 m. The outline bounds the playable area; the rest
    /// of the map is converted as a render-only backdrop behind a barrier.
    ///
    /// Adding a district: draw its outline on a tools/mapstage overview, trace it (trace_outline.py), generate its data
    /// (section_consts.py) into a new data class, give it a reserved shell scene (Scenes below), list it in All, then mark
    /// its newt altars and player spawns in game (cac_map_mark). Plain data only: the converter (net472) compiles this too.
    /// </summary>
    public class MapSectionDef
    {
        public string Id = "";              // folder, fingerprint and command name: never change once released
        public string Map = "dl_midtown";

        // ---- the stage
        public string Name = "", Subtitle = "";
        /// <summary>Its shell scene in the cac_stagescenes bundle (one of <see cref="Scenes"/>).</summary>
        public string Scene = "";
        /// <summary>The stage slot it joins (1-5): offered among that slot's vanilla stages.</summary>
        public int StageOrder = 3;
        /// <summary>Where players arrive (Deadlock units): spawns are picked near it unless placed by hand.</summary>
        public float[] Arrival = System.Array.Empty<float>();
        /// <summary>Vanilla assets borrowed (Addressables keys): monster and interactable pools, music, sky.</summary>
        public string MonsterPool = "RoR2/Base/frozenwall/dpFrozenWallMonsters.asset";
        public string InteractablePool = "RoR2/Base/frozenwall/dpFrozenWallInteractables.asset";
        public string MusicFrom = "RoR2/Base/frozenwall/frozenwall.asset";
        public string Sky = "RoR2/DLC1/ancientloft/matSkyboxAncientLoft.mat";

        // ---- the geometry
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
        /// <summary>Deadlock's climb ropes (min x, y, z, max x, y, z units): a RoR2 geyser at each one's foot launches you to its top.</summary>
        public int[] Ropes = System.Array.Empty<int>();
        /// <summary>Doorways that send whoever walks in to the teleporter (min x, y, z, max x, y, z units).</summary>
        public int[] SubwayTeleports = System.Array.Empty<int>();
        /// <summary>
        /// Interactables always placed at a spot (cac_map_mark), on top of what the director spends: a vanilla spawn card
        /// (Addressables key) at x, y, z, yaw in the section's Unity space. E.g. Chinatown's legendary chest on its bell tower.
        /// </summary>
        public FixedSpawn[] FixedInteractables = System.Array.Empty<FixedSpawn>();
        /// <summary>Player spawns placed by hand (cac_map_mark spawn): x, y, z, yaw each, in the section's Unity space. Empty = picked automatically.</summary>
        public float[] PlayerSpawns = System.Array.Empty<float>();
        /// <summary>
        /// Where the district's Walker stands in Deadlock (x, y, z, yaw in the section's Unity space; cac_map_mark): something
        /// important goes there every stage, the teleporter (Walker Teleporter Chance) or else one major interactable.
        /// </summary>
        public float[] WalkerSlot = System.Array.Empty<float>();
        /// <summary>
        /// The centre of a pit where using any interactable wakes an elite Imp Overlord nearby, once per visit (x, y, z, yaw in the section's Unity space; cac_map_mark imp).
        /// Sunken Plaza's purple pit.
        /// </summary>
        public float[] ImpLordSpot = System.Array.Empty<float>();
        /// <summary>
        /// The district's secrets: a blacked-out silhouette of each hero stands at its spot (cac_map_mark). Touching one unlocks
        /// that hero for everyone in the run.
        /// </summary>
        public SecretSpot[] Secrets = System.Array.Empty<SecretSpot>();
        /// <summary>Newt altar spots: x, y, z, yaw each, in the section's Unity space (from cac_map_mark). One altar per stage, at one of them.</summary>
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
            Name = "Sunken Plaza",
            Subtitle = "Archmother's Ward",
            Scene = "cac_sunkenplaza",
            StageOrder = 3,
            Arrival = new[] { -2400f, 2700f, 600f }, // the plaza's centre, at street height
            Outline = SunkenPlazaData.Outline,
            Origin = new[] { -3700f, 3200f, 0f }, // the first outline's centre (2026-10-13): every mark is relative to it
            NoWalk = SunkenPlazaData.VentTunnels,
            Seal = SunkenPlazaData.VentOpenings,
            Shops = SunkenPlazaData.Shops,
            NewtAltars = SunkenPlazaData.NewtAltars,
            InteractableSpots = SunkenPlazaData.InteractableSpots,
            SubwayTeleports = SunkenPlazaData.SubwayTeleports,
            Ropes = SunkenPlazaData.Ropes,
            // The sunken square in the plaza's middle (~30 x 30 m, 8 m below the street; Deadlock -2750, 2550, floor -1.1 m).
            ImpLordSpot = new[] { 16.51f, -1.10f, 24.13f, 0f },
            Secrets = new[]
            {
                new SecretSpot("Paradox", 20.45f, 6.51f, 2.05f, 88.0f),    // user's mark paradoxsecret2, 2026-10-15
                new SecretSpot("Lash", 19.76f, 210.12f, -38.88f, -1.8f),   // user's mark paradoxsecret: the top of the district's highest building (user: "perfect for him")
            },
        };

        /// <summary>Haunted Lot: the Hidden King side west of the blue lane, around the yellow lane's southern Walker (stage 1). Newt altars and player spawns: to be marked in game.</summary>
        public static readonly MapSectionDef HauntedLot = new MapSectionDef
        {
            Id = "hauntedlot",
            Name = "Haunted Lot",
            Subtitle = "Hidden King's Ward",
            Scene = "cac_district2",
            StageOrder = 1,
            Origin = new[] { -4800f, -3500f, 0f }, // the drawn outline's centre: marks are relative to it, so it never moves
            Arrival = new[] { -4800f, -3500f, 600f },
            MonsterPool = "RoR2/Base/blackbeach/dpBlackBeachMonsters.asset",
            InteractablePool = "RoR2/Base/blackbeach/dpBlackBeachInteractables.asset",
            MusicFrom = "RoR2/Base/blackbeach/blackbeach.asset",
            Outline = HauntedLotData.Outline,
            NoWalk = HauntedLotData.VentTunnels,
            Seal = HauntedLotData.VentOpenings,
            Shops = HauntedLotData.Shops,
            SubwayTeleports = HauntedLotData.SubwayTeleports,
            InteractableSpots = HauntedLotData.InteractableSpots,
            Ropes = HauntedLotData.Ropes,
            NewtAltars = HauntedLotData.NewtAltars,
            PlayerSpawns = HauntedLotData.PlayerSpawns,
            Secrets = new[] { new SecretSpot("Bebop", 37.31f, 30.90f, 41.68f, -32.0f) }, // user's mark bebopsecret, 2026-10-15
        };

        /// <summary>Umbria University: the Archmother side east of the blue lane, around the purple lane's northern Walker (stage 2). Newt altars and player spawns: to be marked in game.</summary>
        public static readonly MapSectionDef Umbria = new MapSectionDef
        {
            Id = "umbria",
            Name = "Umbria University",
            Subtitle = "Archmother's Ward",
            Scene = "cac_district3",
            StageOrder = 2,
            Origin = new[] { 5400f, 3900f, 0f }, // the drawn outline's centre: marks are relative to it, so it never moves
            Arrival = new[] { 5400f, 3900f, 600f },
            MonsterPool = "RoR2/Base/goolake/dpGooLakeMonsters.asset",
            InteractablePool = "RoR2/Base/goolake/dpGooLakeInteractables.asset",
            MusicFrom = "RoR2/Base/goolake/goolake.asset",
            Outline = UmbriaData.Outline,
            NoWalk = UmbriaData.VentTunnels,
            Seal = UmbriaData.VentOpenings,
            Shops = UmbriaData.Shops,
            SubwayTeleports = UmbriaData.SubwayTeleports,
            InteractableSpots = UmbriaData.InteractableSpots,
            Ropes = UmbriaData.Ropes,
            NewtAltars = UmbriaData.NewtAltars,
            PlayerSpawns = UmbriaData.PlayerSpawns,
            WalkerSlot = new[] { -21.83f, 9.56f, 27.21f, 35.5f }, // the Walker's spot (user's mark, 2026-10-14)
            Secrets = new[] { new SecretSpot("Ivy", 77.83f, -2.43f, -118.25f, -90.1f) }, // user's mark umbriasecret, 2026-10-14
        };

        /// <summary>
        /// Chinatown: the Hidden King side east of the blue lane, around the purple lane's southern Walker (stage 4). Newt altars,
        /// player spawns and the legendary chest on the bell tower (FixedInteractables, FixedSpawn.LegendaryChest): to be marked in game.
        /// </summary>
        public static readonly MapSectionDef Chinatown = new MapSectionDef
        {
            Id = "chinatown",
            Name = "Chinatown",
            Subtitle = "Hidden King's Ward",
            Scene = "cac_district4",
            StageOrder = 4,
            Origin = new[] { 3800f, -4700f, 0f }, // the drawn outline's centre: marks are relative to it, so it never moves
            Arrival = new[] { 3800f, -4700f, 600f },
            MonsterPool = "RoR2/Base/rootjungle/dpRootJungleMonsters.asset",
            InteractablePool = "RoR2/Base/rootjungle/dpRootJungleInteractables.asset",
            MusicFrom = "RoR2/Base/rootjungle/rootjungle.asset",
            Outline = ChinatownData.Outline,
            NoWalk = ChinatownData.VentTunnels,
            Seal = ChinatownData.VentOpenings,
            Shops = ChinatownData.Shops,
            SubwayTeleports = ChinatownData.SubwayTeleports,
            InteractableSpots = ChinatownData.InteractableSpots,
            Ropes = ChinatownData.Ropes,
            // The legendary chest on the bell tower's top floor: inside the tower's footprint, under its roof (rays from just
            // above the top of its climb rope, ChinatownData.Ropes[2]), beside where the rope's geyser lands you.
            FixedInteractables = new[] { FixedSpawn.OnTop(FixedSpawn.LegendaryChest, 2636f, -2532f, 3532f, -1256f, below: 2630f + 120f, besideX: 3088f, besideY: -1888f) },
            NewtAltars = ChinatownData.NewtAltars,
            PlayerSpawns = ChinatownData.PlayerSpawns,
            WalkerSlot = new[] { 9.43f, 10.17f, 47.00f, -89.7f }, // the Walker's spot (user's mark, 2026-10-14)
            Secrets = new[] { new SecretSpot("Mina", -74.25f, 52.59f, -76.85f, -0.2f) }, // user's mark chinatownsecret, 2026-10-14
        };

        /// <summary>Every district, in the order they are converted (the first finishes first).</summary>
        public static readonly MapSectionDef[] All = { SunkenPlaza, HauntedLot, Umbria, Chinatown };

        /// <summary>The shell scenes the stage bundle carries (tools/stageshell): one per district, spares for the next ones.</summary>
        public static readonly string[] Scenes = { "cac_sunkenplaza", "cac_district2", "cac_district3", "cac_district4" };

        public static MapSectionDef? Find(string id)
        {
            foreach (MapSectionDef d in All) if (d.Id == id) return d;
            return null;
        }
    }

    /// <summary>A hero's silhouette: who (the survivor's name, as the plugin lists it) and where (x, y, z, yaw in the section's Unity space).</summary>
    public class SecretSpot
    {
        public string Hero;
        public float X, Y, Z, Yaw;
        public SecretSpot(string hero, float x, float y, float z, float yaw) { Hero = hero; X = x; Y = y; Z = z; Yaw = yaw; }
    }

    /// <summary>One interactable a district always has at a spot (see <see cref="MapSectionDef.FixedInteractables"/>).</summary>
    public class FixedSpawn
    {
        public const string LegendaryChest = "RoR2/Base/GoldChest/iscGoldChest.asset";
        public string Card = "";
        public float X, Y, Z, Yaw;
        /// <summary>
        /// Instead of a marked spot: the highest flat spot inside this box (x0, y0, x1, y1 Deadlock units), e.g. a roof; with a
        /// 5th value, the highest under that height (a top floor under a roof); with 6th and 7th, the spot of that floor nearest
        /// this point that still leaves it clear (e.g. beside a geyser's landing).
        /// </summary>
        public float[]? Atop;
        public FixedSpawn(string card, float x, float y, float z, float yaw) { Card = card; X = x; Y = y; Z = z; Yaw = yaw; }
        /// <summary>On top of whatever stands in the box (Deadlock units): the highest flat spot found there (see <see cref="Atop"/>).</summary>
        public static FixedSpawn OnTop(string card, float x0, float y0, float x1, float y1, float below = float.NaN, float besideX = float.NaN, float besideY = float.NaN) =>
            new FixedSpawn(card, 0f, 0f, 0f, 0f) { Atop = float.IsNaN(below) ? new[] { x0, y0, x1, y1 } : float.IsNaN(besideX) ? new[] { x0, y0, x1, y1, below } : new[] { x0, y0, x1, y1, below, besideX, besideY } };
    }

    internal static class SunkenPlazaData
    {
        public static readonly int[] Ropes = { // min x, y, z, max x, y, z
            -4024, 744, 272, -3864, 904, 1088,
            -6528, 2016, 256, -6336, 2208, 1056,
            -1848, 4928, 446, -1656, 5120, 1351,
        };
        // Outline (Deadlock units, x/y), from the yellow and blue lane ziplines (tools/mapstage/selpoly.py), widened by the
        // user 2026-10-13: the street outside the yellow-side base approach, the whole NYSE block, the strip west of the yellow lane; south edge on the railing over the trench;
        // then redrawn by the user (west and north to the map's natural edge, no kink at the Bank).
        public static readonly float[] Outline = { -9508, 704, -9508, 1875, -9388, 3057, -9439, 3197, -9388, 3301, -9422, 3927, -9009, 4031, -8854, 4796, -8441, 4935, -8406, 5979, -7976, 6187, -7149, 6222, -6443, 6535, -6340, 6674, -5789, 6709, -5479, 6918, -4859, 7126, -4773, 7231, -4670, 7196, -4188, 7103, -3964, 7103, -3499, 7578, -3034, 7648, -2311, 7891, -1571, 7926, -865, 7752, -779, 6709, -830, 6639, -796, 5074, -727, 5040, -555, 5144, -314, 5144, 380, 5364, 392, 5109, 581, 5109, 847, 4898, 771, 4796, 788, 3892, 702, 3683, 375, 3475, -193, 3266, -486, 2953, -434, 1075, -469, 658, -589, 414, -1123, 484, -4033, 484, -6220, 414, -6650, 449, -7907, 693, -9095, 797 };
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
        /// <summary>Hidden by hand in-game (cac_map_mark newt): x, y, z, yaw in section space. Candidates: one is picked per stage.</summary>
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

    /// <summary>Haunted Lot: the Hidden King side west of the blue lane, around the yellow lane's southern Walker. Traced from the user's drawing (2026-10-14); data by tools/mapstage/section_consts.py.</summary>
    internal static class HauntedLotData
    {
        public static readonly float[] NewtAltars = { // x, y, z, yaw in the section's Unity space (cac_map_mark, 2026-10-14)
            -33.16f, 13.42f, -39.67f, -90.90f,
            -78.27f, 22.97f, 22.08f, 91.80f,
        };
        public static readonly float[] PlayerSpawns = { // x, y, z, yaw in the section's Unity space (cac_map_mark, 2026-10-14)
            6.40f, 47.97f, -19.10f, 0.00f,
            -38.75f, 6.31f, -88.05f, 1.90f,
        };
        public static readonly int[] Ropes = { // min x, y, z, max x, y, z
            -832, -1184, -384, -672, -1024, 732,
            -4024, -640, 272, -3864, -480, 1088,
            -4752, -3104, 400, -4560, -2912, 1344,
            -4192, -3600, 384, -4000, -3408, 1344,
            -4664, -1160, 256, -4472, -968, 1200,
            -3292, -2240, 270, -3100, -2112, 1030,
            -7344, -2512, 271, -7152, -2320, 1152,
        };
        public static readonly float[] Outline = {-9055, -157, -8268, -118, -7835, -197, -7717, -354, -7618, -236, -6004, -236, -4547, -276, -4331, -354, -925, -354, -472, -433, -433, -2283, -965, -2717, -1398, -2835, -1909, -2795, -2106, -2992, -1890, -3268, -1811, -3622, -1811, -4882, -2008, -5276, -2795, -5315, -2953, -5472, -3051, -6220, -3248, -6378, -3189, -7323, -4567, -7323, -5335, -6929, -5728, -6575, -6181, -6339, -6693, -5787, -6791, -5787, -7559, -4724, -7953, -4331, -8366, -4331, -8701, -4213, -8957, -3622, -9055, -2953 };
        public static readonly int[] VentTunnels = { // min x, y, z, max x, y, z
            -832, -1696, -640, 1280, -1408, -192,
            -832, -1952, -640, 192, -1696, -192,
            -640, -1408, -640, -288, -1024, -464,
            -1472, -1920, -672, -864, -1472, -160,
            -2272, -1920, -512, -1472, -992, -160,
            -3328, -1280, -512, -2272, -992, -160,
            -6176, -1792, -128, -5472, -1152, 192,
            -6400, -2400, -64, -5984, -1792, 192,
            -6752, -2400, -64, -6400, -2112, 224,
            -7072, -2416, 256, -6944, -2144, 384,
            -2464, -992, -512, -2272, -672, -32,
            -2464, -672, -384, -2080, -336, -32,
            -2080, -544, -320, -1824, -336, 192,
            -1824, -544, -160, -1568, -368, 192,
            -1568, -544, -32, -1424, -368, 224,
            -1856, -3744, 32, -1216, -3328, 256,
            -2752, -3744, 32, -1856, -3552, 240,
            -3712, -4288, 32, -2752, -3552, 240,
            -4584, -4288, 32, -3712, -4096, 336,
            -4736, -4288, 176, -4584, -4096, 480,
            -1632, -2720, 120, -1472, -2208, 336,
            -4008, -1280, -384, -3328, -992, 32,
            -3776, -1824, -128, -3648, -1376, 0,
            -5472, -1728, -128, -4864, -1408, 192,
            -1632, -3104, -8, -1472, -2720, 336,
            -6944, -2416, 32, -6752, -2144, 384,
            -4768, -4288, 384, -4736, -4096, 480,
            -1216, -1696, 112, -256, -1312, 368,
            -256, -1696, 112, 640, -1472, 320,
            -1312, -1504, 256, -1216, -1312, 368,
        };
        public static readonly int[] VentOpenings = { // min x, y, z, max x, y, z
            -392, -1024, -640, -312, -928, -576,
            -7160, -2416, 256, -7080, -2320, 320,
            -3752, -1920, -128, -3672, -1824, -64,
            -1416, -496, 64, -1336, -400, 128,
            -1592, -2208, 128, -1512, -2112, 192,
            -4856, -4240, 384, -4776, -4144, 448,
            -4096, -1120, -128, -4016, -1024, -64,
            -4968, -1408, -64, -4888, -1312, 0,
            -3752, -1376, -128, -3672, -1280, -64,
            -1400, -1456, 256, -1320, -1360, 320,
        };
        public static readonly int[] Shops = { // min x, y, z, max x, y, z
            -9000, -2088, 248, -8684, -1816, 376,
        };
        public static readonly int[] SubwayTeleports = { // min x, y, z, max x, y, z
            -3640, -2840, -128, -3528, -2728, 0,
        };
        // Deadlock's neutral trooper camps and spawns and item crates (x, y, z units): where interactables may stand.
        public static readonly int[] InteractableSpots = {
            -7296, -1280, 256, -7168, -1408, 256, -7168, -1280, 264, -7040, -1280, 256,
            -6016, -1184, 256, -5928, -2296, 1088, -5920, -1184, 264, -5824, -2560, 1088,
            -5824, -2400, 1088, -5824, -1472, -128, -5824, -1280, -128, -5720, -2296, 1088,
            -5632, -1376, -128, -5584, -3776, 384, -5504, -3680, 392, -5504, -3552, 384,
            -5376, -3680, 384, -5224, -672, -128, -5120, -576, -128, -5092, -1792, 256,
            -5024, -832, -128, -5024, -680, -128, -4916, -1792, 264, -4896, -576, -128,
            -4832, -1936, 256, -4832, -1664, 256, -4800, -1248, -64, -4736, -688, -128,
            -4592, -3760, 384, -4480, -3840, 384, -4352, -3744, 384, -4336, -3936, 384,
            -4000, -1856, 248, -4000, -1696, 248, -4000, -1536, 248, -3872, -1696, 248,
            -3712, -4384, 384, -3680, -3072, 384, -3520, -4576, 384, -3520, -4384, 384,
            -3440, -1260, 256, -3424, -1728, 568, -3384, -1448, 564, -3328, -4288, 384,
            -3318, -1260, 256, -3318, -1148, 256, -3264, -1600, 564, -3186, -3961, 64,
            -3172, -1260, 256, -3138, -1524, 562, -3088, -3824, 64, -2985, -3751, 64,
            -2464, -1088, -448, -2360, -962, -448, -2240, -1090, -448, -2096, -928, 256,
            -2096, -776, 256, -1920, -864, 256, -1792, -944, 256, -1792, -776, 256,
        };
    }

    /// <summary>Umbria University: the Archmother side east of the blue lane, around the purple lane's northern Walker. Traced from the user's drawing (2026-10-14); data by tools/mapstage/section_consts.py.</summary>
    internal static class UmbriaData
    {
        public static readonly float[] NewtAltars = { // x, y, z, yaw in the section's Unity space (cac_map_mark, 2026-10-14)
            87.79f, 23.23f, -36.62f, -88.80f,
            72.02f, 23.46f, -8.21f, -89.30f,
            57.74f, 17.89f, -112.69f, -43.90f,
        };
        public static readonly float[] PlayerSpawns = { // x, y, z, yaw in the section's Unity space (cac_map_mark, 2026-10-14)
            23.32f, 31.51f, 6.50f, 95.40f,
            34.66f, 9.18f, -23.95f, 105.30f,
        };
        public static readonly int[] Ropes = { // min x, y, z, max x, y, z
            672, 1024, -384, 832, 1184, 728,
            6704, 2432, 248, 6896, 2624, 1152,
            3864, 480, 320, 4024, 640, 1088,
            4336, 2784, -120, 4464, 2912, 1714,
        };
        public static readonly float[] Outline = {669, 433, 650, 2402, 1083, 2717, 1280, 2717, 1654, 2913, 1929, 3189, 1949, 5039, 3071, 5118, 3425, 5394, 3445, 7441, 4311, 7362, 4764, 6969, 5138, 7008, 5413, 6850, 6378, 6732, 7323, 6299, 7776, 6260, 7815, 6181, 9252, 6142, 9311, 3701, 9134, 3543, 8878, 3543, 8600, 3346, 8600, 512, 8560, 394, 7480, 394, 7067, 512, 5276, 472, 5177, 394 };
        public static readonly int[] VentTunnels = { // min x, y, z, max x, y, z
            -1328, 1312, -640, 832, 1696, -184,
            296, 1024, -640, 640, 1312, -480,
            416, 1312, 128, 704, 1696, 366,
            896, 1312, 238, 1472, 1696, 366,
            320, 1312, 128, 512, 1536, 320,
            960, 1152, -416, 1568, 1440, 0,
            1316, 336, -384, 2580, 720, 112,
            1984, 1456, -640, 2176, 1648, -192,
            2176, 752, -640, 3232, 1648, -192,
            832, 1456, -640, 1984, 1648, -448,
            4184, 3976, 176, 4376, 4104, 576,
            3352, 4040, 64, 4184, 4232, 336,
            2896, 3616, 64, 3712, 4272, 240,
            1212, 3328, 32, 1860, 3748, 256,
            1440, 2788, 24, 1600, 3904, 288,
            1864, 3552, 64, 2756, 3744, 224,
            4040, 3976, 176, 4168, 4104, 336,
            4280, 3976, 448, 4376, 4104, 576,
        };
        public static readonly int[] VentOpenings = { // min x, y, z, max x, y, z
            1336, 400, 64, 1416, 496, 128,
            1320, 1360, 256, 1400, 1456, 320,
            3856, 1208, -128, 3936, 1304, -64,
            4696, 4088, 448, 4776, 4184, 512,
            1512, 2112, 128, 1592, 2208, 192,
            5768, 2608, 256, 5848, 2704, 320,
        };
        public static readonly int[] Shops = { // min x, y, z, max x, y, z
            8664, 1800, 248, 8980, 2072, 376,
        };
        public static readonly int[] SubwayTeleports = { // min x, y, z, max x, y, z
            3528, 2728, -128, 3640, 2840, 0,
        };
        // Deadlock's neutral trooper camps and spawns and item crates (x, y, z units): where interactables may stand.
        public static readonly int[] InteractableSpots = {
            1568, 1536, -640, 1632, 1584, -640, 1696, 1536, -640, 1792, 776, 256,
            1792, 944, 256, 1952, 832, 256, 2096, 760, 256, 2096, 944, 256,
            2496, 544, -384, 2544, 480, -384, 2985, 3751, 64, 3088, 3824, 64,
            3186, 3961, 64, 3232, 3840, 944, 3328, 1280, -384, 3328, 3712, 944,
            3355, 3514, 944, 3392, 1200, -384, 3456, 1280, -384, 3456, 3808, 944,
            3584, 1856, 256, 3616, 3608, 448, 3776, 1856, 256, 3776, 2048, 256,
            3808, 3704, 448, 3808, 3864, 448, 3968, 1856, 256, 4000, 3008, 1008,
            4000, 3608, 448, 4032, 1024, -128, 4096, 896, -128, 4160, 2624, 1008,
            4160, 2784, 1008, 4288, 896, -128, 4288, 1024, -128, 4288, 1152, -128,
            4320, 3008, 1008, 4368, 1856, 256, 4480, 1152, -128, 4544, 1024, -128,
            4544, 1856, 256, 4544, 2016, 256, 4576, 3792, 944, 4704, 3568, 944,
            4720, 1856, 256, 4736, 3728, 944, 4864, 3824, 944, 5696, 1600, 270,
            5748, 3884, 384, 5824, 3984, 384, 5863, 4110, 384, 5912, 1348, 264,
            5960, 3960, 384, 6008, 1348, 256, 6152, 2212, 1000, 6176, 964, 256,
            6304, 836, 256, 6304, 964, 264, 6304, 2208, 1000, 6404, 2064, 1000,
            6432, 964, 256, 6432, 2372, 1000,
        };
    }

    /// <summary>Chinatown: the Hidden King side east of the blue lane, around the purple lane's southern Walker. Traced from the user's drawing (2026-10-14); data by tools/mapstage/section_consts.py.</summary>
    internal static class ChinatownData
    {
        public static readonly float[] NewtAltars = { // x, y, z, yaw in the section's Unity space (cac_map_mark, 2026-10-14)
            -38.51f, 37.40f, 41.84f, -90.10f,
            -102.11f, 21.14f, 3.49f, 88.70f,
            -95.27f, 3.26f, 105.09f, -45.50f,
        };
        public static readonly float[] PlayerSpawns = { // x, y, z, yaw in the section's Unity space (cac_map_mark, 2026-10-14)
            -53.49f, 6.31f, 11.54f, 89.50f,
        };
        public static readonly int[] Ropes = { // min x, y, z, max x, y, z
            6336, -2400, 256, 6528, -2208, 1056,
            3864, -904, 272, 4024, -744, 1130,
            3008, -1968, 400, 3168, -1808, 2630,
        };
        public static readonly float[] Outline = {-217, -5354, -200, -4520, 1076, -4520, 1076, -3230, 480, -3230, 531, -472, 610, -433, 2480, -433, 3720, -591, 9114, -591, 9016, -1654, 9016, -3346, 8740, -5079, 8386, -5276, 7126, -5276, 6949, -5472, 6890, -6142, 6732, -6260, 5315, -6260, 5118, -6457, 5157, -7795, 5079, -7874, 1201, -7795, 1083, -7441, 925, -7283, 610, -7244, 492, -7087, 433, -5630, 276, -5472 };
        public static readonly int[] VentTunnels = { // min x, y, z, max x, y, z
            -832, -1696, -640, 1280, -1408, -192,
            256, -1408, -640, 384, -1296, -448,
            -1200, -3712, 48, -256, -3584, 344,
            528, -3520, 176, 656, -3392, 480,
            256, -3520, 176, 528, -3392, 344,
            -256, -3712, 176, 256, -3392, 344,
            656, -3520, 384, 784, -3392, 480,
            2528, -1376, 112, 2720, -1184, 368,
            1024, -1696, 112, 1280, -1152, 336,
            -256, -1696, 112, 640, -1472, 320,
            640, -1696, 112, 1024, -1312, 336,
            1664, -1376, 112, 2528, -1184, 336,
            1280, -1408, 112, 1664, -1152, 336,
            2720, -1376, 112, 2848, -1184, 368,
            4800, -3040, 0, 4992, -2912, 416,
            4672, -3040, 0, 4800, -2912, 192,
            4416, -3040, -144, 4672, -2784, 192,
            4416, -2784, -144, 4544, -2080, 192,
            4416, -2080, -144, 4544, -1568, 48,
            2656, -1248, -256, 2976, -1024, 48,
            2208, -1024, -672, 2816, -324, -16,
            1312, -1632, -672, 2816, -1472, -16,
            1536, -576, -672, 2208, -324, -288,
            1056, -448, -672, 1536, -324, -544,
            2624, -1472, -672, 2816, -1248, -16,
            4992, -3040, 256, 5108, -2912, 416,
        };
        public static readonly int[] VentOpenings = { // min x, y, z, max x, y, z
            968, -432, -640, 1048, -336, -576,
            792, -3504, 384, 872, -3408, 448,
            2856, -1328, 256, 2936, -1232, 320,
            2984, -1200, -144, 3064, -1104, -80,
            4440, -1568, -144, 4520, -1472, -80,
            5116, -3024, 256, 5196, -2928, 320,
            5208, -2048, 256, 5288, -1952, 320,
        };
        public static readonly int[] Shops = { // min x, y, z, max x, y, z
            856, -1392, 376, 1172, -1120, 504,
            6264, -1904, 248, 6580, -1632, 376,
            3752, -1264, -144, 4068, -992, -16,
        };
        public static readonly int[] SubwayTeleports = { // min x, y, z, max x, y, z
            3848, -1848, -144, 3960, -1736, -16,
        };
        // Deadlock's neutral trooper camps and spawns and item crates (x, y, z units): where interactables may stand.
        public static readonly int[] InteractableSpots = {
            560, -1584, -640, 640, -1472, -640, 720, -1584, -640, 1305, -1120, 384,
            1312, -896, 384, 1408, -1536, -640, 1408, -992, 384, 1472, -1584, -640,
            1504, -896, 384, 1536, -1536, -640, 1792, -3616, 406, 1920, -3488, 406,
            1920, -3360, 406, 2048, -3616, 406, 2080, -1056, 384, 2080, -864, 384,
            2208, -960, 400, 2264, -2792, 376, 2368, -960, 392, 2392, -2792, 376,
            2488, -2936, 376, 2488, -2664, 376, 2688, -1120, -256, 2736, -1184, -256,
            2736, -1056, -256, 2752, -1888, 2496, 3008, -3984, 725, 3136, -3888, 725,
            3136, -3728, 725, 3248, -1888, 2499, 3264, -3984, 725, 3440, -2048, 2496,
            3440, -1739, 2496, 3744, -4128, 376, 3872, -4000, 376, 4000, -4128, 376,
            4000, -3872, 376, 4128, -2240, 256, 4288, -2688, 271, 4352, -1824, 256,
            4448, -1856, -144, 4480, -2080, 256, 4480, -1920, 264, 4496, -1920, -144,
            4496, -1792, -144, 4640, -1920, 256, 4800, -1696, 256, 5504, -1536, 928,
            5696, -1920, 928, 5696, -1728, 944, 5760, -1136, 256, 5792, -1248, 264,
            5888, -1536, 928, 7792, -2408, 256, 7816, -2152, 256, 7840, -4640, 376,
            7872, -4864, 376, 7936, -2288, 257, 7944, -2472, 258, 8000, -4736, 376,
            8080, -2400, 256, 8080, -2144, 256, 8144, -4616, 376, 8152, -4840, 376,
            8184, -4736, 376,
        };
    }
}
