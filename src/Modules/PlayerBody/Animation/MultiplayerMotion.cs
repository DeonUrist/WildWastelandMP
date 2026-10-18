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
            internal readonly Quaternion[] Bones = new Quaternion[22];
            internal void Copy(Frame other)
            {
                Peer = other.Peer; Epoch = other.Epoch; Time = other.Time; Received = UnityEngine.Time.unscaledTime;
                Jump = other.Jump; Mode = other.Mode; Choice = other.Choice; Airborne = other.Airborne;
                Position = other.Position; Hips = other.Hips; Rotation = other.Rotation; Array.Copy(other.Bones, Bones, Bones.Length);
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
        static readonly Frame Outgoing = new Frame(), Incoming = new Frame();
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
            MotionFrames.Clear(); _nextMotion = 0; _motionEpoch = 0; _jumpState = ""; _jump = 0;
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
                if (!body.Motion || body.Model == null || body.Remote == null) continue;
                Frame first, second; float blend;
                if (!MotionAt(body.Peer, _renderTime(body.Remote), body.Choice, out first, out second, out blend)) continue;
                var root = body.Model.transform;
                root.localPosition = Vector3.Lerp(first.Position, second.Position, blend);
                root.localRotation = Quaternion.Slerp(first.Rotation, second.Rotation, blend);
                for (int i = 0; i < body.Bones.Length; i++) if (body.Bones[i] != null) body.Bones[i].localRotation = Quaternion.Slerp(first.Bones[i], second.Bones[i], blend);
                if (body.Bones[0] != null) body.Bones[0].localPosition = Vector3.Lerp(first.Hips, second.Hips, blend);
            }
            UnusedDrivers.Clear();
            foreach (var pair in DriverMasks) if (pair.Key == null || pair.Value.Used != Time.frameCount) { pair.Value.Restore(); UnusedDrivers.Add(pair.Key); }
            foreach (var car in UnusedDrivers) DriverMasks.Remove(car);
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
        }
        static void ReceiveMotion(ulong from, BinaryReader reader)
        {
            var frame = Incoming; frame.Epoch = reader.ReadUInt64(); frame.Peer = reader.ReadUInt64(); frame.Time = reader.ReadSingle();
            frame.Choice = reader.ReadByte(); frame.Jump = reader.ReadByte(); frame.Airborne = reader.ReadBoolean(); frame.Mode = reader.ReadByte();
            if (frame.Epoch != MultiplayerParts.Epoch || frame.Mode > 3 || frame.Choice > 7 || float.IsNaN(frame.Time) || float.IsInfinity(frame.Time) || frame.Time < 0) return;
            if (Multiplayer.Hosting) { if (from == 0 || Multiplayer.PlayerFor(from) == null) return; frame.Peer = from; }
            else if (!Multiplayer.Client || from != 0 || frame.Peer == Multiplayer.LocalPeer) return;
            if (frame.Mode != 0)
            {
                frame.Position = Position(reader); frame.Rotation = Rotation(reader); frame.Hips = Position(reader);
                for (int i = 0; i < frame.Bones.Length; i++) frame.Bones[i] = Rotation(reader);
            }
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
    }
}
