using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Modules.Patrols
{
    // Removes raider cars the player has left behind, so they do not pile up in the world and in the save. The game itself only
    // deletes cars beyond 5000 m (DeleteFarAwayItems); everything closer stays forever. A car is eligible when the player never sat
    // in it (loot you took or a car you drove is never touched) and it is farther than [Cleanup] MinDistance. It goes once it has been
    // that far for RemoveAfterMinutes, or right away (farthest first) while there are more than MaxCars raider cars in the world.
    // Removal takes the car, its crew, its parts and its cargo out of the game's registration lists as well.
    internal class Cleanup : MonoBehaviour
    {
        private const float Interval = 5f;
        private float _nextTick, _nextEnteredCheck;
        private Patrol _patrol;

        // WW: was Update (Update job)
        internal void Tick()
        {
            if (_patrol == null) _patrol = GetComponent<Patrol>();
            if (_patrol == null || !_patrol.InGame()) return;

            // a raider car the player sits in is theirs from now on
            if (Time.unscaledTime >= _nextEnteredCheck)
            {
                _nextEnteredCheck = Time.unscaledTime + 1f;
                var pc = PlayerRef.PlayerCar;
                var m = pc != null ? pc.GetComponent<PatrolMarker>() : null;
                if (m != null && !m.PlayerEntered) { m.PlayerEntered = true; PatrolsModule.Verbose("Cleanup: " + pc.name + " is the player's now, never removed"); }
                if (m != null && m.TemplateName == "Abandoned War Rig" && m.SetupOnly)
                {
                    var body = pc.GetComponent<Rigidbody>();
                    if (body != null && Vector3.ProjectOnPlane(body.velocity, Vector3.up).sqrMagnitude > 1f && Nwh.EngineRunning(pc))
                    { m.SetupOnly = false; Core.Log.Always("[Patrols] War Rig truck driven by player: normal native car cleanup now applies; tanker remains protected."); }
                }
            }

            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + Interval;
            if (!PatrolsModule.CleanupEnabled.Value && !PatrolsModule.SelfDestruct.Value) return;
            var player = PlayerRef.Player;
            if (player == null) return;

            var all = PatrolMarker.All.ToArray();   // copy: Remove() destroys cars, which edits the list
            float minD = PatrolsModule.CleanupDistance.Value;
            float limit = PatrolsModule.CleanupMinutes.Value * 60f;
            var eligible = new List<KeyValuePair<float, PatrolMarker>>();
            int alive = 0;
            foreach (var m in all)
            {
                if (m == null) continue;
                if (Core.Multiplayer.OwnedElsewhere(m.gameObject)) { m.PlayerEntered = true; m.FarSeconds = 0f; continue; }
                if (WarRig.PoiEncounter.Protected(m.gameObject)) continue;
                if (WorldTraffic.Owns(m)) continue; // traffic unloads its own representations while retaining world records
                if(WorldTraffic.IsBattleCar(m.gameObject))continue;
                if (CrewReturn.Holds(m.gameObject)) { m.FarSeconds = 0f; continue; }   // its crew may still walk back to it
                // a self-destructed chassis goes as soon as the player is far enough away to not see it (regardless of the timer and the cap)
                if (m.Exploded)
                {
                    if (Core.Multiplayer.PlayerDistance(m.transform.position, player) > Explode.RemoveDistance) Remove(m, "dead chassis beyond " + Explode.RemoveDistance.ToString("0") + " m");
                    continue;
                }
                if (!PatrolsModule.CleanupEnabled.Value) continue;   // only dead chassis are removed with the cleanup off
                alive++;
                if (m.PlayerEntered) { m.FarSeconds = 0f; continue; }
                float d = Core.Multiplayer.PlayerDistance(m.transform.position, player);
                if (d < minD) { m.FarSeconds = 0f; continue; }
                m.FarSeconds += Interval;
                eligible.Add(new KeyValuePair<float, PatrolMarker>(d, m));
            }
            if (eligible.Count == 0) return;

            // timer
            for (int i = eligible.Count - 1; i >= 0; i--)
            {
                var m = eligible[i].Value;
                if (limit > 0f && m.FarSeconds >= limit)
                {
                    Remove(m, "left behind for " + (m.FarSeconds / 60f).ToString("0") + " min");
                    eligible.RemoveAt(i);
                    alive--;
                }
            }
            // cap: farthest first
            int max = PatrolsModule.CleanupMaxCars.Value;
            if (max <= 0 || alive <= max) return;
            eligible.Sort((a, b) => b.Key.CompareTo(a.Key));
            foreach (var kv in eligible)
            {
                if (alive <= max) break;
                Remove(kv.Value, "more than " + max + " raider cars, " + kv.Key.ToString("0") + " m away");
                alive--;
            }
        }

        internal static void Remove(PatrolMarker m, string why)
        {
            var car = m.gameObject;
            if (Core.Multiplayer.OwnedElsewhere(car)) { m.PlayerEntered = true; return; }
            if (WarRig.PoiEncounter.Protected(car)) return;
            PatrolsModule.Verbose("Cleanup: removed " + car.name + " (" + why + ")");
            var trailer = TrailerSupport.Attached(car);
            if (trailer != null)
            {
                if (trailer.GetComponent<WarRig.WarRigUnit>() != null) WarRig.PoiEncounter.DetachTankers(car);
                else { Register.RemoveTree(trailer); Destroy(trailer); }
            }
            Register.RemoveTree(car);
            Destroy(car);
        }
    }
}
