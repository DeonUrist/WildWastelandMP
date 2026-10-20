using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace WildWasteland.Core
{
    // Reliable, bounded whole-state transfers. Apply only a complete generation; never expose half a map/book.
    internal sealed class MultiplayerSnapshot : IDisposable
    {
        const int Chunk = 900, Limit = 4 * 1024 * 1024;
        readonly byte _request, _data;
        readonly Func<byte[]> _capture;
        readonly Action<byte[]> _apply;
        readonly Job _job;
        byte[] _sending, _receiving;
        byte[] _latest, _hash;
        readonly HashSet<ulong> _requests = new HashSet<ulong>();
        ulong? _target;
        bool[] _parts;
        ulong _epoch, _receivedEpoch;
        uint _version, _receivedVersion, _appliedVersion;
        int _part, _count;
        float _next, _retry;
        public MultiplayerSnapshot(IModule owner, string name, byte request, byte data, Func<byte[]> capture, Action<byte[]> apply)
        {
            _request = request; _data = data; _capture = capture; _apply = apply;
            Reset(); Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
            MultiplayerPlayers.Left += Left;
            _job = Scheduler.Interval(owner, name, .1f, Tick);
        }
        void Left(ulong peer)
        { _requests.Remove(peer); if (_target == peer) { _sending = null; _target = null; } }
        void Reset()
        {
            _sending = _receiving = null; _parts = null; _part = _count = 0; _next = _retry = 0;
            _latest = _hash = null; _requests.Clear(); _target = null;
            _epoch = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); _receivedEpoch = 0; _version = _receivedVersion = _appliedVersion = 0;
        }
        void Tick()
        {
            if (!Multiplayer.WorldReady) return;
            float now = Time.unscaledTime;
            if (Multiplayer.Client)
            {
                if (now >= _retry) { _retry = now + 3f; Multiplayer.Send(_request, w => { w.Write(_receivedEpoch); w.Write(_appliedVersion); }); }
                return;
            }
            if (_sending == null && now >= _next)
            {
                _next = now + 1f; var captured = _capture();
                if (captured != null && captured.Length != 0)
                {
                    if (captured.Length > Limit) throw new InvalidDataException("WW snapshot exceeds 4 MiB");
                    byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(captured);
                    bool changed = _hash == null;
                    if (!changed) for (int i = 0; i < hash.Length; i++) if (hash[i] != _hash[i]) { changed = true; break; }
                    if (changed) { _hash = hash; _latest = captured; _version++; _sending = _latest; _part = 0; _target = null; _requests.Clear(); }
                }
            }
            if (_sending == null && _latest != null && _requests.Count != 0)
            { foreach (ulong peer in _requests) { _target = peer; break; } _requests.Remove(_target.Value); _sending = _latest; _part = 0; }
            for (int sent = 0; sent < 4 && _sending != null; sent++)
            {
                if (_target.HasValue ? !MultiplayerReliable.CanQueue(_target.Value) : !MultiplayerReliable.CanBroadcast()) break;
                int index = _part++, offset = index * Chunk, count = Math.Min(Chunk, _sending.Length - offset);
                Multiplayer.Send(_data, w => { w.Write(_epoch); w.Write(_version); w.Write(_sending.Length); w.Write(index); w.Write((ushort)count); w.Write(_sending, offset, count); }, _target);
                if (_part * Chunk >= _sending.Length) _sending = null;
            }
        }
        void Receive(ulong peer, byte kind, BinaryReader reader)
        {
            if (kind == _request && Multiplayer.Hosting)
            {
                ulong requestedEpoch = reader.ReadUInt64(); uint requestedVersion = reader.ReadUInt32();
                if (reader.BaseStream.Position == reader.BaseStream.Length && MultiplayerReliable.ConnectedPeers().Contains(peer) && (requestedEpoch != _epoch || requestedVersion != _version)) _requests.Add(peer);
                return;
            }
            if (kind != _data || !Multiplayer.Client || peer != 0 || !Multiplayer.WorldReady) return;
            ulong epoch = reader.ReadUInt64(); uint version = reader.ReadUInt32();
            int length = reader.ReadInt32(), index = reader.ReadInt32(), count = reader.ReadUInt16();
            if (length < 1 || length > Limit || index < 0 || index >= (length + Chunk - 1) / Chunk || count != Math.Min(Chunk, length - index * Chunk)
                || reader.BaseStream.Length - reader.BaseStream.Position != count || (epoch == _receivedEpoch && (version < _receivedVersion || version == _appliedVersion))) return;
            if (_receiving == null || epoch != _receivedEpoch || version != _receivedVersion)
            {
                _receivedEpoch = epoch; _receivedVersion = version; _receiving = new byte[length]; _parts = new bool[(length + Chunk - 1) / Chunk]; _count = 0;
            }
            if (_receiving.Length != length || _parts[index]) return;
            if (reader.Read(_receiving, index * Chunk, count) != count) return;
            _parts[index] = true;
            if (++_count != _parts.Length) return;
            var completed = _receiving; _receiving = null; _parts = null; _count = 0;
            _apply(completed); _appliedVersion = version;
        }
        public void Dispose()
        { _job.Enabled = false; Multiplayer.Message -= Receive; Multiplayer.SessionChanged -= Reset; MultiplayerPlayers.Left -= Left; Reset(); }
    }
}
