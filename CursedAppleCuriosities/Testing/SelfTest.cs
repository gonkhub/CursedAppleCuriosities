using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CursedAppleCuriosities.Testing
{
    /// <summary>
    /// Scripted playtest driver for development. Off unless the game is launched with <c>--cac-selftest &lt;script&gt;</c>, so
    /// players never run it. The script is a text file, one command per line (# comments):
    /// <code>
    /// run CursedAppleViscousBody      start a singleplayer run as this body (from the title screen)
    /// wait 5                          seconds
    /// cmd god 1                       any console command (DebugToolkit's too)
    /// hold primary 2                  hold a button for N seconds (primary secondary utility special jump sprint equipment interact)
    /// tap special                     press and release a button
    /// press utility / release utility hold until released
    /// move 0 1 [seconds]              move (x, forward), relative to the camera; "move 0 0" stops
    /// aim enemy | aim 0 -10 | aim off look at the nearest enemy each frame, at yaw/pitch from the body's facing, or let go
    /// slide 1 / slide 0               hold the Deadlock slide key
    /// shot name                       save a screenshot of the screen
    /// portrait name yaw [zoom] [h]    render the player's body from yaw degrees (0 = its front) to name.png; h = aim height (0..1)
    /// setskill special CursedAppleAbramsSeismic   put a skill in a slot of the live body only (the saved loadout is untouched)
    /// probe                           log the body's foot, main hurtbox bounds and Body state
    /// log text                        write a line to the BepInEx log
    /// quit
    /// </code>
    /// Screenshots go to the script's folder. Every line is logged as "[SelfTest]".
    /// </summary>
    public class SelfTest : MonoBehaviour
    {
        public static bool Active { get; private set; }
        /// <summary>Held by the script: the Deadlock slide key.</summary>
        public static bool SlideHeld;

        private static readonly Dictionary<string, bool> Held = new Dictionary<string, bool>();
        private static Vector2 _move;
        private static string _aim = "off";
        private static float _aimYaw, _aimPitch;

        private string _script = "", _outDir = "";
        private static float _launchClock;

        public static void TryStart(GameObject host)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--cac-selftest");
            if (i < 0 || i + 1 >= args.Length) return;
            Active = true;
            _launchClock = Time.realtimeSinceStartup;
            var t = host.AddComponent<SelfTest>();
            t._script = args[i + 1];
            t._outDir = Path.GetDirectoryName(t._script) ?? ".";
            Log($"script {t._script}");

            On.RoR2.PlayerCharacterMasterController.PollButtonInput += (orig, self) =>
            {
                orig(self);
                InputBankTest? bank = self.master != null && self.master.GetBody() != null ? self.master.GetBody().inputBank : null;
                if (bank == null) return;
                Override(ref bank.skill1, "primary");
                Override(ref bank.skill2, "secondary");
                Override(ref bank.skill3, "utility");
                Override(ref bank.skill4, "special");
                Override(ref bank.jump, "jump");
                Override(ref bank.sprint, "sprint");
                Override(ref bank.activateEquipment, "equipment");
                Override(ref bank.interact, "interact");
            };
            On.RoR2.PlayerCharacterMasterController.Update += (orig, self) =>
            {
                orig(self);
                InputBankTest? bank = self.master != null && self.master.GetBody() != null ? self.master.GetBody().inputBank : null;
                if (bank == null) return;
                CharacterBody me = self.master.GetBody();
                CameraRigController? rig = self.networkUser != null && self.networkUser.localUser != null ? self.networkUser.localUser.cameraRigController : null;
                float yaw = rig != null ? rig.transform.eulerAngles.y : me.transform.eulerAngles.y;
                if (_move != Vector2.zero) bank.moveVector = Quaternion.Euler(0f, yaw, 0f) * new Vector3(_move.x, 0f, _move.y).normalized;
                Vector3? dir = AimDirection(me, bank);
                if (dir != null) bank.aimDirection = dir.Value;
            };
        }

        private static void Override(ref InputBankTest.ButtonState b, string key)
        {
            if (!Held.TryGetValue(key, out bool down) || !down) return;
            bool justPressed = !b.wasDown;
            b.down = true;
            if (justPressed) b.lastPressedTimeStamp = Run.FixedTimeStamp.now;
        }

        private static Vector3? AimDirection(CharacterBody body, InputBankTest bank)
        {
            switch (_aim)
            {
                case "enemy":
                    CharacterBody? e = NearestEnemy(body);
                    return e != null ? (e.corePosition - bank.aimOrigin).normalized : (Vector3?)null;
                case "angle":
                    return Quaternion.Euler(-_aimPitch, body.transform.eulerAngles.y + _aimYaw, 0f) * Vector3.forward;
                default:
                    return null;
            }
        }

        private static CharacterBody? NearestEnemy(CharacterBody me)
        {
            CharacterBody? best = null;
            float bestD = float.MaxValue;
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
            {
                if (b == null || b == me || b.teamComponent == null || b.teamComponent.teamIndex == me.teamComponent.teamIndex) continue;
                if (b.healthComponent == null || !b.healthComponent.alive) continue;
                float d = (b.corePosition - me.corePosition).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        internal static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[SelfTest] " + s);

        private float _nextTick;
        private bool _atTitle;

        /// <summary>Load clock: the time every 2 s and every frame that stalls, until the title screen (for load-time work).</summary>
        private void Update()
        {
            if (_atTitle) return;
            float now = Time.realtimeSinceStartup;
            if (Time.unscaledDeltaTime > 0.3f) Log($"[clock] {now:0.0}s: a {Time.unscaledDeltaTime:0.00}s frame");
            if (now >= _nextTick) { _nextTick = now + 2f; Log($"[clock] {now:0.0}s"); }
        }

        private IEnumerator Start()
        {
            string[] lines;
            try { lines = File.ReadAllLines(_script); }
            catch (Exception e) { Log("can't read script: " + e.Message); yield break; }

            // The title screen is the starting point for every script.
            while (SceneManager.GetActiveScene().name != "title" || RoR2.Console.instance == null) yield return null;
            _atTitle = true;
            Log($"title screen after {Time.realtimeSinceStartup - _launchClock:0.0}s since plugin load ({Time.realtimeSinceStartup:0.0}s since launch)");
            yield return new WaitForSeconds(1f);

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Log("> " + line);
                IEnumerator? step = null;
                try { step = Exec(line); }
                catch (Exception e) { Log($"! {line}: {e}"); }
                if (step == null) continue;
                while (true)
                {
                    object? cur;
                    try
                    {
                        if (!step.MoveNext()) break;
                        cur = step.Current;
                    }
                    catch (Exception e) { Log($"! {line}: {e}"); break; }
                    yield return cur;
                }
            }
            Log("script done");
        }

        private static float F(string[] p, int i, float fallback) =>
            p.Length > i && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        private IEnumerator? Exec(string line)
        {
            string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string rest = line.Length > p[0].Length ? line.Substring(p[0].Length).Trim() : "";
            switch (p[0].ToLowerInvariant())
            {
                case "run": return StartRun(p[1]);
                case "wait": return Wait(F(p, 1, 1f));
                case "cmd":
                    RoR2.Console.instance.SubmitCmd(LocalUserManager.GetFirstLocalUser()?.currentNetworkUser, rest);
                    return null;
                case "hold": return Hold(p[1], F(p, 2, 1f));
                case "tap": return Hold(p[1], 0.1f);
                case "press": Held[p[1]] = true; return null;
                case "release": Held[p[1]] = false; return null;
                case "move":
                    _move = new Vector2(F(p, 1, 0f), F(p, 2, 0f));
                    return p.Length > 3 ? StopMoveAfter(F(p, 3, 1f)) : null;
                case "aim":
                    if (p[1] == "enemy" || p[1] == "off") _aim = p[1];
                    else { _aim = "angle"; _aimYaw = F(p, 1, 0f); _aimPitch = F(p, 2, 0f); }
                    return null;
                case "slide": SlideHeld = F(p, 1, 1f) > 0f; return null;
                case "shot":
                    return Screen(p[1]);
                case "portrait": return Portrait(p[1], F(p, 2, 0f), F(p, 3, 1f), F(p, 4, 0.5f));
                case "setskill": SetSkill(p[1], p[2]); return null;
                case "probe":
                {
                    CharacterBody? b = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                    Collider? c = b != null && b.mainHurtBox != null ? b.mainHurtBox.collider : null;
                    Log(b == null ? "probe: no body" : $"probe: foot {b.footPosition} hurtbox {(c != null ? c.bounds.center + " size " + c.bounds.size : "none")} state {EntityStateMachine.FindByCustomName(b.gameObject, "Body")?.state?.GetType().Name}");
                    return null;
                }
                case "log": Log(rest); return null;
                case "quit": Application.Quit(); return null;
                default: Log("unknown command: " + p[0]); return null;
            }
        }

        private IEnumerator Screen(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(UnityEngine.Screen.width, UnityEngine.Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        private static void SetSkill(string slot, string skillName)
        {
            CharacterBody? body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (body == null || body.skillLocator == null) { Log("setskill: no body"); return; }
            GenericSkill? gs = slot switch
            {
                "primary" => body.skillLocator.primary,
                "secondary" => body.skillLocator.secondary,
                "utility" => body.skillLocator.utility,
                "special" => body.skillLocator.special,
                _ => null,
            };
            var def = RoR2.Skills.SkillCatalog.GetSkillDef(RoR2.Skills.SkillCatalog.FindSkillIndexByName(skillName));
            if (gs == null || def == null) { Log($"setskill: no slot {slot} or skill {skillName}"); return; }
            gs.SetBaseSkill(def);
            Log($"setskill: {slot} = {skillName}");
        }

        private static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

        private static IEnumerator StopMoveAfter(float s) { yield return new WaitForSeconds(s); _move = Vector2.zero; }

        private static IEnumerator Hold(string key, float s)
        {
            Held[key] = true;
            yield return new WaitForSeconds(s);
            Held[key] = false;
            yield return new WaitForFixedUpdate();
        }

        private static IEnumerator StartRun(string bodyName)
        {
            RoR2.Console.instance.SubmitCmd(null, "transition_command \"gamemode ClassicRun; host 0;\"");
            float t0 = Time.realtimeSinceStartup;
            while (PreGameController.instance == null) yield return null;
            yield return new WaitForSeconds(1.5f);
            NetworkUser? user = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            BodyIndex body = BodyCatalog.FindBodyIndex(bodyName);
            Log($"lobby after {Time.realtimeSinceStartup - t0:0.0}s; body {bodyName} = {body}");
            if (user != null && body != BodyIndex.None)
            {
                SurvivorDef? sv = SurvivorCatalog.FindSurvivorDefFromBody(BodyCatalog.GetBodyPrefab(body));
                if (sv != null) user.CmdSetBodyPreference(body);
            }
            yield return new WaitForSeconds(3f); // the character select display and its intro
            RoR2.Console.instance.SubmitCmd(user, "pregame_start_run");
            t0 = Time.realtimeSinceStartup;
            CharacterBody? b = null;
            while ((b = LocalUserManager.GetFirstLocalUser()?.cachedBody) == null) yield return null;
            Log($"body {b.name} after {Time.realtimeSinceStartup - t0:0.0}s in {SceneManager.GetActiveScene().name}");
            // Out of the drop pod: press interact until the body is free (the pod opens a few seconds in).
            for (float t = 0f; t < 30f; t += 0.5f)
            {
                b = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                if (b != null && b.currentVehicle == null && t > 1f) break;
                Held["interact"] = !Held.TryGetValue("interact", out bool d) || !d;
                yield return new WaitForSeconds(0.5f);
            }
            Held["interact"] = false;
            Log($"out of the pod: {(b != null && b.currentVehicle == null)}");
        }

        /// <summary>Renders the local player's body from a camera of our own (no HUD) to a PNG.</summary>
        private IEnumerator Portrait(string name, float yaw, float zoom, float height)
        {
            yield return new WaitForEndOfFrame();
            CharacterBody? body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            Camera? main = Camera.main;
            if (body == null || main == null) { Log("portrait: no body or camera"); yield break; }
            Transform? model = body.modelLocator != null ? body.modelLocator.modelTransform : null;
            Bounds bounds = new Bounds(body.corePosition, Vector3.one);
            if (model != null)
            {
                bool first = true;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (first) { bounds = r.bounds; first = false; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            float tall = Mathf.Clamp(bounds.size.y, 0.5f, 6f);
            Vector3 focus = new Vector3(body.transform.position.x, bounds.min.y + tall * height, body.transform.position.z);
            Vector3 facing = model != null ? model.forward : body.transform.forward;
            facing.y = 0f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * facing.normalized;

            var rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("CursedAppleSelfTestCamera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.fieldOfView = 30f;
            cam.targetTexture = rt;
            cam.nearClipPlane = 0.05f;
            float dist = tall * 0.6f / Mathf.Tan(15f * Mathf.Deg2Rad) / Mathf.Max(0.1f, zoom);
            go.transform.position = focus + dir * dist;
            go.transform.LookAt(focus);
            // Only the body: its renderers go on a spare layer for this one render, over a flat background.
            const int Layer = 31;
            var restore = new List<(GameObject go, int layer)>();
            if (model != null)
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    restore.Add((r.gameObject, r.gameObject.layer));
                    r.gameObject.layer = Layer;
                }
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
            try
            {
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
            }
            finally
            {
                foreach (var (o, l) in restore) if (o != null) o.layer = l;
                RenderTexture.active = null;
                Destroy(go);
                rt.Release();
                Destroy(rt);
            }
        }
    }
}
