using System;
using System.Collections.Generic;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Modules.Guns
{
    internal static partial class Tracers
    {
        private struct LandedBolt { public Transform Visual, Owner; public string Bone; public float Until; }
        private static Mesh _boltSkin;
        private static readonly List<SkinnedMeshRenderer> _boltSkins = new List<SkinnedMeshRenderer>();
        private static readonly List<Vector3> _boltVertices = new List<Vector3>();
        private static readonly List<int> _boltTriangles = new List<int>();
        private static readonly Dictionary<Mesh, BoneWeight[]> _boltWeights = new Dictionary<Mesh, BoneWeight[]>();

        // Collision capsules sit outside the skin. Anchor to the visible surface and the bone that deforms it.
        private static Transform BoltSurface(Transform target, ref Vector3 point, Vector3 dir, out Transform owner, out int layer)
        {
            owner = Creature(target); layer = target.gameObject.layer;
            if (owner == null) return target;
            for (var p = owner; p != null; p = p.parent)
            {
                p.GetComponentsInChildren<SkinnedMeshRenderer>(true, _boltSkins);
                if (_boltSkins.Count > 0) break;
            }
            if (_boltSkin == null) _boltSkin = new Mesh { name = "Crossbow skin intersection", hideFlags = HideFlags.HideAndDontSave };
            Transform anchor = target; Vector3 surface = point, origin = point - dir * 2f;
            float best = 4f, nearest = float.MaxValue; bool intersected = false;
            foreach (var skin in _boltSkins)
            {
                if (skin == null || skin.sharedMesh == null || !skin.enabled || skin.forceRenderingOff) continue;
                skin.BakeMesh(_boltSkin); _boltSkin.GetVertices(_boltVertices);
                var tr = skin.transform; Vector3 from = tr.InverseTransformPoint(origin), direction = tr.InverseTransformVector(dir);
                int vertex = -1; Vector3 candidate = point; bool found = false;
                for (int sub = 0; sub < _boltSkin.subMeshCount; sub++)
                {
                    _boltSkin.GetTriangles(_boltTriangles, sub);
                    for (int i = 0; i + 2 < _boltTriangles.Count; i += 3)
                    {
                        int a = _boltTriangles[i], b = _boltTriangles[i + 1], c = _boltTriangles[i + 2];
                        Vector3 edge = _boltVertices[b] - _boltVertices[a], edge2 = _boltVertices[c] - _boltVertices[a];
                        Vector3 cross = Vector3.Cross(direction, edge2); float det = Vector3.Dot(edge, cross);
                        if (Mathf.Abs(det) < 1e-8f) continue;
                        Vector3 offset = from - _boltVertices[a]; float u = Vector3.Dot(offset, cross) / det;
                        if (u < 0f || u > 1f) continue;
                        Vector3 q = Vector3.Cross(offset, edge); float v = Vector3.Dot(direction, q) / det;
                        if (v < 0f || u + v > 1f) continue;
                        float distance = Vector3.Dot(edge2, q) / det;
                        if (distance < 0f || distance >= best) continue;
                        best = distance; candidate = origin + dir * distance; found = intersected = true;
                        vertex = u > v && u > 1f - u - v ? b : v > 1f - u - v ? c : a;
                    }
                }
                // Thick NPC projectiles can graze the skin without the centre ray intersecting it.
                if (!intersected)
                    for (int i = 0; i < _boltVertices.Count; i++)
                    {
                        Vector3 pos = tr.TransformPoint(_boltVertices[i]); float distance = (pos - point).sqrMagnitude;
                        if (distance >= nearest) continue;
                        nearest = distance; candidate = pos; vertex = i; found = true;
                    }
                if (!found) continue;
                surface = candidate; anchor = tr; layer = skin.gameObject.layer;
                var bones = skin.bones;
                if (!skin.sharedMesh.isReadable)
                {
                    float closest = float.MaxValue;
                    foreach (var bone in bones)
                    {
                        if (bone == null) continue;
                        float distance = (bone.position - surface).sqrMagnitude;
                        if (distance < closest) { closest = distance; anchor = bone; }
                    }
                    continue;
                }
                BoneWeight[] weights;
                if (!_boltWeights.TryGetValue(skin.sharedMesh, out weights)) { weights = skin.sharedMesh.boneWeights; _boltWeights[skin.sharedMesh] = weights; }
                if (vertex < 0 || vertex >= weights.Length) continue;
                var weight = weights[vertex]; int index = weight.boneIndex0; float strongest = weight.weight0;
                if (weight.weight1 > strongest) { index = weight.boneIndex1; strongest = weight.weight1; }
                if (weight.weight2 > strongest) { index = weight.boneIndex2; strongest = weight.weight2; }
                if (weight.weight3 > strongest) index = weight.boneIndex3;
                if (index >= 0 && index < bones.Length && bones[index] != null) anchor = bones[index];
            }
            point = surface;
            return anchor;
        }

        internal static void BoltCorpseSpawned(CreateObject action, GameObject made)
        {
            RememberBoltCorpse(action, made);
            if (Core.Multiplayer.Client) return;
            if (_landedBolts.Count == 0 || action.Fsm == null || action.Fsm.Name != "Health" || made == null) return;
            var owner = action.Fsm.GameObject.transform; bool attached = false;
            foreach (var b in _landedBolts) if (b.Owner == owner && b.Visual != null) { attached = true; break; }
            if (!attached || made.name.IndexOf("_Dead", StringComparison.OrdinalIgnoreCase) < 0) return;
            for (int i = 0; i < _landedBolts.Count; i++)
            {
                var b = _landedBolts[i];
                if (b.Owner != owner || b.Visual == null) continue;
                var bone = b.Bone == "" ? made.transform : b.Bone != null ? made.transform.Find(b.Bone) : null;
                if (bone == null) { Core.Log.Warn("[Guns] Crossbow bolt: corpse has no matching bone " + b.Bone); continue; }
                b.Visual.SetParent(bone, false); b.Visual.gameObject.layer = bone.gameObject.layer;
                b.Owner = made.transform; _landedBolts[i] = b;
                if (Core.Log.Verbose) GunsModule.Verbose("Crossbow bolt: transferred to corpse bone " + b.Bone + " at the same local position and angle");
            }
        }

        private static string BoltBonePath(Transform bone, Transform owner)
        {
            if (bone == owner) return "";
            if (!bone.IsChildOf(owner)) return null;
            string path = bone.name;
            for (var p = bone.parent; p != owner; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
