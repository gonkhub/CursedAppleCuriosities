using System.Linq;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>
    /// Air Drop with a passenger (user 3.0, Deadlock's airlift): cast with an ally near the crosshair and Ivy lifts them and carries
    /// them under her while she flies. Dropping them makes the ally the bomb: they explode (the bomb's blast, Ivy's damage,
    /// enemies only) when they next touch the ground, or after <see cref="IvyData.AirDropAllyFuse"/> seconds at most. The
    /// passenger takes no fall damage from it and gets Air Drop's damage buff too. Movement belongs to whoever has authority
    /// over the ally (a player's own machine, the server for drones), so <see cref="IvyCarried"/> runs there.
    /// </summary>
    public static class IvyCarry
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<IvyCarryRequest>();
            NetworkingAPI.RegisterMessageType<IvyCarryState>();
        }

        /// <summary>Ivy's owner: the ally nearest the crosshair she could lift (in reach, in sight, able to be moved), or null.</summary>
        public static CharacterBody? FindAlly(CharacterBody ivy)
        {
            if (ivy.inputBank == null || ivy.teamComponent == null) return null;
            Ray aim = ivy.inputBank.GetAimRay();
            var search = new BullseyeSearch
            {
                teamMaskFilter = TeamMask.none, filterByLoS = true, searchOrigin = aim.origin, searchDirection = aim.direction,
                maxDistanceFilter = IvyData.AirDropAllyRange, maxAngleFilter = IvyData.AirDropAllyCone, sortMode = BullseyeSearch.SortMode.Angle,
            };
            search.teamMaskFilter.AddTeam(ivy.teamComponent.teamIndex);
            search.RefreshCandidates();
            search.FilterOutGameObject(ivy.gameObject);
            return search.GetResults()
                .Select(h => h != null && h.healthComponent != null ? h.healthComponent.body : null)
                .FirstOrDefault(b => b != null && b != ivy && b.healthComponent.alive && b.characterMotor != null && !b.isChampion);
        }

        /// <summary>Server: the dropped ally becomes the bomb.</summary>
        internal static void Arm(CharacterBody ivy, CharacterBody ally, bool crit)
        {
            var bomb = ally.GetComponent<IvyLivingBomb>() ?? ally.gameObject.AddComponent<IvyLivingBomb>();
            bomb.Ivy = ivy;
            bomb.Crit = crit;
            bomb.Until = Time.time + IvyData.AirDropAllyFuse * DurationExtender.Multiplier(ivy);
            bomb.Armed = true;
            DeadlockFallGuard.ProtectNextLanding(ally.gameObject, IvyData.AirDropAllyFuse + 2f, bomb.Explode);
            if (IvySurvivor.AirDropBuff != null)
            {
                ivy.AddTimedBuff(IvySurvivor.AirDropBuff, IvyData.AirDropBuffSeconds);
                ally.AddTimedBuff(IvySurvivor.AirDropBuff, IvyData.AirDropBuffSeconds);
            }
            EquipmentFx.Sound("ivy_airdrop_launch", ally.corePosition);
        }
    }

    /// <summary>Ivy's owner to server: lift this ally, or drop them (as the bomb) with this toss.</summary>
    public class IvyCarryRequest : INetMessage
    {
        public GameObject? Ivy, Ally;
        public bool Release, Crit;
        public Vector3 Velocity;

        public void Serialize(NetworkWriter w) { w.Write(Ivy); w.Write(Ally); w.Write(Release); w.Write(Crit); w.Write(Velocity); }
        public void Deserialize(NetworkReader r) { Ivy = r.ReadGameObject(); Ally = r.ReadGameObject(); Release = r.ReadBoolean(); Crit = r.ReadBoolean(); Velocity = r.ReadVector3(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Ivy == null || Ally == null || !Ivy.TryGetComponent(out CharacterBody ivy) || !Ally.TryGetComponent(out CharacterBody ally)) return;
            if (ally.healthComponent == null || !ally.healthComponent.alive || ally.teamComponent == null || ivy.teamComponent == null || ally.teamComponent.teamIndex != ivy.teamComponent.teamIndex) return;
            new IvyCarryState { Ivy = Ivy, Ally = Ally, Release = Release, Velocity = Velocity }.Send(NetworkDestination.Clients); // the host's own client too
            if (Release) IvyCarry.Arm(ivy, ally, Crit);
            else DeadlockFallGuard.ProtectNextLanding(Ally, IvyData.AirDropSeconds * DurationExtender.Multiplier(ivy) + 2f); // a carried ally scraping the ground isn't a fall
        }
    }

    /// <summary>Server to every client: the ally is now carried by Ivy, or let go with this velocity.</summary>
    public class IvyCarryState : INetMessage
    {
        public GameObject? Ivy, Ally;
        public bool Release;
        public Vector3 Velocity;

        public void Serialize(NetworkWriter w) { w.Write(Ivy); w.Write(Ally); w.Write(Release); w.Write(Velocity); }
        public void Deserialize(NetworkReader r) { Ivy = r.ReadGameObject(); Ally = r.ReadGameObject(); Release = r.ReadBoolean(); Velocity = r.ReadVector3(); }

        public void OnReceived()
        {
            if (Ally == null) return;
            var carried = Ally.GetComponent<IvyCarried>();
            if (Release)
            {
                if (carried != null) carried.Release(Velocity);
                return;
            }
            if (Ivy == null) return;
            carried ??= Ally.AddComponent<IvyCarried>();
            carried.Begin(Ivy);
        }
    }

    /// <summary>
    /// Every machine, on the carried ally; only the one with authority over the ally moves them: kept just under Ivy's feet while
    /// she flies, then tossed when she lets go. Ends on its own if Ivy is gone or the flight outlasts Air Drop.
    /// </summary>
    public class IvyCarried : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private CharacterBody? _ivy;
        private float _until;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Begin(GameObject ivy)
        {
            _ivy = ivy.GetComponent<CharacterBody>();
            _until = Time.time + IvyData.AirDropSeconds * DurationExtender.Multiplier(_ivy) + 1.5f;
            enabled = true;
        }

        public void Release(Vector3 velocity)
        {
            if (_body.hasEffectiveAuthority && _body.characterMotor != null)
            {
                _body.characterMotor.velocity = velocity;
                _body.characterMotor.disableAirControlUntilCollision = false;
            }
            Destroy(this);
        }

        private void FixedUpdate()
        {
            if (_ivy == null || _ivy.healthComponent == null || !_ivy.healthComponent.alive || Time.time > _until) { Destroy(this); return; }
            if (!_body.hasEffectiveAuthority || _body.characterMotor == null) return;
            CharacterMotor motor = _body.characterMotor;
            Vector3 hold = _ivy.footPosition + Vector3.down * IvyData.AirDropCarryGap;
            Vector3 to = hold - _body.footPosition;
            motor.Motor.ForceUnground();
            motor.velocity = Vector3.ClampMagnitude(to / Time.fixedDeltaTime * 0.5f, 60f);
        }
    }

    /// <summary>Server, on a dropped ally: they are the bomb until they land (or the fuse runs out, or they die).</summary>
    public class IvyLivingBomb : MonoBehaviour
    {
        public CharacterBody? Ivy;
        public bool Crit, Armed;
        public float Until;
        private CharacterBody _body = null!;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            if (!NetworkServer.active || !Armed) return;
            if (Time.time >= Until || _body.healthComponent == null || !_body.healthComponent.alive) Explode();
        }

        public void Explode()
        {
            if (!Armed) return;
            Armed = false;
            CharacterBody? by = Ivy;
            if (by != null && by.teamComponent != null)
            {
                Vector3 at = _body.footPosition;
                float radius = IvyData.AirDropRadius * MysticExpansion.RadiusMultiplier(by);
                IvyNet.Effect(IvySurvivor.BombEffect, at, radius);
                EquipmentFx.Sound("ivy_airdrop_explode", at);
                foreach (HealthComponent hc in IvyNet.EnemiesIn(at, radius, by.teamComponent.teamIndex))
                    IvyNet.Hit(by, hc, IvyData.AirDropDamage * by.damage, Crit, (hc.transform.position - at).normalized * 1500f, DamageSource.Special);
            }
            Destroy(this);
        }
    }
}
