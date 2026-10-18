using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using WildWasteland.Core.UI;

namespace WildWasteland.Core
{
    internal interface ISleepingBodies
    {
        int LocalChoice { get; }
        ISleepingBody Create(int choice, Vector3 position, float yaw);
    }
    internal interface ISleepingBody : IDisposable
    {
        GameObject Root { get; }
        Rigidbody[] Bodies { get; }
        Collider[] Colliders { get; }
        void Apply();
    }
    internal static class MultiplayerSleepers
    {
        internal const string SleepingKey = "ww.sleeping", ChoiceKey = "ww.bodyChoice";
        const byte Full = 159, Pose = 160, Remove = 161, Carry = 162;
        const ulong Nobody = ulong.MaxValue;
        const int BoneCount = 11, Limit = 512;
        sealed class Sleeper
        {
            internal int Id, Choice; internal string Name, CarryHint; internal object Record; internal ulong Carrier = Nobody;
            internal uint Sequence; internal float NextBuild, NextGround; internal bool HasPose, Supported, Place = true;
            internal ISleepingBody Body; internal TextMesh Label;
            internal readonly TerrainCollider[] Ground = new TerrainCollider[BoneCount];
            internal readonly MapMagic.Terrains.TerrainTile[] Tiles = new MapMagic.Terrains.TerrainTile[BoneCount];
            internal readonly Vector3[] Positions = new Vector3[BoneCount];
            internal readonly Quaternion[] Rotations = new Quaternion[BoneCount];
        }
        static readonly Dictionary<int, Sleeper> Bodies = new Dictionary<int, Sleeper>();
        static readonly HashSet<int> Removed = new HashSet<int>();
        static readonly Vector3[] IncomingPositions = new Vector3[BoneCount];
        static readonly Quaternion[] IncomingRotations = new Quaternion[BoneCount];
        static Func<object, IDictionary> _records;
        static Func<object, Dictionary<string, float>> _stats;
        static Func<object, string> _name;
        static Func<object, bool> _hasPosition;
        static Func<object, Vector3> _position;
        static Func<object, float> _yaw;
        static Action<object, Vector3> _setPosition;
        static Action<object, bool> _setHasPosition;
        static Func<object, bool> _closing;
        static Func<bool> _localInCar;
        static Func<object, Transform> _passenger;
        static Text _hint; static string _hintText; static float _nextHint;
        static int _nextId; static ulong _epoch;
        static float _nextFull, _nextPose, _nextCheckpoint, _nextCommand;
        static bool _loaded, _stopping;
        internal static void Install()
        {
            var record = Multiplayer.Type("Worlds.PlayerRecord"); var session = Multiplayer.Type("Net.NetSession");
            _records = MultiplayerAccess.Get<IDictionary>(Multiplayer.Type("Worlds.PlayerRegistry"), "records");
            _stats = MultiplayerAccess.Get<Dictionary<string, float>>(record, "Stats"); _name = MultiplayerAccess.Get<string>(record, "Name");
            _hasPosition = MultiplayerAccess.Get<bool>(record, "HasPosition"); _position = MultiplayerAccess.Get<Vector3>(record, "Position"); _yaw = MultiplayerAccess.Get<float>(record, "Yaw");
            _setPosition = MultiplayerAccess.Set<Vector3>(record, "Position"); _setHasPosition = MultiplayerAccess.Set<bool>(record, "HasPosition");
            _closing = MultiplayerAccess.Get<bool>(Multiplayer.Type("Worlds.WorldSaver"), "<Quitting>k__BackingField");
            _localInCar = MultiplayerAccess.Call<Func<bool>>(Multiplayer.Type("Game.LocalPlayer"), "get_InCar", Type.EmptyTypes);
            _passenger = MultiplayerAccess.Get<Transform>(Multiplayer.Type("Net.WorldObjects"), "passengerSeat");
            Require(Multiplayer.Type("Game.PlayerVitals"), "Capture", "CaptureChoice");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "Stop"), prefix: AccessTools.Method(typeof(MultiplayerSleepers), "Stopping"), postfix: AccessTools.Method(typeof(MultiplayerSleepers), "Stopped")))
                throw new InvalidOperationException("Sleeping-player session cleanup hook failed");
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Changed;
            Scheduler.Interval(Multiplayer.PatchOwner, "Sleeping players replication", .1f, Tick);
            Scheduler.EveryFrame(Multiplayer.PatchOwner, "Sleeping players physics", PhysicsTick, Phase.EarlyFixedUpdate);
            Scheduler.EveryFrame(Multiplayer.PatchOwner, "Sleeping players presentation", LateTick, Phase.LateUpdateLast);
        }
        static void Require(Type type, string method, string postfix)
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(type, method), postfix: AccessTools.Method(typeof(MultiplayerSleepers), postfix)))
                throw new InvalidOperationException("Sleeping-player hook failed: " + method);
        }
        static void CaptureChoice(Dictionary<string, float> __result)
        { if (Multiplayer.Active && __result != null && Services.SleepingBodies != null) __result[ChoiceKey] = Services.SleepingBodies.LocalChoice; }
        static void Stopping() { _stopping = true; StoreAll(); }
        static void Stopped() { _stopping = false; Clear(); }
        static void Changed()
        {
            if (Multiplayer.Active && _epoch == MultiplayerParts.Epoch) return;
            Clear(); _epoch = MultiplayerParts.Epoch;
        }
        static void Clear()
        {
            foreach (var sleeper in Bodies.Values) Destroy(sleeper);
            Bodies.Clear(); Removed.Clear(); _loaded = false; _nextId = 0; _nextFull = _nextPose = _nextCheckpoint = 0; Hint(null);
        }
        static void Destroy(Sleeper sleeper)
        { if (sleeper.Label != null) MultiplayerNames.RemoveSleeper(sleeper.Label); if (sleeper.Body != null) sleeper.Body.Dispose(); sleeper.Body = null; sleeper.Label = null; }
        internal static void Left(ulong peer, object record)
        {
            foreach (var sleeper in Bodies.Values) if (sleeper.Carrier == peer) { sleeper.Carrier = Nobody; sleeper.Place = true; sleeper.NextGround = 0; }
            if (!Multiplayer.Hosting || !Multiplayer.WorldReady || record == null || !_hasPosition(record) || _closing(null) || _stopping) return;
            float health; var stats = _stats(record);
            if (stats.TryGetValue("p.Health", out health) && health < 1) return;
            foreach (var existing in Bodies.Values) if (ReferenceEquals(existing.Record, record)) return;
            stats[SleepingKey] = 1; Add(record, false); _nextFull = 0;
        }
        static void Add(object record, bool restored)
        {
            foreach (var existing in Bodies.Values) if (ReferenceEquals(existing.Record, record)) return;
            if (Bodies.Count >= Limit) { Log.Warn("[Multiplayer] Sleeping-player limit reached (512)"); return; }
            float selected; int choice = _stats(record).TryGetValue(ChoiceKey, out selected) ? Mathf.Clamp(Mathf.RoundToInt(selected), 0, 9) : 1;
            var sleeper = new Sleeper { Id = ++_nextId, Choice = choice, Name = _name(record), Record = record };
            sleeper.CarryHint = "Carry " + sleeper.Name + ": Use";
            sleeper.Positions[0] = _position(record) - (restored ? Vector3.up * .8f : Vector3.zero); sleeper.Rotations[0] = Quaternion.Euler(0, _yaw(record), 0); Bodies.Add(sleeper.Id, sleeper);
        }
        internal static void Waking(object record)
        {
            if (!Multiplayer.Hosting || record == null) return;
            Sleeper found = null;
            foreach (var sleeper in Bodies.Values) if (ReferenceEquals(sleeper.Record, record)) { found = sleeper; break; }
            if (found != null)
            {
                Store(found); Destroy(found); Bodies.Remove(found.Id);
                Multiplayer.Send(Remove, w => { w.Write(_epoch); w.Write(found.Id); });
            }
            _stats(record).Remove(SleepingKey);
        }
        // Called after native RefreshRecords, before copying records into any checkpoint/world snapshot.
        internal static void StoreAll()
        { if (Multiplayer.Hosting) foreach (var sleeper in Bodies.Values) Store(sleeper); }
        static void Store(Sleeper sleeper)
        {
            if (sleeper.Record == null) return;
            if (sleeper.Body != null) sleeper.Positions[0] = Origin.World(sleeper.Body.Bodies[0].position);
            _setPosition(sleeper.Record, sleeper.Positions[0] + Vector3.up * .8f); _setHasPosition(sleeper.Record, true);
        }
        static void Tick()
        {
            if (!Multiplayer.WorldReady || MultiplayerParts.Epoch == 0) return;
            if (_epoch != MultiplayerParts.Epoch) { Clear(); _epoch = MultiplayerParts.Epoch; }
            if (Multiplayer.Hosting && !_loaded)
            {
                _loaded = true;
                foreach (DictionaryEntry entry in _records(null))
                { float asleep; if ((string)entry.Key != "host" && _hasPosition(entry.Value) && _stats(entry.Value).TryGetValue(SleepingKey, out asleep) && asleep == 1) Add(entry.Value, true); }
            }
            foreach (var sleeper in Bodies.Values)
            {
                if (sleeper.Body == null && Services.SleepingBodies != null && Time.unscaledTime >= sleeper.NextBuild)
                {
                    sleeper.NextBuild = Time.unscaledTime + 5f;
                    try
                    {
                        sleeper.Body = Services.SleepingBodies.Create(sleeper.Choice, Origin.Scene(sleeper.Positions[0]), sleeper.Record != null ? _yaw(sleeper.Record) : 0);
                        if (sleeper.Body == null) continue;
                        var tag = new GameObject("Sleeping name"); tag.transform.SetParent(sleeper.Body.Root.transform, false);
                        sleeper.Label = tag.AddComponent<TextMesh>(); var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                        sleeper.Label.font = font; sleeper.Label.fontSize = 64; sleeper.Label.characterSize = .02f; sleeper.Label.anchor = TextAnchor.MiddleCenter;
                        sleeper.Label.text = sleeper.Name + " - Sleeping"; tag.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                        MultiplayerNames.AddSleeper(sleeper.Label);
                        if (Multiplayer.Hosting) Capture(sleeper);
                        else if (sleeper.HasPose) Apply(sleeper, 1f);
                        Log.Always("[Multiplayer] Sleeping body ready: " + sleeper.Name);
                    }
                    catch (Exception error) { Destroy(sleeper); Log.Error("[Multiplayer] Sleeping body could not be built", error); }
                }
            }
            if (!Multiplayer.Hosting || MultiplayerSaving.Frozen || SaveBus.Busy) return;
            bool full = Time.unscaledTime >= _nextFull;
            if (full) _nextFull = Time.unscaledTime + 3f;
            if (full || Time.unscaledTime >= _nextPose)
            {
                _nextPose = Time.unscaledTime + .1f;
                foreach (var sleeper in Bodies.Values) if (sleeper.Body != null) { Capture(sleeper); Publish(sleeper, full); }
            }
            if (Time.unscaledTime >= _nextCheckpoint && Bodies.Count > 0)
            { _nextCheckpoint = Time.unscaledTime + 10f; MultiplayerRecovery.Checkpoint(); }
        }
        static void Capture(Sleeper sleeper)
        {
            var bodies = sleeper.Body.Bodies;
            for (int i = 0; i < BoneCount; i++) { sleeper.Positions[i] = Origin.World(bodies[i].position); sleeper.Rotations[i] = bodies[i].rotation; }
            sleeper.HasPose = true;
        }
        static void Publish(Sleeper sleeper, bool full)
        {
            sleeper.Sequence++;
            Multiplayer.Send(full ? Full : Pose, w =>
            {
                w.Write(_epoch); w.Write(sleeper.Id); w.Write(sleeper.Sequence); w.Write(sleeper.Carrier);
                if (full) { w.Write((byte)sleeper.Choice); w.Write(sleeper.Name); }
                for (int i = 0; i < BoneCount; i++) { Vector(w, sleeper.Positions[i]); Rotation(w, sleeper.Rotations[i]); }
            }, reliable: full);
        }
        static void PhysicsTick()
        {
            if (!Multiplayer.Hosting) return;
            foreach (var sleeper in Bodies.Values)
            {
                if (sleeper.Body == null) continue;
                var bones = sleeper.Body.Bodies; var hip = bones[0]; var carrier = sleeper.Carrier != Nobody ? Multiplayer.PlayerFor(sleeper.Carrier) : null;
                if (sleeper.Carrier != Nobody && (carrier == null || !carrier.activeInHierarchy || (sleeper.Carrier == 0 ? !Multiplayer.LocalTargetable || _localInCar() || _passenger(null) != null : !MultiplayerPlayers.Targetable(sleeper.Carrier) || MultiplayerPlayers.Car(sleeper.Carrier) != null)))
                { sleeper.Carrier = Nobody; sleeper.Place = true; sleeper.NextGround = 0; }
                bool carrying = sleeper.Carrier != Nobody;
                bool simulate = Multiplayer.WorldReady && !MultiplayerSaving.Frozen && !SaveBus.Busy && GameRefs.WorldRunning;
                if (simulate && !carrying && Time.unscaledTime >= sleeper.NextGround)
                {
                    sleeper.NextGround = Time.unscaledTime + .5f; ProbeGround(sleeper);
                }
                simulate &= carrying || sleeper.Supported && GroundReady(sleeper);
                for (int i = 0; i < bones.Length; i++)
                {
                    bool kinematic = !simulate || carrying;
                    if (bones[i].isKinematic != kinematic)
                    { bones[i].isKinematic = kinematic; if (!kinematic) { bones[i].velocity = Vector3.zero; bones[i].angularVelocity = Vector3.zero; } }
                }
                if (simulate && carrying)
                {
                    var target = carrier.transform.position + carrier.transform.forward * 1.4f + Vector3.up * .3f;
                    var delta = Vector3.ClampMagnitude(target - hip.position, .6f);
                    for (int i = 0; i < bones.Length; i++) bones[i].MovePosition(bones[i].position + delta);
                }
            }
        }
        static bool GroundReady(Sleeper sleeper)
        {
            var bones = sleeper.Body.Bodies;
            for (int i = 0; i < BoneCount; i++)
            {
                var ground = sleeper.Ground[i]; var tile = sleeper.Tiles[i];
                if (ground == null || !ground.enabled || !ground.gameObject.activeInHierarchy || ground.name == "Draft Terrain"
                    || tile != null && (tile.main == null || !tile.main.applyReady || tile.main.terrain == null || ground.gameObject != tile.main.terrain.gameObject)) return false;
                var bounds = ground.bounds; var p = bones[i].position;
                if (p.x < bounds.min.x || p.x > bounds.max.x || p.z < bounds.min.z || p.z > bounds.max.z) return false;
            }
            return true;
        }
        static void ProbeGround(Sleeper sleeper)
        {
            var bones = sleeper.Body.Bodies; float lift = 0; sleeper.Supported = false;
            for (int i = 0; i < BoneCount; i++)
            {
                RaycastHit hit;
                if (!Physics.Raycast(bones[i].position + Vector3.up * 1500f, Vector3.down, out hit, 3000f, 1 << 14, QueryTriggerInteraction.Ignore) || !(hit.collider is TerrainCollider)) return;
                var ground = (TerrainCollider)hit.collider;
                if (sleeper.Ground[i] != ground) { sleeper.Ground[i] = ground; sleeper.Tiles[i] = ground.GetComponentInParent<MapMagic.Terrains.TerrainTile>(); }
                // New/dropped poses may start partly under the surface. Lift the whole body before releasing physics.
                if (sleeper.Place || bones[i].position.y < hit.point.y - .25f) lift = Mathf.Max(lift, hit.point.y + .05f - sleeper.Body.Colliders[i].bounds.min.y);
            }
            if (!GroundReady(sleeper)) return;
            if (lift > 0)
            { foreach (var bone in bones) { bone.isKinematic = true; bone.position += Vector3.up * lift; } Physics.SyncTransforms(); }
            sleeper.Place = false; sleeper.Supported = true;
        }
        static void Apply(Sleeper sleeper, float blend)
        {
            var bodies = sleeper.Body.Bodies;
            for (int i = 0; i < BoneCount; i++)
            { bodies[i].position = Vector3.Lerp(bodies[i].position, Origin.Scene(sleeper.Positions[i]), blend); bodies[i].rotation = Quaternion.Slerp(bodies[i].rotation, sleeper.Rotations[i], blend); }
        }
        static void LateTick()
        {
            if (!Multiplayer.WorldReady) { Hint(null); return; }
            foreach (var sleeper in Bodies.Values) if (sleeper.Body != null)
            {
                if (Multiplayer.Client && sleeper.HasPose) Apply(sleeper, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f));
                sleeper.Body.Apply();
                if (sleeper.Label != null) sleeper.Label.transform.position = sleeper.Body.Bodies[0].position + Vector3.up * .8f;
            }
            Interaction();
        }
        static void Interaction()
        {
            if (!GameRefs.InGame || !Multiplayer.LocalTargetable || Multiplayer.Typing || EditorSession.Busy || ApocasetterBridge.InputBlocked || !Application.isFocused || MultiplayerSaving.Frozen || _localInCar() || _passenger(null) != null)
            { Hint(null); return; }
            Sleeper selected = null; bool carrying = false;
            foreach (var sleeper in Bodies.Values) if (sleeper.Carrier == Multiplayer.LocalPeer) { selected = sleeper; carrying = true; break; }
            var camera = GameRefs.MainCamera;
            if (selected == null && camera != null)
            {
                var ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0)); float best = 8f;
                foreach (var sleeper in Bodies.Values)
                {
                    if (sleeper.Body == null || sleeper.Carrier != Nobody) continue;
                    if (GameRefs.Player == null || (sleeper.Body.Bodies[0].position - GameRefs.Player.transform.position).sqrMagnitude > 16f) continue;
                    var delta = sleeper.Body.Bodies[0].position - ray.origin; float distance = Vector3.Dot(delta, ray.direction);
                    if (distance < 0 || distance > best || Vector3.Cross(delta, ray.direction).sqrMagnitude > .55f * .55f) continue;
                    if (Physics.Raycast(ray.origin, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    selected = sleeper; best = distance;
                }
            }
            Hint(selected == null ? null : carrying ? "Drop sleeping player: Use" : selected.CarryHint);
            if (selected == null || !Input.GetButtonDown("Use") || Time.unscaledTime < _nextCommand) return;
            _nextCommand = Time.unscaledTime + .3f;
            if (Multiplayer.Hosting) Command(0, selected.Id, !carrying);
            else { int id = selected.Id; Multiplayer.Send(Carry, w => { w.Write(_epoch); w.Write(id); w.Write(!carrying); }); }
        }
        static void Hint(string text)
        {
            if (_hint == null && Time.unscaledTime >= _nextHint)
            { _nextHint = Time.unscaledTime + 2f; var canvas = GameObject.Find("Canvas"); var target = canvas != null ? canvas.transform.Find("ItemGrab") : null; _hint = target != null ? target.GetComponent<Text>() : null; }
            if (_hint != null && (text != null || _hint.text == _hintText)) _hint.text = text ?? "";
            _hintText = text;
        }
        static void Command(ulong from, int id, bool take)
        {
            Sleeper sleeper; var player = Multiplayer.PlayerFor(from);
            if (player == null || !Bodies.TryGetValue(id, out sleeper) || sleeper.Body == null || MultiplayerSaving.Frozen || SaveBus.Busy) return;
            if (!take) { if (sleeper.Carrier == from) { sleeper.Carrier = Nobody; sleeper.Place = true; sleeper.NextGround = 0; Store(sleeper); MultiplayerRecovery.Checkpoint(); Capture(sleeper); Publish(sleeper, true); } return; }
            if (sleeper.Carrier != Nobody || (from == 0 ? !Multiplayer.LocalTargetable || _localInCar() || _passenger(null) != null : !MultiplayerPlayers.Targetable(from) || MultiplayerPlayers.Car(from) != null)) return;
            foreach (var other in Bodies.Values) if (other.Carrier == from) return;
            var delta = sleeper.Body.Bodies[0].position - player.transform.position;
            if (delta.sqrMagnitude > 16f || Physics.Raycast(player.transform.position, delta.normalized, delta.magnitude, (1 << 0) | (1 << 8) | (1 << 14) | (1 << 16), QueryTriggerInteraction.Ignore)) return;
            sleeper.Carrier = from; Capture(sleeper); Publish(sleeper, true);
        }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void Vector(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 Vector(BinaryReader r)
        { var v = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) throw new InvalidDataException("Invalid sleeper position"); return v; }
        static void Rotation(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        static Quaternion Rotation(BinaryReader r)
        { var q = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); float length = Quaternion.Dot(q, q); if (!Finite(length) || length < .5f || length > 1.5f) throw new InvalidDataException("Invalid sleeper rotation"); return q.normalized; }
        static void Receive(ulong from, byte kind, BinaryReader r)
        {
            if (kind < Full || kind > Carry || r.ReadUInt64() != MultiplayerParts.Epoch || MultiplayerParts.Epoch == 0) return;
            int id = r.ReadInt32(); if (id <= 0) return;
            if (kind == Carry)
            { bool take = r.ReadBoolean(); if (Multiplayer.Hosting && from != 0 && r.BaseStream.Position == r.BaseStream.Length) Command(from, id, take); return; }
            if (!Multiplayer.Client || from != 0) return;
            if (_epoch != MultiplayerParts.Epoch) { Clear(); _epoch = MultiplayerParts.Epoch; }
            if (kind == Remove)
            {
                if (r.BaseStream.Position != r.BaseStream.Length) return;
                Sleeper old; if (Bodies.TryGetValue(id, out old)) { Destroy(old); Bodies.Remove(id); }
                Removed.Add(id); return;
            }
            uint sequence = r.ReadUInt32(); ulong carrier = r.ReadUInt64(); Sleeper sleeper; bool exists = Bodies.TryGetValue(id, out sleeper);
            int choice = kind == Full ? r.ReadByte() : exists ? sleeper.Choice : 1;
            string name = kind == Full ? r.ReadString() : exists ? sleeper.Name : "";
            if (choice >= 10 || name.Length > 48 || Removed.Contains(id) || exists && sequence <= sleeper.Sequence || !exists && kind != Full || !exists && Bodies.Count >= Limit) return;
            for (int i = 0; i < BoneCount; i++) { IncomingPositions[i] = Vector(r); IncomingRotations[i] = Rotation(r); }
            if (r.BaseStream.Position != r.BaseStream.Length) return;
            if (!exists) { sleeper = new Sleeper { Id = id, Choice = choice, Name = name, CarryHint = "Carry " + name + ": Use" }; Bodies.Add(id, sleeper); }
            sleeper.Sequence = sequence; sleeper.Carrier = carrier; sleeper.HasPose = true;
            Array.Copy(IncomingPositions, sleeper.Positions, BoneCount); Array.Copy(IncomingRotations, sleeper.Rotations, BoneCount);
        }
    }
}
