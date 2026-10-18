using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Guns
{
    internal static partial class Tracers
    {
        struct BoltKey : IEquatable<BoltKey>
        {
            internal ulong Epoch, Peer; internal uint Sequence;
            public bool Equals(BoltKey other) { return Epoch == other.Epoch && Peer == other.Peer && Sequence == other.Sequence; }
            public override bool Equals(object other) { return other is BoltKey && Equals((BoltKey)other); }
            public override int GetHashCode() { return Epoch.GetHashCode() ^ Peer.GetHashCode() ^ Sequence.GetHashCode(); }
        }
        sealed class NetworkBolt
        {
            internal BoltKey Key; internal Transform Visual, Anchor; internal Vector3 Start, Direction, Position, LocalPosition;
            internal Quaternion Rotation, LocalRotation; internal float Speed, Range, Until, Next;
            internal int Root; internal string Path = ""; internal byte Mode; internal uint Revision; internal bool Settled;
        }
        sealed class BoltCorpse { internal Transform Body; internal float Until; }
        static readonly Dictionary<BoltKey, NetworkBolt> NetworkBolts = new Dictionary<BoltKey, NetworkBolt>();
        static readonly Dictionary<BoltKey, float> BoltTombstones = new Dictionary<BoltKey, float>();
        static readonly Dictionary<int, BoltCorpse> BoltCorpses = new Dictionary<int, BoltCorpse>();
        static readonly List<BoltKey> BoltGone = new List<BoltKey>();
        static readonly List<int> CorpseGone = new List<int>();
        static readonly Dictionary<ulong, float> BoltResync = new Dictionary<ulong, float>();
        static ulong _boltEpoch; static int _boltGeneration = -1; static float _boltHello;

        static void WriteKey(BinaryWriter w, BoltKey key) { w.Write(key.Epoch); w.Write(key.Peer); w.Write(key.Sequence); }
        static BoltKey ReadKey(BinaryReader r) { return new BoltKey { Epoch = r.ReadUInt64(), Peer = r.ReadUInt64(), Sequence = r.ReadUInt32() }; }
        static void WriteRotation(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        static Quaternion ReadRotation(BinaryReader r) { return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        static bool ValidRotation(Quaternion q)
        { float length = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w; return length > .9f && length < 1.1f; }
        static bool ValidPath(string path) { return path.Length <= 256 && System.Text.Encoding.UTF8.GetByteCount(path) <= 700 && path.IndexOf('\0') < 0; }
        static bool CurrentKey(BoltKey key) { return key.Epoch != 0 && key.Epoch == MultiplayerParts.Epoch && key.Sequence != 0; }
        static void ResetNetworkBolts()
        {
            foreach (var b in NetworkBolts.Values) if (b.Visual != null) UnityEngine.Object.Destroy(b.Visual.gameObject);
            NetworkBolts.Clear(); BoltTombstones.Clear(); BoltCorpses.Clear(); BoltResync.Clear(); ImpactReceipts.Clear();
            _boltEpoch = MultiplayerParts.Epoch; _boltGeneration = SaveBus.Generation; _boltHello = 0;
        }
        static void CheckBoltWorld()
        { if (_boltEpoch != MultiplayerParts.Epoch || _boltGeneration != SaveBus.Generation) ResetNetworkBolts(); }
        static bool BoltAlreadySettled(BoltKey key)
        { NetworkBolt b; return BoltTombstones.ContainsKey(key) || NetworkBolts.TryGetValue(key, out b) && b.Settled; }
        static void RememberBolt(ref Shot shot)
        {
            if (!shot.Bolt || !CurrentKey(shot.NetworkKey)) return;
            CheckBoltWorld();
            if (NetworkBolts.ContainsKey(shot.NetworkKey) || BoltTombstones.ContainsKey(shot.NetworkKey) || NetworkBolts.Count >= 512) return;
            NetworkBolts[shot.NetworkKey] = new NetworkBolt { Key = shot.NetworkKey, Visual = shot.Visual,
                Start = Origin.World(shot.Start), Direction = shot.Dir, Speed = shot.Speed, Range = shot.Range,
                Until = Time.time + Mathf.Min(120f, shot.Range / Mathf.Max(1f, shot.Speed)) + 20f };
        }
        static bool BoltAnchor(Transform anchor, out int root, out string path)
        {
            for (var t = anchor; t != null; t = t.parent)
            {
                int id = Multiplayer.ObjectId(t.gameObject);
                if (id == 0) continue;
                root = id; path = BoltBonePath(anchor, t); return path != null && ValidPath(path);
            }
            root = 0; path = ""; return false;
        }
        static void CaptureBolt(NetworkBolt b)
        {
            b.Position = Origin.World(b.Visual.position); b.Rotation = b.Visual.rotation;
            if (b.Mode != 0) { b.Root = 0; b.Path = ""; b.LocalPosition = Vector3.zero; b.LocalRotation = Quaternion.identity; return; }
            b.Anchor = b.Visual.parent;
            if (BoltAnchor(b.Anchor, out b.Root, out b.Path))
            { b.LocalPosition = b.Visual.localPosition; b.LocalRotation = b.Visual.localRotation; }
            else { b.Root = 0; b.Path = ""; b.LocalPosition = Vector3.zero; b.LocalRotation = Quaternion.identity; }
        }
        static void WriteBolt(BinaryWriter w, NetworkBolt b)
        {
            WriteKey(w, b.Key); w.Write(b.Revision); w.Write(b.Mode); w.Write(b.Root); w.Write(b.Path);
            WriteVector(w, b.LocalPosition); WriteRotation(w, b.LocalRotation);
            WriteVector(w, b.Position); WriteRotation(w, b.Rotation); w.Write(Mathf.Clamp(b.Until - Time.time, 0f, 15f));
        }
        static NetworkBolt ReadBolt(BinaryReader r)
        {
            var b = new NetworkBolt { Key = ReadKey(r), Revision = r.ReadUInt32(), Mode = r.ReadByte(), Root = r.ReadInt32(), Path = r.ReadString(),
                LocalPosition = ReadVector(r), LocalRotation = ReadRotation(r), Position = ReadVector(r), Rotation = ReadRotation(r) };
            float ttl = r.ReadSingle();
            if (!CurrentKey(b.Key) || !ValidPath(b.Path) || b.Mode > 2 || !Finite(b.LocalPosition) || b.LocalPosition.sqrMagnitude > 400f
                || !Finite(b.Position) || !ValidRotation(b.LocalRotation) || !ValidRotation(b.Rotation) || !(ttl >= 0f && ttl <= 15f)
                || r.BaseStream.Position != r.BaseStream.Length) return null;
            b.Until = Time.time + ttl; b.Settled = true; return b;
        }
        static void PublishBolt(NetworkBolt b, bool reliable, ulong? peer = null)
        { b.Revision++; Multiplayer.Send(126, w => WriteBolt(w, b), peer, reliable); }
        static void NetworkBoltLanded(ref Shot shot, Transform visual, Transform anchor, bool soft, Vector3 normal)
        {
            if (!Multiplayer.WorldReady || !CurrentKey(shot.NetworkKey)) return;
            NetworkBolt b;
            if (!NetworkBolts.TryGetValue(shot.NetworkKey, out b)) return;
            b.Visual = visual; b.Mode = soft ? (byte)0 : (byte)1; b.Settled = true; b.Until = Time.time + 15f; CaptureBolt(b);
            if (Multiplayer.Hosting) PublishBolt(b, true);
            else
            {
                // The normal is only for the host's ricochet. The chosen skin/bone point is never re-raycast there.
                Multiplayer.Send(125, w => { WriteVector(w, normal); WriteBolt(w, b); });
            }
        }
        static void StopNetworkFlight(BoltKey key, Transform keep = null)
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i]; if (!s.NetworkKey.Equals(key)) continue;
                if (s.Visual != null && s.Visual != keep) UnityEngine.Object.Destroy(s.Visual.gameObject);
                _shots.RemoveAt(i);
            }
        }
        static Transform ResolveBoltAnchor(NetworkBolt b)
        {
            var root = Multiplayer.ObjectById(b.Root);
            if (root == null) { BoltCorpse corpse; if (Multiplayer.Hosting && BoltCorpses.TryGetValue(b.Root, out corpse)) return b.Path.Length == 0 ? corpse.Body : corpse.Body != null ? corpse.Body.Find(b.Path) : null; return null; }
            return b.Path.Length == 0 ? root.transform : root.transform.Find(b.Path);
        }
        static void PresentBolt(NetworkBolt b)
        {
            if (b.Visual == null)
            {
                var s = new Shot { Bolt = true, Dir = b.Rotation * Vector3.forward, Pos = Origin.Scene(b.Position) };
                LaunchBolt(ref s); b.Visual = s.Visual; if (b.Visual == null) return;
            }
            var anchor = b.Mode == 0 && b.Root != 0 ? ResolveBoltAnchor(b) : null;
            if (b.Visual.parent != anchor) b.Visual.SetParent(anchor, false);
            if (anchor != null)
            {
                b.Visual.localPosition = b.LocalPosition; b.Visual.localRotation = b.LocalRotation; b.Visual.gameObject.layer = anchor.gameObject.layer;
                var scale = anchor.lossyScale;
                b.Visual.localScale = new Vector3(_boltScale.x / (Mathf.Abs(scale.x) > .000001f ? scale.x : 1f),
                    _boltScale.y / (Mathf.Abs(scale.y) > .000001f ? scale.y : 1f), _boltScale.z / (Mathf.Abs(scale.z) > .000001f ? scale.z : 1f));
            }
            else { b.Visual.localScale = _boltScale; b.Visual.SetPositionAndRotation(Origin.Scene(b.Position), b.Rotation); }
            b.Anchor = anchor;
        }
        static void HostRicochet(NetworkBolt b, Vector3 normal)
        {
            if (b.Visual == null || b.Mode != 1) return;
            var bounds = _boltMesh.bounds; var physics = new GameObject("BoltPhysics") { layer = 2 }; physics.transform.SetParent(b.Visual, false);
            var col = physics.AddComponent<CapsuleCollider>(); col.direction = 2; col.center = bounds.center; col.height = bounds.size.z;
            col.radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
            var rb = b.Visual.gameObject.AddComponent<Rigidbody>(); rb.mass = .03f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.velocity = Vector3.Reflect(b.Direction, normal) * Mathf.Min(b.Speed * .15f, 8f) + normal;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 10f;
        }
        static void ReceiveBolt(ulong from, byte kind, BinaryReader r)
        {
            if (kind < 125 || kind > 130 || !Multiplayer.WorldReady) return;
            CheckBoltWorld();
            if (kind >= 128) { ReceivePlayerImpact(from, kind, r); return; }
            if (kind == 127)
            {
                ulong epoch = r.ReadUInt64(); float next;
                if (!Multiplayer.Hosting || from == 0 || Multiplayer.PlayerFor(from) == null || epoch != MultiplayerParts.Epoch
                    || r.BaseStream.Position != r.BaseStream.Length || BoltResync.TryGetValue(from, out next) && Time.unscaledTime < next) return;
                BoltResync[from] = Time.unscaledTime + 2f;
                foreach (var bolt in NetworkBolts.Values) if (bolt.Settled && bolt.Until > Time.time) PublishBolt(bolt, true, from);
                return;
            }
            if (kind == 125 ? !Multiplayer.Hosting || from == 0 : !Multiplayer.Client || from != 0) return;
            Vector3 normal = kind == 125 ? ReadVector(r) : Vector3.up;
            var b = ReadBolt(r); if (b == null) return;
            NetworkBolt old; NetworkBolts.TryGetValue(b.Key, out old);
            if (kind == 125)
            {
                if (b.Key.Peer != from || old == null || old.Settled || BoltTombstones.ContainsKey(b.Key) || b.Mode == 2
                    || !Finite(normal) || normal.sqrMagnitude < .5f || normal.sqrMagnitude > 1.5f
                    || (b.Position - old.Start).sqrMagnitude > (old.Range + 20f) * (old.Range + 20f)) return;
                var player = Multiplayer.PlayerFor(from); if (player == null) return;
                b.Direction = old.Direction; b.Speed = old.Speed; b.Range = old.Range; b.Start = old.Start;
                // A tracked anchor may arrive after its native spawn, but an existing root must contain the exact path.
                var root = Multiplayer.ObjectById(b.Root);
                if (b.Root != 0 && root != null && ResolveBoltAnchor(b) == null) return;
                b.Visual = old.Visual; StopNetworkFlight(b.Key, b.Visual);
                NetworkBolts[b.Key] = b; PresentBolt(b); HostRicochet(b, normal.normalized);
                if (b.Visual != null)
                {
                    var owner = b.Anchor != null ? Creature(b.Anchor) : null;
                    _landedBolts.Add(new LandedBolt { Visual = b.Visual, Owner = owner, Bone = owner != null ? BoltBonePath(b.Anchor, owner) : null, Until = b.Until });
                }
                CaptureBoltIfVisible(b); PublishBolt(b, true);
                if (Log.Verbose) Log.Info("[Multiplayer] Bolt accepted: player " + from + " shot " + b.Key.Sequence + ", anchor " + b.Root + "/" + b.Path);
            }
            else
            {
                if (BoltTombstones.ContainsKey(b.Key) || old != null && b.Revision <= old.Revision) return;
                if (b.Mode == 2)
                {
                    StopNetworkFlight(b.Key); if (old != null && old.Visual != null) UnityEngine.Object.Destroy(old.Visual.gameObject);
                    NetworkBolts.Remove(b.Key); BoltTombstones[b.Key] = Time.time + 30f; return;
                }
                if (old == null && NetworkBolts.Count >= 512) return;
                b.Visual = old != null ? old.Visual : null; StopNetworkFlight(b.Key, b.Visual);
                // Authoritative expiry replaces the shooter's predicted landed-bolt lifetime.
                if (b.Visual != null) for (int i = _landedBolts.Count - 1; i >= 0; i--) if (_landedBolts[i].Visual == b.Visual) _landedBolts.RemoveAt(i);
                NetworkBolts[b.Key] = b; PresentBolt(b);
            }
        }
        static void CaptureBoltIfVisible(NetworkBolt b)
        {
            if (b.Visual == null) return;
            if (b.Mode == 0 && b.Root != 0 && b.Visual.parent == null)
            {
                if (ResolveBoltAnchor(b) == null) return;
                PresentBolt(b);
            }
            CaptureBolt(b);
        }
        static void TickNetworkBolts()
        {
            if (!Multiplayer.Active) return;
            CheckBoltWorld(); if (!Multiplayer.WorldReady) return;
            if (Multiplayer.Client && Time.unscaledTime >= _boltHello && MultiplayerParts.Epoch != 0)
            { _boltHello = Time.unscaledTime + 3f; Multiplayer.Send(127, w => w.Write(MultiplayerParts.Epoch)); }
            BoltGone.Clear();
            foreach (var pair in NetworkBolts)
            {
                var b = pair.Value;
                if (Time.time >= b.Until)
                {
                    if (Multiplayer.Hosting && b.Settled) { b.Mode = 2; PublishBolt(b, true); }
                    if (b.Visual != null) UnityEngine.Object.Destroy(b.Visual.gameObject);
                    BoltGone.Add(pair.Key); continue;
                }
                if (!b.Settled) continue;
                if (Multiplayer.Client) { PresentBolt(b); continue; }
                int root = b.Root; string path = b.Path;
                // The existing corpse hook reparents the real bolt. Capture that exact local transform for all peers.
                CaptureBoltIfVisible(b);
                if (root != b.Root || path != b.Path) PublishBolt(b, true);
                else if (Time.unscaledTime >= b.Next)
                { b.Next = Time.unscaledTime + .2f; PublishBolt(b, false); }
            }
            foreach (var key in BoltGone) { NetworkBolts.Remove(key); BoltTombstones[key] = Time.time + 30f; }
            BoltGone.Clear(); foreach (var pair in BoltTombstones) if (Time.time >= pair.Value) BoltGone.Add(pair.Key);
            foreach (var key in BoltGone) BoltTombstones.Remove(key);
            CorpseGone.Clear(); foreach (var pair in BoltCorpses) if (pair.Value.Body == null || Time.time >= pair.Value.Until) CorpseGone.Add(pair.Key);
            foreach (var id in CorpseGone) BoltCorpses.Remove(id);
        }
        static void RememberBoltCorpse(CreateObject action, GameObject made)
        {
            if (!Multiplayer.Hosting || action.Fsm == null || action.Fsm.Name != "Health" || made == null || made.name.IndexOf("_Dead", StringComparison.OrdinalIgnoreCase) < 0) return;
            var owner = Multiplayer.CreatureObject(action.Fsm.GameObject); int id = Multiplayer.ObjectId(owner);
            if (id != 0) BoltCorpses[id] = new BoltCorpse { Body = made.transform, Until = Time.time + 30f };
        }
    }
}
