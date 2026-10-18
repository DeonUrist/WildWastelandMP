using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.Patrols
{
    // Patrols, traffic & War Rig (ex Apocapatrol 2.14.4 + War Rig 1.1.3 + the debug MerchantSpotter 1.0.2 plugin).
    // AI-driven raider cars: templates assembled from the game's own frames and parts, live crews with a driving AI, ranged
    // passengers, human turrets, ram damage, loot trucks, a convoy spawner driven by the game's Distance Travelled, persistent world
    // traffic (camps, routes, faction battles, merchants), the editors, and the War Rig tanker trailer. Save data stays verbatim
    // (ES3 key "Apocapatrol.Patrol", schema-14 sidecar, motorcycle / War Rig ES3 prefab ids).
    //
    // Runner: the old Apocapatrol.Runner MonoBehaviours (Patrol, TemplateMenu, TemplateExporter, TurretSpotter, BusPassengerPoseEditor,
    // Convoy, WorldTraffic, Cleanup + the MerchantEditor) sit on a module-owned child of Core.Runner.Host; their Update / LateUpdate /
    // OnGUI are scheduler jobs registered in that order (PORTING.md pattern 2). Per-object components (Crew, Pilot, Pose, ...) keep their
    // Unity loops and stand down while Running is false.
    public sealed partial class PatrolsModule : ModuleBase
    {
        public override string Id { get { return "Patrols"; } }
        public override string DisplayName { get { return "Patrols, traffic & War Rig"; } }
        static readonly string[] Conflicts = { "com.denis.apocalypter.apocapatrol", "com.denis.apocalypter.warrig", "com.denis.apocalypter.merchantspotter" };
        public override string[] ConflictsWith { get { return Conflicts; } }

        internal const string NAME = "Wild Wasteland Patrols";
        internal static string VERSION { get { return WildWasteland.Plugin.VERSION; } }

        internal static PatrolsModule Instance;
        // false = the module is off: every per-object loop (crews, pilots, poses, turrets, merchants, rigs) returns at once
        internal static bool Running;
        // BepInEx\plugins\WildWasteland\Patrols (templates, layouts, poses, textures, sounds, War Rig models)
        internal static string DataDir = ".";

        static bool _paintPreloaded;
        GameObject _host;
        Patrol _patrol; TemplateMenu _menu; TemplateExporter _exporter; TurretSpotter _spotter; BusPassengerPoseEditor _busPose;
        Convoy _convoy; WorldTraffic _traffic; Cleanup _cleanup; MerchantEditor _merchantEditor;
        SaveBus.StateChange _onSaveState;

        internal TemplateMenu Menu { get { return _menu; } }
        internal TemplateExporter Exporter { get { return _exporter; } }
        internal TurretSpotter Spotter { get { return _spotter; } }
        internal BusPassengerPoseEditor BusPose { get { return _busPose; } }
        internal MerchantEditor MerchantSpot { get { return _merchantEditor; } }

        // ---------------------------------------------------------------- lifecycle

        public override void Init()
        {
            try { InitInner(); }
            catch { Shutdown(); throw; }   // a failed start leaves nothing running (ModuleHost then removes hooks / jobs / patches)
            Running = true;   // last: per-object loops only run once everything is in place
            Services.FollowerVehicles = new QuestFollowers();
            Services.Camps = new TrafficCamps.Status();
            LoadingProgress.Register(this, VehicleBuildCache.LoadWork);
        }

        void InitInner()
        {
            Instance = this;
            DataDir = Dir;
            try { Directory.CreateDirectory(DataDir); } catch (Exception e) { Log.Warn("[Patrols] data folder: " + e.Message); }
            MigrateData();

            // sidecars: BepInEx\config\WildWasteland\Patrols\Saves (+ the old BepInEx\config\Apocapatrol\Saves as a read fallback), road cache
            var cfgDir = Assets.ModuleConfigDir(Id);
            PatrolPersistence.SavesDir = Path.Combine(cfgDir, "Saves");
            PatrolPersistence.LegacySavesDir = Path.Combine(Path.Combine(Paths.ConfigPath, "Apocapatrol"), "Saves");
            TrafficRoads.CacheDir = Path.Combine(cfgDir, "roads");
            TrafficRoads.LegacyCacheDir = Path.Combine(Paths.ConfigPath, "Apocapatrol.roads");

            // hooks (Core dispatchers) and module patches, each in its own try like the old Awake
            Try("Traffic roads", TrafficRoads.Install);
            Scheduler.Sliced(this, "Traffic final terrain", 500, TrafficRoads.GroundStep);
            Try("Camp status", () => TrafficCamps.Install(this));
            Try("Seated LOS guard", () => SeatedLosGuard.Install(this));
            Try("Suspension", () => Suspension.Install(this));
            Prefabs.Install(this);
            PatrolLoadPreparation.Install(this);
            Core.Hooks.SetFsmFloatEnter.Postfix(this, Ram.AfterSetFsmFloat);
            Core.Hooks.SetFsmFloatDo.Prefix(this, Ram.BeforeSetFsmFloat);   // a car never rams its own (trigger-only) crew
            Core.Hooks.SendEventEnter.Prefix(this, Ram.BeforeSendEvent);
            Ram.StartNetwork();
            Try("Motorcycle save hook (saved motorcycles may not load)", () => MotorcycleIntegration.Install(this));

            Paint.Init(DataDir);
            EditorStore.Load(DataDir);
            CarTemplates.Init(DataDir);
            if (!_paintPreloaded) { _paintPreloaded = true; Paint.Preload(); }     // all Textures decoded now, not in the middle of a spawn
            CarTemplates.Load();
            Try("War Rig", () => WarRig.WarRigFeature.Install(this, Path.Combine(DataDir, "WarRig")));

            // per-scene state (fresh start, or a restart after Shutdown)
            Patrol.ResetForScene(); PlayerRef.Reset(); PatrolPersistence.ResetForScene(); PickupCatalog.Invalidate(); ExitSpeed.Restore();
            PatrolPersistence.Install();
            _onSaveState = PatrolPersistence.OnStateChanged;
            SaveBus.StateChanged += _onSaveState;

            EnsureRunner();
            RegisterJobs();
            RegisterHotkeys();
            Log.Always("[Patrols] " + DisplayName + " ready (" + CarTemplates.Summary + ")");
        }

        public override void Shutdown()
        {
            Ram.StopNetwork();
            WorldTraffic.StopNetwork();
            Running = false;
            LoadingProgress.Remove(this);
            Try("build queue", BuildQueue.Reset);
            Try("vehicle build cache", VehicleBuildCache.Reset);
            PatrolLoadPreparation.Reset();
            if (Services.FollowerVehicles is QuestFollowers) Services.FollowerVehicles = null;
            if (Services.Camps is TrafficCamps.Status) Services.Camps = null;
            TrafficCompass.Clear();
            Try("foot patrols", WorldTraffic.ReleaseFootAll);
            if (_onSaveState != null) { SaveBus.StateChanged -= _onSaveState; _onSaveState = null; }
            Try("persistence flush", PatrolPersistence.Uninstall);
            Try("road cache flush", () => { TrafficRoads.Flush(); TrafficRoads.Uninstall(); });
            Try("War Rig", WarRig.WarRigFeature.Uninstall);
            Try("exit speed", ExitSpeed.Restore);
            Try("coroutines", StopObjectCoroutines);
            PatrolPersistence.Runner = null;
            // destroying the runner object closes the editors (their OnDestroy releases the pause), the spot / merchant markers and the
            // debug-map textures; its coroutines (builds, restores, traffic materialisation) stop with it
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null; _patrol = null; _menu = null; _exporter = null; _spotter = null; _busPose = null; _convoy = null; _traffic = null; _cleanup = null; _merchantEditor = null;
            BusPassengerPoseEditor.Target = null;
            ModVehicles.Clear();
            Prefabs.Invalidate();
            Instance = null;
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Ram.SceneReset();
            TrafficCompass.Clear();
            PickupCatalog.Invalidate(); PatrolVisibility.Reset(); EnsureRunner(); Patrol.ResetForScene(); PlayerRef.Reset(); PatrolPersistence.ResetForScene(); Convoy.ResetForScene();
            WarRig.WarRigFeature.OnSceneLoaded();
        }

        // Core.Origin (NWH FloatingOrigin.Jump): legacy physics caches of the pilots and PlayerRef move with the world
        public override void OnOriginShift(Vector3 delta) { TrafficSpace.OriginShift(delta); }
        public override void OnWorldReady() { Prefabs.Refresh(); WarRig.PoiEncounter.Prepare(); }
        internal void SpawnPoi(CarTemplate template, Vector3 position, Quaternion rotation, Action<GameObject> onDone)
        { if (_patrol != null && Running) _patrol.SpawnAt(template, position, rotation, true, onDone); else onDone(null); }

        // ---------------------------------------------------------------- runner object + jobs

        void EnsureRunner()
        {
            // the game destroys the plugin's GameObject on scene load; the logic lives on a child of Core's hidden runner object
            if (_host != null) return;
            var parent = Core.Runner.Host;
            _host = new GameObject("WW.Patrols") { hideFlags = HideFlags.HideAndDontSave };
            if (parent != null) _host.transform.SetParent(parent.transform, false); else UnityEngine.Object.DontDestroyOnLoad(_host);
            _patrol = _host.AddComponent<Patrol>();
            _menu = _host.AddComponent<TemplateMenu>();
            _exporter = _host.AddComponent<TemplateExporter>();
            _spotter = _host.AddComponent<TurretSpotter>();
            _busPose = _host.AddComponent<BusPassengerPoseEditor>();
            _convoy = _host.AddComponent<Convoy>();
            _traffic = _host.AddComponent<WorldTraffic>();
            _cleanup = _host.AddComponent<Cleanup>();
            _merchantEditor = _host.AddComponent<MerchantEditor>();
            PatrolPersistence.Runner = _patrol;
        }

        // the old runner components' order: Patrol, TemplateMenu, TemplateExporter, TurretSpotter, BusPassengerPoseEditor, Convoy,
        // WorldTraffic, Cleanup (then the absorbed MerchantSpotter editor). Each job re-reads the component (recreated after a scene load).
        void RegisterJobs()
        {
            WorldTraffic.StartNetwork(this, false);
            Scheduler.Interval(this, "Patrols.Prefab cache", .5f, Prefabs.Refresh).RunNow();
            Scheduler.Sliced(this, "Patrols.Build", 1000, BuildQueue.Step);
            Scheduler.Sliced(this, "Patrols.Vehicle build cache", 500, VehicleBuildCache.Step);
            TrafficCompass.Clear();
            Scheduler.Interval(this, "Compass markers", .5f, TrafficCompass.Tick);
            Scheduler.EveryFrame(this, "Patrol.Tick", () => { if (_patrol != null) _patrol.Tick(); });
            Scheduler.EveryFrame(this, "TemplateMenu.Tick", () => { if (_menu != null) _menu.Tick(); });
            Scheduler.EveryFrame(this, "TemplateExporter.Tick", () => { if (_exporter != null) _exporter.Tick(); });
            Scheduler.EveryFrame(this, "TurretSpotter.Tick", () => { if (_spotter != null) _spotter.Tick(); });
            Scheduler.EveryFrame(this, "BusPassengerPoseEditor.Tick", () => { if (_busPose != null) _busPose.Tick(); });
            Scheduler.EveryFrame(this, "Convoy.Tick", () => { if (_convoy != null) _convoy.Tick(); });
            Scheduler.EveryFrame(this, "WorldTraffic.Tick", () => { if (_traffic != null) _traffic.Tick(); });
            Scheduler.EveryFrame(this, "Cleanup.Tick", () => { if (_cleanup != null) _cleanup.Tick(); });
            Scheduler.EveryFrame(this, "MerchantEditor.Tick", () => { if (_merchantEditor != null) _merchantEditor.Tick(); });

            Scheduler.EveryFrame(this, "TemplateMenu.LateTick", () => { if (_menu != null) _menu.LateTick(); }, Phase.LateUpdate);
            Scheduler.EveryFrame(this, "TemplateExporter.LateTick", () => { if (_exporter != null) _exporter.LateTick(); }, Phase.LateUpdate);

            // (WW perf) each with a predicate (built once here): IMGUI stops when none of them (and no other module) has anything to draw
            Scheduler.Gui(this, "Patrol.Gui", () => { if (_patrol != null) _patrol.Gui(); }, () => _patrol != null && Patrol.GuiWanted());
            Scheduler.Gui(this, "TemplateMenu.Gui", () => { if (_menu != null) _menu.Gui(); }, () => _menu != null && _menu.IsOpen);
            Scheduler.Gui(this, "TemplateExporter.Gui", () => { if (_exporter != null) _exporter.Gui(); }, () => _exporter != null && _exporter.GuiWanted());
            Scheduler.Gui(this, "TurretSpotter.Gui", () => { if (_spotter != null) _spotter.Gui(); }, () => _spotter != null && _spotter.GuiWanted());
            Scheduler.Gui(this, "BusPassengerPoseEditor.Gui", () => { if (_busPose != null) _busPose.Gui(); }, () => _busPose != null && BusPassengerPoseEditor.Active);
            Scheduler.Gui(this, "WorldTraffic.Gui", () => { if (_traffic != null) _traffic.Gui(); }, () => _traffic != null && _traffic.GuiWanted());
            Scheduler.Gui(this, "MerchantEditor.Gui", () => { if (_merchantEditor != null) _merchantEditor.Gui(); }, () => _merchantEditor != null && MerchantEditor.Enabled);
        }

        void RegisterHotkeys()
        {
            Hotkeys.Register(this, MenuKey, () => OpenEditor(), () => GameRefs.InGame);
            Hotkeys.Register(this, TemplateDumpKey, () => OpenRecorder(), () => GameRefs.InGame);
            Hotkeys.Register(this, BusPassengerPoseEditorKey, () => BusPassengerPoseEditorEnabled.Value = !BusPassengerPoseEditorEnabled.Value);
            Hotkeys.Register(this, MerchantEditorKey, () => { MerchantEditorEnabled.Value = !MerchantEditorEnabled.Value; Log.Info("[Patrols] Merchant editor " + (MerchantEditorEnabled.Value ? "enabled" : "disabled")); });
        }

        internal bool OpenEditor() { return _menu != null && _menu.TryOpen(); }
        internal bool OpenRecorder() { return _exporter != null && _exporter.TryOpen(); }

        // per-object coroutines (overheat, revive, bail-out timers, merchant setup) end with the module; the objects themselves stay
        static void StopObjectCoroutines()
        {
            var types = new[] { typeof(Crew), typeof(Pilot), typeof(PassengerGuard), typeof(RiderGuard), typeof(Pose), typeof(MerchantHost), typeof(MerchantGuardHealth), typeof(TrafficActor) };
            foreach (var t in types)
                foreach (var o in UnityEngine.Object.FindObjectsOfType(t))
                {
                    var mb = o as MonoBehaviour;
                    if (mb != null) mb.StopAllCoroutines();
                }
        }

        static void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Error("[Patrols] " + what + ": " + e); }
        }

        // ---------------------------------------------------------------- small helpers the old Plugin class provided

        internal static void Verbose(string msg) { if (Log.Verbose) Log.Info("[Patrols] " + msg); }

        // the game's menu font as captured by Apocasetter (ex EditorSession.Resolve -> PatrolSkin.SetGameFont): asked when an editor opens or
        // the spawn warning shows, at most once a second while unknown
        static float _nextFontAsk;
        internal static void EnsureGameFont()
        {
            if (Skin.GameFont != null || Time.unscaledTime < _nextFontAsk) return;
            _nextFontAsk = Time.unscaledTime + 1f;
            var f = ApocasetterBridge.GameFont;
            if (f != null) Skin.SetGameFont(f);
        }

        internal static bool Pressed(Key key)
        {
            if (key == Key.None) return false;
            var kb = Keyboard.current;
            if (kb == null) return false;
            try { return kb[key].wasPressedThisFrame; }
            catch (Exception) { return false; }
        }
    }
}
