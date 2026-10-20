using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class VehicleMeleeProtection
    {
        // Ignore suspension/parking jitter below 0.36 km/h and 0.1 rad/s.
        const float StillSquared = .01f;
        internal static void Install()
        {
            Hooks.ProcessEvent.Prefix(null, BeforeAttack);
            Log.Always("[Core] Animal/melee occupant damage requires a stationary vehicle with its engine off");
        }
        static bool BeforeAttack(Fsm fsm, FsmEvent ev, FsmEventData data)
        {
            if (ev == null || ev.Name != "attack" || fsm == null || fsm.Name != "Damage") return true;
            var target = fsm.Variables.FindFsmGameObject("detectedObj");
            return target == null || CanHit(target.Value);
        }
        internal static bool CanHit(GameObject target)
        {
            if (target == null) return true;
            GameObject car = null;
            var player = GameRefs.Player;
            if (player != null && target.transform.IsChildOf(player.transform))
            {
                var vehicle = player.GetComponentInParent<VehicleController>();
                if (vehicle != null) car = vehicle.gameObject;
                else
                {
                    var seated = FsmUtil.Find(player, "InCar");
                    if (seated != null && seated.ActiveStateName == "InCar")
                    {
                        var value = seated.FsmVariables.FindFsmGameObject("Car");
                        car = value != null ? value.Value : null;
                        if (car == null) return false;
                    }
                }
            }
            else if (Multiplayer.IsRemotePlayer(target))
            {
                car = Multiplayer.RemoteCar(target);
                if (car == null && MultiplayerPlayers.Riding(Multiplayer.PeerFor(target))) return false;
            }
            if (car == null) return true;
            if (Multiplayer.OwnedElsewhere(car))
            {
                bool running; Vector3 velocity, angular;
                return MultiplayerVehicles.TryMeleeState(car, out running, out velocity, out angular)
                    && !running && velocity.sqrMagnitude <= StillSquared && angular.sqrMagnitude <= StillSquared;
            }
            var controller = car.GetComponent<VehicleController>();
            var body = car.GetComponent<Rigidbody>();
            if (controller == null || controller.powertrain == null || controller.powertrain.engine == null || body == null) return false;
            bool engine;
            if (Services.VehicleIgnition == null || !Services.VehicleIgnition.TryRunning(car, out engine))
                engine = controller.powertrain.engine.IsRunning;
            return !engine && body.velocity.sqrMagnitude <= StillSquared && body.angularVelocity.sqrMagnitude <= StillSquared;
        }
    }
}
