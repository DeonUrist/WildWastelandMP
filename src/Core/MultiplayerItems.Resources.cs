using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static partial class MultiplayerItems
    {
        // Grant before native credit, retain the item until its resulting debit/consumption is committed.
        // A granted operation never expires: losing its result must not grant the same contents twice.
        sealed class ResourceUse
        {
            internal int Id, Field = -1; internal uint Request, Token; internal ulong Peer;
            internal string Fsm, Event; internal GameObject Root; internal PlayMakerFSM Machine;
            internal NamedVariable Resource; internal double Before, GrantedAt, RequestedAt; internal bool Continuous, Granted, Stopped, Completed;
        }
        static readonly Queue<ResourceUse> ResourceRequests = new Queue<ResourceUse>();
        static readonly List<ResourceUse> _stale = new List<ResourceUse>();
        static readonly Dictionary<int, ResourceUse> ResourceLeases = new Dictionary<int, ResourceUse>();
        static readonly Dictionary<int, ResourceUse> LocalResourceUses = new Dictionary<int, ResourceUse>();
        static ulong _resourceEpoch;
        static uint _resourceRequest, _resourceToken;
        static bool _resourceHooks;            // session active: the ProcessEvent prefix must cost nothing offline
        const float LeaseTimeout = 120f, ContinuousTimeout = 300f, RequestTimeout = 10f;   // released with the host value kept, never fatal
        static Fsm _resourceReplay;
        static string _resourceFailure;
        internal static bool ResourceBusy { get { return _resourceFailure != null || ResourceRequests.Count != 0 || ResourceLeases.Count != 0 || LocalResourceUses.Count != 0; } }
        internal static string ResourceFailure { get { return _resourceFailure; } }
        static bool ResourceLocked(int id) { return id != 0 && (ResourceLeases.ContainsKey(id) || LocalResourceUses.ContainsKey(id)); }
        static void InstallResources()
        {
            Hooks.ProcessEvent.Prefix(Multiplayer.PatchOwner, BeforeResourceUse);
            var world = Multiplayer.Type("Net.WorldObjects");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "OnClaim"), prefix: AccessTools.Method(typeof(MultiplayerItems), "BeforeResourceClaim")))
                throw new InvalidOperationException("MP resource transaction ownership hook failed");
            Multiplayer.Message += ReceiveResource; Multiplayer.SessionChanged += ResetResources;
            Multiplayer.SessionChanged += () => _resourceHooks = Multiplayer.Active;
            Scheduler.Interval(Multiplayer.PatchOwner, "Item resource transactions", .1f, TickResources);
        }
        static void ResetResources()
        {
            if (Multiplayer.SessionActive) return;
            // Same policy as a departed peer: an open grant at session end keeps the host's item value (nothing was committed).
            if (ResourceLeases.Count != 0) Log.Warn("[Multiplayer] " + ResourceLeases.Count + " item use(s) were still open when the session ended; host item values kept");
            ResourceRequests.Clear(); ResourceLeases.Clear(); LocalResourceUses.Clear(); _resourceFailure = null;
            _resourceEpoch = 0; _resourceRequest = _resourceToken = 0; _resourceReplay = null;
        }
        static bool ResourceEpoch()
        {
            if (_resourceEpoch == MultiplayerParts.Epoch) return _resourceEpoch != 0;
            if (ResourceBusy && _resourceEpoch != 0)
            {
                // The authority epoch changed under open grants (host reset). The host keeps its item values; nothing was committed.
                Log.Warn("[Multiplayer] " + (ResourceLeases.Count + LocalResourceUses.Count) + " item use(s) dropped by an authority epoch change");
                ResourceRequests.Clear(); ResourceLeases.Clear(); LocalResourceUses.Clear();
            }
            _resourceEpoch = MultiplayerParts.Epoch; return _resourceEpoch != 0;
        }
        static void ResourceFailed(string message)
        {
            if (Multiplayer.Hosting) HostSaveSystem.ReloadRequired = true;
            if (_resourceFailure == null) { _resourceFailure = message; Log.Warn("[Multiplayer] " + message); }
        }
        static bool SupportedResource(string fsm, string ev)
        { return ev == "UseItem" && (fsm == "Ammo" || fsm == "Batteries" || fsm == "Nuts") || fsm == "Food" && ev == "Eat" || fsm == "Drink" && ev == "DrinkON"; }
        // Releasing a grant keeps the host's item values (the use is simply not committed); only a failed host commit is fatal.
        static void ReleaseResource(ResourceUse use, string why)
        {
            ResourceLeases.Remove(use.Id);
            if (use.Peer == 0) LocalResourceUses.Remove(use.Id);
            Log.Warn("[Multiplayer] Item use " + use.Fsm + "/" + use.Event + " on " + use.Id + " released: " + why);
            if (use.Peer != 0 && MultiplayerSleepers.WelcomedPeer(use.Peer)) ReplyResource(use, 2);
        }
        static bool BeforeResourceUse(Fsm fsm, FsmEvent ev, FsmEventData data)
        {
            if (!_resourceHooks || ev == null || fsm == null || !SupportedResource(fsm.Name, ev.Name) && !(fsm.Name == "Drink" && ev.Name == "DrinkOFF")) return true;
            if (!Multiplayer.Active || ReferenceEquals(fsm, _resourceReplay)) return true;
            if (fsm.Name == "Drink" && ev.Name == "DrinkOFF")
            {
                ResourceUse drinking;
                if (LocalResourceUses.TryGetValue(Multiplayer.ObjectId(fsm.GameObject), out drinking)) drinking.Stopped = true;
                return true;
            }
            if (!SupportedResource(fsm.Name, ev.Name) || fsm.GameObject == null) return true;
            var root = fsm.GameObject; int id = Multiplayer.ObjectId(root);
            if (id == 0 || Vars(root) == null) return true;
            if (!Multiplayer.WorldReady || MultiplayerSaving.Frozen || SaveBus.Busy || !Multiplayer.LocalTargetable || !ResourceEpoch() || _resourceFailure != null) return false;
            if (LocalResourceUses.ContainsKey(id)) return false;
            if (LocalResourceUses.Count >= 128) return false;
            var use = new ResourceUse { Id = id, Request = ++_resourceRequest, Root = root, Machine = fsm.Owner as PlayMakerFSM, Fsm = fsm.Name, Event = ev.Name, Continuous = ev.Name == "DrinkON", RequestedAt = Time.unscaledTime };
            LocalResourceUses.Add(id, use);
            if (Multiplayer.Hosting) ResourceRequests.Enqueue(use);
            else Multiplayer.Send(177, w => { w.Write(_resourceEpoch); w.Write(use.Request); w.Write(id); w.Write(use.Fsm); w.Write(use.Event); });
            return false;
        }
        static bool BeforeResourceClaim(ulong __0, BinaryReader __1)
        {
            if (!Multiplayer.Hosting) return true;
            long position = __1.BaseStream.Position; int id;
            try { id = __1.ReadInt32(); } finally { __1.BaseStream.Position = position; }
            return !ResourceLocked(id);
        }
        static bool FindResource(ResourceUse use)
        {
            use.Machine = FsmUtil.Find(use.Root, use.Fsm);
            if (use.Machine == null || !use.Machine.enabled || !use.Machine.Fsm.Initialized || !use.Root.activeInHierarchy || ServerOwned(use.Root)) return false;
            var sale = FsmUtil.Find(use.Root, "ForBuy"); var forSale = sale != null ? sale.FsmVariables.FindFsmInt("ForBuyInt") : null;
            if (forSale != null && forSale.Value > 0) return false;
            if (use.Fsm == "Food") return true;
            NamedVariable resource = null;
            if (use.Fsm == "Drink")
            { var liquid = FsmUtil.Find(use.Root, "LiquidAmount"); if (liquid != null) resource = liquid.FsmVariables.FindFsmFloat("Liquid"); }
            else resource = use.Machine.FsmVariables.FindFsmInt(use.Fsm == "Ammo" ? "ammo" : use.Fsm == "Batteries" ? "batteries" : "nuts");
            var vars = Vars(use.Root); if (vars == null || resource == null) return false;
            var fields = _variables(vars);
            for (int i = 0; i < fields.Length; i++) if (ReferenceEquals(fields[i], resource)) { use.Field = i; break; }
            if (use.Field < 0) return false;
            use.Resource = resource; use.Before = ResourceValue(resource);
            return use.Before > 0 && !double.IsNaN(use.Before) && !double.IsInfinity(use.Before);
        }
        static double ResourceValue(NamedVariable field)
        { return field is FsmInt ? ((FsmInt)field).Value : field is FsmFloat ? ((FsmFloat)field).Value : 0; }
        static byte[] ResourceSnapshot(GameObject root)
        {
            var vars = Vars(root); if (vars == null) return null; var fields = _variables(vars);
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write((byte)fields.Length);
                for (int i = 0; i < fields.Length; i++)
                {
                    w.Write((byte)i);
                    if (fields[i] is FsmFloat) { w.Write((byte)0); w.Write((double)((FsmFloat)fields[i]).Value); }
                    else if (fields[i] is FsmInt) { w.Write((byte)1); w.Write((double)((FsmInt)fields[i]).Value); }
                    else if (fields[i] is FsmBool) { w.Write((byte)2); w.Write(((FsmBool)fields[i]).Value); }
                    else if (fields[i] is FsmString) { w.Write((byte)3); w.Write(((FsmString)fields[i]).Value ?? ""); }
                    else return null;
                }
                return stream.Length <= 1000 ? stream.ToArray() : null;
            }
        }
        static void ReplyResource(ResourceUse use, byte mode, byte[] snapshot = null)
        {
            if (use.Peer == 0) { if (mode == 0) LocalResourceUses.Remove(use.Id); return; }
            Multiplayer.Send(178, w => { w.Write(_resourceEpoch); w.Write(use.Request); w.Write(use.Id); w.Write(mode); w.Write(use.Token); if (mode == 1) { w.Write((ushort)snapshot.Length); w.Write(snapshot); } }, use.Peer);
        }
        static void TickResources()
        {
            if (!Multiplayer.Active) { ResetResources(); return; }
            if (!ResourceEpoch()) return;
            if (Multiplayer.Client)
            {
                // A request the host never answered (dropped before admission, queue full) must not lock the item or block flushes.
                _stale.Clear();
                foreach (var use in LocalResourceUses.Values) if (!use.Granted && Time.unscaledTime - use.RequestedAt > RequestTimeout) _stale.Add(use);
                foreach (var use in _stale) { LocalResourceUses.Remove(use.Id); Log.Warn("[Multiplayer] Item use " + use.Fsm + "/" + use.Event + " on " + use.Id + " got no answer from the host; dropped"); }
                return;
            }
            if (!Multiplayer.WorldReady) return;
            _stale.Clear();
            foreach (var use in ResourceLeases.Values)
            {
                if (use.Peer != 0 && !MultiplayerSleepers.WelcomedPeer(use.Peer)) _stale.Add(use);
                else if (Time.unscaledTime - use.GrantedAt > (use.Continuous ? ContinuousTimeout : LeaseTimeout)) _stale.Add(use);
            }
            foreach (var use in _stale) ReleaseResource(use, use.Peer != 0 && !MultiplayerSleepers.WelcomedPeer(use.Peer) ? "player left before the result arrived (host item value kept)" : "no result in time (host item value kept)");
            if (_resourceFailure != null || MultiplayerSaving.Frozen || SaveBus.Busy || MultiplayerEdits.Busy) return;
            int budget = Math.Min(16, ResourceRequests.Count);
            while (budget-- > 0)
            {
                var use = ResourceRequests.Dequeue();
                if (ResourceLeases.ContainsKey(use.Id)) { ResourceRequests.Enqueue(use); continue; }
                use.Root = Multiplayer.ObjectById(use.Id);
                var player = use.Peer == 0 ? GameRefs.Player : Multiplayer.PlayerFor(use.Peer);
                if (use.Root == null || player == null || use.Peer != 0 && (!MultiplayerSleepers.WelcomedPeer(use.Peer) || !Multiplayer.RemoteTargetable(player))
                    || (player.transform.position - use.Root.transform.position).sqrMagnitude > 16f
                    || Multiplayer.OwnerOf(use.Root) != 0 && Multiplayer.OwnerOf(use.Root) != use.Peer || !FindResource(use)) { ReplyResource(use, 0); continue; }
                var snapshot = ResourceSnapshot(use.Root); if (snapshot == null) { ReplyResource(use, 0); continue; }
                use.Token = ++_resourceToken; use.Granted = true; use.GrantedAt = Time.unscaledTime; ResourceLeases.Add(use.Id, use);
                if (use.Peer == 0) ExecuteResource(use);
                else ReplyResource(use, 1, snapshot);
            }
        }
        // A local use that cannot run after its grant: hand the grant back (the host keeps its value) instead of poisoning the session.
        static void AbandonLocalUse(ResourceUse use, string why)
        {
            Log.Warn("[Multiplayer] Item use " + use.Fsm + "/" + use.Event + " on " + use.Id + " abandoned: " + why);
            if (Multiplayer.Hosting) { ReleaseResource(use, why); return; }
            LocalResourceUses.Remove(use.Id);
            Multiplayer.Send(179, w => { w.Write(_resourceEpoch); w.Write(use.Request); w.Write(use.Id); w.Write(use.Token); w.Write(false); w.Write(double.NaN); });
        }
        static void ExecuteResource(ResourceUse use)
        {
            if (!use.Stopped)
            {
                var previous = _resourceReplay; _resourceReplay = use.Machine.Fsm;
                try { use.Machine.SendEvent(use.Event); }
                catch (Exception e) { AbandonLocalUse(use, "the item event threw: " + e.Message); return; }
                finally { _resourceReplay = previous; }
            }
            Scheduler.StartCoroutine(FinishResource(use));
        }
        static IEnumerator FinishResource(ResourceUse use)
        {
            yield return null;
            while (use.Continuous && use.Root != null && use.Machine != null && use.Machine.enabled && use.Machine.ActiveStateName == "on")
            {
                if (!Multiplayer.Active || _resourceEpoch != MultiplayerParts.Epoch) yield break;
                yield return null;
            }
            if (!Multiplayer.Active || _resourceEpoch != MultiplayerParts.Epoch) yield break;
            double value = use.Resource != null ? ResourceValue(use.Resource) : 0;
            bool consumed = use.Root == null;
            use.Completed = true;
            if (Multiplayer.Hosting) CompleteResource(use, consumed, value);
            else Multiplayer.Send(179, w => { w.Write(_resourceEpoch); w.Write(use.Request); w.Write(use.Id); w.Write(use.Token); w.Write(consumed); w.Write(value); });
        }
        static void CompleteResource(ResourceUse use, bool consumed, double value)
        {
            bool countable = use.Resource != null;
            if (countable && !consumed && (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > use.Before || use.Resource is FsmInt && value != Math.Truncate(value)))
            { ReleaseResource(use, "invalid result " + value + " (before " + use.Before + "); host item value kept"); return; }
            // Food/Nuts without a counted resource: a root that survives (sanity_pills decrement Quantity instead of DestroySelf)
            // is a normal outcome; its variables follow the ordinary edit/snapshot path.
            var root = Multiplayer.ObjectById(use.Id);
            if (consumed)
            {
                // The local native DestroySelf may already have removed its tracked root.
                // Publish even then, and order this before the next claimant can receive a grant.
                Multiplayer.Send(133, w => { w.Write(_resourceEpoch); w.Write(use.Id); });
                if (root != null) { _kick(root); UnityEngine.Object.Destroy(root); }
            }
            else if (root != null && use.Field >= 0)
            {
                using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
                {
                    w.Write((byte)1); w.Write((byte)use.Field); w.Write((byte)(use.Resource is FsmInt ? 1 : 0)); w.Write(value);
                    if (!Edit(root, stream.ToArray(), true, true)) { ResourceFailed("The host could not commit an approved item use; saving is blocked."); return; }
                }
            }
            ResourceLeases.Remove(use.Id); ReplyResource(use, 2);
            if (use.Peer == 0) LocalResourceUses.Remove(use.Id);
        }
        static void ReceiveResource(ulong from, byte kind, BinaryReader r)
        {
            if (kind < 177 || kind > 179 || !Multiplayer.WorldReady || !ResourceEpoch() || r.ReadUInt64() != _resourceEpoch) return;
            uint request = r.ReadUInt32(); int id = r.ReadInt32();
            if (kind == 177)
            {
                string fsm = r.ReadString(), ev = r.ReadString();
                if (!Multiplayer.Hosting || from == 0 || !MultiplayerSleepers.WelcomedPeer(from) || !SupportedResource(fsm, ev) || r.BaseStream.Position != r.BaseStream.Length || ResourceRequests.Count >= 1024) return;
                var use = new ResourceUse { Id = id, Request = request, Peer = from, Fsm = fsm, Event = ev, Continuous = ev == "DrinkON" };
                if (MultiplayerSaving.Frozen || SaveBus.Busy || _resourceFailure != null) { ReplyResource(use, 0); return; }
                ResourceRequests.Enqueue(use); return;
            }
            if (kind == 179)
            {
                uint token = r.ReadUInt32(); bool consumed = r.ReadBoolean(); double value = r.ReadDouble(); ResourceUse use;
                if (!Multiplayer.Hosting || from == 0 || r.BaseStream.Position != r.BaseStream.Length || !ResourceLeases.TryGetValue(id, out use) || use.Peer != from || use.Request != request || use.Token != token) return;
                CompleteResource(use, consumed, value); return;
            }
            if (!Multiplayer.Client || from != 0) return;
            byte mode = r.ReadByte(); uint grant = r.ReadUInt32(); ResourceUse local;
            if (mode > 2) return;
            if (!LocalResourceUses.TryGetValue(id, out local) || local.Request != request)
            {
                // A grant for a request this side already dropped (10 s without an answer): hand it straight back so the host
                // releases the lease now instead of holding the item for the lease timeout.
                if (mode == 1) Multiplayer.Send(179, w => { w.Write(_resourceEpoch); w.Write(request); w.Write(id); w.Write(grant); w.Write(false); w.Write(double.NaN); });
                return;
            }
            byte[] snapshot = null;
            if (mode == 1) { int size = r.ReadUInt16(); if (size < 1 || size > 1000) return; snapshot = r.ReadBytes(size); }
            if (r.BaseStream.Position != r.BaseStream.Length) return;
            if (mode != 1)
            { if (mode == 0 || local.Token == grant) LocalResourceUses.Remove(id); return; }
            if (local.Granted) return;
            local.Token = grant; local.Granted = true;
            if (local.Root == null || !Edit(local.Root, snapshot, true, true, false) || !FindResource(local))
            { AbandonLocalUse(local, "the granted item could not be restored locally"); return; }
            ExecuteResource(local);
        }
    }
}
