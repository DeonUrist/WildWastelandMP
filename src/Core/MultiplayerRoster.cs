using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WildWasteland.Core
{
    // A complete, numbered host snapshot replaces the visible roster atomically.
    internal static class MultiplayerRoster
    {
        const byte Request = 169, Snapshot = 170;
        internal sealed class Player
        {
            internal ulong Peer, Epoch; internal int SleeperId; internal object Record;
            internal string Name, Vehicle; internal Color Color; internal Vector3 Position;
            internal float Health, Armor, Bolts; internal bool Sleeping, Downed, HasPosition;
        }
        internal static readonly List<Player> Rows = new List<Player>();
        static readonly List<Player> Building = new List<Player>();
        static readonly Dictionary<ulong, float> Requests = new Dictionary<ulong, float>();
        static Func<IList> _players;
        static Func<object, ulong> _id;
        static Func<object, bool> _hasPosition;
        static Func<object, Vector3> _position;
        static Func<object, string> _name;
        static Func<object, Dictionary<string, float>> _stats;
        static Func<ulong, object> _record;
        static Func<Dictionary<string, float>> _capture;
        static Func<bool> _passenger;
        static float _next;
        static uint _sequence, _incoming;
        static int _expected;
        static ulong _epoch;
        static PlayMakerFSM _inCar;
        internal static void Install()
        {
            var session = Multiplayer.Type("Net.NetSession"); var info = Multiplayer.Type("Net.NetSession+PlayerInfo");
            _players = MultiplayerAccess.Call<Func<IList>>(session, "Players", Type.EmptyTypes);
            _id = MultiplayerAccess.Get<ulong>(info, "Id"); _name = MultiplayerAccess.Get<string>(info, "Name");
            _hasPosition = MultiplayerAccess.Get<bool>(info, "HasPosition"); _position = MultiplayerAccess.Get<Vector3>(info, "LocalPosition");
            _stats = MultiplayerAccess.Get<Dictionary<string, float>>(Multiplayer.Type("Worlds.PlayerRecord"), "Stats");
            _record = MultiplayerAccess.Call<Func<ulong, object>>(session, "RecordOf", typeof(ulong));
            _capture = MultiplayerAccess.Call<Func<Dictionary<string, float>>>(Multiplayer.Type("Game.PlayerVitals"), "Capture", Type.EmptyTypes);
            _passenger = MultiplayerAccess.Call<Func<bool>>(Multiplayer.Type("Net.WorldObjects"), "get_IsLocalPassenger", Type.EmptyTypes);
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
        }
        static void Reset() { Rows.Clear(); Building.Clear(); Requests.Clear(); _next = 0; _incoming = _sequence = 0; _epoch = 0; }
        internal static void Capture(Dictionary<string, float> stats)
        {
            var color = MultiplayerNames.ColorOf(Multiplayer.LocalPeer);
            stats["ww.colorR"] = color.r; stats["ww.colorG"] = color.g; stats["ww.colorB"] = color.b;
            stats["ww.downed"] = Multiplayer.LocalTargetable ? 0 : 1;
        }
        static float Stat(Dictionary<string, float> stats, string key, float fallback = 0)
        { float value; return stats != null && stats.TryGetValue(key, out value) && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback; }
        static Player FromStats(Dictionary<string, float> stats)
        {
            return new Player { Epoch = MultiplayerParts.Epoch, Health = Stat(stats, "p.Health", -1), Armor = Stat(stats, "ww.armor", -1), Bolts = Stat(stats, "g.Money", -1),
                Downed = Stat(stats, "ww.downed") != 0, Color = new Color(Stat(stats, "ww.colorR", .7f), Stat(stats, "ww.colorG", .7f), Stat(stats, "ww.colorB", .7f)) };
        }
        internal static Player Saved(object record, string name, Vector3 position)
        { var row = FromStats(_stats(record)); row.Record = record; row.Name = Clean(name); row.Position = position; row.HasPosition = row.Sleeping = true; row.Vehicle = ""; return row; }
        internal static void Invalidate() { _next = 0; }
        static string Clean(string text)
        {
            text = (text ?? "").Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " ");
            return text.Length > 64 ? text.Substring(0, 64) : text;
        }
        internal static void Refresh()
        {
            if (!Multiplayer.WorldReady || MultiplayerSaving.Frozen) { Rows.Clear(); return; }
            if (_epoch != MultiplayerParts.Epoch) { Reset(); _epoch = MultiplayerParts.Epoch; }
            if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 1f;
            if (Multiplayer.Hosting) { Build(); Rows.Clear(); Rows.AddRange(Building); }
            else Multiplayer.Send(Request, w => w.Write(_epoch), 0);
        }
        static void Build()
        {
            Building.Clear();
            var host = FromStats(_capture()); host.Name = Clean(Multiplayer.PlayerName); host.Color = MultiplayerNames.ColorOf(0);
            host.HasPosition = GameRefs.Player != null; host.Position = host.HasPosition ? Origin.World(GameRefs.Player.transform.position) : Vector3.zero;
            host.Downed = !Multiplayer.LocalTargetable; host.Vehicle = "";
            if (_inCar == null && GameRefs.Player != null) _inCar = FsmUtil.Find(GameRefs.Player, "InCar");
            var car = _inCar != null && _inCar.ActiveStateName == "InCar" ? _inCar.FsmVariables.FindFsmGameObject("Car") : null;
            if (car != null && car.Value != null && !_passenger()) host.Vehicle = VehicleName(car.Value);
            Building.Add(host);
            foreach (var player in _players())
            {
                ulong peer = _id(player); if (peer == 0) continue;
                var record = _record(peer); var row = FromStats(record != null ? _stats(record) : null);
                // Reconnecting players keep their sleeping body until the existing handoff completes.
                if (record != null && MultiplayerSleepers.PendingRecord(record)) continue;
                row.Name = Clean(_name(player)); row.Color = MultiplayerNames.ColorOf(peer);
                row.Peer = peer; row.Record = record;
                row.HasPosition = _hasPosition(player); row.Position = row.HasPosition ? Origin.World(_position(player)) : Vector3.zero;
                row.Downed = MultiplayerPlayers.Downed(peer); row.Vehicle = VehicleName(MultiplayerPlayers.Driving(peer)); Building.Add(row);
            }
            MultiplayerSleepers.Roster(Building);
        }
        static string VehicleName(GameObject car)
        {
            if (car == null) return "";
            string name = car.name; int suffix = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return Clean(suffix >= 0 ? name.Substring(0, suffix) : name).Replace('_', ' ');
        }
        static void Receive(ulong from, byte kind, BinaryReader r)
        {
            if (kind != Request && kind != Snapshot || !Multiplayer.WorldReady || MultiplayerSaving.Frozen) return;
            ulong epoch = r.ReadUInt64(); if (epoch != MultiplayerParts.Epoch) return;
            if (kind == Request)
            {
                if (!Multiplayer.Hosting || !MultiplayerSleepers.WelcomedPeer(from) || r.BaseStream.Position != r.BaseStream.Length) return;
                float next; if (Requests.TryGetValue(from, out next) && Time.unscaledTime < next) return;
                Requests[from] = Time.unscaledTime + .8f; Build(); uint sequence = ++_sequence;
                for (int i = 0; i < Building.Count; i++)
                {
                    int index = i; var row = Building[i];
                    Multiplayer.Send(Snapshot, w => { w.Write(epoch); w.Write(sequence); w.Write((ushort)Building.Count); w.Write((ushort)index); Write(w, row); }, from);
                }
                return;
            }
            if (!Multiplayer.Client || from != 0) return;
            uint incoming = r.ReadUInt32(); int count = r.ReadUInt16(), offset = r.ReadUInt16();
            if (count < 1 || count > 1024 || offset >= count || incoming < _incoming) return;
            var value = Read(r); if (r.BaseStream.Position != r.BaseStream.Length) return;
            if (incoming != _incoming) { _incoming = incoming; _expected = count; Building.Clear(); }
            if (_expected != count || offset != Building.Count) return;
            Building.Add(value);
            if (Building.Count == count) { Rows.Clear(); Rows.AddRange(Building); }
        }
        static void Write(BinaryWriter w, Player row)
        {
            w.Write(row.Name); w.Write(row.Vehicle); w.Write(row.Sleeping); w.Write(row.Downed); w.Write(row.HasPosition);
            w.Write(row.Color.r); w.Write(row.Color.g); w.Write(row.Color.b); w.Write(row.Health); w.Write(row.Armor); w.Write(row.Bolts);
            w.Write(row.Position.x); w.Write(row.Position.y); w.Write(row.Position.z);
        }
        static float Number(BinaryReader r) { float v = r.ReadSingle(); if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Invalid roster number"); return v; }
        static Player Read(BinaryReader r)
        {
            return new Player { Name = Clean(r.ReadString()), Vehicle = Clean(r.ReadString()), Sleeping = r.ReadBoolean(), Downed = r.ReadBoolean(), HasPosition = r.ReadBoolean(),
                Color = new Color(Number(r), Number(r), Number(r)), Health = Number(r), Armor = Number(r), Bolts = Number(r), Position = new Vector3(Number(r), Number(r), Number(r)) };
        }
    }
}
