using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Modules.Patrols
{
    // Ram damage: the game's own CarAttack bumper trigger only hurts creatures, and the player's car's CrashDamage FSM scales
    // with the player's own speed - a parked player is never hurt by an AI car. This applies damage the vanilla way instead:
    // Player [Bodypart].Damage = -amount (damage values are negative in this game: BumperDamage -3, speedDamage -0.5) and the
    // event "Damage" on that FSM (armor first, then health). The hurt sound, camera kick and red blood splash live in the
    // DamageEffectSound / DamageEffectSound_InCar FSMs (one of them enabled, toggled by InCar); Bodypart's own SendEvent to them
    // is disabled in the prefab - every attacker sends "Damage" to the effect FSM itself (FallDamage does), so we do too.
    internal static partial class Ram
    {
        // sits on every AI car root (next to PatrolMarker); the Rigidbody is there, so every collision of the car arrives here
        internal sealed class Sensor : MonoBehaviour
        {
            private PatrolMarker _mk; private Rigidbody _rb;   // cached (a car scraping along a wall fires this many times a second)

            private void OnCollisionEnter(Collision col)
            { if (!PatrolsModule.Running || Core.Multiplayer.OwnedElsewhere(gameObject)) return;
                try
                {
                    if (col.collider == null) return;
                    var p = PlayerRef.Player;                                   // refresh the cached player first
                    int kind = p == null ? 0 : PlayerRef.LocalKind(col.collider.transform);
                    if (kind == 0 && !Core.Multiplayer.Hosting) return;
                    if (_mk == null) _mk = GetComponent<PatrolMarker>();
                    if (_rb == null) _rb = GetComponent<Rigidbody>();
                    var mk = _mk; var rb = _rb;
                    if(mk!=null&&!TrafficFactions.PlayerHostile(mk.Faction))return;
                    var carVel = rb != null ? rb.velocity : Vector3.zero;
                    // The impact speed is how fast THIS car moves into the contact point - not the relative speed, which is just as
                    // high when the player runs or drives into a parked car. A standing car never hurts anyone.
                    Vector3 toContact = col.contactCount > 0 ? col.GetContact(0).point - transform.position : (col.collider.transform.position - transform.position);
                    toContact.y = 0f;
                    float into = toContact.sqrMagnitude > 1e-4f ? Vector3.Dot(carVel, toContact.normalized) : 0f;
                    if (kind != 0) Hit(gameObject, mk != null ? mk.BodyPrefab : null, kind, Mathf.Max(0f, into), col.collider, carVel);
                    if (Core.Multiplayer.Hosting)
                        for (int i = 0; i < Core.Multiplayer.RemoteCount; i++)
                        {
                            var remote = Core.Multiplayer.RemoteAt(i); int remoteKind = RemoteKind(remote, col.collider.transform);
                            if (remoteKind != 0) Hit(gameObject, mk != null ? mk.BodyPrefab : null, remoteKind, Mathf.Max(0f, into), col.collider, carVel, remote);
                        }
                }
                catch (Exception e) { Core.Log.Error("[Patrols] " + "Ram: " + e); }
            }
        }

        internal const float DefaultDamage = 30f;
        struct ImpactKey : IEquatable<ImpactKey>
        {
            internal int Car, Target;
            public bool Equals(ImpactKey other) { return Car == other.Car && Target == other.Target; }
            public override bool Equals(object other) { return other is ImpactKey && Equals((ImpactKey)other); }
            public override int GetHashCode() { return unchecked(Car * 397 ^ Target); }
        }
        private static readonly Dictionary<ImpactKey, float> _next = new Dictionary<ImpactKey, float>();
        private static float _pending, _pendingAt = -1f, _healthBefore;
        private static string _pendingCar;
        private static PlayMakerFSM _pausedMovement;      // the player's Movement FSM, paused while a shove carries
        private static float _resumeAt = -1f;
        private const float PushSeconds = 0.5f;

        // Scale the native creature hit after its value is written, before CarAttack sends Damage. Never change the shared speed/bumper variable.
        // Seated drivers / passengers / turrets are all-trigger bodies (Rider.NoContacts) without per-pair collision ignores, so a
        // car's own CarAttack trigger (OnTriggerEnter, layer 10 = the NPC layer; Sources fsm/prefabs/Rustallion.md "CarAttack")
        // could now see its own crew when a collider of theirs (re)enters it, and ram-damage them above 5 m/s. The old ignore
        // pairs suppressed that; this keeps it suppressed: a car never ram-damages anything in its own hierarchy.
        static bool OwnCrew(HutongGames.PlayMaker.Fsm fsm, GameObject target)
        {
            return target != null && fsm.GameObject != null && ReferenceEquals(target.transform.root, fsm.GameObject.transform.root);
        }
        internal static bool BeforeSetFsmFloat(HutongGames.PlayMaker.Actions.SetFsmFloat action)
        {
            if (!PatrolsModule.Running) return true;
            var fsm = action.Fsm;
            if (fsm == null || fsm.Name.Length != 9 || fsm.Name != "CarAttack") return true;
            return !RemoteDriven(fsm) && !OwnCrew(fsm, fsm.GetOwnerDefaultTarget(action.gameObject));
        }
        internal static bool BeforeSendEvent(HutongGames.PlayMaker.Actions.SendEvent action)
        {
            if (!PatrolsModule.Running) return true;
            var fsm = action.Fsm;
            if (fsm == null || fsm.Name.Length != 9 || fsm.Name != "CarAttack" || action.eventTarget == null) return true;
            if (!RemoteDriven(fsm) && !OwnCrew(fsm, fsm.GetOwnerDefaultTarget(action.eventTarget.gameObject))) return true;
            action.Finish();
            return false;
        }

        internal static void AfterSetFsmFloat(HutongGames.PlayMaker.Actions.SetFsmFloat action)
        {
            if (!PatrolsModule.Running) return;
            var fsm = action.Fsm;
            if (fsm == null || fsm.Name.Length != 9 || fsm.Name != "CarAttack" || fsm.GameObject == null) return;
            if (RemoteDriven(fsm)) return;
            if (action.fsmName == null || action.fsmName.Value != "Bodypart" || action.variableName == null || action.variableName.Value != "Damage") return;
            var target = fsm.GetOwnerDefaultTarget(action.gameObject);
            var player = Core.GameRefs.Player;
            if (target == null || Core.Multiplayer.IsRemotePlayer(target) || (player != null && target.transform.IsChildOf(player.transform))) return;
            if (OwnCrew(fsm, target)) return;   // BeforeSetFsmFloat skipped the write: nothing to scale
            var bodypart = Core.FsmUtil.Find(target, "Bodypart");
            var damage = bodypart != null ? bodypart.FsmVariables.FindFsmFloat("Damage") : null;
            if (damage == null || damage.Value >= 0f) return;
            damage.Value *= PatrolsModule.NpcRamDamageMultiplier.Value;
            if (Core.Log.Verbose) PatrolsModule.Verbose("Ram: car hit -> " + target.name + ", " + (-damage.Value) + " damage (x" + PatrolsModule.NpcRamDamageMultiplier.Value + ")");
        }

        // kind: 1 = the player on foot, 2 = the player's car. rel = the car's speed into the contact (m/s), carVel = its velocity (push direction).
        internal static void Hit(GameObject car, string body, int kind, float rel, Collider hit, Vector3 carVel, GameObject remote = null)
        {
            if (!PatrolsModule.RamDamage.Value || car == null || kind == 0) return;
            if (kind == 2 && !PatrolsModule.RamDamageInCar.Value) return;                  // in the player's car: only with the toggle
            float kmh = rel * 3.6f;
            float full = Mathf.Max(1f, PatrolsModule.RamFullSpeedKmh.Value);
            if (kmh < full * 0.5f) return;                                          // below half the full-damage speed: nothing
            var player = remote != null ? remote.transform : PlayerRef.Player;
            if (player == null || (remote != null && !Core.Multiplayer.RemoteTargetable(remote))) return;
            var id = new ImpactKey { Car = car.GetInstanceID(), Target = player.GetInstanceID() };
            float next;
            if (_next.TryGetValue(id, out next) && Time.time < next) return;       // one hit per second per car (many colliders touch at once)
            _next[id] = Time.time + 1f;

            float speedFactor = Mathf.Clamp(kmh / full, 0.5f, 1f);                  // 50 % at half speed, 100 % at full speed
            float amount = BaseDamage(body) * PatrolsModule.RamDamageMultiplier.Value * speedFactor;
            if (kind == 2) amount *= PatrolsModule.RamInCarFactor.Value;
            amount = Mathf.Round(amount);

            if (remote != null)
            {
                var shove = kind == 1 ? PushVelocity(player.position, car, carVel, rel) : Vector3.zero;
                SendImpact(remote, amount, shove, car.name);
                return;
            }
            PlayMakerFSM bodypart = null, health = null, movement = null;
            var effects = new List<PlayMakerFSM>();
            bool god = false;
            foreach (var f in player.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName == "Bodypart") bodypart = f;
                else if (f.FsmName == "Health") health = f;
                else if (f.FsmName == "Movement") movement = f;
                else if (f.FsmName == "DamageEffectSound" || f.FsmName == "DamageEffectSound_InCar") effects.Add(f);
                else if ((f.FsmName == "GODMODE" || f.FsmName == "GODMODEMovement") && f.enabled && f.Fsm.Initialized && f.ActiveStateName == "active") god = true;
            }
            if (god) { PatrolsModule.Verbose("Ram: " + car.name + " hit the player - god mode, no damage"); return; }
            if (kind == 1) Push(player, movement, car, carVel, rel);
            if (amount < 1f) { PatrolsModule.Verbose("Ram: " + car.name + " hit the player at " + kmh.ToString("0") + " km/h - no damage (multiplier)"); return; }
            float before = -1f;
            if (health != null && health.Fsm.Initialized) { var h = health.FsmVariables.GetFsmFloat("Health"); if (h != null) before = h.Value; }

            bool applied = false;
            if (bodypart != null && bodypart.Fsm.Initialized)
            {
                var d = bodypart.FsmVariables.GetFsmFloat("Damage");
                if (d != null) { d.Value = -amount; bodypart.SendEvent("Damage"); applied = true; }
            }
            if (!applied && health != null && health.Fsm.Initialized)
            {
                var h = health.FsmVariables.GetFsmFloat("Health");
                if (h != null) { h.Value -= amount; applied = true; }
            }
            if (applied)
                foreach (var f in effects)
                    if (f.enabled && f.Fsm.Initialized) f.SendEvent("Damage");   // the disabled one ignores it
            PatrolsModule.Verbose("Ram: " + car.name + " (" + (body ?? "?") + ") hit the player " + (kind == 2 ? "in their car" : "on foot")
                + " at " + kmh.ToString("0") + " km/h -> " + amount + " damage" + (applied ? "" : " (no Player FSM found!)")
                + (before >= 0f ? ", health " + before.ToString("0") : "") + (hit != null ? ", collider " + hit.name + " layer " + LayerMask.LayerToName(hit.gameObject.layer) : ""));
            if (applied && before >= 0f) { _pending = amount; _pendingAt = Time.time + 0.5f; _healthBefore = before; _pendingCar = car.name; }
        }

        // Explosion damage to the player (the "Witness me!" rider, 2.1.4), the same vanilla path as a ram: Bodypart.Damage = -amount +
        // "Damage", the effect FSM's "Damage" for the hurt sound and splash. God mode: nothing. Returns whether it landed.
        internal static bool HurtPlayer(float amount, string source)
        {
            var player = PlayerRef.Player;
            if (player == null || amount < 1f) return false;
            PlayMakerFSM bodypart = null, health = null;
            var effects = new List<PlayMakerFSM>();
            foreach (var f in player.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName == "Bodypart") bodypart = f;
                else if (f.FsmName == "Health") health = f;
                else if (f.FsmName == "DamageEffectSound" || f.FsmName == "DamageEffectSound_InCar") effects.Add(f);
                else if ((f.FsmName == "GODMODE" || f.FsmName == "GODMODEMovement") && f.enabled && f.Fsm.Initialized && f.ActiveStateName == "active")
                { PatrolsModule.Verbose(source + ": god mode, no damage"); return false; }
            }
            amount = Mathf.Round(amount);
            bool applied = false;
            if (bodypart != null && bodypart.Fsm.Initialized)
            {
                var d = bodypart.FsmVariables.GetFsmFloat("Damage");
                if (d != null) { d.Value = -amount; bodypart.SendEvent("Damage"); applied = true; }
            }
            if (!applied && health != null && health.Fsm.Initialized)
            {
                var h = health.FsmVariables.GetFsmFloat("Health");
                if (h != null) { h.Value -= amount; applied = true; }
            }
            if (applied) foreach (var f in effects) if (f.enabled && f.Fsm.Initialized) f.SendEvent("Damage");
            PatrolsModule.Verbose(source + ": " + amount + " damage to the player" + (applied ? "" : " (no Player FSM found!)"));
            return applied;
        }

        // Shove the player on foot along the car's direction of travel (plus a small hop). The Movement FSM rewrites the velocity every
        // frame (SetVelocity from the input axes), so it is paused for PushSeconds - RestartOnEnable off, so it resumes in the same state.
        private static void Push(Transform player, PlayMakerFSM movement, GameObject car, Vector3 carVel, float rel)
        {
            Push(player, movement, PushVelocity(player.position, car, carVel, rel));
        }
        private static Vector3 PushVelocity(Vector3 position, GameObject car, Vector3 carVel, float rel)
        {
            float strength = PatrolsModule.RamPushStrength.Value;
            if (strength <= 0f) return Vector3.zero;
            Vector3 dir = carVel; dir.y = 0f;
            if (dir.sqrMagnitude < 0.25f) { dir = position - car.transform.position; dir.y = 0f; }
            if (dir.sqrMagnitude < 1e-4f) dir = car.transform.forward;
            dir.Normalize();
            return dir * (rel * strength) + Vector3.up * (5f * strength);
        }
        private static void Push(Transform player, PlayMakerFSM movement, Vector3 v)
        {
            if (v.sqrMagnitude < 1e-6f) return;
            var rb = player.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;
            if (movement != null && movement.enabled)
            {
                if (_pausedMovement != null && _pausedMovement != movement && _pausedMovement.gameObject != null) _pausedMovement.enabled = true;
                movement.Fsm.RestartOnEnable = false;
                movement.enabled = false;
                _pausedMovement = movement;
                _resumeAt = Time.time + PushSeconds;
            }
            rb.velocity = v;
            PatrolsModule.Verbose("Ram: shoved the player " + v.magnitude.ToString("0.0") + " m/s");
        }

        // Called from the runner: resumes the paused Movement FSM, and checks half a second after a hit that the health really went down.
        // an entry only matters until its time has passed; dropped every 30 s (destroyed cars left theirs behind for good)
        private static float _nextPrune;
        private static readonly List<ImpactKey> _expired = new List<ImpactKey>();
        private static void PruneHitTimes()
        {
            _expired.Clear();
            foreach (var kv in _next) if (Time.time >= kv.Value) _expired.Add(kv.Key);
            foreach (var k in _expired) _next.Remove(k);
        }

        internal static void Tick()
        {
            if (Time.unscaledTime >= _nextPrune) { _nextPrune = Time.unscaledTime + 30f; PruneHitTimes(); }
            if (_pausedMovement != null && Time.time >= _resumeAt)
            {
                if (_pausedMovement.gameObject != null) { _pausedMovement.enabled = true; _pausedMovement.Fsm.RestartOnEnable = true; }
                _pausedMovement = null;
            }
            if (_pendingAt < 0f || Time.time < _pendingAt) return;
            _pendingAt = -1f;
            var player = PlayerRef.Player;
            if (player == null) return;
            foreach (var f in player.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Health" && f.Fsm.Initialized)
                {
                    var h = f.FsmVariables.GetFsmFloat("Health");
                    if (h == null) return;
                    float delta = h.Value - _healthBefore;
                    if (delta > 0f) Core.Log.Warn("[Patrols] " + "Ram: player health went UP after " + _pendingCar + "'s hit (" + _healthBefore.ToString("0") + " -> " + h.Value.ToString("0") + ") - damage sign wrong?");
                    else PatrolsModule.Verbose("Ram: player health " + _healthBefore.ToString("0") + " -> " + h.Value.ToString("0") + " (" + _pending + " sent)");
                    return;
                }
        }

        // "Junker=50, Rust*=70" -> full-speed damage for this body; unlisted bodies = DefaultDamage.
        internal static float BaseDamage(string body)
        {
            string b = (body ?? "").Trim();
            string table = PatrolsModule.RamDamageByBody.Value ?? "";
            float best = -1f; int bestLen = -1;
            foreach (var raw in table.Split(',', ';'))
            {
                int eq = raw.IndexOf('=');
                if (eq < 0) continue;
                string key = raw.Substring(0, eq).Trim(); float val;
                if (key.Length == 0 || !float.TryParse(raw.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val)) continue;
                bool match = key.EndsWith("*") ? b.StartsWith(key.Substring(0, key.Length - 1), StringComparison.OrdinalIgnoreCase)
                                                : string.Equals(key, b, StringComparison.OrdinalIgnoreCase);
                if (match && key.Length > bestLen) { best = val; bestLen = key.Length; }   // the most specific key wins
            }
            return best >= 0f ? best : DefaultDamage;
        }
    }
}
