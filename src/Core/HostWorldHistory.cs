using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ES3Internal;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // Adapted from Apocasaver 2.0.4 WorldHistory: capture before terrain pooling, reset templates before restore.
    // Only immutable ES3 records are retained; no unloaded scene objects are pinned by the history ledger.
    internal static class HostWorldHistory
    {
        const string CaptureFile = "wwmp-location-history.es3";
        static readonly Dictionary<string, ES3Data> History = new Dictionary<string, ES3Data>(), Delta = new Dictionary<string, ES3Data>();
        static readonly Dictionary<string, PlayMakerFSM> Templates = new Dictionary<string, PlayMakerFSM>();
        static Func<object, Dictionary<string, ES3Data>> _data;
        static Func<object, IList> _instances;
        static Func<object, object> _prototype;
        static Func<object, GameObject> _prefab;
        static Func<object, bool> _dirty;
        static ES3File _restoreCache;
        static string _slot, _error;
        static int _generation = -1;
        static bool _running, _restoring;
        internal static void Install()
        {
            SaveTerrain.Install();
            _data = MultiplayerAccess.Get<Dictionary<string, ES3Data>>(typeof(ES3File), "cache");
            _dirty = MultiplayerAccess.Get<bool>(typeof(ES3File), "dirty");
            var tile = typeof(MapMagic.Terrains.TerrainTile); var pool = AccessTools.TypeByName("Den.Tools.ObjectsPool+Pool");
            if (pool == null) throw new MissingMemberException("Terrain object pool unavailable");
            _instances = MultiplayerAccess.Get<IList>(pool, "instances"); _prototype = MultiplayerAccess.Get<object>(pool, "prototype");
            var prototype = AccessTools.Field(pool, "prototype"); if (prototype == null) throw new MissingFieldException("Terrain pool prototype unavailable");
            _prefab = MultiplayerAccess.Get<GameObject>(prototype.FieldType, "prefab");
            foreach (string name in new[] { "Move", "Remove", "ResetTerrain" }) Require(tile, name, "BeforeTile");
            Require(pool, "ClampCount", "BeforeClamp");
            if (!Patches.Patch(null, AccessTools.Method(pool, "MoveItems"), prefix: AccessTools.Method(typeof(HostWorldHistory), "BeforePool"), postfix: AccessTools.Method(typeof(HostWorldHistory), "AfterPool"))) throw new InvalidOperationException("Terrain history placement hook failed");
            SaveBus.StateChanged += StateChanged;
        }
        static void Require(Type type, string name, string before)
        { if (!Patches.Patch(null, AccessTools.Method(type, name), prefix: AccessTools.Method(typeof(HostWorldHistory), before))) throw new InvalidOperationException("Terrain history hook failed: " + name); }
        static void StateChanged(string previous, string current)
        { if (current == "LoadGame" || current == "setSeed" || current == "Start") { History.Clear(); Delta.Clear(); Templates.Clear(); _restoreCache = null; _slot = null; _generation = -1; } }
        static bool Session()
        {
            try { return OpenSession(); }
            catch (Exception error) { Failed(error); return false; }
        }
        static void Failed(Exception error)
        { HostSaveSystem.ReloadRequired = true; HostSaveSystem.Status = "Location history unavailable; reload before saving: " + error.Message; Log.Error("[System] " + HostSaveSystem.Status, error); }
        static bool OpenSession()
        {
            if (!HostSaveSystem.LocalReady || _running) return false;
            string slot = HostSaveSystem.ActiveFile;
            if (_slot == slot && _generation == SaveBus.Generation) return true;
            History.Clear(); Delta.Clear(); Templates.Clear(); _restoreCache = null; _slot = slot; _generation = SaveBus.Generation;
            ES3File file;
            if (ES3File.cachedFiles.TryGetValue(slot, out file)) foreach (var pair in _data(file)) History[pair.Key] = pair.Value;
            else if (File.Exists(HostSaveFiles.WorldPath(slot))) foreach (var pair in _data(new ES3File(HostSaveFiles.Disk(slot), true))) History[pair.Key] = pair.Value;
            return true;
        }
        static void BeforeTile(Component __instance) { try { if (__instance != null) Capture(__instance.gameObject); } catch (Exception error) { Failed(error); } }
        static void BeforePool(object __instance, int startNum, int endNum)
        {
            try {
            if (!Session() || startNum >= endNum) return;
            var items = _instances(__instance); for (int i = Mathf.Max(0, startNum); i < Mathf.Min(endNum, items.Count); i++) Capture(items[i] as GameObject);
            } catch (Exception error) { Failed(error); }
        }
        static void BeforeClamp(object __instance, int targetCount)
        {
            try {
            if (!Session()) return;
            var items = _instances(__instance); for (int i = Mathf.Max(0, targetCount); i < items.Count; i++) Capture(items[i] as GameObject);
            } catch (Exception error) { Failed(error); }
        }
        static void AfterPool(object __instance, int startNum, int endNum)
        {
            try {
            if (!Session() || startNum >= endNum) return;
            var items = _instances(__instance); var prototype = _prototype(__instance); if (prototype == null) return; var prefab = _prefab(prototype);
            for (int i = Mathf.Max(0, startNum); i < Mathf.Min(endNum, items.Count); i++) Restore(items[i] as GameObject, prefab);
            } catch (Exception error) { Failed(error); }
        }
        static bool Event(PlayMakerFSM fsm, string name)
        { foreach (var transition in fsm.Fsm.GlobalTransitions) if (transition.FsmEvent.Name == name) return true; return false; }
        static List<PlayMakerFSM> SiteFsms(GameObject root)
        {
            var result = new List<PlayMakerFSM>();
            foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm == null || !fsm.enabled || !fsm.Fsm.Initialized || !Event(fsm, "Save")) continue;
                bool saves = false;
                foreach (var state in fsm.FsmStates) foreach (var action in state.Actions) if (action is ES3PlayMaker.Save || action is ES3PlayMaker.SaveAll) saves = true;
                if (saves) result.Add(fsm);
            }
            return result;
        }
        static void Capture(GameObject root)
        {
            if (root == null || !Session()) return;
            var fsms = SiteFsms(root); if (fsms.Count == 0) return;
            Run(fsms, false);
            if (HostSaveSystem.Busy) SaveBus.ReportFailure("Terrain locations moved during the world save");
        }
        static void Restore(GameObject root, GameObject prefab)
        {
            if (root == null || prefab == null || !Session()) return;
            var fsms = SiteFsms(root);
            for (int i = fsms.Count - 1; i >= 0; i--)
            {
                var fsm = fsms[i]; if (!Event(fsm, "Load")) { fsms.RemoveAt(i); continue; }
                string relative = ""; for (var t = fsm.transform; t != null && t != root.transform; t = t.parent) relative = "/" + t.name + relative;
                string key = prefab.GetInstanceID() + relative + "/" + fsm.FsmName; PlayMakerFSM template;
                if (!Templates.TryGetValue(key, out template) || !ReferenceEquals(template, null) && template == null)
                {
                    var target = relative.Length == 0 ? prefab.transform : prefab.transform.Find(relative.TrimStart('/'));
                    template = target == null ? null : FsmUtil.Find(target.gameObject, fsm.FsmName); Templates[key] = template;
                }
                if (template == null) continue;
                foreach (var v in fsm.FsmVariables.IntVariables) { var original = template.FsmVariables.FindFsmInt(v.Name); if (original != null) v.Value = original.Value; }
                foreach (var v in fsm.FsmVariables.FloatVariables) { var original = template.FsmVariables.FindFsmFloat(v.Name); if (original != null) v.Value = original.Value; }
                foreach (var v in fsm.FsmVariables.BoolVariables) { var original = template.FsmVariables.FindFsmBool(v.Name); if (original != null) v.Value = original.Value; }
            }
            if (fsms.Count != 0) Run(fsms, true);
        }
        static void Run(List<PlayMakerFSM> fsms, bool restore)
        {
            string path = ES3Settings.defaultSettings.path; var location = ES3Settings.defaultSettings.location;
            try
            {
                _running = true; _restoring = restore; _error = null;
                ES3File file;
                if (restore && _restoreCache != null && !_dirty(_restoreCache)) file = _restoreCache;
                else
                {
                    file = new ES3File(new ES3Settings(CaptureFile) { location = ES3.Location.Cache }, false);
                    if (restore) { foreach (var pair in History) _data(file)[pair.Key] = pair.Value; _restoreCache = file; }
                }
                var records = _data(file);
                ES3File.cachedFiles[CaptureFile] = file;
                ES3Settings.defaultSettings.path = CaptureFile; ES3Settings.defaultSettings.location = ES3.Location.Cache;
                foreach (var fsm in fsms) fsm.SendEvent(restore ? "Load" : "Save");
                if (_error != null) throw new IOException(_error);
                if (!restore) { foreach (var pair in records) { History[pair.Key] = pair.Value; Delta[pair.Key] = pair.Value; } _restoreCache = null; }
            }
            catch (Exception error) { HostSaveSystem.ReloadRequired = true; HostSaveSystem.Status = "Location history could not be preserved. Reload before saving: " + error.Message; Log.Error("[System] " + HostSaveSystem.Status, error); }
            finally { ES3Settings.defaultSettings.path = path; ES3Settings.defaultSettings.location = location; ES3File.cachedFiles.Remove(CaptureFile); _running = _restoring = false; }
        }
        internal static bool NativeError(string error)
        {
            if (!_running) return false;
            // Missing records on a newly generated site use the reset prefab defaults.
            if (_restoring && error.IndexOf("KeyNotFoundException", StringComparison.Ordinal) >= 0) return true;
            _error = error; return false;
        }
        internal static void Merge()
        {
            if (HostSaveSystem.ReloadRequired) throw new IOException("Location history requires a reload");
            ES3File file;
            if (Delta.Count == 0 || !ES3File.cachedFiles.TryGetValue(HostSaveSystem.ActiveFile, out file)) return;
            var records = _data(file); foreach (var pair in Delta) records[pair.Key] = pair.Value;
            // HostSaveSystem.Stamp follows this call and marks the same cache dirty.
        }
        internal static void Accepted()
        {
            ES3File file; if (!ES3File.cachedFiles.TryGetValue(HostSaveSystem.ActiveFile, out file)) return;
            History.Clear(); foreach (var pair in _data(file)) History[pair.Key] = pair.Value; Delta.Clear(); _restoreCache = null;
        }
    }
}
