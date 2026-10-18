using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using Den.Tools;
using MapMagic.Core;
using MapMagic.Nodes;
using MapMagic.Nodes.MatrixGenerators;
using MapMagic.Nodes.ObjectsGenerators;
using MapMagic.Products;
using MapMagic.Terrains;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;
using Log = WildWasteland.Core.Log;

namespace WildWasteland.Modules.POI
{
    public sealed class PoiModule : ModuleBase, IPoiApi
    {
        private sealed class Registration
        {
            internal Graph Graph;
            internal readonly List<Generator> Added = new List<Generator>();
            internal readonly Dictionary<IInlet<object>, IOutlet<object>> Original = new Dictionary<IInlet<object>, IOutlet<object>>();
            internal readonly Dictionary<string, PoiLocations> Sites = new Dictionary<string, PoiLocations>();
        }
        private sealed class Capture
        {
            internal int Seed;
            internal KeyValuePair<string, PoiLocations>[] Sources;
            internal readonly Dictionary<string, Transition[]> Points = new Dictionary<string, Transition[]>();
        }
        private readonly ConcurrentDictionary<TileData, Capture> _captures = new ConcurrentDictionary<TileData, Capture>();
        private readonly Dictionary<string, PoiDefinition> _definitions = new Dictionary<string, PoiDefinition>();
        private readonly Dictionary<Graph, Registration> _graphs = new Dictionary<Graph, Registration>();
        private readonly List<PoiSite> _sites = new List<PoiSite>();
        private readonly HashSet<string> _spawned = new HashSet<string>();
        private ConfigEntry<int> _chance;
        private bool _running, _loaded;
        private string _saves, _slot;
        public override string Id { get { return "POI"; } }
        public override string DisplayName { get { return "Points of interest"; } }
        public override void BindConfig(ModuleConfig cfg)
        {
            _chance = cfg.Player("World", "Spawn chance (%)", 2,
                "Chance for each added POI in each generated 5 km terrain slice. 100 guarantees one site per slice for debugging. Changes affect newly generated slices.", new AcceptableValueRange<int>(0, 100));
        }
        public override void Init()
        {
            _saves = Path.Combine(Assets.ModuleConfigDir(Id), "Saves");
            Directory.CreateDirectory(_saves); _running = true;
            Services.Poi = this;
            TerrainTile.OnBeforeTilePrepare += Prepare;
            TerrainTile.OnTileFinalized += Finalized;
            TerrainTile.OnTileApplied += Applied;
            TerrainTile.OnTileMoved += Moved;
            Scheduler.Interval(this, "Site proximity", 1f, Proximity, Phase.Update, true);
        }
        // Replaces the old 500 m trigger sphere per site (a huge Default-layer trigger every rigidbody and query touched):
        // a site is tried when the player comes within 500 m, as OnTriggerEnter did. Game time, like physics; skipped while not in play.
        private void Proximity()
        {
            if (!_running || _sites.Count == 0 || !GameRefs.WorldRunning || SaveBus.Busy) return;
            var player = GameRefs.Player; if (player == null) return;
            var p = player.transform.position;
            for (int i = 0; i < _sites.Count; i++)
            {
                var site = _sites[i]; if (site == null) continue;
                bool inside = site.gameObject.activeInHierarchy && Multiplayer.PlayerDistance(site.transform.position, player.transform) <= 500f;
                if (inside && !site.Inside) TrySpawn(site);
                site.Inside = inside;
            }
        }
        public void Register(PoiDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id) || definition.Spawn == null) throw new ArgumentException("Invalid POI definition");
            _definitions[definition.Id] = definition;
        }
        public void Remove(string id)
        {
            _definitions.Remove(id);
            foreach (var registration in _graphs.Values)
                if (registration.Sites.ContainsKey(id)) registration.Sites[id].Chance = 0;
            for (int i = _sites.Count - 1; i >= 0; i--)
                if (_sites[i] == null || _sites[i].Definition.Id == id)
                { if (_sites[i] != null) UnityEngine.Object.Destroy(_sites[i].gameObject); _sites.RemoveAt(i); }
        }
        private static bool NativePoi(ObjectsOutput output)
        {
            foreach (var prefab in output.prefabs)
                if (prefab != null && (prefab.name.StartsWith("Camp_", StringComparison.Ordinal) || prefab.name.StartsWith("Cave_", StringComparison.Ordinal) || prefab.name.StartsWith("Wreck_", StringComparison.Ordinal) || prefab.name.StartsWith("Building_", StringComparison.Ordinal))) return true;
            return false;
        }
        private void Prepare(TerrainTile tile, TileData data)
        {
            if (!_running || tile == null || data == null || tile.mapMagic == null || tile.mapMagic.graph == null) return;
            try
            {
                var graph = tile.mapMagic.graph; Registration registration;
                if (!_graphs.TryGetValue(graph, out registration))
                { registration = new Registration { Graph = graph }; _graphs.Add(graph, registration); }
                var outputs = new List<ObjectsOutput>(); var avoid = new List<IOutlet<TransitionsList>>();
                HeightOutput200 height = null;
                foreach (var gen in graph.generators)
                {
                    var objects = gen as ObjectsOutput;
                    if (objects != null)
                    {
                        outputs.Add(objects);
                        IOutlet<object> source;
                        if (NativePoi(objects) && graph.links.TryGetValue(objects, out source)) avoid.Add((IOutlet<TransitionsList>)source);
                    }
                    if (gen is HeightOutput200) height = (HeightOutput200)gen;
                }
                if (height == null) throw new InvalidOperationException("Native height output not found");
                foreach (var definition in _definitions.Values)
                {
                    PoiLocations locations;
                    if (!registration.Sites.TryGetValue(definition.Id, out locations))
                    {
                        var before = graph.links[height];
                        locations = (PoiLocations)Generator.Create(typeof(PoiLocations));
                        locations.Avoid = avoid.ToArray(); locations.Radius = definition.Radius;
                        int seed = 17; foreach (char c in definition.Id) seed = unchecked(seed * 31 + c); locations.Seed = seed & 32767;
                        var pad = (Flatten200)Generator.Create(typeof(Flatten200));
                        pad.radius = definition.Radius; pad.hardness = 1f; pad.noiseFallof = false;
                        graph.Add(locations); graph.Add(pad); registration.Added.Add(locations); registration.Added.Add(pad);
                        graph.Link(before, locations.Heights); graph.Link(before, pad.heightsIn); graph.Link(locations, pad.positionsIn);
                        if (!registration.Original.ContainsKey(height)) registration.Original.Add(height, before);
                        graph.Link(pad, height); registration.Sites.Add(definition.Id, locations);
                        var allSites = new List<PoiLocations>(registration.Sites.Values).ToArray();
                        foreach (var output in outputs)
                        {
                            if (NativePoi(output)) continue;
                            bool solid = false;
                            foreach (var prefab in output.prefabs)
                                if (prefab != null) foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
                                    if (!collider.isTrigger) { solid = true; break; }
                            if (!solid) continue;
                            IOutlet<object> original;
                            if (!graph.links.TryGetValue(output, out original)) continue;
                            var existing = original as PoiClearance;
                            if (existing != null) { existing.Sites = allSites; continue; }
                            var filter = (PoiClearance)Generator.Create(typeof(PoiClearance)); filter.Sites = allSites;
                            graph.Add(filter); registration.Added.Add(filter); registration.Original[output] = original;
                            graph.Link(original, filter); graph.Link(filter, output);
                        }
                        Log.Always("[POI] Registered " + definition.Id + " with native terrain generation.");
                    }
                    locations.Chance = definition.Enabled == null || definition.Enabled() ? _chance.Value : 0;
                }
                if (!data.isDraft) _captures[data] = new Capture { Seed = graph.random.Seed,
                    Sources = new List<KeyValuePair<string, PoiLocations>>(registration.Sites).ToArray() };
            }
            catch (Exception e) { Log.Error("[POI] Registration failed", e); }
        }
        // MapMagic clears products and heights after this worker callback, before main-thread application.
        private void Finalized(TerrainTile tile, TileData data, StopToken stop)
        {
            Capture capture;
            if (data == null || data.isDraft || (stop != null && stop.stop) || !_captures.TryGetValue(data, out capture)) return;
            foreach (var source in capture.Sources)
            {
                var points = data.ReadOutletProduct(source.Value); if (points == null || points.count == 0) continue;
                var snapshot = new Transition[points.count]; Array.Copy(points.arr, snapshot, points.count);
                for (int i = 0; i < snapshot.Length; i++) snapshot[i].pos.y =
                    data.heights.GetWorldInterpolatedValue(snapshot[i].pos.x, snapshot[i].pos.z, roundToShort: true) * data.globals.height;
                capture.Points[source.Key] = snapshot;
            }
        }
        private void Applied(TerrainTile tile, TileData data, StopToken stop)
        {
            if (!_running || data == null || data.isDraft || tile == null || (stop != null && stop.stop)) return;
            Capture capture;
            if (!_captures.TryRemove(data, out capture)) return;
            try
            {
                foreach (var pair in capture.Points)
                {
                    PoiDefinition definition; if (!_definitions.TryGetValue(pair.Key, out definition)) continue;
                    string key = capture.Seed.ToString(CultureInfo.InvariantCulture) + "/" + pair.Key + "/" + tile.coord.x.ToString(CultureInfo.InvariantCulture) + "/" + tile.coord.z.ToString(CultureInfo.InvariantCulture);
                    _sites.RemoveAll(Missing);
                    bool exists = false; foreach (var site in _sites) if (site.Key == key) { exists = true; break; }
                    if (exists) continue;
                    var point = pair.Value[0];
                    var marker = new GameObject("WW.POI." + pair.Key);
                    marker.transform.SetParent(tile.objectsPool.transform, false);
                    marker.transform.position = tile.mapMagic.transform.TransformPoint(point.pos);
                    marker.transform.rotation = point.rotation;
                    var siteComponent = marker.AddComponent<PoiSite>(); siteComponent.Owner = this; siteComponent.Definition = definition; siteComponent.Key = key;
                    _sites.Add(siteComponent); if (definition.Mark != null) definition.Mark(marker);
                    Log.Always("[POI] " + pair.Key + " site generated in slice " + tile.coord + " at " + point.pos);
                    TrySpawn(siteComponent);
                }
            }
            catch (Exception e) { Log.Error("[POI] Applying sites failed", e); }
        }
        private static bool Missing(PoiSite site) { return site == null; }
        private void Moved(TerrainTile tile)
        {
            Capture capture; if (tile.main != null && tile.main.data != null) _captures.TryRemove(tile.main.data, out capture);
            for (int i = _sites.Count - 1; i >= 0; i--)
                if (_sites[i] == null || _sites[i].transform.IsChildOf(tile.transform))
                { if (_sites[i] != null) UnityEngine.Object.Destroy(_sites[i].gameObject); _sites.RemoveAt(i); }
        }
        internal void TrySpawn(PoiSite site)
        {
            if (!_running || !_loaded || site == null || !site.gameObject.activeInHierarchy || site.Building || _spawned.Contains(site.Key) || !GameRefs.WorldRunning || SaveBus.Busy) return;
            if (site.Definition.Enabled != null && !site.Definition.Enabled()) return;
            var player = GameRefs.Player; if (Multiplayer.PlayerDistance(site.transform.position, player != null ? player.transform : null) > 500f) return;
            site.Building = true;
            try { site.Definition.Spawn(site.transform.position, site.transform.rotation, site.Completed); }
            catch (Exception e) { site.Building = false; Log.Error("[POI] Spawn failed: " + site.Definition.Id, e); }
        }
        internal void Completed(PoiSite site, GameObject result)
        {
            if (site == null) return; site.Building = false;
            if (result != null && _running) { _spawned.Add(site.Key); Log.Always("[POI] " + site.Definition.Id + " encounter spawned: " + site.Key); }
            else Log.Warn("[POI] Encounter could not be built: " + site.Key);
        }
        public override void OnWorldReady()
        {
            if (!_loaded || _slot != SaveBus.Slot) Load(SaveBus.Slot);
            _sites.RemoveAll(Missing); foreach (var site in _sites) TrySpawn(site);
        }
        public override void OnSaveBegin(string slot)
        {
            var snapshot = new List<string>(_spawned).ToArray();
            Write(slot, snapshot);
        }
        private void Write(string slot, string[] snapshot)
        {
            if (string.IsNullOrEmpty(slot)) return;
            string path = Path.Combine(_saves, Path.GetFileName(slot) + ".poi.txt");
            File.WriteAllLines(path + ".tmp", snapshot);
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
        }
        private void Load(string slot)
        {
            _spawned.Clear(); _loaded = true; _slot = slot;
            if (string.IsNullOrEmpty(slot)) return;
            string path = Path.Combine(_saves, Path.GetFileName(slot) + ".poi.txt");
            if (File.Exists(path)) foreach (var key in File.ReadAllLines(path)) if (!string.IsNullOrEmpty(key)) _spawned.Add(key);
        }
        public override void OnLoadBegin(string slot) { Load(slot); }
        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode) { _sites.Clear(); _captures.Clear(); _spawned.Clear(); _loaded = false; }
        public override void Shutdown()
        {
            _running = false;
            TerrainTile.OnBeforeTilePrepare -= Prepare; TerrainTile.OnTileApplied -= Applied;
            TerrainTile.OnTileFinalized -= Finalized;
            TerrainTile.OnTileMoved -= Moved;
            foreach (var registration in _graphs.Values)
            {
                foreach (var link in registration.Original) registration.Graph.Link(link.Value, link.Key);
                foreach (var generator in registration.Added) registration.Graph.Remove(generator);
            }
            foreach (var site in _sites) if (site != null) UnityEngine.Object.Destroy(site.gameObject);
            _sites.Clear(); _captures.Clear(); _graphs.Clear(); _definitions.Clear();
            if (ReferenceEquals(Services.Poi, this)) Services.Poi = null;
        }
        public override IEnumerable<ToolPage> ToolPages()
        {
            return new ToolPage[] { new ConfigPage(this, ToolGroups.Vehicles, "Points of interest", Config.Main, "POI / World") { Intro = "Native terrain-generation additions. 100% creates one site per 5 km slice. Encounters spawn within 500 m of the player (checked once per second)." } };
        }
    }

    internal sealed class PoiSite : MonoBehaviour
    {
        internal PoiModule Owner;
        internal PoiDefinition Definition;
        internal string Key;
        internal bool Building, Inside;
        private void OnEnable() { if (Owner != null) Owner.TrySpawn(this); }
        internal void Completed(GameObject result) { if (Owner != null) Owner.Completed(this, result); }
    }
}
