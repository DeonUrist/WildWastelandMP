using System;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Core
{
    // MP's creature damage handler drops the sender; inspect at Handle, before its unchanged damage dispatch.
    internal static partial class MultiplayerCombat
    {
        static byte _creatureDamage;
        internal static void Install()
        {
            _creatureDamage = Convert.ToByte(Enum.Parse(Multiplayer.Type("Net.MessageType"), "CreatureDamage"));
            Hooks.ProcessEvent.Prefix(Multiplayer.PatchOwner, BeforeEvent);
            InstallCars();
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(FsmFloatAdd), "OnEnter"), prefix: AccessTools.Method(typeof(MultiplayerCombat), "Sanity")))
                throw new InvalidOperationException("MP sanity damage forwarding hook failed");
            Multiplayer.Message += ReceiveSanity;
        }
        static bool Sanity(FsmFloatAdd __instance)
        {
            if (!Multiplayer.Hosting || __instance.fsmName == null || __instance.fsmName.Value != "SanityAdd"
                || __instance.variableName == null || __instance.variableName.Value != "SanityAdd") return true;
            var target = __instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject);
            if (!Multiplayer.IsRemotePlayer(target)) return true;
            float amount = __instance.add.Value; if (__instance.perSecond) amount *= Time.deltaTime;
            ulong peer = Multiplayer.PeerFor(target);
            if (amount > 0f && !float.IsInfinity(amount)) Multiplayer.Send(116, w => w.Write(amount), peer);
            if (!__instance.everyFrame) __instance.Finish(); return false;
        }
        static void ReceiveSanity(ulong from, byte kind, BinaryReader reader)
        {
            if (kind != 116 || !Multiplayer.Client || from != 0 || !Multiplayer.WorldReady) return;
            float amount = reader.ReadSingle(); if (!(amount > 0f && amount <= 1000f) || reader.BaseStream.Position != reader.BaseStream.Length || GameRefs.Player == null) return;
            var fsm = FsmUtil.Find(GameRefs.Player, "SanityAdd"); var value = fsm != null ? fsm.FsmVariables.FindFsmFloat("SanityAdd") : null;
            if (value != null) value.Value = Mathf.Min(200f, value.Value + amount);
        }
        static bool BeforeEvent(Fsm fsm, FsmEvent ev, FsmEventData data)
        {
            if (!Multiplayer.Active || ev == null || ev.Name != "Damage" || fsm == null || (fsm.Name != "Bodypart" && fsm.Name != "Health")) return true;
            if (fsm.Name == "Bodypart" && fsm.GameObject != null && (fsm.GameObject.CompareTag("vehPart") || fsm.GameObject.CompareTag("vehPartRemoved")))
            {
                var damage = fsm.Variables.FindFsmFloat("Damage");
                if (damage != null && damage.Value < 0f && MultiplayerParts.HitPart(fsm.GameObject, damage.Value, 2, Vector3.zero)) return false;
            }
            if (!Multiplayer.Client) return true;
            // SendEvent.OnEnter has already forwarded vanilla hits. WW Replay forwards explicitly.
            // Player damage/revival and native health snapshots do not target a replicated creature's damage event.
            return !Multiplayer.Creature(fsm.GameObject);
        }
        internal static bool BeforeNative(ulong from, byte[] packet)
        {
            if (!Multiplayer.Present || packet[0] != _creatureDamage) return true;
            if (!Multiplayer.Hosting || !Multiplayer.WorldReady || from == 0 || packet.Length < 10 || packet.Length > 1024) return false;
            var player = Multiplayer.PlayerFor(from);
            if (player == null) return false;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(packet, false)))
                {
                    reader.ReadByte(); int id = reader.ReadInt32(); string path = reader.ReadString(); float damage = reader.ReadSingle();
                    if (reader.BaseStream.Position != reader.BaseStream.Length || path.Length > 512 || path.IndexOf('\0') >= 0
                        || !(damage < 0f) || float.IsInfinity(damage)) return false;
                    var root = Multiplayer.ObjectById(id);
                    if (root == null || !root.activeInHierarchy || !Multiplayer.Creature(root)) return false;
                    // Legacy MP hits carry no shot origin/weapon. WW projectile ranges cannot validate them;
                    // in particular the creature may have moved since the client's collision. WW shots use kind 128.
                    float distance = Vector3.Distance(root.transform.position, player.transform.position);
                    if (Mathf.Abs(damage) > 10000f || distance > 1000f || !Multiplayer.RemoteTargetable(player))
                    {
                        if (Log.Verbose) Log.Info("[Multiplayer] Rejected legacy creature hit: player " + from + ", distance " + distance.ToString("0.0") + ", targetable " + Multiplayer.RemoteTargetable(player));
                        return false;
                    }
                    var part = path.Length == 0 ? root.transform : root.transform.Find(path);
                    if (part == null || !part.IsChildOf(root.transform)) return false;
                    var bodypart = FsmUtil.Find(part.gameObject, "Bodypart");
                    var health = FsmUtil.Find(root, "Health");
                    var hp = health != null ? health.FsmVariables.FindFsmFloat("Health") : null;
                    if (bodypart == null || bodypart.FsmVariables.FindFsmFloat("Damage") == null || hp == null || hp.Value < 1f) return false;
                    // Notify before the native event can kill/destroy the victim, using this peer rather than the host.
                    HostSaveCombat.Activity();
                    var npc = Services.Npc;
                    if (npc != null)
                        try { npc.Hurt(part.gameObject, player); }
                        catch (Exception e) { Log.Warn("[Multiplayer] NPC hurt observation: " + e.Message); }
                    if (Log.Verbose) Log.Info("[Multiplayer] Creature hit: player " + from + " -> " + root.name + "/" + path + ", damage " + damage.ToString("0.0"));
                    return true;
                }
            }
            catch (Exception e) { Log.Warn("[Multiplayer] Rejected creature hit: " + e.Message); return false; }
        }
    }
}
