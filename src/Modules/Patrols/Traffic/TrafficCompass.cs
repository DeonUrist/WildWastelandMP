using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Modules.Patrols
{
    internal static class TrafficCompass
    {
        private const string Owner = "WildWasteland.VehicleGroups";
        private sealed class Tracked
        {
            internal int Id, Kind = -1;
            internal Transform Target;
            internal Vector3 Anchor;
            internal bool Known;
            internal Vector3 Position() { return Target != null ? Target.position : TrafficSpace.Scene(Anchor); }
        }
        private static readonly Color[] Colors = { new Color32(255,220,50,255), new Color32(80,220,100,255), new Color32(255,80,80,255), new Color32(255,150,40,255) };
        private static readonly Dictionary<object, Tracked> Markers = new Dictionary<object, Tracked>();
        private static readonly HashSet<object> Seen = new HashSet<object>();
        private static readonly HashSet<string> Journeys = new HashSet<string>();
        private static readonly List<object> Dead = new List<object>();
        private static Core.ICompassMarkers _api;
        internal static bool Known(object key) { Tracked marker; return key != null && Markers.TryGetValue(key, out marker) && marker.Known; }
        internal static object MerchantKey(MerchantHost merchant)
        { var encounter = merchant.GetComponent<EncounterCar>(); return encounter != null && !string.IsNullOrEmpty(encounter.Event) ? (object)encounter.Event : merchant; }

        internal static void Clear()
        {
            if (_api != null && Markers.Count > 0) _api.RemoveAll(Owner);
            _api = null; Markers.Clear(); Seen.Clear(); Journeys.Clear(); Dead.Clear();
        }
        internal static void Tick()
        {
            var api = Core.Services.Compass;
            if (!ReferenceEquals(api, _api)) { Clear(); _api = api; }
            if (api == null && Core.Services.Map == null) { Clear(); return; }
            if ((!PatrolsModule.Running && !Core.Multiplayer.Client) || !Core.GameRefs.InPlay) return;
            Seen.Clear(); Journeys.Clear(); Dead.Clear();
            var journeys = WorldTraffic.CompassJourneys;
            if (PatrolsModule.ConvoyEnabled.Value && journeys != null)
                foreach (var journey in journeys)
                {
                    bool alive = journey.foot;
                    foreach (var vehicle in journey.vehicles) if (!vehicle.retired) { alive = true; break; }
                    if (!alive) continue;
                    Journeys.Add(journey.id);
                    Track(journey, WorldTraffic.CompassTarget(journey), journey.position, Kind(journey.merchant, journey.faction, journey.convoy), WorldTraffic.MapName(journey, true), WorldTraffic.RouteColor(journey, false), !journey.foot, journey);
                }
            // Include parked/manual merchants and editor-spawned groups, without duplicating traffic members.
            foreach (var merchant in MerchantHost.All)
            {
                if (merchant == null || merchant.Data == null) continue;
                var marker = merchant.GetComponent<PatrolMarker>();
                if (marker != null && (marker.PlayerEntered || marker.Exploded || Journeys.Contains(marker.TrafficId))) continue;
                Track(MerchantKey(merchant), merchant.transform, Vector3.zero, 0, merchant.Data.group ?? "Merchant", Colors[0], true);
            }
            foreach (var marker in PatrolMarker.All)
            {
                if (marker == null || marker.PlayerEntered || marker.Exploded || marker.SetupOnly || marker.Driver == null || Journeys.Contains(marker.TrafficId)) continue;
                var encounter = marker.GetComponent<EncounterCar>();
                if (encounter != null && encounter.Building) continue;
                object key = encounter != null && !string.IsNullOrEmpty(encounter.Event) ? (object)encounter.Event : marker;
                int kind = Kind(false, marker.Faction, encounter != null && encounter.Convoy);
                Track(key, marker.transform, Vector3.zero, kind, string.IsNullOrEmpty(marker.TemplateName) ? (kind == 3 ? "Convoy" : "Patrol") : marker.TemplateName, Colors[kind], true);
            }
            foreach (var pair in Markers) if (!Seen.Contains(pair.Key)) { if (api != null) api.Remove(pair.Value.Id); Dead.Add(pair.Key); }
            foreach (var key in Dead) Markers.Remove(key);
        }
        private static int Kind(bool merchant, string faction, bool convoy)
        { return merchant ? 0 : TrafficFactions.Normalize(faction) == "Coyotes" ? 1 : convoy ? 3 : 2; }
        private static void Track(object key, Transform target, Vector3 anchor, int kind, string name, Color color, bool compass, TrafficJourney journey = null)
        {
            bool first = Seen.Add(key);
            Tracked marker;
            if (!Markers.TryGetValue(key, out marker)) Markers.Add(key, marker = new Tracked());
            bool known = marker.Known; int previousId = marker.Id;
            var map = Core.Services.Map;
            bool observed = map != null && (journey != null ? WorldTraffic.ObserveJourney(journey, map, name, color) : map.Observe(target, name, color));
            if (observed && !marker.Known) { marker.Known = true; if (Core.Log.Verbose) Core.Log.Info("[Patrols] Visible group identified: " + name); }
            if (!first) return;
            marker.Target = target; marker.Anchor = anchor;
            if (!compass || _api == null || !_api.VehiclesEnabled)
            { if (marker.Id != 0 && _api != null) _api.Remove(marker.Id); marker.Id = 0; return; }
            if (marker.Kind != kind) { if (marker.Id != 0) _api.Remove(marker.Id); marker.Id = 0; marker.Kind = kind; }
            if (marker.Id == 0 || !_api.Exists(marker.Id)) marker.Id = _api.Add(Owner, marker.Position, "car", Colors[kind]);
            if (marker.Known && (!known || previousId != marker.Id)) _api.Identify(marker.Id);
        }
    }

    internal sealed partial class WorldTraffic
    {
        internal static bool ObserveJourney(TrafficJourney journey, Core.IMapMarkers map, string name, Color color)
        {
            if (_instance == null) return false;
            if (Core.Multiplayer.Client)
            {
                int[] ids; bool observed = false;
                if (_instance._networkMembers.TryGetValue(journey.id, out ids)) foreach (int id in ids) { var body = Core.Multiplayer.ObjectById(id); if (body != null) observed |= map.Observe(body.transform, name, color); }
                return observed;
            }
            bool seen = false;
            if (journey.foot)
            {
                FootLive foot;
                if (_instance._footLive.TryGetValue(journey.id, out foot))
                    foreach (var body in foot.Bodies) if (body != null) seen |= map.Observe(body.transform, name, color);
            }
            else
            {
                Live live;
                if (_instance._live.TryGetValue(journey.id, out live))
                    foreach (var car in live.Cars.Values) if (car != null)
                    {
                        var marker = car.GetComponent<PatrolMarker>();
                        if (marker != null && !marker.PlayerEntered && !marker.Exploded) seen |= map.Observe(car.transform, name, color);
                    }
            }
            return seen;
        }
        internal static IList<TrafficJourney> CompassJourneys { get { return _instance != null ? _instance._journeys : null; } }
        internal static Transform CompassTarget(TrafficJourney journey)
        {
            if (Core.Multiplayer.Client) return NetworkTarget(journey);
            Live live;
            if (_instance != null && journey.foot) return _instance.FootLeader(journey);   // leader walker, else the virtual position
            if (_instance == null || !_instance._live.TryGetValue(journey.id, out live)) return null;
            Transform target = null; int slot = int.MaxValue; bool truck = false;
            foreach (var pair in live.Cars)
            {
                var car = pair.Value; if (car == null) continue;
                var marker = car.GetComponent<PatrolMarker>();
                if (marker == null || marker.PlayerEntered || marker.Exploded) continue;
                bool cargo = pair.Key >= 0 && pair.Key < journey.vehicles.Length && journey.vehicles[pair.Key].truck;
                if (target != null && (!cargo && truck || cargo == truck && pair.Key >= slot)) continue;
                target = car.transform; slot = pair.Key; truck = cargo;
            }
            return target;
        }
    }
}
