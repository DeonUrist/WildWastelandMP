using System;
using System.Collections;
using System.IO;
using Den.Tools.Tasks;
using MapMagic.Core;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class SaveTerrain
    {
        static bool _held, _draining;
        internal static void Install()
        {
            if (!Patches.Patch(null, Patches.Method(typeof(MapMagicObject), "Update"), prefix: Patches.Method(typeof(SaveTerrain), "MapUpdate"))
                || !Patches.Patch(null, Patches.Method(typeof(CoroutineManager), "Update"), prefix: Patches.Method(typeof(SaveTerrain), "ApplyUpdate")))
                throw new InvalidOperationException("Terrain save barrier could not be installed");
            Log.Always("[System] Terrain save barrier ready: drain pending work, hold streaming until publication completes");
        }
        static bool MapUpdate()
        {
            if (!_held) return true;
            // Stop new tile moves/admission, but let already queued applications finish before Begin.
            if (_draining) CoroutineManager.Update();
            return false;
        }
        static bool ApplyUpdate() { return !_held || _draining; }
        internal static IEnumerator Prepare()
        {
            _held = _draining = true;
            int generation = SaveBus.Generation, quiet = 0; float deadline = Time.unscaledTime + 120f;
            while (true)
            {
                if (generation != SaveBus.Generation) throw new IOException("World changed before terrain could settle for saving");
                if (Time.unscaledTime >= deadline) throw new IOException("Terrain is still loading; save deferred before writing. Try again after loading finishes");
                quiet = ThreadManager.IsWorking || CoroutineManager.IsWorking ? 0 : quiet + 1;
                if (quiet >= 2)
                {
                    _draining = false;
                    Log.Always("[System] Terrain settled; streaming held for world save");
                    yield break;
                }
                yield return null;
            }
        }
        internal static void Release() { _held = _draining = false; }
    }
}
