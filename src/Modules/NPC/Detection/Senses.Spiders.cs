using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Modules.NPC
{
    internal static partial class Senses
    {
        private static bool _hasSpiders;
        private static float _playerCaveLight = 1f;
        private static PlayMakerArrayListProxy _spiderItems;
        private static readonly Dictionary<int, Light[]> _canLights = new Dictionary<int, Light[]>();
        private static readonly List<Light> _spiderLights = new List<Light>();
        private static readonly List<int> _deadLights = new List<int>();
        private static Transform _spiderFlashOf; private static Light _spiderFlash;
        private sealed class TargetCaveLight { internal GameObject Target; internal float Next, Level; }
        private static readonly Dictionary<int, TargetCaveLight> _targetCaveLights = new Dictionary<int, TargetCaveLight>();

        private static void ResetSpiderLights()
        {
            _hasSpiders = false; _spiderItems = null; _spiderFlashOf = null; _spiderFlash = null;
            _canLights.Clear(); _spiderLights.Clear(); _deadLights.Clear(); _playerCaveLight = 1f; _spiderItemPos = -1; _notCanLight.Clear();
            CaveDarknessBridge.Reset();
            _targetCaveLights.Clear();
        }

        private static bool OutsideCaveWeb(Agent a, Vector3 noise, bool outside)
        {
            return a.CaveWeb && outside && (noise - a.T.position).sqrMagnitude > 9f;
        }

        private static void RefreshSpiderLights()
        {
            if (!_hasSpiders) return;
            CaveDarknessBridge.Refresh();
            _playerCaveLight = SpiderAmbient(_playerHead != null ? _playerHead.transform.position : _player.transform.position + Vector3.up);
            if (_spiderItems == null)
            {
                var reg = Core.GameRefs.NewGOArrayList;
                if (reg != null) foreach (var p in reg.GetComponents<PlayMakerArrayListProxy>())
                    if (p != null && p.referenceName == "ArrayList_Items") { _spiderItems = p; break; }
            }
            _spiderLights.Clear(); _deadLights.Clear();
            foreach (var kv in _targetCaveLights) if (kv.Value.Target == null) _deadLights.Add(kv.Key);
            foreach (int id in _deadLights) _targetCaveLights.Remove(id);
            _deadLights.Clear();
            foreach (var kv in _canLights)
            {
                bool alive = false;
                foreach (var l in kv.Value) if (l != null) { alive = true; _spiderLights.Add(l); }
                if (!alive) _deadLights.Add(kv.Key);
            }
            foreach (var id in _deadLights) _canLights.Remove(id);
            if (_spiderItemPos < 0) _spiderItemPos = 0;          // (perf) the item walk follows over the next frames (SpiderItemStep); a walk still running goes on (a long list is not restarted every second and never finished)
        }

        // (perf) D5: the item list walked 256 items a frame (was all of it in the maintenance frame), and items that are not tin can lights
        // are remembered by id instead of filling the shared prefab-name cache (it was cleared at 4096 by this walk itself, so every
        // second each item's name was read again). A new can light is found within items/256 frames.
        private static int _spiderItemPos = -1;
        private static readonly HashSet<int> _notCanLight = new HashSet<int>();
        private static void SpiderItemStep()
        {
            var items = _spiderItems != null ? _spiderItems.arrayList : null;
            if (items == null) { _spiderItemPos = -1; return; }
            int end = Mathf.Min(items.Count, _spiderItemPos + 256);
            for (int i = _spiderItemPos; i < end; i++)
            {
                var go = items[i] as GameObject;
                if (go == null) continue;
                int id = go.GetInstanceID();
                if (_notCanLight.Contains(id) || _canLights.ContainsKey(id)) continue;
                if (Prefab(go.name) != "tin_can_light") { if (_notCanLight.Count > 65536) _notCanLight.Clear(); _notCanLight.Add(id); continue; }
                var lights = go.GetComponentsInChildren<Light>(true);
                _canLights[id] = lights;
                foreach (var l in lights) if (l != null) _spiderLights.Add(l);
            }
            _spiderItemPos = end >= items.Count ? -1 : end;
        }

        private static float SpiderAmbient(Vector3 p)
        {
            float level = CaveDarknessBridge.At(p);
            if (level >= 0f) return level;
            RaycastHit hit;
            if (!Physics.Raycast(p, Vector3.up, out hit, 80f, Blockers, QueryTriggerInteraction.Ignore)) return 1f;
            for (var t = hit.collider.transform; t != null; t = t.parent)
                if (t.name.StartsWith("Cave_", System.StringComparison.Ordinal)) return Mathf.Lerp(0.15f, 1f, Webs.SkyOpen(p));
            return 1f;
        }

        private static void SpiderCaveLight(Agent a, float now)
        {
            if (now < a.NextCaveLight) return;
            a.NextCaveLight = now + 0.5f;
            a.CaveLight = SpiderAmbient(Eye(a));
        }

        private static float SpiderTargetLight(GameObject target)
        {
            if (target == _player) return _playerCaveLight;
            if (Core.Multiplayer.IsRemotePlayer(target))
            {
                int id = target.GetInstanceID(); TargetCaveLight light;
                if (!_targetCaveLights.TryGetValue(id, out light) || light.Target != target)
                { light = new TargetCaveLight { Target = target }; _targetCaveLights[id] = light; }
                if (Time.time >= light.Next) { light.Next = Time.time + .5f; light.Level = SpiderAmbient(target.transform.position + Vector3.up); }
                return light.Level;
            }
            var a = Get(target);
            if (a == null) return 1f;
            SpiderCaveLight(a, Time.time);
            return a.CaveLight;
        }

        private static bool SpiderDark(Agent a) { return !a.Lit && LightLevel() * a.CaveLight < 0.5f; }

        private static bool SpiderIlluminated(Agent a, Vector3 eye, Light l)
        {
            if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f || (l.cullingMask & (1 << a.Owner.layer)) == 0) return false;
            if (l.type != LightType.Point && l.type != LightType.Spot) return false;
            Vector3 from = l.transform.position, d = eye - from;
            if (d.sqrMagnitude >= l.range * l.range) return false;
            if (l.type == LightType.Spot && Vector3.Angle(l.transform.forward, d) > l.spotAngle * 0.5f) return false;
            return Clear(from, eye, a.T.root);
        }

        private static void SpiderLook(Agent a, float now)
        {
            SpiderCaveLight(a, now);
            Vector3 eye = Eye(a); Light light = null;
            var fl = Flashlight;
            if (_spiderFlashOf != fl) { _spiderFlashOf = fl; _spiderFlash = fl != null ? fl.GetComponentInChildren<Light>(true) : null; }
            if (SpiderIlluminated(a, eye, _spiderFlash)) light = _spiderFlash;
            else foreach (var l in _spiderLights) if (SpiderIlluminated(a, eye, l)) { light = l; break; }
            a.Lit = light != null;
            var source = light != null ? light.gameObject : null;
            if (source == a.LightSource) return;
            a.LightSource = source;
            if (source == null || a.Blown || a.State == State.Combat || a.State == State.Hide) return;
            a.AmbushDist = 0f;
            float distance; var player = NearestVisiblePlayer(a, eye, 0f, now, out distance);
            if (player != null) { Engage(a, player); return; }
            Vector3 pos = light.transform.position;
            var g = GetOrMake(Src.Vision, OnGround(pos, pos.y), source, "a light", 0f, 1f, now);
            bool had = a.Ghost == g;
            if (Assign(a, g, Src.Vision, now)) { if (!had) if (NpcModule.SensesLog.Value) Log(a, "is illuminated, investigates a light (ghost #" + g.Id + ")"); }
            else if (g.Holders.Count == 0) { int i = _ghosts.IndexOf(g); if (i >= 0) KillGhost(i); }
        }
    }
}
