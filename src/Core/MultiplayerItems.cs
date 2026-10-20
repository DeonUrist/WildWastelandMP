using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // MP only discovers variables referenced by vanilla load actions. WW adds saved fields beyond those actions.
    internal static partial class MultiplayerItems
    {
        internal static event Action<GameObject, List<NamedVariable>> Variables;
        internal static event Action BeforeFlush;
        internal static event Func<GameObject, bool> ServerAuthority;
        static Action<object> _revert;
        static Action<GameObject> _kick;
        internal static bool ServerOwned(GameObject root)
        {
            var policies = ServerAuthority; if (!Multiplayer.Active || root == null || policies == null) return false;
            foreach (Func<GameObject, bool> policy in policies.GetInvocationList()) if (policy(root)) return true;
            return false;
        }
        static Func<object, NamedVariable[]> _variables;
        static Func<object, object[]> _baseline;
        static Func<object, object> _stateVars;
        static ulong _varsPeer;
        static int _varsId;
        struct VarsContext { internal ulong Peer; internal int Id; }
        static Func<List<NamedVariable>, object> _new;
        static Func<object, IDictionary> _objects;
        static Func<object, bool> _owned, _changed;
        static Func<object, ulong> _owner;
        static Func<object, float> _quiet;
        static Func<object, byte> _kind;
        static Func<object, uint> _sequence;
        static Action<object, uint> _setSequence;
        static Action<object> _capture;
        static Action<BinaryWriter, object> _writeState, _writeCar;
        static Action<object, BinaryWriter> _writeVars;
        static Func<List<KeyValuePair<int, string>>> _inventory;
        static Func<object, IDictionary> _pendingCreates;
        internal static void Install()
        {
            var type = Multiplayer.Type("Net.ItemVars");
            _variables = MultiplayerAccess.Get<NamedVariable[]>(type, "vars");
            _baseline = MultiplayerAccess.Get<object[]>(type, "baseline");
            _stateVars = MultiplayerAccess.Get<object>(Multiplayer.Type("Net.WorldObjects+Tracked"), "Vars");
            _new = MultiplayerAccess.New<List<NamedVariable>>(type);
            var world = Multiplayer.Type("Net.WorldObjects"); var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
            _objects = MultiplayerAccess.Get<IDictionary>(world, "objects");
            _owned = MultiplayerAccess.Get<bool>(tracked, "OwnedLocally"); _kind = MultiplayerAccess.Get<byte>(tracked, "Kind");
            _owner = MultiplayerAccess.Get<ulong>(tracked, "Owner");
            _quiet = MultiplayerAccess.Get<float>(tracked, "QuietUntil");
            _sequence = MultiplayerAccess.Get<uint>(world, "stateSeq"); _setSequence = MultiplayerAccess.Set<uint>(world, "stateSeq");
            _capture = MultiplayerAccess.Call<Action<object>>(world, "Capture", tracked);
            _writeState = MultiplayerAccess.Call<Action<BinaryWriter, object>>(world, "WriteState", typeof(BinaryWriter), tracked);
            _writeCar = MultiplayerAccess.Call<Action<BinaryWriter, object>>(world, "WriteCarState", typeof(BinaryWriter), tracked);
            _writeVars = MultiplayerAccess.Call<Action<object, BinaryWriter>>(type, "Write", typeof(BinaryWriter));
            _inventory = MultiplayerAccess.Call<Func<List<KeyValuePair<int, string>>>>(world, "LocalInventory", Type.EmptyTypes);
            _pendingCreates = MultiplayerAccess.Get<IDictionary>(world, "pendingCreates");
            _changed = MultiplayerAccess.Call<Func<object, bool>>(type, "Changed", Type.EmptyTypes);
            _revert = MultiplayerAccess.Call<Action<object>>(type, "Revert", Type.EmptyTypes);
            _kick = MultiplayerAccess.Call<Action<GameObject>>(world, "KickOutOf", typeof(GameObject));
            Multiplayer.Message += ConsumeMessage;
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(type, "For"), postfix: AccessTools.Method(typeof(MultiplayerItems), "Extend")))
                throw new InvalidOperationException("MP custom saved item variables could not be installed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Net.WorldObjects"), "OnVars"),
                prefix: AccessTools.Method(typeof(MultiplayerItems), "BeforeVars"), finalizer: AccessTools.Method(typeof(MultiplayerItems), "AfterVars"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(type, "Apply"), prefix: AccessTools.Method(typeof(MultiplayerItems), "BeforeApply")))
                throw new InvalidOperationException("MP condition reconciliation hooks could not be installed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "ShouldSendVars"), prefix: AccessTools.Method(typeof(MultiplayerItems), "BeforeSendVars")))
                throw new InvalidOperationException("MP item interaction authority hook could not be installed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "OnRelease"),
                prefix: AccessTools.Method(typeof(MultiplayerItems), "BeforeRelease"), postfix: AccessTools.Method(typeof(MultiplayerItems), "AfterRelease")))
                throw new InvalidOperationException("MP dropped item publication hook could not be installed");
            MultiplayerEdits.Install();
            InstallLifecycle();
            InstallResources();
        }
        static void Extend(GameObject __0, ref object __result)
        {
            var provide = Variables; if (__0 == null || __result == null) return;
            var old = _variables(__result); var values = new List<NamedVariable>(old);
            if (__0.CompareTag("vehPart") || __0.CompareTag("vehPartRemoved"))
                foreach (var fsm in __0.GetComponents<PlayMakerFSM>())
                    if (fsm != null && (fsm.FsmName == "Condition" || fsm.FsmName == "Repair")) Add(values, fsm.FsmVariables.FindFsmFloat("Condition"));
            if (provide != null) provide(__0, values);
            if (values.Count != old.Length)
            {
                if (values.Count > 255) throw new InvalidOperationException("Too many MP saved item variables");
                __result = _new(values);
            }
        }
        internal static void Add(List<NamedVariable> values, NamedVariable value)
        { if (value != null && !values.Contains(value)) values.Add(value); }
        internal static IEnumerator FlushOwned()
        {
            var prepare = BeforeFlush; if (prepare != null) prepare();
            var states = new List<object>(); foreach (DictionaryEntry entry in _objects(null)) states.Add(entry.Value);
            int budget = 0;
            foreach (object state in states)
            {
                var go = Multiplayer.TrackedObject(state); if (go == null) continue;
                bool owned = _owned(state); var vars = _stateVars(state);
                // Native permits an unowned nearby item interaction as well as a held/owned item.
                bool nearby = GameRefs.Player != null && (go.transform.position - GameRefs.Player.transform.position).sqrMagnitude < 9f;
                if (vars != null && !ServerOwned(go))
                {
                    if (owned) SendNative(30, w => { w.Write(Multiplayer.ObjectId(go)); _writeVars(vars, w); });
                    else if (nearby && _changed(vars)) { MultiplayerEdits.Submit(go, _variables(vars), _baseline(vars)); _revert(vars); }
                }
                if (owned)
                {
                    _capture(state);
                    if (_kind(state) == 1) SendNative(18, w => { w.Write(Time.realtimeSinceStartup); w.Write((ushort)1); _writeCar(w, state); });
                    else
                    {
                        uint sequence = _sequence(null) + 1; _setSequence(null, sequence);
                        SendNative(8, w => { w.Write(sequence); w.Write(true); w.Write((ushort)1); w.Write(Multiplayer.ObjectId(go)); _writeState(w, state); });
                    }
                }
                if (++budget == 16) { budget = 0; yield return null; }
            }
        }
        static void SendNative(byte kind, Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            { writer.Write(kind); write(writer); MultiplayerReliable.Send(stream.ToArray(), null); }
        }
        static void BeforeRelease(ulong __0, BinaryReader __1, out object __state)
        {
            __state = null; if (!Multiplayer.Hosting || __0 == 0) return;
            long position = __1.BaseStream.Position; int id;
            try { id = __1.ReadInt32(); } finally { __1.BaseStream.Position = position; }
            var state = Multiplayer.TrackedState(Multiplayer.ObjectById(id));
            if (state != null && _owner(state) == __0) __state = state;
        }
        static void AfterRelease(object __state)
        {
            if (__state == null || _owner(__state) != 0) return;
            var root = Multiplayer.TrackedObject(__state); if (root == null) return;
            // GiveBackToHost captures the final pose, so native change detection may never publish it.
            uint sequence = _sequence(null) + 1; _setSequence(null, sequence);
            SendNative(8, w => { w.Write(sequence); w.Write(true); w.Write((ushort)1); w.Write(Multiplayer.ObjectId(root)); _writeState(w, __state); });
            if (Log.Verbose) Log.Info("[Multiplayer] Released item state published to all clients: " + root.name);
        }
        internal static bool FlushInventory()
        {
            foreach (var item in _inventory())
            {
                var root = Multiplayer.ObjectById(item.Key); var state = Multiplayer.TrackedState(root);
                var vars = state != null ? _stateVars(state) : null;
                if (vars != null) SendNative(30, w => { w.Write(item.Key); _writeVars(vars, w); });
            }
            return _pendingCreates(null).Count == 0 && !ResourceBusy && !MultiplayerParts.Pending && !MultiplayerEdits.Busy && !UltimateToolsCompat.Busy;
        }
        static object Vars(GameObject part)
        { var state = Multiplayer.TrackedState(part); return state != null ? _stateVars(state) : null; }
        static bool BeforeSendVars(object __0, ref bool __result)
        {
            if (!Multiplayer.Active) return true;
            var root = Multiplayer.TrackedObject(__0);
            // A leased item keeps native's own decision: answering false here makes ItemsTick Revert() the variables mid-use
            // (the lease debit would be undone every 0.5 s); the host drops a guest's ObjectVars for a locked id in BeforeVars.
            if (ServerOwned(root)) { __result = Multiplayer.Hosting; return false; }
            if (root == null || !Multiplayer.OwnedElsewhere(root) || Time.realtimeSinceStartup < _quiet(__0)) return true;
            if (GameRefs.Player != null && (root.transform.position - GameRefs.Player.transform.position).sqrMagnitude < 16f)
            {
                var vars = _stateVars(__0); if (vars != null) MultiplayerEdits.Submit(root, _variables(vars), _baseline(vars));
            }
            __result = false; return false;
        }
        internal static byte[] PrepareEdit(GameObject root, byte[] changes)
        {
            var vars = Vars(root); if (vars == null) return null;
            if (ResourceLocked(Multiplayer.ObjectId(root))) return null;
            var fields = _variables(vars);
            using (var reader = new BinaryReader(new MemoryStream(changes, false)))
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                int count = reader.ReadByte(); writer.Write((byte)count); var seen = new HashSet<int>();
                for (int n = 0; n < count; n++)
                {
                    int index = reader.ReadByte(); byte type = reader.ReadByte();
                    if (index >= fields.Length || !seen.Add(index)) return null;
                    writer.Write((byte)index); writer.Write(type);
                    if (type <= 1)
                    {
                        double delta = reader.ReadDouble(); if (double.IsNaN(delta) || double.IsInfinity(delta) || Math.Abs(delta) > 1000000) return null;
                        if (type == 0 && fields[index] is FsmFloat)
                        { double value = ((FsmFloat)fields[index]).Value + delta; if (value < 0 || value > float.MaxValue || double.IsNaN(value) || double.IsInfinity(value)) return null; writer.Write(value); }
                        else if (type == 1 && fields[index] is FsmInt)
                        { double value = ((FsmInt)fields[index]).Value + delta; if (value < 0 || value > int.MaxValue || value != Math.Truncate(value)) return null; writer.Write(value); }
                        else return null;
                    }
                    else if (type == 2 && fields[index] is FsmBool)
                    { bool expected = reader.ReadBoolean(), value = reader.ReadBoolean(); if (((FsmBool)fields[index]).Value != expected) return null; writer.Write(value); }
                    else if (type == 3 && fields[index] is FsmString)
                    {
                        string expected = reader.ReadString(), value = reader.ReadString();
                        if (expected != (((FsmString)fields[index]).Value ?? "") || value.Length > 256 || System.Text.Encoding.UTF8.GetByteCount(value) > 256) return null;
                        writer.Write(value);
                    }
                    else return null;
                }
                return reader.BaseStream.Position == reader.BaseStream.Length ? stream.ToArray() : null;
            }
        }
        internal static bool Edit(GameObject root, byte[] changes, bool absolute = false, bool mirror = false, bool publish = true)
        {
            var vars = Vars(root); if (vars == null || !mirror && Multiplayer.OwnedElsewhere(root)) return false;
            if (!absolute) { changes = PrepareEdit(root, changes); if (changes == null) return false; }
            var fields = _variables(vars); var baseline = _baseline(vars);
            FsmInt purchased = null;
            using (var reader = new BinaryReader(new MemoryStream(changes, false)))
            {
                int count = reader.ReadByte(); var indices = new int[count]; var values = new object[count];
                for (int n = 0; n < count; n++)
                {
                    int index = reader.ReadByte(); byte type = reader.ReadByte();
                    if (index >= fields.Length) return false;
                    indices[n] = index;
                    if (type <= 1)
                    {
                        double value = reader.ReadDouble(); if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > float.MaxValue) return false;
                        if (type == 0 && fields[index] is FsmFloat) values[n] = (float)value;
                        else if (type == 1 && fields[index] is FsmInt && value <= int.MaxValue) values[n] = (int)value;
                        else return false;
                    }
                    else if (type == 2 && fields[index] is FsmBool) values[n] = reader.ReadBoolean();
                    else if (type == 3 && fields[index] is FsmString) { string value = reader.ReadString(); if (value.Length > 256) return false; values[n] = value; }
                    else return false;
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) return false;
                for (int n = 0; n < count; n++)
                {
                    int index = indices[n];
                    if (fields[index] is FsmFloat) ((FsmFloat)fields[index]).Value = (float)values[n];
                    else if (fields[index] is FsmInt)
                    {
                        var value = (FsmInt)fields[index]; value.Value = (int)values[n];
                        if (value.Name == "ForBuyInt" && value.Value == 0) purchased = value;
                    }
                    else if (fields[index] is FsmBool) ((FsmBool)fields[index]).Value = (bool)values[n];
                    else ((FsmString)fields[index]).Value = (string)values[n];
                    baseline[index] = values[n];
                }
            }
            // Native ApplyVars also retires the Buyable interaction. Our authoritative edits bypass it.
            if (purchased != null)
            {
                var forBuy = FsmUtil.Find(root, "ForBuy");
                if (forBuy != null && ReferenceEquals(purchased, forBuy.FsmVariables.FindFsmInt("ForBuyInt")))
                {
                    var buyable = FsmUtil.Find(root, "Buyable");
                    if (buyable != null && buyable.enabled)
                    {
                        buyable.SendEvent("FactionCoyotesEnemy");
                        if (Log.Verbose) Log.Info("[Multiplayer] Purchased item interaction reconciled: " + root.name);
                    }
                }
            }
            if (publish) SendNative(30, w => { w.Write(Multiplayer.ObjectId(root)); _writeVars(vars, w); });
            return true;
        }
        internal static float[] Conditions(GameObject part)
        {
            var vars = Vars(part); if (vars == null) return null;
            var fields = _variables(vars); int count = 0;
            for (int i = 0; i < fields.Length; i++) if (fields[i] is FsmFloat && fields[i].Name == "Condition") count++;
            var result = new float[count]; count = 0;
            for (int i = 0; i < fields.Length; i++) if (fields[i] is FsmFloat && fields[i].Name == "Condition") result[count++] = ((FsmFloat)fields[i]).Value;
            return result;
        }
        internal static bool SetConditions(GameObject part, float[] values)
        {
            var vars = Vars(part); if (vars == null) return false;
            var fields = _variables(vars); var baseline = _baseline(vars); int count = 0;
            for (int i = 0; i < fields.Length; i++) if (fields[i] is FsmFloat && fields[i].Name == "Condition") count++;
            if (count != values.Length) return false;
            count = 0;
            for (int i = 0; i < fields.Length; i++)
                if (fields[i] is FsmFloat && fields[i].Name == "Condition") { float value = values[count++]; ((FsmFloat)fields[i]).Value = value; baseline[i] = value; }
            return true;
        }
        internal static bool LoseCondition(GameObject part, float amount, bool melee)
        {
            var vars = Vars(part); if (vars == null) return false;
            var main = FsmUtil.Find(part, "Condition"); var primary = main != null ? main.FsmVariables.FindFsmFloat("Condition") : null;
            var fields = _variables(vars); var baseline = _baseline(vars); bool broke = false;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i] as FsmFloat;
                if (field == null || field.Name != "Condition" || (melee && !ReferenceEquals(field, primary))) continue;
                float before = field.Value; field.Value = Mathf.Max(0f, before - amount); baseline[i] = field.Value;
                if (before > 0f && field.Value <= 0f) broke = true;
            }
            return broke;
        }
        static bool BeforeVars(ulong __0, BinaryReader __1, out VarsContext __state)
        {
            __state = new VarsContext { Peer = _varsPeer, Id = _varsId }; _varsPeer = __0; _varsId = 0;
            if (!Multiplayer.Hosting) return true;
            long position = __1.BaseStream.Position;
            try { _varsId = __1.ReadInt32(); } finally { __1.BaseStream.Position = position; }
            return !ResourceLocked(_varsId) && (__0 == 0 || !ServerOwned(Multiplayer.ObjectById(_varsId)));
        }
        internal static void Consume(GameObject root)
        {
            if (!Multiplayer.Hosting || root == null) return;
            int id = Multiplayer.ObjectId(root); if (id == 0) return;
            Multiplayer.Send(133, w => { w.Write(MultiplayerParts.Epoch); w.Write(id); });
            _kick(root); UnityEngine.Object.Destroy(root);
        }
        static void ConsumeMessage(ulong from, byte kind, BinaryReader reader)
        {
            if (kind != 133 || !Multiplayer.Client || from != 0) return;
            ulong epoch = reader.ReadUInt64(); int id = reader.ReadInt32();
            if (epoch == 0 || epoch != MultiplayerParts.Epoch || reader.BaseStream.Position != reader.BaseStream.Length) return;
            var root = Multiplayer.ObjectById(id); if (root != null) { _kick(root); root.SetActive(false); UnityEngine.Object.Destroy(root); }
        }
        static Exception AfterVars(Exception __exception, VarsContext __state)
        { _varsPeer = __state.Peer; _varsId = __state.Id; return __exception; }
        static void BeforeApply(object __instance, object[] __0)
        {
            bool editing = MultiplayerEdits.WaitingForSnapshot(_varsId, _varsPeer);
            if (!Multiplayer.Hosting || _varsPeer == 0 || !editing && !MultiplayerParts.WaitingForSnapshot(_varsId, _varsPeer)) return;
            if (!ReferenceEquals(__instance, Vars(Multiplayer.ObjectById(_varsId)))) return;
            var fields = _variables(__instance);
            for (int i = 0; i < fields.Length && i < __0.Length; i++)
            {
                if (editing)
                {
                    if (fields[i] is FsmFloat) __0[i] = ((FsmFloat)fields[i]).Value;
                    else if (fields[i] is FsmInt) __0[i] = ((FsmInt)fields[i]).Value;
                    else if (fields[i] is FsmBool) __0[i] = ((FsmBool)fields[i]).Value;
                    else if (fields[i] is FsmString) __0[i] = ((FsmString)fields[i]).Value;
                }
                else if (fields[i] is FsmFloat && fields[i].Name == "Condition") __0[i] = ((FsmFloat)fields[i]).Value;
            }
        }
    }
}
