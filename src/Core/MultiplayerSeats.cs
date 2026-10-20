using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Native PassengerGetIn parents the local player to a car's passenger seat with no arbitration: SeatTaken only compares other
    // players' last known positions to the seat, so two guests pressing F in the same moment both sit. The host now grants the
    // seat: a guest asks (kind 183), the host answers (kind 184) after checking the latest player states and its own seat, and the
    // guest sits on the grant. The host's own GetIn checks the same states. A grant is good for 2 s and one car at a time.
    internal static class MultiplayerSeats
    {
        const byte RequestKind = 183, ReplyKind = 184;
        const float GrantSeconds = 2f;
        sealed class Grant { internal ulong Peer; internal float At; }
        static readonly Dictionary<int, Grant> Grants = new Dictionary<int, Grant>();   // host: car id -> last grant
        static Func<bool> _localPassenger;
        static Func<object, Transform> _passengerCar;
        static Action<Transform, object> _getIn;
        static int _granted; static float _grantedAt, _nextRequest; static int _requested;
        static Transform _player; static object _car;
        internal static void Install()
        {
            var world = Multiplayer.Type("Net.WorldObjects"); var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
            _localPassenger = MultiplayerAccess.Call<Func<bool>>(world, "get_IsLocalPassenger", Type.EmptyTypes);
            _passengerCar = MultiplayerAccess.Get<Transform>(world, "passengerCar");
            _getIn = MultiplayerAccess.Call<Action<Transform, object>>(world, "PassengerGetIn", typeof(Transform), tracked);
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "PassengerGetIn"), prefix: AccessTools.Method(typeof(MultiplayerSeats), "BeforeGetIn")))
                throw new InvalidOperationException("MP passenger seat arbitration hook failed");
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
            MultiplayerPlayers.Left += Left;
        }
        static void Reset() { if (!Multiplayer.SessionActive) { Grants.Clear(); _granted = _requested = 0; _player = null; _car = null; } }
        static void Left(ulong peer) { foreach (var grant in Grants.Values) if (grant.Peer == peer) grant.At = -1f; }
        static bool Free(int carId, ulong requester, GameObject car)
        {
            ulong seated = MultiplayerPlayers.PassengerOf(carId);
            if (seated != 0 && seated != requester) return false;
            if (requester != 0 && _localPassenger() && car != null && _passengerCar(null) == car.transform) return false;
            Grant grant; return !Grants.TryGetValue(carId, out grant) || grant.Peer == requester || Time.unscaledTime - grant.At > GrantSeconds;
        }
        static bool BeforeGetIn(Transform __0, object __1)
        {
            if (!Multiplayer.Active || __0 == null || __1 == null) return true;
            var car = Multiplayer.TrackedObject(__1); int carId = Multiplayer.ObjectId(car);
            if (car == null || carId == 0) return true;
            if (Multiplayer.Hosting)
            {
                if (!Free(carId, 0, car)) { if (Log.Verbose) Log.Info("[Multiplayer] Passenger seat of " + car.name + " is taken"); return false; }
                Grants[carId] = new Grant { Peer = 0, At = Time.unscaledTime }; return true;
            }
            if (_granted == carId && Time.unscaledTime - _grantedAt <= GrantSeconds) { _granted = 0; return true; }
            if (Time.unscaledTime >= _nextRequest || _requested != carId)
            {
                _nextRequest = Time.unscaledTime + .5f; _requested = carId; _player = __0; _car = __1;
                Multiplayer.Send(RequestKind, w => w.Write(carId));
            }
            return false;
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind != RequestKind && kind != ReplyKind || !Multiplayer.WorldReady) return;
            int carId = reader.ReadInt32();
            if (kind == RequestKind)
            {
                if (!Multiplayer.Hosting || from == 0 || reader.BaseStream.Position != reader.BaseStream.Length) return;
                var car = Multiplayer.ObjectById(carId); var player = Multiplayer.PlayerFor(from);
                bool ok = car != null && player != null && car.GetComponent("VehicleController") != null && MultiplayerSleepers.WelcomedPeer(from)
                    && !MultiplayerPlayers.Riding(from) && (car.transform.position - player.transform.position).sqrMagnitude < 100f && Free(carId, from, car);
                if (ok) Grants[carId] = new Grant { Peer = from, At = Time.unscaledTime };
                Multiplayer.Send(ReplyKind, w => { w.Write(carId); w.Write(ok); }, from);
                if (Log.Verbose) Log.Info("[Multiplayer] Passenger seat " + (ok ? "granted" : "denied") + ": player " + from + ", car " + carId);
                return;
            }
            bool granted = reader.ReadBoolean();
            if (!Multiplayer.Client || from != 0 || reader.BaseStream.Position != reader.BaseStream.Length || carId != _requested) return;
            _requested = 0;
            if (!granted) { _player = null; _car = null; return; }
            // Sit now if the player is still on foot beside the same car; the prefix passes this one call through.
            var seated = _player; var tracked = _car; _player = null; _car = null;
            var root = Multiplayer.TrackedObject(tracked);
            // The host checked reach against its lagged copy of this player; re-check here so a player who walked or drove off is not pulled back.
            if (seated == null || root == null || seated.parent != null || Multiplayer.ObjectId(root) != carId || !Multiplayer.LocalTargetable
                || (root.transform.position - seated.position).sqrMagnitude > 100f) return;
            _granted = carId; _grantedAt = Time.unscaledTime;
            try { _getIn(seated, tracked); } finally { _granted = 0; }
        }
    }
}
