using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEngine;
using HarmonyLib;

namespace WildWasteland.Core
{
    // Transient projectiles/effects have one gameplay copy on the host; guests receive inert presentation copies.
    internal static class MultiplayerEffects
    {
        sealed class Effect { internal GameObject Object; internal string Prefab; internal bool Inert, Presentation; internal float Until; }
        static readonly Dictionary<uint, Effect> Effects = new Dictionary<uint, Effect>();
        static readonly HashSet<uint> Removed = new HashSet<uint>();
        static readonly List<uint> Gone = new List<uint>();
        static Func<string, string, GameObject> _prefab;
        static uint _id;
        static ulong _epoch;
        internal static void Install()
        {
            _prefab = MultiplayerAccess.Call<Func<string, string, GameObject>>(Multiplayer.Type("Net.WorldObjects"), "FindPrefab", typeof(string), typeof(string));
            Multiplayer.Message += Receive;
            Hooks.CreateObjectEnter.Postfix(Multiplayer.PatchOwner, Created);
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Net.WorldObjects"), "OnThrown"), prefix: AccessTools.Method(typeof(MultiplayerEffects), "Thrown")))
                throw new InvalidOperationException("MP explosive authority hook failed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Net.WorldObjects"), "OnLocalThrow"), postfix: AccessTools.Method(typeof(MultiplayerEffects), "LocalThrow")))
                throw new InvalidOperationException("MP thrown presentation hook failed");
            Scheduler.Interval(Multiplayer.PatchOwner, "Transient MP effects", .1f, Tick);
        }
        internal static void Track(GameObject obj, string prefab, bool inert = false)
        {
            if (!Multiplayer.Hosting || obj == null) return;
            Epoch(); if (Effects.Count >= 256) return;
            foreach (var effect in Effects.Values) if (effect.Object == obj) return;
            var tracked = new Effect { Object = obj, Prefab = prefab, Inert = inert };
            Effects[++_id] = tracked;
            Describe(_id, tracked, true);
        }
        static void Created(HutongGames.PlayMaker.Actions.CreateObject action)
        {
            if (!Multiplayer.Active || action.Fsm == null || action.storeObject == null) return;
            string fsm = action.Fsm.Name;
            if (fsm != "Damage Ranged" && fsm != "Explosion" && fsm != "Health") return;
            var obj = action.storeObject.Value; if (obj == null) return;
            if (fsm == "Damage Ranged" ? obj.GetComponent<Rigidbody>() == null : !obj.name.StartsWith("Explosion", StringComparison.Ordinal)) return;
            if (Multiplayer.Client)
            {
                foreach (var component in obj.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (component.FsmName == "ExplosionDamage" || component.FsmName == "ExplosionRadius") component.enabled = false;
                foreach (var collider in obj.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                return;
            }
            Track(obj, obj.name.Replace("(Clone)", "").Trim(), fsm != "Damage Ranged");
        }
        static bool Thrown(ulong __0, BinaryReader __1)
        {
            if (!Multiplayer.Active) return true;
            if (Multiplayer.Client) return false;
            string name = __1.ReadString(); var world = new Vector3(__1.ReadSingle(), __1.ReadSingle(), __1.ReadSingle());
            var rotation = new Quaternion(__1.ReadSingle(), __1.ReadSingle(), __1.ReadSingle(), __1.ReadSingle());
            var velocity = new Vector3(__1.ReadSingle(), __1.ReadSingle(), __1.ReadSingle()); var player = Multiplayer.PlayerFor(__0);
            float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
            if (!Multiplayer.WorldReady || player == null || __0 == 0 || name.Length > 64 || !name.Contains("_explode") || float.IsNaN(world.sqrMagnitude) || float.IsInfinity(world.sqrMagnitude)
                || !(magnitude > .9f && magnitude < 1.1f) || float.IsNaN(velocity.sqrMagnitude) || velocity.sqrMagnitude > 1000000f || (Origin.Scene(world) - player.transform.position).sqrMagnitude > 225f) return false;
            var prefab = _prefab(name, null); if (prefab == null) return false;
            var obj = UnityEngine.Object.Instantiate(prefab, Origin.Scene(world), rotation); var body = obj.GetComponent<Rigidbody>();
            if (body != null) body.velocity = velocity;
            Track(obj, name); return false;
        }
        static void LocalThrow(GameObject __0)
        {
            if (__0 == null) return;
            if (Multiplayer.Hosting) Track(__0, __0.name.Replace("(Clone)", "").Trim());
            else if (Multiplayer.Client) Scheduler.StartCoroutine(RetireLocalThrow(__0));
        }
        static IEnumerator RetireLocalThrow(GameObject thrown)
        {
            // Native SendThrowNextFrame must read the launch pose/velocity before the guest relinquishes its copy.
            if (thrown != null)
            {
                foreach (var fsm in thrown.GetComponentsInChildren<PlayMakerFSM>(true)) fsm.enabled = false;
                foreach (var collider in thrown.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            }
            yield return null; yield return null;
            if (thrown != null) { thrown.SetActive(false); UnityEngine.Object.Destroy(thrown); }
        }
        static void Epoch()
        {
            if (_epoch == MultiplayerParts.Epoch) return;
            foreach (var effect in Effects.Values) if (effect.Presentation && effect.Object != null) UnityEngine.Object.Destroy(effect.Object);
            Effects.Clear(); Removed.Clear(); _epoch = MultiplayerParts.Epoch; _id = 0;
        }
        static void Describe(uint id, Effect effect, bool reliable)
        {
            if (effect.Object == null || _epoch == 0) return;
            var position = Origin.World(effect.Object.transform.position); var rotation = effect.Object.transform.rotation;
            Multiplayer.Send(114, w => { w.Write(_epoch); w.Write(id); w.Write(effect.Prefab); w.Write(effect.Inert); w.Write(position.x); w.Write(position.y); w.Write(position.z); w.Write(rotation.x); w.Write(rotation.y); w.Write(rotation.z); w.Write(rotation.w); }, reliable: reliable);
        }
        static void Tick()
        {
            Epoch(); if (!Multiplayer.Active || _epoch == 0) return;
            Gone.Clear();
            foreach (var pair in Effects)
            {
                var effect = pair.Value;
                if (effect.Object == null || !effect.Object.activeInHierarchy || Multiplayer.Client && Time.unscaledTime >= effect.Until)
                {
                    if (Multiplayer.Hosting) Multiplayer.Send(115, w => { w.Write(_epoch); w.Write(pair.Key); });
                    else if (effect.Object != null) UnityEngine.Object.Destroy(effect.Object);
                    Gone.Add(pair.Key); continue;
                }
                if (!Multiplayer.Hosting) continue;
                // Repeating the complete small descriptor also repairs a lost first visual packet and supports late join.
                Describe(pair.Key, effect, false);
            }
            foreach (uint id in Gone) Effects.Remove(id);
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if ((kind != 114 && kind != 115) || !Multiplayer.Client || from != 0) return;
            Epoch(); if (reader.ReadUInt64() != _epoch || _epoch == 0) return;
            uint id = reader.ReadUInt32(); Effect effect;
            if (kind == 115)
            { if (reader.BaseStream.Position != reader.BaseStream.Length) return; Removed.Add(id); if (Effects.TryGetValue(id, out effect)) { if (effect.Object != null) UnityEngine.Object.Destroy(effect.Object); Effects.Remove(id); } return; }
            if (Removed.Contains(id)) return;
            string name = reader.ReadString(); bool inert = reader.ReadBoolean();
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
            if (name.Length > 128 || float.IsNaN(position.sqrMagnitude) || float.IsInfinity(position.sqrMagnitude) || !(magnitude > .9f && magnitude < 1.1f) || reader.BaseStream.Position != reader.BaseStream.Length) return;
            if (!Effects.TryGetValue(id, out effect))
            {
                if (Effects.Count >= 256) return;
                var prefab = _prefab(name, null); if (prefab == null) return;
                var obj = UnityEngine.Object.Instantiate(prefab, Origin.Scene(position), rotation);
                // Preserve visual lifetime/particles/audio, but never simulate hits or a second explosion on a guest.
                foreach (var fsm in obj.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (inert ? fsm.FsmName == "ExplosionRadius" || fsm.FsmName == "ExplosionDamage" : true) fsm.enabled = false;
                foreach (var collider in obj.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var body in obj.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                effect = new Effect { Object = obj, Prefab = name, Inert = inert, Presentation = true }; Effects[id] = effect;
                obj.SetActive(true);
            }
            if (effect.Object != null && !effect.Inert) effect.Object.transform.SetPositionAndRotation(Origin.Scene(position), rotation);
            effect.Until = Time.unscaledTime + 3f;
        }
    }
}
