using System;
using HarmonyLib;

namespace WildWasteland.Core
{
    internal static class MultiplayerNotifications
    {
        internal static event Func<string, bool> Present;
        static Action[] _draw;
        static bool Hidden { get { return LoadingProgress.MenusBlocked || SaveBus.Loading || SaveBus.Saving; } }
        internal static void Install()
        {
            string[] types = { "UI.MultiplayerMenu", "UI.PlayersOverlay", "UI.CompassBar", "UI.HudMenu", "UI.Chat", "UI.Hud", "Game.Survival" };
            _draw = new Action[types.Length];
            for (int i = 0; i < types.Length; i++) _draw[i] = MultiplayerAccess.Call<Action>(Multiplayer.Type(types[i]), "OnGUI", Type.EmptyTypes);
            Patch("ModRunner", "OnGUI", "Draw");
            Patch("UI.Chat", "Notice", "ChatNotice");
            Patch("UI.Hud", "Show", "Notice");
        }
        static void Patch(string type, string method, string prefix)
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type(type), method), prefix: AccessTools.Method(typeof(MultiplayerNotifications), prefix)))
                throw new InvalidOperationException("Multiplayer notification hook failed: " + type + "." + method);
        }
        static bool Draw()
        {
            if (!Multiplayer.Present) return true;
            if (!Hidden) for (int i = 0; i < _draw.Length; i++) _draw[i]();
            return false;
        }
        static bool ChatNotice(string __0) { return Multiplayer.WorldReady && Notice(__0); }
        static bool Notice(string __0)
        {
            if (string.IsNullOrWhiteSpace(__0)) return true;
            if (Hidden) return false;
            var show = Present;
            return !Multiplayer.WorldReady || show == null || !show(__0);
        }
    }
}
