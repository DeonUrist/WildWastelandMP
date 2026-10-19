using System;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;

namespace WildWasteland.Modules.Guns
{
    [HarmonyPatch(typeof(UiTextSetText), "OnEnter")]
    internal static class WeaponDamageText
    {
        static readonly FsmString Damage = new FsmString { Value = "Damage - 50" };
        static readonly FsmString BuyDamage = new FsmString { Value = "Crossbow / Damage - 50" };

        static void Prefix(UiTextSetText __instance, out FsmString __state)
        {
            __state = null;
            if (!GunsModule.ProjectilesEnabled || !GunsModule.PlayerTracers.Value) return;
            var fsm = __instance.Fsm;
            if (fsm == null || (fsm.Name != "ItemName" && fsm.Name != "Buyable")) return;
            var owner = fsm.GameObject;
            if (owner == null) return;
            string name = owner.name;
            int start = name.StartsWith("MP_", StringComparison.Ordinal) ? 3 : 0;
            if (name.IndexOf("crossbow", start, StringComparison.OrdinalIgnoreCase) != start) return;
            var text = __instance.text;
            if (text == null || text.UseVariable) return;
            if (text.Value == "Damage - 20") { __state = text; __instance.text = Damage; }
            else if (text.Value == "Crossbow / Damage - 20") { __state = text; __instance.text = BuyDamage; }
        }

        static void Finalizer(UiTextSetText __instance, FsmString __state)
        {
            if (__state != null) __instance.text = __state;
        }
    }
}
