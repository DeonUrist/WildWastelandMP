using System;
using System.Collections.Generic;
using ES3Internal;
using HarmonyLib;
using HutongGames.PlayMaker;

namespace WildWasteland.Core
{
    // Preserve Apocasaver's stable hinge keys when its standalone save engine is disabled.
    internal static class HostSaveCompatibility
    {
        static readonly Dictionary<string, string> Poses = new Dictionary<string, string>();
        internal static void Install()
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(ES3PlayMaker.Save), "Enter"), prefix: AccessTools.Method(typeof(HostSaveCompatibility), "BeforeSave"), finalizer: AccessTools.Method(typeof(HostSaveCompatibility), "AfterSave"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(ES3PlayMaker.Load), "Enter"), prefix: AccessTools.Method(typeof(HostSaveCompatibility), "BeforeLoad"), finalizer: AccessTools.Method(typeof(HostSaveCompatibility), "AfterLoad"))) throw new InvalidOperationException("Stable hinge compatibility hooks failed");
            SaveBus.StateChanged += StateChanged;
        }
        static void StateChanged(string previous, string current) { if (current == "LoadGame" || current == "Start" || current == "setSeed") Poses.Clear(); }
        static string Stable(Fsm fsm)
        {
            if (fsm == null || fsm.Name != "saveItemVar" || fsm.GameObject == null || !fsm.GameObject.name.StartsWith("hinge_", StringComparison.Ordinal) || fsm.GameObject.GetComponentInParent<ES3Prefab>() == null) return null;
            var manager = ES3ReferenceMgrBase.Current; if (manager == null) return null;
            long id = manager.Get(fsm.GameObject); if (id < 0) id = manager.Add(fsm.GameObject);
            return "apocasaver.object." + id + ".hinge-rotation";
        }
        static void BeforeSave(ES3PlayMaker.Save __instance, out FsmString __state)
        {
            __state = null; if (!HostSaveSystem.LocalWorld || __instance.value.Type != VariableType.Vector3) return;
            string key = Stable(__instance.Fsm); if (key == null) return;
            __state = __instance.key; __instance.key = new FsmString { Value = key };
        }
        static Exception AfterSave(ES3PlayMaker.Save __instance, FsmString __state, Exception __exception)
        { if (__state != null) __instance.key = __state; if (__exception != null) HostSaveSystem.Fail(__exception.Message); return __exception; }
        static void BeforeLoad(ES3PlayMaker.Load __instance, out FsmString __state)
        {
            __state = null; if (Multiplayer.Client || __instance.value.Type != VariableType.Vector3) return;
            string key = Stable(__instance.Fsm); if (key == null || !ES3.KeyExists(key, __instance.GetSettings())) return;
            __state = __instance.key; __instance.key = new FsmString { Value = key };
        }
        static Exception AfterLoad(ES3PlayMaker.Load __instance, FsmString __state, Exception __exception)
        { if (__state != null) __instance.key = __state; if (__exception == null) VehicleLoadPlacement.LoadedStance(__instance); return __exception; }
        internal static bool SetPose(string itemName, string pose)
        { if (!string.IsNullOrEmpty(itemName) && !Multiplayer.Client) Poses["apocasaver.pose." + itemName] = pose ?? ""; return false; }
        internal static bool GetPose(string itemName, ref string __result)
        {
            __result = null; if (Multiplayer.Client || string.IsNullOrEmpty(itemName)) return false;
            string key = "apocasaver.pose." + itemName;
            if (Poses.TryGetValue(key, out __result)) return false;
            string file = SaveBus.Slot; if (file == null) return false;
            foreach (var location in new[] { ES3.Location.Cache, ES3.Location.File })
            {
                var settings = new ES3Settings(file) { location = location };
                if (ES3.KeyExists(key, settings)) { __result = ES3.Load<string>(key, settings); break; }
            }
            return false;
        }
        internal static void SavePoses()
        { foreach (var pair in Poses) ES3.Save<string>(pair.Key, pair.Value, new ES3Settings(HostSaveSystem.ActiveFile) { location = ES3.Location.Cache }); }
    }
}
