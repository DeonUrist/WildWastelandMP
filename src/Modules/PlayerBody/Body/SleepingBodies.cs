using System;
using System.Collections.Generic;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.PlayerBody
{
    // Config-lifetime provider: sleepers remain available when local player animation is switched off.
    internal sealed class SleepingBodies : ISleepingBodies
    {
        public int LocalChoice { get { return (int)PlayerBodyModule.Character.Value; } }
        public ISleepingBody Create(int choice, Vector3 position, float yaw)
        {
            var source = MultiplayerModels.Source(choice); if (source == null) return null;
            return new SleepingBody(source, choice, position, yaw);
        }
    }
    internal sealed class SleepingBody : ISleepingBody
    {
        readonly GameObject _root;
        readonly Ragdoll _ragdoll;
        readonly Rigidbody[] _bodies;
        readonly Material _material;
        public GameObject Root { get { return _root; } }
        public Rigidbody[] Bodies { get { return _bodies; } }
        public SleepingBody(Animator source, int choice, Vector3 position, float yaw)
        {
            _root = new GameObject("WWMP Sleeping body"); _root.SetActive(false);
            try
            {
                var model = UnityEngine.Object.Instantiate(source.gameObject, _root.transform, false);
                foreach (var component in model.GetComponentsInChildren<Component>(true))
                    if (!(component is Transform) && !(component is Animator) && !(component is SkinnedMeshRenderer) && !(component is MeshRenderer) && !(component is MeshFilter))
                        UnityEngine.Object.DestroyImmediate(component);
                foreach (var prop in model.GetComponentsInChildren<MeshFilter>(true)) UnityEngine.Object.DestroyImmediate(prop.gameObject);
                var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var bone in model.GetComponentsInChildren<Transform>(true))
                { bone.gameObject.layer = 2; if (bone.name.StartsWith("mixamorig:", StringComparison.Ordinal)) bones[bone.name] = bone; }
                SkinnedMeshRenderer skin = null;
                foreach (var candidate in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (candidate.bones.Length >= 20) { skin = candidate; break; }
                if (skin == null || !bones.ContainsKey("mixamorig:Hips")) throw new InvalidOperationException("Sleeper skeleton is incomplete");
                foreach (var other in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (other != skin) UnityEngine.Object.DestroyImmediate(other.gameObject);
                if (MultiplayerModels.CustomSkin(choice)) _material = Model.RemoteSkin(skin, choice);
                var animator = model.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.enabled = false;
                skin.updateWhenOffscreen = true;
                model.transform.rotation = Quaternion.Euler(80f, yaw, 0);
                model.transform.position += position - bones["mixamorig:Hips"].position;
                _root.SetActive(true);
                _ragdoll = new Ragdoll(bones, Vector3.zero, null, true); _bodies = _ragdoll.Bodies;
                foreach (var body in _bodies) body.isKinematic = true;
            }
            catch { Dispose(); throw; }
        }
        public void Apply() { _ragdoll.Apply(); }
        public void Dispose()
        {
            if (_ragdoll != null) _ragdoll.Destroy();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
