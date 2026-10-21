using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildWasteland.Modules.Patrols
{
    // Packed, uninitialised prefabs. Native components initialise only on the independently owned spawned copy.
    internal static class VehicleBuildCache
    {
        internal sealed class Entry
        {
            internal CarTemplate Template;
            internal string Bumper;
            internal bool Trailer, Ready, Failed, Queued;
            internal GameObject Root;
            internal GameObject Preparing;
            internal PlayMakerFSM[] Fsms;
            internal int FsmIndex;
            internal readonly Queue<GameObject> Reserves = new Queue<GameObject>();
            internal IEnumerator Build;
            internal readonly List<Item> Items = new List<Item>();
        }
        internal sealed class Item { internal int[] Path; internal string Prefab; }
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly Queue<Entry> Pending = new Queue<Entry>();
        private static Entry _active;
        private static GameObject _holder;
        private static bool _warmRequested = true, _reported;
        private const int ReserveCount = 2;
        internal static bool Warming { get { return _warmRequested || _active != null || Pending.Count > 0; } }

        internal static float LoadFraction
        {
            get
            {
                if (_warmRequested) return 0f;
                if (Entries.Count == 0) return 1f;
                float done = 0f;
                foreach (var entry in Entries.Values)
                {
                    done += entry.Failed ? ReserveCount : entry.Reserves.Count;
                    if (!entry.Failed && entry.Preparing != null && entry.Fsms != null)
                        done += entry.FsmIndex / (float)Math.Max(1, entry.Fsms.Length);
                }
                return Mathf.Clamp01(done / (Entries.Count * ReserveCount));
            }
        }
        internal static Core.LoadingProgress.Work? LoadWork()
        {
            if (!Warming && !PatrolPersistence.Restoring) return null;
            string detail = Warming
                ? (_active != null ? "preparing vehicles - " + _active.Template.Name + " (" + Mathf.RoundToInt(LoadFraction * 100f) + "%)" : "preparing vehicle templates")
                : "restoring saved patrols, crew and AI";
            return new Core.LoadingProgress.Work(.85f * LoadFraction + .15f * PatrolPersistence.LoadFraction, detail);
        }

        internal static void WarmPark() { _warmRequested = true; _reported = false; }
        internal static void Warm(CarTemplate t, bool trailer = false)
        {
            if (t == null || !t.Available) return;
            if (trailer || t.Bumpers == null || t.Bumpers.Length == 0) Require(t, "", trailer);
            else foreach (string bumper in t.Bumpers) Require(t, bumper ?? "", false);
            if (t.Trailer != null) Warm(t.Trailer, true);
        }

        // Exact binary definition, independent of template name, crew/cargo rolls and traffic-instance metadata.
        private static string Key(CarTemplate t, string bumper, bool trailer)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(t.Body); writer.Write(bumper); writer.Write(trailer);
                writer.Write(t.Parts != null);
                if (t.Parts != null)
                {
                    writer.Write(t.Parts.Length);
                    foreach (var p in t.Parts)
                    {
                        writer.Write(p.prefab ?? ""); writer.Write(p.hinge ?? ""); writer.Write(p.parent);
                        Floats(writer, p.hingePos); Floats(writer, p.hingeRot); Floats(writer, p.pos); Floats(writer, p.rot);
                    }
                }
                else foreach (string part in new[] { t.Wheel, t.RearWheel, t.Engine, t.Radiator, t.SteeringWheel, t.Exhaust, t.Seat, t.PassengerSeat }) writer.Write(part ?? "");
                writer.Flush(); return Convert.ToBase64String(stream.ToArray());
            }
        }
        private static void Floats(BinaryWriter writer, float[] values)
        { writer.Write(values != null ? values.Length : 0); if (values != null) foreach (float v in values) writer.Write(v); }

        internal static Entry Require(CarTemplate t, string bumper, bool trailer)
        {
            string key = Key(t, bumper, trailer); Entry entry;
            if (Entries.TryGetValue(key, out entry)) return entry;
            entry = new Entry { Template = t, Bumper = bumper, Trailer = trailer };
            Entries.Add(key, entry); Enqueue(entry);
            return entry;
        }
        private static void Enqueue(Entry entry)
        { if (entry.Queued || ReferenceEquals(_active, entry)) return; entry.Queued = true; Pending.Enqueue(entry); _reported = false; }

        internal static bool Step()
        {
            if (!PatrolsModule.Running || Core.Scheduler.FrameMs >= Core.Scheduler.FrameBudgetMs) return false;
            if (_warmRequested)
            {
                if (Core.GameRefs.NewGOArrayList == null) return false;
                _warmRequested = false;
                foreach (var t in CarTemplate.Park) Warm(t);
                return true;
            }
            if (_active == null)
            {
                if (Pending.Count == 0)
                {
                    if (!_reported) { _reported = true; PatrolsModule.Verbose("Vehicle build cache ready: " + Entries.Count + " structural variant(s)."); }
                    return false;
                }
                _active = Pending.Dequeue();
                _active.Queued = false;
            }
            var entry = _active;
            try
            {
                if (entry.Root == null)
                {
                    var source = Prefabs.Exact(entry.Template.Body);
                    if (source == null) throw new InvalidOperationException("Missing frame " + entry.Template.Body);
                    if (_holder == null)
                    {
                        _holder = new GameObject("WW.Patrols.BuildCache") { hideFlags = HideFlags.HideAndDontSave };
                        _holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(_holder);
                    }
                    entry.Root = UnityEngine.Object.Instantiate(source, _holder.transform, false);
                    entry.Root.name = source.name; entry.Root.transform.localPosition = Vector3.zero; entry.Root.transform.localRotation = Quaternion.identity;
                    entry.Root.SetActive(true); // inactive hierarchy: no native Awake, save registration, physics or AI
                    entry.Build = Patrol.PackStructure(entry.Root, entry.Template, entry.Bumper, entry.Trailer);
                    return true;
                }
                if (entry.Build != null)
                {
                    if (entry.Build.MoveNext()) return true;
                    ((IDisposable)entry.Build).Dispose(); entry.Build = null;
                    PatrolsModule.Verbose("Vehicle build cached: " + entry.Template.Name + " / " + entry.Items.Count + " fitted parts / bumper " + entry.Bumper);
                    return true;
                }
                if (entry.Preparing == null)
                {
                    entry.Preparing = UnityEngine.Object.Instantiate(entry.Root, _holder.transform, false);
                    entry.Fsms = entry.Preparing.GetComponentsInChildren<PlayMakerFSM>(true); entry.FsmIndex = 0;
                    return true;
                }
                if (entry.FsmIndex < entry.Fsms.Length)
                {
                    // Native preprocessing decodes/binds actions and installs event handlers while the hierarchy is dormant.
                    // No state entry, native physics or save-list membership; consume this exact instance, never clone its runtime FSM data.
                    var fsm = entry.Fsms[entry.FsmIndex++];
                    fsm.Preprocess();
                    return true;
                }
                entry.Reserves.Enqueue(entry.Preparing); entry.Preparing = null; entry.Fsms = null; entry.Ready = true;
                if (entry.Reserves.Count >= ReserveCount) _active = null;
                return true;
            }
            catch (Exception e)
            {
                entry.Failed = true; _active = null;
                if (entry.Build != null) ((IDisposable)entry.Build).Dispose();
                if (entry.Root != null) UnityEngine.Object.Destroy(entry.Root);
                Core.Log.Warn("[Patrols] Vehicle build cache failed for " + entry.Template.Name + ": " + e);
                return false;
            }
        }

        internal static void Record(GameObject part, string prefab)
        {
            var path = new List<int>();
            for (var node = part.transform; node != _active.Root.transform; node = node.parent) path.Add(node.GetSiblingIndex());
            path.Reverse(); _active.Items.Add(new Item { Path = path.ToArray(), Prefab = prefab });
        }

        internal static GameObject ClonePart(GameObject prefab, Transform hinge)
        {
            var part = UnityEngine.Object.Instantiate(prefab, _holder.transform, false);
            part.transform.SetPositionAndRotation(hinge.position, hinge.rotation);
            return part;
        }

        internal static GameObject Spawn(Entry entry, Vector3 position, Quaternion rotation,bool merchant=false)
        {
            // Clone while dormant, assign unique native save names, then activate the entire independent hierarchy.
            var car = entry.Reserves.Dequeue(); entry.Ready = entry.Reserves.Count > 0; Enqueue(entry);
            try
            {
                car.SetActive(false);
                if(merchant)MerchantSpawnAudit.Begin(car);
                Register.Defer(car); Register.Name(car, entry.Template.Body); Register.Add(car, true);
                foreach (var item in entry.Items)
                {
                    var node = car.transform; foreach (int index in item.Path) node = node.GetChild(index);
                    Register.Name(node.gameObject, item.Prefab); Register.Add(node.gameObject, false);
                    if(merchant)MerchantSpawnAudit.Keep(node.gameObject);
                }
                car.transform.SetParent(null, false);
                SceneManager.MoveGameObjectToScene(car, SceneManager.GetActiveScene());
                car.transform.SetPositionAndRotation(position, rotation);
                if(merchant)MerchantSpawnAudit.CaptureArea(car);
                car.SetActive(true);
                PatrolsModule.Verbose("Vehicle build cache hit: " + entry.Template.Name + " -> " + car.name);
                return car;
            }
            catch { MerchantSpawnAudit.Clear(car);Register.Discard(car); UnityEngine.Object.Destroy(car); throw; }
        }

        internal static void Reset()
        {
            if (_active != null && _active.Build != null) ((IDisposable)_active.Build).Dispose();
            _active = null; Pending.Clear(); Entries.Clear();
            if (_holder != null) UnityEngine.Object.Destroy(_holder);
            _holder = null; _warmRequested = true; _reported = false;
        }
    }
}
