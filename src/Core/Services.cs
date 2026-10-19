using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Core
{
    /// <summary>
    /// Cross-module contracts (AGENTS.md 5.1: modules never reference each other's internals). A provider module assigns
    /// its implementation in Init and clears it in Shutdown; consumers null-check every call, so any module can be
    /// switched off without breaking the others (they fall back exactly as the standalone mods did when the provider mod was absent).
    /// </summary>
    public static class Services
    {
        /// <summary>Animation framework (PlayerBody module, ex Apocaplayer.ModAPI). Null when PlayerBody is off.</summary>
        public static IAnimationApi Animation;
        /// <summary>NPC AI (NPC module, ex NPCAI.Api + Nav structures). Null when NPC is off.</summary>
        public static INpcApi Npc;
        public static INpcAppearance NpcAppearance;
        // Asset paths follow bound settings even while NPC simulation is off; no runtime NPC state is retained.
        public static ICharacterAssets CharacterAssets;
        internal static ISleepingBodies SleepingBodies;
        /// <summary>Guns (Guns module, ex Gunplay.Api). Null when Guns is off.</summary>
        public static IGunsApi Guns;
        /// <summary>Hit feedback (HUD module, ex GunplayHUD.Api). Null when HUD is off.</summary>
        public static IHitFeedback Hud;
        public static IVehicleStatusDisplay VehicleStatus;
        public static IVehicleIgnition VehicleIgnition;
        public static ICompassMarkers Compass;
        public static IMapMarkers Map;
        public static IWeatherRegions Weather;
        public static IWeatherVisualPolicy WeatherVisualPolicy;
        public static IFollowerVehicles FollowerVehicles;
        public static IQuestApi Quests;
        public static IQuestPresentation QuestUi;
        public static ICampStatus Camps;
        // Patrols supplies a cheap query of its live, cached vehicle targets; null when it is off.
        internal static Func<bool> VehicleCombatActive;
        internal static void ReportCombat() { HostSaveCombat.Activity(); }
        private static IPoiApi _poi;
        public static event Action<IPoiApi> PoiChanged;
        public static IPoiApi Poi
        {
            get { return _poi; }
            set
            {
                _poi = value;
                var changed = PoiChanged;
                if (changed != null) try { changed(value); } catch (Exception e) { Log.Error("POI provider notification", e); }
            }
        }

        /// <summary>Patrols raised: an occupant left a raider car (ex Apocapatrol.Patrol.BailOut). (car, previous passenger object, new on-foot NPC)</summary>
        public static event Action<GameObject, GameObject, GameObject> CrewBailedOut;
        /// <summary>Patrols raised: a raider car exploded (ex Apocapatrol.Explode.Blast).</summary>
        public static event Action<GameObject> VehicleExploded;

        public static void RaiseCrewBailedOut(GameObject car, GameObject pax, GameObject npc)
        {
            var h = CrewBailedOut; if (h == null) return;
            try { h(car, pax, npc); } catch (Exception e) { Log.Error("CrewBailedOut handler", e); }
        }
        public static void RaiseVehicleExploded(GameObject car)
        {
            var h = VehicleExploded; if (h == null) return;
            try { h(car); } catch (Exception e) { Log.Error("VehicleExploded handler", e); }
        }

        internal static void ClearEvents()
        {
            CrewBailedOut = null; VehicleExploded = null;
        }
    }

    public enum WeatherVisualDetail { Low, Balanced, Max }

    public interface IWeatherVisualPolicy
    {
        WeatherVisualDetail WeatherDetail { get; }
    }

    public struct WeatherRegion
    {
        public Vector3 WorldCenter;
        public Vector2 Radii;
        public Vector3 Velocity;
        public float Heading;
        public bool Hurricane;
        public float AreaSquareKm
        {
            get
            {
                float area = 0; Vector2 a = Boundary(63);
                for (int i = 0; i < 64; i++) { Vector2 b = Boundary(i); area += a.x*b.y-b.x*a.y; a = b; }
                return Mathf.Abs(area)*.0000005f;
            }
        }
        public static float EdgeWave(float x) { return .055f*Mathf.Sin(x*3.7f+.4f)+.025f*Mathf.Sin(x*10.3f+.9f); }
        public static float EdgeDistance(float x, float z)
        {
            z -= EdgeWave(x);
            float u = Mathf.Abs(x)/Mathf.Max(.1f,.875f-.125f*z), v = Mathf.Abs(z);
            float u2 = u*u, v2 = v*v;
            return 1-Mathf.Pow(u2*u2*u2+v2*v2*v2,1f/6);
        }
        public Vector2 Boundary(int point)
        {
            float angle = point*Mathf.PI*2/64, c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float z = Mathf.Sign(s)*Mathf.Pow(Mathf.Abs(s),1f/3);
            float x = Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),1f/3)*(.875f-.125f*z);
            return new Vector2(x*Radii.x,(z+EdgeWave(x))*Radii.y);
        }
    }

    public interface IWeatherRegions
    {
        int Count { get; }
        bool MapAreas { get; set; }
        float HurricaneDensity { get; set; }
        float FrontWidthKm { get; set; }
        float DepthKm { get; set; }
        bool TryGet(int index, out WeatherRegion region);
    }

    public interface IVehicleIgnition
    {
        bool TryPlayer(GameObject car, bool down, bool held, out bool keyOn, out bool running, out bool cranking);
        bool TryRunning(GameObject car, out bool running);
        bool TryStart(GameObject car);
        bool TryStop(GameObject car);
    }

    public interface ICharacterAssets
    {
        bool TryPaths(string identity, out string model, out string texture);
    }

    public interface INpcAppearance
    {
        void Dress(GameObject root, string identity, bool randomize);
    }

    public sealed class PoiDefinition
    {
        public string Id;
        public float Radius;
        public Func<bool> Enabled;
        public Action<Vector3, Quaternion, Action<GameObject>> Spawn;
        public Action<GameObject> Mark;
    }

    public interface IPoiApi
    {
        void Register(PoiDefinition definition);
        void Remove(string id);
    }

    public interface ICompassMarkers
    {
        bool VehiclesEnabled { get; }
        int Add(string owner, Func<Vector3> position, string icon, Color color);
        bool Exists(int id);
        bool Remove(int id);
        void Identify(int id);
        void RemoveAll(string owner);
    }

    public interface IPlayerCompassMarkers
    {
        bool PlayersEnabled { get; }
        int AddPlayer(string owner, Func<Vector3> position, string name, Color color);
    }

    // ------------------------------------------------------------------ Animation (PlayerBody)

    /// <summary>Mirror of Apocaplayer.ModAPI v1 (static part).</summary>
    public interface IAnimationApi
    {
        int ApiVersion { get; }
        string Version { get; }
        /// <summary>Animation bundle loaded (clips available).</summary>
        bool Ready { get; }
        bool PlayerThirdPerson { get; }
        AudioClip PlayerShout { get; }
        bool TryPlayerJumpHead(GameObject player, out Vector3 position);
        bool TryPlayerWeaponMuzzle(out Vector3 position);
        string[] ClipNames();
        bool IsPlayerClip(string name);
        AnimationClip GetClip(string name);
        string WeaponKey(string objectName);
        string WeaponKind(string weaponKey);
        bool HasWeaponPoses(string weaponKey);
        bool WeaponPose(string weaponKey, string clip, out Vector3 localPosition, out Quaternion localRotation);
        string ReloadClip(string weaponKey);
        float ReloadClipSeconds(string weaponKey);
        float RoundSeconds(string weaponKey);
        bool ReloadsOneRoundAtATime(string weaponKey);
        bool CocksAfterShot(string weaponKey);
        float NativeSpeed(string tier);
        int Count { get; }
        /// <summary>Drives an NPC/turret Animator with the player animation set. Null when another owner animates it.</summary>
        IAnimatedCharacter Attach(Animator animator);
        IAnimatedCharacter Find(Animator animator);
    }

    /// <summary>Mirror of Apocaplayer.ModAPI.Character.</summary>
    public interface IAnimatedCharacter
    {
        Animator Animator { get; }
        bool Valid { get; }
        string WeaponKey { get; }
        string WeaponKind { get; }
        bool Reloading { get; }
        bool Pumping { get; }
        string HandsClip { get; }
        string ActionClip { get; }
        string UpperClip { get; }
        float SpeedCap { get; }
        bool ManualVelocity { get; set; }
        Vector3 Velocity { get; set; }
        bool Crouched { get; set; }
        bool Aiming { get; set; }
        bool Firing { get; set; }
        bool Airborne { get; set; }
        float AimPitch { get; set; }
        bool TurnInPlace { get; set; }
        bool WalkToStop { get; set; }
        bool Suspended { get; set; }
        void SetWeapon(Transform model);
        void SetWeapon(Transform model, string weaponKey);
        void Shoot();
        void Shoot(float seconds);
        void Reload(float seconds);
        void Reload(float seconds, int rounds);
        void CancelReload();
        void Pump();
        void Jump();
        void Kick();
        void Throw();
        void Throw(bool rightHand);
        void Strike();
        void Strike(float seconds);
        void Dispose();
    }

    public enum CampState { Unknown, Unloaded, Spawning, Ready, Cleared }
    public interface ICampStatus
    {
        // Stable MapMagic-relative position; Unknown/Unloaded never mean empty.
        CampState At(Vector3 world, float radius);
    }

    // ------------------------------------------------------------------ NPC

    /// <summary>A baked NPC navigation structure (camp/cave/building), ex NPCAI.Nav.Structure.</summary>
    public sealed class NavStructure
    {
        public Transform Root;
        /// <summary>Baked bounds (scene space at bake time; re-validate with Box.Contains(Root.position) after origin jumps).</summary>
        public Bounds? Box;
        public bool Baked;
        /// <summary>Floor height per cell (NaN = no floor), row-major with width <see cref="W"/>.</summary>
        public float[] FloorY;
        public int W;
        public float Cell;
        public Vector3 GridOrigin, GridRight, GridForward;
        public Vector3 FloorPoint(int index)
        {
            var p = GridOrigin + GridRight * ((index % W + 0.5f) * Cell) + GridForward * ((index / W + 0.5f) * Cell);
            p.y = FloorY[index]; return p;
        }
    }

    public struct NpcPlayerSight
    {
        public GameObject Player;
        public Vector3 WorldPosition;
        public bool Visible, Heard;
        public float Until, At;
        public Vector3 Position { get { return Origin.Scene(WorldPosition); } }
    }

    /// <summary>Mirror of NPCAI.Api v1 plus the nav structure list Apocapatrol read by reflection.</summary>
    public interface INpcApi
    {
        int ContractVersion { get; }
        void SetSimpleConvoyBandit(GameObject npc, bool enabled);
        void SetSimpleConvoyBanditState(GameObject npc, bool enabled, bool combat);
        void SetSimpleConvoyBanditTarget(GameObject npc, GameObject target);
        /// <summary>weaponKind 0..5 = Pistol, Smg, Rifle, Sniper, Shotgun, Crossbow (shared with Guns).</summary>
        void Shot(GameObject shooter, Vector3 position, int weaponKind, bool player);
        void Hurt(GameObject victim, GameObject attacker);
        float SpreadFactor(float distance);
        void Inform(GameObject npc, GameObject target);
        void SetFollowerDriven(GameObject npc, bool enabled, float reloadUntil = 0f);
        /// <summary>Fills <paramref name="into"/> (cleared first) with the current nav structures.</summary>
        void Structures(List<NavStructure> into);
        /// <summary>Temporary map diagnostic: highlight a POI type with no captured persistent bake.</summary>
        bool HighlightMissingPoiBake(string prefabName);
        /// <summary>Walks an NPC to a scene point with baked-map pathfinding and feelers (an Idle travel leg); null cancels.
        /// Combat takes over while the NPC has a target; the walk resumes once it is idle again. Works for simple convoy bandits too.</summary>
        void Travel(GameObject npc, Vector3? scenePoint, float arriveRadius);
        /// <summary>0 = no travel order, 1 = walking (or paused by combat), 2 = arrived, 3 = failed (gets nowhere / no NPC module brain).</summary>
        int TravelState(GameObject npc);
        /// <summary>True while the NPC has no target, ghost or search (simple convoy bandits: no target).</summary>
        bool IsIdle(GameObject npc);
        /// <summary>Awareness memory: the NPC knows where <paramref name="target"/> is for <paramref name="seconds"/> (it fought it),
        /// so losing sight does not make it forget. Refreshed by the NPC module itself while they keep fighting.</summary>
        void Remember(GameObject npc, GameObject target, float seconds);
        /// <summary>Host perception for one seated actor; hearing preserves a last-known point rather than granting vision.</summary>
        NpcPlayerSight ObservePlayers(GameObject observer, GameObject vehicle, Transform facing, bool hostile, float memorySeconds);
        void SetSeatedTarget(GameObject observer, GameObject target);
        /// <summary>Copy only this actor's actual player knowledge when replacing its seated/on-foot body.</summary>
        void TransferPlayerAwareness(GameObject source, GameObject destination, float seconds);
    }

    // ------------------------------------------------------------------ Guns

    /// <summary>Mirror of Gunplay.Api v1.</summary>
    public interface IGunsApi
    {
        int ContractVersion { get; }
        /// <summary>Gunplay projectiles/tracers replace the vanilla hitscan (NPC vanilla observers stand down).</summary>
        bool ProjectilesEnabled { get; }
        bool PlayerProjectilesEnabled { get; }
        bool BlocksProjectile(Collider collider);
        float EffectiveRange(int kind);
        bool TryGetWeaponKind(GameObject owner, out int kind);
        /// <summary>Guns handles melee hits on fitted wheels (Patrols' own melee-wheel code stands down; ex Apocapatrol.MeleeWheels.RaiderHandles).</summary>
        bool HandlesFittedWheelMelee { get; }
    }

    // ------------------------------------------------------------------ HUD

    public enum MapDetail { Off, Ambiguous, On }

    public interface IMapMarkers
    {
        /// <summary>Borrowed map artwork, or null while Clean HUD is off. Callers must not dispose it.</summary>
        Texture2D GetIcon(string key);
        MapDetail Markers { get; }
        MapDetail Routes { get; }
        int Add(string owner, Func<Vector3> position, string icon, Color color, string label);
        bool Exists(int id);
        void Remove(int id);
        void RemoveAll(string owner);
        /// <summary>True while the map's fog of war covers this scene position (always false with the fog off).</summary>
        bool Hidden(Vector3 scene);
        /// <summary>Saved exploration at this scene position, independent of fog display settings.</summary>
        bool Explored(Vector3 scene);
        /// <summary>True while the native map is shown (other mouse-wheel users stand down).</summary>
        bool Open { get; }
        /// <summary>Map setting "World traffic markers": moving traffic dots + names, with independent detail and fog of war.</summary>
        MapDetail TrafficMarkers { get; }
        /// <summary>Map camera zoom factor: 1 = native view, smaller = zoomed in (min about 0.667).</summary>
        float Zoom { get; }
        /// <summary>Observe a live target through binoculars, offering its colored caption; true when visible.</summary>
        bool Observe(Transform target, string name, Color color);
    }

    public interface IFollowerVehicles
    {
        bool IsSeated(GameObject npc);
        float ShootDistance { get; }
        Component SeatPose(GameObject npc, Transform seat, string name, string armLike);
        Component SeatCombat(GameObject npc, GameObject car, Transform seat);
        void ReleasePose(Component pose);
        bool CanFire(GameObject npc);
        // Legacy Quests binds these types by reflection; the provider owns the mapping.
        Type ResolveType(string name);
        // Includes dismounted crew while their existing runtime tags are present.
        bool IsCrew(GameObject npc);
        void Animated(GameObject npc, bool enabled);
        void Combat(GameObject npc, GameObject target, float blockedUntil);
    }

    public interface IVehicleStatusDisplay
    {
        bool TryShow(bool ignitionOff, bool handbrakeOn, bool playing, string volume);
    }

    /// <summary>Mirror of GunplayHUD.Api.</summary>
    public interface IHitFeedback
    {
        bool Enabled { get; }
        void PlayerHit(GameObject target, Vector3 point, float damage, bool head);
        void PartHit(GameObject target, Vector3 point, float percent);
        /// <summary>True while the Guns module reports hits through this API, so the HUD's vanilla SetFsmFloat observer must stay quiet.</summary>
        bool ExternalReporter { get; set; }
    }
}
