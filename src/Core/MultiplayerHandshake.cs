using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerHandshake
    {
        const uint Magic = 0x4857574D;
        const uint Revision = 27;
        static Func<object, IDictionary> _connections;
        static Func<object, byte> _id;
        static Func<object, bool> _welcomed;
        static Func<object, object> _transport;
        static Action<object, ulong, byte[], bool> _send;
        static Action<object, string> _reject;
        static Action<string> _end;
        static Action<ulong, byte[]> _received;
        static Func<object, ulong> _peerConnection;
        static readonly Dictionary<ulong, byte[]> PendingWorld = new Dictionary<ulong, byte[]>();
        static readonly Dictionary<ulong, ulong> ReadyPeers = new Dictionary<ulong, ulong>();
        static readonly List<ulong> WorldSent = new List<ulong>();
        static byte[] _hello;
        static byte[] _baseHello;
        static float _overflowAt;
        sealed class BootstrapTransfer { internal ulong Nonce; internal byte[] Data; internal bool[] Parts; internal int Count; }
        static readonly Dictionary<ulong, BootstrapTransfer> BootstrapParts = new Dictionary<ulong, BootstrapTransfer>();
        static ulong _helloNonce;
        static ulong _connection;
        static float _next;
        static bool _accepted;
        internal static void Install()
        {
            var session = Multiplayer.Type("Net.NetSession"); var peer = Multiplayer.Type("Net.NetSession+Peer");
            _connections = MultiplayerAccess.Get<IDictionary>(session, "byConnection");
            _id = MultiplayerAccess.Get<byte>(peer, "Id"); _welcomed = MultiplayerAccess.Get<bool>(peer, "Welcomed");
            _peerConnection = MultiplayerAccess.Get<ulong>(peer, "Connection");
            _transport = MultiplayerAccess.Get<object>(session, "transport");
            _send = MultiplayerAccess.Call<Action<object, ulong, byte[], bool>>(Multiplayer.Type("Net.ITransport"), "Send", typeof(ulong), typeof(byte[]), typeof(bool));
            _reject = MultiplayerAccess.Call<Action<object, string>>(session, "Reject", peer, typeof(string));
            _end = MultiplayerAccess.Call<Action<string>>(session, "End", typeof(string));
            _received = MultiplayerAccess.Call<Action<ulong, byte[]>>(session, "OnDataReceived", typeof(ulong), typeof(byte[]));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "OnHello"), prefix: AccessTools.Method(typeof(MultiplayerHandshake), "Hello"))) throw new InvalidOperationException("WW MP handshake hook failed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "OnDataReceived"), prefix: AccessTools.Method(typeof(MultiplayerHandshake), "Bootstrap"))) throw new InvalidOperationException("WW MP bootstrap receive hook failed");
            foreach (string name in new[] { "Net.UdpTransport", "Net.SteamSocketsTransport" })
                if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type(name), "Send"), prefix: AccessTools.Method(typeof(MultiplayerHandshake), "Transport"))) throw new InvalidOperationException("WW MP bootstrap transport hook failed");
            Multiplayer.SessionChanged += Changed;
            MultiplayerPlayers.Left += Left;
            MultiplayerJoin.Install();
            Scheduler.Interval(Multiplayer.PatchOwner, "MP handshake retry", .5f, Tick);
        }
        static void Changed() { if (!Multiplayer.SessionActive) { _accepted = false; _hello = _baseHello = null; PendingWorld.Clear(); ReadyPeers.Clear(); JoinSince.Clear(); JoinWarned.Clear(); BootstrapParts.Clear(); MultiplayerModManifest.Reset(); } }
        static void Left(ulong peer) { ReadyPeers.Remove(peer); JoinSince.Remove(peer); JoinWarned.Remove(peer); }
        internal static bool Joining(ulong peer) { return !ReadyPeers.ContainsKey(peer) || MultiplayerJoin.Joining(peer); }
        static readonly Dictionary<ulong, float> JoinSince = new Dictionary<ulong, float>();
        static readonly HashSet<ulong> JoinWarned = new HashSet<ulong>();
        const float JoinGrace = 180f;
        static bool StillJoining(ulong peer) { return Joining(peer) || !MultiplayerSleepers.ReadyPeer(peer) || !MultiplayerPlayers.HasSnapshot(peer); }
        // Kept from the handshake tick (0.5 s) so the grace counts from the connection, not from the first blocked autosave.
        static void TrackJoining()
        {
            if (!Multiplayer.Hosting) { if (JoinSince.Count != 0) { JoinSince.Clear(); JoinWarned.Clear(); } return; }
            float now = Time.unscaledTime;
            foreach (ulong peer in MultiplayerReliable.ConnectedPeers())
            {
                if (!StillJoining(peer)) { JoinSince.Remove(peer); JoinWarned.Remove(peer); continue; }
                float since; if (!JoinSince.TryGetValue(peer, out since)) JoinSince[peer] = since = now;
                if (now - since >= JoinGrace && JoinWarned.Add(peer)) Log.Warn("[Multiplayer] Player " + peer + " has been joining for 3 minutes; autosaves no longer wait for it");
            }
        }
        // A peer that never finishes joining (stuck loading, alive pings) must not stop the autosave clock for the whole session.
        internal static bool HasJoiningPeers
        {
            get
            {
                if (!Multiplayer.Hosting) return false;
                float now = Time.unscaledTime;
                foreach (ulong peer in MultiplayerReliable.ConnectedPeers())
                {
                    if (!StillJoining(peer)) continue;
                    float since; if (!JoinSince.TryGetValue(peer, out since)) JoinSince[peer] = since = now;
                    if (now - since < JoinGrace) return true;
                }
                return false;
            }
        }
        internal static void Overflow(ulong peer)
        {
            if (Time.unscaledTime < _overflowAt) return; _overflowAt = Time.unscaledTime + 1f;
            const string reason = "Reliable multiplayer queue exhausted. Reconnect to receive a complete world.";
            if (Multiplayer.Client) { _end(reason); return; }
            foreach (DictionaryEntry entry in _connections(null))
                if (_id(entry.Value) == peer) { try { _reject(entry.Value, reason); } catch (System.Net.Sockets.SocketException) { } return; }
        }
        static void SendBootstrap(ulong connection, byte[] data)
        {
            var transport = _transport(null); if (transport == null) return;
            for (int offset = 0; offset < data.Length; offset += 900)
                using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
                { writer.Write((byte)246); writer.Write(_helloNonce); writer.Write(data.Length); writer.Write(offset); writer.Write(data, offset, Math.Min(900, data.Length - offset)); _send(transport, connection, stream.ToArray(), true); }
        }
        static bool Bootstrap(ulong __0, byte[] __1)
        {
            if (__1 == null || __1.Length == 0 || (__1[0] != 246 && __1[0] != 247)) return true;
            if (__1[0] == 247)
            {
                if (Multiplayer.Client && !_accepted && __0 == _connection && __1.Length == 1 && _baseHello != null)
                { _hello = Append(_baseHello, false, true); _helloNonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); SendBootstrap(__0, _hello); }
                return false;
            }
            if (!Multiplayer.Hosting || __1.Length < 18 || __1.Length > 917) return false;
            var connections = _connections(null);
            if (!connections.Contains(__0) || _welcomed(connections[__0])) return false;
            ulong nonce = BitConverter.ToUInt64(__1, 1); int length = BitConverter.ToInt32(__1, 9), offset = BitConverter.ToInt32(__1, 13), count = __1.Length - 17;
            if (length < 1 || length > 60000 || offset < 0 || offset >= length || offset % 900 != 0 || count != Math.Min(900, length - offset)) return false;
            BootstrapTransfer transfer;
            if (!BootstrapParts.TryGetValue(__0, out transfer) || transfer.Nonce != nonce)
            { if (BootstrapParts.Count >= 64 && !BootstrapParts.ContainsKey(__0)) return false; transfer = new BootstrapTransfer { Nonce = nonce, Data = new byte[length], Parts = new bool[(length + 899) / 900] }; BootstrapParts[__0] = transfer; }
            if (transfer.Data.Length != length || transfer.Parts[offset / 900]) return false;
            Array.Copy(__1, 17, transfer.Data, offset, count); transfer.Parts[offset / 900] = true;
            if (++transfer.Count == transfer.Parts.Length) { BootstrapParts.Remove(__0); if (transfer.Data[0] == 1) _received(__0, transfer.Data); }
            return false;
        }
        static bool Hello(object __0, byte[] __1)
        {
            if (MultiplayerSaving.Frozen) return false;
            if (_welcomed(__0)) return true;
            try
            {
                if (__1 == null || __1.Length > 60000) throw new InvalidDataException("Invalid multiplayer handshake size.");
                using (var reader = new BinaryReader(new MemoryStream(__1, false)))
                {
                    reader.ReadByte(); if (reader.ReadInt32() != 21) return true;
                    reader.ReadString(); reader.ReadString();
                    Header(reader, true);
                    bool equal = ReadHash(reader);
                    bool full = reader.ReadBoolean();
                    if (!equal && !full) { _send(_transport(null), _peerConnection(__0), new byte[] { 247 }, true); return false; }
                    if (full) { string mismatch = MultiplayerModManifest.Check(reader, true); if (mismatch != null) throw new InvalidDataException(mismatch); }
                    else if (!equal) throw new InvalidDataException("Multiplayer mod builds differ.");
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Invalid multiplayer handshake trailer.");
                }
                return true;
            }
            catch (InvalidDataException e) { _reject(__0, e.Message); return false; }
            catch { _reject(__0, "Missing or incomplete WildWastelandMP handshake. Install the host's exact mod set."); return false; }
        }
        static void Header(BinaryReader reader, bool hosting)
        {
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("WildWastelandMP is required. Install the host's exact mod set.");
            uint other = reader.ReadUInt32();
            if (other != Revision) throw new InvalidDataException("WildWastelandMP patch mismatch. Host revision: " + (hosting ? Revision : other) + "; client revision: " + (hosting ? other : Revision) + ". Install the host's build.");
        }
        static bool ReadHash(BinaryReader reader)
        { var local = MultiplayerModManifest.Hash(); var remote = reader.ReadBytes(32); if (remote.Length != 32) throw new EndOfStreamException(); for (int i = 0; i < local.Length; i++) if (local[i] != remote[i]) return false; return true; }
        static byte[] Append(byte[] packet, bool settings, bool full = false)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(packet); writer.Write(Magic); writer.Write(Revision);
                writer.Write(MultiplayerModManifest.Hash());
                if (!settings) { writer.Write(full); if (full) MultiplayerModManifest.Write(writer); }
                if (settings) { var data = MultiplayerSettings.CaptureWorld(); writer.Write(data.Length); writer.Write(data); }
                var result = stream.ToArray(); if (result.Length > 60000) throw new InvalidDataException("WW handshake settings exceed transport capacity"); return result;
            }
        }
        static bool Transport(ulong __0, ref byte[] __1)
        {
            if (!Multiplayer.Active || __1 == null || __1.Length == 0 || __1[0] == MultiplayerReliable.Packet) return true;
            if (Multiplayer.Client && __1[0] == 1)
            {
                // The retry uses the already extended packet through the underlying transport.
                if (!ReferenceEquals(__1, _hello)) { _baseHello = __1; __1 = Append(__1, false); _hello = __1; _connection = __0; _next = Time.unscaledTime + 1f; }
                if (__1.Length > 1000) { SendBootstrap(__0, __1); return false; }
                return true;
            }
            if (!Multiplayer.Hosting || (__1[0] != 24 && __1[0] != 5 && __1[0] != 26 && __1[0] != 27 && __1[0] != 35 && __1[0] != 37)) return true;
            var connections = _connections(null); if (!connections.Contains(__0) || !_welcomed(connections[__0])) return true;
            // Welcome immediately keeps transport heartbeats alive while the host generates its world.
            if (__1[0] == 5 && !Multiplayer.WorldReady) { PendingWorld[__0] = __1; return false; }
            if (__1[0] == 24) __1 = Append(__1, true);
            MultiplayerReliable.Send(__1, _id(connections[__0])); return false;
        }
        static void Tick()
        {
            TrackJoining();
            if (Multiplayer.Hosting && BootstrapParts.Count != 0)
            {
                var peers = _connections(null); WorldSent.Clear();
                foreach (var pair in BootstrapParts) if (!peers.Contains(pair.Key) || _welcomed(peers[pair.Key])) WorldSent.Add(pair.Key);
                foreach (ulong connection in WorldSent) BootstrapParts.Remove(connection);
            }
            if (Multiplayer.Hosting && Multiplayer.WorldReady && PendingWorld.Count != 0)
            {
                var connections = _connections(null); var worldTransport = _transport(null); WorldSent.Clear();
                foreach (var pair in PendingWorld)
                {
                    WorldSent.Add(pair.Key);
                    if (worldTransport != null && connections.Contains(pair.Key) && _welcomed(connections[pair.Key])) _send(worldTransport, pair.Key, pair.Value, true);
                }
                foreach (ulong connection in WorldSent) PendingWorld.Remove(connection);
            }
            if (!Multiplayer.Client || _accepted || _hello == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f; var transport = _transport(null); if (transport != null) { if (_hello.Length > 1000) SendBootstrap(_connection, _hello); else _send(transport, _connection, _hello, true); }
        }
        internal static void ClientReady(ulong peer, ulong hello)
        {
            ulong previous;
            if (ReadyPeers.TryGetValue(peer, out previous) && previous == hello) return;
            ReadyPeers[peer] = hello;
            MultiplayerJoin.Ready(peer);
            Multiplayer.SendAppearances(peer);
            MultiplayerParty.ForgetSent(peer);   // first party/transaction packets go out on the next tick, not after the 5 s keepalive
        }
        internal static bool BeforeNative(ulong from, byte[] packet)
        {
            if (!Multiplayer.Client || from != 0) return true;
            if (packet[0] == 5) return _accepted;
            if (packet[0] != 24) return true;
            try
            {
                if (packet.Length > 60000) throw new InvalidDataException("Invalid multiplayer handshake size.");
                using (var reader = new BinaryReader(new MemoryStream(packet, false)))
                {
                    reader.ReadByte(); reader.ReadByte();
                    Header(reader, false);
                    if (!ReadHash(reader)) throw new InvalidDataException("The host's loaded mod builds changed during admission. Reconnect with the host's exact mod set.");
                    int length = reader.ReadInt32(); if (length < 0 || length != packet.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid WW settings length");
                    MultiplayerSettings.ApplyWorld(reader.ReadBytes(length));
                }
                _accepted = true; _hello = null;
                Log.Always("[Multiplayer] Exact mod versions/builds accepted; host world settings applied before world launch"); return true;
            }
            catch (Exception e) { Log.Error("[Multiplayer] Join refused: " + e.Message, e); _end("Join refused: " + e.Message); return false; }
        }
    }
}
