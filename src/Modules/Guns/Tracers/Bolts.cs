using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Modules.Guns
{
    internal static partial class Tracers
    {
        private static Mesh _boltMesh;
        private static Material[] _boltMaterials;
        private static Vector3 _boltScale;
        private static readonly List<LandedBolt> _landedBolts = new List<LandedBolt>();

        // Native crossbow arrow: a plain mesh pointing along +Z, also present in NPC props.
        private static void CacheBoltModel(Transform weapon)
        {
            if (_boltMesh != null || weapon == null) return;
            foreach (var mf in weapon.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name != "arrow" || mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                _boltMesh = mf.sharedMesh; _boltMaterials = mr.sharedMaterials;
                var scale = mf.transform.lossyScale;
                _boltScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                return;
            }
            Core.Log.Warn("[Guns] Crossbow bolt: native arrow mesh missing on " + weapon.name);
        }

        private static void LaunchBolt(ref Shot s)
        {
            if (!s.Bolt) return;
            if (_boltMesh == null) { var arm = PlayerRig.WeaponsArm; if (arm != null) CacheBoltModel(arm); }
            if (_boltMesh == null) return;
            var go = new GameObject("WildWasteland.Guns.Bolt") { hideFlags = HideFlags.DontSave, layer = 9 };
            go.AddComponent<MeshFilter>().sharedMesh = _boltMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = _boltMaterials;
            s.Visual = go.transform; s.Visual.localScale = _boltScale;
            MoveBolt(ref s);
        }

        private static void MoveBolt(ref Shot s) { MoveBoltAt(ref s, s.Pos); }
        private static void MoveBoltAt(ref Shot s, Vector3 pos)
        {
            if (s.Visual != null) s.Visual.SetPositionAndRotation(pos - s.Dir * (_boltMesh.bounds.max.z * _boltScale.z), Quaternion.LookRotation(s.Dir));
        }

        private static void LandBolt(ref Shot s, Vector3 point, Vector3 normal, Transform target, bool soft)
        {
            if (s.Visual == null) return;
            var bolt = s.Visual; var bounds = _boltMesh.bounds;
            Transform owner = null, anchor = target;
            if (soft)
            {
                int layer;
                anchor = BoltSurface(target, ref point, s.Dir, out owner, out layer);
                bolt.gameObject.layer = layer;
                // Mesh centre on the surface puts exactly half the shaft inside it.
                bolt.SetPositionAndRotation(point - Quaternion.LookRotation(s.Dir) * Vector3.Scale(bounds.center, _boltScale), Quaternion.LookRotation(s.Dir));
                bolt.SetParent(anchor, true);
            }
            else
            {
                s.Pos = point + normal * 0.03f; MoveBolt(ref s);
                if (!Core.Multiplayer.Client)
                {
                var physics = new GameObject("BoltPhysics") { layer = 2 }; physics.transform.SetParent(bolt, false);
                var col = physics.AddComponent<CapsuleCollider>();
                col.direction = 2; col.center = bounds.center; col.height = bounds.size.z;
                col.radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
                var rb = bolt.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.03f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.velocity = Vector3.Reflect(s.Dir, normal) * Mathf.Min(s.Speed * 0.15f, 8f) + normal;
                rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 10f;
                }
            }
            _landedBolts.Add(new LandedBolt { Visual = bolt, Owner = owner, Bone = owner != null ? BoltBonePath(anchor, owner) : null, Until = Time.time + 15f });
            NetworkBoltLanded(ref s, bolt, anchor, soft, normal);
            s.Visual = null;
            if (Core.Log.Verbose) GunsModule.Verbose("Crossbow bolt: " + (soft ? "embedded halfway in " : "ricochet from ") + (target != null ? target.name : "world") + " (15 s)");
        }

        private static void TickBolts()
        {
            TickNetworkBolts();
            for (int i = _landedBolts.Count - 1; i >= 0; i--)
            {
                var b = _landedBolts[i];
                if (b.Visual != null && Time.time < b.Until) continue;
                if (b.Visual != null) UnityEngine.Object.Destroy(b.Visual.gameObject);
                _landedBolts.RemoveAt(i);
            }
        }

        private static void ClearBolts()
        {
            foreach (var s in _shots) if (s.Visual != null) UnityEngine.Object.Destroy(s.Visual.gameObject);
            foreach (var b in _landedBolts) if (b.Visual != null) UnityEngine.Object.Destroy(b.Visual.gameObject);
            _landedBolts.Clear(); _boltMesh = null; _boltMaterials = null;
            ResetNetworkBolts();
            _boltSkins.Clear(); _boltVertices.Clear(); _boltTriangles.Clear(); _boltWeights.Clear();
            if (_boltSkin != null) UnityEngine.Object.Destroy(_boltSkin); _boltSkin = null;
        }
    }
}
