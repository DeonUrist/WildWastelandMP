using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using WildWasteland.Core;

namespace WildWasteland.Modules.Patrols
{
    // All Patrols settings (ex Apocapatrol.Plugin config statics + War Rig's S entries + MerchantSpotter's keys).
    //   F6 (main cfg, Apocasetter): [Patrols] Enabled (Core) + one toggle per feature, then the player-facing values
    //       ([Patrols / Spawning], [Patrols / World traffic], [Patrols / Merchants], [Patrols / Combat], [Patrols / Balance], [Patrols / War Rig]).
    //   F7 (tools.cfg): every former hidden / technical / pose / debug setting in "Patrols / <name>" sections.
    //   [Keys]: Patrol editor, Template recorder, Bus passenger pose editor, Merchant item editor (all None by default).
    // Every entry that existed in a standalone cfg is migrated on the first Wild Wasteland run (Core ModuleConfig.Migrate).
    public sealed partial class PatrolsModule
    {
        internal const string OldCfg = "com.denis.apocalypter.apocapatrol.cfg", WarRigCfg = "com.denis.apocalypter.warrig.cfg", SpotterCfg = "com.denis.apocalypter.merchantspotter.cfg";

        internal static ConfigEntry<Key> MenuKey, TemplateDumpKey, BusPassengerPoseEditorKey, MerchantEditorKey;
        internal static ConfigEntry<bool> AllowDebugSpawns;
        internal static ConfigEntry<float> PatrolSizePercent;
        internal static ConfigEntry<float> MinPartHealth, MaxPartHealth, MinPartsFill, MaxPartsFill;

        // Condition of a spawned part: min..max, weighted toward two thirds of the way up (triangular distribution)
        internal static float RollPartHealth() { return Triangular(MinPartHealth.Value, MaxPartHealth.Value); }
        internal static float RollPartFill() { return Triangular(MinPartsFill.Value, MaxPartsFill.Value); }

        // random between a and b, weighted toward two thirds of the way up
        internal static float Triangular(float a, float b)
        {
            float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            if (hi - lo < 0.01f) return lo;
            float mode = lo + (hi - lo) * 2f / 3f;
            float u = UnityEngine.Random.value, f = (mode - lo) / (hi - lo);
            return u < f ? lo + Mathf.Sqrt(u * (hi - lo) * (mode - lo)) : hi - Mathf.Sqrt((1f - u) * (hi - lo) * (hi - mode));
        }

        // [Combat]
        internal static ConfigEntry<bool> RangedCombat;
        internal static ConfigEntry<int> AudioVoices;
        internal static ConfigEntry<float> FireArc, MaxAimPitch, AimTurnSpeed, FireBurstSeconds, FireIntervalMin, FireIntervalMax, ShootDistance;
        internal static ConfigEntry<bool> RamDamage, RamDamageInCar;
        internal static ConfigEntry<float> RamDamageMultiplier, NpcRamDamageMultiplier, RamFullSpeedKmh, RamInCarFactor, RamPushStrength;
        internal static ConfigEntry<string> RamDamageByBody;
        // [Driving]
        internal static ConfigEntry<float> StuckPedalChance, BailChance, StuckBailChance;
        // [Self-destruct]
        internal static ConfigEntry<bool> SelfDestruct;
        internal static ConfigEntry<float> ExplodedLootPercent;
        // [AI]
        internal static ConfigEntry<bool> AiInvertSteering, AiOverlay, CustomPaintjobs;
        internal static ConfigEntry<bool> RaidersOverheat;
        internal static ConfigEntry<bool> RaiderMusic, RiderLances, HideFsmWarnings, TurretSpotEditor, FuryRoad, SuspensionLiftCenterOfMass;
        internal static ConfigEntry<float> RiderRange, RiderIntervalMin, RiderIntervalMax, RiderLanceSpeed, RiderMaxThrowAngle, RiderJumpRange, WitnessMeChance, RiderLeapSpeed, RiderBlastDamage, RiderBlastRadius, TurretThigh, TurretKnee, TurretHipHeight, RiderLeapReachPercent, RiderBailRange;
        internal static ConfigEntry<string> RiderLanceOffset, RiderLanceRotation;
        internal static ConfigEntry<float> AiDriveByOffset;
        internal static ConfigEntry<float> AiThrottle, AiLeadTime, AiCommitSeconds, AiSteerRate, AiSteerAngle, AiMaxSteerAtSpeed, AiTurnSafeSpeed,
            AiRamDistance, AiPassWidth, AiRunOutMeters, AiRunOutMaxSeconds, AiReverseSeconds, AiReverseThrottle, AiStuckSeconds,
            AiRecoverWindow, AiMaxRecovers, AiWaitSeconds, AiFeelerRange, AiFeelerSpeedFactor, AiFrontOffset, AiMaxSlopeDeg, AiAvoidGain,
            AiIgnoreMassBelow, AiGiveUpDistance;
        // [Debug]
        internal static ConfigEntry<bool> BusPassengerPoseEditorEnabled, MerchantEditorEnabled;
        internal static ConfigEntry<float> ExitSpeedKmh;
        // persistent world traffic
        internal static ConfigEntry<bool> TrafficDebugMap, TacticalDriving, FactionBattles, Merchants;
        internal static ConfigEntry<float> TrafficLoadRange, TrafficUnloadRange,
            TrafficDestinationGrace, TrafficPatrolSpeed, TrafficConvoySpeed, ConvoyGuardRange,
            PatrolMemorySeconds;
        internal static ConfigEntry<int> TrafficMaxGroups, TrafficMaxRealCars;
        internal static ConfigEntry<bool> TrafficRoadRouting;
        internal static ConfigEntry<float> TrafficRoadConnector;
        internal static ConfigEntry<bool> CarSpawnLimit, SimpleConvoyBanditsAI, FootPatrols, ReturnToCar;
        // foot patrols / return to car / awareness handover tuning (tools.cfg, Traffic tuning)
        internal static ConfigEntry<int> FootMaxActive;
        internal static ConfigEntry<float> FootSpawnChance, FootCheckSeconds, FootSpeed, FootLoadRange, FootUnloadRange, FootMinSourceDistance, FootMinPoiMetres,
            StrandedIdleSeconds, ReturnTimeoutSeconds, ReturnMaxDistance, DismountMemorySeconds, FootTargetRange;
        internal static ConfigEntry<int> MaxCarsInEvent, MaxCarsInDoubleEvent, MaxPatrols, MaxConvoys;
        internal static ConfigEntry<int> MaxMerchants; internal static ConfigEntry<float> MerchantSpawnChance, MerchantCheckMinutes;
        internal static ConfigEntry<float> CampTrafficRange;
        internal static ConfigEntry<bool> RealPatrolNames, HideRouteType;
        // [Cleanup]
        internal static ConfigEntry<bool> CleanupEnabled;
        internal static ConfigEntry<float> CleanupMinutes, CleanupDistance;
        internal static ConfigEntry<int> CleanupMaxCars;
        // [Convoy spawner]
        internal static ConfigEntry<bool> ConvoyEnabled;
        internal static ConfigEntry<float> ConvoySpawnDistance;
        // pose settings (F7 "Patrols / Pose", "Patrols / Pose.<Human>"; were hidden behind PoseConfiguration = true)
        internal static PoseFloat DriverOffsetX, DriverOffsetY, DriverOffsetZ;
        internal static PoseBool PoseEnabled;
        internal static PoseFloat PoseThigh, PoseKnee, PoseArm, PoseElbow, PoseLeftLegCloser, PoseRightLegCloser, PoseLeftArmCloser, PoseRightArmCloser, PoseBikeArm, PoseBikeArmSpread;

        // fixed values that used to be settings
        internal const float SpawnDistance = 8f;          // m in front of the player (template spawner)
        internal const float TakeoverSeconds = 2f;        // a promoted passenger drives off this long after climbing over
        internal const float EjectDistance = 0.3f;        // the carcass must be this far from the seat before the passenger climbs over
        internal const float EjectSpeed = 4f;             // m/s sideways (+ half of it up) given to the thrown carcass
        internal const float BailDistance = 0.5f;         // an occupant getting out appears this far beside its seat

        internal static readonly Dictionary<string, ArmPoseProfile> HumanArmPoses = new Dictionary<string, ArmPoseProfile>(StringComparer.OrdinalIgnoreCase);
        internal static readonly string[] HumanTypes = { "Boltjaw", "Flexa", "Lugnut", "Scrud", "Sprokka", "Scraffa", "Spanna" };

        // F7 section names (tools.cfg)
        internal const string TDebug = "Debug", TTraffic = "Traffic tuning", TCombat = "Combat tuning", TTurret = "Turret pose", TDriving = "Driving",
            TAI = "AI", TLoot = "Loot", TSpawner = "Spawner", TCleanup = "Cleanup", TAudio = "Audio", TPose = "Pose";

        internal static ModuleConfig Cfg;

        public override void BindConfig(ModuleConfig cfg)
        {
            Cfg = cfg;
            BindToggles(cfg);
            BindValues(cfg);
            BindKeys(cfg);
            BindTools(cfg);
            BindPoses(cfg);
            RaidersOverheat.SettingChanged += (s, e) => { if (Running) Crew.ApplyOverheatAll(); };
            SimpleConvoyBanditsAI.SettingChanged += (sender, args) => { if (Running) SimpleBanditBridge.RefreshAll(); };
            QuietFsm.On = HideFsmWarnings.Value;
            HideFsmWarnings.SettingChanged += (s, e) => QuietFsm.On = HideFsmWarnings.Value;
        }

        // ---------------------------------------------------------------- F6: one toggle per feature, in feature order
        void BindToggles(ModuleConfig cfg)
        {
            ConvoyEnabled = cfg.Player(null, "Automatic spawns", true,
                "Raider patrols and convoys spawn on their own while you play. Off = no automatic spawns at all (the editor's test spawns still work)");
            Migrate(cfg, ConvoyEnabled, "Convoy spawner", "Enabled"); Migrate(cfg, ConvoyEnabled, "General", "Enabled");
            FactionBattles = cfg.Player(null, "Faction battles", true,
                "World traffic: Scrapyard and Coyote groups that meet fight each other (resolved off-screen when you are far away; wrecks, corpses and a skull on the debug map). Off = no new battles");
            Merchants = cfg.Player(null, "Merchants", true,
                "World traffic: Coyote trader buses (General / Mechanic / Organic shops) travel and park; stop one with Alt+Q or the horn. Off = no automatic merchant routes (same as Max merchants 0)");
            RiderLances = cfg.Player(null, "Human turrets", true,
                "A template's human turrets fight: a Warboy crouching on the roof throws blast lances (and may leap at you: \"Witness me!\"), a gunner " +
                "shoots all around with its own gun. Off = they just ride along");
            Migrate(cfg, RiderLances, "Combat", "HumanTurrets");
            RangedCombat = cfg.Player(null, "Ranged combat", true,
                "Ranged humans (Boltjaw/Flexa/Lugnut/Scrud/Sprokka) in a car use their vanilla targeting and ranged attack: the passenger whenever a target " +
                "is in the fire arc, the driver in bursts. Off = everybody just rides along");
            Migrate(cfg, RangedCombat, "Combat", "RangedCombat");
            RamDamage = cfg.Player(null, "Ram damage", true,
                "An AI car that hits you (on foot or in your car) hurts you. The game's own bumper damage only works against creatures, not the player");
            Migrate(cfg, RamDamage, "Combat", "RamDamage");
            RamDamageInCar = cfg.Player(null, "Ram damage in car", false,
                "Vehicle collision damage to player drivers and passengers: AI rams and native crashes. Off protects occupants; on-foot ram damage keeps its separate switch. The host controls this in multiplayer");
            Migrate(cfg, RamDamageInCar, "Combat", "RamDamageInCar");
            RaiderMusic = cfg.Player(null, "Raider music", true,
                "A raider car built from a template that has a cassette in its radio plays it at full volume from the moment it spawns, " +
                "until you switch the radio off. Off = the cassette sits in the radio, silent, as in a parked car");
            Migrate(cfg, RaiderMusic, "General", "Raider music");
            CustomPaintjobs = cfg.Player(null, "Custom paintjobs", true,
                "Raider cars wear the paint jobs from the Patrols\\Textures folder (body textures, truck container sides and inside). " +
                "Off = they keep the game's own textures (cars spawned from then on; the textures are not loaded at game start)");
            Migrate(cfg, CustomPaintjobs, "Debug", "CustomPaintjobs");
            SelfDestruct = cfg.Player(null, "Always explode cars", false,
                "Override template explodeChance with 100%. Requires a dead driver and all living passengers/turrets to leave. An alive driver bailout never explodes the car; captured cars are protected");
            Migrate(cfg, SelfDestruct, "General", "Always explode cars");
            CleanupEnabled = cfg.Player(null, "Cleanup", true,
                "Remove raider cars you left behind. A car you have ever sat in is never removed (the game itself only deletes cars beyond 5 km)");
            Migrate(cfg, CleanupEnabled, "Cleanup", "Enabled");
            RaidersOverheat = cfg.Player(null, "Raiders overheat", false,
                "Raider engines overheat like yours (smoke, steam, the engine wears down). Off = they never overheat while their crew drives; " +
                "a car you take or an engine you loot heats normally again");
            Migrate(cfg, RaidersOverheat, "Balance", "RaidersOverheat");
            FuryRoad = cfg.Player(null, "Fury Road", true,
                "Every patrol and convoy group can spawn from the start: their minimum distance travelled and minimum boss kills are ignored. " +
                "Off = groups unlock with distance and bosses as set in the editor");
            Migrate(cfg, FuryRoad, "General", "Fury Road");
            SimpleConvoyBanditsAI = cfg.Player(null, "Simple convoy bandits AI", true,
                "NPC module: no crew AI while seated; dismounted bandits fight using baked-map navigation; they hunt you only once they have seen or fought you, and keep track of you for a while after a fight");
            Migrate(cfg, SimpleConvoyBanditsAI, "Experimental", "simple convoy bandits AI");
            FootPatrols = cfg.Player(null, "Foot patrols", true,
                "World traffic: groups of 2-5 raiders walk between camps over open land (simulated while you are far away). Crew left without a usable car also walk to a nearby camp. Off = no new foot patrols");
            ReturnToCar = cfg.Player(null, "Return to car", true,
                "Debug: dismounted crew whose fight is over walk back to their car and take their seats again (a passenger or gunner takes the wheel if the driver is dead); the car leaves once every living member is aboard");
            TacticalDriving = cfg.Player(null, "Tactical driving", true, "World traffic: varied attack runs and vehicle-checked camp approaches");
            Migrate(cfg, TacticalDriving, "World traffic prototype", "TacticalDriving");
            WarRig.S.SpawnEnabled = cfg.Player(null, "War Rig spawns", true,
                "Allow the abandoned War Rig POI and manual War Rig spawns. World discovery chance is in POI / World. Existing rigs are unaffected");
            cfg.Migrate(WarRig.S.SpawnEnabled, WarRigCfg, "Spawn", "Enabled");
            WarRig.S.GroundRecoveryEnabled = cfg.Player(null, "War Rig ground recovery", false,
                "Rescue a War Rig (with its truck and passengers) after a confirmed fall below the terrain. Off by default");
            cfg.Migrate(WarRig.S.GroundRecoveryEnabled, WarRigCfg, "Recovery", "Enabled");
        }

        // ---------------------------------------------------------------- F6: player-facing values
        void BindValues(ModuleConfig cfg)
        {
            const string sp = "Spawning";
            PatrolSizePercent = cfg.Player(sp, "PatrolSizePercent", 100f, "Size of every patrol / convoy that spawns, for weaker PCs: below 100 the group loses cars at random down to this share (rounded down, never below one car; a convoy keeps all of its trucks)", new AcceptableValueRange<float>(10f, 100f));
            Migrate(cfg, PatrolSizePercent, "Scaling", "PatrolSizePercent");
            MaxPatrols = cfg.Player(sp, "Max patrols", 3, "Maximum tracked patrol groups, including arrived and passing Coyote groups; replaces density", new AcceptableValueRange<int>(0, 64));
            Migrate(cfg, MaxPatrols, "Experimental", "Max patrols");
            MaxConvoys = cfg.Player(sp, "Max convoys", 1, "Maximum tracked convoy groups, including arrived groups; replaces density", new AcceptableValueRange<int>(0, 64));
            Migrate(cfg, MaxConvoys, "Experimental", "Max convoys");
            CarSpawnLimit = cfg.Player(sp, "Car spawn limit", true, "Limit physical encounter cars; pending builds count toward the same budget");
            Migrate(cfg, CarSpawnLimit, "Experimental", "car spawn limit");
            MaxCarsInEvent = cfg.Player(sp, "Max cars in event", 5, "Maximum cars in one patrol or convoy; cargo trucks have priority", new AcceptableValueRange<int>(1, 24));
            Migrate(cfg, MaxCarsInEvent, "Experimental", "Max cars in event");
            MaxCarsInDoubleEvent = cfg.Player(sp, "Max cars in double event", 6, "Combined physical car budget when encounters overlap near the player", new AcceptableValueRange<int>(1, 48));
            Migrate(cfg, MaxCarsInDoubleEvent, "Experimental", "Max cars in double event");

            const string wt = "World traffic";
            TrafficPatrolSpeed = cfg.Player(wt, "PatrolSpeedKmh", 55f, "Simulated/cruising patrol speed", new AcceptableValueRange<float>(10f, 100f));
            Migrate(cfg, TrafficPatrolSpeed, "World traffic prototype", "PatrolSpeedKmh");
            TrafficConvoySpeed = cfg.Player(wt, "ConvoySpeedKmh", 38f, "Simulated/cruising convoy speed", new AcceptableValueRange<float>(10f, 80f));
            Migrate(cfg, TrafficConvoySpeed, "World traffic prototype", "ConvoySpeedKmh");
            CampTrafficRange = cfg.Player(wt, "Camp traffic range", 2500f, "Loaded, uncleared raider camps support base patrols and convoys; incoming Coyote patrols do not require camps", new AcceptableValueRange<float>(500f, 10000f));
            Migrate(cfg, CampTrafficRange, "Experimental", "Camp traffic range");

            const string mc = "Merchants";
            MaxMerchants = cfg.Player(mc, "Max merchants", 2, "Maximum merchants, with at most one of each shop type. Zero disables automatic merchants.", new AcceptableValueRange<int>(0, 3));
            Migrate(cfg, MaxMerchants, "Experimental", "Max merchants");
            MerchantSpawnChance = cfg.Player(mc, "Merchant spawn chance", 100f, "Average percentage of Max merchants represented by routes. Whole slots are guaranteed; the remaining fractional slot is rolled at each check. 100 fills the cap; zero creates no merchant routes.", new AcceptableValueRange<float>(0f, 100f));
            Migrate(cfg, MerchantSpawnChance, "Experimental", "Merchant spawn chance");
            MerchantCheckMinutes = cfg.Player(mc, "Merchant check interval (minutes)", 5f, "Check and adjust merchant route population this often, including replacing departed merchants. No immediate replacement when a route ends. Loaded merchants finish their visit before excess routes are removed.", new AcceptableValueRange<float>(1f, 60f));
            Migrate(cfg, MerchantCheckMinutes, "Experimental", "Merchant check interval (minutes)");

            const string cb = "Combat";
            NpcRamDamageMultiplier = cfg.Player(cb, "NPC ram damage multiplier", 4f,
                "Damage multiplier when any car hits a creature other than the player, including NPC-driven cars: 4 = four times the game's bumper damage, 1 = vanilla, 0 = no damage", new AcceptableValueRange<float>(0f, 10f));
            ShootDistance = cfg.Player(cb, "ShootDistance", 40f, "Occupants only shoot at a target closer than this, m", new AcceptableValueRange<float>(1f, 200f));
            Migrate(cfg, ShootDistance, "Combat", "ShootDistance");
            WitnessMeChance = cfg.Player(cb, "WitnessMeChance", 10f,
                "% chance that an unhurt blast-lance rider screams \"Witness me!\" and leaps at your car when you drive close " +
                "(a wounded rider always does). 100 = every time", new AcceptableValueRange<float>(0f, 100f));
            Migrate(cfg, WitnessMeChance, "Debug", "WitnessMeChance"); Migrate(cfg, WitnessMeChance, "Combat", "WitnessMeChance");

            const string bl = "Balance";
            MinPartHealth = cfg.Player(bl, "MinPartHealth", 80f, "Lowest condition (%) of a spawned raider car's parts that have one (engine, radiator, wheels ...)", new AcceptableValueRange<float>(0f, 100f));
            Migrate(cfg, MinPartHealth, "Balance", "MinPartHealth");
            MaxPartHealth = cfg.Player(bl, "MaxPartHealth", 100f,
                "Highest condition (%) of a spawned raider car's parts; the roll is weighted toward two thirds of the way from Min to Max", new AcceptableValueRange<float>(0f, 100f));
            Migrate(cfg, MaxPartHealth, "Balance", "MaxPartHealth");
            ExplodedLootPercent = cfg.Player(bl, "ExplodedCarPartsPercent", 20f,
                "% chance for each part of an exploded raider car to keep its condition (MinPartHealth..MaxPartHealth) instead of dropping " +
                "at 0 %. 20 = about one part in five, 0 = all junk, 100 = all usable", new AcceptableValueRange<float>(0f, 100f));
            Migrate(cfg, ExplodedLootPercent, "Scaling", "ExplodedCarPartsPercent");

        }

        // ---------------------------------------------------------------- [Keys]
        void BindKeys(ModuleConfig cfg)
        {
            MenuKey = cfg.Hotkey("Patrol editor", Key.None, "Patrols: open the patrol / convoy / merchant / loot editor (also F7 > Patrols & vehicles > Patrol editor). None = unbound");
            Migrate(cfg, MenuKey, "Debug", "TemplateSpawnerKey"); Migrate(cfg, MenuKey, "Debug", "ApocaPatrol Config Key");
            TemplateDumpKey = cfg.Hotkey("Template recorder", Key.None, "Patrols: save the vehicle you sit in as a template (only while seated; also F7 > Template recorder). None = unbound");
            Migrate(cfg, TemplateDumpKey, "Debug", "TemplateDumpKey");
            BusPassengerPoseEditorKey = cfg.Hotkey("Bus passenger pose editor", Key.None, "Patrols: toggle the bus passenger pose editor (also F7 > Seat & turret poses). None = unbound");
            MigrateChangedKey(cfg, BusPassengerPoseEditorKey, OldCfg, "Debug", "Bus passenger pose editor key", "Insert");
            MerchantEditorKey = cfg.Hotkey("Merchant item editor", Key.None, "Patrols: toggle the merchant item placement editor (also F7 > Merchants). None = unbound");
            MigrateChangedKey(cfg, MerchantEditorKey, SpotterCfg, "Debug", "ToggleKey", "NumpadMultiply");
        }

        // ---------------------------------------------------------------- F7 tools.cfg
        void BindTools(ModuleConfig cfg)
        {
            // debug switches and editors
            TurretSpotEditor = cfg.Tool(TDebug, "TurretSpotEditor", false,
                "Glowing markers on the human turret spots of the nearest raider car and numpad keys to move them: 8/2 forward/back, 4/6 left/right, " +
                "7/9 down/up (Shift = x5), 0 = next turret, 5 = save the spot into the car's template file, . = markers on/off");
            Migrate(cfg, TurretSpotEditor, "Debug", "TurretSpotEditor");
            BusPassengerPoseEditorEnabled = cfg.Tool(TDebug, "Bus passenger pose editor", false, "Live arm, leg and seat adjustment for the nearest bus passenger. Num0 selects joint, Num/ selects side, Num8/2 bends, Num4/6 swings sideways, Num7/9 twists, Num5 saves. Disable other numpad editors while using this tool.");
            Migrate(cfg, BusPassengerPoseEditorEnabled, "Debug", "Bus passenger pose editor");
            MerchantEditorEnabled = cfg.Tool(TDebug, "Merchant item editor", false, "The in-car merchant object editor (numpad). Disable TurretSpotEditor while using it.");
            cfg.Migrate(MerchantEditorEnabled, SpotterCfg, "Debug", "Enabled");
            AllowDebugSpawns = cfg.Tool(TDebug, "AllowDebugSpawns", true, "Show Spawn Template, S group buttons and SPAWN in the patrol editor");
            Migrate(cfg, AllowDebugSpawns, "Debug", "AllowDebugSpawns");
            AiOverlay = cfg.Tool(TDebug, "AiOverlay", false, "On-screen line per AI car: state, speed, target angle, steering, feeler distances. With no raider car driving: the time until the next spawn roll");
            Migrate(cfg, AiOverlay, "Debug", "AiOverlay");
            HideFsmWarnings = cfg.Tool(TDebug, "Hide missing-FSM warnings", true, "The game's \"Could not find FSM: ID on GameObject: ...\" warnings are not printed (QuietFsm)");
            Migrate(cfg, HideFsmWarnings, "General", "Hide missing-FSM warnings");
            ExitSpeedKmh = cfg.Tool(TDebug, "ExitSpeedKmh", 30f,
                "You can leave your car below this speed (km/h). The game's own limit is 6 m/s = 21.6 km/h, too low to get out of a car a truck keeps shoving",
                new AcceptableValueRange<float>(5f, 200f));
            Migrate(cfg, ExitSpeedKmh, "Debug", "ExitSpeedKmh");

            // world traffic tuning + debug map
            TrafficDebugMap = cfg.Tool(TTraffic, "DebugMap", true, "Draw traffic routes only on the game's debug map when it is open");
            Migrate(cfg, TrafficDebugMap, "World traffic prototype", "DebugMap");
            RealPatrolNames = cfg.Tool(TTraffic, "Real patrol names", true, "Use each group's actual name on the game debug map; off labels groups Patrol or Convoy");
            Migrate(cfg, RealPatrolNames, "Experimental", "Real patrol names");
            HideRouteType = cfg.Tool(TTraffic, "hide route type", false, "Draw every traffic route and marker gray and label it Distant traffic; overrides Real patrol names");
            Migrate(cfg, HideRouteType, "Experimental", "hide route type");
            TrafficRoadRouting = cfg.Tool(TTraffic, "FollowRoads", true, "Influence sparse patrol waypoints with cached road hints; missing hints use a cached snake route");
            Migrate(cfg, TrafficRoadRouting, "World traffic prototype", "FollowRoads");
            TrafficRoadConnector = cfg.Tool(TTraffic, "MaxRoadConnectorMetres", 350f, "Maximum distance a coarse waypoint is attracted toward a cached road hint, m", new AcceptableValueRange<float>(25f, 1000f));
            Migrate(cfg, TrafficRoadConnector, "World traffic prototype", "MaxRoadConnectorMetres");
            TrafficMaxGroups = cfg.Tool(TTraffic, "MaxGroups", 24, "Maximum simulated plus real groups", new AcceptableValueRange<int>(1, 128));
            Migrate(cfg, TrafficMaxGroups, "World traffic prototype", "MaxGroups");
            TrafficMaxRealCars = cfg.Tool(TTraffic, "MaxRealCars", 24, "Budget for nearby physical traffic cars; pending builds reserve slots", new AcceptableValueRange<int>(1, 100));
            Migrate(cfg, TrafficMaxRealCars, "World traffic prototype", "MaxRealCars");
            TrafficLoadRange = cfg.Tool(TTraffic, "LoadRange", 750f, "Load traffic within this distance of the player, m", new AcceptableValueRange<float>(100f, 2000f));
            Migrate(cfg, TrafficLoadRange, "World traffic prototype", "LoadRange");
            TrafficUnloadRange = cfg.Tool(TTraffic, "UnloadRange", 1000f, "Unload only once every group member is this far away; always at least LoadRange + 100 m", new AcceptableValueRange<float>(200f, 4000f));
            Migrate(cfg, TrafficUnloadRange, "World traffic prototype", "UnloadRange");
            TrafficDestinationGrace = cfg.Tool(TTraffic, "DestinationGraceSeconds", 180f, "An arrived group waits this long without a nearby player before removal; proximity resets the clock", new AcceptableValueRange<float>(10f, 3600f));
            Migrate(cfg, TrafficDestinationGrace, "World traffic prototype", "DestinationGraceSeconds");
            ConvoyGuardRange = cfg.Tool(TTraffic, "ConvoyGuardRange", 300f, "Escort drivers attack within this range of the convoy; truck drivers keep travelling, m", new AcceptableValueRange<float>(20f, 1000f));
            Migrate(cfg, ConvoyGuardRange, "World traffic prototype", "ConvoyGuardRange");
            PatrolMemorySeconds = cfg.Tool(TTraffic, "PatrolMemorySeconds", 20f, "Search last known player position this long after losing sight/sound, then resume the route", new AcceptableValueRange<float>(0f, 120f));
            Migrate(cfg, PatrolMemorySeconds, "World traffic prototype", "PatrolMemorySeconds");
            FootMaxActive = cfg.Tool(TTraffic, "FootPatrolMaxActive", 2, "Most generated foot patrols at once (crew left without a usable car may add a few more)", new AcceptableValueRange<int>(0, 8));
            FootSpawnChance = cfg.Tool(TTraffic, "FootPatrolSpawnChance", 35f, "Chance (%) per check that a foot patrol leaves a nearby camp", new AcceptableValueRange<float>(0f, 100f));
            FootCheckSeconds = cfg.Tool(TTraffic, "FootPatrolCheckSeconds", 60f, "Seconds between foot patrol spawn checks", new AcceptableValueRange<float>(5f, 1800f));
            FootSpeed = cfg.Tool(TTraffic, "FootPatrolSpeed", 1.3f, "Simulated walking speed of a foot patrol, m/s", new AcceptableValueRange<float>(0.5f, 4f));
            FootLoadRange = cfg.Tool(TTraffic, "FootPatrolLoadRange", 280f, "Foot patrols become real NPCs within this distance of the player, m", new AcceptableValueRange<float>(80f, 600f));
            FootUnloadRange = cfg.Tool(TTraffic, "FootPatrolUnloadRange", 400f, "Real foot patrols become simulated again once every walker is this far away; at least load range + 80 m", new AcceptableValueRange<float>(150f, 1200f));
            FootMinPoiMetres = cfg.Tool(TTraffic, "FootPatrolMinPoiMetres", 1000f, "A generated foot patrol walks out to a point of interest at least this far from its camp (anywhere within one map chunk) and back, m", new AcceptableValueRange<float>(300f, 8000f));
            FootMinSourceDistance = cfg.Tool(TTraffic, "FootPatrolMinSourceDistance", 500f, "A generated foot patrol leaves only from a camp at least this far from you (out of sight; it walks simulated until it comes within FootPatrolLoadRange), m", new AcceptableValueRange<float>(300f, 5000f));
            StrandedIdleSeconds = cfg.Tool(TTraffic, "StrandedIdleSeconds", 30f, "Crew left without a usable car wait this long out of combat before walking to the nearest camp of their faction, s", new AcceptableValueRange<float>(5f, 600f));
            ReturnTimeoutSeconds = cfg.Tool(TTraffic, "ReturnToCarTimeoutSeconds", 120f, "Crew walking back to their car: the car leaves with whoever is aboard after this long (the rest become a foot patrol), s", new AcceptableValueRange<float>(20f, 900f));
            ReturnMaxDistance = cfg.Tool(TTraffic, "ReturnToCarMaxDistance", 300f, "Crew farther than this from their car when the fight is over do not walk back, m", new AcceptableValueRange<float>(30f, 1500f));
            DismountMemorySeconds = cfg.Tool(TTraffic, "DismountMemorySeconds", 90f, "Crew that fought or detected the player from the car still know where he is this long after getting out, s", new AcceptableValueRange<float>(0f, 600f));
            FootTargetRange = cfg.Tool(TTraffic, "FootTargetRange", 350f, "On-foot crew and foot patrols are offered the player as a target only within this distance (they still need to see, hear or remember him), m", new AcceptableValueRange<float>(50f, 1000f));

            // audio (startup)
            AudioVoices = cfg.Tool(TAudio, "AudioVoices", 64,
                "How many sounds the game can play at once (Unity's real voices; the game ships with 32). A firefight with several raider cars needs " +
                "more: every shot and every hit is a sound, and beyond the limit sounds - the player's own shots too - cut out. 64 is a good value; " +
                "0 leaves the game's own setting. Applied at game start (restart the game after a change)",
                new AcceptableValueList<int>(0, 32, 48, 64, 96, 128));
            Migrate(cfg, AudioVoices, "General", "AudioVoices"); Migrate(cfg, AudioVoices, "Scaling", "AudioVoices");

            // combat tuning (was the hidden [Combat] block)
            FireArc = cfg.Tool(TCombat, "FireArcHalfAngle", 100f, "Occupants may fire this many degrees left or right of the car's forward direction", new AcceptableValueRange<float>(0f, 180f));
            MaxAimPitch = cfg.Tool(TCombat, "MaxAimPitch", 35f, "Maximum upper-body aim angle up or down (degrees)", new AcceptableValueRange<float>(0f, 80f));
            AimTurnSpeed = cfg.Tool(TCombat, "AimTurnSpeed", 180f, "How quickly an occupant turns its upper body toward or away from a target (degrees/second)", new AcceptableValueRange<float>(1f, 720f));
            FireBurstSeconds = cfg.Tool(TCombat, "FireBurstSeconds", 3f, "How long one of the driver's bursts lasts (shooting pose, vanilla Attack inside the fire arc), s", new AcceptableValueRange<float>(0.5f, 30f));
            FireIntervalMin = cfg.Tool(TCombat, "FireIntervalMin", 8f, "Shortest pause between two bursts of the driver, s", new AcceptableValueRange<float>(0f, 120f));
            FireIntervalMax = cfg.Tool(TCombat, "FireIntervalMax", 20f, "Longest pause between two bursts of the driver, s", new AcceptableValueRange<float>(0f, 300f));
            RamDamageMultiplier = cfg.Tool(TCombat, "RamDamageMultiplier", 1f, "Ram damage scale: at 1 a full-speed hit takes 30 health with a small car, 50 with a Junker, 70 with a truck (RamDamageByBody)", new AcceptableValueRange<float>(0f, 3f));
            RamDamageByBody = cfg.Tool(TCombat, "RamDamageByBody", "Junker=50, Rust*=70, Scrapwagon=70, PigPen=50",
                "Full-speed damage per car body, 'Body=damage' pairs; * = prefix. Bodies not listed take " + Ram.DefaultDamage + ". Applied before the multiplier");
            RamFullSpeedKmh = cfg.Tool(TCombat, "RamFullSpeedKmh", 30f, "Impact speed (km/h, relative) for the full damage. At half that speed the hit does half damage; below half it does nothing", new AcceptableValueRange<float>(5f, 200f));
            RamPushStrength = cfg.Tool(TCombat, "RamPushStrength", 1f, "A damaging hit also shoves you (on foot) in the car's direction: 1 = about the impact speed plus a hop; 0 = off", new AcceptableValueRange<float>(0f, 4f));
            RamInCarFactor = cfg.Tool(TCombat, "RamInCarFactor", 1f, "With Ram damage in car: damage factor while you sit in your own car (1 = same as on foot)", new AcceptableValueRange<float>(0f, 1f));
            RiderRange = cfg.Tool(TCombat, "RiderRange", 35f, "A blast-lance rider throws at a target closer than this, m (and only when a flat throw reaches it)", new AcceptableValueRange<float>(5f, 150f));
            RiderMaxThrowAngle = cfg.Tool(TCombat, "RiderMaxThrowAngle", 20f, "The rider only throws when the lance reaches the predicted spot with at most this many degrees up (a throw, not a lob)", new AcceptableValueRange<float>(0f, 45f));
            RiderJumpRange = cfg.Tool(TCombat, "RiderJumpRange", 15f, "\"Witness me!\": the rider leaps when your car is closer than this, m", new AcceptableValueRange<float>(3f, 30f));
            RiderIntervalMin = cfg.Tool(TCombat, "RiderIntervalMin", 5f, "Shortest pause between two of a rider's throws, s", new AcceptableValueRange<float>(1f, 60f));
            RiderIntervalMax = cfg.Tool(TCombat, "RiderIntervalMax", 9f, "Longest pause between two of a rider's throws, s", new AcceptableValueRange<float>(1f, 120f));
            RiderLeapReachPercent = cfg.Tool(TCombat, "RiderLeapReachPercent", 80f, "\"Witness me!\": the Warboy only leaps at a car within this % of his longest jump (RiderLeapSpeed x 1.2 s, the speeds of both cars counted)", new AcceptableValueRange<float>(10f, 100f));
            RiderBailRange = cfg.Tool(TCombat, "RiderBailRange", 6f, "A Warboy on a motorcycle who cannot turn to you jumps off when you are closer than this, m", new AcceptableValueRange<float>(1f, 30f));
            SuspensionLiftCenterOfMass = cfg.Tool(TCombat, "SuspensionLiftCenterOfMass", true, "A raider car with a suspension lift kit carries its centre of mass lower (0.3 m, plus 1.5 x any extra lift of the body), so tall cars on big wheels do not flip in turns (Part Adjustment uses the same rule)");
            RiderLeapSpeed = cfg.Tool(TCombat, "RiderLeapSpeed", 11f, "\"Witness me!\": the leap's ground speed relative to the rider's car, m/s - a long, flat jump, no lob", new AcceptableValueRange<float>(5f, 40f));
            RiderBlastDamage = cfg.Tool(TCombat, "RiderBlastDamage", 60f, "\"Witness me!\": damage of the rider's blast at its centre (in your car too); falls to a third at the edge", new AcceptableValueRange<float>(0f, 300f));
            RiderBlastRadius = cfg.Tool(TCombat, "RiderBlastRadius", 7f, "\"Witness me!\": reach of the rider's blast, m (damage and the shove of your car)", new AcceptableValueRange<float>(1f, 20f));
            RiderLanceSpeed = cfg.Tool(TCombat, "RiderLanceSpeed", 30f, "Throw speed of a rider's lance, m/s (the player's is 30)", new AcceptableValueRange<float>(5f, 80f));
            foreach (var e in new ConfigEntryBase[] { FireArc, MaxAimPitch, AimTurnSpeed, FireBurstSeconds, FireIntervalMin, FireIntervalMax, RamDamageMultiplier, RamDamageByBody, RamFullSpeedKmh,
                RamPushStrength, RamInCarFactor, RiderRange, RiderMaxThrowAngle, RiderJumpRange, RiderIntervalMin, RiderIntervalMax, RiderLeapReachPercent, RiderBailRange,
                SuspensionLiftCenterOfMass, RiderLeapSpeed, RiderBlastDamage, RiderBlastRadius, RiderLanceSpeed })
                MigrateBase(cfg, e, "Combat", e.Definition.Key);

            // turret rider pose (crouched gunner + the Warboy's lance prop)
            TurretThigh = cfg.Tool(TTurret, "TurretThigh", 80f, "Crouched human turret gunner: thigh angle forward, degrees", new AcceptableValueRange<float>(0f, 150f));
            TurretKnee = cfg.Tool(TTurret, "TurretKnee", 130f, "Crouched human turret gunner: knee bend, degrees", new AcceptableValueRange<float>(0f, 170f));
            TurretHipHeight = cfg.Tool(TTurret, "TurretHipHeight", 0.4f, "Crouched human turret gunner: hips above the spot (feet), m", new AcceptableValueRange<float>(0f, 1.2f));
            RiderLanceOffset = cfg.Tool(TTurret, "RiderLanceOffset", "0, 0, 0", "Warboy's lance prop: position in the throwing hand, hand-local metres x, y, z");
            RiderLanceRotation = cfg.Tool(TTurret, "RiderLanceRotation", "0, 0, 0", "Warboy's lance prop: rotation in the throwing hand, degrees x, y, z");
            foreach (var e in new ConfigEntryBase[] { TurretThigh, TurretKnee, TurretHipHeight, RiderLanceOffset, RiderLanceRotation }) MigrateBase(cfg, e, "Combat", e.Definition.Key);

            StuckPedalChance = cfg.Tool(TDriving, "StuckPedalChance", 5f, "% chance that a killed driver's gas pedal stays stuck; otherwise the gas is released and the car rolls to a stop", new AcceptableValueRange<float>(0f, 100f));
            BailChance = cfg.Tool(TDriving, "BailChance", 25f,
                "% chance that a passenger who outlives the driver gets out and fights on foot once the car stands still; otherwise it takes the wheel. " +
                "If the player took the car first it always gets out", new AcceptableValueRange<float>(0f, 100f));
            StuckBailChance = cfg.Tool(TDriving, "StuckBailChance", 100f,
                "% chance that, when the driving AI gives up on a stuck car (after AI MaxRecovers), the whole crew gets out and fights on foot " +
                "instead of the car waiting WaitSeconds and trying again", new AcceptableValueRange<float>(0f, 100f));
            foreach (var e in new ConfigEntryBase[] { StuckPedalChance, BailChance, StuckBailChance }) MigrateBase(cfg, e, "Driving", e.Definition.Key);

            AiThrottle = cfg.Tool(TAI, "Throttle", 1f, "Maximum throttle the driving AI uses (0..1); also the throttle of a stuck pedal", new AcceptableValueRange<float>(0.05f, 1f));
            AiDriveByOffset = cfg.Tool(TAI, "DriveByOffset", 5f, "When the target may not be rammed (the template's ramsTargets), the car aims this far beside it and drives past instead, m", new AcceptableValueRange<float>(1f, 20f));
            AiLeadTime = cfg.Tool(TAI, "LeadTime", 1f, "Aim this many seconds ahead of the target's movement (intercept), s", new AcceptableValueRange<float>(0f, 4f));
            AiCommitSeconds = cfg.Tool(TAI, "CommitSeconds", 0.5f, "The aim point is re-taken only this often, so the car commits to a heading instead of twitching after the target, s", new AcceptableValueRange<float>(0.05f, 3f));
            AiSteerRate = cfg.Tool(TAI, "SteerRate", 1.5f, "How fast the wheel turns: full-lock units per second (1.5 = straight to full lock in 0.67 s). Lower = lazier, wider turns", new AcceptableValueRange<float>(0.2f, 10f));
            AiSteerAngle = cfg.Tool(TAI, "SteerAngle", 30f, "Angle to the aim point at which the driver asks for full lock, degrees (smaller = sharper corrections)", new AcceptableValueRange<float>(5f, 90f));
            AiMaxSteerAtSpeed = cfg.Tool(TAI, "MaxSteerAtSpeed", 0.35f, "Steering limit at 72 km/h and above (0..1); full lock is allowed below 18 km/h, blended in between. Keeps the car on its wheels", new AcceptableValueRange<float>(0.05f, 1f));
            AiTurnSafeSpeed = cfg.Tool(TAI, "TurnSafeSpeed", 18f, "Above this speed (m/s) the driver lifts off and brakes lightly when the target is more than 40 degrees off the nose", new AcceptableValueRange<float>(3f, 40f));
            AiRamDistance = cfg.Tool(TAI, "RamDistance", 15f, "Within this distance of the target (m), obstacle avoidance is switched off: go straight for the ram", new AcceptableValueRange<float>(0f, 50f));
            AiPassWidth = cfg.Tool(TAI, "PassWidth", 12f, "The target counts as passed when it is behind the car and within this many metres of the car's track", new AcceptableValueRange<float>(2f, 40f));
            AiRunOutMeters = cfg.Tool(TAI, "RunOutMeters", 30f, "After passing or ramming the target the car keeps going this far before turning around, m", new AcceptableValueRange<float>(0f, 200f));
            AiRunOutMaxSeconds = cfg.Tool(TAI, "RunOutMaxSeconds", 4f, "... or at most this long, s", new AcceptableValueRange<float>(0.5f, 20f));
            AiReverseSeconds = cfg.Tool(TAI, "ReverseSeconds", 2f, "How long the car reverses after hitting an obstacle or getting stuck, s (grows with repeated attempts)", new AcceptableValueRange<float>(0.5f, 10f));
            AiReverseThrottle = cfg.Tool(TAI, "ReverseThrottle", 0.6f, "Throttle used while reversing", new AcceptableValueRange<float>(0.1f, 1f));
            AiStuckSeconds = cfg.Tool(TAI, "StuckSeconds", 2f, "Not moving for this long while trying to drive forward = stuck, reverse out", new AcceptableValueRange<float>(0.5f, 10f));
            AiRecoverWindow = cfg.Tool(TAI, "RecoverWindow", 30f, "Recoveries closer together than this count as repeated attempts, s", new AcceptableValueRange<float>(1f, 60f));
            AiMaxRecovers = cfg.Tool(TAI, "MaxRecovers", 2f, "After this many repeated recoveries the car gives up: the crew bails (Driving StuckBailChance) or the car waits WaitSeconds", new AcceptableValueRange<float>(1f, 20f));
            AiWaitSeconds = cfg.Tool(TAI, "WaitSeconds", 4f, "How long a car that gave up sits still before trying again, s", new AcceptableValueRange<float>(1f, 60f));
            AiFeelerRange = cfg.Tool(TAI, "FeelerRange", 10f, "Base length of the centre obstacle feeler ray, m (side rays are shorter)", new AcceptableValueRange<float>(2f, 40f));
            AiFeelerSpeedFactor = cfg.Tool(TAI, "FeelerSpeedFactor", 0.6f, "Extra feeler length per m/s of speed", new AcceptableValueRange<float>(0f, 2f));
            AiFrontOffset = cfg.Tool(TAI, "FrontOffset", 2f, "Feelers start this far in front of the car's centre of mass, m (should be just outside the bumper)", new AcceptableValueRange<float>(0f, 5f));
            AiMaxSlopeDeg = cfg.Tool(TAI, "MaxSlopeDeg", 35f, "Surfaces flatter than this are driven over, steeper ones are obstacles (rocks, walls), degrees", new AcceptableValueRange<float>(10f, 80f));
            AiAvoidGain = cfg.Tool(TAI, "AvoidGain", 1.2f, "How hard the feelers steer away from obstacles", new AcceptableValueRange<float>(0f, 4f));
            AiIgnoreMassBelow = cfg.Tool(TAI, "IgnoreMassBelow", 40f, "Loose physics objects lighter than this (kg) are not obstacles; the car drives through them", new AcceptableValueRange<float>(0f, 1000f));
            AiGiveUpDistance = cfg.Tool(TAI, "GiveUpDistance", 700f, "Beyond this distance from the player the car stops chasing and coasts until the player comes closer, m", new AcceptableValueRange<float>(20f, 2000f));
            AiInvertSteering = cfg.Tool(TAI, "InvertSteering", false, "Flip the steering sign if the car turns away from the target instead of toward it");
            foreach (var e in new ConfigEntryBase[] { AiThrottle, AiDriveByOffset, AiLeadTime, AiCommitSeconds, AiSteerRate, AiSteerAngle, AiMaxSteerAtSpeed, AiTurnSafeSpeed, AiRamDistance,
                AiPassWidth, AiRunOutMeters, AiRunOutMaxSeconds, AiReverseSeconds, AiReverseThrottle, AiStuckSeconds, AiRecoverWindow, AiMaxRecovers, AiWaitSeconds, AiFeelerRange,
                AiFeelerSpeedFactor, AiFrontOffset, AiMaxSlopeDeg, AiAvoidGain, AiIgnoreMassBelow, AiGiveUpDistance, AiInvertSteering })
                MigrateBase(cfg, e, "AI", e.Definition.Key);

            MinPartsFill = cfg.Tool(TLoot, "MinPartsFill", 15f, "Lowest fill (% of capacity) of a spawned car's tank (gas/diesel), engine oil and radiator water", new AcceptableValueRange<float>(0f, 100f));
            MaxPartsFill = cfg.Tool(TLoot, "MaxPartsFill", 60f, "Highest fill (%) of tank, oil and water; each rolled separately, weighted toward two thirds of the way from Min to Max", new AcceptableValueRange<float>(0f, 100f));
            Migrate(cfg, MinPartsFill, "Loot", "MinPartsFill"); Migrate(cfg, MaxPartsFill, "Loot", "MaxPartsFill");

            ConvoySpawnDistance = cfg.Tool(TSpawner, "SpawnDistance", 350f, "How far away a spawn appears (m): ahead of your car, up to 45 degrees left or right; behind you when on foot", new AcceptableValueRange<float>(50f, 1000f));
            MigrateBase(cfg, ConvoySpawnDistance, "Convoy spawner", ConvoySpawnDistance.Definition.Key);

            CleanupMinutes = cfg.Tool(TCleanup, "RemoveAfterMinutes", 10f, "A raider car farther than MinDistance for this long is removed with its crew and cargo (0 = never by time)", new AcceptableValueRange<float>(0f, 600f));
            CleanupMaxCars = cfg.Tool(TCleanup, "MaxCars", 30, "With more raider cars than this in the world, the farthest ones beyond MinDistance are removed first (0 = no limit)", new AcceptableValueRange<int>(0, 200));
            CleanupDistance = cfg.Tool(TCleanup, "MinDistance", 800f, "Cars closer than this are never removed (m)", new AcceptableValueRange<float>(100f, 5000f));
            foreach (var e in new ConfigEntryBase[] { CleanupMinutes, CleanupMaxCars, CleanupDistance }) MigrateBase(cfg, e, "Cleanup", e.Definition.Key);
        }

        // ---------------------------------------------------------------- F7 poses (always editable; the PoseConfiguration gate is gone)
        void BindPoses(ModuleConfig cfg)
        {
            var offsetRange = new AcceptableValueRange<float>(-2f, 2f);
            DriverOffsetX = PoseFloat.Create(cfg, TPose, "OffsetX", 0f, "Where each occupant's hips go: offset from its seat anchor, right (m)", offsetRange);
            DriverOffsetY = PoseFloat.Create(cfg, TPose, "OffsetY", -0.3f, "Where each occupant's hips go: offset from its seat anchor, up (m)", offsetRange);
            DriverOffsetZ = PoseFloat.Create(cfg, TPose, "OffsetZ", 0f, "Where each occupant's hips go: offset from its seat anchor, forward (m)", offsetRange);
            foreach (var p in new[] { DriverOffsetX, DriverOffsetY, DriverOffsetZ }) { MigrateBase(cfg, p.Entry, "Driver", p.Entry.Definition.Key); MigrateBase(cfg, p.Entry, "Pose", p.Entry.Definition.Key); }
            PoseEnabled = PoseBool.Create(cfg, TPose, "Enabled", true, "Seated pose for live occupants");
            MigrateBase(cfg, PoseEnabled.Entry, "Pose", "Enabled");
            var bodyRange = new AcceptableValueRange<float>(0f, 130f);
            var closerRange = new AcceptableValueRange<float>(-60f, 60f);
            PoseThigh = PoseFloat.Create(cfg, TPose, "ThighAngle", 85f, "Thigh angle", bodyRange);
            PoseKnee = PoseFloat.Create(cfg, TPose, "KneeAngle", 30f, "Knee angle", bodyRange);
            PoseArm = PoseFloat.Create(cfg, TPose, "ArmAngle", 65f, "Fallback arm angle", bodyRange);
            PoseLeftLegCloser = PoseFloat.Create(cfg, TPose, "LeftLegCloser", 10f, "Left leg inward angle", closerRange);
            PoseRightLegCloser = PoseFloat.Create(cfg, TPose, "RightLegCloser", 10f, "Right leg inward angle", closerRange);
            PoseLeftArmCloser = PoseFloat.Create(cfg, TPose, "LeftArmCloser", 10f, "Fallback left arm inward angle", closerRange);
            PoseRightArmCloser = PoseFloat.Create(cfg, TPose, "RightArmCloser", 10f, "Fallback right arm inward angle", closerRange);
            PoseBikeArm = PoseFloat.Create(cfg, TPose, "BikeArmAngle", 55f, "Motorcycle riders (Motorcycle, Halfbreed): arm angle down to the handlebar", bodyRange);
            PoseBikeArmSpread = PoseFloat.Create(cfg, TPose, "BikeArmSpread", 22f, "Motorcycle riders: each arm this far outward, hands on the handlebar grips", closerRange);
            PoseElbow = PoseFloat.Create(cfg, TPose, "ElbowAngle", 25f, "Fallback elbow angle", bodyRange);
            foreach (var p in new[] { PoseThigh, PoseKnee, PoseArm, PoseLeftLegCloser, PoseRightLegCloser, PoseLeftArmCloser, PoseRightArmCloser, PoseBikeArm, PoseBikeArmSpread, PoseElbow })
                MigrateBase(cfg, p.Entry, "Pose", p.Entry.Definition.Key);

            HumanArmPoses.Clear();
            foreach (var human in HumanTypes)
            {
                var profile = ArmPoseProfile.Create(cfg, human, Array.IndexOf(PassengerGuard.RangedHumans, human) >= 0);
                HumanArmPoses[human] = profile;
                foreach (var e in profile.Entries()) MigrateBase(cfg, e, "Pose." + human, e.Definition.Key);
            }
        }

        // ---------------------------------------------------------------- migration helpers (old Apocapatrol cfg unless named)
        static void Migrate<T>(ModuleConfig cfg, ConfigEntry<T> e, string section, string key) { cfg.Migrate(e, OldCfg, section, key); }
        static void MigrateBase(ModuleConfig cfg, ConfigEntryBase e, string section, string key)
        {
            if (e == null) return;
            bool firstRun = ReferenceEquals(e.ConfigFile, cfg.Tools) ? Legacy.FirstRunTools : Legacy.FirstRunMain;
            if (!firstRun) return;
            string raw = Legacy.Get(OldCfg, section, key);
            if (raw == null) return;
            try { e.SetSerializedValue(raw); Log.Info("[Patrols] migrated " + OldCfg + " [" + section + "] " + key + " = " + raw); }
            catch (Exception x) { Log.Warn("[Patrols] could not migrate [" + section + "] " + key + ": " + x.Message); }
        }
        // keys whose default changed to None: the old value is carried over only when the player had changed it from the old default
        static void MigrateChangedKey(ModuleConfig cfg, ConfigEntry<Key> e, string file, string section, string key, string oldDefault)
        {
            string raw = Legacy.Get(file, section, key);
            if (raw == null || string.Equals(raw.Trim(), oldDefault, StringComparison.OrdinalIgnoreCase)) return;
            cfg.Migrate(e, file, section, key);
        }
    }

    internal class BonePoseConfig
    {
        internal PoseFloat Vertical, Horizontal, Rotation;

        internal static BonePoseConfig Create(ModuleConfig config, string section, string bone, float vertical, float horizontal, float twist)
        {
            var range = new AcceptableValueRange<float>(-180f, 180f);
            return new BonePoseConfig
            {
                Vertical = PoseFloat.Create(config, section, bone + "Vertical", vertical, bone + " vertical angle: positive swings forward/up (degrees)", range),
                Horizontal = PoseFloat.Create(config, section, bone + "Horizontal", horizontal, bone + " horizontal angle: positive turns toward the occupant's right (degrees)", range),
                Rotation = PoseFloat.Create(config, section, bone + "Rotation", twist, bone + " axial rotation around the limb/joint (degrees)", range)
            };
        }
    }

    // WW: always backed by an F7 tools.cfg entry (the old in-memory default path for "PoseConfiguration = false" is gone)
    internal class PoseFloat
    {
        internal readonly ConfigEntry<float> Entry;
        internal float Value { get { return Entry.Value; } }
        private PoseFloat(ConfigEntry<float> entry) { Entry = entry; }
        internal static PoseFloat Create(ModuleConfig config, string sub, string key, float value, string description, AcceptableValueRange<float> range)
        {
            return new PoseFloat(config.Tool(sub, key, value, description, range));
        }
    }

    internal class PoseBool
    {
        internal readonly ConfigEntry<bool> Entry;
        internal bool Value { get { return Entry.Value; } }
        private PoseBool(ConfigEntry<bool> entry) { Entry = entry; }
        internal static PoseBool Create(ModuleConfig config, string sub, string key, bool value, string description)
        {
            return new PoseBool(config.Tool(sub, key, value, description));
        }
    }

    internal class ArmPoseProfile
    {
        internal BonePoseConfig LeftArm, LeftElbow, LeftHand, RightArm, RightElbow, RightHand;
        internal WeaponPoseConfig Weapon;

        private static readonly Dictionary<string, float[]> Defaults = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Boltjaw", new[] { 80f,35f,-15.6f, 25f,0f,0f, 0f,0f,0f, -20f,50f,-20f, 125f,0f,25f, 0f,25f,-25f, 0f,0f,0f,-15f,20f,0f } },
            { "Flexa", new[] { 80f,35f,-15.652f, 25f,0f,0f, 0f,0f,0f, 5f,-10f,0f, 120f,0f,0f, 0f,25f,-45f, 0f,0f,0f,-15f,20f,0f } },
            { "Lugnut", new[] { 85f,34f,0f, 0f,0f,0f, 5f,15f,-10f, 80f,60f,20f, 20f,-140f,0f, -40f,50f,0f, 0f,-0.1f,0f,-15f,0f,0f } },
            { "Scrud", new[] { 78f,20f,30f, 25f,0f,0f, 0f,0f,0f, 75f,-20f,-30f, 25f,0f,0f, 0f,0f,0f, 0f,0f,0f,-25f,35f,0f } },
            { "Sprokka", new[] { 105f,10f,0f, 0f,0f,0f, 0f,0f,0f, 15f,-27f,-30f, 25f,0f,0f, 0f,0f,0f, 0f,0f,0.03f,0f,15f,0f } },
            { "Scraffa", new[] { 65f,10f,0f, 25f,0f,0f, 0f,0f,0f, 65f,-10f,0f, 25f,0f,0f, 0f,0f,0f } },
            { "Spanna", new[] { 65f,10f,0f, 25f,0f,0f, 0f,0f,0f, 65f,-10f,0f, 25f,0f,0f, 0f,0f,0f } }
        };

        // F7 section "Patrols / Pose.<Human>" (the old cfg section was [Pose.<Human>])
        internal static ArmPoseProfile Create(ModuleConfig config, string human, bool ranged)
        {
            string section = "Pose." + human;
            float[] d = Defaults[human];
            return new ArmPoseProfile
            {
                LeftArm = BonePoseConfig.Create(config, section, "LeftArm", d[0], d[1], d[2]),
                LeftElbow = BonePoseConfig.Create(config, section, "LeftElbow", d[3], d[4], d[5]),
                LeftHand = BonePoseConfig.Create(config, section, "LeftHand", d[6], d[7], d[8]),
                RightArm = BonePoseConfig.Create(config, section, "RightArm", d[9], d[10], d[11]),
                RightElbow = BonePoseConfig.Create(config, section, "RightElbow", d[12], d[13], d[14]),
                RightHand = BonePoseConfig.Create(config, section, "RightHand", d[15], d[16], d[17]),
                Weapon = ranged ? WeaponPoseConfig.Create(config, section, d) : null
            };
        }

        internal IEnumerable<ConfigEntryBase> Entries()
        {
            foreach (var b in new[] { LeftArm, LeftElbow, LeftHand, RightArm, RightElbow, RightHand })
            { yield return b.Vertical.Entry; yield return b.Horizontal.Entry; yield return b.Rotation.Entry; }
            if (Weapon != null)
                foreach (var p in new[] { Weapon.PositionX, Weapon.PositionY, Weapon.PositionZ, Weapon.RotationX, Weapon.RotationY, Weapon.RotationZ }) yield return p.Entry;
        }
    }

    internal class WeaponPoseConfig
    {
        internal PoseFloat PositionX, PositionY, PositionZ;
        internal PoseFloat RotationX, RotationY, RotationZ;

        internal static WeaponPoseConfig Create(ModuleConfig config, string section, float[] d)
        {
            var positionRange = new AcceptableValueRange<float>(-2f, 2f);
            var rotationRange = new AcceptableValueRange<float>(-180f, 180f);
            return new WeaponPoseConfig
            {
                PositionX = PoseFloat.Create(config, section, "WeaponPositionX", d[18], "Weapon local position along the hand's X axis (m)", positionRange),
                PositionY = PoseFloat.Create(config, section, "WeaponPositionY", d[19], "Weapon local position along the hand's Y axis (m)", positionRange),
                PositionZ = PoseFloat.Create(config, section, "WeaponPositionZ", d[20], "Weapon local position along the hand's Z axis (m)", positionRange),
                RotationX = PoseFloat.Create(config, section, "WeaponRotationX", d[21], "Weapon local rotation around its X axis (degrees)", rotationRange),
                RotationY = PoseFloat.Create(config, section, "WeaponRotationY", d[22], "Weapon local rotation around its Y axis (degrees)", rotationRange),
                RotationZ = PoseFloat.Create(config, section, "WeaponRotationZ", d[23], "Weapon local rotation around its Z axis (degrees)", rotationRange)
            };
        }
    }
}
