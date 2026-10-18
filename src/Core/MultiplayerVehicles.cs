using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using UnityEngine;

namespace WildWasteland.Core
{
    // Native car snapshots own ignition and handbrake values. Cassette state follows the same car owner.
    internal static class MultiplayerVehicles
    {
        sealed class Controls
        {
            internal GameObject Car;
            internal VehicleController Vehicle;
            internal PlayMakerFSM Brake, Radio, Volume;
            internal Transform RadioRoot;
            internal AudioSource Audio;
            internal FsmFloat AudioVolume;
            internal int Id, RadioId, SnapshotRadio;
            internal uint Sequence, Received;
            internal ulong Source;
            internal bool BrakeKnown, BrakeWritten, RadioKnown, PlayWritten, SnapshotKnown, SnapshotPlay;
            internal float VolumeWritten, SnapshotVolume, Refresh, SendAt;
        }
        static readonly Dictionary<GameObject, Controls> Cars = new Dictionary<GameObject, Controls>();
        static readonly List<GameObject> Gone = new List<GameObject>();
        static readonly List<PlayMakerFSM> Fsms = new List<PlayMakerFSM>();
        static Func<object, float> _handbrake;
        static Func<object, Transform> _passengerSeat;
        static Func<object, float> _passengerSpeed;
        static Func<Vector3> _exitSpot;
        static Func<int> _exitMask;
        static Action _getOut;
        static Action<string, float> _notice;
        static Transform _pendingExit;
        static float _exitDeadline;
        static Controls _writing;
        static ulong _epoch;
        static int _generation = -1;
        internal static void Install()
        {
            var world = Multiplayer.Type("Net.WorldObjects");
            _handbrake = MultiplayerAccess.Get<float>(Multiplayer.Type("Net.WorldObjects+CarSnapshot"), "Handbrake");
            Patch(world, "CurrentCarState", "Captured");
            Patch(world, "ApplyCarVisuals", "Copied");
            Patch(world, "TakeOverCarState", "TakenOver");
            _passengerSeat = MultiplayerAccess.Get<Transform>(world, "passengerSeat");
            _passengerSpeed = MultiplayerAccess.Get<float>(world, "passengerSpeed");
            _exitSpot = MultiplayerAccess.Call<Func<Vector3>>(world, "PassengerExitSpot", Type.EmptyTypes);
            _exitMask = MultiplayerAccess.Call<Func<int>>(world, "get_PassengerMask", Type.EmptyTypes);
            _getOut = MultiplayerAccess.Call<Action>(world, "PassengerGetOut", Type.EmptyTypes);
            _notice = MultiplayerAccess.Call<Action<string, float>>(Multiplayer.Type("UI.Hud"), "Show", typeof(string), typeof(float));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "PassengerGetOut"), prefix: AccessTools.Method(typeof(MultiplayerVehicles), "BeforeExit")))
                throw new InvalidOperationException("MP passenger exit guard failed");
            Scheduler.Interval(Multiplayer.PatchOwner, "Passenger ground readiness", .25f, ExitTick);
            Multiplayer.Message += Receive;
            Multiplayer.SessionChanged += Reset;
            Scheduler.Interval(Multiplayer.PatchOwner, "Vehicle control cache", 5f, Sweep);
            Log.Always("[Multiplayer] Passenger vehicle controls ready (handbrake and cassette; native ignition retained)");
        }
        static void Patch(Type type, string method, string after)
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(type, method), postfix: AccessTools.Method(typeof(MultiplayerVehicles), after)))
                throw new InvalidOperationException("MP vehicle control hook failed: " + method);
        }
        static void Reset() { Cars.Clear(); Gone.Clear(); Fsms.Clear(); _writing = null; _pendingExit = null; _epoch = MultiplayerParts.Epoch; _generation = SaveBus.Generation; }
        static bool Ready()
        {
            if (_epoch != MultiplayerParts.Epoch || _generation != SaveBus.Generation) Reset();
            return Multiplayer.WorldReady && _epoch != 0 && !SaveBus.Busy && !MultiplayerSaving.Frozen;
        }
        static void Sweep()
        {
            if (!Ready()) return;
            Gone.Clear(); foreach (var pair in Cars) if (pair.Key == null) Gone.Add(pair.Key);
            foreach (var car in Gone) Cars.Remove(car);
        }
        static bool BeforeExit()
        {
            if (!Multiplayer.Client || !Ready()) return true;
            var player = GameRefs.Player; var seat = _passengerSeat(null);
            if (player == null || seat == null || player.transform.parent != seat || !Multiplayer.LocalTargetable) { _pendingExit = null; return true; }
            var health = FsmUtil.Find(player, "Health"); var value = health != null ? health.FsmVariables.FindFsmFloat("Health") : null;
            if (value != null && value.Value <= 0f) { _pendingExit = null; return true; }
            RaycastHit hit;
            var spot = _exitSpot(); int mask = _exitMask();
            bool support = Physics.Raycast(spot, Vector3.down, out hit, 1.5f, mask, QueryTriggerInteraction.Ignore);
            var tile = support && hit.collider is TerrainCollider ? hit.collider.GetComponentInParent<MapMagic.Terrains.TerrainTile>() : null;
            if (support && !Physics.CheckCapsule(spot + Vector3.down * .5f, spot + Vector3.up * .5f, .3f, mask, QueryTriggerInteraction.Ignore)
                && hit.collider.name != "Draft Terrain" && (tile == null || tile.main != null && tile.main.applyReady && tile.main.terrain != null
                && hit.collider.gameObject == tile.main.terrain.gameObject)) { _pendingExit = null; return true; }
            if (_pendingExit != seat) { _pendingExit = seat; _exitDeadline = Time.unscaledTime + 120f; _notice("Waiting for a safe place to get out.", 4f); }
            return false;
        }
        static void ExitTick()
        {
            if (_pendingExit == null) return;
            var player = GameRefs.Player;
            if (!Multiplayer.Client || !Ready() || player == null || player.transform.parent != _pendingExit || Time.unscaledTime > _exitDeadline || _passengerSpeed(null) > 6f)
            { _pendingExit = null; return; }
            if (Multiplayer.Typing || ApocasetterBridge.InputBlocked || GameRefs.Menu != null && GameRefs.Menu.ActiveStateName == "pause") return;
            _getOut();
        }
        static Controls Get(GameObject car)
        {
            Controls c;
            if (!Cars.TryGetValue(car, out c))
            {
                c = new Controls { Car = car, Id = Multiplayer.ObjectId(car), Vehicle = car.GetComponent<VehicleController>() };
                Cars.Add(car, c);
            }
            if (Time.unscaledTime < c.Refresh) return c;
            c.Refresh = Time.unscaledTime + .5f;
            var brake = car.transform.Find("handbrake");
            if (c.Brake == null && brake != null) c.Brake = FsmUtil.Find(brake.gameObject, "Handbrake");
            var hinge = car.transform.Find("hinge_radio");
            var radio = hinge != null && hinge.childCount > 0 ? hinge.GetChild(0) : null;
            if (radio == c.RadioRoot && c.Radio != null)
            { if (c.RadioId == 0) c.RadioId = Multiplayer.ObjectId(c.Radio.gameObject); return c; }
            c.RadioRoot = radio; c.Radio = c.Volume = null; c.Audio = null; c.AudioVolume = null; c.RadioId = 0; c.RadioKnown = false;
            if (radio == null) return c;
            Fsms.Clear(); radio.GetComponentsInChildren(true, Fsms);
            foreach (var fsm in Fsms)
            {
                if (fsm.FsmName == "OnOff" && fsm.GetComponent<AudioSource>() != null)
                { c.Radio = fsm; c.Audio = fsm.GetComponent<AudioSource>(); c.RadioId = Multiplayer.ObjectId(fsm.gameObject); }
                if (fsm.FsmName == "Volume" && fsm.name == "Volume") c.Volume = fsm;
            }
            if (c.Volume != null) c.AudioVolume = c.Volume.FsmVariables.FindFsmFloat("audioVolume");
            Fsms.Clear(); return c;
        }
        static bool BrakeOn(PlayMakerFSM brake)
        { string state = brake.ActiveStateName; return state == "HandbrakeOn" || state == "over" || state == "Sound 2"; }
        static void Brake(Controls c, bool on)
        {
            if (c.Brake != null && c.Brake.Fsm.Initialized && BrakeOn(c.Brake) != on)
                c.Brake.Fsm.SetState(on ? "HandbrakeOn" : "HandbrakeOff");
            if (c.Vehicle != null) c.Vehicle.input.Handbrake = on ? 1f : 0f;
            c.BrakeKnown = c.Brake != null && c.Brake.Fsm.Initialized; c.BrakeWritten = on;
        }
        static void Copied(GameObject __0, object __1)
        {
            if (!Ready() || __0 == null) return;
            var c = Get(__0);
            if (c.BrakeKnown && c.Brake != null && BrakeOn(c.Brake) != c.BrakeWritten)
                Request(c, 1, BrakeOn(c.Brake) ? 1f : 0f);
            Brake(c, _handbrake(__1) > .5f);
            if (!c.SnapshotKnown || c.Audio == null || c.RadioId == 0 || c.RadioId != c.SnapshotRadio || !c.Radio.Fsm.Initialized) return;
            bool play = c.Radio.ActiveStateName == "on";
            if (c.RadioKnown)
            {
                if (play != c.PlayWritten) Request(c, 2, play ? 1f : 0f);
                if (Mathf.Abs(c.Audio.volume - c.VolumeWritten) > .001f) Request(c, 4, c.Audio.volume);
            }
            Radio(c, c.SnapshotPlay, c.SnapshotVolume);
        }
        static void TakenOver(GameObject __0, object __1)
        {
            if (!Ready() || __0 == null) return;
            var c = Get(__0); Brake(c, _handbrake(__1) > .5f); c.RadioKnown = false; c.SendAt = 0;
        }
        static void Radio(Controls c, bool play, float volume)
        {
            if ((c.Radio.ActiveStateName == "on") != play) c.Radio.Fsm.SetState(play ? "on" : "off");
            c.Audio.volume = volume;
            if (c.AudioVolume != null) c.AudioVolume.Value = volume;
            c.RadioKnown = true; c.PlayWritten = play; c.VolumeWritten = volume;
        }
        static void Captured(object __0)
        {
            if (!Ready()) return;
            var car = Multiplayer.TrackedObject(__0);
            if (car == null || Multiplayer.OwnedElsewhere(car)) return;
            var c = Get(car);
            c.BrakeKnown = c.RadioKnown = false;
            if (Time.unscaledTime < c.SendAt) return;
            Publish(c);
        }
        static void Publish(Controls c)
        {
            if (c.Id == 0) c.Id = Multiplayer.ObjectId(c.Car);
            if (c.Id == 0) return;
            c.SendAt = Time.unscaledTime + .1f; c.Sequence++; _writing = c;
            Multiplayer.Send(139, WriteState, reliable: false);
        }
        static void WriteState(BinaryWriter w)
        {
            var c = _writing; w.Write(_epoch); w.Write(c.Id); w.Write(c.RadioId); w.Write(c.Sequence);
            w.Write(c.Radio != null && c.Radio.ActiveStateName == "on"); w.Write(c.Audio != null ? c.Audio.volume : 0f);
        }
        static void Request(Controls c, byte control, float value)
        {
            if (c.Id == 0) return;
            if (Multiplayer.Hosting) Route(c, control, value);
            else SendControl(140, c, control, value, null);
        }
        static void SendControl(byte kind, Controls c, byte control, float value, ulong? peer)
        { Multiplayer.Send(kind, w => { w.Write(_epoch); w.Write(c.Id); w.Write(c.RadioId); w.Write(control); w.Write(value); }, peer); }
        static void Route(Controls c, byte control, float value)
        {
            ulong owner = Multiplayer.OwnerOf(c.Car);
            if (owner == 0) Apply(c, control, value);
            else SendControl(141, c, control, value, owner);
        }
        static void Apply(Controls c, byte control, float value)
        {
            if (control == 1) Brake(c, value > .5f);
            else if (c.Radio != null && c.Audio != null && c.Radio.Fsm.Initialized)
                Radio(c, control == 2 ? value > .5f : c.Radio.ActiveStateName == "on", control == 4 ? value : c.Audio.volume);
            Publish(c);
            if (Log.Verbose) Log.Info("[Multiplayer] Vehicle control applied: " + c.Id + ", control " + control + ", value " + value);
        }
        static void Receive(ulong from, byte kind, BinaryReader r)
        {
            if (kind < 139 || kind > 141 || !Ready() || r.ReadUInt64() != _epoch) return;
            int id = r.ReadInt32(), radio = r.ReadInt32();
            uint sequence = kind == 139 ? r.ReadUInt32() : 0;
            bool play = kind == 139 && r.ReadBoolean(); byte control = kind == 139 ? (byte)0 : r.ReadByte(); float value = r.ReadSingle();
            if (r.BaseStream.Position != r.BaseStream.Length || float.IsNaN(value) || value < 0f || value > 1f) return;
            var car = Multiplayer.ObjectById(id);
            if (car == null || car.GetComponent<VehicleController>() == null) return;
            var c = Get(car);
            if (kind == 139)
            {
                if (Multiplayer.Hosting ? from == 0 || Multiplayer.OwnerOf(car) != from || Multiplayer.PlayerFor(from) == null : from != 0) return;
                if (c.SnapshotKnown && c.Source == from && sequence <= c.Received) return;
                c.Source = from; c.Received = sequence;
                c.SnapshotKnown = true; c.SnapshotRadio = radio; c.SnapshotPlay = play; c.SnapshotVolume = value;
                if (Multiplayer.Hosting)
                {
                    uint relay = ++c.Sequence;
                    Multiplayer.Send(139, w => { w.Write(_epoch); w.Write(id); w.Write(radio); w.Write(relay); w.Write(play); w.Write(value); }, reliable: false);
                }
                return;
            }
            if (control != 1 && control != 2 && control != 4 || control != 1 && (radio == 0 || radio != c.RadioId)) return;
            if (kind == 140)
            {
                var player = Multiplayer.PlayerFor(from);
                if (Multiplayer.Hosting && from != 0 && player != null && (player.transform.position - car.transform.position).sqrMagnitude < 144f)
                    Route(c, control, value);
            }
            else if (Multiplayer.Client && from == 0 && !Multiplayer.OwnedElsewhere(car)) Apply(c, control, value);
        }
    }
}
