using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Victor's character-select entrance (user: Shocking Reanimation): his body parts float together on the pedestal
    /// (Deadlock's reanimation model, <see cref="VictorReassembly"/>), then he gets up. No lightning strikes (user).
    /// Until that model is converted, he lies on the pedestal in his knockdown pose instead.
    /// </summary>
    public class VictorDisplayIntro : DeadlockDisplayIntro
    {
        private const float Down = 3f;    // seconds of reassembly before he gets up (as long as the channel in a run)

        protected override IEnumerator Run()
        {
            yield return null; // the Deadlock model attaches on its first frame
            yield return null;
            VictorReassembly? parts = VictorReassembly.Spawn(gameObject, Down);
            if (parts != null) Own(parts); // a scene-root object: cleared if he is clicked off mid-reassembly
            if (parts == null) Play("revive_hold");
            DeadlockSounds.Play("victor_revive_start", Home);
            for (float t = 0f; t < Down; t += Time.deltaTime)
            {
                if (parts == null && Current != "revive_hold") Play("revive_hold"); // clips that finished loading mid-intro
                yield return null;
            }

            if (parts != null) parts.Finish();
            Play("revive_up");
            DeadlockSounds.Play("victor_revive_blast", Home);
        }
    }
}
