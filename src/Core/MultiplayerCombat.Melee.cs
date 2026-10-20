using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Core
{
    // Creature melee against guests is decided on the guest. The host's creature still chooses when it bites (its Damage FSM,
    // unchanged), but instead of the native PlayerDamage the host sends a hit request; the guest checks it against its own
    // position and its own copy of the creature, applies it like vanilla (Bodypart + hurt sound, red flash, camera kick) and
    // reports the result back. The host's view of a guest lags behind, so a guest who already stepped out of reach is not bitten.
    // Every other forwarded hit (NPC bullets, explosions, spikes) keeps the native path; guests now also get its hurt effects.
    internal static partial class MultiplayerCombat
    {
        internal const byte MeleeHitKind = 180, MeleeResultKind = 181;   // 160/161 are sleeper Pose/Remove, 173-176 Ultimate Tools; Message shares one reader
        // The guest sees the host's creatures about 0.1-0.2 s late; a lunging dog can be up to ~1 m short of where the host has it.
        const float MeleeSlack = .8f, DefaultReach = 2f;
        sealed class PendingMelee { internal string Attacker; internal float Damage, Reach, At; }
        static readonly Dictionary<ulong, Dictionary<uint, PendingMelee>> Melee = new Dictionary<ulong, Dictionary<uint, PendingMelee>>();
        static readonly List<uint> _meleeOld = new List<uint>();
        static readonly List<PlayMakerFSM> _meleeFsms = new List<PlayMakerFSM>(8);
        static uint _meleeSeq;
        static Type _presence;
        static Func<object, float> _pendingDamage;
        static bool _meleeLogged;

        static void InstallMelee()
        {
            _presence = Multiplayer.Type("Game.PlayerPresence");
            _pendingDamage = MultiplayerAccess.Get<float>(_presence, "PendingDamage");
            var forward = AccessTools.Method(Multiplayer.Type("Patches.ForwardDamagePatch"), "Prefix");
            var apply = AccessTools.Method(Multiplayer.Type("Net.NetSession"), "ApplyPlayerDamage");
            if (forward == null || apply == null
                || !Patches.Patch(Multiplayer.PatchOwner, forward, prefix: AccessTools.Method(typeof(MultiplayerCombat), "BeforeForwardDamage"))
                || !Patches.Patch(Multiplayer.PatchOwner, apply, postfix: AccessTools.Method(typeof(MultiplayerCombat), "AfterPlayerDamage")))
                throw new InvalidOperationException("MP melee hit hooks could not be installed");
            Multiplayer.Message += ReceiveMelee;
            Multiplayer.SessionChanged += ClearMelee;
        }
        static void ClearMelee() { Melee.Clear(); }

        // Host: native ForwardDamagePatch.Prefix (SendEvent.OnEnter). A creature's melee Damage FSM hitting a guest's presence
        // becomes a hit request; anything else runs the native forward unchanged.
        static bool BeforeForwardDamage(SendEvent __0)
        {
            if (!Multiplayer.Hosting || __0 == null || __0.sendEvent == null || __0.sendEvent.Name != "Damage") return true;
            try { return !RequestMelee(__0); }
            catch (Exception e) { Log.Warn("[Multiplayer] Melee hit request failed, native damage used: " + e.Message); return true; }
        }
        static bool RequestMelee(SendEvent __0)
        {
            var fsm = __0.Fsm; var et = __0.eventTarget;
            if (fsm == null || fsm.Name != "Damage" || et == null
                || (et.target != FsmEventTarget.EventTarget.GameObject && et.target != FsmEventTarget.EventTarget.GameObjectFSM)) return false;
            var target = fsm.GetOwnerDefaultTarget(et.gameObject);
            if (target == null || target.GetComponent(_presence) == null) return false;
            // A second Damage send aimed at an effect FSM (FallDamage pattern) is not another hit: skip it natively too.
            if (et.target == FsmEventTarget.EventTarget.GameObjectFSM && et.fsmName != null && !et.fsmName.IsNone && et.fsmName.Value != "Bodypart") return true;
            ulong peer = Multiplayer.PeerFor(target);
            var attacker = Multiplayer.CreatureObject(fsm.GameObject);
            int id = attacker != null ? Multiplayer.ObjectId(attacker) : 0;
            if (peer == 0 || id == 0) return false;
            float damage = StateDamage(__0, target);
            if (!(damage < 0f) || damage < -10000f) return false;
            float reach = Reach(attacker);
            uint seq = ++_meleeSeq; if (seq == 0) seq = ++_meleeSeq;
            Dictionary<uint, PendingMelee> pending;
            if (!Melee.TryGetValue(peer, out pending)) { pending = new Dictionary<uint, PendingMelee>(); Melee.Add(peer, pending); }
            Prune(pending);
            pending[seq] = new PendingMelee { Attacker = attacker.name, Damage = damage, Reach = reach, At = Time.unscaledTime };
            Multiplayer.Send(MeleeHitKind, w => { w.Write(seq); w.Write(id); w.Write(damage); w.Write(reach); }, peer);
            HostSaveCombat.Activity();
            if (!_meleeLogged) { _meleeLogged = true; Log.Always("[Multiplayer] Creature melee on guests is checked on the guest's side"); }
            if (Log.Verbose) Log.Info("[Multiplayer] Melee " + seq + ": " + attacker.name + " bites player " + peer + " (" + damage.ToString("0.0") + ", reach " + reach.ToString("0.00") + " m), asking the guest");
            return true;
        }
        // Native CapturePresenceDamagePatch already holds the SetFsmFloat value executed just before this SendEvent (vanilla order);
        // the state scan is only the fallback for a presence without a captured value.
        static float StateDamage(SendEvent send, GameObject target)
        {
            var captured = target.GetComponent(_presence);
            float pending = captured != null ? _pendingDamage(captured) : 0f;
            if (pending < 0f) return pending;
            var state = send.State;
            if (state != null && state.Actions != null)
                foreach (var action in state.Actions)
                {
                    var set = action as SetFsmFloat;
                    if (set == null || !set.Enabled || set.setValue == null || set.variableName == null || set.variableName.Value != "Damage"
                        || set.fsmName == null || set.fsmName.Value != "Bodypart" || send.Fsm.GetOwnerDefaultTarget(set.gameObject) != target) continue;
                    return set.setValue.Value;
                }
            return 0f;
        }
        static float Reach(GameObject attacker)
        {
            var ray = attacker.transform.Find("AttackRaycast");
            var sensor = ray != null ? ray.GetComponent<Micosmo.SensorToolkit.RaySensor>() : null;
            return sensor != null && sensor.Length > .1f ? Mathf.Min(sensor.Length, 10f) : DefaultReach;
        }
        static void Prune(Dictionary<uint, PendingMelee> pending)
        {
            if (pending.Count < 64) return;
            float old = Time.unscaledTime - 10f;
            _meleeOld.Clear();
            foreach (var pair in pending) if (pair.Value.At < old) _meleeOld.Add(pair.Key);
            foreach (uint key in _meleeOld) pending.Remove(key);
            _meleeOld.Clear();
            if (pending.Count >= 256) pending.Clear();
        }

        static void ReceiveMelee(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == MeleeHitKind) GuestMelee(from, reader);
            else if (kind == MeleeResultKind) HostMeleeResult(from, reader);
        }

        // Guest: the hit request, checked against what this player sees.
        static void GuestMelee(ulong from, BinaryReader reader)
        {
            if (!Multiplayer.Client || from != 0 || !Multiplayer.WorldReady) return;
            uint seq = reader.ReadUInt32(); int id = reader.ReadInt32(); float damage = reader.ReadSingle(), reach = reader.ReadSingle();
            if (reader.BaseStream.Position != reader.BaseStream.Length || !(damage < 0f) || damage < -10000f || !(reach > 0f) || reach > 20f) return;
            var player = GameRefs.Player;
            if (player == null) return;
            var attacker = Multiplayer.ObjectById(id);
            float distance = -1f; bool hit = false; string why;
            if (!Multiplayer.LocalTargetable) why = "downed";
            else if (!VehicleMeleeProtection.CanHit(player)) why = "protected in vehicle";
            else if (attacker == null) { hit = true; why = "attacker not spawned here, host decides"; }
            else
            {
                Vector3 origin = AttackOrigin(attacker);
                distance = Vector3.Distance(origin, BodyPoint(player, origin));
                hit = distance <= reach + MeleeSlack;
                why = hit ? "in reach" : "out of reach";
            }
            float before = Health(player);
            if (hit) ApplyMelee(player, damage);
            float after = Health(player);
            Multiplayer.Send(MeleeResultKind, w => { w.Write(seq); w.Write(hit); w.Write(distance); w.Write(after); }, 0UL);
            Log.Always("[Multiplayer] Melee " + seq + " from " + (attacker != null ? attacker.name : "#" + id) + ": " + (hit ? "hit" : "dodged") + " (" + why
                + (distance >= 0f ? ", " + distance.ToString("0.00") + " m, reach " + reach.ToString("0.00") + " + " + MeleeSlack.ToString("0.0") : "")
                + "), damage " + damage.ToString("0.0") + ", health " + before.ToString("0.0") + " -> " + after.ToString("0.0"));
        }
        static Vector3 AttackOrigin(GameObject attacker)
        {
            var ray = attacker.transform.Find("AttackRaycast");
            return ray != null ? ray.position : attacker.transform.position + Vector3.up * .25f;
        }
        static Vector3 BodyPoint(GameObject player, Vector3 origin)
        {
            var collider = player.GetComponent<Collider>();
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy ? collider.ClosestPoint(origin) : player.transform.position;
        }
        static float Health(GameObject player)
        {
            var fsm = FsmUtil.Find(player, "Health");
            var value = fsm != null ? fsm.FsmVariables.FindFsmFloat("Health") : null;
            return value != null ? value.Value : float.NaN;
        }
        // Exactly the vanilla bite: Bodypart.Damage, then Damage to every enabled FSM on the player (Bodypart takes the health,
        // DamageEffectSound or DamageEffectSound_InCar plays the hurt sound, the red flash and the camera kick).
        static void ApplyMelee(GameObject player, float damage)
        {
            var body = FsmUtil.Find(player, "Bodypart");
            var value = body != null ? body.FsmVariables.FindFsmFloat("Damage") : null;
            if (value == null) return;
            value.Value = damage;
            player.GetComponents(_meleeFsms);
            var fsms = _meleeFsms.ToArray(); _meleeFsms.Clear();
            foreach (var f in fsms) if (f != null && f.enabled) f.SendEvent("Damage");
        }
        // Guest: native ApplyPlayerDamage fires only the Bodypart FSM (its own SendEvent to DamageEffectSound is disabled in the
        // prefab, vanilla attackers send that themselves), so forwarded hits were silent. Add the hurt effects.
        static void AfterPlayerDamage(float __0)
        {
            if (!(__0 < 0f) || !Multiplayer.LocalTargetable) return;
            var player = GameRefs.Player;
            if (player == null) return;
            player.GetComponents(_meleeFsms);
            var fsms = _meleeFsms.ToArray(); _meleeFsms.Clear();
            foreach (var f in fsms)
                if (f != null && f.enabled && (f.FsmName == "DamageEffectSound" || f.FsmName == "DamageEffectSound_InCar")) f.SendEvent("Damage");
        }

        // Host: the guest's verdict. Its health itself stays guest-owned (reported by the native player save).
        static void HostMeleeResult(ulong from, BinaryReader reader)
        {
            if (!Multiplayer.Hosting || from == 0) return;
            uint seq = reader.ReadUInt32(); bool hit = reader.ReadBoolean(); float distance = reader.ReadSingle(), health = reader.ReadSingle();
            if (reader.BaseStream.Position != reader.BaseStream.Length) return;
            Dictionary<uint, PendingMelee> pending; PendingMelee melee;
            if (!Melee.TryGetValue(from, out pending) || !pending.TryGetValue(seq, out melee)) return;
            pending.Remove(seq);
            if (hit) HostSaveCombat.Activity();
            if (Log.Verbose) Log.Info("[Multiplayer] Melee " + seq + ": " + melee.Attacker + " -> player " + from + " " + (hit ? "hit" : "dodged") + " on the guest ("
                + (distance >= 0f ? distance.ToString("0.00") + " m of " + melee.Reach.ToString("0.00") + " m reach" : "no distance") + "), damage "
                + melee.Damage.ToString("0.0") + ", guest health " + health.ToString("0.0") + ", round trip " + (Time.unscaledTime - melee.At).ToString("0.00") + " s");
        }
    }
}
