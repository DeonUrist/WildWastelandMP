using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerPlayers
    {
        sealed class State { internal float Time; internal Vector3 Position, Velocity; internal int Car; internal bool Downed, Passenger; internal byte Attacks, Kicks; }
        static readonly Dictionary<ulong, State> States = new Dictionary<ulong, State>();
        static Func<object, ulong> _peer;
        static Func<object, float> _time;
        static Func<object, Vector3> _position;
        static Func<object, int> _car;
        static Func<object, bool> _downed, _passenger;
        static Func<object, byte> _attacks, _kicks;
        internal static void Install()
        {
            var remote = Multiplayer.Type("Game.RemotePlayer"); var state = Multiplayer.Type("Game.Avatar.PlayerState");
            _peer = MultiplayerAccess.Get<ulong>(remote, "peer"); _time = MultiplayerAccess.Get<float>(state, "Time");
            _position = MultiplayerAccess.Get<Vector3>(state, "Position"); _car = MultiplayerAccess.Get<int>(state, "RideCar"); _downed = MultiplayerAccess.Get<bool>(state, "Downed");
            _passenger = MultiplayerAccess.Get<bool>(state, "Passenger");
            _attacks = MultiplayerAccess.Get<byte>(state, "Attacks"); _kicks = MultiplayerAccess.Get<byte>(state, "Kicks");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(remote, "AddSnapshot"), postfix: AccessTools.Method(typeof(MultiplayerPlayers), "Snapshot")))
                throw new InvalidOperationException("Multiplayer player-state hook failed");
            Multiplayer.SessionChanged += Reset;
        }
        static void Reset() { States.Clear(); }
        static void Snapshot(object __instance, object __0)
        {
            if (!Multiplayer.Hosting) return;
            ulong peer = _peer(__instance); float time = _time(__0); var position = _position(__0); State state;
            if (!States.TryGetValue(peer, out state)) { state = new State { Time = time, Position = position }; States.Add(peer, state); }
            else if (time <= state.Time) return;
            else if (state.Attacks != _attacks(__0) || state.Kicks != _kicks(__0)) HostSaveCombat.Activity();
            state.Attacks = _attacks(__0); state.Kicks = _kicks(__0);
            float elapsed = time - state.Time; var delta = position - state.Position;
            state.Velocity = elapsed > .001f && elapsed < 1f && delta.sqrMagnitude < 225f ? Vector3.ClampMagnitude(delta / elapsed, 200f) : Vector3.zero;
            state.Time = time; state.Position = position; state.Car = _car(__0); state.Downed = _downed(__0); state.Passenger = _passenger(__0);
        }
        internal static bool Targetable(ulong peer) { State state; return States.TryGetValue(peer, out state) && !state.Downed; }
        internal static bool Downed(ulong peer) { State state; return States.TryGetValue(peer, out state) && state.Downed; }
        internal static GameObject Driving(ulong peer) { State state; return States.TryGetValue(peer, out state) && !state.Passenger ? Multiplayer.ObjectById(state.Car) : null; }
        internal static bool At(ulong peer, Vector3 position) { State state; return States.TryGetValue(peer, out state) && (state.Position - position).sqrMagnitude < 4f; }
        internal static void Forget(ulong peer) { States.Remove(peer); }
        internal static GameObject Car(ulong peer) { State state; return States.TryGetValue(peer, out state) ? Multiplayer.ObjectById(state.Car) : null; }
        internal static Vector3 Velocity(ulong peer) { State state; return States.TryGetValue(peer, out state) ? state.Velocity : Vector3.zero; }
    }
}
