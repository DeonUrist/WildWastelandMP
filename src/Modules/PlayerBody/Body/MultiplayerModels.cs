using System;
using System.Collections.Generic;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.PlayerBody
{
    internal static class MultiplayerModels
    {
        static readonly string[] Names = { "Flexa", "Flexa", "Merchant", "Bodyguard", "Sprokka", "Pistoleer", "Mechanic", "Organic_Mechanic", "Flexa", "Sprokka" };
        internal static int ChoiceCount { get { return Names.Length; } }
        internal static bool CustomSkin(int choice) { return choice < 2 || choice == 8 || choice == 9; }
        static readonly Dictionary<string, Animator> Sources = new Dictionary<string, Animator>();
        static Func<string, Animator> _find;
        static float _nextFind;
        internal static bool NativeSelection { get { return (int)PlayerBodyModule.Character.Value >= 2; } }
        internal static Animator SelectedSource() { return NativeSelection ? Source((int)PlayerBodyModule.Character.Value) : null; }
        internal static Animator Source(int choice)
        {
            if (choice < 0 || choice >= Names.Length) return null;
            Animator source; string name = Names[choice];
            if (Sources.TryGetValue(name, out source) && source != null) return source;
            if (Multiplayer.Present)
            {
                if (_find == null) _find = MultiplayerAccess.Call<Func<string, Animator>>(Multiplayer.Type("Game.Avatar.PlayerBody"), "FindPrefab", typeof(string));
                source = _find(name); if (source != null) Sources[name] = source;
            }
            else if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 5f;
                foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
                    if (animator != null && !animator.gameObject.scene.IsValid() && animator.transform.Find("mixamorig:Hips") != null)
                    {
                        string root = animator.transform.root.name;
                        if (Array.IndexOf(Names, root) >= 0) Sources[root] = animator;
                    }
                Sources.TryGetValue(name, out source);
            }
            return source;
        }
    }

    internal static partial class Model
    {
        static readonly Dictionary<int, Mesh> RemoteMeshes = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Texture2D> RemoteTextures = new Dictionary<int, Texture2D>();
        internal static Material RemoteSkin(SkinnedMeshRenderer skin, int choice)
        {
            string modelPath = PlayerBodyModule.ModPath(choice == 0 ? PlayerBodyModule.ModelFile.Value : "Models/Player_male.glb");
            string texturePath = PlayerBodyModule.ModPath(choice == 0 ? PlayerBodyModule.TextureFile.Value : "Models/Player_male.png");
            if (choice >= 8 && (Services.CharacterAssets == null || !Services.CharacterAssets.TryPaths(choice == 8 ? "Gungirl" : "Shoota", out modelPath, out texturePath)))
                throw new InvalidOperationException("Women of Wasteland player assets are unavailable");
            Mesh mesh;
            if (!RemoteMeshes.TryGetValue(choice, out mesh) || mesh == null)
            {
                var model = Gltf.Load(modelPath);
                var bones = skin.bones; var names = new string[bones.Length]; int root = 0;
                for (int i = 0; i < bones.Length; i++) { names[i] = bones[i] != null ? bones[i].name : null; if (bones[i] == skin.rootBone) root = i; }
                int[] indices; float[] weights; List<string> missing; Gltf.Weights(model, names, root, out indices, out weights, out missing);
                var vertices = new Vector3[model.VertexCount]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length]; var bound = new BoneWeight[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = new Vector3(model.Pos[i*3], model.Pos[i*3+1], model.Pos[i*3+2]);
                    normals[i] = new Vector3(model.Nrm[i*3], model.Nrm[i*3+1], model.Nrm[i*3+2]); uv[i] = new Vector2(model.Uv[i*2], model.Uv[i*2+1]);
                    bound[i] = new BoneWeight { boneIndex0=indices[i*4], boneIndex1=indices[i*4+1], boneIndex2=indices[i*4+2], boneIndex3=indices[i*4+3], weight0=weights[i*4], weight1=weights[i*4+1], weight2=weights[i*4+2], weight3=weights[i*4+3] };
                }
                mesh = Make("WW skin " + choice, vertices, normals, uv, bound, skin.sharedMesh.bindposes, model.Tris, model.HasNormals); RemoteMeshes[choice] = mesh;
            }
            Texture2D texture;
            if (!RemoteTextures.TryGetValue(choice, out texture) || texture == null)
            { texture = Load(texturePath, "WW skin"); RemoteTextures[choice] = texture; }
            var material = new Material(skin.sharedMaterial) { name = "WW remote skin" };
            if (texture != null) material.mainTexture = texture;
            skin.sharedMesh = mesh; skin.sharedMaterial = material; return material;
        }
        static bool BuildNative(SkinnedMeshRenderer skin)
        {
            var original = skin.sharedMesh; if (original == null) return false;
            var bones = skin.bones; var weights = original.boneWeights; var dominant = new string[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i]; int bone = w.boneIndex0; float weight = w.weight0;
                if (w.weight1 > weight) { bone = w.boneIndex1; weight = w.weight1; }
                if (w.weight2 > weight) { bone = w.boneIndex2; weight = w.weight2; }
                if (w.weight3 > weight) bone = w.boneIndex3;
                dominant[i] = bone >= 0 && bone < bones.Length && bones[bone] != null ? bones[bone].name : "";
            }
            Vector3[] vertices, normals; Vector2[] uv; int[] tris;
            NativeGeometry(skin, out vertices, out normals, out uv, out tris);
            var binds = original.bindposes;
            if (vertices.Length == 0 || weights.Length != vertices.Length || tris.Length == 0)
            { PlayerBodyModule.Error("Native body " + original.name + " has no valid readable geometry/weights"); return false; }
            Full = Make("WW MP body", vertices, normals, uv, weights, binds, tris, normals.Length == vertices.Length);
            NoHead = Make("WW MP no head", vertices, normals, uv, weights, binds, Filter(tris, dominant, HeadBones, null), normals.Length == vertices.Length);
            NoArms = Make("WW MP no arms", vertices, normals, uv, weights, binds, Filter(tris, dominant, HeadNeckBones, ArmBones), normals.Length == vertices.Length);
            ArmsOnly = Make("WW MP arms", vertices, normals, uv, weights, binds, Only(tris, dominant, ArmBones, HeadNeckBones), normals.Length == vertices.Length);
            NoHeadRightArm = Make("WW MP no right arm", vertices, normals, uv, weights, binds, Filter(tris, dominant, HeadNeckBones, RightArmBones), normals.Length == vertices.Length);
            LeftArmOnly = Make("WW MP left arm", vertices, normals, uv, weights, binds, Only(tris, dominant, LeftArmBones, HeadNeckBones), normals.Length == vertices.Length);
            BodyTex = skin.sharedMaterial != null ? skin.sharedMaterial.mainTexture as Texture2D : null; _texTried = true;
            PlayerBodyModule.Info("Body " + PlayerBodyModule.Character.Value + ": " + vertices.Length + " vertices; first-person meshes ready" + (original.isReadable ? "" : " (native mesh baked)"));
            return true;
        }
        static void NativeGeometry(SkinnedMeshRenderer skin, out Vector3[] vertices, out Vector3[] normals, out Vector2[] uv, out int[] tris)
        {
            var original = skin.sharedMesh; Mesh copy = null, baked = null;
            try
            {
                var geometry = original;
                if (!original.isReadable)
                {
                    copy = UnityEngine.Object.Instantiate(original); baked = new Mesh();
                    var bones = skin.bones; var bind = original.bindposes;
                    // Cancel the current pose on this private copy: BakeMesh must return bind-space geometry, not a posed body.
                    for (int i = 0; i < bind.Length; i++) if (i < bones.Length && bones[i] != null) bind[i] = bones[i].worldToLocalMatrix * skin.transform.localToWorldMatrix;
                    copy.bindposes = bind; skin.sharedMesh = copy;
                    skin.BakeMesh(baked, false); geometry = baked;
                }
                vertices = geometry.vertices; normals = geometry.normals; uv = geometry.uv; tris = geometry.triangles;
            }
            finally
            {
                skin.sharedMesh = original;
                if (copy != null) UnityEngine.Object.Destroy(copy);
                if (baked != null) UnityEngine.Object.Destroy(baked);
            }
        }
    }
}
