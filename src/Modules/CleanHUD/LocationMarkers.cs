using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Den.Tools;
using MapMagic.Products;
using MapMagic.Terrains;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.CleanHUD
{
    internal static class LocationMarkers
    {
        internal const string Owner = "WildWasteland.Locations";
        private sealed class Location
        {
            internal int Marker, MapMarker; internal Transform Root; internal Renderer Native; internal bool ForceOff;
            internal string Icon, Label, Key, Caption; internal Color Color; internal IMapMarkers Map; internal bool Known, Cleared;
            internal Vector3 Position() { return Root != null ? Root.position : Vector3.zero; }
        }
        private static readonly Dictionary<Transform, Location> Locations = new Dictionary<Transform, Location>();
        private static readonly List<Transform> Dead = new List<Transform>();
        private static readonly HashSet<string> Discovered = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loaded, _fresh;
        private static string _slot;
        private static int _seed;

        internal static bool Known(Transform root) { Location location; return root != null && Locations.TryGetValue(root, out location) && location.Known; }
        internal static bool Known(Func<Vector3> position) { var location = position.Target as Location; return location != null && location.Known; }
        internal static bool MissingBake(Func<Vector3> position)
        {
            var location = position.Target as Location;
            return location != null && Services.Npc != null && Services.Npc.HighlightMissingPoiBake(location.Label);
        }
        internal static void Begin(bool fresh)
        {
            Clear(); _loaded = false; _fresh = fresh; Discovered.Clear();
        }
        private static void StateChanged(string previous, string now) { if (now == "setSeed") Begin(true); else if (now == "wait" || now == "SaveGame") SaveEnded(SaveBus.Slot); }
        private static int Seed() { var fsm = GameRefs.SaveLoadGame; var seed = fsm != null ? fsm.FsmVariables.FindFsmInt("seed") : null; return seed != null ? seed.Value : int.MinValue; }
        private static string SavePath(string slot) { return Path.Combine(Path.Combine(Core.Assets.ModuleConfigDir("CleanHUD"), "Saves"), Path.GetFileName(slot) + ".locations.txt"); }
        internal static void WorldReady(string slot)
        {
            int seed = Seed(); if (_loaded && slot == _slot && seed == _seed) return;
            Discovered.Clear(); _slot = slot; _seed = seed; _loaded = seed != int.MinValue;
            try
            {
                if (!_fresh && _loaded && !string.IsNullOrEmpty(slot) && File.Exists(SavePath(slot)))
                {
                    var lines = File.ReadAllLines(SavePath(slot));
                    if (lines.Length > 0 && lines[0] == seed.ToString(CultureInfo.InvariantCulture))
                        for (int i = 1; i < lines.Length; i++) if (lines[i].Length > 0) Discovered.Add(lines[i]);
                }
            }
            catch (Exception e) { CleanHudModule.Warn("Location discoveries could not be read: " + e.Message); }
            _fresh = false;
            CleanHudModule.Log("Location discoveries loaded: " + Discovered.Count + " (seed " + seed + ")");
        }
        internal static void SaveEnded(string slot)
        {
            if (!_loaded || string.IsNullOrEmpty(slot) || Seed() != _seed) return;
            _slot = slot;
            var lines = new string[Discovered.Count + 1]; lines[0] = _seed.ToString(CultureInfo.InvariantCulture); Discovered.CopyTo(lines, 1);
            string path = SavePath(slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path + ".tmp", lines);
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            if (Core.Log.Verbose) Core.Log.Info("[CleanHUD] Location discoveries saved: " + path);
        }

        internal static void Init()
        {
            Clear();
            SaveBus.StateChanged -= StateChanged; SaveBus.StateChanged += StateChanged;
            TerrainTile.OnTileApplied -= Applied;
            TerrainTile.OnTileApplied += Applied;
        }
        private static void Applied(TerrainTile tile, TileData data, StopToken stop)
        {
            if (!CleanHudModule.Running || tile == null) return;
            SceneCache.QueueRoot(tile.transform);
        }
        internal static void Observe(Transform icon)
        {
            var root = icon != null ? icon.parent : null;
            if (root == null || Locations.ContainsKey(root)) return;
            string label = root.name.Split('(')[0].Trim(), glyph = "other"; Color color = Color.white;
            if (label.StartsWith("Cave_", System.StringComparison.OrdinalIgnoreCase)) glyph = "cave";
            else if (label.StartsWith("Wreck_", System.StringComparison.OrdinalIgnoreCase)) glyph = "wreck";
            else if (label.StartsWith("Building_", System.StringComparison.OrdinalIgnoreCase)) glyph = "building";
            else if (label.StartsWith("Camp_", System.StringComparison.OrdinalIgnoreCase))
            {
                glyph = "camp"; int number;
                if (int.TryParse(label.Substring(5), out number)) color = number == 10 || number == 15 || number == 16 ? new Color(.31f, .86f, .39f) : new Color(1f, .31f, .31f);
            }
            var renderer = icon.GetComponent<Renderer>();
            Locations.Add(root, new Location { Root = root, Icon = glyph, Color = color, Label = label, Caption = CaptionName(label), Native = renderer, ForceOff = renderer != null && renderer.forceRenderingOff });
        }
        private static string CaptionName(string label)
        {
            string name = label.Replace('_', ' ').TrimEnd();
            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1])) end--;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Substring(0, end).TrimEnd().ToLowerInvariant());
        }
        internal static void Tick()
        {
            if (!_loaded && GameRefs.InGame && !SaveBus.Busy) WorldReady(SaveBus.Slot);
            var player = Core.GameRefs.Player;
            bool hud = CleanHudModule.Running && CleanHudModule.Enabled.Value;
            bool visible = hud && CleanHudModule.ShowCompass.Value && CleanHudModule.ShowLocations.Value && player != null;
            var camera = Core.GameRefs.MainCamera;
            var origin = camera != null ? camera.transform.position : player != null ? player.transform.position : Vector3.zero;
            Dead.Clear();
            foreach (var pair in Locations)
            {
                var location = pair.Value;
                if (pair.Key != null && _loaded && Origin.AnchorTransform != null && GameRefs.InGame && !SaveBus.Busy && player != null)
                {
                    var world = Origin.World(pair.Key.position);
                    if (location.Key == null)
                    {
                        location.Key = location.Label + ":" + Mathf.RoundToInt(world.x).ToString(CultureInfo.InvariantCulture) + ":" + Mathf.RoundToInt(world.z).ToString(CultureInfo.InvariantCulture);
                        location.Known = Discovered.Contains(location.Key);
                    }
                    var delta = pair.Key.position - player.transform.position; delta.y = 0;
                    bool spotted = Binoculars.Observe(pair.Key, location.Caption, location.Cleared ? Color.white : location.Color) || !location.Known && VantageDiscovery.Sees(pair.Key);
                    if (!location.Known && (delta.sqrMagnitude <= 10000f || spotted))
                    {
                        location.Known = true; Discovered.Add(location.Key);
                        if (Core.Log.Verbose) Core.Log.Info("[CleanHUD] Location discovered: " + location.Key);
                    }
                    if (!location.Cleared && location.Icon == "camp" && location.Color.g < .5f && Services.Camps != null && Services.Camps.At(world, 120f) == CampState.Cleared)
                    {
                        location.Cleared = true;
                        if (location.Map != null) location.Map.Remove(location.MapMarker); location.MapMarker = 0;
                        if (location.Marker != 0) Api.Compass.Remove(location.Marker); location.Marker = 0;
                        if (Core.Log.Verbose) Core.Log.Info("[CleanHUD] Location cleared: " + location.Key);
                    }
                }
                var color = location.Cleared ? Color.white : location.Color;
                if (location.Native != null) location.Native.forceRenderingOff = hud && CleanHudModule.MapEnabled.Value ? true : location.ForceOff;
                if (location.Map != Services.Map) { if (location.Map != null) location.Map.Remove(location.MapMarker); location.Map = Services.Map; location.MapMarker = 0; }
                bool onMap = pair.Key != null && hud && CleanHudModule.MapEnabled.Value && location.Map != null && location.Map.Markers != MapDetail.Off;
                if (!onMap) { if (location.Map != null) location.Map.Remove(location.MapMarker); location.MapMarker = 0; }
                else if (!location.Map.Exists(location.MapMarker)) location.MapMarker = location.Map.Add(Owner, location.Position, location.Icon, color, location.Label);
                if (pair.Key == null || !visible || (pair.Key.position - origin).sqrMagnitude > 1000000f * VantageDiscovery.Scale * VantageDiscovery.Scale)
                {
                    if (location.Marker != 0) Api.Compass.Remove(location.Marker);
                    location.Marker = 0;
                    if (pair.Key == null) Dead.Add(pair.Key);
                }
                else if (location.Marker == 0)
                    location.Marker = Api.Compass.Add(Owner, pair.Key, new Api.Marker { Icon = location.Icon, Color = color, ShowDistance = true, MaxDistance = 1000f });
            }
            foreach (var root in Dead) Locations.Remove(root);
        }
        internal static void Clear()
        {
            foreach (var location in Locations.Values)
            {
                if (location.Map != null) location.Map.Remove(location.MapMarker);
                if (location.Native != null) location.Native.forceRenderingOff = location.ForceOff;
            }
            Api.Compass.RemoveAll(Owner); Locations.Clear(); Dead.Clear();
        }
        internal static void Shutdown()
        {
            TerrainTile.OnTileApplied -= Applied;
            SaveBus.StateChanged -= StateChanged;
            Clear();
        }
    }
}
