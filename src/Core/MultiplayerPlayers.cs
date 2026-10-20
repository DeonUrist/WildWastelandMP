using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerPlayers
    {
        sealed class State { internal float Time, Received; internal Vector3 Position, Velocity; internal int Car; internal bool Downed, Passenger, InCar; internal byte Attacks, Kicks; }
        static readonly Dictionary<ulong, State> States = new Dictionary<ulong, State>();
        internal static event Action<ulong> Left;
        static Func<object, IDictionary> _names;
        static Func<object, GameObject> _root;
        static byte _stateKind, _eventKind, _profileKind, _diedKind;
        static Func<object, ulong> _peer;
        static Func<object, float> _time;
        static Func<object, Vector3> _position;
        static Func<object, int> _car;
        static Func<object, bool> _downed, _passenger, _inCar;
        static Func<object, byte> _attacks, _kicks;
        internal static void Install()
        {
            var remote = Multiplayer.Type("Game.RemotePlayer"); var state = Multiplayer.Type("Game.Avatar.PlayerState");
            _peer = MultiplayerAccess.Get<ulong>(remote, "peer"); _time = MultiplayerAccess.Get<float>(state, "Time");
            _position = MultiplayerAccess.Get<Vector3>(state, "Position"); _car = MultiplayerAccess.Get<int>(state, "RideCar"); _downed = MultiplayerAccess.Get<bool>(state, "Downed");
            _passenger = MultiplayerAccess.Get<bool>(state, "Passenger");
            _inCar = MultiplayerAccess.Get<bool>(state, "InCar");
            _attacks = MultiplayerAccess.Get<byte>(state, "Attacks"); _kicks = MultiplayerAccess.Get<byte>(state, "Kicks");
            var session = Multiplayer.Type("Net.NetSession"); var messages = Multiplayer.Type("Net.MessageType");
            _names = MultiplayerAccess.Get<IDictionary>(session, "playerNames"); _root = MultiplayerAccess.Get<GameObject>(remote, "root");
            _stateKind = Convert.ToByte(Enum.Parse(messages, "PlayerState")); _eventKind = Convert.ToByte(Enum.Parse(messages, "PlayerEvent"));
            _profileKind = Convert.ToByte(Enum.Parse(messages, "PlayerProfile")); _diedKind = Convert.ToByte(Enum.Parse(messages, "PlayerDied"));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(remote, "AddSnapshot"), postfix: AccessTools.Method(typeof(MultiplayerPlayers), "Snapshot"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(remote, "Update"), prefix: AccessTools.Method(typeof(MultiplayerPlayers), "Update"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "AddPlayer"), prefix: AccessTools.Method(typeof(MultiplayerPlayers), "Joined"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "RemovePlayer"), postfix: AccessTools.Method(typeof(MultiplayerPlayers), "Departed")))
                throw new InvalidOperationException("Multiplayer player-state hook failed");
            Multiplayer.SessionChanged += Reset;
        }
        static void Reset() { States.Clear(); }
        static void Joined(ulong __0) { States.Remove(__0); }
        static void Departed(ulong __0)
        {
            States.Remove(__0);
            var left = Left; if (left != null) left(__0);
        }
        // Native relayed UDP state can arrive after reliable PlayerLeft. Only admitted identities may create avatars.
        internal static bool BeforeNative(ulong from, byte[] data)
        {
            if (!AllowAvatarPacket(from, data)) return false;
            byte kind = data[0];
            if (kind == _diedKind && Multiplayer.Hosting && from != 0) MultiplayerRecovery.PlayerDied(from);
            return true;
        }
        internal static bool AllowAvatarPacket(ulong from, byte[] data)
        {
            byte kind = data[0];
            if (kind != _stateKind && kind != _eventKind && kind != _profileKind && kind != _diedKind) return true;
            if (from != 0)
            {
                var names = _names(null);
                if (names == null || !names.Contains(from) || Multiplayer.Hosting && !MultiplayerSleepers.WelcomedPeer(from)) return false;
            }
            if (kind == _diedKind && Multiplayer.Hosting && from != 0)
            {
                if (data.Length != 13) return false;
                for (int offset = 1; offset < 13; offset += 4)
                { float value = BitConverter.ToSingle(data, offset); if (float.IsNaN(value) || float.IsInfinity(value)) return false; }
            }
            return true;
        }
        static bool Update(object __instance)
        {
            State state;
            if (MultiplayerSaving.Frozen || !States.TryGetValue(_peer(__instance), out state) || Time.unscaledTime - state.Received <= 5f) return true;
            var root = _root(__instance); if (root != null && root.activeSelf) root.SetActive(false);
            return false;
        }
        static void Snapshot(object __instance, object __0)
        {
            ulong peer = _peer(__instance); float time = _time(__0); var position = _position(__0); State state;
            if (!States.TryGetValue(peer, out state)) { state = new State { Time = time, Position = position }; States.Add(peer, state); }
            else if (time <= state.Time) return;
            else if (Multiplayer.Hosting && (state.Attacks != _attacks(__0) || state.Kicks != _kicks(__0))) HostSaveCombat.Activity();
            state.Received = Time.unscaledTime;
            state.Attacks = _attacks(__0); state.Kicks = _kicks(__0);
            float elapsed = time - state.Time; var delta = position - state.Position;
            state.Velocity = elapsed > .001f && elapsed < 1f && delta.sqrMagnitude < 225f ? Vector3.ClampMagnitude(delta / elapsed, 200f) : Vector3.zero;
            state.Time = time; state.Position = position; state.Car = _car(__0); state.Downed = _downed(__0); state.Passenger = _passenger(__0); state.InCar = _inCar(__0);
        }
        internal static bool HasSnapshot(ulong peer) { return States.ContainsKey(peer); }
        internal static bool Known(ulong peer) { var names = _names != null ? _names(null) : null; return peer == 0 || names != null && names.Contains(peer); }
        internal static bool Fresh(ulong peer) { State state; return States.TryGetValue(peer, out state) && Time.unscaledTime - state.Received <= 5f; }
        internal static bool Targetable(ulong peer) { State state; return States.TryGetValue(peer, out state) && !state.Downed && Time.unscaledTime - state.Received <= 5f; }
        internal static bool Downed(ulong peer) { State state; return States.TryGetValue(peer, out state) && state.Downed; }
        internal static bool Riding(ulong peer) { State state; return States.TryGetValue(peer, out state) && (state.InCar || state.Passenger || state.Car != 0); }
        /// <summary>The peer whose last state says it sits in this car's passenger seat (fresh within 5 s), or 0.</summary>
        internal static ulong PassengerOf(int carId)
        {
            foreach (var pair in States) if (pair.Value.Passenger && pair.Value.Car == carId && Time.unscaledTime - pair.Value.Received <= 5f) return pair.Key;
            return 0;
        }
        internal static GameObject Driving(ulong peer) { State state; return States.TryGetValue(peer, out state) && !state.Passenger ? Multiplayer.ObjectById(state.Car) : null; }
        internal static bool At(ulong peer, Vector3 position) { State state; return States.TryGetValue(peer, out state) && (state.Position - position).sqrMagnitude < 4f; }
        internal static void Forget(ulong peer) { States.Remove(peer); }
        internal static GameObject Car(ulong peer) { State state; return States.TryGetValue(peer, out state) ? Multiplayer.ObjectById(state.Car) : null; }
        internal static Vector3 Velocity(ulong peer) { State state; return States.TryGetValue(peer, out state) ? state.Velocity : Vector3.zero; }
    }
}
