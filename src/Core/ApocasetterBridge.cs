using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace WildWasteland.Core
{
    /// <summary>
    /// The only file that talks to Apocasetter (soft dependency, AGENTS.md 5.9). Everything is resolved by reflection on
    /// assembly name "Apocasetter" and cached; WW works without it.
    /// </summary>
    public static class ApocasetterBridge
    {
        public const string GUID = "com.denis.apocalypter.apocasetter";
        static bool _resolved;
        static MethodInfo _set;
        static FieldInfo _active, _font;
        // (WW perf) InputBlocker.Active compiled once into a typed getter (it is read every frame by Hotkeys and by input hooks:
        // FieldInfo.GetValue boxed a bool per call). Falls back to reflection if the delegate can't be built or fails once.
        static Func<bool> _activeGet;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Assembly asm = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) if (a.GetName().Name == "Apocasetter") { asm = a; break; }
                if (asm == null) return;
                var ib = asm.GetType("Apocasetter.InputBlocker");
                if (ib != null) { _set = ib.GetMethod("Set", BindingFlags.Static | BindingFlags.Public); _active = ib.GetField("Active", BindingFlags.Static | BindingFlags.Public); }
                if (_active != null && _active.FieldType == typeof(bool))
                {
                    try { _activeGet = System.Linq.Expressions.Expression.Lambda<Func<bool>>(System.Linq.Expressions.Expression.Field(null, _active)).Compile(); _activeGet(); }
                    catch (Exception) { _activeGet = null; }
                }
                var gs = asm.GetType("Apocasetter.GameSkin");
                if (gs != null) _font = gs.GetField("GameFont", BindingFlags.Static | BindingFlags.Public);
                var ui = asm.GetType("Apocasetter.SettingsUI");
                var toggle = ui != null ? ui.GetMethod("Toggle", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null) : null;
                var open = ui != null ? ui.GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic) : null;
                if (_set != null) Patches.Patch(null, _set, postfix: Patches.Method(typeof(ApocasetterBridge), nameof(AfterInputBlock)));
                if (ui != null && open != null && open.FieldType == typeof(bool)) Patches.Patch(null, ui.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic), postfix: Patches.Method(typeof(ApocasetterBridge), nameof(AfterSettingsUpdate)));
                if (toggle != null && toggle.ReturnType == typeof(void) && open != null && open.FieldType == typeof(bool))
                    Patches.Patch(null, toggle, prefix: Patches.Method(typeof(ApocasetterBridge), nameof(BeforeMenuToggle)));
                else Log.Warn("Apocasetter bridge: loading menu guard unavailable; SettingsUI.Toggle/_open not found");
            }
            catch (Exception e) { Log.Warn("Apocasetter bridge: " + e.Message); }
        }

        public static bool Present { get { Resolve(); return _set != null; } }

        private static bool BeforeMenuToggle(bool ____open) { return ____open || !LoadingProgress.MenusBlocked; }
        private static void AfterInputBlock() { if (MultiplayerSaving.Frozen) Time.timeScale = 0f; else if (Multiplayer.WorldReady) Time.timeScale = 1f; }
        private static void AfterSettingsUpdate(bool ____open) { if (____open) AfterInputBlock(); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool QueueDisable(PluginInfo info, out string error)
        {
            error = null;
            try
            {
                PluginInfo setter;
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out setter) || ReferenceEquals(setter.Instance, null))
                { error = "Apocasetter is not loaded"; return false; }
                var assembly = setter.Instance.GetType().Assembly;
                var updates = assembly.GetType("Apocasetter.Updates");
                var catalog = assembly.GetType("Apocasetter.Catalog");
                var installed = updates != null ? updates.GetField("InstallerPresent", BindingFlags.Public | BindingFlags.Static) : null;
                var stage = updates != null ? updates.GetMethod("Stage", BindingFlags.Public | BindingFlags.Static) : null;
                var opFor = updates != null ? updates.GetMethod("OpFor", BindingFlags.Public | BindingFlags.Static) : null;
                var find = catalog != null ? catalog.GetMethod("Find", BindingFlags.Public | BindingFlags.Static) : null;
                if (installed == null || stage == null || opFor == null || find == null || !(bool)installed.GetValue(null))
                { error = "Apocasetter installer/API is missing"; return false; }
                var pending = opFor.Invoke(null, new object[] { info.Metadata.GUID }) as Dictionary<string, object>;
                object operation;
                if (pending != null && pending.TryGetValue("op", out operation) && ((string)operation == "disable" || (string)operation == "remove"))
                    return DisableSaved(assembly, info.Metadata.GUID, out error);
                object entry = find.Invoke(null, new object[] { info.Metadata.GUID });
                var pathsMethod = entry != null ? entry.GetType().GetMethod("PluginPaths", BindingFlags.Public | BindingFlags.Instance) : null;
                var paths = pathsMethod != null ? pathsMethod.Invoke(entry, null) as IEnumerable : null;
                if (paths == null) { error = "Apocasetter could not locate the plugin files"; return false; }
                string plugins = Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string ownDll = Path.GetFullPath(info.Location);
                foreach (var other in Chainloader.PluginInfos.Values)
                    if (other != info && !string.IsNullOrEmpty(other.Location)
                        && string.Equals(Path.GetFullPath(other.Location), ownDll, StringComparison.OrdinalIgnoreCase))
                    { error = "the DLL also contains another plugin"; return false; }
                var files = new List<object>();
                foreach (object item in paths)
                {
                    string path = Path.GetFullPath((string)item).TrimEnd('\\', '/');
                    if (!path.StartsWith(plugins, StringComparison.OrdinalIgnoreCase))
                    { error = "plugin path is outside BepInEx/plugins"; return false; }
                    bool shared = false;
                    foreach (var other in Chainloader.PluginInfos.Values)
                    {
                        if (other == info || string.IsNullOrEmpty(other.Location)) continue;
                        string location = Path.GetFullPath(other.Location);
                        if (string.Equals(location, path, StringComparison.OrdinalIgnoreCase))
                        { error = "the DLL also contains another plugin"; return false; }
                        if (location.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) shared = true;
                    }
                    // Keep shared folders; move only the replaced plugin's DLL in that case.
                    if (shared) path = ownDll;
                    if (!files.Contains(path)) files.Add(path);
                }
                if (files.Count == 0) { error = "no plugin files found"; return false; }
                stage.Invoke(null, new object[] { new Dictionary<string, object> {
                    { "op", "disable" }, { "guid", info.Metadata.GUID }, { "name", info.Metadata.Name },
                    { "version", info.Metadata.Version.ToString() }, { "paths", files }, { "removeConfig", false }
                } });
                return DisableSaved(assembly, info.Metadata.GUID, out error);
            }
            catch (Exception e) { error = (e.InnerException ?? e).Message; return false; }
        }

        // Updates.Stage logs disk-write failures rather than throwing; only promise a restart after the operation is saved.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool DisableSaved(Assembly assembly, string guid, out string error)
        {
            error = "Apocasetter could not save the disable operation";
            string path = Path.Combine(Paths.CachePath, "Apocasetter", "pending.json");
            if (!File.Exists(path)) return false;
            var json = assembly.GetType("Apocasetter.MiniJson");
            var parse = json != null ? json.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static) : null;
            var root = parse != null ? parse.Invoke(null, new object[] { File.ReadAllText(path) }) as Dictionary<string, object> : null;
            object value;
            if (root == null || !root.TryGetValue("ops", out value)) return false;
            var operations = value as IEnumerable;
            if (operations == null) return false;
            foreach (object item in operations)
            {
                var op = item as Dictionary<string, object>;
                object id, kind;
                if (op != null && op.TryGetValue("guid", out id) && string.Equals(id as string, guid, StringComparison.OrdinalIgnoreCase)
                    && op.TryGetValue("op", out kind) && ((kind as string) == "disable" || (kind as string) == "remove"))
                { error = null; return true; }
            }
            return false;
        }

        /// <summary>True while the Apocasetter Mods window blocks game input.</summary>
        public static bool InputBlocked
        {
            get
            {
                Resolve();
                var g = _activeGet;
                if (g != null) { try { return g(); } catch { _activeGet = null; } }
                try { return _active != null && (bool)_active.GetValue(null); } catch { return false; }
            }
        }

        /// <summary>Returns true when the blocker was changed (Apocasetter present).</summary>
        public static bool SetInputBlock(bool on)
        {
            Resolve();
            if (_set == null) return false;
            try { _set.Invoke(null, new object[] { on }); return true; }
            catch (Exception e) { Log.Warn("Apocasetter InputBlocker: " + e.Message); return false; }
        }

        /// <summary>The game's menu font as captured by Apocasetter (null until its MODS button was cloned). Polled lazily.</summary>
        public static Font GameFont
        {
            get { Resolve(); try { return _font != null ? _font.GetValue(null) as Font : null; } catch { return null; } }
        }
    }
}
