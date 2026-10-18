using System;
using HarmonyLib;
using MapMagic.Core;
using MapMagic.Nodes.MatrixGenerators;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildWasteland.Core
{
    internal static class MultiplayerHeadless
    {
        internal static void Install()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) return;
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(MapMagicObject), "OnEnable"), prefix: AccessTools.Method(typeof(MultiplayerHeadless), "CpuHeightmap")))
                throw new InvalidOperationException("Headless terrain height application hook failed");
            Log.Always("[Multiplayer] Headless terrain: native CPU height application selected (no RenderTexture)");
        }
        static void CpuHeightmap(MapMagicObject __instance)
        {
            if (__instance.globals == null) return;
            __instance.globals.heightMainApply = HeightOutput200.ApplyType.SetHeights;
            __instance.globals.heightDraftApply = HeightOutput200.ApplyType.SetHeights;
        }
    }
}
