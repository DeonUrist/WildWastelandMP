using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static partial class MultiplayerCombat
    {
        internal static event Action<GameObject, GameObject, float, ulong> CarImpact;
        static Func<object, byte> _passageKind;
        static Func<object, string> _passagePrefab;
        static Func<object, bool> _ragdoll, _carried, _passageOwned;
        struct Passage { internal bool Value; internal float Until; }
        // ClearTheWay runs every frame for a moving owned car over every tracked object; the four reflection reads and the
        // prefab name test per Kind-0 item are cached for a second (carried/owned/prefab change rarely, kind never).
        static readonly Dictionary<object, Passage> PassageCache = new Dictionary<object, Passage>();
        static float _passageNext;
        static void InstallCars()
        {
            var world = Multiplayer.Type("Net.WorldObjects");
            var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
            _passageKind = MultiplayerAccess.Get<byte>(tracked, "Kind"); _ragdoll = MultiplayerAccess.Get<bool>(tracked, "Ragdoll"); _carried = MultiplayerAccess.Get<bool>(tracked, "Carried");
            _passageOwned = MultiplayerAccess.Get<bool>(tracked, "OwnedLocally");
            _passagePrefab = MultiplayerAccess.Get<string>(tracked, "Prefab");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "SendCarHit"), prefix: AccessTools.Method(typeof(MultiplayerCombat), "BeforeCarHit"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "OnClientHit"), prefix: AccessTools.Method(typeof(MultiplayerCombat), "BeforeClientCarHit")))
                throw new InvalidOperationException("MP car damage hooks could not be installed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "ClearTheWay"), transpiler: AccessTools.Method(typeof(MultiplayerCombat), "PassageFilter")))
                throw new InvalidOperationException("MP corpse car-passage hook failed");
            Hooks.SendEventEnter.Prefix(Multiplayer.PatchOwner, ForwardCarHit);
            Multiplayer.Message += ReceiveCarHit;
            Multiplayer.SessionChanged += PassageCache.Clear;
        }
        // Remote corpse roots are positioned by the host. They must share the native temporary car-passage
        // rule with the living creature, or a kill replaces the passable victim with a stationary obstacle.
        static bool PassageTarget(object tracked)
        {
            Passage cached; float now = Time.unscaledTime;
            if (PassageCache.TryGetValue(tracked, out cached) && now < cached.Until) return cached.Value;
            if (now >= _passageNext) { _passageNext = now + 30f; if (PassageCache.Count > 8192) PassageCache.Clear(); }
            bool value = ComputePassageTarget(tracked);
            PassageCache[tracked] = new Passage { Value = value, Until = now + 1f };
            return value;
        }
        static bool ComputePassageTarget(object tracked)
        {
            if (_passageKind(tracked) == 3) return true;
            if (_passageKind(tracked) != 0 || _carried(tracked) || _passageOwned(tracked)) return false;
            // MP's Ragdoll flag requires a Food FSM; native animal remains have Carcasse instead.
            string prefab = _passagePrefab(tracked);
            return _ragdoll(tracked) || prefab != null && prefab.IndexOf("_Dead", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        static IEnumerable<CodeInstruction> PassageFilter(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions); int matches = 0;
            var kind = AccessTools.Field(Multiplayer.Type("Net.WorldObjects+Tracked"), "Kind");
            for (int i = 0; i + 2 < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldfld && Equals(code[i].operand, kind) && code[i + 1].LoadsConstant(3)
                    && (code[i + 2].opcode == OpCodes.Bne_Un || code[i + 2].opcode == OpCodes.Bne_Un_S))
                {
                    code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(MultiplayerCombat), "PassageTarget");
                    code[i + 1].labels.AddRange(code[i + 2].labels); code[i + 1].blocks.AddRange(code[i + 2].blocks);
                    code[i + 1].opcode = OpCodes.Brfalse; code[i + 1].operand = code[i + 2].operand; code.RemoveAt(i + 2); matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Unsupported MP ClearTheWay creature filter");
            return code;
        }
        static bool BeforeClientCarHit(GameObject __0, bool __1)
        {
            if (!Multiplayer.Active || CarImpact == null) return true;
            if (__1) return false;
            // Native resolves the creature with FirstOrDefault(IsChildOf) over every tracked object, on every Damage send with a
            // GameObject target (each melee swing, each player effect send). TrackedAncestor is the parent walk it already has.
            if (!Multiplayer.Client || __0 == null) return true;
            GameObject creature = null;   // native: any tracked creature the part is under, even past a tracked item parented inside it
            for (var t = __0.transform; t != null && creature == null; t = t.parent) creature = Multiplayer.CreatureObject(t.gameObject);
            if (creature == null) return false;   // native finds nothing either
            var bodypart = FsmUtil.Find(__0, "Bodypart");
            var value = bodypart != null ? bodypart.FsmVariables.FindFsmFloat("Damage") : null;
            float damage = value != null ? value.Value : 0f;
            int id = Multiplayer.ObjectId(creature); string path = RelativePath(creature.transform, __0.transform);
            if (id == 0) return false;
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            { w.Write(_creatureDamage); w.Write(id); w.Write(path); w.Write(damage); MultiplayerReliable.Send(stream.ToArray(), null); }
            if (Log.Verbose) Log.Info("[Multiplayer] Hit " + creature.name + "/" + path + " (" + damage.ToString("0.0") + ")");
            return false;
        }
        static string RelativePath(Transform root, Transform part)
        {
            string text = "";
            for (var t = part; t != null && t != root; t = t.parent) text = text.Length == 0 ? t.name : t.name + "/" + text;
            return text;
        }
        static bool BeforeCarHit(object __0, object __1, float __2)
        {
            if (!Multiplayer.Active || CarImpact == null) return true;
            RequestCarHit(Multiplayer.TrackedObject(__0), Multiplayer.TrackedObject(__1), __2);
            return false;
        }
        static bool ForwardCarHit(SendEvent action)
        {
            if (!Multiplayer.Client || CarImpact == null || action.Fsm == null || action.Fsm.Name != "CarAttack"
                || action.sendEvent == null || action.sendEvent.Name != "Damage" || action.eventTarget == null) return true;
            var car = action.Fsm.GameObject != null ? action.Fsm.GameObject.transform.parent : null;
            var target = action.Fsm.GetOwnerDefaultTarget(action.eventTarget.gameObject);
            var rb = car != null ? car.GetComponent<Rigidbody>() : null;
            if (rb != null) RequestCarHit(car.gameObject, target, rb.velocity.magnitude);
            return true; // native forwarding is suppressed above; Core's client creature event guard owns local damage suppression
        }
        static void RequestCarHit(GameObject car, GameObject target, float speed)
        {
            if (!Multiplayer.Client || !Multiplayer.WorldReady || car == null || target == null || target.transform.IsChildOf(car.transform)) return;
            var creature = Multiplayer.CreatureObject(target);
            int carId = Multiplayer.ObjectId(car), victimId = Multiplayer.ObjectId(creature);
            if (carId == 0 || victimId == 0 || !(speed > 5f) || float.IsInfinity(speed)) return;
            Multiplayer.Send(61, w => { w.Write(carId); w.Write(victimId); w.Write(speed); });
        }
        static void ReceiveCarHit(ulong from, byte kind, BinaryReader reader)
        {
            if (kind != 61 || !Multiplayer.Hosting || !Multiplayer.WorldReady || from == 0 || CarImpact == null) return;
            int carId = reader.ReadInt32(), victimId = reader.ReadInt32(); float speed = reader.ReadSingle();
            if (reader.BaseStream.Position != reader.BaseStream.Length || !(speed > 5f && speed <= 200f)) return;
            var car = Multiplayer.ObjectById(carId); var victim = Multiplayer.ObjectById(victimId);
            if (car == null || victim == null || !car.activeInHierarchy || !victim.activeInHierarchy || Multiplayer.PlayerFor(from) == null
                || Multiplayer.OwnerOf(car) != from || car.GetComponent("VehicleController") == null || !Multiplayer.Creature(victim)
                || victim.transform.IsChildOf(car.transform)) return;
            float reach = 8f + speed * .2f;
            if ((victim.transform.position - car.transform.position).sqrMagnitude > reach * reach) return;
            HostSaveCombat.Activity();
            CarImpact(car, victim, speed, from);
        }
    }
}
