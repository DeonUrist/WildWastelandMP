using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Native MP owns object movement, attachments, animation/sounds and damage. This carries WW's manual bone poses.
    internal static class MultiplayerActors
    {
        const int MaxNodes = 128, NodesPerPacket = 40, MaxPath = 600;
        sealed class Rig
        {
            internal GameObject Root; internal int Id; internal uint Generation, Frame;
            internal Transform[] Nodes; internal string[] Paths;
            internal Vector3[] Positions, Scales; internal Quaternion[] Rotations; internal bool[] Active, Received;
            internal uint[] Frames;
            internal Vector3[] ShownPositions, ShownScales; internal Quaternion[] ShownRotations; internal bool[] Showing;
            internal Animator[] Animators; internal bool[] AnimatorEnabled; internal bool AnimatorsHeld;
            internal Transform[] Candidates;
            internal float LastSeen, NextRequest;
            internal int ManifestCount;
            internal bool Seated, AllBones;
        }
        static readonly List<Rig> Sending = new List<Rig>();
        static readonly Dictionary<GameObject, Rig> ByRoot = new Dictionary<GameObject, Rig>();
        static readonly Dictionary<int, Rig> Receiving = new Dictionary<int, Rig>();
        static readonly List<int> Removed = new List<int>();
        static readonly Dictionary<ulong, float> ManifestRequests = new Dictionary<ulong, float>();
        static uint _generation;
        static int _cursor;
        static float _nextSend, _nextSweep;
        internal static void Install(IModule owner)
        {
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
            Scheduler.EveryFrame(owner, "Multiplayer crew poses", Late, Phase.LateUpdateLast);
            var world = Multiplayer.Type("Net.WorldObjects");
            if (!Patches.Patch(owner, AccessTools.Method(world, "IsCreature"), postfix: AccessTools.Method(typeof(MultiplayerActors), "CreatureKind"))
                || !Patches.Patch(owner, AccessTools.Method(world, "IsCorpse"), postfix: AccessTools.Method(typeof(MultiplayerActors), "CorpseKind")))
                throw new InvalidOperationException("MP corpse classification hooks failed");
        }
        static bool JointedCorpse(GameObject actor)
        { return actor != null && actor.name.IndexOf("_Dead", StringComparison.OrdinalIgnoreCase) >= 0 && actor.GetComponentInChildren<Joint>(true) != null; }
        static void CreatureKind(GameObject __0, ref bool __result) { if (__result && JointedCorpse(__0)) __result = false; }
        static void CorpseKind(GameObject __0, ref bool __result) { if (!__result && JointedCorpse(__0)) __result = true; }
        static void Release(Rig rig)
        {
            if (!rig.AnimatorsHeld || rig.Animators == null) return;
            for (int i = 0; i < rig.Animators.Length; i++) if (rig.Animators[i] != null) rig.Animators[i].enabled = rig.AnimatorEnabled[i];
            rig.AnimatorsHeld = false;
        }
        static void Reset()
        {
            foreach (var rig in Receiving.Values) Release(rig);
            Sending.Clear(); ByRoot.Clear(); Receiving.Clear(); Removed.Clear(); ManifestRequests.Clear(); _cursor = 0; _nextSend = _nextSweep = 0;
        }
        internal static void Register(GameObject actor, Transform[] nodes, bool seated, bool allBones = false)
        {
            if (!Multiplayer.Hosting || actor == null || nodes == null) return;
            var selected = new List<Transform>(); var paths = new List<string>();
            foreach (var node in nodes)
            {
                if (node == null || node == actor.transform || selected.Count >= MaxNodes) continue;
                // Bone nodes plus held model roots: render-only state stays local to this replicated actor.
                if (!allBones && !node.name.StartsWith("mixamorig:", StringComparison.Ordinal) && (node.parent == null || !node.parent.name.EndsWith("Hand", StringComparison.Ordinal))) continue;
                string path = Relative(actor.transform, node); if (Encoding.UTF8.GetByteCount(path) > MaxPath) continue;
                selected.Add(node); paths.Add(path);
            }
            if (selected.Count == 0) return;
            Rig existing;
            if (ByRoot.TryGetValue(actor, out existing)) Sending.Remove(existing);
            var rig = new Rig { Root = actor, Generation = ++_generation, Nodes = selected.ToArray(), Paths = paths.ToArray(), Seated = seated, AllBones = allBones };
            ByRoot[actor] = rig; Sending.Add(rig);
        }
        internal static void Refresh(GameObject actor)
        { Rig rig; if (actor != null && ByRoot.TryGetValue(actor, out rig)) Register(actor, rig.AllBones ? rig.Nodes : actor.GetComponentsInChildren<Transform>(true), rig.Seated, rig.AllBones); }
        internal static void Unregister(GameObject actor)
        { Rig rig; if (actor != null && ByRoot.TryGetValue(actor, out rig)) { Sending.Remove(rig); ByRoot.Remove(actor); } }
        static string Relative(Transform root, Transform node)
        {
            var path = new StringBuilder(node.name);
            for (var parent = node.parent; parent != null && parent != root; parent = parent.parent) { path.Insert(0, '/'); path.Insert(0, parent.name); }
            return path.ToString();
        }
        static void Late()
        {
            if (!Multiplayer.WorldReady) return;
            float now = Time.unscaledTime;
            if (Multiplayer.Hosting)
            {
                if (now < _nextSend || Sending.Count == 0) return;
                _nextSend = now + .1f; int budget = 8, visited = 0;
                while (budget > 0 && visited++ < Sending.Count)
                {
                    if (_cursor >= Sending.Count) _cursor = 0;
                    var rig = Sending[_cursor++];
                    if (rig.Root == null || !rig.Root.activeInHierarchy) continue;
                    if (rig.Id == 0) rig.Id = Multiplayer.ObjectId(rig.Root);
                    if (rig.Id == 0) continue;
                    int packets = (rig.Nodes.Length + NodesPerPacket - 1) / NodesPerPacket;
                    if (packets > budget) break;
                    rig.Frame++;
                    for (int start = 0; start < rig.Nodes.Length; start += NodesPerPacket)
                    {
                        int offset = start, count = Math.Min(NodesPerPacket, rig.Nodes.Length - start);
                        Multiplayer.Send(33, w => WritePose(w, rig, offset, count), reliable: false); budget--;
                    }
                }
            }
            else foreach (var pair in Receiving)
            {
                var rig = pair.Value; if (rig.Root == null || now - rig.LastSeen > 1.5f) { Release(rig); continue; }
                if (rig.ManifestCount < rig.Nodes.Length)
                {
                    if (now >= rig.NextRequest) { rig.NextRequest = now + 1f; int id = rig.Id; Multiplayer.Send(31, w => w.Write(id)); }
                }
                for (int i = 0; i < rig.Nodes.Length; i++)
                {
                    var node = rig.Nodes[i]; if (node == null || !rig.Received[i]) continue;
                    float blend = rig.Showing[i] ? 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f) : 1f;
                    rig.ShownPositions[i] = Vector3.Lerp(rig.ShownPositions[i], rig.Positions[i], blend);
                    rig.ShownRotations[i] = Quaternion.Slerp(rig.ShownRotations[i], rig.Rotations[i], blend);
                    rig.ShownScales[i] = Vector3.Lerp(rig.ShownScales[i], rig.Scales[i], blend); rig.Showing[i] = true;
                    node.localPosition = rig.ShownPositions[i]; node.localRotation = rig.ShownRotations[i]; node.localScale = rig.ShownScales[i];
                    if (node.gameObject.activeSelf != rig.Active[i]) node.gameObject.SetActive(rig.Active[i]);
                }
            }
            if (now < _nextSweep) return;
            _nextSweep = now + 1f;
            for (int i = Sending.Count - 1; i >= 0; i--) if (Sending[i].Root == null) { ByRoot.Remove(Sending[i].Root); Sending.RemoveAt(i); }
            Removed.Clear(); foreach (var pair in Receiving) if (pair.Value.Root == null || now - pair.Value.LastSeen > 10f) Removed.Add(pair.Key);
            foreach (int id in Removed) { Release(Receiving[id]); Receiving.Remove(id); }
        }
        static short Encode(float value, float scale) { return (short)Mathf.Clamp(Mathf.RoundToInt(value * scale), short.MinValue, short.MaxValue); }
        static void Vector(BinaryWriter writer, Vector3 value)
        { writer.Write(Encode(value.x, 1000)); writer.Write(Encode(value.y, 1000)); writer.Write(Encode(value.z, 1000)); }
        static Vector3 Vector(BinaryReader reader) { return new Vector3(reader.ReadInt16() * .001f, reader.ReadInt16() * .001f, reader.ReadInt16() * .001f); }
        static void WritePose(BinaryWriter writer, Rig rig, int start, int count)
        {
            writer.Write(rig.Id); writer.Write(rig.Generation); writer.Write(rig.Frame); writer.Write(rig.Seated); writer.Write((byte)rig.Nodes.Length); writer.Write((byte)start); writer.Write((byte)count);
            for (int i = start; i < start + count; i++)
            {
                var node = rig.Nodes[i]; writer.Write(node != null && node.gameObject.activeSelf);
                Vector(writer, node != null ? node.localPosition : Vector3.zero);
                var q = node != null ? node.localRotation : Quaternion.identity;
                writer.Write(Encode(q.x, 32767)); writer.Write(Encode(q.y, 32767)); writer.Write(Encode(q.z, 32767)); writer.Write(Encode(q.w, 32767));
                Vector(writer, node != null ? node.localScale : Vector3.one);
            }
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == 31 && Multiplayer.Hosting)
            {
                int id = reader.ReadInt32(); var root = Multiplayer.ObjectById(id); Rig rig;
                if (root == null || !ByRoot.TryGetValue(root, out rig)) return;
                float next; if (ManifestRequests.TryGetValue(from, out next) && Time.unscaledTime < next) return;
                // Queue exhaustion now ends the connection: defer the reply (the guest re-requests) instead of bursting a rig past capacity.
                if (!MultiplayerReliable.CanQueue(from, rig.Paths.Length)) return;
                ManifestRequests[from] = Time.unscaledTime + .05f;
                for (int start = 0; start < rig.Paths.Length; start++)
                {
                    int offset = start, count = 1;
                    Multiplayer.Send(32, w => { w.Write(id); w.Write(rig.Generation); w.Write(rig.Seated); w.Write((byte)rig.Paths.Length); w.Write((byte)offset); w.Write((byte)count); for (int i = offset; i < offset + count; i++) w.Write(rig.Paths[i]); }, from);
                }
                return;
            }
            if ((kind != 32 && kind != 33) || !Multiplayer.Client || from != 0) return;
            int objectId = reader.ReadInt32(); uint generation = reader.ReadUInt32(); uint frame = kind == 33 ? reader.ReadUInt32() : 0;
            bool seated = reader.ReadBoolean();
            int total = reader.ReadByte(), begin = reader.ReadByte(), amount = reader.ReadByte();
            if (total < 1 || total > MaxNodes || begin + amount > total || amount < 1 || (kind == 32 ? amount > 1 : amount > NodesPerPacket)) return;
            var actor = Multiplayer.ObjectById(objectId); if (actor == null) return;
            Rig copy;
            if (Receiving.TryGetValue(objectId, out copy) && copy.Root == actor && generation < copy.Generation) return;
            if (!Receiving.TryGetValue(objectId, out copy) || copy.Root != actor || copy.Generation != generation)
            {
                if (copy != null) Release(copy);
                copy = new Rig { Id = objectId, Generation = generation, Root = actor, Nodes = new Transform[total], Positions = new Vector3[total], Scales = new Vector3[total], Rotations = new Quaternion[total], Active = new bool[total], Received = new bool[total], Frames = new uint[total] };
                copy.ShownPositions = new Vector3[total]; copy.ShownScales = new Vector3[total]; copy.ShownRotations = new Quaternion[total]; copy.Showing = new bool[total];
                copy.Animators = actor.GetComponentsInChildren<Animator>(true); copy.AnimatorEnabled = new bool[copy.Animators.Length];
                for (int i = 0; i < copy.Animators.Length; i++) copy.AnimatorEnabled[i] = copy.Animators[i].enabled;
                copy.Candidates = actor.GetComponentsInChildren<Transform>(true);
                Receiving[objectId] = copy;
                if (seated) Multiplayer.PresentCrew(actor);
            }
            if (copy.Nodes.Length != total) return; copy.LastSeen = Time.unscaledTime;
            if (kind == 32)
            {
                if (copy.Paths == null) copy.Paths = new string[total];
                for (int i = begin; i < begin + amount; i++)
                {
                    string path = reader.ReadString(); if (Encoding.UTF8.GetByteCount(path) > MaxPath || path.Contains("..") || path.StartsWith("/", StringComparison.Ordinal)) return;
                    if (copy.Paths[i] == null) copy.ManifestCount++;
                    copy.Paths[i] = path; copy.Nodes[i] = actor.transform.Find(path);
                    if (copy.Nodes[i] == null)
                    {
                        int slash = path.LastIndexOf('/'); string name = slash >= 0 ? path.Substring(slash + 1) : path;
                        var parent = slash >= 0 ? actor.transform.Find(path.Substring(0, slash)) : actor.transform;
                        if (parent == null || name.StartsWith("mixamorig:", StringComparison.Ordinal)) continue;
                        Transform match = null; bool ambiguous = false;
                        foreach (var candidate in copy.Candidates) if (candidate != null && candidate.name == name) { if (match != null) { ambiguous = true; break; } match = candidate; }
                        if (!ambiguous && match != null) { match.SetParent(parent, false); copy.Nodes[i] = match; }
                    }
                }
                return;
            }
            if (reader.BaseStream.Length - reader.BaseStream.Position != amount * 21) return;
            for (int i = begin; i < begin + amount; i++)
            {
                bool active = reader.ReadBoolean(); var position = Vector(reader);
                var q = new Quaternion(reader.ReadInt16() / 32767f, reader.ReadInt16() / 32767f, reader.ReadInt16() / 32767f, reader.ReadInt16() / 32767f); var scale = Vector(reader);
                if (frame < copy.Frames[i]) continue;
                float length = Mathf.Sqrt(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w); if (length < .5f) continue;
                copy.Frames[i] = frame; copy.Positions[i] = position; copy.Rotations[i] = new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length);
                copy.Scales[i] = scale; copy.Active[i] = active; copy.Received[i] = true;
            }
            // The custom WW rig is the single pose writer. Native Animator playback resumes if its stream expires.
            if (!copy.AnimatorsHeld && copy.ManifestCount == total)
            { foreach (var animator in copy.Animators) if (animator != null) animator.enabled = false; copy.AnimatorsHeld = true; }
        }
    }
}
