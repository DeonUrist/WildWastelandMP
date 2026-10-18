using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerCompass
    {
        const string Owner = "WildWasteland.Players";
        sealed class Marker
        {
            internal int Id; internal Vector3 World; internal string Name;
            internal Vector3 Position() { return Origin.Scene(World); }
        }
        static readonly Dictionary<ulong, Marker> Markers = new Dictionary<ulong, Marker>();
        static readonly HashSet<ulong> Seen = new HashSet<ulong>();
        static readonly List<ulong> Removed = new List<ulong>();
        internal struct MapPlayer { internal ulong Peer; internal Vector3 World; }
        internal static readonly List<MapPlayer> MapPlayers = new List<MapPlayer>();
        static Func<IList> _players;
        static Func<object, ulong> _id;
        static Func<object, bool> _hasPosition;
        static Func<object, Vector3> _position;
        static Func<object, string> _name;
        static Func<ulong, Color> _color;
        static ICompassMarkers _api;
        static float _next;
        internal static void Clear()
        {
            if (_api != null) _api.RemoveAll(Owner);
            _api = null; Markers.Clear(); MapPlayers.Clear(); Seen.Clear(); Removed.Clear(); _next = 0;
        }
        internal static void Tick()
        {
            if (!Multiplayer.WorldReady) { if (Markers.Count > 0) Clear(); return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + .2f;
            var api = Services.Compass;
            if (!ReferenceEquals(api, _api)) { Clear(); _api = api; }
            var playersApi = api as IPlayerCompassMarkers;
            if (_players == null)
            {
                var session = Multiplayer.Type("Net.NetSession"); var info = Multiplayer.Type("Net.NetSession+PlayerInfo");
                _players = MultiplayerAccess.Call<Func<IList>>(session, "Players", Type.EmptyTypes);
                _id = MultiplayerAccess.Get<ulong>(info, "Id"); _hasPosition = MultiplayerAccess.Get<bool>(info, "HasPosition");
                _position = MultiplayerAccess.Get<Vector3>(info, "LocalPosition"); _name = MultiplayerAccess.Get<string>(info, "Name");
                _color = MultiplayerAccess.Call<Func<ulong, Color>>(Multiplayer.Type("UI.CompassBar"), "ColorOf", typeof(ulong));
            }
            Seen.Clear();
            MapPlayers.Clear();
            foreach (var player in _players())
            {
                if (!_hasPosition(player)) continue;
                ulong id = _id(player); var world = Origin.World(_position(player));
                MapPlayers.Add(new MapPlayer { Peer = id, World = world });
                if (playersApi == null || !playersApi.PlayersEnabled) continue;
                Seen.Add(id); Marker marker;
                if (!Markers.TryGetValue(id, out marker)) { marker = new Marker(); Markers.Add(id, marker); }
                marker.World = world;
                string name = (_name(player) ?? "Player").Replace("<", "").Replace(">", "");
                if (name.Length > 24) name = name.Substring(0, 24);
                if (marker.Id != 0 && marker.Name != name) { api.Remove(marker.Id); marker.Id = 0; }
                marker.Name = name;
                if (marker.Id == 0 || !api.Exists(marker.Id)) marker.Id = playersApi.AddPlayer(Owner, marker.Position, marker.Name, _color(id));
            }
            Removed.Clear();
            foreach (var pair in Markers) if (!Seen.Contains(pair.Key)) { if (api != null) api.Remove(pair.Value.Id); Removed.Add(pair.Key); }
            foreach (var id in Removed) Markers.Remove(id);
        }
    }
}
