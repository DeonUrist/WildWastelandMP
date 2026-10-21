using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // Nearby native repair/refill changes are deltas applied by the actual simulation owner.
    internal static class MultiplayerEdits
    {
        sealed class Edit { internal int Id; internal uint Command; internal ulong Owner; internal byte[] Data; internal float Retry; internal bool Prepared, Settling; internal readonly HashSet<ulong> Waiting = new HashSet<ulong>(); }
        static readonly SortedDictionary<uint, Edit> Pending = new SortedDictionary<uint, Edit>();
        static readonly Dictionary<uint, bool> Receipts = new Dictionary<uint, bool>();
        static readonly List<uint> Done = new List<uint>();
        static readonly HashSet<int> Active = new HashSet<int>();
        static readonly Dictionary<int, uint> Revisions = new Dictionary<int, uint>();
        static uint _command;
        static ulong _epoch;
        static void Epoch()
        { if (_epoch != MultiplayerParts.Epoch) { _epoch = MultiplayerParts.Epoch; Pending.Clear(); Receipts.Clear(); Revisions.Clear(); _command = 0; } }
        internal static bool WaitingForSnapshot(int id, ulong peer)
        { foreach (var edit in Pending.Values) if (edit.Id == id && (!edit.Settling || edit.Waiting.Contains(peer))) return true; return false; }
        internal static bool Busy { get { return Pending.Count != 0; } }
        internal static string SavePending { get { return "item edits " + Pending.Count; } }
        internal static void Install()
        {
            Multiplayer.Message += Receive;
            Scheduler.Interval(Multiplayer.PatchOwner, "Item interaction authority", .1f, Tick);
        }
        internal static void Submit(GameObject root, NamedVariable[] fields, object[] baseline)
        {
            if (MultiplayerParts.Epoch == 0 || MultiplayerItems.ServerOwned(root)) return;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)0); int count = 0;
                for (int i = 0; i < fields.Length && i < baseline.Length; i++)
                {
                    if (stream.Length > 350) { SendChanges(root, stream, writer, count); stream.SetLength(0); stream.Position = 0; writer.Write((byte)0); count = 0; }
                    if (fields[i] is FsmBool && baseline[i] is bool)
                    { bool value = ((FsmBool)fields[i]).Value; if (value != (bool)baseline[i]) { writer.Write((byte)i); writer.Write((byte)2); writer.Write((bool)baseline[i]); writer.Write(value); count++; } continue; }
                    if (fields[i] is FsmString && baseline[i] is string)
                    {
                        string value = ((FsmString)fields[i]).Value ?? "", before = (string)baseline[i] ?? "";
                        if (value != before && System.Text.Encoding.UTF8.GetByteCount(value) <= 256 && System.Text.Encoding.UTF8.GetByteCount(before) <= 256)
                        { writer.Write((byte)i); writer.Write((byte)3); writer.Write(before); writer.Write(value); count++; }
                        continue;
                    }
                    double delta; byte type;
                    if (fields[i] is FsmFloat && baseline[i] is float) { delta = ((FsmFloat)fields[i]).Value - (float)baseline[i]; type = 0; }
                    else if (fields[i] is FsmInt && baseline[i] is int) { delta = (long)((FsmInt)fields[i]).Value - (int)baseline[i]; type = 1; }
                    else continue;
                    if (Math.Abs(delta) < .00001 || double.IsNaN(delta) || double.IsInfinity(delta) || Math.Abs(delta) > 1000000) continue;
                    writer.Write((byte)i); writer.Write(type); writer.Write(delta); count++;
                }
                SendChanges(root, stream, writer, count);
            }
        }
        static void SendChanges(GameObject root, MemoryStream stream, BinaryWriter writer, int count)
        {
            if (count == 0) return;
            stream.Position = 0; writer.Write((byte)count); var data = stream.ToArray(); int id = Multiplayer.ObjectId(root);
            if (Multiplayer.Hosting) Queue(id, data);
            else Multiplayer.Send(110, w => { w.Write(MultiplayerParts.Epoch); w.Write(id); w.Write((ushort)data.Length); w.Write(data); });
        }
        static void Queue(int id, byte[] data)
        { Epoch(); if (id != 0 && Pending.Count < 1024) { uint command = ++_command; Pending.Add(command, new Edit { Id = id, Command = command, Owner = Multiplayer.OwnerOf(Multiplayer.ObjectById(id)), Data = data }); } }
        static void Tick()
        {
            Epoch();
            if (!Multiplayer.Hosting || !Multiplayer.WorldReady || MultiplayerParts.Pending) return;
            Done.Clear(); Active.Clear(); int budget = 16; var peers = MultiplayerReliable.ConnectedPeers();
            foreach (var pair in Pending)
            {
                var edit = pair.Value; var root = Multiplayer.ObjectById(edit.Id);
                if (root == null) { Done.Add(pair.Key); continue; }
                if (!Active.Add(edit.Id)) continue;
                if (edit.Settling)
                {
                    edit.Waiting.RemoveWhere(peer => !peers.Contains(peer));
                    if (edit.Waiting.Count == 0) { Done.Add(pair.Key); continue; }
                    if (Time.unscaledTime >= edit.Retry && budget > 0) { edit.Retry = Time.unscaledTime + 1f; foreach (ulong peer in edit.Waiting) Snapshot(edit, peer); budget--; }
                    continue;
                }
                if (budget <= 0 || Time.unscaledTime < edit.Retry) continue;
                if (!edit.Prepared)
                {
                    var prepared = MultiplayerItems.PrepareEdit(root, edit.Data);
                    if (prepared == null) { Done.Add(pair.Key); continue; }
                    edit.Data = prepared; edit.Prepared = true;
                }
                edit.Owner = Multiplayer.OwnerOf(root);
                if (edit.Owner == 0) { if (MultiplayerItems.Edit(root, edit.Data, true)) Complete(edit); budget--; continue; }
                edit.Retry = Time.unscaledTime + 1f; budget--;
                Multiplayer.Send(111, w => { w.Write(_epoch); w.Write(edit.Command); w.Write(edit.Id); w.Write((ushort)edit.Data.Length); w.Write(edit.Data); }, edit.Owner);
            }
            foreach (uint command in Done) Pending.Remove(command);
        }
        static void Snapshot(Edit edit, ulong peer)
        { Multiplayer.Send(113, w => { w.Write(_epoch); w.Write(edit.Command); w.Write(edit.Id); w.Write((ushort)edit.Data.Length); w.Write(edit.Data); }, peer); }
        static void Complete(Edit edit)
        {
            var root = Multiplayer.ObjectById(edit.Id);
            if (root == null || !MultiplayerItems.Edit(root, edit.Data, true, true)) return;
            edit.Settling = true; edit.Retry = Time.unscaledTime + 1f;
            foreach (ulong peer in MultiplayerReliable.ConnectedPeers()) { edit.Waiting.Add(peer); Snapshot(edit, peer); }
            if (Log.Verbose) Log.Info("[Multiplayer] Item edit committed: " + edit.Id + ", command " + edit.Command + "; waiting for snapshots");
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if ((kind < 110 || kind > 113) && kind != 134 || !Multiplayer.WorldReady || reader.ReadUInt64() != MultiplayerParts.Epoch) return;
            Epoch();
            if (kind == 134)
            {
                uint command = reader.ReadUInt32(); Edit edit;
                if (Multiplayer.Hosting && reader.BaseStream.Position == reader.BaseStream.Length && Pending.TryGetValue(command, out edit) && edit.Settling) edit.Waiting.Remove(from);
                return;
            }
            if (kind == 112)
            {
                uint command = reader.ReadUInt32(); bool ok = reader.ReadBoolean(); Edit edit;
                if (!Multiplayer.Hosting || reader.BaseStream.Position != reader.BaseStream.Length || !Pending.TryGetValue(command, out edit) || edit.Owner != from || edit.Settling) return;
                if (ok) Complete(edit);
                else { edit.Owner = Multiplayer.OwnerOf(Multiplayer.ObjectById(edit.Id)); edit.Retry = 0; }
                return;
            }
            uint operation = kind == 111 || kind == 113 ? reader.ReadUInt32() : 0; int id = reader.ReadInt32(); int size = reader.ReadUInt16();
            if (size < 1 || size > 1000 || size != reader.BaseStream.Length - reader.BaseStream.Position) return;
            var data = reader.ReadBytes(size); var root = Multiplayer.ObjectById(id); if (root == null) return;
            if (kind == 110)
            {
                var player = Multiplayer.PlayerFor(from);
                if (Multiplayer.Hosting && from != 0 && player != null && !MultiplayerItems.ServerOwned(root) && (root.transform.position - player.transform.position).sqrMagnitude < 16f) Queue(id, data);
                return;
            }
            if (!Multiplayer.Client || from != 0) return;
            if (kind == 113)
            {
                uint revision;
                if (!Revisions.TryGetValue(id, out revision) || operation > revision)
                { if (!MultiplayerItems.Edit(root, data, true, true, false)) return; Revisions[id] = operation; }
                Multiplayer.Send(134, w => { w.Write(MultiplayerParts.Epoch); w.Write(operation); }); return;
            }
            bool applied;
            if (!Receipts.TryGetValue(operation, out applied))
            {
                uint revision;
                applied = Revisions.TryGetValue(id, out revision) && operation <= revision || MultiplayerItems.Edit(root, data, true);
                // A rejected ownership handoff may be retried after the native grant arrives.
                if (applied) Receipts[operation] = true;
            }
            Multiplayer.Send(112, w => { w.Write(MultiplayerParts.Epoch); w.Write(operation); w.Write(applied); });
        }
    }
}
