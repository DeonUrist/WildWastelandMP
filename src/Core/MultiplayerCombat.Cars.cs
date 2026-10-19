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
        }
        // Remote corpse roots are positioned by the host. They must share the native temporary car-passage
        // rule with the living creature, or a kill replaces the passable victim with a stationary obstacle.
        static bool PassageTarget(object tracked)
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
        static bool BeforeClientCarHit(bool __1)
        { return !Multiplayer.Active || CarImpact == null || !__1; }
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
