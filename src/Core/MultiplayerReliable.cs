using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // The supplied UDP transport ignores reliable=true. WW and native part-state packets share one ordered stream (lane 0).
    // Combat (native CreatureDamage/PlayerDamage, WW melee hit requests/results) has its own ordered lane 1 with a shorter
    // retry, so a lost world-state packet never holds back given or received damage.
    internal static class MultiplayerReliable
    {
        internal const byte Packet = 249;
        const uint Magic = 0x4C525757;
        const int Window = 64;
        sealed class Pending { internal byte[] Data; internal float Retry; }
        sealed class Outgoing
        {
            internal ulong Nonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            internal uint Sequence, Sent;
            internal readonly SortedDictionary<uint, Pending> Pending = new SortedDictionary<uint, Pending>();
        }
        sealed class Incoming
        {
            internal ulong Nonce; internal uint Next = 1;
            internal readonly SortedDictionary<uint, byte[]> Pending = new SortedDictionary<uint, byte[]>();
            internal readonly HashSet<ulong> Retired = new HashSet<ulong>();
        }
        const int Lanes = 2;
        static readonly float[] RetrySeconds = { .35f, .2f };
        static readonly Dictionary<ulong, Outgoing>[] Out = { new Dictionary<ulong, Outgoing>(), new Dictionary<ulong, Outgoing>() };
        static readonly Dictionary<ulong, Incoming>[] In = { new Dictionary<ulong, Incoming>(), new Dictionary<ulong, Incoming>() };
        static readonly HashSet<byte> Combat = new HashSet<byte>();
        static readonly HashSet<ulong> Peers = new HashSet<ulong>();
        static readonly List<ulong> Gone = new List<ulong>();
        static readonly List<uint> Acked = new List<uint>();
        static Func<object, IDictionary> _connections;
        static Func<object, byte> _id;
        static Func<object, bool> _welcomed;
        static readonly HashSet<byte> Native = new HashSet<byte>();
        static readonly HashSet<byte> Forward = new HashSet<byte>();
        static readonly HashSet<byte> Snapshot = new HashSet<byte> { 5, 8, 18, 24, 26, 27, 35, 37 };
        static byte _relayed;
        static float _nextPeers, _nextFullLog;
        static bool _active, _hosting;
        internal static void Install()
        {
            var session = Multiplayer.Type("Net.NetSession"); var peer = Multiplayer.Type("Net.NetSession+Peer");
            _connections = MultiplayerAccess.Get<IDictionary>(session, "byConnection");
            _id = MultiplayerAccess.Get<byte>(peer, "Id"); _welcomed = MultiplayerAccess.Get<bool>(peer, "Welcomed");
            var messages = Multiplayer.Type("Net.MessageType");
            _relayed = Convert.ToByte(Enum.Parse(messages, "Relayed")); Native.Add(_relayed);
            foreach (string name in new[] { "Switch", "Thrown" }) Forward.Add(Convert.ToByte(Enum.Parse(messages, name)));
            foreach (string name in new[] { "ObjectVars", "ObjectSpawn", "ObjectDespawn", "ObjectGone", "ObjectCreate", "ObjectAdopted", "ObjectClaim", "ObjectRelease", "ObjectRevoke", "CarClaim", "CarGrant", "CarDeny", "CarRelease", "CarRequest", "CreatureDamage", "PlayerDamage", "Switch", "SwitchRequest", "Thrown", "Revive", "SleepVote", "SleepNow", "PlayerSave", "PlayerRestore" })
                Native.Add(Convert.ToByte(Enum.Parse(messages, name)));
            foreach (string name in new[] { "CreatureDamage", "PlayerDamage" }) Combat.Add(Convert.ToByte(Enum.Parse(messages, name)));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "SendToAll"), prefix: AccessTools.Method(typeof(MultiplayerReliable), "BeforeAll"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "SendTo"), prefix: AccessTools.Method(typeof(MultiplayerReliable), "BeforeTo"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "SendToAllExcept"), prefix: AccessTools.Method(typeof(MultiplayerReliable), "BeforeExcept"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "Relay"), prefix: AccessTools.Method(typeof(MultiplayerReliable), "BeforeRelay")))
                throw new InvalidOperationException("MP reliable part-state transport hooks could not be installed");
            Multiplayer.SessionChanged += Changed;
            Scheduler.Interval(Multiplayer.PatchOwner, "Reliable MP messages", .05f, Tick);
        }
        static void Reset() { for (int lane = 0; lane < Lanes; lane++) { Out[lane].Clear(); In[lane].Clear(); } Peers.Clear(); _nextPeers = 0; }
        static int Lane(byte[] data)
        {
            if (Combat.Contains(data[0])) return 1;
            return data[0] == Multiplayer.PacketType && data.Length > 6 && (data[6] == MultiplayerCombat.MeleeHitKind || data[6] == MultiplayerCombat.MeleeResultKind) ? 1 : 0;
        }
        static void Changed()
        {
            bool active = Multiplayer.SessionActive, hosting = Multiplayer.Hosting;
            // Joining -> Connected is one connection: resetting its receive sequence here loses the already acknowledged prefix.
            if (!active || !_active || hosting != _hosting) Reset();
            _active = active; _hosting = hosting;
        }
        static bool Wrap(byte[] data)
        { return Multiplayer.Active && data != null && data.Length > 0 && (Native.Contains(data[0]) || data[0] == 8 && data.Length > 5 && data[5] != 0) && (data[0] != _relayed || data.Length > 2 && Forward.Contains(data[2])); }
        static bool BeforeAll(byte[] __0) { if (!Wrap(__0)) return true; Send(__0, null); return false; }
        static bool BeforeTo(ulong __0, byte[] __1) { if (!Wrap(__1)) return true; Send(__1, __0); return false; }
        static bool BeforeExcept(ulong __0, byte[] __1)
        {
            if (!Wrap(__1)) return true;
            RefreshPeers(); foreach (ulong peer in Peers) if (peer != __0) To(peer, __1);
            return false;
        }
        static bool BeforeRelay(ulong __0, byte[] __1, bool __2)
        {
            // Native has filtered ownership. Order final car poses before the next owner-save phase too.
            if (!Multiplayer.Hosting || __1 == null || __1.Length < 8 || !(__1[0] == 8 && __2 || __1[0] == 18 && MultiplayerSaving.Frozen)) return true;
            RefreshPeers(); if (!Peers.Contains(__0)) return false;
            var relayed = new byte[__1.Length + 2]; relayed[0] = _relayed; relayed[1] = (byte)__0;
            Array.Copy(__1, 0, relayed, 2, __1.Length);
            foreach (ulong peer in Peers) if (peer != __0) To(peer, relayed);
            return false;
        }
        static void RefreshPeers()
        {
            Peers.Clear();
            if (!Multiplayer.Hosting) { Peers.Add(0); return; }
            foreach (DictionaryEntry entry in _connections(null)) if (_welcomed(entry.Value)) Peers.Add(_id(entry.Value));
        }
        internal static HashSet<ulong> ConnectedPeers() { RefreshPeers(); return new HashSet<ulong>(Peers); }
        internal static void Send(byte[] data, ulong? peer)
        {
            if (peer.HasValue) { To(peer.Value, data); return; }
            RefreshPeers(); foreach (ulong target in Peers) To(target, data);
        }
        static void To(ulong peer, byte[] data)
        {
            if (data.Length > 60000) throw new InvalidDataException("MP reliable payload exceeds 60000 bytes");
            int lane = Lane(data);
            Outgoing output; if (!Out[lane].TryGetValue(peer, out output)) { output = new Outgoing(); Out[lane].Add(peer, output); }
            if (output.Pending.Count >= 4096)
            {
                if (Time.unscaledTime >= _nextFullLog) { _nextFullLog = Time.unscaledTime + 5f; Log.Warn("[Multiplayer] Reliable queue full for player " + peer); }
                return;
            }
            uint sequence = ++output.Sequence;
            var packet = Frame(lane, output.Nonce, sequence, false, data);
            var pending = new Pending { Data = packet }; output.Pending.Add(sequence, pending);
            if (sequence <= output.Sent + Window && output.Pending.Count <= Window)
            { pending.Retry = Time.unscaledTime + RetrySeconds[lane]; output.Sent = sequence; Multiplayer.RawTo(peer, packet); }
        }
        static byte[] Frame(int lane, ulong nonce, uint sequence, bool ack, byte[] payload = null)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            { writer.Write(Packet); writer.Write(Magic); writer.Write((byte)(1 + lane)); writer.Write(ack); writer.Write(nonce); writer.Write(sequence); if (payload != null) writer.Write(payload); return stream.ToArray(); }
        }
        static void Tick()
        {
            if (!Multiplayer.Active) return;
            if (Time.unscaledTime >= _nextPeers)
            {
                _nextPeers = Time.unscaledTime + .5f; RefreshPeers();
                for (int lane = 0; lane < Lanes; lane++)
                {
                    Gone.Clear(); foreach (var pair in Out[lane]) if (!Peers.Contains(pair.Key)) Gone.Add(pair.Key);
                    foreach (ulong peer in Gone) { Out[lane].Remove(peer); In[lane].Remove(peer); }
                    Gone.Clear(); foreach (var pair in In[lane]) if (!Peers.Contains(pair.Key)) Gone.Add(pair.Key);
                    foreach (ulong peer in Gone) In[lane].Remove(peer);
                }
            }
            for (int lane = 0; lane < Lanes; lane++)
            foreach (var pair in Out[lane])
            {
                int count = 0, sent = 0;
                foreach (var packet in pair.Value.Pending)
                {
                    if (++count > Window || sent >= 16) break;
                    if (Time.unscaledTime < packet.Value.Retry) continue;
                    packet.Value.Retry = Time.unscaledTime + RetrySeconds[lane];
                    pair.Value.Sent = Math.Max(pair.Value.Sent, packet.Key);
                    Multiplayer.RawTo(pair.Key, packet.Value.Data); sent++;
                }
            }
        }
        internal static void Receive(ulong from, byte[] data)
        {
            if (!Multiplayer.Active || (Multiplayer.Client && from != 0) || data.Length < 19 || data.Length > 60019) return;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data, false)))
                {
                    reader.ReadByte(); if (reader.ReadUInt32() != Magic) return;
                    int lane = reader.ReadByte() - 1; if (lane < 0 || lane >= Lanes) return;
                    bool ack = reader.ReadBoolean(); ulong nonce = reader.ReadUInt64(); uint sequence = reader.ReadUInt32();
                    if (ack)
                    {
                        Outgoing output;
                        if (data.Length != 19 || !Out[lane].TryGetValue(from, out output) || output.Nonce != nonce || sequence > output.Sent) return;
                        Acked.Clear(); foreach (var packet in output.Pending) { if (packet.Key > sequence) break; Acked.Add(packet.Key); }
                        foreach (uint key in Acked) output.Pending.Remove(key);
                        return;
                    }
                    if (data.Length == 19 || sequence == 0) return;
                    Incoming input; if (!In[lane].TryGetValue(from, out input)) { input = new Incoming { Nonce = nonce }; In[lane].Add(from, input); }
                    if (input.Nonce != nonce)
                    {
                        if (sequence != 1 || input.Retired.Contains(nonce)) return;
                        input.Retired.Add(input.Nonce); input.Nonce = nonce; input.Next = 1; input.Pending.Clear();
                    }
                    if (sequence >= input.Next && sequence - input.Next < Window * 2 && !input.Pending.ContainsKey(sequence))
                        input.Pending.Add(sequence, reader.ReadBytes(data.Length - 19));
                    byte[] payload;
                    while (input.Pending.TryGetValue(input.Next, out payload))
                    {
                        input.Pending.Remove(input.Next++);
                        if (payload.Length == 0 || (payload[0] != Multiplayer.PacketType && !Native.Contains(payload[0]) && !Snapshot.Contains(payload[0]))) continue;
                        try { Deliver(from, payload); } catch (Exception e) { Log.Error("[Multiplayer] Reliable message failed", e); }
                    }
                    Multiplayer.RawTo(from, Frame(lane, nonce, input.Next - 1, true));
                }
            }
            catch (Exception e) { Log.Warn("[Multiplayer] Rejected reliable packet: " + e.Message); }
        }
        static void Deliver(ulong from, byte[] payload)
        {
            if (payload[0] == _relayed)
            {
                if (!Multiplayer.Client || from != 0 || payload.Length < 3 || !(Forward.Contains(payload[2]) || payload[2] == 8 || payload[2] == 18)) return;
                var inner = new byte[payload.Length - 2]; Array.Copy(payload, 2, inner, 0, inner.Length);
                Multiplayer.Deliver(payload[1], inner); return;
            }
            Multiplayer.Deliver(from, payload);
            // Native OnDataReceived normally relays these after Handle. The ordered envelope bypasses that outer switch.
            if (Multiplayer.Hosting && from != 0 && Forward.Contains(payload[0]))
            {
                var relayed = new byte[payload.Length + 2]; relayed[0] = _relayed; relayed[1] = (byte)from;
                Array.Copy(payload, 0, relayed, 2, payload.Length); RefreshPeers();
                foreach (ulong peer in Peers) if (peer != from) To(peer, relayed);
            }
        }
    }
}
