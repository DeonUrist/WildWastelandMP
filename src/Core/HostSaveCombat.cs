using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // Observe native detection too: turning NPC AI off must not turn autosave safety off.
    internal static class HostSaveCombat
    {
        sealed class Actor { internal PlayMakerFSM Fsm, Attack; internal FsmGameObject Target; internal FsmFloat Health; }
        static readonly Dictionary<PlayMakerFSM, Actor> Actors = new Dictionary<PlayMakerFSM, Actor>();
        static readonly List<PlayMakerFSM> Dead = new List<PlayMakerFSM>();
        static int _scan;
        static bool _scanning;
        static float _quietUntil;
        internal static bool Unsafe { get; private set; }
        internal static void Install()
        {
            Hooks.FsmEnable.Postfix(Multiplayer.PatchOwner, Track);
            Hooks.ProcessEvent.Prefix(Multiplayer.PatchOwner, Event);
            Hooks.EnterState.Postfix(Multiplayer.PatchOwner, Attack);
            Multiplayer.SessionChanged += Reset;
            SaveBus.StateChanged += State;
            Scheduler.Sliced(Multiplayer.PatchOwner, "Autosave combat discovery", 500, Discover);
            Reset();
        }
        static void State(string previous, string current) { if (current == "LoadGame" || current == "setSeed") Reset(); }
        static void Reset() { Actors.Clear(); Dead.Clear(); _scan = 0; _scanning = true; _quietUntil = 0; Unsafe = true; }
        internal static void Activity()
        { if (HostSaveSystem.LocalReady) { _quietUntil = Time.unscaledTime + 10f; Unsafe = true; } }
        static void Track(PlayMakerFSM fsm)
        {
            if (!HostSaveSystem.LocalWorld || fsm == null || fsm.FsmName != "Detection" || Actors.ContainsKey(fsm)) return;
            Actors.Add(fsm, new Actor { Fsm = fsm });
        }
        static bool Discover()
        {
            if (!HostSaveSystem.LocalReady || !_scanning) return false;
            var fsms = PlayMakerFSM.FsmList;
            if (_scan >= fsms.Count) { _scanning = false; return false; }
            Track(fsms[_scan++]); return true;
        }
        static bool Event(Fsm fsm, FsmEvent ev, FsmEventData data)
        {
            if (!HostSaveSystem.LocalWorld || ev == null || fsm == null) return true;
            if (ev.Name == "Damage" || ev.Name == "Animal_Attack" || ev.Name == "Animal_Attack_Ranged") Activity();
            return true;
        }
        static void Attack(Fsm fsm, FsmState state)
        {
            if (!HostSaveSystem.LocalWorld || fsm == null || fsm.Name != "Attack" || state == null) return;
            if (state.Name == "fire" || state.Name == "attack" || state.Name == "attack_ranged" || state.Name == "throw") Activity();
        }
        internal static bool Check()
        {
            bool active = false;
            Dead.Clear();
            foreach (var pair in Actors)
            {
                var a = pair.Value; var fsm = a.Fsm;
                if (fsm == null) { Dead.Add(pair.Key); continue; }
                if (!fsm.enabled || !fsm.gameObject.activeInHierarchy) continue;
                if (a.Target == null)
                {
                    a.Target = fsm.FsmVariables.FindFsmGameObject("detectedObj");
                    var hp = FsmUtil.Find(fsm.gameObject, "Health");
                    a.Health = hp != null ? hp.FsmVariables.FindFsmFloat("Health") : null;
                    a.Attack = FsmUtil.Find(fsm.gameObject, "Attack");
                }
                if (a.Attack != null && !a.Attack.enabled) continue;
                if (a.Health != null && a.Health.Value < 1) continue;
                var target = a.Target != null ? a.Target.Value : null;
                // NPC search ghosts have no faction and are not an ongoing fight.
                if (target != null && target.activeInHierarchy && !target.CompareTag("Untagged")) { active = true; break; }
            }
            foreach (var fsm in Dead) Actors.Remove(fsm);
            var vehicles = Services.VehicleCombatActive;
            if (vehicles != null && vehicles()) active = true;
            if (active) Activity();
            Unsafe = _scanning || active || Time.unscaledTime < _quietUntil;
            return Unsafe;
        }
    }
}
