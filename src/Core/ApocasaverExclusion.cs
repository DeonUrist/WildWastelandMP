using System;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Apocasaver loads first through the soft dependency. Stand it down before its first runner update.
    internal static class ApocasaverExclusion
    {
        internal static void Install()
        {
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue("com.denis.apocalypter.apocasaver", out info) || info.Instance == null) return;
            var plugin = info.Instance.GetType(); var runner = plugin.Assembly.GetType("Apocasaver.Runner");
            if (runner == null) throw new InvalidOperationException("Cannot safely disable this Apocasaver version");
            foreach (var type in new[] { plugin, runner }) foreach (string name in type == plugin ? new[] { "Awake", "ApplyPatches", "EnsureRunner", "OnSceneLoaded" } : new[] { "Update", "LateUpdate", "OnGUI", "OnDestroy" })
            {
                var method = AccessTools.Method(type, name);
                if (method == null || !Patches.Patch(null, method, prefix: AccessTools.Method(typeof(ApocasaverExclusion), "StandDown"))) throw new InvalidOperationException("Cannot disable Apocasaver entry point: " + name);
            }
            Patches.RemoveForeignOwner("com.denis.apocalypter.apocasaver");
            Patches.RemoveForeignOwner("com.denis.apocalypter.apocasaver.history");
            var poses = plugin.Assembly.GetType("Apocasaver.HandPose");
            if (poses != null && (!Patches.Patch(null, AccessTools.Method(poses, "Set", new[] { typeof(string), typeof(string) }), prefix: AccessTools.Method(typeof(HostSaveCompatibility), "SetPose"))
                || !Patches.Patch(null, AccessTools.Method(poses, "Get", new[] { typeof(string) }), prefix: AccessTools.Method(typeof(HostSaveCompatibility), "GetPose")))) throw new InvalidOperationException("Apocasaver metadata API takeover failed");
            info.Instance.enabled = false;
            var field = AccessTools.Field(plugin, "_runnerGo"); var go = field != null ? field.GetValue(null) as GameObject : null;
            if (go != null) foreach (var behaviour in go.GetComponents<Behaviour>()) behaviour.enabled = false;
            Log.Always("[System] Standalone Apocasaver suppressed: runner, scene callbacks and save/history patches disabled; files/settings preserved");
        }
        static bool StandDown() { return false; }
    }
}
