using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;

namespace WildWasteland.Modules.Patrols
{
    // Hold the native final terrain wait, before Menu.Activate / ScaleTime 1 give control back to the player.
    internal static class PatrolLoadPreparation
    {
        private static Fsm _save;
        private static bool _loading;
        internal static void Install(Core.IModule owner)
        {
            Core.Hooks.WaitUpdate.Prefix(owner, BeforeWait);
        }
        internal static void StateChanged(string state, PlayMakerFSM save)
        {
            if (state == "LoadGame" || state == "setSeed")
            { _loading = true; _save = save.Fsm; VehicleBuildCache.WarmPark(); }
            if (_loading && state == "Wait for Terrain Gen")
            { Prefabs.Refresh(); VehicleBuildCache.WarmPark(); WarRig.PoiEncounter.Prepare(); }
            if (state == "isPlay")
            {
                if (_loading) Core.Log.Always("[Patrols] Load handoff ready: vehicle builds cached, saved patrol AI prepared; native vehicle/item deltas retained.");
                _loading = false;
            }
        }
        private static bool BeforeWait(Wait __instance)
        {
            if (!_loading || !PatrolsModule.Running || !ReferenceEquals(__instance.Fsm, _save)) return true;
            if (__instance.State.Name != "Wait for Terrain Gen") return true;
            return !VehicleBuildCache.Warming && !PatrolPersistence.Restoring;
        }
        internal static void Reset() { _save = null; _loading = false; }
    }
}
