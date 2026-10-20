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
        const uint Revision = 26;
        static Func<object, IDictionary> _connections;
        static Func<object, byte> _id;
        static Func<object, bool> _welcomed;
        static Func<object, object> _transport;
        static Action<object, ulong, byte[], bool> _send;
        static Action<object, string> _reject;
        static Action<string> _end;
        static Action<ulong> _sendAll;
        static readonly Dictionary<ulong, byte[]> PendingWorld = new Dictionary<ulong, byte[]>();
        static readonly Dictionary<ulong, ulong> ReadyPeers = new Dictionary<ulong, ulong>();
        static readonly List<ulong> WorldSent = new List<ulong>();
        static byte[] _hello;
        static ulong _connection;
        static float _next;
        static bool _accepted;
        internal static void Install()
        {
            var session = Multiplayer.Type("Net.NetSession"); var peer = Multiplayer.Type("Net.NetSession+Peer");
            _connections = MultiplayerAccess.Get<IDictionary>(session, "byConnection");
            _id = MultiplayerAccess.Get<byte>(peer, "Id"); _welcomed = MultiplayerAccess.Get<bool>(peer, "Welcomed");
            _transport = MultiplayerAccess.Get<object>(session, "transport");
            _send = MultiplayerAccess.Call<Action<object, ulong, byte[], bool>>(Multiplayer.Type("Net.ITransport"), "Send", typeof(ulong), typeof(byte[]), typeof(bool));
            _reject = MultiplayerAccess.Call<Action<object, string>>(session, "Reject", peer, typeof(string));
            _end = MultiplayerAccess.Call<Action<string>>(session, "End", typeof(string));
            _sendAll = MultiplayerAccess.Call<Action<ulong>>(Multiplayer.Type("Net.WorldObjects"), "SendAllTo", typeof(ulong));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "OnHello"), prefix: AccessTools.Method(typeof(MultiplayerHandshake), "Hello"))) throw new InvalidOperationException("WW MP handshake hook failed");
            foreach (string name in new[] { "Net.UdpTransport", "Net.SteamSocketsTransport" })
                if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type(name), "Send"), prefix: AccessTools.Method(typeof(MultiplayerHandshake), "Transport"))) throw new InvalidOperationException("WW MP bootstrap transport hook failed");
            Multiplayer.SessionChanged += Changed;
            Scheduler.Interval(Multiplayer.PatchOwner, "MP handshake retry", .5f, Tick);
        }
        static void Changed() { if (!Multiplayer.SessionActive) { _accepted = false; _hello = null; PendingWorld.Clear(); ReadyPeers.Clear(); MultiplayerModManifest.Reset(); } }
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
                    string mismatch = MultiplayerModManifest.Check(reader, true);
                    if (mismatch != null) throw new InvalidDataException(mismatch);
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
        static byte[] Append(byte[] packet, bool settings)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(packet); writer.Write(Magic); writer.Write(Revision);
                MultiplayerModManifest.Write(writer);
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
                if (!ReferenceEquals(__1, _hello)) { __1 = Append(__1, false); _hello = __1; _connection = __0; _next = Time.unscaledTime + 1f; }
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
            _next = Time.unscaledTime + 1f; var transport = _transport(null); if (transport != null) _send(transport, _connection, _hello, true);
        }
        internal static void ClientReady(ulong peer, ulong hello)
        {
            ulong previous;
            if (ReadyPeers.TryGetValue(peer, out previous) && previous == hello) return;
            ReadyPeers[peer] = hello;
            // Scene loading can clear native pre-load spawns. Re-send after this client's readiness request.
            _sendAll(peer);
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
                    string mismatch = MultiplayerModManifest.Check(reader, false);
                    if (mismatch != null) throw new InvalidDataException(mismatch);
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
