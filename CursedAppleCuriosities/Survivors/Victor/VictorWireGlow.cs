using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Every client, on Victor: the barbed wire on his left arm glows green while Pain Battery is full (user), as in
    /// Deadlock. There, Pain Battery's m_FullyChargedParticle (frank_shock_target_ready_buff) runs a child that reads the
    /// particle snapshot barbed_wire_l_fx_model.vsnap (76 points along the wire, each rigidly bound to one arm/hand bone),
    /// skins them to the bones every frame and draws them as a rope, colour (157, 203, 114). Here: the same points, read
    /// from the player's own install (a cached text dump, never shipped), bound to the same bones, drawn as glowing lines.
    /// </summary>
    public class VictorWireGlow : MonoBehaviour
    {
        public const string Snapshot = "particles/models/heroes_wip/frank/frank/barbed_wire_l_fx_model.vsnap_c";
        public static readonly Color Green = new Color(157f / 255f, 203f / 255f, 114f / 255f);
        private const float Inch = 0.0254f;
        private const float Break = 0.3f;       // metres between neighbours that start a new strand (rest pose; the wire is one
                                                // coil, points ~7 cm apart, up to 18 cm on the wraps: one strand, as Deadlock's rope)
        private const float Width = 0.035f;     // metres, before the hero's scale

        private static List<(Vector3 Pos, string Bone)>? _points;
        private static bool _parsed;
        private static Material? _material;

        private CharacterBody _body = null!;
        private GameObject? _hero;
        private readonly List<(Transform Bone, Vector3 Local)> _bound = new List<(Transform, Vector3)>();
        private readonly List<(LineRenderer Line, int From, int Count)> _strands = new List<(LineRenderer, int, int)>();
        private float _nextTry;

        private void Awake() => _body = GetComponent<CharacterBody>();

        /// <summary>The snapshot's points in the converted model's root space (Deadlock x, y, z inches -> glTF (y, z, x) m -> Unity -x).</summary>
        private static List<(Vector3, string)>? Points()
        {
            if (_parsed) return _points;
            string? text = DeadlockInstall.Dump(Snapshot);
            if (text == null) return null;
            _parsed = true;
            var pos = new List<Vector3>();
            var bones = new List<string>();
            int a = text.IndexOf("- Attribute position", System.StringComparison.Ordinal);
            int k = text.IndexOf("- Attribute skinning", System.StringComparison.Ordinal);
            if (a < 0 || k < 0) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Victor] wire glow: snapshot dump has no points"); return null; }
            string posText = text.Substring(a, (text.IndexOf("- Attribute", a + 10, System.StringComparison.Ordinal) is int e && e > a ? e : text.Length) - a);
            foreach (Match m in Regex.Matches(posText, @"<(-?[\d.eE+-]+), (-?[\d.eE+-]+), (-?[\d.eE+-]+)>"))
            {
                float x = F(m.Groups[1].Value), y = F(m.Groups[2].Value), z = F(m.Groups[3].Value);
                pos.Add(new Vector3(-y * Inch, z * Inch, x * Inch));
            }
            foreach (Match m in Regex.Matches(text.Substring(k), @"^\((\w+): [\d.]+\)", RegexOptions.Multiline)) bones.Add(m.Groups[1].Value);
            int n = Mathf.Min(pos.Count, bones.Count);
            _points = new List<(Vector3, string)>(n);
            for (int i = 0; i < n; i++) _points.Add((pos[i], bones[i]));
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Victor] wire glow: {n} points");
            return _points;
        }

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static Material? Mat()
        {
            if (_material != null) return _material;
            Material? beam = DeadlockSurvivorKit.TryLoad<Material>("Victor", "RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            if (beam == null) return null;
            _material = new Material(beam) { name = "matCursedAppleVictorWireGlow" };
            if (_material.HasProperty("_TintColor")) _material.SetColor("_TintColor", new Color(Green.r, Green.g, Green.b, 1f));
            if (_material.HasProperty("_Boost")) _material.SetFloat("_Boost", Mathf.Max(2f, _material.GetFloat("_Boost")));
            return _material;
        }

        private void LateUpdate()
        {
            bool full = VictorSurvivor.BatteryBuff != null && _body.GetBuffCount(VictorSurvivor.BatteryBuff) >= VictorData.BatteryFull
                && _body.healthComponent != null && _body.healthComponent.alive;
            if (full && _strands.Count == 0 && Time.unscaledTime >= _nextTry) { _nextTry = Time.unscaledTime + 2f; Build(); }
            bool show = full && _hero != null && _hero.activeInHierarchy && !VictorReassembly.IsHidden(_hero); // hidden with his model (Reanimation's parts, cloak)
            foreach (var s in _strands)
            {
                if (s.Line.enabled != show) s.Line.enabled = show;
                if (!show) continue;
                float pulse = 1f + 0.25f * Mathf.Sin(Time.time * 9f); // a live current, not a steady tube
                s.Line.widthMultiplier = Width * _hero!.transform.lossyScale.y * pulse;
                for (int i = 0; i < s.Count; i++)
                {
                    var (bone, local) = _bound[s.From + i];
                    s.Line.SetPosition(i, bone.TransformPoint(local));
                }
            }
        }

        private void Build()
        {
            var swap = GetComponentInChildren<DeadlockModelSwap>(true);
            _hero = swap != null ? swap.HeroObject : null;
            List<(Vector3, string)>? points = Points();
            GlbModel? template = DeadlockInstall.Model(VictorData.Hero, (name, tex) => DeadlockHeroes.MakeMaterial(VictorModel.Def.DisplayName, name, tex));
            Material? mat = Mat();
            if (_hero == null || points == null || template == null || mat == null) return;

            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in _hero.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            _bound.Clear();
            var rest = new List<Vector3>();
            foreach (var (p, boneName) in points)
            {
                if (!bones.TryGetValue(boneName, out Transform bone) || !template.RestInRoot.TryGetValue(boneName, out Matrix4x4 restM)) continue;
                _bound.Add((bone, restM.inverse.MultiplyPoint3x4(p)));
                rest.Add(p);
            }
            // The rope runs through the points in order; a big jump starts another strand (the wire wraps the arm in loops).
            int from = 0;
            for (int i = 1; i <= _bound.Count; i++)
            {
                if (i < _bound.Count && Vector3.Distance(rest[i], rest[i - 1]) <= Break) continue;
                if (i - from >= 2) _strands.Add((NewLine(mat, i - from), from, i - from));
                from = i;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Victor] wire glow: {_bound.Count} points bound, {_strands.Count} strands");
        }

        private LineRenderer NewLine(Material mat, int count)
        {
            var go = new GameObject("VictorWireGlow");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.positionCount = count;
            lr.numCapVertices = 2;
            lr.startColor = lr.endColor = Green;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }
    }
}
