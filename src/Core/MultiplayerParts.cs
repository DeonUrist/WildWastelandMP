using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WildWasteland.Core
{
    // Host approves damage; the current part owner applies it to its live wear/repair state once.
    internal static class MultiplayerParts
    {
        internal static Func<GameObject, float, byte, float> DamageLoss;
        internal static Func<GameObject, bool> CanPop;
        internal static Action<GameObject, Vector3> Pop;
        sealed class Hit { internal GameObject Part; internal float Damage, Loss; internal byte Mode; internal Vector3 Direction; }
        sealed class Work
        {
            internal GameObject Part; internal readonly Queue<Hit> Queue = new Queue<Hit>();
            internal readonly Dictionary<ulong, uint> Barriers = new Dictionary<ulong, uint>();
            internal Hit Current; internal uint Command, Revision; internal ulong Owner; internal bool Pop; internal float Retry, SnapshotRetry;
        }
        sealed class Receipt { internal uint Command; internal bool Applied; internal float[] Values; }
        sealed class Snapshot { internal ulong Epoch; internal uint Command; internal int Id; internal float[] Values; internal float Expires; }
        static readonly Dictionary<int, Work> Parts = new Dictionary<int, Work>();
        static readonly Dictionary<int, Receipt> Receipts = new Dictionary<int, Receipt>();
        static readonly Dictionary<int, Snapshot> Inbox = new Dictionary<int, Snapshot>();
        static readonly Dictionary<ulong, uint> Requests = new Dictionary<ulong, uint>();
        static readonly Queue<Hit> Local = new Queue<Hit>();
        static readonly List<int> Gone = new List<int>();
        static readonly List<ulong> GonePeers = new List<ulong>();
        static ulong _epoch, _hello;
        static uint _command, _request;
        static int _generation = -1, _queued;
        static float _nextHello, _nextCapacityLog, _nextSweep;
        internal static bool Pending
        {
            get { if (Local.Count != 0 || _queued != 0 || Inbox.Count != 0) return true; foreach (var work in Parts.Values) if (work.Barriers.Count != 0) return true; return false; }
        }
        internal static ulong Epoch { get { return _epoch; } }
        internal static void Install()
        {
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
            Scheduler.Interval(Multiplayer.PatchOwner, "Part damage authority", .1f, Tick);
            Reset();
        }
        static void Reset()
        {
            Parts.Clear(); Receipts.Clear(); Inbox.Clear(); Requests.Clear(); Local.Clear(); Gone.Clear();
            _epoch = Multiplayer.Hosting ? BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) : 0;
            _hello = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); _command = _request = 0; _queued = 0;
            _generation = SaveBus.Generation; _nextHello = _nextSweep = 0;
        }
        static bool Part(GameObject part)
        { return part != null && (part.CompareTag("vehPart") || part.CompareTag("vehPartRemoved")); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool Finite(Vector3 value) { return Finite(value.sqrMagnitude); }
        internal static bool HitPart(GameObject part, float damage, byte mode, Vector3 direction)
        {
            if (!Multiplayer.Active) return false;
            if (Part(part) && Finite(damage) && Finite(direction) && Mathf.Abs(damage) > .001f && Local.Count < 1024)
                Local.Enqueue(new Hit { Part = part, Damage = damage, Mode = mode, Direction = direction.normalized });
            else if (Local.Count >= 1024) Capacity();
            return true;
        }
        static void Capacity()
        { if (Time.unscaledTime >= _nextCapacityLog) { _nextCapacityLog = Time.unscaledTime + 5f; Log.Warn("[Multiplayer] Part damage queue limit reached (1024); excess requests rejected"); } }
        internal static bool WaitingForSnapshot(int id, ulong peer)
        { Work work; return Parts.TryGetValue(id, out work) && work.Barriers.ContainsKey(peer); }
        static void Queue(Hit hit)
        {
            int id = Multiplayer.ObjectId(hit.Part);
            if (id == 0 || hit.Mode != 2 && DamageLoss == null) return;
            if (_queued >= 1024) { Capacity(); return; }
            var fields = MultiplayerItems.Conditions(hit.Part); if (fields != null && fields.Length == 0) return;
            float loss = hit.Mode == 2 ? Mathf.Abs(hit.Damage) : DamageLoss(hit.Part, hit.Damage, hit.Mode);
            if (!(loss > 0f) || !Finite(loss)) return;
            Work work;
            if (!Parts.TryGetValue(id, out work) || work.Part != hit.Part) { work = new Work { Part = hit.Part }; Parts[id] = work; }
            hit.Loss = loss; work.Queue.Enqueue(hit); _queued++;
        }
        static void Tick()
        {
            if (_generation != SaveBus.Generation) Reset();
            if (!Multiplayer.WorldReady) return;
            if (Multiplayer.Client && _epoch == 0 && Time.unscaledTime >= _nextHello)
            { _nextHello = Time.unscaledTime + 1f; Multiplayer.Send(56, w => w.Write(_hello)); }
            if (_epoch == 0) return;
            int local = Math.Min(32, Local.Count);
            while (local-- > 0)
            {
                var hit = Local.Dequeue(); if (!Part(hit.Part)) continue;
                int id = Multiplayer.ObjectId(hit.Part);
                if (id == 0) { Local.Enqueue(hit); continue; }
                if (Multiplayer.Hosting) Queue(hit);
                else
                {
                    uint request = ++_request;
                    Multiplayer.Send(52, w => { w.Write(_epoch); w.Write(request); w.Write(id); w.Write(hit.Damage); w.Write(hit.Mode); WriteVector(w, hit.Direction); });
                }
            }
            if (Multiplayer.Client)
            {
                Gone.Clear();
                foreach (var pair in Inbox)
                {
                    var snapshot = pair.Value;
                    if (snapshot.Epoch != _epoch || Time.unscaledTime >= snapshot.Expires) { Gone.Add(pair.Key); continue; }
                    if (ApplySnapshot(snapshot)) Gone.Add(pair.Key);
                }
                foreach (int id in Gone) Inbox.Remove(id);
                if (Time.unscaledTime >= _nextSweep)
                {
                    _nextSweep = Time.unscaledTime + 5f; Gone.Clear();
                    foreach (var pair in Receipts) if (Multiplayer.ObjectById(pair.Key) == null) Gone.Add(pair.Key);
                    foreach (int id in Gone) Receipts.Remove(id);
                }
                return;
            }
            if (SaveBus.Busy) return;
            Gone.Clear(); int budget = 32;
            foreach (var pair in Parts)
            {
                var work = pair.Value;
                if (work.Part == null) { _queued -= work.Queue.Count + (work.Current != null ? 1 : 0); Gone.Add(pair.Key); continue; }
                if (work.Current == null && work.Queue.Count == 0 && work.Barriers.Count == 0) { Gone.Add(pair.Key); continue; }
                if (budget <= 0) continue;
                if (work.Barriers.Count > 0 && Time.unscaledTime >= work.SnapshotRetry)
                {
                    work.SnapshotRetry = Time.unscaledTime + 1f; GonePeers.Clear();
                    foreach (var barrier in work.Barriers)
                    {
                        if (Multiplayer.PlayerFor(barrier.Key) == null) { GonePeers.Add(barrier.Key); continue; }
                        if (budget <= 0) break;
                        SendSnapshot(pair.Key, work, MultiplayerItems.Conditions(work.Part), barrier.Key); budget--;
                    }
                    foreach (ulong peer in GonePeers) work.Barriers.Remove(peer);
                }
                if (budget <= 0) continue;
                if (work.Current == null && work.Queue.Count > 0 && MultiplayerItems.Conditions(work.Part) != null)
                {
                    work.Current = work.Queue.Dequeue(); work.Command = ++_command; work.Owner = Multiplayer.OwnerOf(work.Part);
                    work.Pop = work.Current.Mode != 2 && CanPop != null && CanPop(work.Part); work.Retry = 0;
                }
                if (work.Current == null) continue;
                if (work.Owner != 0 && Multiplayer.PlayerFor(work.Owner) == null)
                { work.Owner = Multiplayer.OwnerOf(work.Part); work.Command = ++_command; work.Retry = 0; }
                if (work.Owner == 0)
                {
                    Apply(work.Part, work.Current.Loss, work.Current.Mode, work.Pop, work.Current.Direction);
                    Complete(pair.Key, work, MultiplayerItems.Conditions(work.Part), 0); budget--; continue;
                }
                if (Time.unscaledTime < work.Retry) continue;
                work.Retry = Time.unscaledTime + 1f; budget--;
                Multiplayer.Send(53, w => { w.Write(_epoch); w.Write(work.Command); w.Write(pair.Key); w.Write(work.Current.Loss); w.Write(work.Current.Mode); w.Write(work.Pop); WriteVector(w, work.Current.Direction); }, work.Owner);
            }
            foreach (int id in Gone) Parts.Remove(id);
        }
        static void Apply(GameObject part, float loss, byte mode, bool pop, Vector3 direction)
        {
            bool broke = MultiplayerItems.LoseCondition(part, loss, mode != 0);
            if (broke && pop && Pop != null) Pop(part, direction);
        }
        static void Complete(int id, Work work, float[] values, ulong appliedBy)
        {
            if (values == null || !MultiplayerItems.SetConditions(work.Part, values)) return;
            work.Current = null; work.Revision = work.Command; work.SnapshotRetry = Time.unscaledTime + 1f; _queued--;
            foreach (var peer in Peers())
            {
                if (peer == appliedBy) { work.Barriers.Remove(peer); continue; }
                work.Barriers[peer] = work.Revision; SendSnapshot(id, work, values, peer);
            }
            if (Log.Verbose) Log.Info("[Multiplayer] Part damage settled: " + work.Part.name + ", operation " + work.Command + ", owner " + appliedBy);
        }
        static IEnumerable<ulong> Peers()
        { for (int i = 0; i < Multiplayer.RemoteCount; i++) { var player = Multiplayer.RemoteAt(i); if (player != null) yield return Multiplayer.PeerFor(player); } }
        static void SendSnapshot(int id, Work work, float[] values, ulong peer)
        { if (values != null) Multiplayer.Send(55, w => { w.Write(_epoch); w.Write(work.Revision); w.Write(id); WriteValues(w, values); }, peer); }
        static void WriteVector(BinaryWriter w, Vector3 value) { w.Write(value.x); w.Write(value.y); w.Write(value.z); }
        static Vector3 ReadVector(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        static void WriteValues(BinaryWriter w, float[] values) { w.Write((byte)values.Length); foreach (float value in values) w.Write(value); }
        static float[] ReadValues(BinaryReader r)
        {
            int count = r.ReadByte(); var values = new float[count];
            for (int i = 0; i < count; i++) { values[i] = r.ReadSingle(); if (!Finite(values[i])) throw new InvalidDataException("Non-finite condition"); }
            return values;
        }
        static bool End(BinaryReader r) { return r.BaseStream.Position == r.BaseStream.Length; }
        static void Reply(int id, Receipt receipt)
        { Multiplayer.Send(54, w => { w.Write(_epoch); w.Write(receipt.Command); w.Write(id); w.Write(receipt.Applied); if (receipt.Applied) WriteValues(w, receipt.Values); }); }
        static void AckSnapshot(Snapshot snapshot)
        { Multiplayer.Send(58, w => { w.Write(_epoch); w.Write(snapshot.Command); w.Write(snapshot.Id); }); }
        static bool ApplySnapshot(Snapshot snapshot)
        {
            if (snapshot.Epoch != _epoch || _epoch == 0) return false;
            Receipt receipt;
            if (Receipts.TryGetValue(snapshot.Id, out receipt) && receipt.Command >= snapshot.Command)
            { AckSnapshot(snapshot); return true; }
            var part = Multiplayer.ObjectById(snapshot.Id);
            if (!Part(part) || !MultiplayerItems.SetConditions(part, snapshot.Values)) return false;
            Receipts[snapshot.Id] = new Receipt { Command = snapshot.Command, Applied = false, Values = snapshot.Values };
            AckSnapshot(snapshot); return true;
        }
        static void Receive(ulong from, byte kind, BinaryReader r)
        {
            if (kind < 52 || kind > 58 || !Multiplayer.WorldReady) return;
            if (_generation != SaveBus.Generation) Reset();
            if (kind == 56)
            {
                if (!Multiplayer.Hosting || from == 0 || !MultiplayerSleepers.WelcomedPeer(from)) return;
                ulong hello = r.ReadUInt64(); if (End(r)) { MultiplayerHandshake.ClientReady(from, hello); Multiplayer.Send(57, w => { w.Write(hello); w.Write(_epoch); }, from); }
                return;
            }
            if (kind == 57)
            {
                if (!Multiplayer.Client || from != 0) return;
                ulong hello = r.ReadUInt64(), epoch = r.ReadUInt64(); if (End(r) && hello == _hello && epoch != 0) _epoch = epoch;
                return;
            }
            if (Multiplayer.Client ? from != 0 : from == 0 || Multiplayer.PlayerFor(from) == null) return;
            ulong packetEpoch = r.ReadUInt64();
            if (packetEpoch != _epoch && !(Multiplayer.Client && _epoch == 0 && kind == 55)) return;
            uint command = r.ReadUInt32(); int id = r.ReadInt32(); var part = Multiplayer.ObjectById(id);
            if (kind == 55)
            {
                if (!Multiplayer.Client) return;
                var values = ReadValues(r); if (!End(r)) return;
                Snapshot previous; Receipt receipt;
                if (Inbox.TryGetValue(id, out previous) && previous.Command > command) return;
                if (Receipts.TryGetValue(id, out receipt) && receipt.Command > command) return;
                var snapshot = new Snapshot { Epoch = packetEpoch, Command = command, Id = id, Values = values, Expires = Time.unscaledTime + 30f };
                if (ApplySnapshot(snapshot)) Inbox.Remove(id);
                else if (Inbox.Count < 1024 || Inbox.ContainsKey(id)) Inbox[id] = snapshot;
                return;
            }
            if (kind == 58)
            {
                Work work; uint waiting;
                if (Multiplayer.Hosting && End(r) && Parts.TryGetValue(id, out work) && work.Barriers.TryGetValue(from, out waiting) && command >= waiting) work.Barriers.Remove(from);
                return;
            }
            if (kind == 52)
            {
                if (!Multiplayer.Hosting) return;
                float damage = r.ReadSingle(); byte mode = r.ReadByte(); var direction = ReadVector(r); uint last;
                if (!End(r) || !Part(part) || mode > 2 || !Finite(damage) || !Finite(direction) || Mathf.Abs(damage) < .001f
                    || (Requests.TryGetValue(from, out last) && command <= last)) return;
                var player = Multiplayer.PlayerFor(from); float range = mode == 1 ? 4f : 1000f;
                if (mode != 1 && Services.Guns != null) range = 4f;
                if (mode != 1 && Services.Guns != null) for (int i = 0; i < 6; i++) range = Mathf.Max(range, Services.Guns.EffectiveRange(i) + 15f);
                if ((part.transform.position - player.transform.position).sqrMagnitude > range * range) return;
                Requests[from] = command; Queue(new Hit { Part = part, Damage = damage, Mode = mode, Direction = direction.normalized }); return;
            }
            if (kind == 53)
            {
                if (!Multiplayer.Client) return;
                float loss = r.ReadSingle(); byte mode = r.ReadByte(); bool pop = r.ReadBoolean(); var direction = ReadVector(r);
                if (!End(r) || !(loss > 0f) || !Finite(loss) || !Finite(direction) || mode > 2 || !Part(part) || MultiplayerItems.Conditions(part) == null) return;
                Snapshot pending;
                if (Inbox.TryGetValue(id, out pending)) { if (!ApplySnapshot(pending)) return; Inbox.Remove(id); }
                Receipt receipt;
                if (Receipts.TryGetValue(id, out receipt) && command <= receipt.Command) { if (command == receipt.Command) Reply(id, receipt); return; }
                receipt = new Receipt { Command = command, Applied = !Multiplayer.OwnedElsewhere(part) };
                if (receipt.Applied) { Apply(part, loss, mode, pop, direction); receipt.Values = MultiplayerItems.Conditions(part); }
                Receipts[id] = receipt; Reply(id, receipt); return;
            }
            if (kind == 54)
            {
                if (!Multiplayer.Hosting) return;
                bool applied = r.ReadBoolean(); var values = applied ? ReadValues(r) : null; Work work;
                if (!End(r) || !Parts.TryGetValue(id, out work) || work.Current == null || work.Command != command || work.Owner != from) return;
                if (applied) Complete(id, work, values, from);
                else if (Multiplayer.OwnerOf(work.Part) != from) { work.Owner = Multiplayer.OwnerOf(work.Part); work.Command = ++_command; work.Retry = 0; }
            }
        }
    }
}
