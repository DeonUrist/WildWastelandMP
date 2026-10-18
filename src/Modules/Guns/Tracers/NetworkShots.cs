using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Guns
{
    internal static partial class Tracers
    {
        sealed class ShotWindow { internal float Until; internal int Count; internal uint Last; internal readonly HashSet<uint> Seen = new HashSet<uint>(); }
        static readonly Dictionary<ulong, ShotWindow> Windows = new Dictionary<ulong, ShotWindow>();
        static uint _shotSequence;
        internal static void NetworkInit()
        { Multiplayer.Message += ReceiveShot; Multiplayer.Message += ReceiveBolt; Multiplayer.SessionChanged += ResetNetwork; MultiplayerParts.DamageLoss = PartLoss; MultiplayerParts.CanPop = PartCanPop; MultiplayerParts.Pop = PopNetworkPart; ResetNetwork(); }
        internal static void NetworkStop()
        { Multiplayer.Message -= ReceiveShot; Multiplayer.Message -= ReceiveBolt; Multiplayer.SessionChanged -= ResetNetwork; MultiplayerParts.DamageLoss = null; MultiplayerParts.CanPop = null; MultiplayerParts.Pop = null; ResetNetwork(); }
        static float PartLoss(GameObject part, float damage, byte mode)
        {
            bool wheel = IsWheel(part.transform, part.transform);
            if (mode == 1)
            { if (!wheel || part.transform.parent == null || !part.transform.parent.name.StartsWith("hinge_wheel", StringComparison.Ordinal)) return 0f; }
            else if (!GunsModule.VehicleDamage.Value) return 0f;
            float loss = Mathf.Abs(damage) / (mode == 1 ? 1f : Mathf.Max(1f, GunsModule.VehicleDamagePer1.Value));
            return wheel ? loss * Mathf.Max(0f, GunsModule.WheelDamageMultiplier.Value) : loss;
        }
        static bool PartCanPop(GameObject part) { return GunsModule.WheelPopOff.Value && IsWheel(part.transform, part.transform); }
        static void PopNetworkPart(GameObject part, Vector3 direction) { if (part != null) PopOff(part.transform, direction); }
        static void ResetNetwork() { Windows.Clear(); _shotSequence = 0; ResetNetworkBolts(); }
        static void WriteVector(BinaryWriter w, Vector3 value) { w.Write(value.x); w.Write(value.y); w.Write(value.z); }
        static Vector3 ReadVector(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        static bool Finite(Vector3 value) { return !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude); }
        static void NetworkShot(ref Shot shot, bool player)
        {
            if (!Multiplayer.WorldReady) return;
            Vector3 start = Origin.World(shot.Start), direction = shot.Dir; float speed = shot.Speed, range = shot.Range; byte kind = (byte)shot.Kind;
            uint sequence = ++_shotSequence; int actor = player ? 0 : Multiplayer.ObjectId(shot.ShooterRoot);
            ulong peer = player ? Multiplayer.LocalPeer : ulong.MaxValue;
            shot.NetworkKey = new BoltKey { Epoch = MultiplayerParts.Epoch, Peer = peer, Sequence = sequence };
            RememberBolt(ref shot);
            ulong epoch = shot.NetworkKey.Epoch;
            Multiplayer.Send(Multiplayer.Hosting ? (byte)51 : (byte)50, w => { w.Write(epoch); w.Write(peer); w.Write(sequence); w.Write(actor); WriteVector(w, start); WriteVector(w, direction); w.Write(speed); w.Write(range); w.Write(kind); }, reliable: shot.Bolt);
        }
        static void ReceiveShot(ulong from, byte packet, BinaryReader reader)
        {
            if (!Multiplayer.WorldReady || (packet != 50 && packet != 51) || (packet == 50 ? !Multiplayer.Hosting : !Multiplayer.Client || from != 0)) return;
            ulong epoch = reader.ReadUInt64(), shooter = reader.ReadUInt64(); uint sequence = reader.ReadUInt32(); int actor = reader.ReadInt32();
            Vector3 start = ReadVector(reader), direction = ReadVector(reader); float speed = reader.ReadSingle(), range = reader.ReadSingle(); byte kind = reader.ReadByte();
            if (epoch == 0 || epoch != MultiplayerParts.Epoch || reader.BaseStream.Position != reader.BaseStream.Length || !Finite(start) || !Finite(direction) || direction.sqrMagnitude < .9f || direction.sqrMagnitude > 1.1f
                || !(speed > 0 && speed <= 10000) || !(range > 0 && range <= 10000) || kind > (byte)Kind.Crossbow) return;
            Vector3 scene = Origin.Scene(start);
            if (packet == 50)
            {
                var player = Multiplayer.PlayerFor(from);
                if (from == 0 || player == null) return;
                ShotWindow window; if (!Windows.TryGetValue(from, out window)) { window = new ShotWindow(); Windows[from] = window; }
                // Unreliable bullets can overtake a reliable bolt launch. Keep a bounded replay window instead of rejecting every older sequence.
                if (sequence == 0 || sequence <= window.Last && window.Last - sequence > 4096 || window.Seen.Contains(sequence)) return;
                if (Time.unscaledTime >= window.Until) { window.Until = Time.unscaledTime + 1f; window.Count = 0; }
                if (++window.Count > 256) return;
                window.Last = Math.Max(window.Last, sequence); window.Seen.Add(sequence);
                if (window.Seen.Count > 8192) { uint floor = window.Last > 4096 ? window.Last - 4096 : 0; window.Seen.RemoveWhere(value => value < floor); }
                shooter = from; actor = 0;
                Bridge.Shot(player, scene, kind, true);
                Multiplayer.Send(51, w => { w.Write(epoch); w.Write(shooter); w.Write(sequence); w.Write(actor); WriteVector(w, start); WriteVector(w, direction); w.Write(speed); w.Write(range); w.Write(kind); }, reliable: kind == (byte)Kind.Crossbow);
            }
            if (shooter == Multiplayer.LocalPeer) return;
            var key = new BoltKey { Epoch = epoch, Peer = shooter, Sequence = sequence };
            if (BoltAlreadySettled(key)) return;
            var shot = new Shot { Start = scene, Pos = scene, Dir = direction.normalized, Speed = speed, Range = range, Kind = (Kind)kind,
                Bolt = kind == (byte)Kind.Crossbow, Shotgun = kind == (byte)Kind.Shotgun, Alive = true, RemoteVisual = true, NetworkKey = key, ShooterRoot = actor != 0 ? Multiplayer.ObjectById(actor) : Multiplayer.Hosting ? Multiplayer.PlayerFor(shooter) : null };
            if (_shots.Count >= GunsModule.MaxTracers.Value) { RememberBolt(ref shot); return; }
            if (shot.Bolt && _boltMesh == null) { var arm = PlayerRig.WeaponsArm; if (arm != null) CacheBoltModel(arm); }
            LaunchBolt(ref shot); RememberBolt(ref shot); _shots.Add(shot);
        }
        static void StepNetworkVisual(ref Shot shot, float distance)
        {
            // Observers never choose another collision. The shooter reports the end; the host maintains landed bolts.
            shot.Pos += shot.Dir * distance; shot.Travelled += distance;
            if (shot.Travelled >= shot.Range) shot.Alive = false;
        }
    }
}
