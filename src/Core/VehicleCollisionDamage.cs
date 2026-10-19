using BepInEx.Configuration;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;

namespace WildWasteland.Core
{
    // Native CrashDamage runs on the local driver's vehicle, including on MP guests.
    internal static class VehicleCollisionDamage
    {
        static ConfigEntry<bool> _enabled;
        internal static void Install(ConfigEntry<bool> enabled)
        {
            _enabled = enabled;
            Hooks.SendEventEnter.Prefix(null, BeforeDamage);
            Log.Always("[Core] Vehicle occupant collision damage follows Ram damage in car (default off)");
        }
        static bool BeforeDamage(SendEvent action)
        {
            if (_enabled == null || _enabled.Value || action.Fsm == null || action.Fsm.Name != "CrashDamage"
                || action.sendEvent == null || action.sendEvent.Name != "Damage" || action.eventTarget == null) return true;
            var kind = action.eventTarget.target;
            if (kind != FsmEventTarget.EventTarget.GameObject && kind != FsmEventTarget.EventTarget.GameObjectFSM) return true;
            var target = action.Fsm.GetOwnerDefaultTarget(action.eventTarget.gameObject);
            var player = GameRefs.Player;
            if (target == null || player == null || !target.transform.IsChildOf(player.transform)) return true;
            if (Log.Verbose) Log.Info("[Core] Blocked vehicle collision damage to player from " + action.Fsm.GameObject.name);
            if (!action.everyFrame) action.Finish();
            return false;
        }
    }
}
