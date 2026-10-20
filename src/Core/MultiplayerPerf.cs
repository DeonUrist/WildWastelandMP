using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Per-frame costs of the native object loops that scale with the tracked-object count (systems review, section C):
    //  - guest ClientUpdate: FollowTarget wrote every copy's transform every frame even when the glide had settled;
    //    KeepKinematic did GetComponent<Rigidbody> per frame for every object without a body;
    //    PassengerSideLookedAt scanned every tracked object (plus GameObject.Find) every frame on foot;
    //  - host HasChanged (10 Hz, every owned object): GameObject.tag allocates a managed string per object per tick,
    //    IsVisible does GetComponent<Renderer> ?? GetComponentInChildren per object per tick.
    // Behaviour is unchanged; only repeated lookups are cached. All caches are keyed by native Tracked/GameObject and
    // cleared on session change.
    internal static class MultiplayerPerf
    {
        static Func<object, Vector3> _position;
        static Func<object, Quaternion> _rotation;
        static Func<object, float> _receivedAt, _groundLift;
        static Func<object, Rigidbody> _body;
        static readonly Dictionary<object, float> Bodiless = new Dictionary<object, float>();
        sealed class RendererCache { internal Renderer Renderer; internal float Until; }
        static readonly Dictionary<GameObject, RendererCache> Renderers = new Dictionary<GameObject, RendererCache>();
        static readonly Dictionary<GameObject, string> Tags = new Dictionary<GameObject, string>();
        static object _looked; static float _lookedAt = -1f;
        // Native per-object caches that are never pruned on despawn (ids) or destruction (Unity keys).
        static Func<object, IDictionary> _objects;
        static readonly List<Func<object, IDictionary>> ById = new List<Func<object, IDictionary>>(), ByObject = new List<Func<object, IDictionary>>();
        static Func<object, IDictionary> _fsmCache;
        static Func<object, GameObject> _tupleCar;
        static Action<object, float> _presenceForget; static Func<object, float> _presenceForgetAt;
        static readonly List<object> _keys = new List<object>();
        internal static void Install()
        {
            var world = Multiplayer.Type("Net.WorldObjects"); var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
            _position = MultiplayerAccess.Get<Vector3>(tracked, "Position"); _rotation = MultiplayerAccess.Get<Quaternion>(tracked, "Rotation");
            _receivedAt = MultiplayerAccess.Get<float>(tracked, "ReceivedAt"); _groundLift = MultiplayerAccess.Get<float>(tracked, "GroundLift");
            _body = MultiplayerAccess.Get<Rigidbody>(tracked, "Body");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "KeepKinematic"), prefix: AccessTools.Method(typeof(MultiplayerPerf), "BeforeKeepKinematic"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "PassengerSideLookedAt"), prefix: AccessTools.Method(typeof(MultiplayerPerf), "BeforeLooked"), postfix: AccessTools.Method(typeof(MultiplayerPerf), "AfterLooked"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "IsVisible"), prefix: AccessTools.Method(typeof(MultiplayerPerf), "BeforeIsVisible"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "HasChanged"), transpiler: AccessTools.Method(typeof(MultiplayerPerf), "CachedTag")))
                throw new InvalidOperationException("MP object loop caches could not be installed");
            Multiplayer.SessionChanged += Reset;
            Scheduler.Interval(Multiplayer.PatchOwner, "MP object cache sweep", 10f, Sweep);
            InstallNativeCaches(world);
        }
        static void InstallNativeCaches(Type world)
        {
            _objects = MultiplayerAccess.Get<IDictionary>(world, "objects");
            // Only Dictionary fields (dashboardsOn is a HashSet); a missing or differently typed field is skipped, never fatal.
            foreach (string name in new[] { "creatureLooks", "passiveCars", "carMotions", "carCopyStates", "carLastSent" }) Collect(world, name, ById);
            foreach (string name in new[] { "carParts", "hornCache", "keyboardAsShipped", "hingeCache", "driveZones" }) Collect(world, name, ByObject);
            Collect(Multiplayer.Type("Game.GroupScaling"), "sizes", ByObject);
            var fsmField = AccessTools.Field(world, "fsmCache");
            if (fsmField != null && typeof(IDictionary).IsAssignableFrom(fsmField.FieldType))
            {
                _fsmCache = MultiplayerAccess.Get<IDictionary>(world, "fsmCache");
                var item1 = AccessTools.Field(fsmField.FieldType.GetGenericArguments()[0], "Item1");
                if (item1 != null && item1.FieldType == typeof(GameObject)) _tupleCar = key => (GameObject)item1.GetValue(key); else _fsmCache = null;
            }
            // PlayerPresence re-issues Physics.IgnoreCollision for every nearby rigidbody every 3 s. Unity drops an ignore pair when
            // either collider is toggled, so the forget must stay; 15 s costs one fifth of the pair setup.
            var presence = Multiplayer.Type("Game.PlayerPresence");
            _presenceForget = MultiplayerAccess.Set<float>(presence, "nextForget"); _presenceForgetAt = MultiplayerAccess.Get<float>(presence, "nextForget");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(presence, "Update"), prefix: AccessTools.Method(typeof(MultiplayerPerf), "BeforePresenceUpdate"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Game.Avatar.LocalActions"), "Tick"), prefix: AccessTools.Method(typeof(MultiplayerPerf), "BeforeLocalActions")))
                throw new InvalidOperationException("MP presence/menu cost hooks failed");
            Scheduler.Interval(Multiplayer.PatchOwner, "MP native cache sweep", 30f, SweepNative);
        }
        static void Collect(Type type, string name, List<Func<object, IDictionary>> into)
        {
            var field = AccessTools.Field(type, name);
            if (field == null || !typeof(IDictionary).IsAssignableFrom(field.FieldType)) return;
            try { into.Add(MultiplayerAccess.Get<IDictionary>(type, name)); } catch (Exception e) { Log.Warn("[Multiplayer] Native cache " + name + " not swept: " + e.Message); }
        }
        static void BeforePresenceUpdate(object __instance)
        {
            // Native sets nextForget = now + 3 after each clear; stretch that one window to 15 s (never past it).
            float at = _presenceForgetAt(__instance), now = Time.unscaledTime;
            if (at > now && at - now <= 3f && !_stretched.Contains(__instance)) { _presenceForget(__instance, now + 15f); _stretched.Add(__instance); }
            else if (at <= now) _stretched.Remove(__instance);
        }
        static readonly HashSet<object> _stretched = new HashSet<object>();
        // LocalActions.Tick runs every frame even without a session and looks the Player up by name twice per frame in the menus.
        static bool BeforeLocalActions() { return GameRefs.Player != null; }
        static void SweepNative()
        {
            if (!Multiplayer.Active) return;
            var objects = _objects(null); if (objects == null) return;
            int removed = 0;
            foreach (var get in ById)
            {
                var map = get(null); if (map == null) continue; _keys.Clear();
                foreach (DictionaryEntry entry in map) if (!objects.Contains(entry.Key)) _keys.Add(entry.Key);
                foreach (var key in _keys) map.Remove(key); removed += _keys.Count;
            }
            foreach (var get in ByObject)
            {
                var map = get(null); if (map == null) continue; _keys.Clear();
                foreach (DictionaryEntry entry in map) { var key = entry.Key as UnityEngine.Object; if (key == null) _keys.Add(entry.Key); }
                foreach (var key in _keys) map.Remove(key); removed += _keys.Count;
            }
            if (_fsmCache != null)
            {
                var map = _fsmCache(null); _keys.Clear();
                foreach (DictionaryEntry entry in map) if (_tupleCar(entry.Key) == null) _keys.Add(entry.Key);
                foreach (var key in _keys) map.Remove(key); removed += _keys.Count;
            }
            _keys.Clear();
            if (removed > 0 && Log.Verbose) Log.Info("[Multiplayer] Native object caches: " + removed + " stale entries removed");
        }
        static void Reset() { if (!Multiplayer.SessionActive) { Bodiless.Clear(); Renderers.Clear(); Tags.Clear(); _stretched.Clear(); _looked = null; _lookedAt = -1f; } }
        static readonly List<GameObject> _gone = new List<GameObject>();
        static void Sweep()
        {
            _gone.Clear(); foreach (var pair in Renderers) if (pair.Key == null) _gone.Add(pair.Key);
            foreach (var go in _gone) Renderers.Remove(go);
            _gone.Clear(); foreach (var pair in Tags) if (pair.Key == null) _gone.Add(pair.Key);
            foreach (var go in _gone) Tags.Remove(go);
            if (Bodiless.Count > 8192) Bodiless.Clear();
        }

        // Guest: the 0.1 s glide has ended and the copy already sits on its target; skip the transform write.
        // Called from MultiplayerSaving's FollowTarget prefix (one patch per target).
        internal static bool FollowSettled(object tracked)
        {
            if (Time.realtimeSinceStartup - _receivedAt(tracked) <= .1f) return false;
            var root = Multiplayer.TrackedObject(tracked); if (root == null) return false;
            var transform = root.transform; var target = Origin.Scene(_position(tracked)) + Vector3.up * _groundLift(tracked);
            if ((transform.position - target).sqrMagnitude > 1e-6f) return false;
            return Quaternion.Angle(transform.rotation, _rotation(tracked)) < .01f;
        }
        // Guest: objects without a Rigidbody are remembered for 30 s instead of GetComponent every frame.
        static bool BeforeKeepKinematic(object __0)
        {
            if (_body(__0) != null) return true;
            float until; float now = Time.unscaledTime;
            if (Bodiless.TryGetValue(__0, out until) && now < until) return false;
            var root = Multiplayer.TrackedObject(__0);
            if (root != null && root.GetComponent<Rigidbody>() == null) { Bodiless[__0] = now + 30f; return false; }
            Bodiless.Remove(__0); return true;
        }
        // Guest on foot: the passenger-seat look test (GameObject.Find + every tracked car) answers from a 0.1 s cache.
        static bool BeforeLooked(ref object __result)
        {
            if (Time.unscaledTime - _lookedAt >= .1f) return true;
            if (_looked != null) { var car = Multiplayer.TrackedObject(_looked); if (car == null || !car.activeInHierarchy) return true; }   // despawned since: rescan
            __result = _looked; return false;
        }
        static void AfterLooked(object __result, bool __runOriginal)
        { if (__runOriginal) { _looked = __result; _lookedAt = Time.unscaledTime; } }
        // Host: the renderer used by IsVisible is cached per object; "no renderer" is re-checked every 5 s.
        static bool BeforeIsVisible(GameObject __0, ref bool __result)
        {
            // Host only: the 10 Hz HasChanged scan is the cost. Guests call IsVisible on state changes, where a renderer swapped by a
            // model replacement must be seen immediately.
            if (__0 == null || !Multiplayer.Hosting) return true;
            RendererCache cache; float now = Time.unscaledTime;
            if (!Renderers.TryGetValue(__0, out cache)) { cache = new RendererCache(); Renderers[__0] = cache; cache.Until = 0f; }
            if (cache.Renderer == null && now >= cache.Until)
            { cache.Renderer = __0.GetComponent<Renderer>() ?? __0.GetComponentInChildren<Renderer>(); cache.Until = now + 5f; }
            __result = cache.Renderer == null || cache.Renderer.enabled; return false;
        }
        // Host: HasChanged compares t.Tag != t.Object.tag; the tag getter allocates. CompareTag against the last known tag does not.
        static string TagOf(GameObject go)
        {
            string tag;
            if (Tags.TryGetValue(go, out tag) && go.CompareTag(tag)) return tag;
            tag = go.tag; Tags[go] = tag; return tag;
        }
        static IEnumerable<CodeInstruction> CachedTag(IEnumerable<CodeInstruction> instructions)
        {
            var getTag = AccessTools.PropertyGetter(typeof(GameObject), "tag"); int count = 0;
            foreach (var code in instructions)
            {
                if ((code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call) && Equals(code.operand, getTag))
                { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(MultiplayerPerf), "TagOf"); count++; }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected GameObject.tag reads in HasChanged: " + count);
        }
    }
}
