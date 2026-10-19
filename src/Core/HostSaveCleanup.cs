using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // Only positively identified disposable roots; unknown ownership or references always retain the object.
    internal static class HostSaveCleanup
    {
        internal static ConfigEntry<bool> Enabled, Corpses, Packs;
        internal static ConfigEntry<int> Distance, Minutes;
        static Func<object, IDictionary> _objects;
        static Func<object, ulong> _owner;
        static Func<object, bool> _carried;
        static readonly HashSet<string> Touched = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<GameObject, float> Far = new Dictionary<GameObject, float>();
        static readonly List<GameObject> Candidates = new List<GameObject>(), Removed = new List<GameObject>();
        static readonly HashSet<string> RemovedKeys = new HashSet<string>(StringComparer.Ordinal);
        static readonly List<KeyValuePair<ArrayList, GameObject>> Registrations = new List<KeyValuePair<ArrayList, GameObject>>();
        static int _generation = -1;
        static float _last;
        static object[] _observations = new object[1024];
        static int _observation, _observationCount;
        static float _elapsed;
        static bool _tracking;
        static bool _wasHost;
        static ArrayList _offlineItems;
        static PlayMakerFSM[] _referenceFsms;
        const string ProtectionKey = "WildWastelandMP.CleanupProtected";
        internal static void Install()
        {
            var cfg = Plugin.Settings;
            Enabled = cfg.Bind("System / Cleanup", "Cleanup on save", false, "Remove only selected, positively identified disposable objects after a verified save. Unknown ownership/references always retain objects.");
            Corpses = cfg.Bind("System / Cleanup", "Non-resource zombie remains", false, "Remove abandoned Zombie_Runner_Dead roots only; never resource carcasses, live actors or player bodies.");
            Packs = cfg.Bind("System / Cleanup", "Exhausted disposable packs", false, "Remove empty ammo/battery packs only when they have no remaining sale value and have never been observed in player custody.");
            Distance = cfg.Bind("System / Cleanup", "Minimum distance metres", 1000, new ConfigDescription("Must remain this far from every connected player.", new AcceptableValueRange<int>(1000, 10000)));
            Minutes = cfg.Bind("System / Cleanup", "Abandoned minutes", 10, new ConfigDescription("Continuous active gameplay beyond the minimum distance. Enabling cleanup starts a fresh observation period.", new AcceptableValueRange<int>(10, 600)));
            if (Multiplayer.Present)
            {
                var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
                _objects = MultiplayerAccess.Get<IDictionary>(Multiplayer.Type("Net.WorldObjects"), "objects");
                _owner = MultiplayerAccess.Get<ulong>(tracked, "Owner"); _carried = MultiplayerAccess.Get<bool>(tracked, "Carried");
            }
            Scheduler.Interval(null, "Conservative save cleanup observations", 5f, Tick);
            Scheduler.Sliced(null, "Save cleanup observation slice", 500, ObservationStep);
        }
        static bool Category(GameObject go)
        {
            string name = go.name; int at = name.IndexOf("(Clone)", StringComparison.Ordinal); if (at >= 0) name = name.Substring(0, at);
            bool corpse = Corpses.Value && name == "Zombie_Runner_Dead";
            // Strict native prefab allowlist: a modded container with an Ammo FSM is never presumed disposable.
            bool pack = Packs.Value && (name.StartsWith("ammo_box_", StringComparison.Ordinal) && !name.StartsWith("ammo_box_big_", StringComparison.Ordinal) || name == "flashlight_batteries");
            if (!corpse && !pack) return false;
            var fsms = go.GetComponents<PlayMakerFSM>(); bool empty = false, worthless = false;
            foreach (var fsm in fsms)
            {
                if (fsm.FsmName == "AmmoStorage" || fsm.FsmName == "Food" || fsm.FsmName == "Carcasse" || fsm.FsmName.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0 || fsm.FsmName.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (fsm.FsmName == "Health") { var hp = fsm.FsmVariables.FindFsmFloat("Health"); if (hp != null && hp.Value > 0) return false; }
                var buy = fsm.FsmVariables.FindFsmInt("ForBuyInt"); if (buy != null && buy.Value != 0) return false;
                if (fsm.FsmName == "Ammo" || fsm.FsmName == "Batteries") { var count = fsm.FsmVariables.FindFsmInt(fsm.FsmName == "Ammo" ? "ammo" : "batteries"); empty |= count != null && count.Value <= 0; }
                if (fsm.FsmName == "Value") { var value = fsm.FsmVariables.FindFsmInt("SellValue"); worthless |= value != null && value.Value <= 0; }
            }
            return corpse || empty && worthless;
        }
        static bool Protected(GameObject go)
        {
            return go == null || go.transform.parent != null || go.layer == 22 || !go.activeInHierarchy || Touched.Contains(go.name)
                || MultiplayerRespawn.ProtectedObject(go) || Multiplayer.OwnedElsewhere(go);
        }
        static void Tick()
        {
            if (!HostSaveSystem.LocalReady) return;
            if (_generation != SaveBus.Generation || _wasHost != Multiplayer.Hosting)
            {
                _generation = SaveBus.Generation; _wasHost = Multiplayer.Hosting; Far.Clear(); Touched.Clear(); _last = 0;
                _offlineItems = null;
                if (!Multiplayer.Hosting && GameRefs.NewGOArrayList != null) foreach (var list in GameRefs.NewGOArrayList.GetComponents<PlayMakerArrayListProxy>()) if (list.referenceName == "ArrayList_Items") _offlineItems = list.arrayList;
                Array.Clear(_observations, 0, _observations.Length); _observationCount = 0; _tracking = false;
                try
                {
                    var settings = HostSaveFiles.Disk(HostSaveSystem.ActiveFile);
                    if (System.IO.File.Exists(HostSaveFiles.WorldPath(HostSaveSystem.ActiveFile)) && ES3.KeyExists(ProtectionKey, settings)) foreach (string name in ES3.Load<string[]>(ProtectionKey, settings)) Touched.Add(name);
                    else { SnapshotObjects(); for (int i = 0; i < _observationCount; i++) { var existing = ObservationRoot(_observations[i]); if (existing != null) Touched.Add(existing.name); } }
                }
                catch (Exception error) { Log.Error("[System] Cleanup ownership data unreadable; cleanup disabled for this session", error); Enabled.Value = false; }
            }
            if (!Enabled.Value) { Far.Clear(); _last = 0; _tracking = false; if (_observationCount != 0) Array.Clear(_observations, 0, _observations.Length); _observationCount = 0; return; }
            if (!GameRefs.WorldRunning || HostSaveSystem.Busy || MultiplayerSaving.Frozen) { _last = 0; return; }
            float now = Time.unscaledTime; _elapsed = _last > 0 ? Mathf.Min(5.5f, now - _last) : 0; _last = now;
            var gone = new List<GameObject>(); foreach (var pair in Far) if (pair.Key == null) gone.Add(pair.Key); foreach (var go in gone) Far.Remove(go);
            SnapshotObjects();
            if (!_tracking) { for (int i = 0; i < _observationCount; i++) { var item = ObservationRoot(_observations[i]); if (item != null) Touched.Add(item.name); } _tracking = true; }
        }
        static GameObject ObservationRoot(object state) { return state as GameObject ?? Multiplayer.TrackedObject(state); }
        static void SnapshotObjects()
        {
            ICollection objects = Multiplayer.Hosting ? (ICollection)_objects(null).Values : _offlineItems;
            int count = objects != null ? objects.Count : 0;
            if (_observations.Length < count) _observations = new object[Mathf.NextPowerOfTwo(count)];
            Array.Clear(_observations, 0, _observations.Length); if (objects != null) objects.CopyTo(_observations, 0);
            _observation = 0; _observationCount = count;
        }
        static bool ObservationStep()
        {
            if (!Enabled.Value || !HostSaveSystem.LocalWorld || !GameRefs.WorldRunning || HostSaveSystem.Busy || MultiplayerSaving.Frozen || _observation >= _observationCount) return false;
            object state = _observations[_observation]; _observations[_observation++] = null;
            var go = ObservationRoot(state); if (go == null) return _observation < _observationCount;
            if (Multiplayer.Hosting ? _owner(state) != 0 || _carried(state) : go.transform.parent != null) Touched.Add(go.name);
            if (Protected(go) || !Category(go)) { Far.Remove(go); return _observation < _observationCount; }
            float age; Far.TryGetValue(go, out age);
            Far[go] = Multiplayer.PlayerDistance(go.transform.position, GameRefs.Player != null ? GameRefs.Player.transform : null) > Distance.Value ? age + _elapsed : 0;
            return _observation < _observationCount;
        }
        static bool Referenced(GameObject candidate)
        {
            // On-save scan, not a gameplay scan. Check every retained FSM, including inactive saved objects.
            foreach (var fsm in _referenceFsms)
            {
                if (fsm == null || Within(fsm.gameObject, candidate)) continue;
                try
                {
                    var vars = fsm.FsmVariables;
                    foreach (var value in vars.GameObjectVariables) if (Within(value.Value, candidate)) return true;
                    foreach (var value in vars.ObjectVariables) { var component = value.Value as Component; if (Within(value.Value as GameObject, candidate) || component != null && Within(component.gameObject, candidate)) return true; }
                    foreach (var value in vars.ArrayVariables) if (value.Values != null) foreach (var item in value.Values) { var component = item as Component; if (Within(item as GameObject, candidate) || component != null && Within(component.gameObject, candidate)) return true; }
                }
                catch { return true; }
            }
            return false;
        }
        static bool Within(GameObject value, GameObject root) { return value != null && (value == root || value.transform.IsChildOf(root.transform)); }
        internal static void Prepare()
        {
            Candidates.Clear(); Removed.Clear(); RemovedKeys.Clear(); Registrations.Clear(); if (!Enabled.Value) return;
            if (Multiplayer.Hosting) foreach (DictionaryEntry entry in _objects(null)) { var item = Multiplayer.TrackedObject(entry.Value); if (item != null && (_owner(entry.Value) != 0 || _carried(entry.Value))) Touched.Add(item.name); }
            var root = GameRefs.NewGOArrayList; if (root == null) return;
            var lists = root.GetComponents<PlayMakerArrayListProxy>();
            _referenceFsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
            foreach (var pair in Far)
            {
                var go = pair.Key;
                if (pair.Value < Minutes.Value * 60f || Protected(go) || !Category(go) || Multiplayer.PlayerDistance(go.transform.position, GameRefs.Player != null ? GameRefs.Player.transform : null) <= Distance.Value) continue;
                // Render children are harmless; a nested saved prefab may be cargo and must be retained.
                bool nested = false;
                foreach (var prefab in go.GetComponentsInChildren<ES3Internal.ES3Prefab>(true)) if (prefab.gameObject != go) { nested = true; break; }
                if (nested || Referenced(go)) continue;
                Candidates.Add(go);
            }
            _referenceFsms = null;
            foreach (var go in Candidates) foreach (var list in lists)
                if (list.arrayList != null && list.arrayList.Contains(go)) { Registrations.Add(new KeyValuePair<ArrayList, GameObject>(list.arrayList, go)); list.arrayList.Remove(go); }
            foreach (var go in Candidates)
            {
                int at = go.name.LastIndexOf("(Clone)", StringComparison.Ordinal), number;
                if (at >= 0 && int.TryParse(go.name.Substring(at + 7), out number))
                { RemovedKeys.Add(go.name); ES3.DeleteKey(go.name, new ES3Settings(HostSaveSystem.ActiveFile) { location = ES3.Location.Cache }); }
            }
        }
        internal static bool RemovesKey(string key) { return RemovedKeys.Contains(key); }
        internal static void SaveProtection()
        {
            var names = new string[Touched.Count]; Touched.CopyTo(names);
            ES3.Save<string[]>(ProtectionKey, names, new ES3Settings(HostSaveSystem.ActiveFile) { location = ES3.Location.Cache });
        }
        internal static void Rollback()
        { foreach (var pair in Registrations) if (pair.Value != null && !pair.Key.Contains(pair.Value)) pair.Key.Add(pair.Value); Registrations.Clear(); Candidates.Clear(); }
        internal static void Commit()
        {
            foreach (var go in Candidates) if (go != null) { Removed.Add(go); Far.Remove(go); UnityEngine.Object.Destroy(go); }
            Registrations.Clear(); Candidates.Clear();
            if (Removed.Count > 0) Log.Always("[System] Save cleanup removed " + Removed.Count + " verified disposable roots; unknown save keys retained");
            Removed.Clear();
        }
    }
}
