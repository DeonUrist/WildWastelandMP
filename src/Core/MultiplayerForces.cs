using System;
using System.IO;
using UnityEngine;
using HutongGames.PlayMaker.Actions;

namespace WildWasteland.Core
{
    internal static class MultiplayerForces
    {
        static readonly Collider[] Hits = new Collider[2048];
        static Func<Explosion, GameObject, bool> _allows;
        internal static void Install()
        {
            Multiplayer.Message += Receive;
            _allows = MultiplayerAccess.Call<Func<Explosion, GameObject, bool>>(typeof(Explosion), "ShouldApplyForce", typeof(GameObject));
            Hooks.ExplosionDo.Prefix(Multiplayer.PatchOwner, NativeExplosion);
        }
        static bool NativeExplosion(Explosion action)
        {
            if (!Multiplayer.Active || action.Fsm.Name == "Tornado") return true;
            if (Multiplayer.Client) return false;
            int count = Physics.OverlapSphereNonAlloc(action.center.Value, action.radius.Value, Hits);
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i]; Hits[i] = null;
                if (hit != null && hit.GetComponent<Rigidbody>() != null && _allows(action, hit.gameObject))
                    Explosion(hit.gameObject, action.force.Value, action.center.Value, action.radius.Value, action.upwardsModifier.Value, action.forceMode);
            }
            return true;
        }
        internal static bool Explosion(GameObject target, float force, Vector3 at, float radius, float up, ForceMode mode)
        {
            if (!Multiplayer.Hosting || target == null) return false;
            ulong peer = Multiplayer.IsRemotePlayer(target) ? Multiplayer.PeerFor(target) : Multiplayer.OwnerOf(target);
            if (peer == 0) return false;
            int id = Multiplayer.IsRemotePlayer(target) ? 0 : Multiplayer.ObjectId(target); if (id == 0 && !Multiplayer.IsRemotePlayer(target)) return false;
            var world = Origin.World(at);
            Multiplayer.Send(117, w => { w.Write(id); w.Write(force); w.Write(world.x); w.Write(world.y); w.Write(world.z); w.Write(radius); w.Write(up); w.Write((int)mode); }, peer);
            return true;
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind != 117 || !Multiplayer.Client || from != 0 || !Multiplayer.WorldReady) return;
            int id = reader.ReadInt32(); float force = reader.ReadSingle(); var at = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float radius = reader.ReadSingle(), up = reader.ReadSingle(); var mode = (ForceMode)reader.ReadInt32();
            if (reader.BaseStream.Position != reader.BaseStream.Length || !(force >= 0f && force <= 10000000f) || !(radius > 0f && radius <= 20000f)
                || float.IsNaN(at.sqrMagnitude) || float.IsInfinity(at.sqrMagnitude) || float.IsNaN(up) || float.IsInfinity(up) || !Enum.IsDefined(typeof(ForceMode), mode)) return;
            var target = id == 0 ? GameRefs.Player : Multiplayer.ObjectById(id);
            if (target == null || id != 0 && Multiplayer.OwnedElsewhere(target)) return;
            var body = target.GetComponent<Rigidbody>(); if (body != null && !body.isKinematic) body.AddExplosionForce(force, Origin.Scene(at), radius, up, mode);
        }
    }
}
