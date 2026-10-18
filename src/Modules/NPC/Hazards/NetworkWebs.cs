using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.NPC
{
    internal static partial class Webs
    {
        const int MeshChunk = 900, MeshLimit = 2 * 1024 * 1024;
        sealed class Transfer { internal uint Id; internal byte[] Bytes; internal int Part; }
        static uint _networkId;
        static MultiplayerSnapshot _network;
        static readonly Dictionary<uint, Vector3> Descriptions = new Dictionary<uint, Vector3>();
        static readonly Dictionary<uint, Web> Copies = new Dictionary<uint, Web>();
        static readonly Dictionary<ulong, Transfer> Transfers = new Dictionary<ulong, Transfer>();
        static readonly List<uint> Removed = new List<uint>();
        static readonly List<ulong> Sent = new List<ulong>();
        static Transfer _receiving;
        static bool[] _received;
        static int _receivedCount;
        static float _requestAt;
        static ulong _networkEpoch;
        internal static void StartNetwork(Core.IModule owner)
        {
            StopNetwork(); if (!Multiplayer.Present) return;
            _network = new MultiplayerSnapshot(owner, "Spider web catalogue", 150, 151, Catalogue, ApplyCatalogue);
            Multiplayer.Message += MeshMessage; Multiplayer.SessionChanged += ResetNetwork;
            Scheduler.Interval(owner, "Spider web transfer", .1f, NetworkTick);
            Log.Always("[Multiplayer] Spider web mirror ready (host meshes; guest lighting; host alarms)");
        }
        internal static void StopNetwork()
        {
            Multiplayer.Message -= MeshMessage; Multiplayer.SessionChanged -= ResetNetwork;
            if (_network != null) _network.Dispose(); _network = null; ResetNetwork();
        }
        static void ResetNetwork()
        {
            foreach (var web in Copies.Values) Kill(web);
            Copies.Clear(); Descriptions.Clear(); Transfers.Clear(); _receiving = null; _received = null;
            _receivedCount = 0; _requestAt = 0; _networkEpoch = MultiplayerParts.Epoch;
        }
        static void Point(BinaryWriter w, Vector3 p) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
        static Vector3 Point(BinaryReader r)
        {
            var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (float.IsNaN(p.sqrMagnitude) || float.IsInfinity(p.sqrMagnitude)) throw new InvalidDataException("Invalid web point");
            return p;
        }
        static byte[] EncodeMesh()
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new GZipStream(stream, CompressionMode.Compress, true)) using (var w = new BinaryWriter(zip))
                { w.Write(_v.Count); for (int i = 0; i < _v.Count; i++) { Point(w, _v[i]); var c = _c[i]; w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); } }
                return stream.ToArray();
            }
        }
        static Web DecodeMesh(byte[] bytes, Vector3 world)
        {
            using (var stream = new MemoryStream(bytes, false)) using (var zip = new GZipStream(stream, CompressionMode.Decompress)) using (var r = new BinaryReader(zip))
            {
                int count = r.ReadInt32(); if (count < 4 || count > 64004 || count % 4 != 0) throw new InvalidDataException("Invalid web mesh size");
                Begin(Origin.Scene(world));
                for (int i = 0; i < count; i++)
                {
                    _v.Add(Point(r)); _c.Add(new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte()));
                    _uv.Add(i % 4 == 0 ? Vector2.zero : i % 4 == 1 ? Vector2.right : i % 4 == 2 ? Vector2.one : Vector2.up);
                    if (i % 4 == 0) { _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3); }
                }
                if (zip.ReadByte() != -1) throw new InvalidDataException("Trailing web mesh data");
                return End("Web_Network");
            }
        }
        static byte[] Catalogue()
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(_all.Count);
                foreach (var web in _all) { w.Write(web.NetworkId); Point(w, Origin.World(web.Go.transform.position)); }
                return stream.ToArray();
            }
        }
        static void ApplyCatalogue(byte[] data)
        {
            var incoming = new Dictionary<uint, Vector3>();
            using (var r = new BinaryReader(new MemoryStream(data, false)))
            {
                int count = r.ReadInt32(); if (count < 0 || count > 4096) throw new InvalidDataException("Invalid web count");
                for (int i = 0; i < count; i++) incoming.Add(r.ReadUInt32(), Point(r));
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Trailing web catalogue");
            }
            Descriptions.Clear(); foreach (var pair in incoming) Descriptions.Add(pair.Key, pair.Value);
            Removed.Clear(); foreach (var pair in Copies) if (!Descriptions.ContainsKey(pair.Key)) { Kill(pair.Value); Removed.Add(pair.Key); }
            foreach (uint id in Removed) Copies.Remove(id);
            if (_receiving != null && !Descriptions.ContainsKey(_receiving.Id)) { _receiving = null; _requestAt = 0; }
        }
        static void NetworkTick()
        {
            if (_networkEpoch != MultiplayerParts.Epoch) ResetNetwork();
            if (!Multiplayer.WorldReady || _networkEpoch == 0) return;
            if (Multiplayer.Client)
            {
                foreach (var pair in Copies) { Vector3 p; if (Descriptions.TryGetValue(pair.Key, out p) && pair.Value.Go != null) pair.Value.Center = pair.Value.Go.transform.position = Origin.Scene(p); }
                if (Time.unscaledTime < _requestAt) return;
                if (_receiving == null)
                    foreach (var pair in Descriptions) if (!Copies.ContainsKey(pair.Key)) { _receiving = new Transfer { Id = pair.Key }; break; }
                if (_receiving == null) return;
                _requestAt = Time.unscaledTime + 3f; uint id = _receiving.Id;
                Multiplayer.Send(152, w => { w.Write(_networkEpoch); w.Write(id); }); return;
            }
            Sent.Clear();
            foreach (var pair in Transfers)
            {
                var transfer = pair.Value;
                for (int n = 0; n < 4 && transfer.Part * MeshChunk < transfer.Bytes.Length; n++)
                {
                    int part = transfer.Part++, offset = part * MeshChunk, count = Math.Min(MeshChunk, transfer.Bytes.Length - offset);
                    Multiplayer.Send(153, w => { w.Write(_networkEpoch); w.Write(transfer.Id); w.Write(transfer.Bytes.Length); w.Write(part); w.Write((ushort)count); w.Write(transfer.Bytes, offset, count); }, pair.Key);
                }
                if (transfer.Part * MeshChunk >= transfer.Bytes.Length) Sent.Add(pair.Key);
            }
            foreach (ulong peer in Sent) Transfers.Remove(peer);
        }
        static void MeshMessage(ulong peer, byte kind, BinaryReader r)
        {
            if ((kind != 152 && kind != 153) || !Multiplayer.WorldReady) return;
            if (r.ReadUInt64() != MultiplayerParts.Epoch || MultiplayerParts.Epoch == 0) return;
            uint id = r.ReadUInt32();
            if (kind == 152)
            {
                if (!Multiplayer.Hosting || peer == 0 || Multiplayer.PlayerFor(peer) == null || r.BaseStream.Position != r.BaseStream.Length || Transfers.ContainsKey(peer)) return;
                foreach (var web in _all) if (web.NetworkId == id && web.NetworkData != null && web.NetworkData.Length <= MeshLimit)
                { Transfers[peer] = new Transfer { Id = id, Bytes = web.NetworkData }; break; }
                return;
            }
            if (!Multiplayer.Client || peer != 0 || _receiving == null || _receiving.Id != id) return;
            int length = r.ReadInt32(), part = r.ReadInt32(), count = r.ReadUInt16();
            if (length < 1 || length > MeshLimit || part < 0 || part >= (length + MeshChunk - 1) / MeshChunk || count != Math.Min(MeshChunk, length - part * MeshChunk) || r.BaseStream.Length - r.BaseStream.Position != count) return;
            if (_receiving.Bytes == null) { _receiving.Bytes = new byte[length]; _received = new bool[(length + MeshChunk - 1) / MeshChunk]; _receivedCount = 0; }
            if (_receiving.Bytes.Length != length || _received[part]) return;
            if (r.Read(_receiving.Bytes, part * MeshChunk, count) != count) return;
            _received[part] = true; _requestAt = Time.unscaledTime + 3f;
            if (++_receivedCount != _received.Length) return;
            Vector3 world; if (Descriptions.TryGetValue(id, out world)) Copies[id] = DecodeMesh(_receiving.Bytes, world);
            _receiving = null; _requestAt = 0;
        }
    }
}
