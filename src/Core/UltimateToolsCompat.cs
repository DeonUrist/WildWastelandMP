using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Ultimate Tools keeps its native saves. This companion stream covers poses and stance
    // which ItemVars cannot represent, including edits to a car owned by another player.
    internal static class UltimateToolsCompat
    {
        const byte Request = 173, Snapshot = 174, Receipt = 175, Catalogue = 176;
        const int Limit = 16384;
        const string SaveKey = "WildWastelandMP.UltimateTools";
        internal struct PoseBefore { internal Transform Target; internal Vector3 Position; internal Quaternion Rotation; }
        sealed class Saved { internal string Parent; internal State State; }
        sealed class State
        {
            internal byte Kind;
            internal int Id, Parent;
            internal string Path = "", Mount = "";
            internal Vector3 Position;
            internal Quaternion Rotation = Quaternion.identity;
            internal uint Revision;
            internal GameObject Root;
            internal Transform Target;
            internal bool Applied;
        }
        sealed class Delivery { internal ulong Peer; internal State State; }
        static readonly Dictionary<string, State> States = new Dictionary<string, State>();
        static readonly Dictionary<int, State> PartPoses = new Dictionary<int, State>();
        static readonly Queue<GameObject> Roots = new Queue<GameObject>();
        static readonly Queue<Transform> Children = new Queue<Transform>();
        static readonly HashSet<GameObject> Known = new HashSet<GameObject>();
        static readonly HashSet<uint> Pending = new HashSet<uint>();
        static readonly Queue<Delivery> Deliveries = new Queue<Delivery>();
        static readonly HashSet<ulong> Catalogues = new HashSet<ulong>();
        static readonly Dictionary<string, List<Saved>> SavedStates = new Dictionary<string, List<Saved>>(StringComparer.Ordinal);
        static readonly Dictionary<string, GameObject> NamedRoots = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        static readonly Queue<State> Unapplied = new Queue<State>();
        static readonly HashSet<GameObject> OfflineEdits = new HashSet<GameObject>();
        static readonly List<string> Gone = new List<string>();
        static Func<object, IDictionary> _objects;
        static Func<object, int> _attached;
        static Func<Transform, object> _ensure;
        static Func<object, Transform> _sessionTarget;
        static Func<PlayMakerFSM> _native;
        static Func<object, float> _width, _height, _balance;
        static Action<object, float> _setWidth, _setHeight, _setBalance;
        static Func<object, bool> _kit;
        static Action<object> _apply;
        static bool _installed, _applying, _bootstrapped, _catalogued;
        static ulong _epoch;
        static uint _revision, _request;
        static int _generation = -1;
        static int _loadedGeneration = -1, _offlineGeneration = -1;
        static string _persistenceError;
        static float _retry, _sweep;
        static Transform _hinge;
        static Vector3 _hingePosition;
        static Quaternion _hingeRotation;
        internal static bool Busy { get { return _installed && (Pending.Count != 0 || Multiplayer.Hosting && (!_bootstrapped || Roots.Count != 0 || Children.Count != 0 || Unapplied.Count != 0)); } }
        internal static string SavePending { get { return "Ultimate Tools: init " + (!_bootstrapped ? 1 : 0) + ", requests " + Pending.Count + ", roots " + Roots.Count + ", children " + Children.Count + ", unapplied " + Unapplied.Count; } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Install()
        {
            BepInEx.PluginInfo plugin;
            if (!Chainloader.PluginInfos.TryGetValue("com.denis.apocalypter.partadjustment", out plugin) || ReferenceEquals(plugin.Instance, null)) return;
            var assembly = plugin.Instance.GetType().Assembly;
            var suspension = assembly.GetType("PartAdjustment.SuspensionAdjustment", true);
            var session = assembly.GetType("PartAdjustment.AdjustmentSession", true);
            _ensure = MultiplayerAccess.Call<Func<Transform, object>>(suspension, "Ensure", typeof(Transform));
            _sessionTarget = MultiplayerAccess.Get<Transform>(session, "Target");
            _native = MultiplayerAccess.Call<Func<PlayMakerFSM>>(assembly.GetType("PartAdjustment.NativeAdjustment", true), "get_Active", Type.EmptyTypes);
            _width = MultiplayerAccess.Get<float>(suspension, "Width"); _setWidth = MultiplayerAccess.Set<float>(suspension, "Width");
            _height = MultiplayerAccess.Get<float>(suspension, "RideHeight"); _setHeight = MultiplayerAccess.Set<float>(suspension, "RideHeight");
            _balance = MultiplayerAccess.Get<float>(suspension, "Balance"); _setBalance = MultiplayerAccess.Set<float>(suspension, "Balance");
            _kit = MultiplayerAccess.Get<bool>(suspension, "KitFitted");
            _apply = MultiplayerAccess.Call<Action<object>>(suspension, "Apply", Type.EmptyTypes);
            _objects = MultiplayerAccess.Get<IDictionary>(Multiplayer.Type("Net.WorldObjects"), "objects");
            _attached = MultiplayerAccess.Get<int>(Multiplayer.Type("Net.WorldObjects+Tracked"), "AttachedToCar");
            Patch(session, "Change", "BeforeSession", "SessionEdited"); Patch(session, "Reset", "BeforeSession", "SessionEdited");
            Patch(suspension, "Change", "BeforeChange", "SuspensionEdited");
            Patch(suspension, "Apply", null, "SuspensionApplied");
            Patch(assembly.GetType("PartAdjustment.AdjustmentLateRunner", true), "LateUpdate", null, "HingeEdited");
            Patch(Multiplayer.Type("Net.WorldObjects"), "ApplyAttachment", null, "AttachmentApplied");
            Patch(Multiplayer.Type("Net.WorldObjects"), "Capture", null, "Captured");
            var tails = assembly.GetType("PartAdjustment.TailLights", true);
            Patch(tails, "IsTail", null, "TailIdentity"); Patch(tails, "Variant", "TailVariant", null);
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
            Scheduler.Interval(Multiplayer.PatchOwner, "Ultimate Tools synchronization", .1f, Tick);
            _installed = true; Reset();
            Log.Always("[Multiplayer] Ultimate Tools sync ready: part/hinge poses and suspension; host relay and native saves");
        }
        static void Patch(Type type, string method, string before, string after)
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(type, method),
                prefix: before == null ? null : AccessTools.Method(typeof(UltimateToolsCompat), before),
                postfix: after == null ? null : AccessTools.Method(typeof(UltimateToolsCompat), after)))
                throw new InvalidOperationException("Ultimate Tools integration failed: " + type.FullName + "." + method);
        }
        static void Reset()
        {
            States.Clear(); PartPoses.Clear(); Roots.Clear(); Children.Clear(); Known.Clear(); Pending.Clear(); Deliveries.Clear(); Catalogues.Clear();
            SavedStates.Clear(); NamedRoots.Clear(); Unapplied.Clear(); _hinge = null;
            _epoch = MultiplayerParts.Epoch; _generation = SaveBus.Generation;
            _revision = _request = 0; _bootstrapped = _catalogued = false; _retry = _sweep = 0;
        }
        static bool Ready()
        {
            if (_epoch != MultiplayerParts.Epoch || _generation != SaveBus.Generation) Reset();
            return _installed && Multiplayer.WorldReady && _epoch != 0;
        }
        internal static void Registered(GameObject root)
        { if (_installed && root != null && Known.Add(root)) { Roots.Enqueue(root); NamedRoots[root.name] = root; } }
        static string Key(State state) { return state.Kind + ":" + state.Id + ":" + state.Path; }
        static bool Same(State a, State b)
        { return a != null && a.Parent == b.Parent && a.Mount == b.Mount && (a.Position - b.Position).sqrMagnitude < .00000001f && Quaternion.Angle(a.Rotation, b.Rotation) < .01f; }
        static int Ancestor(Transform target, out Transform root)
        {
            for (root = target; root != null; root = root.parent) { int id = Multiplayer.ObjectId(root.gameObject); if (id != 0) return id; }
            return 0;
        }
        static string Path(Transform root, Transform target)
        {
            string path = "";
            for (var t = target; t != root; t = t.parent)
            { if (t == null || t.name.IndexOf('/') >= 0) return null; path = path.Length == 0 ? t.name : t.name + "/" + path; if (System.Text.Encoding.UTF8.GetByteCount(path) > 240) return null; }
            return path;
        }
        static State Pose(Transform target)
        {
            if (target == null || target.parent == null) return null;
            Transform root; int id = Ancestor(target, out root); if (id == 0) return null;
            var state = new State { Id = id, Root = root.gameObject, Target = target, Position = target.localPosition, Rotation = target.localRotation };
            state.Path = Path(root, target); if (state.Path == null) return null;
            if (root == target)
            {
                if (!target.CompareTag("vehPart")) return null;
                Transform holder; state.Parent = Ancestor(target.parent, out holder);
                if (state.Parent == 0) return null;
                state.Mount = Path(holder, target.parent); if (state.Mount == null) return null;
            }
            else if (FsmUtil.Find(target.gameObject, "AdjustHinge") == null) return null;
            return state;
        }
        static State Stance(object adjustment)
        {
            var component = adjustment as Component; if (component == null) return null;
            int id = Multiplayer.ObjectId(component.gameObject); if (id == 0) return null;
            return new State { Kind = 1, Id = id, Root = component.gameObject, Target = component.transform,
                Position = new Vector3(_width(adjustment), _height(adjustment), _balance(adjustment)) };
        }
        static bool BeforeSession(object __instance, out PoseBefore __state)
        {
            __state = new PoseBefore();
            if (!BeforeChange()) return false;
            if (!_applying && _installed)
            { var target = _sessionTarget(__instance); if (target != null) __state = new PoseBefore { Target = target, Position = target.localPosition, Rotation = target.localRotation }; }
            return true;
        }
        static bool BeforeChange() { return !Multiplayer.Active || !MultiplayerSaving.Frozen && !ApocasetterBridge.InputBlocked; }
        static void SessionEdited(PoseBefore __state)
        {
            var target = __state.Target;
            if (target != null && ((target.localPosition - __state.Position).sqrMagnitude > .00000001f || Quaternion.Angle(target.localRotation, __state.Rotation) > .01f)) Edited(target);
        }
        static void HingeEdited()
        {
            if (_applying || !_installed || MultiplayerSaving.Frozen) return;
            var hinge = _native(); if (hinge == null || hinge.ActiveStateName == "off") { _hinge = null; return; }
            var target = hinge.transform;
            if (_hinge != target) { _hinge = target; _hingePosition = target.localPosition; _hingeRotation = target.localRotation; return; }
            if ((target.localPosition - _hingePosition).sqrMagnitude < .00000001f && Quaternion.Angle(target.localRotation, _hingeRotation) < .01f) return;
            _hingePosition = target.localPosition; _hingeRotation = target.localRotation; Edited(target);
        }
        static void Edited(Transform target)
        { if (!Multiplayer.Active) RememberOffline(target.gameObject); else if (Ready() && !MultiplayerSaving.Frozen) Submit(Pose(target)); }
        static void RememberOffline(GameObject root)
        { if (_offlineGeneration != SaveBus.Generation) { OfflineEdits.Clear(); _offlineGeneration = SaveBus.Generation; } OfflineEdits.Add(root); }
        static void SuspensionEdited(object __instance)
        {
            if (_applying || !_installed || MultiplayerSaving.Frozen) return;
            var component = __instance as Component;
            if (!Multiplayer.Active && component != null) RememberOffline(component.gameObject);
            else if (Ready()) Submit(Stance(__instance));
        }
        static void SuspensionApplied(object __instance)
        { if (!_applying && Multiplayer.Hosting && Ready()) Submit(Stance(__instance)); }
        static void Submit(State state)
        {
            if (state == null) return;
            State previous; if (States.TryGetValue(Key(state), out previous) && Same(previous, state)) return;
            if (Multiplayer.Hosting) { Commit(state); return; }
            if (Pending.Count >= 1024) { Log.Warn("[Multiplayer] Ultimate Tools edit queue full; wait for the host"); return; }
            uint request = ++_request; Pending.Add(request);
            Multiplayer.Send(Request, w => { w.Write(_epoch); w.Write(request); Write(w, state); });
        }
        static void Commit(State state)
        {
            if (States.Count >= Limit && !States.ContainsKey(Key(state))) throw new InvalidOperationException("Ultimate Tools state limit reached");
            state.Revision = ++_revision; state.Applied = false; Store(state); if (!TryApply(state)) Unapplied.Enqueue(state);
            Send(state, null);
            if (Log.Verbose) Log.Info("[Multiplayer] Ultimate Tools committed: " + state.Id + "/" + state.Path + ", revision " + state.Revision);
        }
        static void Store(State state)
        {
            States[Key(state)] = state;
            if (state.Kind == 0 && state.Path.Length == 0) { if (state.Parent == 0) PartPoses.Remove(state.Id); else PartPoses[state.Id] = state; }
        }
        static void AttachmentApplied(object __0)
        {
            if (!_installed || _applying || !Ready()) return;
            var root = Multiplayer.TrackedObject(__0); if (root == null) return;
            State state; int id = Multiplayer.ObjectId(root);
            if (PartPoses.TryGetValue(id, out state))
            {
                if (Multiplayer.Hosting && _attached(__0) != state.Parent) AttachmentChanged(root, id);
                else TryApply(state);
            }
            if (Multiplayer.Hosting) Registered(root);
        }
        static void Captured(object __0)
        {
            if (!_installed || _applying || !Multiplayer.Hosting || !Ready()) return;
            var root = Multiplayer.TrackedObject(__0); if (root == null) return;
            State state; int id = Multiplayer.ObjectId(root); int parent = _attached(__0);
            if (PartPoses.TryGetValue(id, out state)) { if (parent != state.Parent) AttachmentChanged(root, id); }
            else if (parent != 0 && root.CompareTag("vehPart")) { var pose = Pose(root.transform); if (pose != null) Commit(pose); }
        }
        static void AttachmentChanged(GameObject root, int id)
        {
            var pose = Pose(root.transform);
            Commit(pose ?? new State { Id = id, Root = root, Applied = true });
        }
        static bool TryApply(State state)
        {
            if (state.Kind == 0 && state.Path.Length == 0 && state.Parent == 0) { state.Applied = true; return true; }
            var root = Multiplayer.ObjectById(state.Id); if (root == null) return false;
            var target = state.Path.Length == 0 ? root.transform : root.transform.Find(state.Path);
            if (target == null) return false;
            _applying = true;
            try
            {
                if (state.Kind == 1)
                {
                    var adjustment = _ensure(root.transform); if (adjustment == null) return false;
                    // The reliable companion may precede native kit attachment/spawn.
                    if (!_kit(adjustment) && state.Position != new Vector3(1, 0, 0)) return false;
                    _setWidth(adjustment, state.Position.x); _setHeight(adjustment, state.Position.y); _setBalance(adjustment, state.Position.z); _apply(adjustment);
                }
                else
                {
                    if (state.Path.Length == 0)
                    {
                        var holder = Multiplayer.ObjectById(state.Parent); if (holder == null) return false;
                        var parent = state.Mount.Length == 0 ? holder.transform : holder.transform.Find(state.Mount);
                        if (parent == null || target.parent != parent || !root.CompareTag("vehPart")) return false;
                    }
                    else if (FsmUtil.Find(target.gameObject, "AdjustHinge") == null) return false;
                    target.localPosition = state.Position; target.localRotation = state.Rotation;
                }
                state.Root = root; state.Target = target; state.Applied = true; return true;
            }
            finally { _applying = false; }
        }
        static bool Valid(State state, ulong peer)
        {
            var root = Multiplayer.ObjectById(state.Id); if (root == null) return false;
            var target = state.Path.Length == 0 ? root.transform : root.transform.Find(state.Path);
            var player = Multiplayer.PlayerFor(peer);
            if (target == null || player == null || (target.position - player.transform.position).sqrMagnitude > 64f) return false;
            if (state.Kind == 1)
            {
                var adjustment = _ensure(root.transform); return state.Path.Length == 0 && adjustment != null && _kit(adjustment)
                    && state.Position.x >= 1 && state.Position.x <= 1.5f && Mathf.Abs(state.Position.y + state.Position.z) <= .1501f
                    && Mathf.Abs(state.Position.y - state.Position.z) <= .1501f && Mathf.Abs(state.Position.z) <= .0751f;
            }
            if (state.Path.Length != 0) return FsmUtil.Find(target.gameObject, "AdjustHinge") != null;
            var current = Pose(target); return current != null && current.Parent == state.Parent && current.Mount == state.Mount;
        }
        static void Tick()
        {
            if (!Ready()) return;
            if (!_bootstrapped)
            {
                _bootstrapped = true;
                if (Multiplayer.Hosting)
                {
                    foreach (DictionaryEntry pair in _objects(null)) Registered(Multiplayer.TrackedObject(pair.Value));
                    if (_loadedGeneration != SaveBus.Generation)
                    {
                        _persistenceError = null;
                        try { Load(); }
                        catch (Exception error) { SavedStates.Clear(); _persistenceError = error.Message; Log.Error("[Multiplayer] Ultimate Tools saved edits could not be restored; saving is blocked to retain the original world", error); }
                        _loadedGeneration = SaveBus.Generation;
                    }
                }
            }
            int budget = 16;
            while (Roots.Count > 0 && budget-- > 0)
            {
                var root = Roots.Dequeue(); if (!Multiplayer.Hosting || root == null) continue;
                Restore(root);
                if (FsmUtil.Find(root, "RpmGear") != null)
                {
                    var adjustment = _ensure(root.transform); if (adjustment != null) Baseline(Stance(adjustment));
                    for (int i = 0; i < root.transform.childCount; i++) Children.Enqueue(root.transform.GetChild(i));
                }
                else Baseline(Pose(root.transform));
            }
            budget = 64;
            while (Children.Count > 0 && budget-- > 0)
            {
                var child = Children.Dequeue(); if (child == null || Multiplayer.ObjectId(child.gameObject) != 0) continue;
                if (FsmUtil.Find(child.gameObject, "AdjustHinge") != null) Baseline(Pose(child));
                for (int i = 0; i < child.childCount; i++) Children.Enqueue(child.GetChild(i));
            }
            if (Multiplayer.Client && !_catalogued && Time.unscaledTime >= _retry)
            { _retry = Time.unscaledTime + 15f; Multiplayer.Send(Catalogue, w => { w.Write(_epoch); w.Write(false); }); }
            budget = 16;
            while (Deliveries.Count > 0 && budget-- > 0)
            {
                var delivery = Deliveries.Dequeue();
                if (delivery.State != null) Send(delivery.State, delivery.Peer);
                else { Catalogues.Remove(delivery.Peer); Multiplayer.Send(Catalogue, w => { w.Write(_epoch); w.Write(true); }, delivery.Peer); }
            }
            // Spawn and native attachment can arrive later than the companion; retry with a bounded budget.
            budget = Math.Min(32, Unapplied.Count);
            while (budget-- > 0)
            {
                var state = Unapplied.Dequeue(); State latest;
                if (!States.TryGetValue(Key(state), out latest) || !ReferenceEquals(state, latest)) continue;
                if (!TryApply(state)) Unapplied.Enqueue(state);
            }
            if (Time.unscaledTime < _sweep) return;
            _sweep = Time.unscaledTime + 5f; Gone.Clear();
            foreach (var pair in States) if (!ReferenceEquals(pair.Value.Root, null) && (pair.Value.Root == null || Multiplayer.ObjectById(pair.Value.Id) != pair.Value.Root)) Gone.Add(pair.Key);
            foreach (string key in Gone) { var state = States[key]; if (state.Kind == 0 && state.Path.Length == 0) PartPoses.Remove(state.Id); States.Remove(key); }
            Known.RemoveWhere(Destroyed);
        }
        static bool Destroyed(GameObject root) { return root == null; }
        static void Baseline(State state)
        { if (state != null && !States.ContainsKey(Key(state))) Commit(state); }
        static void Send(State state, ulong? peer)
        { Multiplayer.Send(Snapshot, w => { w.Write(_epoch); w.Write(state.Revision); Write(w, state); }, peer); }
        static void Write(BinaryWriter writer, State state)
        {
            writer.Write(state.Kind); writer.Write(state.Id); writer.Write(state.Path); writer.Write(state.Parent); writer.Write(state.Mount);
            writer.Write(state.Position.x); writer.Write(state.Position.y); writer.Write(state.Position.z);
            writer.Write(state.Rotation.x); writer.Write(state.Rotation.y); writer.Write(state.Rotation.z); writer.Write(state.Rotation.w);
        }
        static State Read(BinaryReader reader)
        {
            var state = new State { Kind = reader.ReadByte(), Id = reader.ReadInt32(), Path = reader.ReadString(), Parent = reader.ReadInt32(), Mount = reader.ReadString(),
                Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) };
            float norm = Quaternion.Dot(state.Rotation, state.Rotation);
            if (state.Kind > 1 || state.Id == 0 || System.Text.Encoding.UTF8.GetByteCount(state.Path) > 240 || System.Text.Encoding.UTF8.GetByteCount(state.Mount) > 240 || !Finite(state.Position.sqrMagnitude)
                || state.Position.sqrMagnitude > 10000 || !Finite(norm) || norm < .9f || norm > 1.1f || reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Invalid Ultimate Tools state");
            state.Rotation = state.Rotation.normalized; return state;
        }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind < Request || kind > Catalogue || !Ready() || reader.ReadUInt64() != _epoch) return;
            if (kind == Receipt)
            {
                uint request = reader.ReadUInt32(); bool acknowledged = reader.ReadBoolean();
                if (Multiplayer.Client && from == 0 && reader.BaseStream.Position == reader.BaseStream.Length && Pending.Remove(request) && !acknowledged)
                    Log.Warn("[Multiplayer] Ultimate Tools edit rejected by host: target changed or is out of reach");
                return;
            }
            if (kind == Catalogue)
            {
                bool complete = reader.ReadBoolean(); if (reader.BaseStream.Position != reader.BaseStream.Length) return;
                if (Multiplayer.Client && from == 0 && complete) _catalogued = true;
                else if (Multiplayer.Hosting && from != 0 && !complete && MultiplayerSleepers.WelcomedPeer(from) && Catalogues.Add(from))
                { foreach (var state in States.Values) Deliveries.Enqueue(new Delivery { Peer = from, State = state }); Deliveries.Enqueue(new Delivery { Peer = from }); }
                return;
            }
            uint number = reader.ReadUInt32(); var incoming = Read(reader); string key = Key(incoming); State previous;
            if (kind == Snapshot)
            {
                if (!Multiplayer.Client || from != 0 || States.Count >= Limit && !States.ContainsKey(key)) return;
                if (States.TryGetValue(key, out previous) && previous.Revision >= number)
                { if (previous.Revision == number) TryApply(previous); return; }
                incoming.Revision = number; Store(incoming); if (!TryApply(incoming)) Unapplied.Enqueue(incoming); return;
            }
            if (!Multiplayer.Hosting || from == 0 || !MultiplayerSleepers.WelcomedPeer(from)) return;
            bool accepted = (States.Count < Limit || States.ContainsKey(key)) && Valid(incoming, from);
            if (accepted) Commit(incoming);
            else if (States.TryGetValue(key, out previous)) Send(previous, from);
            Multiplayer.Send(Receipt, w => { w.Write(_epoch); w.Write(number); w.Write(accepted); }, from);
        }
        static void TailIdentity(GameObject __0, ref bool __result)
        { if (__0 != null && __0.name.StartsWith("MP_tail_light", StringComparison.Ordinal)) __result = true; }
        static void TailVariant(ref string __0)
        { if (__0 != null && __0.StartsWith("MP_", StringComparison.Ordinal)) __0 = __0.Substring(3); }

        // Extra hinge/part offsets use one optional embedded key in the SAME journaled world
        // transaction. Existing native/Ultimate Tools keys and old worlds remain readable.
        internal static void Save()
        {
            if (!_installed || !Multiplayer.Hosting) return;
            try { SaveState(); }
            catch (Exception error) { HostSaveSystem.Fail("Ultimate Tools state: " + error.Message); throw; }
        }
        static void SaveState()
        {
            if (_persistenceError != null) throw new IOException("Ultimate Tools saved state was not restored: " + _persistenceError);
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); long countAt = stream.Position; writer.Write(0); int count = 0;
                foreach (var state in States.Values)
                {
                    var root = Multiplayer.ObjectById(state.Id); if (root == null || state.Kind == 0 && state.Path.Length == 0 && state.Parent == 0) continue;
                    var parent = state.Parent == 0 ? null : Multiplayer.ObjectById(state.Parent);
                    if (state.Parent != 0 && parent == null) continue;
                    writer.Write(root.name); writer.Write(parent == null ? "" : parent.name);
                    using (var record = new MemoryStream()) using (var entry = new BinaryWriter(record))
                    { Write(entry, state); var bytes = record.ToArray(); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
                    count++;
                }
                stream.Position = countAt; writer.Write(count);
                ES3.Save<byte[]>(SaveKey, stream.ToArray(), new ES3Settings(HostSaveSystem.ActiveFile) { location = ES3.Location.Cache });
            }
        }
        static void Load()
        {
            string file = SaveBus.Slot; if (file == null) return;
            var settings = new ES3Settings(file) { location = ES3.Location.Cache };
            if (!ES3.KeyExists(SaveKey, settings)) { settings.location = ES3.Location.File; if (!ES3.KeyExists(SaveKey, settings)) return; }
            var data = ES3.Load<byte[]>(SaveKey, settings);
            if (data == null || data.Length > 16 * 1024 * 1024) throw new InvalidDataException("Ultimate Tools saved state exceeds limit");
            using (var reader = new BinaryReader(new MemoryStream(data, false)))
            {
                if (reader.ReadInt32() != 1) throw new InvalidDataException("Unknown Ultimate Tools saved state");
                int count = reader.ReadInt32(); if (count < 0 || count > Limit) throw new InvalidDataException("Too many saved Ultimate Tools edits");
                for (int i = 0; i < count; i++)
                {
                    string name = reader.ReadString(), parent = reader.ReadString(); int length = reader.ReadUInt16();
                    if (name.Length == 0 || name.Length > 512 || parent.Length > 512 || length > 1100) throw new InvalidDataException("Invalid saved Ultimate Tools identity");
                    State state; using (var entry = new BinaryReader(new MemoryStream(reader.ReadBytes(length), false))) state = Read(entry);
                    List<Saved> values; if (!SavedStates.TryGetValue(name, out values)) SavedStates.Add(name, values = new List<Saved>());
                    values.Add(new Saved { Parent = parent, State = state });
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing Ultimate Tools save data");
            }
            Log.Always("[Multiplayer] Ultimate Tools saved edits loaded for " + SavedStates.Count + " objects");
        }
        static void Restore(GameObject root)
        {
            List<Saved> values; if (!SavedStates.TryGetValue(root.name, out values)) return;
            foreach (var saved in values)
            {
                var state = saved.State; state.Id = Multiplayer.ObjectId(root); state.Root = root;
                var target = state.Path.Length == 0 ? root.transform : root.transform.Find(state.Path);
                if (target != null && _offlineGeneration == SaveBus.Generation && OfflineEdits.Contains(target.gameObject)) continue;
                if (saved.Parent.Length != 0)
                {
                    GameObject parent;
                    if (!NamedRoots.TryGetValue(saved.Parent, out parent) || parent == null) continue;
                    state.Parent = Multiplayer.ObjectId(parent);
                }
                Commit(state);
            }
            SavedStates.Remove(root.name);
        }
    }
}
