using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerTransport
    {
        sealed class Closed { }
        static readonly ConditionalWeakTable<object, Closed> Disposed = new ConditionalWeakTable<object, Closed>();
        static float _socketWarning;
        internal static void Install()
        {
            var type = Multiplayer.Type("Net.UdpTransport"); var owner = Multiplayer.PatchOwner;
            if (!Patches.Patch(owner, AccessTools.Method(type, "Dispose"), prefix: AccessTools.Method(typeof(MultiplayerTransport), "Closing"))
                || !Patches.Patch(owner, AccessTools.Method(type, "Update"), prefix: AccessTools.Method(typeof(MultiplayerTransport), "Open"), transpiler: AccessTools.Method(typeof(MultiplayerTransport), "AfterDispatch"))
                || !Patches.Patch(owner, AccessTools.Method(type, "HandlePacket"), prefix: AccessTools.Method(typeof(MultiplayerTransport), "Packet"))
                || !Patches.Patch(owner, AccessTools.Method(type, "SendTo"), prefix: AccessTools.Method(typeof(MultiplayerTransport), "Open"), finalizer: AccessTools.Method(typeof(MultiplayerTransport), "SocketFailure")))
                throw new InvalidOperationException("MP UDP lifecycle hooks failed");
        }
        static bool Open(object __instance) { Closed closed; return !Disposed.TryGetValue(__instance, out closed); }
        static Exception SocketFailure(Exception __exception)
        {
            var failure = __exception as System.Net.Sockets.SocketException;
            if (failure == null) return __exception;
            if (Time.unscaledTime >= _socketWarning) { _socketWarning = Time.unscaledTime + 5f; Log.Warn("[Multiplayer] UDP send deferred: " + failure.SocketErrorCode); }
            return null;
        }
        static bool Packet(object __instance, System.Net.IPEndPoint __0, byte[] __1)
        {
            if (!Open(__instance)) return false;
            if (__0 != null && __1 != null && __1.Length > 0) return true;
            Log.Warn("[Multiplayer] UDP: discarded an incomplete packet (missing endpoint or payload)");
            return false;
        }
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
