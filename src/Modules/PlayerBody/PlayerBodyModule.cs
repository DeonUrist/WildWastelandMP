using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.PlayerBody
{
    // Player body & animation (ported from Apocaplayer 2.3.9, which had absorbed Apocaclimber): a full character body under the
    // first-person camera and in the driver's seat, third person on foot and in cars, the Mixamo animation framework (ModAPI, also
    // used by NPC and Patrols through Core.Services.Animation), climbing, vehicle hotkeys / HUD and first-person polish.
    // The old Plugin's config statics live here under the same names; Apocaplayer's own Runner / ClimbRunner / ModApiRunner loops are
    // scheduler jobs (BodyRunner), its Harmony patches are Core hooks or module patches (see Init).
    public sealed class PlayerBodyModule : ModuleBase
    {
        public const string SourceVersion = "2.3.9";      // ModAPI.PluginVersion (consumers compare against Apocaplayer versions)
        const string OldCfg = "com.denis.apocalypter.apocaplayer.cfg";
        const string ClimberCfg = "com.denis.apocalypter.apocaclimber.cfg";
        internal static PlayerBodyModule Instance;

        public override string Id { get { return "PlayerBody"; } }
        public override string DisplayName { get { return "Player body & animation"; } }
        public override string[] ConflictsWith { get { return new[] { "com.denis.apocalypter.apocaplayer", "com.denis.apocalypter.apocaclimber" }; } }

        // ------------------------------------------------------------------ state shared with the ported code
        /// <summary>True between Init and Shutdown (replaces the old Plugin.Enabled.Value checks; Core removes the patches/jobs when off).</summary>
        internal static bool Running;
        internal new static string Dir;            // BepInEx\plugins\WildWasteland\PlayerBody\
        internal static string DataDir;        // BepInEx\config\WildWasteland\PlayerBody\
        internal static bool VerboseOn { get { return Core.Log.Verbose; } }

        // ------------------------------------------------------------------ F6 (main cfg): feature toggles + player options
        internal enum Gender { Female, Male, Merchant, Bodyguard, Sprokka, Pistoleer, Mechanic, OrganicMechanic, Gungirl, Shoota }
        internal static ConfigEntry<Gender> Character;
        internal static ConfigEntry<bool> ThirdPersonOnFoot, ToggleMiddleMouse, DynamicCrosshair, BodyFirstPerson, EmptyHandArms, FemaleArms, ReplaceDriver,
            EquipmentOnBody, PainSounds, FirstPersonSwayOn, SmoothAimDownSights, PickAssistOn;
        internal static ConfigEntry<bool> ClimbingOn, JumpTriggersClimbing;
        internal static ConfigEntry<bool> FullBodyOnlyOnFoot;
        internal static ConfigEntry<bool> FirstPersonArmsOnlyOnFoot;
        internal static bool BodyInCar { get { return BodyFirstPerson.Value && !FullBodyOnlyOnFoot.Value; } }
        internal static bool ArmsInCar { get { return EmptyHandArms.Value && !FirstPersonArmsOnlyOnFoot.Value; } }
        internal static ConfigEntry<bool> OcclusionPrototype, OcclusionInVehicle;
        internal static ConfigEntry<bool> VehicleStatusHint, VehicleHotkeyHint, IgnitionControlOn, LanceIgnoresOwnCar;
        // [Keys]
        internal static ConfigEntry<Key> ObserveKey, ClimbKey, CassetteKey, VolumeDownKey, VolumeUpKey;

        // ------------------------------------------------------------------ F7 (tools.cfg): tuning, offsets, editors (the old hidden H() entries)
        internal static ConfigEntry<bool> WeaponAdjust, MirrorBody, EnableMMB, VerboseModApi, FootIK, WalkToStop;
        internal static ConfigEntry<string> ModelFile, TextureFile, ArmsTextureFile, AnimBundleFile;
        internal static ConfigEntry<string> IdleClip, RunClip, RifleClip, PistolClip, MeleeClip;
        internal static ConfigEntry<float> BodyBack, BodyBackDown, RunClipSpeed, WalkStride, RunFrom, ThirdDistance, ThirdHeight, ThirdShoulder, AimPitchShare;
        internal static ConfigEntry<float> CarCameraForward, FirstPersonWeaponBack, FirstPersonChestLean;
        internal static ConfigEntry<float> FpCamX, FpCamY, FpCamZ, FpCarCamX, FpCarCamY, FpCarCamZ;
        internal static ConfigEntry<float> ThirdCarDistance, ThirdCarHeight;
        internal static ConfigEntry<float> JumpClipStart, StrikeWindup, PickAssistRadius, AdsTime, SeatDrop;
        internal static ConfigEntry<float> OcclusionOpacity, OcclusionRadius;
        // (the 1.7.x crouch entries CrouchRifleLegs / CrouchArmsRestFrame / CrouchArmsPhase / CrouchPistolLean / CrouchPistolArms, unused since 2.0, are not ported)
        internal static ConfigEntry<float> UpperPhase, ClipSprintSpeed, SprintFrom, TurnClipAngle, TurnStartRate, DirBlendSpeed, PumpClipSpeed;
        internal static ConfigEntry<float> RunLegsIn, RunLegsTurn;
        internal static ConfigEntry<float> OrbitSpeed, HipsDrift, ClipWalkSpeed, ClipRunSpeed, ClipCrouchSpeed, ThighSwing, KneeBend, ArmSwing, HipBob, CrouchDrop;
        internal static ConfigEntry<float> ClimbMinHeight, ClimbMaxHeight, ClimbReach, ClimbWaistLimit, ClimbSpeed;

        // the driver's-seat view offset Denis set in game (1.5.3: X -0.03, Y 0.02, Z 0.02) is the built-in base; the F7 values add to it
        internal static readonly Vector3 CarViewBase = new Vector3(-0.03f, 0.02f, 0.02f);
        // mirror of the game's Controls "Ignition" key (IgnitionControl), for the moment before the row exists (was a never-saved entry)
        internal static KeyCode IgnitionKeyCode = KeyCode.E;

        internal static bool Female { get { return Character != null && Character.Value == Gender.Female; } }
        internal static string BodyModelFile { get { return Female ? ModelFile.Value : "Models/Player_male.glb"; } }
        internal static string BodyTextureFile { get { return Female ? TextureFile.Value : "Models/Player_male.png"; } }

        internal static KeyCode ObserveKeyCode { get { return KeyMap.Code(ObserveKey.Value); } }
        internal static KeyCode ClimbKeyCode { get { return KeyMap.Code(ClimbKey.Value); } }
        internal static KeyCode CassetteKeyCode { get { return KeyMap.Code(CassetteKey.Value); } }
        internal static KeyCode VolumeDownKeyCode { get { return KeyMap.Code(VolumeDownKey.Value); } }
        internal static KeyCode VolumeUpKeyCode { get { return KeyMap.Code(VolumeUpKey.Value); } }

        // ------------------------------------------------------------------ logging (old Plugin.Log / Verbose / Warn)
        internal static void Info(string s) { if (Core.Log.Verbose) Core.Log.Info("[PlayerBody] " + s); }
        internal static void Verbose(string s) { if (Core.Log.Verbose) Core.Log.Info("[PlayerBody] " + s); }
        internal static void Warn(string s) { Core.Log.Warn("[PlayerBody] " + s); }
        internal static void Error(string s) { Core.Log.Error("[PlayerBody] " + s); }
        internal static void Error(Exception e) { Core.Log.Error("[PlayerBody] " + e); }

        internal static string ModPath(string rel)
        {
            rel = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(rel) ? rel : Path.Combine(Dir ?? Assets.ModuleDir("PlayerBody"), rel);
        }

        // ================================================================== config
        public override void BindConfig(ModuleConfig cfg)
        {
            Instance = this;
            // ---- F6: one toggle per feature (feature order), then the player options
            Services.SleepingBodies = new SleepingBodies();
            Character = cfg.Player("", "Character", Gender.Male, "Your character's body. Male, Female, Merchant, Bodyguard, Sprokka, OrganicMechanic, Gungirl and Shoota use Wild Wasteland player animations, online or offline. Legacy Pistoleer/Mechanic choices map to Bodyguard/Merchant.");
            Mig(Character, "General", "Character");
            Character.Value = (Gender)CharacterPortraits.Canonical((int)Character.Value);
            ThirdPersonOnFoot = cfg.Player("", "ThirdPerson", true, "The game's Change Camera key switches to a camera behind your character, on foot and in cars (replaces the game's own car camera).");
            ToggleMiddleMouse = cfg.Player("", "ToggleObserving", true, "Third person, observing key ([Keys] Observe). Off: hold the key to orbit the camera around the character; it returns behind the character when you let go. On: a press starts observing and the camera stays where you left it until the next press.");
            Mig(ToggleMiddleMouse, "Debug", "ToggleMiddleMouse"); Mig(ToggleMiddleMouse, "General", "Toggle Observing");
            DynamicCrosshair = cfg.Player("", "DynamicCrosshair", true, "Third person with a weapon drawn: the crosshair sits where your shots and melee hits really land (the eye ray from the head). Without a weapon, or Off: the crosshair stays centred and the camera turns toward the aim point.");
            Mig(DynamicCrosshair, "CAMERA", "DynamicCrosshair");
            BodyFirstPerson = cfg.Player("", "BodyFirstPerson", true, "First person: see the body (legs and torso when you look down, the shadow, the body in the driver's seat). Off = only the first-person arms.");
            Mig(BodyFirstPerson, "General", "BodyFirstPerson");
            FullBodyOnlyOnFoot = cfg.Player("", "FullBodyOnlyOnFoot", false, "Full Body: show the first-person body only on foot, keeping the driver's first-person view clear.");
            EmptyHandArms = cfg.Player("", "FirstPersonArms", true, "First person with nothing in hand: show the body's own arms (idle/walk swing, hands on the wheel in a car). Off = no body arms in first person; the game's weapon and item arms always show.");
            Mig(EmptyHandArms, "General", "FirstPersonArms");
            FirstPersonArmsOnlyOnFoot = cfg.Player("", "FirstPersonArmsOnlyOnFoot", false, "First Person Arms: show the empty-handed arms only on foot, hiding the hands on the wheel while driving.");
            FemaleArms = cfg.Player("", "FirstPersonArmSkin", true, "The game's first-person arms and kick leg wear your character's skin (Female; Male keeps the game's own).");
            ReplaceDriver = cfg.Player("", "ReplaceDriver", true, "Your character replaces the game's man in the driver's seat (posed from the seat, guns and throws from the seat) and on the TAB screen picture.");
            EquipmentOnBody = cfg.Player("", "EquipmentOnBody", true, "Show the carried equipment on the body: backpack, long guns on the back, sidearms, blades, binoculars, flashlight (with its glow).");
            PainSounds = cfg.Player("", "FemalePainSounds", true, "Female character: four female hurt sounds and a female death sound.");
            FirstPersonSwayOn = cfg.Player("", "SmoothWeaponSway", true, "First person: frame-rate independent weapon / item sway (the game's own sway stutters with uneven frame times).");
            SmoothAimDownSights = cfg.Player("", "SmoothAimDownSights", true, "First person: the gun eases between hip and sights (right mouse button) instead of jumping. Time in F7.");
            PickAssistOn = cfg.Player("", "PickAssist", true, "Third person on foot: an item, part or switch close to the crosshair on screen is picked even if the eye ray misses it.");

            ClimbingOn = cfg.Player("Climbing", "Climbing", true, "Climb onto reachable solid ledges on foot.");
            cfg.Migrate(ClimbingOn, ClimberCfg, "General", "Enabled"); Mig(ClimbingOn, "climbing", "Enabled");
            JumpTriggersClimbing = cfg.Player("Climbing", "JumpTriggersClimbing", true, "The game's Jump key climbs onto a reachable ledge or catches one within arm's reach during a jump. Otherwise it jumps normally.");
            Mig(JumpTriggersClimbing, "climbing", "Jump triggers climbing");

            OcclusionPrototype = cfg.Player("Camera culling", "CameraCulling", false, "Third person: keep the camera distance and make geometry between camera and body semi-transparent (prototype; roofs and vehicles get a larger window; not inside caves). Off = the camera moves in front of obstacles.");
            Mig(OcclusionPrototype, "CAMERA", "OcclusionPrototype"); Mig(OcclusionPrototype, "General", "3rd person camera culling"); Mig(OcclusionPrototype, "Occlusion", "3rd person camera culling");
            OcclusionInVehicle = cfg.Player("Camera culling", "CameraCullingInVehicles", false, "Third person while driving: also use the camera culling (needs CameraCulling on).");
            Mig(OcclusionInVehicle, "General", "3rd person camera culling in vehicles"); Mig(OcclusionInVehicle, "Occlusion", "3rd person camera culling in vehicles");

            VehicleStatusHint = cfg.Player("Vehicles", "VehicleStatusHint", true, "Show ignition off, handbrake engaged and playing cassette with volume in the top right while driving.");
            Mig(VehicleStatusHint, "VEHICLE", "VehicleStatusHint");
            VehicleHotkeyHint = cfg.Player("Vehicles", "VehicleHotkeyHint", true, "Show the vehicle hotkeys on the left while driving. Hiding the hints keeps the keys working.");
            Mig(VehicleHotkeyHint, "VEHICLE", "VehicleHotkeyHint");
            IgnitionControlOn = cfg.Player("Vehicles", "IgnitionKey", true, "Adds an \"Ignition\" row to the game's Controls screen (default E): each press performs one ignition/start panel click (ignition on, crank, then stop on standard cars).");
            LanceIgnoresOwnCar = cfg.Player("Vehicles", "LanceIgnoresOwnCar", true, "A blast lance thrown from the driver's seat flies out of your own car instead of blowing up on it.");

            // ---- [Keys]: gameplay keys keep Apocaplayer's defaults (polled in context: third person, climbing, driving)
            ObserveKey = cfg.Hotkey("Observe (third person)", Key.LeftAlt, "Third person: orbit the camera around the character (held, or press on / press off with ToggleObserving). None = no key.");
            MigKey(ObserveKey, "General", "RebindObserving");
            ClimbKey = cfg.Hotkey("Climb (alternative key)", Key.None, "Optional separate climb key (the game's Jump key also climbs with JumpTriggersClimbing). None = no key.");
            MigKey(ClimbKey, "climbing", "alternative climb key");
            CassetteKey = cfg.Hotkey("Cassette start/stop", Key.Z, "Driving: start / stop the cassette player. Starting at zero volume sets 0.5. None = no hotkey.");
            MigKey(CassetteKey, "VEHICLE", "CassetteKey");
            VolumeDownKey = cfg.Hotkey("Cassette volume down", Key.Minus, "Driving: cassette volume -0.1 (also keypad minus). None = no hotkey.");
            MigKey(VolumeDownKey, "VEHICLE", "VolumeDownKey");
            VolumeUpKey = cfg.Hotkey("Cassette volume up", Key.Equals, "Driving: cassette volume +0.1 (+ on the main keyboard, also keypad plus). None = no hotkey.");
            MigKey(VolumeUpKey, "VEHICLE", "VolumeUpKey");
            GunPose.BindEditorKeys(cfg);

            // ---- F7 (tools.cfg)
            const string Ed = "Editors", Cam = "Camera", Fp = "First person", Cull = "Camera culling", Seat = "Car seat", Clm = "Climbing",
                Anim = "Animation", Leg = "Animation (fallback clips)", Mdl = "Model", Api = "ModAPI";
            WeaponAdjust = cfg.Tool(Ed, "WeaponAdjustment", false, "Weapon pose editor (third person, weapon drawn): numpad 8/2 6/4 7/1 move the weapon in the hand, 5 move/rotate, 9/3 pick the animation, - / * delete/copy/paste. Aiming: Page Up/Down lift the hands, Home/End tilt the head, Insert/Delete push the hands forward. Saved to config/WildWasteland/PlayerBody/weapon-poses.txt and aim-lift.txt. These keys do nothing while this is off.");
            cfg.Migrate(WeaponAdjust, OldCfg, "Debug", "WeaponAdjustment");

            ThirdDistance = cfg.Tool(Cam, "ThirdPersonDistance", 3.26f, "Third person on foot: camera distance, m (the mouse wheel changes and remembers it).", new AcceptableValueRange<float>(0.8f, 8f));
            cfg.Migrate(ThirdDistance, Path.Combine("Apocaplayer", "camera.cfg"), "Camera", "ThirdPersonDistance");
            ThirdCarDistance = cfg.Tool(Cam, "ThirdPersonCarDistance", 5.38f, "Third person in a car: camera distance, m (mouse wheel).", new AcceptableValueRange<float>(2f, 15f));
            cfg.Migrate(ThirdCarDistance, Path.Combine("Apocaplayer", "camera.cfg"), "Camera", "ThirdPersonCarDistance");
            ThirdHeight = cfg.Tool(Cam, "ThirdHeight", 0.25f, "Third person on foot: camera height above the eyes, m.");
            ThirdShoulder = cfg.Tool(Cam, "ThirdShoulder", 0.45f, "Third person on foot: sideways offset (over the right shoulder), m.");
            ThirdCarHeight = cfg.Tool(Cam, "ThirdCarHeight", 1.1f, "Third person in a car: camera height above the eyes, m.");
            OrbitSpeed = cfg.Tool(Cam, "OrbitSpeed", 3f, "Third person: degrees per mouse step while the observing key orbits the camera.");
            EnableMMB = cfg.Tool(Cam, "EnableMMB", false, "Third person: the middle mouse button also orbits the camera (off: the game rotates a held item with it).");
            AdsTime = cfg.Tool(Cam, "AimDownSightsTime", 0.2f, "First person (SmoothAimDownSights): seconds the gun takes between hip and sights; 0 = the game's own (instant).");
            PickAssistRadius = cfg.Tool(Cam, "PickAssistRadius", 0.06f, "Third person (PickAssist): an item / part / switch this close to the cursor on screen (share of the screen height) is picked even if the eye ray misses it.");

            FpCamX = Slider(cfg, Fp, "FirstPersonBodyX", "FirstPersonCameraX"); FpCamY = Slider(cfg, Fp, "FirstPersonBodyY", "FirstPersonCameraY"); FpCamZ = Slider(cfg, Fp, "FirstPersonBodyZ", "FirstPersonCameraZ");
            BodyBack = cfg.Tool(Fp, "FirstPersonBodyBack", 0.08f, "First person on foot: the head sits this far (m) behind the camera.");
            BodyBackDown = cfg.Tool(Fp, "BodyBackDown", 0.08f, "First person: extra distance behind the camera when looking straight down, m (blended in with the pitch).");
            FirstPersonWeaponBack = cfg.Tool(Fp, "FirstPersonWeaponBack", 0.08f, "First person with a weapon drawn: the body moves this much (m) further back.");
            FirstPersonChestLean = cfg.Tool(Fp, "FirstPersonChestLean", 20f, "First person with a weapon drawn: the chest bends back by this many degrees.");

            FpCarCamX = Slider(cfg, Seat, "FirstPersonDrivingX", "FirstPersonDrivingCameraX"); FpCarCamY = Slider(cfg, Seat, "FirstPersonDrivingY", "FirstPersonDrivingCameraY"); FpCarCamZ = Slider(cfg, Seat, "FirstPersonDrivingZ", "FirstPersonDrivingCameraZ");
            CarCameraForward = cfg.Tool(Seat, "CarCameraForward", 0.12f, "Driving in first person with the body shown: the view is drawn this far (m) in front of the game's eye.");
            SeatDrop = cfg.Tool(Seat, "SeatDrop", 0.05f, "Driver's seat: the body sits this much (m) lower than the game's seated man (his pose put her a bit high for the steering wheel).");

            OcclusionOpacity = cfg.Tool(Cull, "OcclusionOpacity", .20f, "Opacity of blocking geometry inside the cutaway window: 0 = clear, 0.2 = faintly visible.", new AcceptableValueRange<float>(0f, .9f));
            cfg.Migrate(OcclusionOpacity, OldCfg, "CAMERA", "OcclusionOpacity"); cfg.Migrate(OcclusionOpacity, OldCfg, "Occlusion", "OcclusionOpacity");
            OcclusionRadius = cfg.Tool(Cull, "OcclusionRadius", .55f, "Width around the character cleared by the cutaway, in metres. The rest of a large object remains visible.", new AcceptableValueRange<float>(.2f, 1.5f));
            cfg.Migrate(OcclusionRadius, OldCfg, "CAMERA", "OcclusionRadius"); cfg.Migrate(OcclusionRadius, OldCfg, "Occlusion", "OcclusionRadius");

            ClimbMinHeight = ClimbEntry(cfg, Clm, "MinimumHeight", .45f, "Climbing: minimum ledge height above the feet, metres.", .36f, 1f);
            ClimbMaxHeight = ClimbEntry(cfg, Clm, "MaximumHeight", 2.5f, "Climbing: maximum ledge height above the feet, metres.", 1f, 2.6f);
            ClimbReach = ClimbEntry(cfg, Clm, "Reach", .85f, "Climbing: maximum distance to the obstacle face, metres.", .35f, 1.2f);
            ClimbWaistLimit = ClimbEntry(cfg, Clm, "WaistHeightLimit", 1.25f, "Climbing: higher ledges use the high-climb animation.", .8f, 1.5f);
            ClimbSpeed = ClimbEntry(cfg, Clm, "AnimationSpeed", 1.5f, "Climbing: climb playback speed.", .6f, 2.5f);

            JumpClipStart = cfg.Tool(Anim, "JumpClipStart", 0.2f, "Jump / RifleJump / PistolJump clips start this far in (share of the clip): Mixamo jumps crouch first, the game's jump leaves the ground at once.");
            StrikeWindup = cfg.Tool(Anim, "StrikeWindup", 0.08f, "Melee / bare hands: seconds the wind-up takes before the blow lands (the game hits the moment the swing starts).");
            HipsDrift = cfg.Tool(Anim, "HipsDrift", 0.25f, "Bundle clips: how far (m) the hips may move away from the player sideways/forward (room for the idle sway).");
            ClipWalkSpeed = cfg.Tool(Anim, "BundleWalkSpeed", 1.4f, "Ground speed (m/s) at which the bundle's walk clips play at normal speed.");
            ClipRunSpeed = cfg.Tool(Anim, "BundleRunSpeed", 3.8f, "Ground speed (m/s) at which the bundle's run clips play at normal speed.");
            ClipCrouchSpeed = cfg.Tool(Anim, "BundleCrouchSpeed", 1.0f, "Ground speed (m/s) at which the bundle's crouch walk plays at normal speed.");
            ClipSprintSpeed = cfg.Tool(Anim, "BundleSprintSpeed", 5.5f, "Ground speed (m/s) at which the rifle pack's sprint clips play at normal speed (when the clip has no root motion of its own).");
            RunFrom = cfg.Tool(Anim, "RunFrom", 3.2f, "From this speed (m/s) the run clips take over from the walk.");
            SprintFrom = cfg.Tool(Anim, "SprintFrom", 4.3f, "Ground speed (m/s) above which the rifle pack's sprint clips replace its run clips (the game runs at 5).");
            UpperPhase = cfg.Tool(Anim, "UpperPhase", 0f, "Walking / running upper-body clips play in step with the rifle legs; this shifts them (0..1 of a stride, 0.5 = the other foot).");
            TurnClipAngle = cfg.Tool(Anim, "TurnClipAngle", 70f, "0 = no turn-in-place clips; any other value = on (the clips start with the first degree, see TurnStartRate).");
            TurnStartRate = cfg.Tool(Anim, "TurnStartRate", 8f, "Standing still: turning faster than this (degrees per second) steps the feet with the turn-in-place clips.");
            PumpClipSpeed = cfg.Tool(Anim, "PumpClipSpeed", 2f, "ShotgunPump (cocking the pump shotgun / bolt rifle) plays this fast (x normal).");
            DirBlendSpeed = cfg.Tool(Anim, "DirBlendSpeed", 360f, "How fast (degrees per second) the legs' walking direction turns when the movement keys change.");
            FootIK = cfg.Tool(Anim, "FootIK", true, "Unity's humanoid foot IK on the bundle's idle and walking clips (never on run / sprint / jump clips).");
            RunLegsIn = cfg.Tool(Anim, "RunLegsIn", 10f, "Running (bundle animations): each thigh turned this many degrees toward the middle, the feet kept flat.");
            WalkToStop = cfg.Tool(Anim, "WalkToStop", true, "Relaxed walking forward and stopping: the RifleWalkToStop clip's last step settles the feet.");
            RunLegsTurn = cfg.Tool(Anim, "RunLegsTurn", 75f, "Relaxed running (not aiming): the legs turn up to this many degrees toward the way she runs; the chest keeps facing the camera.");
            CrouchDrop = cfg.Tool(Anim, "CrouchDrop", 0.42f, "How far the hips go down when crouched, m.");
            AimPitchShare = cfg.Tool(Anim, "AimPitch", 0.9f, "Share of the camera pitch the spine follows (aiming up/down).");

            IdleClip = cfg.Tool(Leg, "IdleClip", "merchant_idle", "Without the animation bundle: humanoid clip for standing (the game has: merchant_idle, enemy_1_idle, zombie_idle).");
            RunClip = cfg.Tool(Leg, "RunClip", "enemy_1_run", "Without the bundle: humanoid clip for running.");
            RifleClip = cfg.Tool(Leg, "RifleClip", "enemy_machinegun", "Without the bundle: upper-body clip with a rifle / SMG / shotgun / crossbow drawn.");
            PistolClip = cfg.Tool(Leg, "PistolClip", "sprokka_shoot", "Without the bundle: upper-body clip with a pistol / revolver drawn.");
            MeleeClip = cfg.Tool(Leg, "MeleeClip", "enemy_1_attack", "Upper-body clip played once per melee swing (when the bundle has no Melee clip).");
            RunClipSpeed = cfg.Tool(Leg, "RunClipSpeed", 4.5f, "Without the bundle: ground speed (m/s) at which the run clip plays at normal speed.");
            WalkStride = cfg.Tool(Leg, "WalkStride", 1.25f, "Procedural walk: length of one full walk cycle (two steps), m.");
            ThighSwing = cfg.Tool(Leg, "ThighSwing", 26f, "Procedural walk: thigh swing, degrees.");
            KneeBend = cfg.Tool(Leg, "KneeBend", 38f, "Procedural walk: knee bend in the swing phase, degrees.");
            ArmSwing = cfg.Tool(Leg, "ArmSwing", 16f, "Procedural walk: arm swing without a weapon, degrees.");
            HipBob = cfg.Tool(Leg, "HipBob", 0.025f, "Procedural walk: hip bob, m.");
            MirrorBody = cfg.Tool(Leg, "RightHanded", true, "Raider-clip mode only: mirror the body so the gun is in the right hand.");

            ModelFile = cfg.Tool(Mdl, "Model", "Models/Player_female.glb", "Female body model (.glb/.gltf) relative to plugins/WildWasteland/PlayerBody, rigged to Flexa's skeleton (22 mixamorig bones). Restart the module to reload.");
            TextureFile = cfg.Tool(Mdl, "Texture", "Models/Player_female.png", "Female body texture relative to the module folder.");
            ArmsTextureFile = cfg.Tool(Mdl, "ArmsTexture", "Models/Player2_female_arms.png", "Female replacement for the game's Player2 texture atlas, used only on the first-person arms and kick leg.");
            AnimBundleFile = cfg.Tool(Mdl, "AnimationBundle", "Models/apocaplayer_anims.bundle", "AssetBundle with the humanoid (Mixamo) clips; loaded once per game start.");


            VerboseModApi = cfg.Tool(Api, "VerboseModApi", false, "Log every decision of every ModAPI character (very chatty; needs [Debug] VerboseLog).");

            // live reactions (the handlers only act while the module runs)
            Character.SettingChanged += (s, e) => { var choice = (Gender)CharacterPortraits.Canonical((int)Character.Value); if (choice != Character.Value) { Character.Value = choice; return; } if (Running) BodyRunner.CharacterChanged(); };
            ReplaceDriver.SettingChanged += (s, e) => { if (!Running) return; if (ReplaceDriver.Value) InventoryModel.Reset(); else InventoryModel.Off(); };
            IgnitionControlOn.SettingChanged += (s, e) => { if (Running && IgnitionJob != null) IgnitionJob.RunNow(); };
        }

        static void Mig<T>(ConfigEntry<T> e, string section, string key) { Instance.Config.Migrate(e, OldCfg, section, key); }

        // first-person offset slider (m): (2.3.5) [Debug] <key>; carried over from the older [Debug] <oldKey> / [CAMERA] <key>
        static ConfigEntry<float> Slider(ModuleConfig cfg, string sub, string key, string oldKey)
        {
            var e = cfg.Tool(sub, key, 0f, "First-person view offset (m, view right / up / forward): the body moves the opposite way under the camera.", new AcceptableValueRange<float>(-0.3f, 0.3f));
            cfg.Migrate(e, OldCfg, "Debug", oldKey); cfg.Migrate(e, OldCfg, "CAMERA", key); cfg.Migrate(e, OldCfg, "Debug", key);
            if (e.Value < -0.3f || e.Value > 0.3f) e.Value = Mathf.Clamp(e.Value, -0.3f, 0.3f);
            return e;
        }

        // climbing tuning: Apocaclimber [Climbing] <key> -> Apocaplayer [climbing] <key> (2.3.0) -> [Debug] Climb <key> (2.3.5), newest wins
        static ConfigEntry<float> ClimbEntry(ModuleConfig cfg, string sub, string key, float def, string desc, float min, float max)
        {
            var e = cfg.Tool(sub, key, def, desc, new AcceptableValueRange<float>(min, max));
            cfg.Migrate(e, ClimberCfg, "Climbing", key); cfg.Migrate(e, OldCfg, "climbing", key); cfg.Migrate(e, OldCfg, "Debug", "Climb " + key);
            return e;
        }

        // old KeyCode values (LeftAlt, KeypadMinus, Alpha1 ...) -> InputSystem Key, once (first WW run)
        static void MigKey(ConfigEntry<Key> e, string section, string key) { MigrateKey(e, section, key, Legacy.FirstRunMain); }
        static void MigKeyTool(ConfigEntry<Key> e, string section, string key) { MigrateKey(e, section, key, Legacy.FirstRunTools); }
        static void MigrateKey(ConfigEntry<Key> e, string section, string key, bool firstRun)
        {
            if (!firstRun) return;
            string raw = Legacy.Get(OldCfg, section, key);
            if (raw == null) return;
            Key k;
            if (!KeyMap.FromKeyCodeName(raw, out k)) { Core.Log.Warn("[PlayerBody] could not migrate key [" + section + "] " + key + " = " + raw); return; }
            e.Value = k;
            Core.Log.Info("[PlayerBody] migrated " + OldCfg + " [" + section + "] " + key + " = " + raw + " -> " + k);
        }

        // ================================================================== lifecycle
        static readonly AnimationService _service = new AnimationService();
        static Coroutine _eof;
        static Core.Runner _eofHost;
        internal static Job IgnitionJob;

        public override void Init()
        {
            Instance = this;
            Dir = base.Dir;
            DataDir = Assets.ModuleConfigDir(Id);
            MigrateSidecars();
            Running = true;
            BodyRunner.ResetAll();
            ChaseCameraCompat.Init(this);

            // data (was Plugin.Awake): ignition row keys, weapon poses, aim lift, climbing clips, female pain sounds
            IgnitionControl.Load(LegacyIgnitionKey());
            GunPose.Bind(Path.Combine(Paths.ConfigPath, OldCfg));
            AimLift.Load(DataDir);
            ClimbingController.Init();
            Scheduler.StartCoroutine(FemalePain.LoadClips());

            // shared hooks (Core dispatchers) and module patches
            Hooks.GetButtonDownUpdate.Prefix(this, ThirdPerson.BeforeGetButtonDown);
            Hooks.ActivateGameObjectDo.Prefix(this, ThirdPerson.BeforeActivateGameObject);
            Hooks.CreateObjectEnter.Postfix(this, Projectiles.AfterCreateObject);
            Hooks.SmoothLookAtDo.Prefix(this, FirstPersonSway.BeforeSmoothLookAt);
            Core.Patches.Patch(this, Core.Patches.Method(typeof(HutongGames.PlayMaker.Actions.MouseLook), "OnUpdate"),
                prefix: Core.Patches.Method(typeof(ThirdPerson), "BeforeMouseLook"));
            Car.Patch(this);
            WheelArms.Patch(this);
            PickAssist.Patch(this);
            AimTransition.Patch(this);
            FemalePain.Patch(this);

            // the old Runner / ClimbRunner ([DefaultExecutionOrder(-1000)]) / ModApiRunner loops, in their old order
            IgnitionJob = Scheduler.Interval(this, "Ignition control", 0.5f, IgnitionControl.Tick);
            Scheduler.EveryFrame(this, "Climbing", ClimbingController.TickInstance, Phase.EarlyUpdate);
            Scheduler.EveryFrame(this, "Climbing (physics)", ClimbingController.FixedTickInstance, Phase.EarlyFixedUpdate);
            Scheduler.EveryFrame(this, "Body update", BodyRunner.Tick, Phase.Update);
            Scheduler.EveryFrame(this, "Body late", BodyRunner.LateTick, Phase.LateUpdate);
            Scheduler.EveryFrame(this, "ModAPI characters", BodyRunner.ModApiTick, Phase.LateUpdate);
            Scheduler.EveryFrame(this, "PlayerBody.WheelArms", BodyRunner.WheelTick, Phase.LateUpdateLast);
            Scheduler.Gui(this, "Weapon pose hint + vehicle HUD", BodyRunner.Gui, BodyRunner.GuiWanted);   // (WW perf) OnGUI only while one of them shows
            StartEndOfFrame();

            // keys polled in context (third person / climbing / driving); registered so [Keys] conflicts are reported
            foreach (var k in new[] { ObserveKey, ClimbKey, CassetteKey, VolumeDownKey, VolumeUpKey }) Hotkeys.Register(this, k, NoOp, Never);

            Services.Animation = _service;
            MultiplayerAvatars.Init(this);
            Core.Log.Always("[PlayerBody] Player body & animation (Apocaplayer " + SourceVersion + ") ready; assets in " + Dir);
        }

        // the WaitForEndOfFrame loop lives on Core's runner; started again if Core had to recreate the runner
        static void StartEndOfFrame()
        {
            if (_eof != null && _eofHost != null && _eofHost == Core.Runner.Instance) return;
            _eofHost = Core.Runner.Instance;
            _eof = Scheduler.StartCoroutine(BodyRunner.EndOfFrameLoop());
        }

        static void NoOp() { }
        static bool Never() { return false; }

        public override void Shutdown()
        {
            MultiplayerAvatars.Shutdown();
            ChaseCameraCompat.Clear();
            Services.Animation = null;
            if (_eof != null && _eofHost != null && _eofHost == Core.Runner.Instance) Scheduler.StopCoroutine(_eof);
            _eof = null; _eofHost = null; IgnitionJob = null;
            try { GunPose.Flush(); AimLift.SaveIfDirty(); } catch (Exception e) { Warn("saving poses: " + e.Message); }
            try { ClimbingController.StopInstance(); } catch (Exception e) { Warn("climbing: " + e.Message); }
            try { BodyRunner.TearDown(); } catch (Exception e) { Error(e); }
            Perf.StopGc();
            try { IgnitionControl.Remove(); } catch (Exception e) { Warn("ignition row: " + e.Message); }
            try { AimTransition.RestoreAll(); } catch (Exception e) { Warn("aim transition: " + e.Message); }
            try { ModAPI.OnSceneLoaded(); } catch (Exception e) { Warn("ModAPI: " + e.Message); }
            _service.Clear();
            FirstPersonSway.Clear();
            ThirdPerson.ClearCaches();
            Running = false;
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartEndOfFrame();
            BodyRunner.OnSceneLoaded();
            ModAPI.OnSceneLoaded();
            _service.Clear();
            ThirdPerson.ClearCaches();
            FirstPersonSway.Sweep();
            AimTransition.Sweep();
        }

        // the climb path is kept in world space for the duration of a climb: move it with the world (else a cancel would put the player
        // back 1000 m away). The death ragdoll is a scene root object, which the NWH FloatingOrigin moves itself; everything else is
        // re-derived from the Player / camera every frame (ThirdPerson moves its cached view along already).
        public override void OnOriginShift(Vector3 delta) { ClimbingController.ShiftInstance(delta); }

        // ------------------------------------------------------------------ data files
        static readonly string[] Sidecars = { "weapon-poses.txt", "aim-lift.txt", "ignition-key.txt", "legacy-base-lines.txt", "weapon-grips.cfg", "BuiltinPoses.generated.cs" };
        static void MigrateSidecars()
        {
            string old = Path.Combine(Paths.ConfigPath, "Apocaplayer");
            if (!Directory.Exists(old)) return;
            foreach (var f in Sidecars)
            {
                string src = Path.Combine(old, f), dst = Path.Combine(DataDir, f);
                if (File.Exists(dst) || !File.Exists(src)) continue;
                try { File.Copy(src, dst); Core.Log.Always("[PlayerBody] migrated " + src + " -> " + dst); }
                catch (Exception e) { Warn("could not copy " + src + ": " + e.Message); }
            }
        }

        // the old [VEHICLE] / [General] IgnitionKey (removed from Apocaplayer's cfg in 2.3.2): only used when no ignition-key.txt exists
        static KeyCode LegacyIgnitionKey()
        {
            string raw = Legacy.Get(OldCfg, "VEHICLE", "IgnitionKey");
            if (raw == null || raw == "E") raw = Legacy.Get(OldCfg, "General", "IgnitionKey") ?? raw;
            try { return raw != null ? (KeyCode)Enum.Parse(typeof(KeyCode), raw.Trim(), true) : KeyCode.E; } catch { return KeyCode.E; }
        }

        // ================================================================== F7
        public override IEnumerable<ToolPage> ToolPages() { return PlayerBodyPages.Create(this); }
    }
}
