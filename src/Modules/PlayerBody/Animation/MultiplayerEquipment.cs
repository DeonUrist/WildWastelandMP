using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.PlayerBody
{
    internal static partial class MultiplayerAvatars
    {
        const byte EquipmentPacket = 83;
        sealed class Gear
        {
            internal ulong Peer;
            internal int Hidden;
            internal Equipment.Flashlight Flashlight;
            internal readonly string[] Names = new string[9];
            internal readonly GameObject[] Items = new GameObject[9];
        }
        static readonly Dictionary<ulong, Gear> EquipmentFrames = new Dictionary<ulong, Gear>();
        static readonly Gear LocalEquipment = new Gear();
        static readonly string[] ReadEquipment = new string[9], SentEquipment = new string[9];
        static Func<string, string, GameObject> _equipmentPrefab;
        static Gear _writingEquipment;
        static ulong _equipmentEpoch;
        static float _nextEquipment;
        static int _sentHidden = -1;
        static Equipment.Flashlight _sentFlashlight;
        static string EquipmentName(string name)
        { return name != null && name.StartsWith("MP_", StringComparison.Ordinal) ? name.Substring(3) : name ?? ""; }
        static void ResetEquipment()
        {
            foreach (var body in Bodies.Values) { if (body.Equipment != null) body.Equipment.Dispose(); body.Equipment = null; }
            EquipmentFrames.Clear(); _equipmentEpoch = 0; _nextEquipment = 0; _sentHidden = -1;
            Array.Clear(SentEquipment, 0, SentEquipment.Length);
            _sentFlashlight = default(Equipment.Flashlight);
        }
        static void TickEquipment()
        {
            if (MultiplayerParts.Epoch == 0) return;
            if (_equipmentEpoch != MultiplayerParts.Epoch) { ResetEquipment(); _equipmentEpoch = MultiplayerParts.Epoch; }
            foreach (var body in Bodies.Values) if (body.Equipment != null) body.Equipment.SetVisible(PlayerBodyModule.EquipmentOnBody.Value, false);
            var local = BodyRunner.PlayerBody;
            if (local != null && local.Alive) LocalEquipment.Hidden = local.NetworkEquipment(LocalEquipment.Names);
            else { Array.Clear(LocalEquipment.Names, 0, LocalEquipment.Names.Length); LocalEquipment.Hidden = 0; }
            for (int i = 0; i < LocalEquipment.Names.Length; i++) LocalEquipment.Names[i] = EquipmentName(LocalEquipment.Names[i]);
            LocalEquipment.Flashlight = Equipment.CaptureFlashlight();
            bool changed = _sentHidden != LocalEquipment.Hidden || LocalEquipment.Flashlight.Changed(_sentFlashlight);
            for (int i = 0; i < SentEquipment.Length; i++) changed |= SentEquipment[i] != (LocalEquipment.Names[i] ?? "");
            if (!changed && Time.unscaledTime < _nextEquipment) return;
            _sentHidden = LocalEquipment.Hidden; _nextEquipment = Time.unscaledTime + 3f;
            _sentFlashlight = LocalEquipment.Flashlight;
            for (int i = 0; i < SentEquipment.Length; i++) SentEquipment[i] = LocalEquipment.Names[i] ?? "";
            LocalEquipment.Peer = Multiplayer.LocalPeer; _writingEquipment = LocalEquipment;
            Multiplayer.Send(EquipmentPacket, WriteEquipment);
        }
        static void WriteEquipment(BinaryWriter writer)
        {
            writer.Write(_equipmentEpoch); writer.Write(_writingEquipment.Peer); writer.Write((ushort)_writingEquipment.Hidden);
            foreach (var name in _writingEquipment.Names) writer.Write(name ?? "");
            _writingEquipment.Flashlight.Write(writer);
        }
        static void ReceiveEquipment(ulong from, BinaryReader reader)
        {
            ulong epoch = reader.ReadUInt64(), peer = reader.ReadUInt64(); int hidden = reader.ReadUInt16();
            if (epoch == 0 || epoch != MultiplayerParts.Epoch || hidden > 511) return;
            if (Multiplayer.Hosting) { if (from == 0 || Multiplayer.PlayerFor(from) == null) return; peer = from; }
            else if (!Multiplayer.Client || from != 0 || peer == Multiplayer.LocalPeer || !MultiplayerPlayers.Known(peer)) return;
            for (int i = 0; i < ReadEquipment.Length; i++)
            {
                string name = reader.ReadString();
                if (name.Length > 64 || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return;
                ReadEquipment[i] = EquipmentName(name);
            }
            var flashlight = Equipment.Flashlight.Read(reader);
            if (reader.BaseStream.Position != reader.BaseStream.Length) return;
            if (_equipmentEpoch != epoch) { ResetEquipment(); _equipmentEpoch = epoch; }
            Gear gear; if (!EquipmentFrames.TryGetValue(peer, out gear)) EquipmentFrames.Add(peer, gear = new Gear { Peer = peer });
            gear.Hidden = hidden; gear.Flashlight = flashlight;
            for (int i = 0; i < gear.Names.Length; i++)
                if (gear.Names[i] != ReadEquipment[i] || gear.Items[i] == null && ReadEquipment[i].Length > 0)
                { gear.Names[i] = ReadEquipment[i]; gear.Items[i] = ReadEquipment[i].Length > 0 ? _equipmentPrefab(ReadEquipment[i], null) : null; }
            foreach (var body in Bodies.Values) if (body.Peer == peer) ApplyEquipment(body, gear);
            if (Multiplayer.Hosting) { _writingEquipment = gear; Multiplayer.Send(EquipmentPacket, WriteEquipment); }
        }
        static void ApplyEquipment(BodyState body, Gear gear)
        {
            if (body.Model == null) return;
            if (body.Equipment == null)
            {
                var bones = new Dictionary<string, Transform>();
                for (int i = 0; i < PoseBones.Length; i++) if (body.Bones[i] != null) bones[PoseBones[i]] = body.Bones[i];
                body.Equipment = new Equipment(body.Model, bones, true);
            }
            body.Equipment.ApplyRemote(gear.Items, gear.Hidden);
            body.Equipment.ApplyFlashlight(gear.Flashlight);
        }
    }
}
