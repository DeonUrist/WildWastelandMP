using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildWasteland.Core
{
    public enum ModuleState { NotStarted, Running, OffByConfig, OffConflict, InitFailed, DisabledAfterErrors, HostAuthority, OffMultiplayer }

    /// <summary>
    /// Owns every module's lifecycle (AGENTS.md 5.3): config binding, conflict check against the standalone mods it
    /// replaces (5.8), Init, runtime enable/disable from the F6 menu, hook forwarding, and failure isolation
    /// (a module that keeps throwing is shut down instead of throwing every frame).
    /// </summary>
    public static class ModuleHost
    {
        public sealed class Entry
        {
            public IModule Module;
            public ModuleConfig Config;
            public ModuleState State;
            public string Conflict;      // name of the conflicting standalone plugin
            public int Errors;
            public string LastError;
            internal float WindowStart; internal int WindowErrors; internal int Logged;
        }

        public enum Event { WorldReady, SaveBegin, SaveEnd, LoadBegin }

        static readonly List<Entry> _entries = new List<Entry>();
        static readonly Dictionary<IModule, Entry> _byModule = new Dictionary<IModule, Entry>();
        public static IList<Entry> Entries { get { return _entries; } }
        public static Entry Of(IModule m) { Entry e; return m != null && _byModule.TryGetValue(m, out e) ? e : null; }
        public static bool IsRunning(IModule m) { var e = Of(m); return e != null && e.State == ModuleState.Running; }

        const int ErrorWindowLimit = 40;      // errors per 10 s that disable a module
        static bool _quitting;

        public static void Add(IModule m)
        {
            var e = new Entry { Module = m, State = ModuleState.NotStarted };
            _entries.Add(e); _byModule[m] = e;
        }

        /// <summary>Binds every module's config (Enabled first in its section). Order = display order in Apocasetter.</summary>
        internal static void BindAll(ConfigFile main, ConfigFile tools)
        {
            foreach (var e in _entries)
            {
                var m = e.Module;
                var cfg = new ModuleConfig(m.Id, main, tools);
                cfg.Enabled = main.Bind(m.Id, "Enabled", m.DefaultEnabled,
                    m.DisplayName + ": master switch for this whole feature group. Turning it off at runtime stops it immediately (patches removed); turning it on starts it again.");
                e.Config = cfg;
                var mb = m as ModuleBase; if (mb != null) mb.Config = cfg;
                try { m.BindConfig(cfg); }
                catch (Exception ex) { Log.Error("[" + m.Id + "] BindConfig failed", ex); e.State = ModuleState.InitFailed; e.LastError = ex.Message; }
                var captured = e;
                cfg.Enabled.SettingChanged += (s, a) => OnEnabledChanged(captured);
            }
        }

        internal static void StartAll()
        {
            foreach (var e in _entries)
            {
                if (e.State == ModuleState.InitFailed) continue;
                string conflict = FindConflict(e.Module);
                if (conflict != null)
                {
                    e.State = ModuleState.OffConflict; e.Conflict = conflict;
                    Log.Warn("[" + e.Module.Id + "] waiting for standalone replacement: " + conflict + ".");
                    continue;
                }
                if (!e.Config.Enabled.Value) { e.State = ModuleState.OffByConfig; continue; }
                Start(e);
            }
            Hooks.ApplyUsed();
            Hotkeys.CheckConflicts();
        }

        static string FindConflict(IModule m)
        {
            var c = m.ConflictsWith;
            if (c == null) return null;
            foreach (var guid in c)
            {
                PluginInfo info;
                if (Chainloader.PluginInfos.TryGetValue(guid, out info) && !ReferenceEquals(info.Instance, null))
                    return info.Metadata.Name + " " + info.Metadata.Version + " (" + System.IO.Path.GetFileName(info.Location) + "): " + ReplacementGuard.Status(guid);
            }
            return null;
        }

        static void Start(Entry e)
        {
            var m = e.Module;
            try
            {
                if (m.Id == "Performance" && Multiplayer.SessionActive) { e.State = ModuleState.OffMultiplayer; return; }
                if (Multiplayer.Client && Multiplayer.HostOnly(m))
                {
                    var presentation = m as IMultiplayerPresentation;
                    if (presentation != null) presentation.InitClient();
                    e.State = ModuleState.HostAuthority; return;
                }
                m.Init();
                SetActive(m, true);
                e.State = ModuleState.Running;
                e.WindowErrors = 0;
                Log.Always("[" + m.Id + "] started (" + Patches.Count(m.Id) + " patches" + (Patches.Failed(m.Id) > 0 ? ", " + Patches.Failed(m.Id) + " FAILED" : "") + ")");
            }
            catch (Exception ex)
            {
                e.State = ModuleState.InitFailed; e.LastError = ex.Message;
                Log.Error("[" + m.Id + "] Init failed - module stays off (switch it off and on in F6 / the F7 Modules page to retry)", ex);
                // a half-run Init may have set its service field / Running flag / created objects: Shutdown undoes that (modules keep
                // Shutdown safe to call after a partial Init), then Core removes patches, jobs, hooks and hotkeys
                try { m.Shutdown(); } catch (Exception sx) { Log.Error("[" + m.Id + "] Shutdown after the failed Init", sx); }
                Cleanup(m);
            }
        }

        static void Stop(Entry e, ModuleState newState)
        {
            var m = e.Module;
            if (e.State == ModuleState.HostAuthority)
            {
                var presentation = m as IMultiplayerPresentation;
                try { if (presentation != null) presentation.ShutdownClient(); } catch (Exception ex) { Log.Error("[" + m.Id + "] client Shutdown", ex); }
            }
            if (e.State == ModuleState.Running)
            {
                try { m.Shutdown(); } catch (Exception ex) { Log.Error("[" + m.Id + "] Shutdown", ex); }
            }
            Cleanup(m);
            e.State = newState;
            Log.Always("[" + m.Id + "] stopped (" + newState + ")");
        }

        static void Cleanup(IModule m)
        {
            SetActive(m, false);
            Scheduler.RemoveAll(m);
            Hooks.RemoveAll(m);
            Hotkeys.RemoveAll(m);
            Patches.Unpatch(m);
        }

        static void SetActive(IModule m, bool on) { var mb = m as ModuleBase; if (mb != null) mb.Active = on; }

        static void OnEnabledChanged(Entry e)
        {
            if (_quitting) return;
            bool want = e.Config.Enabled.Value;
            if (want && (e.State == ModuleState.OffByConfig || e.State == ModuleState.OffMultiplayer || e.State == ModuleState.DisabledAfterErrors || e.State == ModuleState.InitFailed))
            {
                Start(e);
                Hooks.ApplyUsed();
                if (e.State == ModuleState.Running && GameRefs.InGame) Safe(e, "OnWorldReady", () => e.Module.OnWorldReady());
            }
            else if (!want && (e.State == ModuleState.Running || e.State == ModuleState.HostAuthority)) Stop(e, ModuleState.OffByConfig);
        }

        internal static void MultiplayerChanged()
        {
            foreach (var e in _entries)
            {
                if (e.Module.Id == "Performance")
                {
                    if (Multiplayer.SessionActive && e.State == ModuleState.Running) Stop(e, ModuleState.OffMultiplayer);
                    else if (!Multiplayer.SessionActive && e.State == ModuleState.OffMultiplayer)
                    { e.State = ModuleState.OffByConfig; if (e.Config.Enabled.Value) Start(e); }
                    continue;
                }
                if (!Multiplayer.HostOnly(e.Module)) continue;
                if (Multiplayer.Client && e.State == ModuleState.Running) { Stop(e, ModuleState.OffByConfig); Start(e); }
                else if (!Multiplayer.Client && e.State == ModuleState.HostAuthority)
                {
                    Stop(e, ModuleState.OffByConfig);
                    if (!e.Config.Enabled.Value) { e.State = ModuleState.OffByConfig; continue; }
                    Start(e);
                }
            }
            Hooks.ApplyUsed();
        }

        /// <summary>Restart a running module (used by tools that change Init-time settings).</summary>
        public static void Restart(IModule m)
        {
            var e = Of(m); if (e == null || e.State != ModuleState.Running) return;
            Stop(e, ModuleState.OffByConfig); Start(e); Hooks.ApplyUsed();
        }
        // Modules without a standalone ordering dependency can finish initialization after chainloading.
        internal static bool DeferredInit(IModule module, Action initialize)
        {
            var entry = Of(module); if (entry == null || entry.State != ModuleState.Running) return false;
            string conflict = FindConflict(module);
            if (conflict != null)
            {
                foreach (string guid in module.ConflictsWith) ReplacementGuard.LateConflict(guid);
                conflict = FindConflict(module);
                Stop(entry, ModuleState.OffConflict); entry.Conflict = entry.LastError = conflict; Log.Warn("[" + module.Id + "] " + conflict); return false;
            }
            try { initialize(); Hooks.ApplyUsed(); Hotkeys.CheckConflicts(); return true; }
            catch (Exception error)
            {
                Stop(entry, ModuleState.InitFailed); entry.LastError = error.Message;
                Log.Error("[" + module.Id + "] deferred initialization failed", error); return false;
            }
        }

        // ---------------------------------------------------------------- broadcasts

        internal static void SceneLoaded(Scene s, LoadSceneMode mode)
        {
            foreach (var e in _entries) if (e.State == ModuleState.Running) { var en = e; Safe(en, "OnSceneLoaded", () => en.Module.OnSceneLoaded(s, mode)); }
        }

        internal static void Broadcast(Event ev, string slot)
        {
            foreach (var e in _entries)
            {
                if (e.State != ModuleState.Running) continue;
                var m = e.Module;
                switch (ev)
                {
                    case Event.WorldReady: Safe(e, "OnWorldReady", () => m.OnWorldReady()); break;
                    case Event.SaveBegin: Safe(e, "OnSaveBegin", () => m.OnSaveBegin(slot)); break;
                    case Event.SaveEnd: Safe(e, "OnSaveEnd", () => m.OnSaveEnd(slot)); break;
                    case Event.LoadBegin: Safe(e, "OnLoadBegin", () => m.OnLoadBegin(slot)); break;
                }
            }
        }

        internal static void OriginShift(Vector3 delta)
        {
            foreach (var e in _entries) if (e.State == ModuleState.Running) { var en = e; Safe(en, "OnOriginShift", () => en.Module.OnOriginShift(delta)); }
        }

        internal static void Quit()
        {
            if (_quitting) return;
            _quitting = true;
            foreach (var e in _entries) if (e.State == ModuleState.Running || e.State == ModuleState.HostAuthority) Stop(e, ModuleState.NotStarted);
        }

        static void Safe(Entry e, string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { Fail(e, what, ex, true); }
        }

        // ---------------------------------------------------------------- failure isolation

        /// <summary>
        /// A scheduler job that throws is isolated by the Scheduler itself (the job is switched off after repeated errors, the module keeps
        /// running - the source mods' runners also swallowed a subsystem's exception per frame and went on). Job errors are counted and
        /// logged per module but do not disable the module; only hook / lifecycle failures do.
        /// </summary>
        internal static void ReportJobException(Job j, Exception ex) { Fail(Of(j.Owner), "job " + j.Name, ex, false); }
        internal static void ReportHookException(IModule m, string target, Exception ex) { Fail(Of(m), target, ex, true); }

        static void Fail(Entry e, string where, Exception ex, bool countsTowardsDisable)
        {
            if (e == null) { Log.Error("[Core] " + where, ex); return; }
            e.Errors++; e.LastError = where + ": " + ex.Message;
            if (e.Logged < 5) { e.Logged++; Log.Error("[" + e.Module.Id + "] " + where, ex); }
            if (!countsTowardsDisable) return;
            float now = Time.unscaledTime;
            if (now - e.WindowStart > 10f) { e.WindowStart = now; e.WindowErrors = 0; }
            if (++e.WindowErrors >= ErrorWindowLimit && e.State == ModuleState.Running)
            {
                Log.Error("[" + e.Module.Id + "] " + e.WindowErrors + " errors in 10 s - module DISABLED for this session (last: " + e.LastError + "). Re-enable it in the F6 menu or the F7 window to retry.");
                Stop(e, ModuleState.DisabledAfterErrors);
            }
        }

        // ---------------------------------------------------------------- summary

        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.Append("Wild Wasteland ").Append(Plugin.VERSION).Append(" modules:");
            foreach (var e in _entries)
            {
                sb.Append("\n  ").Append(e.Module.Id).Append(" (").Append(e.Module.DisplayName).Append("): ");
                switch (e.State)
                {
                    case ModuleState.Running: sb.Append("on"); break;
                    case ModuleState.OffByConfig: sb.Append("off by config"); break;
                    case ModuleState.OffConflict: sb.Append("off: standalone mod present - ").Append(e.Conflict); break;
                    case ModuleState.InitFailed: sb.Append("off: init failed - ").Append(e.LastError); break;
                    default: sb.Append(e.State); break;
                }
                int p = Patches.Count(e.Module.Id), f = Patches.Failed(e.Module.Id);
                if (p > 0 || f > 0) sb.Append(", patches ").Append(p).Append(f > 0 ? " (" + f + " failed)" : "");
            }
            sb.Append("\n  core ").Append(Hooks.Summary());
            if (Patches.Conflicts > 0) sb.Append("\n  PATCH CONFLICTS: ").Append(Patches.Conflicts);
            return sb.ToString();
        }
    }
}
