using System.Collections.Generic;
using ES3Internal;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Modules.Patrols.WarRig
{
    // Native SandStorm prefab identity, verified against the installed game's assets.
    // Player-only filtering: the storm still affects the surrounding world normally.
    internal static class NativeStormProtection
    {
        internal const long StormPrefabId = 5697600806714206537L;
        private static GameObject player;
        private static int lastFrame = -1;
        private static readonly HashSet<ParticleSystem> dust = new HashSet<ParticleSystem>();
        private static readonly Dictionary<ParticleSystem, bool> emission = new Dictionary<ParticleSystem, bool>();

        private static GameObject Player
        {
            get { if (player == null || !player.activeInHierarchy) player = Core.GameRefs.Player; return player; }
        }
        internal static bool Protected(GameObject target)
        {
            if (Core.Multiplayer.IsRemotePlayer(target)) return DustStormBridge.Inside(Core.Multiplayer.PlayerFor(Core.Multiplayer.PeerFor(target)));
            var actor = Player;
            return actor != null && target != null && (target == actor || target.transform.IsChildOf(actor.transform)) && DustStormBridge.Inside(actor);
        }
        internal static Transform StormRoot(Transform source)
        {
            for (var node = source; node != null; node = node.parent)
            {
                var prefab = node.GetComponent<ES3Prefab>();
                if (prefab != null && prefab.prefabId == StormPrefabId) return node;
            }
            return null;
        }
        // ---- WW wiring. SendEvent / SetFsmFloat go through the shared Core.Hooks dispatchers; the Assembly-CSharp targets are module patches;
        // PlayMakerFSM.OnEnable goes through Core.Hooks.FsmEnable. First line of every hot handler: no War Rig loaded -> nothing can be sheltered.
        internal static void Install(Core.IModule owner)
        {
            Core.Hooks.FsmEnable.Postfix(owner, TrackDust.Postfix);   // shared dispatcher (Performance also needs PlayMakerFSM.OnEnable)
            Core.Patches.Patch(owner, AccessTools.Method(typeof(Explosion), "ShouldApplyForce"), postfix: AccessTools.Method(typeof(RadialForce), "Postfix"));
            Core.Patches.Patch(owner, AccessTools.Method(typeof(Tornado), "OnTriggerStay"), prefix: AccessTools.Method(typeof(Suction), "Prefix"));
            Core.Hooks.SetFsmFloatDo.Prefix(owner, DamageValue.Prefix);
            Core.Hooks.SendEventEnter.Prefix(owner, DamageEventEnter.Prefix);
            Core.Hooks.SendEventUpdate.Prefix(owner, DamageEventUpdate.Prefix);
        }
        internal static void ResetForScene() { player = null; lastFrame = -1; dust.Clear(); emission.Clear(); stormTornado.Clear(); }
        private static readonly Dictionary<int, bool> stormTornado = new Dictionary<int, bool>();
        private static bool NoRigs { get { return WarRigUnit.All.Count == 0; } }

        private static bool StormAction(HutongGames.PlayMaker.FsmStateAction action)
        { return action != null && action.Owner != null && StormRoot(action.Owner.transform) != null; }

        internal static void Tick()
        {
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            dust.RemoveWhere(x => x == null);
            bool inside = DustStormBridge.Inside(Player);
            if (!inside) { Restore(); return; }
            foreach (var ps in dust)
            {
                if (!ps.gameObject.activeInHierarchy) continue;
                var module = ps.emission;
                if (!emission.ContainsKey(ps)) emission[ps] = module.enabled;
                module.enabled = false; ps.Clear();
            }
        }
        internal static void Restore()
        {
            foreach (var pair in emission)
                if (pair.Key != null)
                {
                    bool enabled = pair.Value;
                    var root = StormRoot(pair.Key.transform);
                    if (root != null)
                        foreach (var fsm in root.GetComponents<PlayMakerFSM>())
                            if (fsm.FsmName == "SandPlayer")
                            {
                                if (fsm.ActiveStateName == "PlayerOn") enabled = true;
                                else if (fsm.ActiveStateName == "PlayerOff") enabled = false;
                            }
                    var module = pair.Key.emission; module.enabled = enabled;
                }
            emission.Clear();
        }

        private static class TrackDust
        {
            internal static void Postfix(PlayMakerFSM __instance)
            {
                if (__instance.FsmName != "SandPlayer") return;
                var root = StormRoot(__instance.transform);
                var node = root != null ? root.Find("Sand Player") : null;
                if (node == null) return;
                foreach (var ps in node.GetComponentsInChildren<ParticleSystem>(true)) dust.Add(ps);
            }
        }

        private static class RadialForce
        {
            internal static void Postfix(Explosion __instance, GameObject __0, ref bool __result)
            { if (__result && !NoRigs && StormAction(__instance) && Protected(__0)) __result = false; }
        }
        private static class Suction
        {
            internal static bool Prefix(Tornado __instance, Collider __0)
            {
                if (NoRigs || __0 == null) return true;
                // Perf: the parent walk once per tornado (was per collider per physics step); a tornado never leaves its storm prefab
                bool storm; int id = __instance.GetInstanceID();
                if (!stormTornado.TryGetValue(id, out storm)) { if (stormTornado.Count > 256) stormTornado.Clear(); stormTornado[id] = storm = StormRoot(__instance.transform) != null; }
                return !storm || !Protected(__0.gameObject);
            }
        }
        private static class DamageValue
        {
            internal static bool Prefix(SetFsmFloat __instance)
            {
                if (NoRigs) return true;
                if (__instance.Fsm.Name != "TornadoDamage" || __instance.variableName.Value != "Damage" || !StormAction(__instance)) return true;
                return !Protected(__instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject));
            }
        }
        private static bool BlockDamage(SendEvent action)
        {
            if (action.Fsm.Name != "TornadoDamage" || action.sendEvent == null || action.sendEvent.Name != "Damage" ||
                !StormAction(action) || action.eventTarget == null) return false;
            return Protected(action.Fsm.GetOwnerDefaultTarget(action.eventTarget.gameObject));
        }
        private static class DamageEventEnter
        {
            internal static bool Prefix(SendEvent __instance)
            { if (NoRigs || !BlockDamage(__instance)) return true; __instance.Finish(); return false; }
        }
        private static class DamageEventUpdate
        { internal static bool Prefix(SendEvent __instance) { return NoRigs || !BlockDamage(__instance); } }
    }
}

