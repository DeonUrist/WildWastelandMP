using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WildWasteland.Core
{
    /// <summary>
    /// The only place that talks to Harmony (AGENTS.md 5.6). Core owns one Harmony id per module
    /// (GUID + "." + module id) so a module can be unpatched cleanly at runtime; the shared dispatchers in <see cref="Hooks"/>
    /// use the plugin GUID itself. Every target is recorded so the startup summary, the F7 overlay and docs/harmony.md
    /// can list it, and a second direct patch of the same target+kind by another module is reported as a conflict
    /// (those targets must go through Hooks instead).
    /// </summary>
    public static class Patches
    {
        public sealed class Record
        {
            public string Owner; public string Target; public string Kind; public string Patch; public bool Ok; public string Error;
            public override string ToString() { return Owner + ": " + Kind + " " + Target + (Ok ? "" : " FAILED " + Error); }
        }

        static readonly Dictionary<string, Harmony> _instances = new Dictionary<string, Harmony>();
        static readonly List<Record> _records = new List<Record>();
        static readonly Dictionary<string, string> _owners = new Dictionary<string, string>(); // target+kind -> owner
        public static int Conflicts;
        public static IEnumerable<Record> Records { get { return _records; } }

        public static Harmony For(IModule m)
        {
            string id = m == null ? Plugin.GUID : Plugin.GUID + "." + m.Id;
            Harmony h;
            if (!_instances.TryGetValue(id, out h)) { h = new Harmony(id); _instances[id] = h; }
            return h;
        }

        internal static void RemoveForeignOwner(string owner)
        {
            var methods = new List<MethodBase>(Harmony.GetAllPatchedMethods());
            foreach (var method in methods)
            {
                var patches = Harmony.GetPatchInfo(method);
                if (patches != null && patches.Owners.Contains(owner)) For(null).Unpatch(method, HarmonyPatchType.All, owner);
            }
        }

        /// <summary>Applies one [HarmonyPatch] class (attributes) for a module. Returns the number of patched methods, -1 on failure.</summary>
        public static int ApplyClass(IModule m, Type patchClass)
        {
            var owner = m == null ? "Core" : m.Id;
            try
            {
                var proc = For(m).CreateClassProcessor(patchClass);
                var patched = proc.Patch();
                int n = 0;
                if (patched != null)
                    foreach (var mb in patched) { n++; Note(owner, mb, "class " + patchClass.Name, "attr", true, null); }
                if (n == 0) Note(owner, null, patchClass.FullName, "attr", false, "no methods patched");
                return n;
            }
            catch (Exception e)
            {
                Note(owner, null, patchClass.FullName, "attr", false, e.Message);
                Log.Error("[" + owner + "] patch class " + patchClass.FullName + " failed", e);
                return -1;
            }
        }

        /// <summary>Manual patch of one target. Each kind that is non-null is recorded. Returns true when Harmony accepted it.</summary>
        public static bool Patch(IModule m, MethodBase original, MethodInfo prefix = null, MethodInfo postfix = null, MethodInfo finalizer = null, MethodInfo transpiler = null)
        {
            var owner = m == null ? "Core" : m.Id;
            if (original == null)
            {
                Note(owner, null, "(null target)", "?", false, "target method not found");
                Log.Warn("[" + owner + "] patch target not found (" + Name(prefix ?? postfix ?? finalizer) + ")");
                return false;
            }
            try
            {
                For(m).Patch(original,
                    prefix: prefix == null ? null : new HarmonyMethod(prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(postfix),
                    transpiler: transpiler == null ? null : new HarmonyMethod(transpiler),
                    finalizer: finalizer == null ? null : new HarmonyMethod(finalizer), ilmanipulator: null);
                if (prefix != null) Note(owner, original, Name(prefix), "prefix", true, null);
                if (postfix != null) Note(owner, original, Name(postfix), "postfix", true, null);
                if (finalizer != null) Note(owner, original, Name(finalizer), "finalizer", true, null);
                if (transpiler != null) Note(owner, original, Name(transpiler), "transpiler", true, null);
                return true;
            }
            catch (Exception e)
            {
                Note(owner, original, Name(prefix ?? postfix ?? finalizer ?? transpiler), "manual", false, e.Message);
                Log.Error("[" + owner + "] patch of " + Describe(original) + " failed", e);
                return false;
            }
        }

        /// <summary>Convenience: find a method by name on a type (any binding) or return null.</summary>
        public static MethodInfo Method(Type t, string name, Type[] args = null)
        {
            if (t == null) return null;
            try { return args == null ? AccessTools.Method(t, name) : AccessTools.Method(t, name, args); }
            catch { return null; }
        }
        public static MethodInfo Method(string typeName, string name, Type[] args = null)
        {
            return Method(AccessTools.TypeByName(typeName), name, args);
        }

        public static void Unpatch(IModule m)
        {
            string id = Plugin.GUID + "." + m.Id;
            Harmony h;
            if (!_instances.TryGetValue(id, out h)) return;
            try { h.UnpatchSelf(); } catch (Exception e) { Log.Error("[" + m.Id + "] unpatch failed", e); }
            for (int i = _records.Count - 1; i >= 0; i--) if (_records[i].Owner == m.Id) _records.RemoveAt(i);
            var dead = new List<string>();
            foreach (var kv in _owners) if (kv.Value == m.Id) dead.Add(kv.Key);
            foreach (var k in dead) _owners.Remove(k);
        }

        static string Name(MethodInfo mi) { return mi == null ? "?" : mi.DeclaringType.Name + "." + mi.Name; }
        public static string Describe(MethodBase mb)
        {
            return mb == null ? "?" : (mb.DeclaringType != null ? mb.DeclaringType.FullName : "") + "." + mb.Name;
        }

        static void Note(string owner, MethodBase target, string patch, string kind, bool ok, string error)
        {
            string t = target == null ? patch : Describe(target);
            _records.Add(new Record { Owner = owner, Target = t, Kind = kind, Patch = patch, Ok = ok, Error = error });
            if (!ok || target == null || kind == "attr") return;
            string key = t + "|" + kind;
            string other;
            if (_owners.TryGetValue(key, out other) && other != owner)
            {
                Conflicts++;
                Log.Error("[Patches] CONFLICT: " + owner + " and " + other + " both " + kind + " " + t + " directly; use a Hooks dispatcher (AGENTS.md 5.6)");
            }
            else _owners[key] = owner;
        }

        public static int Count(string owner)
        {
            int n = 0; foreach (var r in _records) if (r.Owner == owner && r.Ok) n++; return n;
        }
        public static int Failed(string owner)
        {
            int n = 0; foreach (var r in _records) if (r.Owner == owner && !r.Ok) n++; return n;
        }
    }
}
