using R2API;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>
    /// A hostile, AI-driven Bebop (user 3.0: the Sunken Plaza pit's guardian, instead of the Imp Overlord). His own body on a
    /// clone of vanilla's Commando AI master (the Artifact of Vengeance's doppelganger brain), with skill drivers for his kit:
    /// Hyper Beam when he has a clear line, Exploding Uppercut and Sticky Bomb up close, his beam in range, otherwise chase.
    /// </summary>
    public static class BebopInvader
    {
        public static GameObject? MasterPrefab;
        public static CharacterSpawnCard? Card;
        private const string MasterName = "CursedAppleBebopMonsterMaster";
        private static bool _dropHooked;

        /// <summary>Server: a slain invading Bebop drops a Sticky Bomb (user 3.0), popping up where he fell.</summary>
        private static void DropStickyBomb(DamageReport report)
        {
            if (!NetworkServer.active || report?.victimMaster == null || !report.victimMaster.name.StartsWith(MasterName) || report.victimBody == null) return;
            PickupIndex sticky = PickupCatalog.FindPickupIndex(RoR2Content.Items.StickyBomb.itemIndex);
            if (sticky == PickupIndex.none) return;
            PickupDropletController.CreatePickupDroplet(sticky, report.victimBody.corePosition + Vector3.up, Vector3.up * 15f + report.victimBody.transform.forward * 3f);
        }

        /// <summary>At load, after the body exists: the master and its spawn card (registered on every machine).</summary>
        public static void Build(GameObject body)
        {
            GameObject? template = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoMonsterMaster.prefab").WaitForCompletion();
            if (template == null) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Bebop] no Commando AI master to copy: no invader"); return; }
            GameObject master = template.InstantiateClone(MasterName, true);
            if (!_dropHooked) { _dropHooked = true; GlobalEventManager.onCharacterDeathGlobal += DropStickyBomb; }
            master.GetComponent<CharacterMaster>().bodyPrefab = body;
            foreach (AISkillDriver d in master.GetComponents<AISkillDriver>()) Object.DestroyImmediate(d);
            // He knows where everyone is (user 3.0: spawned in his room, he didn't notice the player who woke him).
            foreach (BaseAI ai in master.GetComponents<BaseAI>()) ai.fullVision = true;

            Driver(master, "HyperBeam", SkillSlot.Special, 0f, 45f, AISkillDriver.MovementType.StrafeMovetarget, AISkillDriver.ButtonPressType.Hold, hold: 2f, aim: true);
            Driver(master, "Uppercut", SkillSlot.Utility, 0f, 8f, AISkillDriver.MovementType.ChaseMoveTarget, AISkillDriver.ButtonPressType.TapContinuous);
            Driver(master, "Sticky", SkillSlot.Secondary, 0f, BebopData.StickyRange, AISkillDriver.MovementType.ChaseMoveTarget, AISkillDriver.ButtonPressType.TapContinuous);
            Driver(master, "Beam", SkillSlot.Primary, 0f, BebopData.BeamRange, AISkillDriver.MovementType.StrafeMovetarget, AISkillDriver.ButtonPressType.Hold, ready: false);
            Driver(master, "Chase", SkillSlot.None, 0f, float.PositiveInfinity, AISkillDriver.MovementType.ChaseMoveTarget, AISkillDriver.ButtonPressType.Abstain, ready: false, sprint: true);

            ContentAddition.AddMaster(master);
            MasterPrefab = master;

            Card = ScriptableObject.CreateInstance<CharacterSpawnCard>();
            Card.name = "cscCursedAppleBebopInvader";
            Card.prefab = master;
            Card.sendOverNetwork = true;
            Card.hullSize = HullClassification.Human;
            Card.nodeGraphType = MapNodeGroup.GraphType.Ground;
            Card.requiredFlags = NodeFlags.None;
            Card.forbiddenFlags = NodeFlags.NoCharacterSpawn;
            Card.directorCreditCost = 300; // a miniboss's worth of rewards (the Imp Overlord's card is 600)
            Card.occupyPosition = false;
        }

        private static void Driver(GameObject master, string name, SkillSlot slot, float min, float max, AISkillDriver.MovementType move,
            AISkillDriver.ButtonPressType press, float hold = -1f, bool aim = false, bool ready = true, bool sprint = false)
        {
            var d = master.AddComponent<AISkillDriver>();
            d.customName = name;
            d.skillSlot = slot;
            d.requireSkillReady = ready && slot != SkillSlot.None;
            d.minDistance = min;
            d.maxDistance = max;
            d.selectionRequiresTargetLoS = slot != SkillSlot.None;
            d.activationRequiresTargetLoS = slot != SkillSlot.None;
            d.activationRequiresAimConfirmation = aim;
            d.moveTargetType = AISkillDriver.TargetType.CurrentEnemy;
            d.movementType = move;
            d.aimType = AISkillDriver.AimType.AtCurrentEnemy;
            d.buttonPressType = press;
            d.shouldSprint = sprint;
            d.driverUpdateTimerOverride = hold;
        }
    }
}
