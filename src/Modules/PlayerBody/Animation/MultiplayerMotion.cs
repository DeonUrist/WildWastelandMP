using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.PlayerBody
{
    internal static partial class MultiplayerAvatars
    {
        // The local Player body owns special poses, including climbing and the final steering-wheel IK.
        // Playback is aligned with MP's interpolated player clock; it never moves a player's physics root.
        static readonly string[] PoseBones = { "mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2", "mixamorig:Neck", "mixamorig:Head", "mixamorig:LeftShoulder", "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand", "mixamorig:RightShoulder", "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:RightHand", "mixamorig:LeftUpLeg", "mixamorig:LeftLeg", "mixamorig:LeftFoot", "mixamorig:LeftToeBase", "mixamorig:RightUpLeg", "mixamorig:RightLeg", "mixamorig:RightFoot", "mixamorig:RightToeBase" };
        sealed class Frame
        {
            internal ulong Peer, Epoch; internal float Time, Received;
            internal byte Jump, Mode, Choice; internal bool Airborne;
            internal Vector3 Position, Hips; internal Quaternion Rotation;
            internal string Weapon; internal Vector3 WeaponPosition; internal Quaternion WeaponRotation;
            internal readonly Quaternion[] Bones = new Quaternion[22];
            internal void Copy(Frame other)
            {
                Peer = other.Peer; Epoch = other.Epoch; Time = other.Time; Received = UnityEngine.Time.unscaledTime;
                Jump = other.Jump; Mode = other.Mode; Choice = other.Choice; Airborne = other.Airborne;
                Position = other.Position; Hips = other.Hips; Rotation = other.Rotation; Array.Copy(other.Bones, Bones, Bones.Length);
                Weapon = other.Weapon; WeaponPosition = other.WeaponPosition; WeaponRotation = other.WeaponRotation;
            }
        }
        sealed class Frames
        {
            internal readonly Frame[] Ring = new Frame[32]; internal int Count, Last;
            internal Frames() { for (int i = 0; i < Ring.Length; i++) Ring[i] = new Frame(); }
            internal void Add(Frame frame)
            {
                if (Count > 0 && frame.Time <= Ring[Last].Time) return;
                Last = Count == 0 ? 0 : (Last + 1) % Ring.Length; Count = Math.Min(Count + 1, Ring.Length); Ring[Last].Copy(frame);
            }
        }
        static readonly Dictionary<ulong, Frames> MotionFrames = new Dictionary<ulong, Frames>();
        static readonly Frame Outgoing = new Frame(), Incoming = new Frame(), WeaponPose = new Frame();
        static Body _weaponBody; static int _weaponFrame = -1;
        static Frame _writing;
        static float _nextMotion;
        static ulong _motionEpoch;
        static string _jumpState = "";
        static byte _jump;
        sealed class DriverMask
        {
            internal Renderer[] Renderers; internal bool[] Hidden; internal int Used;
            internal void Restore() { for (int i = 0; i < Renderers.Length; i++) if (Renderers[i] != null) Renderers[i].forceRenderingOff = Hidden[i]; }
        }
        static readonly Dictionary<GameObject, DriverMask> DriverMasks = new Dictionary<GameObject, DriverMask>();
        static readonly List<GameObject> UnusedDrivers = new List<GameObject>();
        static void ResetMotion()
        {
            foreach (var mask in DriverMasks.Values) mask.Restore(); DriverMasks.Clear(); UnusedDrivers.Clear();
            MotionFrames.Clear(); _nextMotion = 0; _motionEpoch = 0; _jumpState = ""; _jump = 0; _weaponBody = null; _weaponFrame = -1;
        }
        internal static void CaptureWeaponMotion(Body local, Transform model)
        {
            if (!Multiplayer.WorldReady || !local.NetworkWeaponAction) return;
            for (int i = 0; i < PoseBones.Length; i++)
            {
                Transform bone; if (!local.Bones.TryGetValue(PoseBones[i], out bone) || bone == null) return;
                WeaponPose.Bones[i] = bone.localRotation; if (i == 0) WeaponPose.Hips = bone.localPosition;
            }
            WeaponPose.Position = model.position; WeaponPose.Rotation = model.rotation;
            local.NetworkWeaponPose(out WeaponPose.Weapon, out WeaponPose.WeaponPosition, out WeaponPose.WeaponRotation);
            _weaponBody = local; _weaponFrame = Time.frameCount;
        }
        static void HideNativeDriver(BodyState body)
        {
            if (!body.Driver || body.Car == 0 || body.Model == null) return;
            var car = Multiplayer.ObjectById(body.Car); if (car == null) return;
            DriverMask mask;
            if (!DriverMasks.TryGetValue(car, out mask))
            {
                var source = Game.FindDeep(car.transform, "PlayerModel_Sit"); if (source == null) return;
                mask = new DriverMask { Renderers = source.GetComponentsInChildren<Renderer>(true) };
                mask.Hidden = new bool[mask.Renderers.Length];
                for (int i = 0; i < mask.Renderers.Length; i++) mask.Hidden[i] = mask.Renderers[i].forceRenderingOff;
                DriverMasks.Add(car, mask);
            }
            mask.Used = Time.frameCount;
            foreach (var renderer in mask.Renderers) if (renderer != null) renderer.forceRenderingOff = true;
        }
        static Transform[] FindBones(Transform root)
        {
            var result = new Transform[PoseBones.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Game.FindDeep(root, PoseBones[i]);
            return result;
        }
        static bool MotionAt(ulong peer, float time, int choice, out Frame first, out Frame second, out float blend)
        {
            first = second = null; blend = 0; Frames frames;
            if (!MotionFrames.TryGetValue(peer, out frames) || frames.Count == 0 || Time.unscaledTime - frames.Ring[frames.Last].Received > 1f) return false;
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames.Ring[(frames.Last - i + frames.Ring.Length) % frames.Ring.Length];
                if (frame.Choice != choice) continue;
                first = frame;
                if (frame.Time <= time) break;
                second = frame;
            }
            if (first == null) return false;
            if (second == null || second.Mode != first.Mode || second.Time <= first.Time) second = first;
            else blend = Mathf.Clamp01((time - first.Time) / (second.Time - first.Time));
            return true;
        }
        static void LateMotion()
        {
            if (!Multiplayer.WorldReady || MultiplayerParts.Epoch == 0) return;
            if (_motionEpoch != MultiplayerParts.Epoch) { ResetMotion(); _motionEpoch = MultiplayerParts.Epoch; }
            foreach (var pair in Bodies)
            {
                var body = pair.Value; HideNativeDriver(body);
                if (body.Model == null || body.Remote == null) continue;
                bool downed = _remoteDowned(body.Remote);
                if (!body.Motion && !downed) continue;
                Frame first, second; float blend;
                if (!MotionAt(body.Peer, _renderTime(body.Remote), body.Choice, out first, out second, out blend)) continue;
                if (downed && (first.Mode != 4 || !string.IsNullOrEmpty(first.Weapon))) continue;
                var root = body.Model.transform;
                var position = Vector3.Lerp(first.Position, second.Position, blend);
                var rotation = Quaternion.Slerp(first.Rotation, second.Rotation, blend);
                if (downed)
                {
                    var parent = _remoteRoot(body.Remote); if (parent == null) continue;
                    var forward = -parent.transform.up; forward.y = 0;
                    var yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
                    root.SetPositionAndRotation(parent.transform.position - Vector3.up * .25f + yaw * position, yaw * rotation);
                    if (body.Character != null) body.Character.Suspended = true;
                    var pose = _pose(pair.Key); if (pose != null) pose.enabled = false;
                    var graph = _graph(pair.Key); if (graph.IsValid() && graph.IsPlaying()) graph.Stop();
                    _showAction(pair.Key, "", Time.unscaledDeltaTime);
                }
                else { root.localPosition = position; root.localRotation = rotation; }
                for (int i = 0; i < body.Bones.Length; i++) if (body.Bones[i] != null) body.Bones[i].localRotation = Quaternion.Slerp(first.Bones[i], second.Bones[i], blend);
                if (body.Bones[0] != null) body.Bones[0].localPosition = Vector3.Lerp(first.Hips, second.Hips, blend);
                if (first.Mode >= 2)
                {
                    _showItem(pair.Key, first.Weapon);
                    var item = _item(pair.Key);
                    if (item != null) item.transform.SetPositionAndRotation(root.TransformPoint(Vector3.Lerp(first.WeaponPosition, second.WeaponPosition, first.Weapon == second.Weapon ? blend : 0)), root.rotation * Quaternion.Slerp(first.WeaponRotation, second.WeaponRotation, first.Weapon == second.Weapon ? blend : 0));
                }
            }
            UnusedDrivers.Clear();
            foreach (var pair in DriverMasks) if (pair.Key == null || pair.Value.Used != Time.frameCount) { pair.Value.Restore(); UnusedDrivers.Add(pair.Key); }
            foreach (var car in UnusedDrivers) DriverMasks.Remove(car);
            foreach (var body in Bodies.Values) if (body.Equipment != null) body.Equipment.TickFlashlight();
            if (!Game.Ready || Game.Dead) return;
            string jump = Game.JumpState;
            if (_jumpState.Length > 0 && (jump == "Jump" && _jumpState != "Jump" || jump == "Falling" && _jumpState == "Idle" && Game.Velocity.y > 2f)) _jump++;
            _jumpState = jump;
            if (Time.unscaledTime < _nextMotion) return;
            _nextMotion = Time.unscaledTime + .05f;
            var local = BodyRunner.PlayerBody;
            Outgoing.Peer = Multiplayer.LocalPeer; Outgoing.Epoch = _motionEpoch; Outgoing.Time = Time.unscaledTime;
            Outgoing.Jump = _jump; Outgoing.Choice = (byte)PlayerBodyModule.Character.Value;
            Outgoing.Mode = local != null && local.Alive && ModAPI.PlayerTraversalActive ? (byte)1 : local != null && local.Alive && Game.InCar && CarSeat.Seat != null ? (LocalPassenger ? (byte)3 : (byte)2) : (byte)0;
            if (Outgoing.Mode == 0 && local != null && local.Alive && local.NetworkWeaponAction && _weaponBody == local && _weaponFrame == Time.frameCount) Outgoing.Mode = 4;
            Outgoing.Airborne = Outgoing.Mode == 0 && (jump == "Jump" || jump == "Falling");
            if (Outgoing.Mode != 0)
            {
                var player = Game.Player.transform; var inverse = Quaternion.Inverse(player.rotation);
                Outgoing.Position = inverse * (local.NetworkPosition - player.position); Outgoing.Rotation = inverse * local.NetworkRotation;
                for (int i = 0; i < PoseBones.Length; i++)
                {
                    Transform bone; if (!local.Bones.TryGetValue(PoseBones[i], out bone) || bone == null) { Outgoing.Mode = 0; break; }
                    Outgoing.Bones[i] = bone.localRotation; if (i == 0) Outgoing.Hips = bone.localPosition;
                }
                if (Outgoing.Mode == 4)
                {
                    Outgoing.Position = inverse * (WeaponPose.Position - player.position); Outgoing.Rotation = inverse * WeaponPose.Rotation; Outgoing.Hips = WeaponPose.Hips;
                    Array.Copy(WeaponPose.Bones, Outgoing.Bones, PoseBones.Length); Outgoing.Weapon = WeaponPose.Weapon; Outgoing.WeaponPosition = WeaponPose.WeaponPosition; Outgoing.WeaponRotation = WeaponPose.WeaponRotation;
                }
                else if (Outgoing.Mode == 2 || Outgoing.Mode == 3) local.NetworkWeaponPose(out Outgoing.Weapon, out Outgoing.WeaponPosition, out Outgoing.WeaponRotation);
            }
            _writing = Outgoing; Multiplayer.Send(82, WriteMotion, reliable: false);
        }
        static short Packed(float value) { return (short)Mathf.Clamp(Mathf.RoundToInt(value * 32767), -32767, 32767); }
        static void Rotation(BinaryWriter writer, Quaternion rotation) { writer.Write(Packed(rotation.x)); writer.Write(Packed(rotation.y)); writer.Write(Packed(rotation.z)); writer.Write(Packed(rotation.w)); }
        static Quaternion Rotation(BinaryReader reader)
        {
            var q = new Quaternion(reader.ReadInt16() / 32767f, reader.ReadInt16() / 32767f, reader.ReadInt16() / 32767f, reader.ReadInt16() / 32767f);
            float length = Mathf.Sqrt(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w);
            if (length < .5f) throw new InvalidDataException("Invalid player pose rotation");
            return new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length);
        }
        static void Position(BinaryWriter writer, Vector3 position) { writer.Write(position.x); writer.Write(position.y); writer.Write(position.z); }
        static Vector3 Position(BinaryReader reader)
        {
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (float.IsNaN(position.sqrMagnitude) || float.IsInfinity(position.sqrMagnitude) || position.sqrMagnitude > 100) throw new InvalidDataException("Invalid player pose offset");
            return position;
        }
        static void WriteMotion(BinaryWriter writer)
        {
            var frame = _writing; writer.Write(frame.Epoch); writer.Write(frame.Peer); writer.Write(frame.Time); writer.Write(frame.Choice); writer.Write(frame.Jump); writer.Write(frame.Airborne); writer.Write(frame.Mode);
            if (frame.Mode == 0) return;
            Position(writer, frame.Position); Rotation(writer, frame.Rotation); Position(writer, frame.Hips);
            foreach (var rotation in frame.Bones) Rotation(writer, rotation);
            if (frame.Mode >= 2) { writer.Write(frame.Weapon); Position(writer, frame.WeaponPosition); Rotation(writer, frame.WeaponRotation); }
        }
        static void ReceiveMotion(ulong from, BinaryReader reader)
        {
            var frame = Incoming; frame.Epoch = reader.ReadUInt64(); frame.Peer = reader.ReadUInt64(); frame.Time = reader.ReadSingle();
            frame.Choice = reader.ReadByte(); frame.Jump = reader.ReadByte(); frame.Airborne = reader.ReadBoolean(); frame.Mode = reader.ReadByte();
            if (frame.Epoch != MultiplayerParts.Epoch || frame.Mode > 4 || frame.Choice >= MultiplayerModels.ChoiceCount || float.IsNaN(frame.Time) || float.IsInfinity(frame.Time) || frame.Time < 0) return;
            if (Multiplayer.Hosting) { if (from == 0 || Multiplayer.PlayerFor(from) == null) return; frame.Peer = from; }
            else if (!Multiplayer.Client || from != 0 || frame.Peer == Multiplayer.LocalPeer) return;
            if (frame.Mode != 0)
            {
                frame.Position = Position(reader); frame.Rotation = Rotation(reader); frame.Hips = Position(reader);
                for (int i = 0; i < frame.Bones.Length; i++) frame.Bones[i] = Rotation(reader);
            }
            if (frame.Mode >= 2) { frame.Weapon = reader.ReadString(); if (frame.Weapon.Length > 64 || frame.Weapon.IndexOf('/') >= 0 || frame.Weapon.IndexOf('\\') >= 0) return; frame.WeaponPosition = Position(reader); frame.WeaponRotation = Rotation(reader); }
            if (reader.BaseStream.Position != reader.BaseStream.Length) return;
            Frames frames; if (!MotionFrames.TryGetValue(frame.Peer, out frames)) MotionFrames.Add(frame.Peer, frames = new Frames());
            frames.Add(frame);
            if (Multiplayer.Hosting) { _writing = frame; Multiplayer.Send(82, WriteMotion, reliable: false); }
        }
    }
    internal sealed partial class Body
    {
        Vector3 _networkPosition; Quaternion _networkRotation;
        internal Vector3 NetworkPosition { get { return _networkPosition + _networkRotation * _anim.localPosition; } }
        internal Quaternion NetworkRotation { get { return _networkRotation * _anim.localRotation; } }
        internal bool NetworkWeaponAction { get { return CrawlMotion || _prop != null || _reloading || Time.time < _throwUntil || Time.time < _pumpUntil; } }
        internal void NetworkWeaponPose(out string weapon, out Vector3 position, out Quaternion rotation)
        {
            weapon = _prop != null ? _propFor : "";
            position = _prop != null ? _anim.InverseTransformPoint(_prop.transform.position) : Vector3.zero;
            rotation = _prop != null ? Quaternion.Inverse(_anim.rotation) * _prop.transform.rotation : Quaternion.identity;
        }
    }
}
