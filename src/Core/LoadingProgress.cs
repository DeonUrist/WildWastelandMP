using System;
using System.Collections.Generic;
using MapMagic.Core;
using UnityEngine;

namespace WildWasteland.Core
{
    // Presentation only: observe native work and module-owned preparation without delaying the load.
    internal static class LoadingProgress
    {
        internal struct Work
        {
            internal float Fraction;
            internal string Detail;
            internal Work(float fraction, string detail) { Fraction = fraction; Detail = detail; }
        }
        private static readonly Dictionary<IModule, Func<Work?>> Sources = new Dictionary<IModule, Func<Work?>>();
        private static MapMagicObject _map;
        internal static bool ScreenVisible;
        internal static bool MenusBlocked { get { return ScreenVisible || SaveBus.Busy || MultiplayerSaving.Frozen; } }
        internal static void Register(IModule owner, Func<Work?> source) { Sources[owner] = source; }
        internal static void Remove(IModule owner) { Sources.Remove(owner); }
        internal static void Begin() { _map = null; ScreenVisible = true; }

        internal static void Read(bool saving, out float fraction, out string detail)
        {
            string state = SaveBus.State;
            fraction = 0f;
            detail = saving ? "Core game: saving world" : "Core game: loading scene";
            if (saving)
            {
                fraction = state == "isPlay" ? 1f : state == "SaveGame" ? .5f : .1f;
                return;
            }
            switch (state)
            {
                case "Start": case "setSeed": fraction = .03f; detail = "Core game: preparing world"; break;
                case "LoadGame": fraction = .08f; detail = "Core game: reading save"; break;
                case "generateTerrain": case "generateTerrain 2":
                    var anchor = Origin.AnchorTransform;
                    if (_map == null && anchor != null) _map = anchor.GetComponent<MapMagicObject>();
                    float terrain = _map != null && _map.graph != null ? _map.GetProgress() : 0f;
                    if (float.IsNaN(terrain) || float.IsInfinity(terrain)) terrain = 0f;
                    terrain = Mathf.Clamp01(terrain);
                    fraction = .1f + .5f * terrain;
                    detail = "Core game: generating terrain (" + Mathf.RoundToInt(terrain * 100f) + "%)";
                    break;
                case "LoadVar": fraction = .62f; detail = "Core game: restoring world and items"; break;
                case "loaded": fraction = .65f; detail = "Core game: restoring vehicles and characters"; break;
                case "Wait for Terrain Gen":
                    fraction = .98f; detail = "Core game: preparing player control";
                    foreach (var pair in Sources)
                    {
                        if (!ModuleHost.IsRunning(pair.Key)) continue;
                        Work? work = pair.Value();
                        if (!work.HasValue) continue;
                        fraction = .7f + .28f * Mathf.Clamp01(work.Value.Fraction);
                        detail = pair.Key.DisplayName + ": " + work.Value.Detail;
                        break;
                    }
                    break;
                case "done": case "play": fraction = .99f; detail = "Core game: handing control to player"; break;
                case "isPlay": fraction = 1f; detail = "Ready"; break;
            }
        }
    }
}
