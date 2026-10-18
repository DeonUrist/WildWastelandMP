using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Patrols
{
    internal sealed partial class WorldTraffic
    {
        static MultiplayerSnapshot _network;
        static GameObject _networkHost;
        readonly Dictionary<string, int[]> _networkMembers = new Dictionary<string, int[]>();
        static readonly List<GameObject> NetworkMembers = new List<GameObject>();
        sealed class NetworkCamp { internal Vector3 Center; internal CampState State; }
        static readonly List<NetworkCamp> NetworkCamps = new List<NetworkCamp>();
        internal sealed class NetworkCampStatus : ICampStatus
        {
            public CampState At(Vector3 world, float radius)
            {
                CampState result = CampState.Unknown; float nearest = radius * radius;
                foreach (var camp in NetworkCamps) { var delta = camp.Center - world; delta.y = 0; if (delta.sqrMagnitude <= nearest) { nearest = delta.sqrMagnitude; result = camp.State; } }
                return result;
            }
        }
        internal static void StartNetwork(IModule owner, bool client)
        {
            StopNetwork(); if (!Multiplayer.Present) return;
            if (client)
            {
                _networkHost = new GameObject("WW.Patrols.Presentation") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(_networkHost); _networkHost.AddComponent<WorldTraffic>();
                Scheduler.Interval(owner, "Patrol presentation compass", .5f, TrafficCompass.Tick);
                Scheduler.Gui(owner, "Patrol presentation map", DrawNetwork, WantsNetwork);
            }
            _network = new MultiplayerSnapshot(owner, "Patrol world snapshot", 70, 71, CaptureNetwork, ApplyNetwork);
            Multiplayer.SessionChanged += ResetNetwork;
        }
        internal static void StopNetwork()
        {
            Multiplayer.SessionChanged -= ResetNetwork;
            if (_network != null) _network.Dispose(); _network = null;
            if (_networkHost != null)
            {
                var view = _networkHost.GetComponent<WorldTraffic>();
                if (view != null)
                {
                    if (view._gameInk.Texture != null) UnityEngine.Object.Destroy(view._gameInk.Texture);
                    if (view._skull != null) UnityEngine.Object.Destroy(view._skull);
                    if (view._mapDot != null) UnityEngine.Object.Destroy(view._mapDot);
                    if (_instance == view) _instance = null;
                }
                UnityEngine.Object.Destroy(_networkHost); _networkHost = null; TrafficCompass.Clear();
            }
        }
        static void ResetNetwork()
        {
            NetworkCamps.Clear();
            if (_networkHost == null || _instance == null) return;
            _instance._journeys.Clear(); _instance._battles.Clear(); _instance._networkMembers.Clear(); _instance._gameInkAt = 0;
            TrafficCompass.Clear();
        }
        static void DrawNetwork() { if (_networkHost != null && _instance != null) _instance.Gui(); }
        static bool WantsNetwork() { return _networkHost != null && _instance != null && _instance.GuiWanted(); }
        static void WritePoint(BinaryWriter w, Vector3 p) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
        static Vector3 ReadPoint(BinaryReader r)
        {
            var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z)) throw new InvalidDataException("Invalid world point");
            return p;
        }
        static string ReadName(BinaryReader r, int max = 256) { string name = r.ReadString(); if (name.Length > max) throw new InvalidDataException("World label too long"); return name; }
        static byte[] CaptureNetwork()
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                var source = _instance; int count = source != null ? source._journeys.Count : 0;
                writer.Write(count);
                if (source != null) foreach (var journey in source._journeys)
                {
                    writer.Write(journey.id); writer.Write(journey.name ?? ""); writer.Write(journey.faction ?? "Scrapyard");
                    writer.Write(journey.foot); writer.Write(journey.convoy); writer.Write(journey.merchant); WritePoint(writer, journey.position);
                    // Only map presentation is sampled; the host's simulation route remains intact.
                    int points = Math.Min(512, journey.route.Length); writer.Write(points);
                    for (int p = 0; p < points; p++) WritePoint(writer, journey.route[points <= 1 ? 0 : (int)((long)p * (journey.route.Length - 1) / (points - 1))]);
                    NetworkMembers.Clear(); FootLive foot; Live live;
                    if (journey.foot && source._footLive.TryGetValue(journey.id, out foot)) foreach (var body in foot.Bodies) { if (body != null) NetworkMembers.Add(body); }
                    else if (!journey.foot && source._live.TryGetValue(journey.id, out live)) foreach (var car in live.Cars.Values) { if (car != null) NetworkMembers.Add(car); }
                    int bodies = Math.Min(100, NetworkMembers.Count); writer.Write((byte)bodies); for (int b = 0; b < bodies; b++) writer.Write(Multiplayer.ObjectId(NetworkMembers[b]));
                }
                writer.Write(source != null ? source._battles.Count : 0);
                if (source != null) foreach (var battle in source._battles) { writer.Write(battle.id); WritePoint(writer, battle.position); writer.Write(battle.resolved); }
                int camps = Math.Min(4096, TrafficLocations.Known.Count); writer.Write(camps);
                foreach (var camp in TrafficLocations.Known.Values)
                { if (camps-- <= 0) break; WritePoint(writer, camp.center); writer.Write((byte)(camp.cleared ? CampState.Cleared : TrafficCamps.Loaded(camp.id) ? TrafficCamps.Ready(camp.id) ? CampState.Ready : CampState.Spawning : CampState.Unloaded)); }
                return stream.ToArray();
            }
        }
        static void ApplyNetwork(byte[] data)
        {
            if (_networkHost == null || _instance == null) return;
            var incoming = new List<TrafficJourney>(); var members = new Dictionary<string, int[]>(); var battles = new List<TrafficBattle>(); var camps = new List<NetworkCamp>();
            using (var reader = new BinaryReader(new MemoryStream(data, false)))
            {
                int count = reader.ReadInt32(); if (count < 0 || count > 128) throw new InvalidDataException("Too many traffic groups");
                for (int i = 0; i < count; i++)
                {
                    var j = new TrafficJourney { id = ReadName(reader), name = ReadName(reader), faction = ReadName(reader, 32), foot = reader.ReadBoolean(), convoy = reader.ReadBoolean(), merchant = reader.ReadBoolean(), position = ReadPoint(reader) };
                    int points = reader.ReadInt32(); if (points < 0 || points > 20000) throw new InvalidDataException("Too many route points");
                    j.route = new Vector3[points]; for (int p = 0; p < points; p++) j.route[p] = ReadPoint(reader);
                    j.vehicles = j.foot ? new TrafficVehicle[0] : new[] { new TrafficVehicle() };
                    int bodies = reader.ReadByte(); if (bodies > 100) throw new InvalidDataException("Too many group members");
                    var ids = new int[bodies]; for (int p = 0; p < bodies; p++) ids[p] = reader.ReadInt32(); members.Add(j.id, ids); incoming.Add(j);
                }
                count = reader.ReadInt32(); if (count < 0 || count > 64) throw new InvalidDataException("Too many battles");
                for (int i = 0; i < count; i++) battles.Add(new TrafficBattle { id = ReadName(reader), position = ReadPoint(reader), resolved = reader.ReadBoolean() });
                count = reader.ReadInt32(); if (count < 0 || count > 4096) throw new InvalidDataException("Too many camp states");
                for (int i = 0; i < count; i++) { var center = ReadPoint(reader); byte state = reader.ReadByte(); if (state > (byte)CampState.Cleared) throw new InvalidDataException("Invalid camp state"); camps.Add(new NetworkCamp { Center = center, State = (CampState)state }); }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing traffic data");
            }
            var view = _instance; var previous = new Dictionary<string, TrafficJourney>(); foreach (var j in view._journeys) previous[j.id] = j;
            view._journeys.Clear(); view._networkMembers.Clear();
            foreach (var fresh in incoming)
            {
                TrafficJourney existing;
                if (previous.TryGetValue(fresh.id, out existing)) { existing.position = fresh.position; existing.route = fresh.route; existing.name = fresh.name; existing.faction = fresh.faction; existing.convoy = fresh.convoy; existing.merchant = fresh.merchant; view._journeys.Add(existing); }
                else view._journeys.Add(fresh);
                view._networkMembers.Add(fresh.id, members[fresh.id]);
            }
            view._battles.Clear(); view._battles.AddRange(battles); view._gameInkAt = 0;
            NetworkCamps.Clear(); NetworkCamps.AddRange(camps);
        }
        static Transform NetworkTarget(TrafficJourney journey)
        {
            int[] members; if (_instance == null || !_instance._networkMembers.TryGetValue(journey.id, out members)) return null;
            foreach (int id in members) { var go = Multiplayer.ObjectById(id); if (go != null && go.activeInHierarchy) return go.transform; }
            return null;
        }
    }
}
