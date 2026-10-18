using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.NPC
{
    // NPC Overhaul (NPCAI 1.9.15) as a Wild Wasteland module: senses / detection, structure navigation (map bakes + routing), the brain,
    // idle behaviour, animals (burrowing scorpions, pouncing spiders, dogs, Dog Companion compatibility), hazards (spider nests and webs,
    // sandstorms) and the debug overlay. Every subsystem is still a static class with its own registry; the old NPCAI.Runner's Update /
    // LateUpdate / OnGUI are scheduler jobs registered in the old order (Senses -> Nav -> Brain -> Idle -> Passthrough -> Burrow -> Pounce ->
    // Webs -> Nests -> Ambush -> DogBite; late: Burrow -> Pounce -> DogBite), and the old Harmony patches are Core hooks or module patches.
    public sealed partial class NpcModule : ModuleBase
    {
        public const string GUID = "com.denis.apocalypter.npcai";      // the standalone mod this module replaces
        public override string Id { get { return "NPC"; } }
        public override string DisplayName { get { return "NPC AI & navigation"; } }
        public override string[] ConflictsWith { get { return new[] { GUID }; } }

        internal static NpcModule Instance;
        /// <summary>The module runs (Init done, Shutdown not yet). Every hook / patch handler and every On property checks it first.</summary>
        internal static bool Running;
        internal static readonly LogAdapter Log = new LogAdapter();
        internal static string ModelDir;       // ex Plugin.Dir + "Models": BepInEx\plugins\WildWasteland\NPC\Models
        internal static string DataDir;        // ex BepInEx\config\NPCAI: BepInEx\config\WildWasteland\NPC
        internal static string LegacyDataDir { get { return Path.Combine(Paths.ConfigPath, "NPCAI"); } }

        internal static void Verbose(string s) { if (VerboseLog != null && VerboseLog.Value) Core.Log.Always("[NPC] " + s); else Core.Log.Info("[NPC] " + s); }
        internal static void Warn(string s) { Core.Log.Warn("[NPC] " + s); }

        /// <summary>Plugin.Log of NPCAI: info lines are written when the module's own VerboseLog or the global one is on.</summary>
        internal sealed class LogAdapter
        {
            public void LogInfo(string s) { if (VerboseLog != null && VerboseLog.Value) Core.Log.Always("[NPC] " + s); else if (Core.Log.Verbose) Core.Log.Info("[NPC] " + s); }
            public void LogWarning(string s) { Core.Log.Warn("[NPC] " + s); }
            public void LogError(string s) { Core.Log.Error("[NPC] " + s); }
        }

        Job _overlay;
        bool _subscribed;

        // ------------------------------------------------------------------ lifecycle

        public override void Init()
        {
            try { Start(); }
            catch (Exception)
            {
                // a half-started module must not leave Services.Npc / event handlers / changed NPCs behind (Core only removes patches, jobs, hooks)
                try { Shutdown(); } catch (Exception) { }
                throw;
            }
        }

        void Start()
        {
            Instance = this;
            ModelDir = Path.Combine(Dir, "Models");
            DataDir = Assets.ModuleConfigDir(Id);
            ResetAll();                                    // re-runnable after Shutdown: every registry starts empty
            MigrateData();

            Services.Npc = new NpcApi();
            Services.CrewBailedOut += OnCrewBailedOut;     // ex Senses.HookApocapatrol (Harmony on Apocapatrol.Patrol.BailOut / Explode.Blast)
            Services.VehicleExploded += OnVehicleExploded;
            SaveBus.StateChanged += Senses.OnSaveLoadState;   // ex Senses.Persist.Tick polling the SaveLoadGame FSM
            _subscribed = true;

            InstallHooks();
            InstallPatches();
            DogAnimations.Initialize(this);                // bundle + its own hooks / patch (only when the bundle loads)
            DogCompanionCompat.Initialize(this);              // only when Dog Companion is installed; [NPC] DogCompanionCompat switches it live
            WomenFeature.Initialize(this);
            InitNetworkInput();

            // the old NPCAI.Runner, in its order (load-bearing: Brain/Idle body hand-over, DogAnimations restore in Update / sample in LateUpdate)
            Scheduler.EveryFrame(this, "Senses", Senses.Tick);
            Scheduler.EveryFrame(this, "Nav", Nav.Tick);
            Scheduler.EveryFrame(this, "Brain", Brain.Tick);
            Scheduler.Sliced(this, "NPC.Rigs", 1000, Brain.BuildRig, Phase.LateUpdate);
            Scheduler.EveryFrame(this, "Idle", Idle.Tick);
            Scheduler.Interval(this, "Passthrough", 0.25f, Passthrough.Tick, Phase.Update, true);
            Scheduler.EveryFrame(this, "Burrow", Burrow.Tick);
            Scheduler.EveryFrame(this, "Pounce", Pounce.Tick);
            Scheduler.EveryFrame(this, "Webs", Webs.Tick);
            Scheduler.EveryFrame(this, "Nests", Nests.Tick);
            Scheduler.Interval(this, "Ambush", 0.5f, Ambush.Tick, Phase.Update, true);
            Scheduler.EveryFrame(this, "DogBite", DogBite.Tick);
            Scheduler.Interval(this, "Aim.Sweep", 10f, Aim.Sweep);
            Scheduler.Interval(this, "WeaponRanges.Sweep", 30f, WeaponRanges.Sweep);
            Scheduler.Interval(this, "Seated perception cleanup", 1f, Senses.SweepObservers);
            Scheduler.EveryFrame(this, "Burrow.Late", Burrow.LateTick, Phase.LateUpdate);
            Scheduler.EveryFrame(this, "Pounce.Late", Pounce.LateTick, Phase.LateUpdate);
            Scheduler.EveryFrame(this, "DogBite.Late", DogBite.LateTick, Phase.LateUpdate);
            _overlay = Scheduler.Gui(this, "Overlay", DebugOverlay.OnGUI, OverlayWanted);   // (WW perf) the job is also disabled while ShowNavigation is off
            _overlay.Enabled = ShowNav.Value;
            ShowNav.SettingChanged += OnShowNavChanged;

            Hotkeys.Register(this, ShoutKey, Senses.ShoutPressed, Senses.ShoutAllowed);

            Running = true;
            Log.LogInfo("NPC Overhaul 1.9.15 port started");
        }

        static bool OverlayWanted() { return Running; }   // = DebugOverlay.OnGUI's own gate (it also prunes Nav's stale markers, so no finer test)
        void OnShowNavChanged(object s, EventArgs a) { if (_overlay != null) _overlay.Enabled = ShowNav.Value; }

        public override void Shutdown()
        {
            StopNetworkInput();
            // first: every handler / On property sees the module off (FSM events sent below re-enter our hooks synchronously)
            Running = false;
            try { ShowNav.SettingChanged -= OnShowNavChanged; } catch (Exception) { }
            if (_subscribed)
            {
                Services.CrewBailedOut -= OnCrewBailedOut;
                Services.VehicleExploded -= OnVehicleExploded;
                SaveBus.StateChanged -= Senses.OnSaveLoadState;
                _subscribed = false;
            }
            if (Services.Npc is NpcApi) Services.Npc = null;
            Step("Women of Wasteland", WomenFeature.Shutdown);
            // hand every NPC back to the game, in reverse dependency order
            Step("Pounce", Pounce.Shutdown);
            Step("Burrow", Burrow.Shutdown);
            Step("DogBite", DogBite.Shutdown);          // + DogAnimations + Dog Companion drivers
            Step("Nests", Nests.Shutdown);
            Step("Webs", Webs.Shutdown);
            Step("Passthrough", Passthrough.Shutdown);
            Step("Idle", Idle.Shutdown);
            Step("Brain", Brain.Shutdown);
            Step("Aim", Aim.Shutdown);
            Step("VanillaEvents", VanillaEvents.Shutdown);
            Step("Senses", Senses.Shutdown);
            ResetAll();
            _overlay = null;
            Instance = null;
        }

        static void Step(string what, Action a)
        {
            try { a(); } catch (Exception e) { Core.Log.Error("[NPC] shutdown " + what, e); }
        }

        /// <summary>Clears every registry / cache (the old Plugin.SceneLoaded chain). Init and Shutdown call it too.</summary>
        static void ResetAll()
        {
            Aim.OnSceneLoaded(); WeaponRanges.OnSceneLoaded(); VanillaEvents.OnSceneLoaded(); Brain.OnSceneLoaded(); Senses.OnSceneLoaded(); Nav.OnSceneLoaded(); Idle.OnSceneLoaded();
            Passthrough.OnSceneLoaded(); Burrow.OnSceneLoaded(); Pounce.OnSceneLoaded(); Webs.OnSceneLoaded(); Nests.OnSceneLoaded(); Ambush.OnSceneLoaded(); DogBite.OnSceneLoaded();
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode) { ResetAll(); Gungirl.OnSceneLoaded(); }
        public override void OnWorldReady() { Gungirl.BeginScan(); }

        // ------------------------------------------------------------------ floating origin
        // The game's FloatingOrigin moves the root objects of every loaded scene by delta (not DontDestroyOnLoad). NPCAI kept scene-space
        // positions across frames; they are translated here (plain Vector3 / Bounds / float data) or dropped where a rebuild is cheap
        // (route caches). See notes/port-NPC.md "Origin shift".
        public override void OnOriginShift(Vector3 delta)
        {
            Step("Senses.Shift", () => Senses.Shift(delta));
            Step("Nav.Shift", () => Nav.Shift(delta));
            Step("Brain.Shift", () => Brain.Shift(delta));
            Step("Idle.Shift", () => Idle.Shift(delta));
            Step("Burrow.Shift", () => Burrow.Shift(delta));
            Step("Pounce.Shift", () => Pounce.Shift(delta));
            Step("Nests.Shift", () => Nests.Shift(delta));
            Step("Webs.Shift", () => Webs.Shift(delta));
        }

        // ------------------------------------------------------------------ Patrols events (ex Apocapatrol postfixes)
        static void OnCrewBailedOut(GameObject car, GameObject pax, GameObject npc) { if (Running) Senses.AfterBailOut(car, pax, npc); }
        static void OnVehicleExploded(GameObject car) { if (Running) Senses.AfterBlast(car); }

        // ------------------------------------------------------------------ Harmony
        void InstallHooks()
        {
            // SendEvent.OnEnter: Brain (Animal_Run while holding / Animal_rotateRandom) then Aim (Activate = the burst) - NPCAI's patch order
            Hooks.SendEventEnter.Prefix(this, Brain.BeforeSendEvent);
            Hooks.SendEventEnter.Prefix(this, Aim.BeforeSendEvent);
            // SetVelocity.DoSetVelocity: Brain (the pedal; registers NPCs) then DogAnimations (held / dashing dogs; only with the bundle, see DogAnimations.Initialize)
            Hooks.SetVelocityDo.Prefix(this, Brain.BeforeSetVelocity);
            // CreateObject.OnEnter postfixes: Senses (structures, pending rigs, blasts), Idle (camp spawn points), Pounce (carcass of a leaping spider)
            Hooks.CreateObjectEnter.Postfix(this, Senses.AfterCreateObject);
            Hooks.CreateObjectEnter.Postfix(this, Idle.AfterCreateObject);
            Hooks.CreateObjectEnter.Postfix(this, Pounce.AfterCreateObject);
            // SetFsmFloat.OnEnter postfixes: vanilla hits -> Senses.Hurt, then a real dog bite -> DogBite's lunge
            Hooks.SetFsmFloatEnter.Postfix(this, VanillaEvents.AfterDamage);
            Hooks.SetFsmFloatEnter.Postfix(this, DogBite.AfterSetFsmFloat);
            // the shout modifier blocks the game's Kick / ShiftDown buttons
            Hooks.GetButtonDownUpdate.Prefix(this, Senses.BeforeGetButtonDown);
            Hooks.GetButtonDo.Prefix(this, Senses.BeforeGetButton);
            Hooks.GetButtonUpUpdate.Prefix(this, Senses.BeforeGetButtonUp);
            // Raycast: DoRaycast = the Attack FSM's bumper rays (Brain), OnEnter = the player's vanilla shot (VanillaEvents)
            Hooks.RaycastDo.Prefix(this, Brain.BeforeRaycast);
            Hooks.RaycastEnter.Prefix(this, VanillaEvents.BeforePlayerShot);
            Hooks.SmoothLookAtDo.Prefix(this, Brain.BeforeSmoothLookAt);
            Hooks.ProcessEvent.Prefix(this, Brain.BeforeDrivingHide);
            Hooks.ActivateGameObjectEnter.Postfix(this, Brain.AfterActivate);
            // SensorToolkit: the Detection FSM (registers agents), the burst LOS, the NPC burst ray (prefix + finalizer restores the ray length)
            Hooks.SensorDetectionsDo.Prefix(this, Senses.BeforeGetDetections);
            Hooks.LosEnter3D.Prefix(this, Senses.BeforeLosResult);
            Hooks.LosUpdate3D.Prefix(this, Senses.BeforeLosResult);
            Hooks.SensorRayHitEnter.Prefix(this, VanillaEvents.BeforeNpcShot);
            Hooks.SensorRayHitEnter.Finalizer(this, VanillaEvents.AfterNpcShot);
        }

        void InstallPatches()
        {
            // targets only NPC patches: module patches (listed in the F7 "Harmony patches" page)
            Patch(typeof(Rotate), "DoRotate", typeof(Brain), "BeforeRotate", true);
            Patch(typeof(LookAt), "DoLookAt", typeof(Brain), "BeforeLookAt", true);
            Patch(typeof(AddForce), "DoAddForce", typeof(Brain), "BeforeAddForce", true);
            Patch(typeof(AudioPlay), "OnEnter", typeof(Senses), "BeforeAudioPlay", true);
            Patch(typeof(RandomWait), "OnEnter", typeof(Aim), "BeforeRandomWait", true);
            var multi = AccessTools.TypeByName("HutongGames.PlayMaker.Actions.ActivateGameObjects");      // optional (not in every PlayMaker build)
            if (multi != null) Patch(multi, "OnEnter", typeof(Brain), "AfterActivate", false);
        }

        internal bool Patch(Type target, string method, Type patchType, string patchMethod, bool prefix)
        {
            var original = AccessTools.Method(target, method);
            var mi = AccessTools.Method(patchType, patchMethod);
            return Core.Patches.Patch(this, original, prefix: prefix ? mi : null, postfix: prefix ? null : mi);
        }

        // ------------------------------------------------------------------ data files (ex BepInEx\config\NPCAI)
        /// <summary>First run: copy the old NPCAI data (senses sidecars, nest layouts) into config\WildWasteland\NPC. The old folder is still read as a fallback.</summary>
        static void MigrateData()
        {
            try
            {
                string old = LegacyDataDir;
                if (!Directory.Exists(old)) return;
                int n = 0;
                string oldSaves = Path.Combine(old, "Saves"), newSaves = Path.Combine(DataDir, "Saves");
                if (Directory.Exists(oldSaves))
                    foreach (var f in Directory.GetFiles(oldSaves, "*.senses.txt"))
                    {
                        string dst = Path.Combine(newSaves, Path.GetFileName(f));
                        if (File.Exists(dst)) continue;
                        Directory.CreateDirectory(newSaves);
                        File.Copy(f, dst); n++;
                    }
                string oldLayouts = Path.Combine(old, "nest-layouts.txt"), newLayouts = Path.Combine(DataDir, "nest-layouts.txt");
                if (File.Exists(oldLayouts) && !File.Exists(newLayouts)) { File.Copy(oldLayouts, newLayouts); n++; }
                if (n > 0) Core.Log.Always("[NPC] migrated " + n + " data file(s) from " + old + " to " + DataDir);
            }
            catch (Exception e) { Core.Log.Warn("[NPC] data migration from config\\NPCAI failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ F7
        public override IEnumerable<ToolPage> ToolPages() { return NpcPages.Build(this); }
    }
}
