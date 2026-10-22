using System;
using System.Collections.Generic;
using System.IO;
using WildWasteland.Core;

namespace WildWasteland.Modules.CleanHUD
{
    internal static partial class MapFog
    {
        const byte FogRequest = 156, FogPersonal = 157, FogShared = 158;
        const int FogPartSize = 512;
        struct PendingFog { internal long Key; internal Chunk Data; }
        static readonly Dictionary<long, Chunk> SharedChunks = new Dictionary<long, Chunk>();
        static readonly Queue<PendingFog> PersonalPending = new Queue<PendingFog>();
        static readonly Queue<PendingFog> SharedPending = new Queue<PendingFog>();
        static readonly byte[] FogIncoming = new byte[FogPartSize];
        static readonly Action<BinaryWriter> FogHeaderWriter = WriteFogHeader, FogPartWriter = WriteFogPart;
        static ulong _fogEpoch;
        static bool _networkSharing, _requestFog;
        static PendingFog _sendingFog;
        static int _sendingPart;
        static bool Sharing { get { return _loaded && !_standDown && _seed != int.MinValue && CleanHudModule.SharedVision.Value && Multiplayer.WorldReady
            && (!Multiplayer.Client || MultiplayerWorld.GuestSlot != null); } }

        static byte FogAt(long key, int cell)
        {
            Chunk own, shared;
            byte value = Chunks.TryGetValue(key, out own) ? own.D[cell] : (byte)255;
            return _networkSharing && SharedChunks.TryGetValue(key, out shared) ? Math.Min(value, shared.D[cell]) : value;
        }
        static void QueuePart(Queue<PendingFog> queue, long key, Chunk chunk, int mask)
        {
            if (chunk.Pending == 0) queue.Enqueue(new PendingFog { Key = key, Data = chunk });
            chunk.Pending |= mask;
        }
        static void QueueAll(Queue<PendingFog> queue, Dictionary<long, Chunk> chunks)
        { foreach (var pair in chunks) QueuePart(queue, pair.Key, pair.Value, 255); }
        static void ClearPending(Queue<PendingFog> queue)
        { while (queue.Count != 0) queue.Dequeue().Data.Pending = 0; }
        static PendingFog NextPart(Queue<PendingFog> queue, out int part)
        {
            var next = queue.Dequeue(); part = next.Data.Next;
            while ((next.Data.Pending & (1 << part)) == 0) part = (part + 1) & 7;
            next.Data.Next = (part + 1) & 7;
            next.Data.Pending &= ~(1 << part);
            if (next.Data.Pending != 0) queue.Enqueue(next);
            return next;
        }
        static void ResetSharing()
        {
            ClearPending(PersonalPending); ClearPending(SharedPending); SharedChunks.Clear();
            _networkSharing = _requestFog = false; _fogEpoch = 0; _sendingFog = new PendingFog();
        }
        static void VisionChanged(object sender, EventArgs args) { _window = _wasOpen = false; PrepareSharing(); }
        static void PrepareSharing()
        {
            ulong epoch = MultiplayerParts.Epoch;
            bool enabled = Sharing && epoch != 0;
            if (enabled == _networkSharing && (!enabled || epoch == _fogEpoch)) return;
            ClearPending(PersonalPending); ClearPending(SharedPending);
            if (Multiplayer.Client) SharedChunks.Clear();
            _networkSharing = enabled; _fogEpoch = epoch; _requestFog = enabled;
            _window = _wasOpen = false;
            if (enabled)
            {
                QueueAll(PersonalPending, Chunks);
                if (Multiplayer.Hosting) QueueAll(SharedPending, SharedChunks);
            }
            CleanHudModule.Log("Fog shared vision " + (enabled ? "on; personal exploration retained separately" : "off; personal exploration restored"));
        }

        // Reuse the existing 0.2 s exploration job; stream bounded blocks rather than serializing the map at once.
        static void SharingTick()
        {
            PrepareSharing();
            if (!_networkSharing || SaveBus.Busy) return;
            if (_requestFog) { _requestFog = false; Multiplayer.Send(FogRequest, FogHeaderWriter); }
            for (int n = 0; n < 4 && PersonalPending.Count != 0; n++)
            {
                int part; var next = NextPart(PersonalPending, out part);
                if (Multiplayer.Hosting) MergeShared(next.Key, part, next.Data.D, part * FogPartSize);
                else SendFogPart(FogPersonal, next, part);
            }
            if (Multiplayer.Hosting)
                for (int n = 0; n < 4 && SharedPending.Count != 0; n++)
                { int part; var next = NextPart(SharedPending, out part); SendFogPart(FogShared, next, part); }
        }
        static void WriteFogHeader(BinaryWriter writer) { writer.Write(_fogEpoch); writer.Write(_seed); }
        static void WriteFogPart(BinaryWriter writer)
        {
            WriteFogHeader(writer);
            writer.Write((int)(_sendingFog.Key >> 32)); writer.Write((int)_sendingFog.Key);
            writer.Write((byte)_sendingPart); writer.Write(_sendingFog.Data.D, _sendingPart * FogPartSize, FogPartSize);
        }
        static void SendFogPart(byte kind, PendingFog next, int part)
        { _sendingFog = next; _sendingPart = part; Multiplayer.Send(kind, FogPartWriter); _sendingFog = new PendingFog(); }
        static void FogMessage(ulong peer, byte kind, BinaryReader reader)
        {
            if (kind < FogRequest || kind > FogShared || !Sharing) return;
            if (Multiplayer.Hosting ? kind == FogShared || peer == 0 || Multiplayer.PlayerFor(peer) == null : kind == FogPersonal || peer != 0) return;
            PrepareSharing();
            if (!_networkSharing || reader.ReadUInt64() != _fogEpoch || reader.ReadInt32() != _seed) return;
            if (kind == FogRequest)
            {
                if (reader.BaseStream.Position == reader.BaseStream.Length)
                { if (Multiplayer.Hosting) QueueAll(SharedPending, SharedChunks); else QueueAll(PersonalPending, Chunks); }
                return;
            }
            int cx = reader.ReadInt32(), cz = reader.ReadInt32(), part = reader.ReadByte();
            if (cx < int.MinValue / 64 || cx > int.MaxValue / 64 || cz < int.MinValue / 64 || cz > int.MaxValue / 64) return;
            if (part >= 8 || reader.BaseStream.Length - reader.BaseStream.Position != FogPartSize) return;
            if (reader.Read(FogIncoming, 0, FogPartSize) == FogPartSize) MergeShared(Key(cx, cz), part, FogIncoming, 0);
        }
        static void MergeShared(long key, int part, byte[] source, int offset)
        {
            Chunk chunk;
            if (!SharedChunks.TryGetValue(key, out chunk)) { chunk = new Chunk(); SharedChunks.Add(key, chunk); }
            bool changed = false; int start = part * FogPartSize;
            for (int i = 0; i < FogPartSize; i++)
            {
                byte value = source[offset + i]; int cell = start + i;
                if (value >= chunk.D[cell]) continue;
                chunk.D[cell] = value; changed = true;
                if (!_window) continue;
                int x = (int)(key >> 32) * 64 + (cell & 63) - _winX, z = (int)key * 64 + (cell >> 6) - _winZ;
                if (x > 0 && z > 0 && x < Window - 1 && z < Window - 1)
                { _buffer[z * Window + x] = FogAt(key, cell); _uploadPending = true; }
            }
            if (changed && Multiplayer.Hosting) QueuePart(SharedPending, key, chunk, 1 << part);
        }

        static string SharedPath(string slot) { return Path.Combine(_saves, Path.GetFileName(slot) + ".shared.fog.bin"); }
        static void LoadShared(string slot)
        {
            if (Multiplayer.Client || string.IsNullOrEmpty(slot) || _seed == int.MinValue) return;
            try
            {
                string path = SharedPath(slot);
                if (File.Exists(path)) Read(File.ReadAllBytes(path), SharedChunks);
            }
            catch (Exception e) { SharedChunks.Clear(); CleanHudModule.Warn("Shared fog of war could not be read: " + e.Message); }
        }
        static void SaveShared(string slot)
        {
            if (Multiplayer.Client || SharedChunks.Count == 0) return;
            string path = SharedPath(slot);
            File.WriteAllBytes(path + ".tmp", Write(_seed, SharedChunks));
            AtomicFile.Replace(path + ".tmp", path);
        }
    }
}
