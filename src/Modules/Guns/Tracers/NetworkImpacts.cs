using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Guns
{
    internal static partial class Tracers
    {
        sealed class ImpactWindow { internal uint Last, Floor; internal readonly HashSet<uint> Seen = new HashSet<uint>(); }
        static readonly Dictionary<ulong, ImpactWindow> ImpactReceipts = new Dictionary<ulong, ImpactWindow>();
        static bool RepeatedImpact(BoltKey key)
        { ImpactWindow window; return ImpactReceipts.TryGetValue(key.Peer, out window) && (key.Sequence < window.Floor || window.Seen.Contains(key.Sequence)); }
        static void RememberImpact(BoltKey key)
        {
            ImpactWindow window;
            if (!ImpactReceipts.TryGetValue(key.Peer, out window)) { window = new ImpactWindow(); ImpactReceipts.Add(key.Peer, window); }
            window.Last = Math.Max(window.Last, key.Sequence); window.Seen.Add(key.Sequence);
            if (window.Seen.Count <= 8192) return;
            window.Floor = window.Last > 4096 ? window.Last - 4096 : 0;
            uint floor = window.Floor; window.Seen.RemoveWhere(sequence => sequence < floor);
        }

        static void ReportShotEnd(ref Shot shot, Vector3 point)
        {
            if (!Multiplayer.WorldReady || !CurrentKey(shot.NetworkKey) || shot.RemoteVisual || shot.Bolt) return;
            var key = shot.NetworkKey; var world = Origin.World(point);
            if (Multiplayer.Hosting) PublishImpact(key, 0, "", world, Vector3.up);
            else Multiplayer.Send(130, w => { WriteKey(w, key); WriteVector(w, world); }, reliable: false);   // impact visual only
        }

        // Collision belongs to the shooter's displayed world. Never test this hit against a later host pose.
        static bool ReportPlayerImpact(ref Shot shot, RaycastHit hit, float multiplier)
        {
            if (!Multiplayer.WorldReady || !CurrentKey(shot.NetworkKey)) return false;
            var root = Multiplayer.CreatureObject(hit.collider.gameObject);
            if (root == null) return false;
            int id = Multiplayer.ObjectId(root); string path = BoltBonePath(hit.collider.transform, root.transform);
            if (id == 0 || path == null || !ValidPath(path)) return Multiplayer.Client;
            var local = hit.collider.transform.InverseTransformPoint(hit.point);
            var normal = hit.collider.transform.InverseTransformDirection(hit.normal);
            float damage = shot.Gun.Damage != null ? shot.Gun.Damage.Value * multiplier : 0f;
            var key = shot.NetworkKey; Vector3 start = Origin.World(shot.Start); float range = shot.Range;
            float distance = shot.Travelled + hit.distance; byte kind = (byte)shot.Kind;
            if (Multiplayer.Client)
            {
                Multiplayer.Send(128, w => { WriteKey(w, key); w.Write(id); w.Write(path); WriteVector(w, local); WriteVector(w, normal);
                    w.Write(damage); WriteVector(w, start); w.Write(range); w.Write(distance); w.Write(kind); });
                return true;
            }
            PublishImpact(key, id, path, local, normal, shot.Bolt);
            return false;
        }
        static void PublishImpact(BoltKey key, int id, string path, Vector3 point, Vector3 normal, bool bolt = false)
        {
            // Presentation only (the damage went through 128 or the host's own hit). Reliable per-shot bullet packets could push a peer
            // past the queue limit in a long firefight, which now ends its connection. A crossbow bolt stays reliable: its launch (51)
            // and attachment (126) are reliable too, and an impact overtaking the launch would tombstone the bolt on that guest.
            Multiplayer.Send(129, w => { WriteKey(w, key); w.Write(id); w.Write(path); WriteVector(w, point); WriteVector(w, normal); }, reliable: bolt);
        }
        static void ReceivePlayerImpact(ulong from, byte kind, BinaryReader r)
        {
            if (kind == 130)
            {
                if (!Multiplayer.Hosting || from == 0 || Multiplayer.PlayerFor(from) == null) return;
                var ended = ReadKey(r); var point = ReadVector(r);
                if (!CurrentKey(ended) || ended.Peer != from || !Finite(point) || r.BaseStream.Position != r.BaseStream.Length || BoltTombstones.ContainsKey(ended)) return;
                StopNetworkFlight(ended); BoltTombstones[ended] = Time.time + 30f; PublishImpact(ended, 0, "", point, Vector3.up); return;
            }
            if (kind == 128 ? !Multiplayer.Hosting || from == 0 : !Multiplayer.Client || from != 0) return;
            var key = ReadKey(r); int id = r.ReadInt32(); string path = r.ReadString(); Vector3 local = ReadVector(r), normal = ReadVector(r);
            if (!CurrentKey(key) || !ValidPath(path) || !Finite(local) || local.sqrMagnitude > 400f || !Finite(normal)
                || normal.sqrMagnitude < .5f || normal.sqrMagnitude > 1.5f) return;
            if (kind == 129)
            {
                if (r.BaseStream.Position != r.BaseStream.Length || key.Peer == Multiplayer.LocalPeer) return;
                // Host attachment snapshots own bolts. This merely terminates the same bullet/pellet's visual flight.
                NetworkBolt bolt; if (NetworkBolts.TryGetValue(key, out bolt)) return;
                StopNetworkFlight(key); BoltTombstones[key] = Time.time + 30f; return;
            }
            float damage = r.ReadSingle(); Vector3 start = ReadVector(r); float range = r.ReadSingle(), distance = r.ReadSingle(); byte weapon = r.ReadByte();
            if (key.Peer != from || r.BaseStream.Position != r.BaseStream.Length || RepeatedImpact(key)
                || !(damage < 0f && damage >= -10000f) || !Finite(start) || !(range > 0f && range <= 10000f)
                || !(distance >= 0f && distance <= range + 1f) || weapon > (byte)Kind.Crossbow) return;
            var player = Multiplayer.PlayerFor(from); if (player == null) return;
            // Position/range bounds use the shot data, not a fresh host sight line or the creature's current distance.
            float maximum = Mathf.Max(1f, RangeOf((Kind)weapon) * 4f + 20f);
            if (range > maximum) return;
            var root = Multiplayer.ObjectById(id);
            if (root == null || !Multiplayer.Creature(root)) return;
            // The shot must have started near the shooter and the creature must be about where the shot says it ended: the host's
            // positions lag the guest's by a round trip plus the creature's own movement, so the margins are generous, not open.
            Vector3 origin = Origin.Scene(start);
            if ((origin - player.transform.position).sqrMagnitude > 75f * 75f || (root.transform.position - origin).sqrMagnitude > (distance + 15f) * (distance + 15f))
            { if (Log.Verbose) Log.Info("[Multiplayer] Rejected shot impact from player " + from + ": start/creature inconsistent with the shot"); return; }
            var part = path.Length == 0 ? root.transform : root.transform.Find(path);
            if (part == null || !part.IsChildOf(root.transform)) return;
            var body = FsmUtil.Find(part.gameObject, "Bodypart"); var health = FsmUtil.Find(root, "Health");
            var hp = health != null ? health.FsmVariables.FindFsmFloat("Health") : null;
            var value = body != null ? body.FsmVariables.FindFsmFloat("Damage") : null;
            if (value == null || hp == null || hp.Value < 1f) return;
            RememberImpact(key);
            NetworkBolt attached;
            if (!NetworkBolts.TryGetValue(key, out attached)) { StopNetworkFlight(key); BoltTombstones[key] = Time.time + 30f; }
            // Keep secondary native hit events on the host as well as the primary damage.
            Bridge.Hurt(part.gameObject, player); value.Value = damage;
            body.Fsm.Event((FsmEventTarget)null, FsmEvent.GetFsmEvent("Damage"));
            if (part != null)
                foreach (var fsm in part.GetComponents<PlayMakerFSM>())
                {
                    fsm.Fsm.Event((FsmEventTarget)null, FsmEvent.GetFsmEvent("DamageFlammable"));
                    fsm.Fsm.Event((FsmEventTarget)null, FsmEvent.GetFsmEvent("CheckFriendly"));
                }
            PublishImpact(key, id, path, local, normal, weapon == (byte)Kind.Crossbow);
            if (Log.Verbose) Log.Info("[Multiplayer] Client impact: player " + from + " shot " + key.Sequence + " -> " + id + "/" + path
                + ", damage " + damage.ToString("0.0") + "; exact client target (no host recast)");
        }
    }
}
