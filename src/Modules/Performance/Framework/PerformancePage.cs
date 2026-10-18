using System;
using UnityEngine;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.Performance
{
    /// <summary>
    /// The separate "Performance" tab of the settings window (between Modules and Debug; registered by Core's CorePages).
    /// The module switch, preset and measurements stay here. Individual optimization modes, stats and tuning live in Developer.
    /// </summary>
    public sealed class PerformancePage : ToolPage
    {
        static PerformancePage _instance;
        public static PerformancePage Instance { get { return _instance ?? (_instance = new PerformancePage()); } }
        bool _tuning;

        PerformancePage() { Group = ToolGroups.General; Title = "Performance"; ShowWhenModuleOff = true; }

        static readonly string[] _modes2 = { "Off", "On" };
        static readonly string[] _modes3 = { "Off", "On", "Shadow" };
        static readonly string[] _presets = { "PERFORMANCE", "BALANCED", "VISUALS", "VANILLA" };
        static readonly PerfPreset[] _presetValues = { PerfPreset.Low, PerfPreset.Balanced, PerfPreset.Max, PerfPreset.Vanilla };

        public override void Draw(ToolLayout l)
        {
            if (Multiplayer.Client) return;
            var m = PerformanceModule.Instance;
            if (m == null) { l.Text("Performance module not loaded."); return; }
            var entry = ModuleHost.Of(m);
            if (Multiplayer.SessionActive) l.Text("Performance optimizations are locked off during multiplayer. Measurements remain available.", Ledger.Danger);
            if (entry != null && entry.Config != null) l.Toggle(entry.Config.Enabled, "Performance optimizations");
            bool running = ModuleHost.IsRunning(m);
            l.Text(running
                ? "Experimental base-game optimizations. Choose a preset and compare measurements in the same scene. Changes apply immediately."
                : "The Performance module is off: the game runs unmodified.", running ? (Color?)null : Ledger.Danger);

            var p = PerformanceModule.PresetEntry;
            if (p != null)
            {
                int sel = l.Choices("Preset", Array.IndexOf(_presetValues, p.Value), _presets);
                if (_presetValues[sel] != p.Value) p.Value = _presetValues[sel];
                l.Text("PERFORMANCE favors speed; BALANCED is the compromise; VISUALS favors detail. VANILLA (default) disables changes to visuals, simulation and item cleanup, including extra culling (marked *). Exact optimizations remain active.");
            }

            l.Heading("Measurements");
            Telemetry.Draw(l);
            l.Heading("Performance and jobs");
            CorePages.Performance(l);
        }

        internal void DrawDeveloper(ToolLayout l)
        {
            if (Multiplayer.Client) return;
            var m = PerformanceModule.Instance;
            if (m == null) { l.Text("Performance module not loaded."); return; }
            bool running = ModuleHost.IsRunning(m);
            l.Text("Individual optimization modes, feature statistics and tuning overrides. Shadow keeps the original game code running and compares the optimized result. Presets and overall measurements are on the Performance tab.");
            string cat = null;
            foreach (var f in m.Features)
            {
                if (f.Category != cat) { cat = f.Category; l.Heading(cat); }
                var labels = f.SupportsShadow ? _modes3 : _modes2;
                int cur = (int)f.ModeEntry.Value; if (cur >= labels.Length) cur = 1;
                string state = f.Running == FeatureMode.Off ? (f.ModeEntry.Value != FeatureMode.Off && running && f.UsesPreset && PerformanceModule.Preset == PerfPreset.Vanilla ? "  (Vanilla preset)" : "") : "";
                int next = l.Choices(f.PlanId + "  " + f.Title + (f.UsesPreset ? " *" : ""), cur, labels);
                if (next != cur) f.ModeEntry.Value = (FeatureMode)next;
                if (!string.IsNullOrEmpty(f.Help) || state.Length > 0) l.Text(f.Help + state);
                if (f.LastError != null) l.Text("Error: " + f.LastError, Ledger.Danger);
                if (f.Mismatches > 0) l.Text("Shadow mismatches: " + f.Mismatches + " (see log)", Ledger.Danger);
                if (f.Running != FeatureMode.Off)
                {
                    try { f.DrawStats(l); } catch (Exception e) { l.Text("Stats error: " + e.Message, Ledger.Danger); }
                }
            }

            l.Space(10);
            _tuning = l.Toggle("Show tuning overrides (-1 = follow preset)", _tuning);
            if (_tuning)
                foreach (var f in m.Features)
                    foreach (var k in f.Knobs) l.Entry(k.Override);
        }
    }
}
