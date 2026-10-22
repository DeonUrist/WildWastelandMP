using System;
using System.Collections.Generic;
using System.IO;
using MapMagic.Terrains;
using UnityEngine;

namespace WildWasteland.Core
{
    // Vistas World Gen in a session: guests follow the host's Vistas weather through Vistas' own network API (a.z) and use the host's
    // clock for wind direction/gusts; every 10 s a guest sends a 7x7 terrain height probe around itself and the host compares it with
    // its own ground (both logs report the result). Generation settings travel with the WW world settings (MultiplayerSettings).
    internal static class VistasMultiplayer
    {
        const byte WeatherRequest = 190, WeatherData = 191, GroundProbe = 192, GroundResult = 193;
        const int Grid = 7, Points = Grid * Grid;
        const float Step = 25f, Tolerance = .25f;
        internal static float ClockOffset;
        static bool _installed, _following, _clockSet;
        static float _nextProbe, _applyWarned;
        static MultiplayerSnapshot _weather;
        static readonly Dictionary<int, KeyValuePair<float, bool>> Owners = new Dictionary<int, KeyValuePair<float, bool>>();
        static readonly Dictionary<ulong, int> Mismatches = new Dictionary<ulong, int>();
        static readonly Dictionary<ulong, float> Warned = new Dictionary<ulong, float>();
        static readonly HashSet<ulong> Matched = new HashSet<ulong>();

        internal static void Install()
        {
            if (!VistasWorldGen.Present || _installed) return;
            try
            {
                if (VistasWorldGen.WeatherCapture != null && VistasWorldGen.WeatherApply != null && VistasWorldGen.WeatherFollow != null)
                    _weather = new MultiplayerSnapshot(Multiplayer.PatchOwner, "Vistas weather", WeatherRequest, WeatherData, CaptureWeather, ApplyWeather);
                Multiplayer.Message += Receive; Multiplayer.SessionChanged += Changed; MultiplayerPlayers.Left += Left;
                Scheduler.Interval(Multiplayer.PatchOwner, "Vistas multiplayer", .5f, Tick);
                _installed = true;
                Log.Always("[Vistas] Multiplayer ready: host generation settings, " + (_weather != null ? "host weather and wind clock" : "NO weather sync (API missing)") + ", terrain alignment check");
            }
            catch (Exception e) { Log.Warn("[Vistas] Multiplayer weather/terrain sync unavailable: " + e.Message); }
        }

        // True when another machine simulates this object (native MP ownership); cached 0.5 s per object, main thread only.
        internal static bool Remote(GameObject go)
        {
            if (go == null || !_installed || !Multiplayer.Active) return false;
            int id = go.GetInstanceID(); float now = Time.unscaledTime; KeyValuePair<float, bool> known;
            if (Owners.TryGetValue(id, out known) && now < known.Key) return known.Value;
            if (Owners.Count > 2048) Owners.Clear();
            bool remote = Multiplayer.OwnedElsewhere(go);
            Owners[id] = new KeyValuePair<float, bool>(now + .5f, remote); return remote;
        }

        static void Changed()
        {
            Owners.Clear(); Mismatches.Clear(); Warned.Clear(); Matched.Clear(); _nextProbe = 0;
            if (Multiplayer.Client) return;
            ClockOffset = 0; _clockSet = false;
            if (_following) { _following = false; Follow(false); }
        }
        static void Left(ulong peer) { Mismatches.Remove(peer); Warned.Remove(peer); Matched.Remove(peer); }
        static void Follow(bool on)
        {
            try { VistasWorldGen.WeatherFollow(on); }
            catch (Exception e) { Log.Warn("[Vistas] Weather follow switch failed: " + e.Message); }
        }

        static void Tick()
        {
            if (!Multiplayer.Client) return;
            // Vistas clears its follow flag whenever it resets the world; keep asserting it (no-op and silent when unchanged).
            if (_weather != null) { Follow(true); _following = true; }
            if (Multiplayer.WorldReady) Probe();
        }

        static byte[] CaptureWeather()
        {
            if (!Multiplayer.Hosting || VistasWorldGen.WeatherRunning == null || !VistasWorldGen.WeatherRunning()) return null;
            var state = VistasWorldGen.WeatherCapture(); if (state == null || state.Length == 0 || state.Length > 4096) return null;
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            { w.Write(Time.unscaledTime); w.Write(state.Length); w.Write(state); w.Flush(); return stream.ToArray(); }
        }
        static void ApplyWeather(byte[] data)
        {
            if (!Multiplayer.Client || data == null) return;
            float hostTime; byte[] state;
            using (var r = new BinaryReader(new MemoryStream(data, false)))
            {
                hostTime = r.ReadSingle(); int length = r.ReadInt32();
                if (length < 1 || length > 4096 || length != r.BaseStream.Length - r.BaseStream.Position) return;
                state = r.ReadBytes(length);
            }
            float offset = hostTime - Time.unscaledTime;
            if (!float.IsNaN(offset) && !float.IsInfinity(offset))
            {
                if (!_clockSet || Math.Abs(offset - ClockOffset) > 1f) { ClockOffset = offset; _clockSet = true; }
                else ClockOffset += (offset - ClockOffset) * .2f;
            }
            Follow(true); _following = true;
            try { VistasWorldGen.WeatherApply(state); }
            catch (Exception e)
            { if (Time.unscaledTime >= _applyWarned) { _applyWarned = Time.unscaledTime + 60f; Log.Warn("[Vistas] Host weather snapshot rejected: " + e.Message); } }
        }

        static void Probe()
        {
            float now = Time.unscaledTime; if (now < _nextProbe) return; _nextProbe = now + 10f;
            var player = GameRefs.Player; if (player == null) return;
            var at = Origin.World(player.transform.position);
            float cx = Mathf.Round(at.x / Step) * Step, cz = Mathf.Round(at.z / Step) * Step;
            var heights = new float[Points]; ulong mask = Sample(cx, cz, heights);
            if (mask == 0) return;
            Multiplayer.Send(GroundProbe, w =>
            {
                w.Write(cx); w.Write(cz); w.Write(mask);
                for (int i = 0; i < Points; i++) if ((mask & (1UL << i)) != 0) w.Write(heights[i]);
            }, null, false);
        }
        // Heights in the shared MapMagic frame, only from full-detail (main) tiles; a draft tile's coarser ground is not compared.
        static ulong Sample(float cx, float cz, float[] heights)
        {
            var terrains = Terrain.activeTerrains; var anchor = Origin.Anchor; ulong mask = 0; int half = Grid / 2;
            for (int i = 0; i < Points; i++)
            {
                var scene = new Vector3(cx + (i % Grid - half) * Step, 0f, cz + (i / Grid - half) * Step) + anchor;
                for (int t = 0; t < terrains.Length; t++)
                {
                    var terrain = terrains[t]; if (terrain == null || terrain.terrainData == null) continue;
                    var corner = terrain.GetPosition(); var size = terrain.terrainData.size;
                    if (scene.x < corner.x || scene.z < corner.z || scene.x > corner.x + size.x || scene.z > corner.z + size.z) continue;
                    var tile = terrain.GetComponentInParent<TerrainTile>();
                    if (tile == null || tile.main == null || tile.main.terrain != terrain) continue;
                    heights[i] = corner.y + terrain.SampleHeight(scene) - anchor.y; mask |= 1UL << i; break;
                }
            }
            return mask;
        }

        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == GroundProbe && Multiplayer.Hosting && from != 0) { Compare(from, reader); return; }
            if (kind != GroundResult || !Multiplayer.Client || from != 0) return;
            byte status = reader.ReadByte(); float cx = reader.ReadSingle(), cz = reader.ReadSingle(); int compared = reader.ReadUInt16(); float worst = reader.ReadSingle();
            if (reader.BaseStream.Position != reader.BaseStream.Length) return;
            Report(0, "host", status, cx, cz, compared, worst);
        }
        static void Compare(ulong from, BinaryReader reader)
        {
            if (!Multiplayer.WorldReady) return;
            float cx = reader.ReadSingle(), cz = reader.ReadSingle(); ulong mask = reader.ReadUInt64();
            if ((mask >> Points) != 0 || float.IsNaN(cx) || float.IsNaN(cz) || Math.Abs(cx) > 1e7f || Math.Abs(cz) > 1e7f) return;
            var theirs = new float[Points];
            for (int i = 0; i < Points; i++) if ((mask & (1UL << i)) != 0) theirs[i] = reader.ReadSingle();
            if (reader.BaseStream.Position != reader.BaseStream.Length) return;
            var ours = new float[Points]; ulong both = Sample(cx, cz, ours) & mask;
            int compared = 0; float worst = 0f;
            for (int i = 0; i < Points; i++)
                if ((both & (1UL << i)) != 0) { compared++; float dy = Math.Abs(ours[i] - theirs[i]); if (dy > worst || float.IsNaN(dy)) worst = float.IsNaN(dy) ? float.MaxValue : dy; }
            // 0 match, 1 differs (seen twice in a row: a tile may still be generating), 2 not comparable here, 3 differs once (pending)
            byte status = compared < 9 ? (byte)2 : worst > Tolerance ? (byte)1 : (byte)0;
            int count; Mismatches.TryGetValue(from, out count);
            if (status == 1) { Mismatches[from] = ++count; if (count < 2) status = 3; }
            else if (status == 0) Mismatches[from] = 0;
            Report(from, "player " + from, status, cx, cz, compared, worst);
            Multiplayer.Send(GroundResult, w => { w.Write(status); w.Write(cx); w.Write(cz); w.Write((ushort)compared); w.Write(worst); }, from, false);
        }
        static void Report(ulong peer, string other, byte status, float cx, float cz, int compared, float worst)
        {
            string where = " near (" + cx.ToString("0") + ", " + cz.ToString("0") + "): " + compared + " points, largest difference " + worst.ToString("0.00") + " m";
            if (status == 0)
            {
                if (Matched.Add(peer)) Log.Always("[Vistas] Terrain matches " + other + where);
                else Log.Info("[Vistas] Terrain matches " + other + where);
                Warned.Remove(peer);
            }
            else if (status == 1)
            {
                float next; if (Warned.TryGetValue(peer, out next) && Time.unscaledTime < next) return;
                Warned[peer] = Time.unscaledTime + 60f;
                Log.Warn("[Vistas] Terrain DIFFERS from " + other + where + ". Check that both run the same Vistas build and no other mod reshapes the ground.");
            }
            else Log.Info("[Vistas] Terrain check with " + other + (status == 2 ? " skipped (ground not loaded on both)" : " pending") + where);
        }
    }
}
