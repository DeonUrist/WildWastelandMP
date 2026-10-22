using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;
using WildWasteland.Core;

namespace WildWasteland.Modules.CleanHUD
{
    // Fog of war over the native debug map. Exploration grid and save format follow Immersive Map 1.12.0
    // (25 m cells in 64x64 chunks, gzip "IMFP" v4), so its per-slot files import directly. Unlike the source, the overlay is a
    // world-anchored Alpha8 window updated texel by texel: painting never rebuilds the whole texture, and an open map only
    // uploads it while something changed.
    internal static partial class MapFog
    {
        private const float Cell = 25f;
        private const int Window = 1024, Magic = 1229799760, FormatVersion = 4, ExploredFog = 55;
        private const string SourceGuid = "local.immersivemap";
        private sealed class Chunk { internal readonly byte[] D = New(); internal int Pending, Next; }   // fog: 255 unexplored, 0 explored
        private static readonly Dictionary<long, Chunk> Chunks = new Dictionary<long, Chunk>();
        private static readonly byte[] Full = New();
        private static byte[] _buffer;
        private static Texture2D _texture;
        private static Material _material;
        private static RawImage _overlay;
        private static RectTransform _map;
        private static int _winX, _winZ;
        private static bool _window, _uploadPending, _loaded, _standDown, _wasOpen;
        private static int _seed = int.MinValue;
        private static float _lastPaint, _holdUntil, _nextUpload;
        private static Vector3 _last;
        private static bool _havePosition, _idle;
        private static float _idleRadius, _idleInner;
        private static Vector3 _idleForward;
        private static float _idleCone, _idleRange;
        private static bool _idleBinoculars;
        private static string _saves;
        private static bool _painting, _paintBinoculars;
        private static Vector3 _paintPosition, _paintForward;
        private static float _paintRadius, _paintInner, _paintCone, _paintRange;
        private static int _paintStep, _paintReach, _paintX, _paintZ, _paintRow;
        internal static float RevealRadius { get { return Mathf.Min(4000f, Mathf.Max(Cell, CleanHudModule.FogRadius.Value) * 2f * VantageDiscovery.Scale); } }

        private static byte[] New() { var d = new byte[4096]; for (int i = 0; i < d.Length; i++) d[i] = 255; return d; }
        private static long Key(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }

        internal static void Init()
        {
            Reset();
            PluginInfo info;
            _standDown = Chainloader.PluginInfos.TryGetValue(SourceGuid, out info) && !ReferenceEquals(info.Instance, null);
            if (_standDown) CleanHudModule.Warn("Immersive Map: " + ReplacementGuard.Status(SourceGuid) + ". Wild Wasteland fog waits until restart.");
            _saves = Path.Combine(Core.Assets.ModuleConfigDir("CleanHUD"), "Saves");
            SaveBus.StateChanged -= SaveState; SaveBus.StateChanged += SaveState;
            Multiplayer.Message -= FogMessage; Multiplayer.Message += FogMessage;
            CleanHudModule.SharedVision.SettingChanged -= VisionChanged; CleanHudModule.SharedVision.SettingChanged += VisionChanged;
        }

        internal static void Reset()
        {
            ResetSharing();
            Chunks.Clear(); _loaded = false; _seed = int.MinValue; _window = false; _havePosition = false; _wasOpen = false; _idle = false; _painting = false;
            DropOverlay();
        }

        // Scene objects die with the scene; exploration stays until the next load replaces it.
        internal static void SceneLoaded() { DropOverlay(); _wasOpen = false; _painting = false; _idle = false; }

        internal static void Shutdown()
        {
            SaveBus.StateChanged -= SaveState;
            Multiplayer.Message -= FogMessage;
            CleanHudModule.SharedVision.SettingChanged -= VisionChanged;
            Reset();
            if (_texture != null) UnityEngine.Object.Destroy(_texture); _texture = null; _buffer = null;
            if (_material != null) UnityEngine.Object.Destroy(_material); _material = null;
        }

        private static void DropOverlay()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay.gameObject);
            _overlay = null; _map = null;
        }

        internal static void OriginShifted() { _holdUntil = Time.unscaledTime + .5f; _painting = false; _idle = false; }

        // Map markers (CleanHUD location symbols) in unexplored cells stay hidden while the fog is shown.
        internal static bool Hides(Vector3 scene)
        {
            if (!Shown) return false;
            var w = Origin.World(scene);
            int cx = Mathf.FloorToInt(w.x / Cell), cz = Mathf.FloorToInt(w.z / Cell);
            return FogAt(Key(cx >> 6, cz >> 6), (cz & 63) * 64 + (cx & 63)) > ExploredFog;
        }
        private static bool Shown { get { return _loaded && !_standDown && CleanHudModule.FogOfWar.Value && CleanHudModule.MapEnabled.Value; } }
        internal static bool Explored(Vector3 scene)
        {
            if (!_loaded || _standDown) return false;
            var world = Origin.World(scene);
            int cx = Mathf.FloorToInt(world.x / Cell), cz = Mathf.FloorToInt(world.z / Cell);
            return FogAt(Key(cx >> 6, cz >> 6), (cz & 63) * 64 + (cx & 63)) <= ExploredFog;
        }

        // ---- exploration (0.2 s interval)
        internal static void Paint()
        {
            SharingTick();
            if (_painting) return;
            float now = Time.unscaledTime, dt = Mathf.Min(now - _lastPaint, 1f); _lastPaint = now;
            if (!_loaded || _standDown || !GameRefs.InGame || SaveBus.Busy || now < _holdUntil || Origin.AnchorTransform == null) return;
            var player = GameRefs.Player; if (player == null) return;
            var p = Origin.World(player.transform.position);
            bool jump = _havePosition && (p - _last).sqrMagnitude > 90000f;
            bool still = _havePosition && p.x == _last.x && p.y == _last.y && p.z == _last.z;
            _last = p; _havePosition = true;
            if (jump) { _holdUntil = now + .5f; return; }
            float radius = RevealRadius;
            float inner = Mathf.Clamp(radius - CleanHudModule.FogEdge.Value, 0f, radius * .999f);
            var forward = Binoculars.Forward; forward.y = 0f;
            bool binoculars = Binoculars.Viewing && forward.sqrMagnitude > .01f;
            forward = binoculars ? forward.normalized : Vector3.zero;
            float cone = binoculars ? Mathf.Cos(Binoculars.HalfAngle) : 1f;
            float range = radius * (binoculars ? Binoculars.RangeScale : 1f);
            // Standing exactly still after a pass that revealed nothing: the same pass would reveal nothing again.
            if (still && _idle && radius == _idleRadius && inner == _idleInner && binoculars == _idleBinoculars && forward == _idleForward && cone == _idleCone && range == _idleRange) return;
            _idle = true; _idleRadius = radius; _idleInner = inner;
            _idleBinoculars = binoculars; _idleForward = forward; _idleCone = cone; _idleRange = range;
            int step = Mathf.Max(1, (int)(dt * 255f)), reach = Mathf.CeilToInt(range / Cell) + 1;
            int px = Mathf.FloorToInt(p.x / Cell), pz = Mathf.FloorToInt(p.z / Cell);
            _painting = true; _paintPosition = p; _paintForward = forward; _paintBinoculars = binoculars;
            _paintRadius = radius; _paintInner = inner; _paintCone = cone; _paintRange = range;
            _paintStep = step; _paintReach = reach; _paintX = px; _paintZ = pz; _paintRow = -reach;
        }
        internal static bool PaintStep()
        {
            if (!_painting || !GameRefs.InGame || SaveBus.Busy) return false;
            if (!_loaded || _standDown || GameRefs.Player == null || (Origin.World(GameRefs.Player.transform.position) - _paintPosition).sqrMagnitude > 90000f)
            { _painting = false; _idle = false; return false; }
            var p = _paintPosition; var forward = _paintForward; bool binoculars = _paintBinoculars;
            float radius = _paintRadius, inner = _paintInner, range = _paintRange, cone = _paintCone;
            int px = _paintX, pz = _paintZ, step = _paintStep, reach = _paintReach;
            long lastKey = long.MinValue; Chunk chunk = null;
            int dz = _paintRow++;
            {
                int cz = pz + dz; float oz = (cz + .5f) * Cell - p.z;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int cx = px + dx; float ox = (cx + .5f) * Cell - p.x;
                    float d = Mathf.Sqrt(ox * ox + oz * oz), limit = radius, solid = inner;
                    if (binoculars && d > 0f && ox * forward.x + oz * forward.z >= d * cone) { limit = range; solid += range - radius; }
                    if (d >= limit) continue;
                    int reveal = (int)((d <= solid ? 1f : (limit - d) / (limit - solid)) * 255f); if (reveal <= 0) continue;
                    long key = Key(cx >> 6, cz >> 6);
                    if (key != lastKey)
                    {
                        lastKey = key;
                        if (!Chunks.TryGetValue(key, out chunk)) { chunk = new Chunk(); Chunks[key] = chunk; }
                    }
                    int i = (cz & 63) * 64 + (cx & 63), cur = chunk.D[i], target = 255 - reveal;
                    if (cur <= target) continue;
                    int v = Mathf.Max(target, cur - step); chunk.D[i] = (byte)v; _idle = false;
                    if (_networkSharing) QueuePart(PersonalPending, key, chunk, 1 << (i / FogPartSize));
                    if (_window)
                    {
                        int x = cx - _winX, z = cz - _winZ;
                        if (x > 0 && z > 0 && x < Window - 1 && z < Window - 1) { _buffer[z * Window + x] = FogAt(key, i); _uploadPending = true; }
                    }
                }
            }
            _painting = _paintRow <= reach; return _painting;
        }

        // ---- overlay (every frame; cheap while the map is closed)
        internal static void Tick(RectTransform map, Camera camera, bool open)
        {
            open &= Shown && map != null && camera != null;
            if (_overlay != null && _overlay.gameObject.activeSelf != open) _overlay.gameObject.SetActive(open);
            if (!open) { _wasOpen = false; return; }
            if (_overlay == null || _map != map) Build(map);
            var player = GameRefs.Player; if (player == null) return;
            var view = _mapImage != null ? _mapImage.uvRect : new Rect(0, 0, 1, 1);
            Vector3 a, b;
            if (!Ground(camera, new Vector3(view.xMin, view.yMin, 0), player.transform.position.y, out a)
                || !Ground(camera, new Vector3(view.xMax, view.yMax, 0), player.transform.position.y, out b)) return;
            a = Origin.World(a); b = Origin.World(b);
            float x0 = Mathf.Min(a.x, b.x) / Cell, x1 = Mathf.Max(a.x, b.x) / Cell, z0 = Mathf.Min(a.z, b.z) / Cell, z1 = Mathf.Max(a.z, b.z) / Cell;
            if (!_window || x0 < _winX + 2 || z0 < _winZ + 2 || x1 > _winX + Window - 2 || z1 > _winZ + Window - 2)
                Recenter(Mathf.FloorToInt((x0 + x1) * .5f), Mathf.FloorToInt((z0 + z1) * .5f));
            _overlay.uvRect = new Rect((x0 - _winX) / Window, (z0 - _winZ) / Window, (x1 - x0) / Window, (z1 - z0) / Window);
            var color = CleanHudModule.Enabled.Value ? new Color(.09f, .08f, .07f) : new Color(.79f, .71f, .54f);
            color.a = CleanHudModule.FogOpacity.Value;
            if (_overlay.color != color) _overlay.color = color;
            float now = Time.unscaledTime;
            if (_uploadPending && (!_wasOpen || now >= _nextUpload))
            {
                _texture.LoadRawTextureData(_buffer); _texture.Apply(false, false);
                _uploadPending = false; _nextUpload = now + .25f;
            }
            _wasOpen = true;
        }
        private static RawImage _mapImage;

        private static bool Ground(Camera camera, Vector3 viewport, float height, out Vector3 point)
        {
            var ray = camera.ViewportPointToRay(viewport); point = Vector3.zero;
            float distance; if (!new Plane(Vector3.up, new Vector3(0, height, 0)).Raycast(ray, out distance)) return false;
            point = ray.GetPoint(distance); return true;
        }

        private static void Build(RectTransform map)
        {
            DropOverlay();
            if (_texture == null)
            {
                _texture = new Texture2D(Window, Window, TextureFormat.Alpha8, false) { name = "CleanHUD.Fog", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _texture.hideFlags = HideFlags.HideAndDontSave; _buffer = new byte[Window * Window]; _window = false;
            }
            if (_material == null)
            {
                // Alpha8 samples as black; the UI shader's sample offset turns it into white so the RawImage colour is the fog colour.
                _material = new Material(Graphic.defaultGraphicMaterial) { name = "CleanHUD.Fog", hideFlags = HideFlags.HideAndDontSave };
                _material.SetVector("_TextureSampleAdd", new Vector4(1f, 1f, 1f, 0f));
            }
            var r = DialogueWidgets.Rect(map, "CleanHUD.Fog", 0, 0, 0, 0);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; r.SetSiblingIndex(0);
            _overlay = r.gameObject.AddComponent<RawImage>(); _overlay.raycastTarget = false; _overlay.material = _material; _overlay.texture = _texture;
            _map = map; _mapImage = map.GetComponent<RawImage>(); _uploadPending = true;
        }

        // Window origin on chunk boundaries so each chunk row is one block copy; the outer texel ring stays fogged (clamped edges).
        private static void Recenter(int cx, int cz)
        {
            _winX = ((cx - Window / 2) >> 6) << 6; _winZ = ((cz - Window / 2) >> 6) << 6; _window = true;
            int kx0 = _winX >> 6, kz0 = _winZ >> 6, n = Window / 64;
            for (int kz = 0; kz < n; kz++) for (int kx = 0; kx < n; kx++)
            {
                long key = Key(kx0 + kx, kz0 + kz);
                Chunk c; var src = Chunks.TryGetValue(key, out c) ? c.D : Full;
                Chunk shared = null; bool sharing = _networkSharing && SharedChunks.TryGetValue(key, out shared);
                int dst = kz * 64 * Window + kx * 64;
                for (int row = 0; row < 64; row++) Buffer.BlockCopy(src, row * 64, _buffer, dst + row * Window, 64);
                if (sharing) for (int row = 0; row < 64; row++) for (int x = 0; x < 64; x++)
                { int i = dst + row * Window + x; _buffer[i] = Math.Min(_buffer[i], shared.D[row * 64 + x]); }
            }
            for (int i = 0; i < Window; i++) { _buffer[i] = 255; _buffer[(Window - 1) * Window + i] = 255; _buffer[i * Window] = 255; _buffer[i * Window + Window - 1] = 255; }
            _uploadPending = true; _wasOpen = false;
            if (Core.Log.Verbose) Core.Log.Info("[CleanHUD] Fog window at cell " + _winX + "," + _winZ + " (" + Chunks.Count + " chunks)");
        }

        // ---- saves: config/WildWasteland/CleanHUD/Saves/<slot>.fog.bin, Immersive Map slot files imported once when absent
        private static int Seed()
        {
            var fsm = GameRefs.SaveLoadGame; if (fsm == null) return int.MinValue;
            var v = fsm.FsmVariables.FindFsmInt("seed"); return v != null ? v.Value : int.MinValue;
        }

        internal static void WorldReady(string slot)
        {
            ResetSharing();
            Chunks.Clear(); _window = false; _havePosition = false; _idle = false; _painting = false; _loaded = true; _seed = Seed();
            if (_standDown) return;
            string from = "nothing saved for this world yet";
            try
            {
                if (_seed == int.MinValue) from = "unknown world seed";
                else if (!string.IsNullOrEmpty(slot))
                {
                    string own = Path.Combine(_saves, Path.GetFileName(slot) + ".fog.bin");
                    string source = Path.Combine(Path.Combine(Path.Combine(Paths.ConfigPath, "ImmersiveMap"), "slots"), Path.GetFileNameWithoutExtension(slot) + "_seed" + _seed + ".bin");
                    if (File.Exists(own) && Read(File.ReadAllBytes(own))) from = own;
                    else if (File.Exists(source) && Read(File.ReadAllBytes(source))) from = "Immersive Map " + source;
                }
            }
            catch (Exception e) { Chunks.Clear(); CleanHudModule.Warn("Fog of war could not be read: " + e.Message); }
            LoadShared(slot);
            CleanHudModule.Log("Fog of war loaded from " + from + " (" + Chunks.Count + " chunks, seed " + _seed + ")");
        }

        internal static void SaveEnded(string slot)
        {
            if (!_loaded || _standDown || string.IsNullOrEmpty(slot)) return;
            int seed = Seed();
            if (seed == int.MinValue || seed != _seed) { CleanHudModule.Warn("Fog of war not saved: world seed " + seed + " differs from " + _seed); return; }
            // Plain sidecar, not ES3: written at once. Save-and-quit leaves for the title during the save, before isPlay/AfterSave.
            try
            {
                var data = Write(seed);
                Directory.CreateDirectory(_saves);
                string path = Path.Combine(_saves, Path.GetFileName(slot) + ".fog.bin");
                File.WriteAllBytes(path + ".tmp", data);
                AtomicFile.Replace(path + ".tmp", path);
                CleanHudModule.Log("Fog of war saved: " + Path.GetFileName(path) + " (" + Chunks.Count + " chunks, " + data.Length + " bytes)");
                SaveShared(slot);
            }
            catch (Exception e) { Core.SaveBus.ReportFailure("Fog of war save failed: " + e.Message); CleanHudModule.Warn("Fog of war could not be saved: " + e.Message); }
        }

        // SaveGame (global event) -> wait -> SaveGame -> isPlay. Write when the save starts and again at SaveAll.
        private static void SaveState(string previous, string now)
        {
            if (now == "wait" || now == "SaveGame") SaveEnded(SaveBus.Slot);
        }

        private static byte[] Write(int seed, Dictionary<long, Chunk> chunks = null)
        {
            if (chunks == null) chunks = Chunks;
            var reveal = new byte[4096];
            using (var memory = new MemoryStream())
            {
                using (var gz = new GZipStream(memory, CompressionMode.Compress, true))
                using (var w = new BinaryWriter(gz))
                {
                    w.Write(Magic); w.Write(FormatVersion); w.Write(seed); w.Write(chunks.Count);
                    foreach (var pair in chunks)
                    {
                        w.Write((int)(pair.Key >> 32)); w.Write((int)(pair.Key & 0xFFFFFFFFu));
                        var d = pair.Value.D; for (int i = 0; i < 4096; i++) reveal[i] = (byte)(255 - d[i]);
                        w.Write(reveal);
                    }
                    w.Write(0); w.Write(0);   // base and POI marker lists of the source format
                }
                return memory.ToArray();
            }
        }

        private static bool Read(byte[] data, Dictionary<long, Chunk> chunks = null)
        {
            if (chunks == null) chunks = Chunks;
            var read = new Dictionary<long, Chunk>();
            using (var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var r = new BinaryReader(gz))
            {
                if (r.ReadInt32() != Magic) return false;
                int version = r.ReadInt32(); if (version < 1 || version > FormatVersion) return false;
                if (r.ReadInt32() != _seed) return false;
                int count = r.ReadInt32();
                for (int n = 0; n < count; n++)
                {
                    int cx = r.ReadInt32(), cz = r.ReadInt32(); var bytes = r.ReadBytes(4096); if (bytes.Length != 4096) return false;
                    var c = new Chunk(); for (int i = 0; i < 4096; i++) c.D[i] = (byte)(255 - bytes[i]);
                    read[Key(cx, cz)] = c;
                }
            }
            chunks.Clear(); foreach (var pair in read) chunks[pair.Key] = pair.Value;
            return true;
        }
    }
}
