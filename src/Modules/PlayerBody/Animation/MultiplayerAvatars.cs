using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Playables;
using WildWasteland.Core;

namespace WildWasteland.Modules.PlayerBody
{
    // MP retains player roots, state, equipment and effects. WW supplies the selected skin and its Mixamo animation graph.
    internal static partial class MultiplayerAvatars
    {
        sealed class BodyState { internal GameObject Model; internal IAnimatedCharacter Character; internal byte Attacks, Kicks, Jump; internal bool Counters, JumpKnown, Motion, Driver, WeaponMotion; internal string Action, ItemName; internal Transform Item; internal Material Material; internal ulong Peer; internal int Choice, Car; internal Transform[] Bones; internal object Remote; internal Equipment Equipment; }
        static readonly Dictionary<object, BodyState> Bodies = new Dictionary<object, BodyState>();
        static readonly Dictionary<ulong, int> Choices = new Dictionary<ulong, int>();
        internal static AudioClip Shout(ulong peer)
        { int choice; return Choices.TryGetValue(peer, out choice) ? FemalePain.Shout(choice) : null; }
        static Func<object, ulong> _peer;
        static Func<object, IDictionary> _remotes;
        static Func<object, GameObject> _model, _item;
        static Func<object, PlayableGraph> _graph;
        static Func<object, Behaviour> _pose;
        static Func<object, bool> _inCar, _downed, _airborne;
        static Func<object, bool> _remoteDowned;
        static Func<object, GameObject> _remoteRoot;
        static Func<object, float> _height;
        static Func<object, float> _renderTime;
        static Func<object, bool> _localPassenger;
        static Func<object, bool> _passenger;
        static Func<object, int> _rideCar;
        internal static bool LocalPassenger { get { return Multiplayer.SessionActive && _localPassenger != null && _localPassenger(null); } }
        static Func<object, byte> _attacks, _kicks;
        static Func<object, string> _stateItem, _action;
        static Action<object> _destroy;
        static Action<object, string> _showItem;
        static Action<object, string, float> _showAction;
        static Action<object, object, bool, float, float, float> _arms;
        static ulong _buildingPeer;
        static int _lastChoice = -1;
        static float _next;
        internal static void Init(IModule owner)
        {
            if (!Multiplayer.Present) return;
            var body = Multiplayer.Type("Game.Avatar.PlayerBody"); var remote = Multiplayer.Type("Game.RemotePlayer"); var state = Multiplayer.Type("Game.Avatar.PlayerState");
            _peer = MultiplayerAccess.Get<ulong>(remote, "peer"); _remotes = MultiplayerAccess.Get<IDictionary>(Multiplayer.Type("Net.NetSession"), "remotePlayers");
            _renderTime = MultiplayerAccess.Get<float>(remote, "renderTime");
            _remoteDowned = MultiplayerAccess.Call<Func<object, bool>>(remote, "get_Downed", Type.EmptyTypes);
            _remoteRoot = MultiplayerAccess.Get<GameObject>(remote, "root");
            _localPassenger = MultiplayerAccess.Get<bool>(Multiplayer.Type("Net.WorldObjects"), "IsLocalPassenger");
            _passenger = MultiplayerAccess.Get<bool>(state, "Passenger"); _rideCar = MultiplayerAccess.Get<int>(state, "RideCar");
            _model = MultiplayerAccess.Get<GameObject>(body, "model"); _item = MultiplayerAccess.Get<GameObject>(body, "item");
            _pose = MultiplayerAccess.Get<Behaviour>(body, "pose"); _graph = MultiplayerAccess.Get<PlayableGraph>(body, "graph");
            _inCar = MultiplayerAccess.Get<bool>(state, "InCar"); _downed = MultiplayerAccess.Get<bool>(state, "Downed"); _airborne = MultiplayerAccess.Get<bool>(state, "Airborne");
            _height = MultiplayerAccess.Get<float>(state, "Height"); _attacks = MultiplayerAccess.Get<byte>(state, "Attacks"); _kicks = MultiplayerAccess.Get<byte>(state, "Kicks");
            _stateItem = MultiplayerAccess.Get<string>(state, "Item"); _action = MultiplayerAccess.Get<string>(state, "Action");
            _destroy = MultiplayerAccess.Call<Action<object>>(remote, "Destroy", Type.EmptyTypes);
            _showItem = MultiplayerAccess.Call<Action<object, string>>(body, "ShowItem", typeof(string));
            _showAction = MultiplayerAccess.Call<Action<object, string, float>>(body, "ShowAction", typeof(string), typeof(float));
            _arms = MultiplayerAccess.Call<Action<object, object, bool, float, float, float>>(body, "AnimateArms", state, typeof(bool), typeof(float), typeof(float), typeof(float));
            _equipmentPrefab = MultiplayerAccess.Call<Func<string, string, GameObject>>(Multiplayer.Type("Net.WorldObjects"), "FindPrefab", typeof(string), typeof(string));
            Patch(owner, remote, "Build", "BeforeRemote", "AfterRemote");
            Patch(owner, body, "Build", "BeforeBody", "AfterBody"); Patch(owner, body, "Animate", "Animate"); Patch(owner, body, "Destroy", "Destroyed");
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
            Scheduler.Interval(owner, "MP character selection", .2f, Tick);
            Scheduler.EveryFrame(owner, "MP player motion", LateMotion, Phase.LateUpdateLast);
        }
        static void Patch(IModule owner, Type type, string name, string before, string after = null)
        {
            if (!Core.Patches.Patch(owner, AccessTools.Method(type, name), prefix: AccessTools.Method(typeof(MultiplayerAvatars), before), postfix: after == null ? null : AccessTools.Method(typeof(MultiplayerAvatars), after)))
                throw new InvalidOperationException("MP player animation hook failed: " + name);
        }
        static void BeforeRemote(object __instance, out ulong __state) { __state = _buildingPeer; _buildingPeer = _peer(__instance); }
        static void AfterRemote(ulong __state) { _buildingPeer = __state; }
        static void BeforeBody(ref Animator __0)
        { int choice; if (Choices.TryGetValue(_buildingPeer, out choice)) { var source = MultiplayerModels.Source(choice); if (source != null) __0 = source; } }
        static void AfterBody(object __instance)
        {
            var model = _model(__instance); if (model == null) return;
            int selected; Choices.TryGetValue(_buildingPeer, out selected);
            var remotes = _remotes(null);
            var entry = new BodyState { Model = model, Peer = _buildingPeer, Choice = selected, Bones = FindBones(model.transform), Remote = remotes != null && remotes.Contains(_buildingPeer) ? remotes[_buildingPeer] : null }; Bodies[__instance] = entry;
            // Match the local Body's render-only clone: native MP only strips hand props, retaining headbands, armbands and bags.
            foreach (var prop in model.GetComponentsInChildren<MeshFilter>(true)) if (prop != null) UnityEngine.Object.DestroyImmediate(prop.gameObject);
            SkinnedMeshRenderer skin = null;
            foreach (var candidate in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (candidate.bones.Length >= 20) { skin = candidate; break; }
            foreach (var other in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (other != skin) UnityEngine.Object.DestroyImmediate(other.gameObject);
            int choice; if (Choices.TryGetValue(_buildingPeer, out choice) && MultiplayerModels.CustomSkin(choice))
            {
                if (skin != null) entry.Material = Model.RemoteSkin(skin, choice);
            }
            Gear gear; if (EquipmentFrames.TryGetValue(entry.Peer, out gear)) ApplyEquipment(entry, gear);
        }
        static bool Animate(object __instance, Vector3 __0, float __1, float __2, object __3, float __4)
        {
            var api = Services.Animation; if (api == null || !api.Ready) return true;
            BodyState body;
            if (!Bodies.TryGetValue(__instance, out body) || body.Model == null) return true;
            body.Motion = false;
            bool seated = _inCar(__3), downed = _downed(__3); var pose = _pose(__instance); var graph = _graph(__instance);
            body.Driver = seated && !_passenger(__3); body.Car = _rideCar(__3);
            float stamp = body.Remote != null ? _renderTime(body.Remote) : 0;
            Frame frame, next; float blend;
            bool motion = MotionAt(body.Peer, stamp, body.Choice, out frame, out next, out blend);
            bool weaponMotion = !downed && !seated && motion && frame.Mode == 4;
            if (body.WeaponMotion && !weaponMotion) _showItem(__instance, "");
            body.WeaponMotion = weaponMotion;
            if (!downed && motion && frame.Mode != 0 && (seated ? frame.Mode == (_passenger(__3) ? 3 : 2) : frame.Mode == 1 || weaponMotion))
            {
                if (body.Character != null) body.Character.Suspended = true;
                if (pose != null) pose.enabled = false; if (graph.IsValid() && graph.IsPlaying()) graph.Stop();
                body.Motion = true;
                _showAction(__instance, frame.Mode == 1 || weaponMotion ? "" : _action(__3) ?? "", __4);
                _showItem(__instance, frame.Mode == 1 ? "" : frame.Weapon);
                _arms(__instance, __3, frame.Mode == 1, __1, __2, __4);
                body.Counters = true; body.Attacks = _attacks(__3); body.Kicks = _kicks(__3); body.Jump = frame.Jump; body.JumpKnown = true;
                return false;
            }
            if (seated || downed)
            {
                if (body.Character != null) body.Character.Suspended = true;
                if (pose != null) pose.enabled = true; if (graph.IsValid() && !graph.IsPlaying()) graph.Play(); return true;
            }
            if (body.Character == null || !body.Character.Valid) body.Character = api.Attach(body.Model.GetComponent<Animator>());
            var character = body.Character; if (character == null) return true;
            if (graph.IsValid() && graph.IsPlaying()) graph.Stop(); if (pose != null) pose.enabled = false;
            character.Suspended = false; character.ManualVelocity = true; character.Velocity = __0;
            character.Crouched = _height(__3) < 1.35f; character.Airborne = _airborne(__3); character.AimPitch = __2; character.TurnInPlace = true; character.WalkToStop = true;
            if (motion)
            {
                character.Airborne = frame.Airborne;
                if (body.JumpKnown && body.Jump != frame.Jump) character.Jump();
                body.Jump = frame.Jump; body.JumpKnown = true;
            }
            string action = _action(__3) ?? "", itemName = _stateItem(__3) ?? "";
            _showAction(__instance, action, __4); _showItem(__instance, action.Length == 0 ? itemName : "");
            // Native counters retain muzzle/audio effects; their animation graph is stopped while WW owns the body.
            _arms(__instance, __3, action.Length > 0, __1, __2, __4);
            var item = _item(__instance); var held = item != null ? item.transform : null;
            if (body.Item != held || body.ItemName != itemName)
            { body.Item = held; body.ItemName = itemName; character.SetWeapon(held, api.WeaponKey(itemName)); }
            character.Aiming = false;
            byte attacks = _attacks(__3), kicks = _kicks(__3);
            if (body.Counters)
            {
                if (attacks != body.Attacks) { if (character.WeaponKind == "None" || character.WeaponKind == "Melee") character.Strike(); else if (character.WeaponKind == "Throw") character.Throw(true); else character.Shoot(); }
                if (kicks != body.Kicks) character.Kick();
            }
            body.Counters = true; body.Attacks = attacks; body.Kicks = kicks; body.Action = action;
            body.Model.transform.localPosition = new Vector3(0, -_height(__3) * .5f - .05f, 0); body.Model.transform.localRotation = Quaternion.identity;
            return false;
        }
        static void Destroyed(object __instance)
        {
            BodyState body; if (!Bodies.TryGetValue(__instance, out body)) return;
            if (body.Character != null) body.Character.Dispose(); if (body.Equipment != null) body.Equipment.Dispose(); if (body.Material != null) UnityEngine.Object.Destroy(body.Material); Bodies.Remove(__instance);
        }
        static void Reset() { Choices.Clear(); ResetMotion(); ResetEquipment(); _lastChoice = -1; _next = 0; }
        static void Tick()
        {
            if (!Multiplayer.WorldReady) return;
            TickEquipment();
            int choice = (int)PlayerBodyModule.Character.Value;
            if (choice == _lastChoice && Time.unscaledTime < _next) return;
            _lastChoice = choice; _next = Time.unscaledTime + 3f;
            if (Multiplayer.Hosting)
            {
                UpdateChoice(0, choice);
                foreach (var pair in Choices) { ulong peer = pair.Key; int selected = pair.Value; Multiplayer.Send(81, w => { w.Write(peer); w.Write((byte)selected); }); }
            }
            else Multiplayer.Send(80, w => w.Write((byte)choice));
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == 82) { ReceiveMotion(from, reader); return; }
            if (kind == EquipmentPacket) { ReceiveEquipment(from, reader); return; }
            if (kind == 80 && Multiplayer.Hosting)
            {
                int choice = reader.ReadByte(); if (choice >= MultiplayerModels.ChoiceCount || Multiplayer.PlayerFor(from) == null) return;
                UpdateChoice(from, choice); Multiplayer.Send(81, w => { w.Write(from); w.Write((byte)choice); }); return;
            }
            if (kind == 81 && Multiplayer.Client && from == 0)
            { ulong peer = reader.ReadUInt64(); int choice = reader.ReadByte(); if (choice < MultiplayerModels.ChoiceCount && peer != Multiplayer.LocalPeer) UpdateChoice(peer, choice); }
        }
        static void UpdateChoice(ulong peer, int choice)
        {
            int old; if (Choices.TryGetValue(peer, out old) && old == choice) return;
            Choices[peer] = choice; var remotes = _remotes(null); if (remotes != null && remotes.Contains(peer)) _destroy(remotes[peer]);
        }
        internal static void Shutdown()
        {
            Multiplayer.Message -= Receive; Multiplayer.SessionChanged -= Reset;
            foreach (var body in Bodies.Values) { if (body.Character != null) body.Character.Dispose(); if (body.Material != null) UnityEngine.Object.Destroy(body.Material); }
            ResetEquipment(); Bodies.Clear(); Reset();
            // Rebuild native bodies after the module's patches are removed, restoring their original graph, pose and material.
            var remotes = _remotes != null ? _remotes(null) : null;
            if (remotes != null) foreach (DictionaryEntry remote in remotes) _destroy(remote.Value);
        }
    }
}
