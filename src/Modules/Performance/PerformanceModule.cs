using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Den.Tools;
using MapMagic.Products;
using MapMagic.Terrains;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;
using Log = WildWasteland.Core.Log;
using Patches = WildWasteland.Core.Patches;

namespace WildWasteland.Modules.Performance
{
    /// <summary>
    /// Vanilla game optimization (issue #133, notes/plan-Performance.md). Every optimization is a <see cref="Feature"/> with its own
    /// Off / On (/ Shadow) switch in tools.cfg ("Performance / ..." sections), edited in Developer. The Performance tab retains
    /// the module switch, preset and measurements. Preset-driven features follow [Performance / Preset] Preset.
    /// </summary>
    public sealed class PerformanceModule : ModuleBase, IWeatherVisualPolicy
    {
        public override string Id { get { return "Performance"; } }
        public override string DisplayName { get { return "Performance & optimization"; } }
        public override bool DefaultEnabled { get { return false; } }

        public static PerformanceModule Instance;
        public static PerfPreset Preset { get { return _preset != null ? _preset.Value : PerfPreset.Vanilla; } }
        static ConfigEntry<PerfPreset> _preset;
        public static ConfigEntry<PerfPreset> PresetEntry { get { return _preset; } }

        readonly List<Feature> _features = new List<Feature>();
        public IList<Feature> Features { get { return _features; } }
        readonly HashSet<string> _patched = new HashSet<string>();
        bool _running;
        public bool Running { get { return _running; } }
        public WeatherVisualDetail WeatherDetail { get { return Preset == PerfPreset.Low ? WeatherVisualDetail.Low : Preset == PerfPreset.Max ? WeatherVisualDetail.Max : WeatherVisualDetail.Balanced; } }

        public PerformanceModule() { Instance = this; }

        public override void BindConfig(ModuleConfig cfg)
        {
            _preset = cfg.Tool("Preset", "Preset", PerfPreset.Vanilla,
                "PERFORMANCE (Low), BALANCED, VISUALS (Max), VANILLA (default). Vanilla preserves original visuals, simulation settings and item cleanup; exact optimizations (no visible change) are not affected. Saved Low/Max values remain compatible.");
            _preset.SettingChanged += (s, a) => { if (_running) ApplyAll(true); };
            DiscoverFeatures();
            foreach (var f in _features)
            {
                f.Module = this;
                string sub = f.Category == Categories.PlayMaker ? "PlayMaker" : f.Category == Categories.Content ? "Game logic" : f.Category == Categories.Rendering ? "Rendering" : "World";
                f.ModeEntry = cfg.Tool(sub, f.Key, f.DefaultMode, f.PlanId + " " + f.Title + ". " + f.Help + (f.SupportsShadow ? " Shadow = vanilla runs, the optimized result is only compared (mismatches shown in Developer)." : ""));
                var captured = f;
                f.ModeEntry.SettingChanged += (s, a) => { if (_running) Apply(captured, true); };
                try { f.Bind(cfg); } catch (Exception e) { f.LastError = e.Message; Log.Error("[Performance] " + f.PlanId + " Bind", e); }
            }
            Telemetry.Bind(cfg);
        }

        void DiscoverFeatures()
        {
            _features.Clear();
            foreach (var t in typeof(PerformanceModule).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(Feature).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                try { _features.Add((Feature)Activator.CreateInstance(t)); }
                catch (Exception e) { Log.Error("[Performance] create " + t.Name, e); }
            }
            _features.Sort((a, b) =>
            {
                int ca = Array.IndexOf(Categories.Order, a.Category), cb = Array.IndexOf(Categories.Order, b.Category);
                if (ca != cb) return ca.CompareTo(cb);
                if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
                return string.CompareOrdinal(a.PlanId, b.PlanId);
            });
        }

        public override void Init()
        {
            _running = true;
            SigmaFsmStates.Install(this);
            Services.WeatherVisualPolicy = this;
            Telemetry.Init(this);
            TerrainTile.OnTileApplied -= TileApplied;
            TerrainTile.OnTileApplied += TileApplied;
            foreach (var f in _features)
            {
                try { f.SubscribeHooks(); } catch (Exception e) { f.LastError = e.Message; Log.Error("[Performance] " + f.PlanId + " hooks", e); }
            }
            // Features touch scene objects and render settings (RenderSettings.sun crashed natively when called from the
            // BepInEx chainloader, before any scene exists): start them on the first loaded scene, or now when switched on in game.
            _deferred = !SceneManager.GetActiveScene().isLoaded;
            if (_deferred) Log.Always("[Performance] waiting for the first scene to start " + _features.Count + " optimizations");
            else StartFeatures();
        }

        bool _deferred;
        void StartFeatures()
        {
            _deferred = false;
            ApplyAll(false);
            int on = 0; foreach (var f in _features) if (f.Running != FeatureMode.Off) on++;
            Log.Always("[Performance] started: " + on + "/" + _features.Count + " optimizations active, preset " + Preset);
        }

        public override void Shutdown()
        {
            _running = false;
            if (ReferenceEquals(Services.WeatherVisualPolicy, this)) Services.WeatherVisualPolicy = null;
            _deferred = false;
            TerrainTile.OnTileApplied -= TileApplied;
            for (int i = _features.Count - 1; i >= 0; i--) _features[i].Stop();
            Telemetry.Shutdown();
            FsmRegistry.Unsubscribed();   // Core removes the module's hook handlers after Shutdown
            _patched.Clear();   // Core unpatches the module after Shutdown
        }

        static FeatureMode Wanted(Feature f)
        {
            if (f.ModeEntry == null) return FeatureMode.Off;
            if (f.UsesPreset && Preset == PerfPreset.Vanilla) return FeatureMode.Off;
            return f.ModeEntry.Value;
        }

        void ApplyAll(bool presetChanged)
        {
            foreach (var f in _features)
            {
                var want = Wanted(f);
                if (presetChanged && f.UsesPreset && want != FeatureMode.Off && f.Running != FeatureMode.Off) { SafeSettings(f); continue; }
                Apply(f, false);
            }
        }

        internal void Apply(Feature f, bool log)
        {
            if (!_running || _deferred) return;
            var want = Wanted(f);
            if (f.Running == want) return;
            f.Stop();
            if (want != FeatureMode.Off) f.Start(want);
            if (log) Log.Always("[Performance] " + f.PlanId + " " + f.Key + " -> " + f.Running);
        }

        internal void SettingsChanged(Feature f)
        {
            if (!_running || f.Running == FeatureMode.Off) return;
            SafeSettings(f);
        }

        static void SafeSettings(Feature f)
        {
            try { f.OnSettingsChanged(); } catch (Exception e) { f.LastError = e.Message; Log.Error("[Performance] " + f.PlanId + " settings", e); }
        }

        internal bool EnsurePatched(Type patchClass)
        {
            if (_patched.Contains(patchClass.FullName)) return true;
            int n = Patches.ApplyClass(this, patchClass);
            if (n > 0) { _patched.Add(patchClass.FullName); return true; }
            return false;
        }

        internal bool EnsurePatched(MethodBase original, MethodInfo prefix, MethodInfo postfix, MethodInfo transpiler, MethodInfo finalizer)
        {
            if (original == null) return false;
            string key = original.DeclaringType.FullName + "::" + original + "|" + (prefix != null ? prefix.DeclaringType.FullName + prefix.Name : "") + (postfix != null ? postfix.DeclaringType.FullName + postfix.Name : "")
                + (transpiler != null ? transpiler.DeclaringType.FullName + transpiler.Name : "") + (finalizer != null ? finalizer.DeclaringType.FullName + finalizer.Name : "");
            if (_patched.Contains(key)) return true;
            if (Patches.Patch(this, original, prefix, postfix, finalizer, transpiler)) { _patched.Add(key); return true; }
            return false;
        }

        void Each(Action<Feature> a, string what)
        {
            foreach (var f in _features)
            {
                if (f.Running == FeatureMode.Off) continue;
                try { a(f); } catch (Exception e) { f.LastError = e.Message; Log.Error("[Performance] " + f.PlanId + " " + what, e); }
            }
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Telemetry.OnSceneLoaded();
            if (_deferred) { StartFeatures(); return; }   // features start against this scene: no separate OnSceneLoaded
            Each(f => f.OnSceneLoaded(), "OnSceneLoaded");
        }
        public override void OnWorldReady() { Each(f => f.OnWorldReady(), "OnWorldReady"); }
        public override void OnOriginShift(Vector3 delta) { Each(f => f.OnOriginShift(delta), "OnOriginShift"); }
        public override void OnSaveBegin(string slot) { Telemetry.Mark("save"); Each(f => f.OnSaveBegin(), "OnSaveBegin"); }
        public override void OnSaveEnd(string slot) { Each(f => f.OnSaveEnd(), "OnSaveEnd"); }
        public override void OnLoadBegin(string slot) { Telemetry.Mark("load"); }

        /// <summary>MapMagic applied a tile (main or draft): features that edit spawned objects re-run on that tile.</summary>
        public static event Action<TerrainTile> TileAppliedEvent;
        void TileApplied(TerrainTile tile, TileData data, StopToken stop)
        {
            Telemetry.Mark("tile");
            var h = TileAppliedEvent;
            if (h == null) return;
            foreach (Action<TerrainTile> d in h.GetInvocationList())
            {
                try { d(tile); } catch (Exception e) { Log.Error("[Performance] tile handler", e); }
            }
        }

        public override IEnumerable<ToolPage> ToolPages() { return null; }   // the Performance tab is registered by Core (CorePages)
    }
}
