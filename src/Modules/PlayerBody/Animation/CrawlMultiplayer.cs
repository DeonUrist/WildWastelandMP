using System;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.PlayerBody
{
    internal static partial class Crawling
    {
        static partial void InstallMultiplayer(PlayerBodyModule owner)
        {
            DownedProbe = LocalDowned;
            if (!Multiplayer.Present) return;
            if (!Core.Patches.Patch(owner, AccessTools.Method(Multiplayer.Type("Game.PauseControl"), "OnInput"), prefix: AccessTools.Method(typeof(Crawling), "DownedInput")))
                throw new InvalidOperationException("MP downed crawl input hook failed");
        }
        static bool LocalDowned() { return Multiplayer.Active && !Multiplayer.LocalTargetable; }
        static bool DownedInput(FsmStateAction __instance, ref bool __result)
        {
            if (!Downed || !GameRefs.InGame || Multiplayer.Typing || MultiplayerSaving.Frozen || Core.UI.EditorSession.Busy || ApocasetterBridge.InputBlocked || !Application.isFocused) return true;
            if (!(__instance is GetAxisKeyAxis) || Game.MovementFsm == null || __instance.Fsm != Game.MovementFsm.Fsm || Game.MoveState != "Crawling") return true;
            __result = true; return false; // Only the native crawl axes bypass downed input freezing; all other input keeps its original guards.
        }
    }
    internal sealed partial class Body
    {
        partial void CaptureCrawlMotion() { MultiplayerAvatars.CaptureWeaponMotion(this, _anim); }
    }
}
