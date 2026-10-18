using System;
using System.IO;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Patrols
{
    internal static partial class Ram
    {
        static readonly HashSet<int> BlastCars = new HashSet<int>();
        internal static void StartNetwork()
        {
            if (!Multiplayer.Present) return;
            Multiplayer.Message -= ReceiveImpact; Multiplayer.Message += ReceiveImpact;
            Multiplayer.SessionChanged -= ResetNetwork; Multiplayer.SessionChanged += ResetNetwork;
            MultiplayerCombat.CarImpact -= GuestCarImpact; MultiplayerCombat.CarImpact += GuestCarImpact;
            ResetNetwork();
        }
        internal static void StopNetwork()
        {
            Multiplayer.Message -= ReceiveImpact; Multiplayer.SessionChanged -= ResetNetwork;
            MultiplayerCombat.CarImpact -= GuestCarImpact;
            ResetNetwork();
        }
        static void ResetNetwork()
        {
            _next.Clear(); _pendingAt = -1f;
            if (_pausedMovement != null) { _pausedMovement.enabled = true; _pausedMovement.Fsm.RestartOnEnable = true; }
            _pausedMovement = null;
        }
        internal static void SceneReset() { ResetNetwork(); }
        static bool RemoteDriven(Fsm fsm)
        {
            var car = fsm != null && fsm.GameObject != null ? fsm.GameObject.transform.parent : null;
            return Multiplayer.Hosting && car != null && Multiplayer.OwnedElsewhere(car.gameObject);
        }
        internal static int RemoteKind(GameObject player, Transform hit)
        {
            if (player == null || hit == null || !Multiplayer.RemoteTargetable(player)) return 0;
            var car = Multiplayer.RemoteCar(player);
            if (car != null && hit.IsChildOf(car.transform)) return 2;
            return Multiplayer.IsRemotePlayer(hit.gameObject) && Multiplayer.PeerFor(hit.gameObject) == Multiplayer.PeerFor(player) ? car != null ? 2 : 1 : 0;
        }
        static void SendImpact(GameObject player, float amount, Vector3 shove, string source)
        {
            ulong peer = Multiplayer.PeerFor(player);
            if (peer == 0) return;
            if (source.Length > 128) source = source.Substring(0, 128);
            Multiplayer.Send(60, w => { w.Write(amount); w.Write(shove.x); w.Write(shove.y); w.Write(shove.z); w.Write(source); }, peer);
            if (Log.Verbose) PatrolsModule.Verbose("Ram: " + source + " -> player " + peer + ", " + amount + " damage");
        }
        static void ReceiveImpact(ulong from, byte kind, BinaryReader reader)
        {
            if (kind != 60 || !Multiplayer.Client || from != 0 || !Multiplayer.WorldReady) return;
            float amount = reader.ReadSingle(); var shove = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); string source = reader.ReadString();
            if (reader.BaseStream.Position != reader.BaseStream.Length || !(amount >= 0f) || float.IsInfinity(amount)
                || float.IsNaN(shove.sqrMagnitude) || float.IsInfinity(shove.sqrMagnitude) || source.Length > 128 || !Multiplayer.LocalTargetable || PlayerRef.GodMode) return;
            var player = PlayerRef.Player; if (player == null) return;
            if (amount >= 1f && !HurtPlayer(amount, "Ram from " + source)) return;
            if (PlayerRef.PlayerCar == null) Push(player, FsmUtil.Find(player.gameObject, "Movement"), shove);
        }
        internal static void RemoteBlast(Vector3 at, float radius, float damage, string source)
        {
            if (!Multiplayer.Hosting || radius <= 0f) return;
            BlastCars.Clear();
            for (int i = 0; i < Multiplayer.RemoteCount; i++)
            {
                var player = Multiplayer.RemoteAt(i); if (!Multiplayer.RemoteTargetable(player)) continue;
                float distance = Vector3.Distance(player.transform.position + Vector3.up * .9f, at);
                var car = Multiplayer.RemoteCar(player); var body = car != null ? car.GetComponent<Rigidbody>() : null;
                if (body != null) distance = Mathf.Min(distance, Mathf.Max(0f, Vector3.Distance(body.worldCenterOfMass, at) - 2f));
                if (distance <= radius) SendImpact(player, damage * Mathf.Lerp(1f, .33f, distance / radius), Vector3.zero, "Rider " + source + " blast");
                if (distance <= radius && body != null && BlastCars.Add(car.GetInstanceID()) && car != PlayerRef.PlayerCar)
                    if (!MultiplayerForces.Explosion(car, body.mass * 5f, at, radius * 1.5f, .5f, ForceMode.Impulse) && !body.isKinematic) body.AddExplosionForce(body.mass * 5f, at, radius * 1.5f, .5f, ForceMode.Impulse);
            }
        }
        static void GuestCarImpact(GameObject car, GameObject victim, float speed, ulong peer)
        {
            if (!Multiplayer.Hosting || !PatrolsModule.Running) return;
            var id = new ImpactKey { Car = car.GetInstanceID(), Target = victim.transform.GetInstanceID() };
            float next; if (_next.TryGetValue(id, out next) && Time.time < next) return;
            var part = FsmUtil.Find(victim, "Bodypart"); var health = FsmUtil.Find(victim, "Health");
            var hp = health != null ? health.FsmVariables.FindFsmFloat("Health") : null;
            var damage = part != null ? part.FsmVariables.FindFsmFloat("Damage") : null;
            if (damage == null || hp == null || hp.Value < 1f) return;
            var attackObject = car.transform.Find("CarAttack");
            var attack = attackObject != null ? FsmUtil.Find(attackObject.gameObject, "CarAttack") : null;
            var bumper = attack != null ? attack.FsmVariables.FindFsmFloat("BumperDamage") : null;
            float value = speed * (bumper != null ? bumper.Value : -3f) * PatrolsModule.NpcRamDamageMultiplier.Value;
            if (!(value < 0f) || float.IsInfinity(value)) return;
            _next[id] = Time.time + 1f;
            var npc = Services.Npc; var player = Multiplayer.PlayerFor(peer);
            if (npc != null && player != null)
                try { npc.Hurt(victim, player); } catch (Exception e) { Log.Warn("[Patrols] Ram hurt observation: " + e.Message); }
            damage.Value = value; part.SendEvent("Damage");
            if (Log.Verbose) PatrolsModule.Verbose("Ram: player " + peer + " in " + car.name + " -> " + victim.name + ", " + (-value) + " damage");
        }
    }
}
