using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace WildWasteland.Core
{
    internal static class MultiplayerTransport
    {
        sealed class Closed { }
        static readonly ConditionalWeakTable<object, Closed> Disposed = new ConditionalWeakTable<object, Closed>();
        internal static void Install()
        {
            var type = Multiplayer.Type("Net.UdpTransport"); var owner = Multiplayer.PatchOwner;
            if (!Patches.Patch(owner, AccessTools.Method(type, "Dispose"), prefix: AccessTools.Method(typeof(MultiplayerTransport), "Closing"))
                || !Patches.Patch(owner, AccessTools.Method(type, "Update"), prefix: AccessTools.Method(typeof(MultiplayerTransport), "Open"), transpiler: AccessTools.Method(typeof(MultiplayerTransport), "AfterDispatch"))
                || !Patches.Patch(owner, AccessTools.Method(type, "SendTo"), prefix: AccessTools.Method(typeof(MultiplayerTransport), "Open")))
                throw new InvalidOperationException("MP UDP lifecycle hooks failed");
        }
        static bool Open(object __instance) { Closed closed; return !Disposed.TryGetValue(__instance, out closed); }
        static bool Closing(object __instance)
        {
            if (!Open(__instance)) return false;
            Disposed.Add(__instance, new Closed()); return true;
        }
        // A received HostClosing/Bye can call NetSession.Stop -> Dispose from inside HandlePacket.
        // Leave Update immediately after that callback, before Available or delayed queues touch the closed socket.
        static IEnumerable<CodeInstruction> AfterDispatch(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var dispatch = AccessTools.Method(Multiplayer.Type("Net.UdpTransport"), "HandlePacket"); int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(dispatch)) continue;
                count++; var next = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MultiplayerTransport), "Open"));
                yield return new CodeInstruction(OpCodes.Brtrue, next);
                yield return new CodeInstruction(OpCodes.Ret);
                var resume = new CodeInstruction(OpCodes.Nop); resume.labels.Add(next); yield return resume;
            }
            if (count != 2) throw new InvalidOperationException("Unexpected MP UDP dispatch loop: " + count);
        }
    }
}
