using System;
using System.Collections.Generic;
using System.IO;
using ES3Internal;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildWasteland.Modules.Survival
{
    public enum Contents { Empty, Water, Meat, Vegetable, Mushroom, Mixed }

    internal static class Factory
    {
        internal const long PrefabId = 7392481500621300201L;
        internal const float Capacity = 20f;
        internal const float Bottom = .55f, Top = 1.40f, Rim = 1.48f, Radius = .84f;
        internal static GameObject Cached;
        internal static readonly Material[] Liquids = new Material[6];
        internal static readonly Material[] RawLiquids = new Material[6], StaleLiquids = new Material[6], SpoiledLiquids = new Material[6];
        static GameObject holder;
        static Mesh potMesh, liquidMesh;
        internal static Mesh ColliderMesh;
        static Material potMaterial;

        internal static PlayMakerFSM Fsm(GameObject go, string name)
        {
            return WildWasteland.Core.FsmUtil.Find(go, name);
        }

        internal static GameObject Template()
        {
            if (Cached != null) return Cached;
            GameObject source = null;
            var manager = ES3ReferenceMgrBase.Current;
            if (manager != null) foreach (var prefab in manager.prefabs)
                if (prefab != null && prefab.name == "Water_Can_Plastic") { source = prefab.gameObject; break; }
            if (source == null) throw new InvalidOperationException("Water_Can_Plastic prefab is not loaded yet.");
            LoadModels();
            // Each imported primitive needs a material slot, even when all use the same texture.
            var potMaterials = new Material[potMesh.subMeshCount];
            for (int i = 0; i < potMaterials.Length; i++) potMaterials[i] = potMaterial;
            if (holder == null)
            {
                holder = new GameObject("StewPot.Templates") { hideFlags = HideFlags.HideAndDontSave };
                holder.SetActive(false); Object.DontDestroyOnLoad(holder);
            }
            var copy = Object.Instantiate(source, holder.transform, false);
            try
            {
                copy.SetActive(false);
                copy.name = "StewPot";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one * SurvivalModule.Scale.Value;
                // Retain native save/interaction FSMs; the can's rendering and animation are replaced entirely.
                for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
                foreach (var collider in copy.GetComponents<Collider>()) Object.DestroyImmediate(collider);
                copy.GetComponent<MeshFilter>().sharedMesh = potMesh;
                copy.GetComponent<MeshRenderer>().enabled = false;
                copy.GetComponent<MeshRenderer>().sharedMaterials = potMaterials;
                var physics = copy.AddComponent<MeshCollider>(); physics.convex = true; physics.sharedMesh = ColliderMesh;
                copy.GetComponent<Rigidbody>().mass = 3f;
                var audio = copy.GetComponent<AudioSource>(); audio.playOnAwake = false; audio.loop = false;
                var visual = new GameObject("StewPot.Visual"); visual.layer = copy.layer; visual.transform.SetParent(copy.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = potMesh;
                visual.AddComponent<MeshRenderer>().sharedMaterials = potMaterials;
                var liquid = new GameObject("Liquid"); liquid.layer = copy.layer; liquid.transform.SetParent(visual.transform, false);
                liquid.AddComponent<MeshFilter>().sharedMesh = liquidMesh;
                liquid.AddComponent<MeshRenderer>().sharedMaterial = Liquids[(int)Contents.Water];
                liquid.SetActive(false);
                Fsm(copy, "ID").FsmVariables.GetFsmString("ID").Value = "stewpot";
                var amount = Fsm(copy, "LiquidAmount");
                amount.FsmVariables.GetFsmFloat("Liquid").Value = 0;
                amount.FsmVariables.GetFsmFloat("LiquidCapacity").Value = Capacity;
                Fsm(copy, "ItemName").FsmVariables.GetFsmString("ItemName").Value = "Stew Pot";
                EmptyActions(Fsm(copy, "Drink"));
                EmptyActions(Fsm(copy, "UseCan"));
                EmptyActions(Fsm(copy, "Randomize"));
                var itemName = Fsm(copy, "ItemName");
                foreach (var state in itemName.FsmStates)
                {
                    state.Fsm = itemName.Fsm;
                    foreach (var action in state.Actions)
                    {
                        var text = action as UiTextSetText;
                        if (text != null && text.text.Value == "/ 13L") text.text.Value = "/ 20L";
                    }
                    if (state.Name == "off")
                    {
                        var actions = new List<FsmStateAction>(state.Actions);
                        actions.Add(new UiTextSetText
                        {
                            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = FsmVariables.GlobalVariables.GetFsmGameObject("UI_ItemName_4") },
                            text = "", resetOnExit = false, everyFrame = false
                        });
                        state.Actions = actions.ToArray();
                    }
                    state.SaveActions();
                }
                // Native SaveAll/LoadAll keeps these fields; old water-only saves leave new fields at zero.
                var save = Fsm(copy, "saveItemVar");
                var ints = new List<FsmInt>(save.FsmVariables.IntVariables);
                ints.Add(new FsmInt("StewContents") { Value = (int)Contents.Water });
                ints.Add(new FsmInt("StewSpoiled"));
                save.FsmVariables.IntVariables = ints.ToArray();
                var floats = new List<FsmFloat>(save.FsmVariables.FloatVariables);
                foreach (string name in new[] { "RawNutrition", "CookedNutrition", "StewInsanity", "SpoiledNutrition", "StaleSeconds" }) floats.Add(new FsmFloat(name));
                save.FsmVariables.FloatVariables = floats.ToArray(); save.FsmVariables.Init();
                var es3 = copy.GetComponent<ES3Prefab>();
                es3.prefabId = PrefabId;
                es3.localRefs = new ES3RefIdDictionary();
                RegisterRefs(es3, copy.transform, "StewPot");
                es3.localRefs[potMesh] = Id("asset:potMesh");
                es3.localRefs[liquidMesh] = Id("asset:liquidMesh");
                es3.localRefs[ColliderMesh] = Id("asset:colliderMesh");
                RegisterMaterial(es3, potMaterial, "pot");
                for (int i = 1; i < Liquids.Length; i++) RegisterMaterial(es3, Liquids[i], "liquid" + i);
                for (int i = 2; i < Liquids.Length; i++)
                {
                    RegisterMaterial(es3, RawLiquids[i], "raw" + i);
                    RegisterMaterial(es3, StaleLiquids[i], "stale" + i);
                    RegisterMaterial(es3, SpoiledLiquids[i], "spoiled" + i);
                }
                copy.SetActive(true); // The inactive holder keeps the template hidden; native clones must start active.
                Cached = copy;
                WildWasteland.Core.Log.Always("[Survival] StewPot template ready: empty, 20 L, mesh collider; Camp & Farming in Apocaspawner.");
                return copy;
            }
            catch { Object.Destroy(copy); throw; }
        }

        static void EmptyActions(PlayMakerFSM fsm)
        {
            foreach (var state in fsm.FsmStates)
            {
                state.Fsm = fsm.Fsm;
                state.Actions = new FsmStateAction[0]; state.SaveActions();
            }
        }

        static void LoadModels()
        {
            if (potMesh != null) return;
            potMesh = Glb.Load(Path.Combine(SurvivalModule.Assets, "stewpot.glb")).Mesh;
            liquidMesh = Glb.Load(Path.Combine(SurvivalModule.Assets, "liquid.glb")).Mesh;
            ColliderMesh = Glb.Load(Path.Combine(SurvivalModule.Assets, "stewpot_collider.glb")).Mesh;
            // The supplied liquid is centred at y=.523; its upper face becomes the fill-level anchor.
            var verts = liquidMesh.vertices;
            float top = liquidMesh.bounds.max.y;
            float radius = Mathf.Max(liquidMesh.bounds.extents.x, liquidMesh.bounds.extents.z);
            for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(verts[i].x * Radius / radius, verts[i].y - top, verts[i].z * Radius / radius);
            liquidMesh.vertices = verts; liquidMesh.RecalculateBounds();
            Object.DontDestroyOnLoad(potMesh); Object.DontDestroyOnLoad(liquidMesh); Object.DontDestroyOnLoad(ColliderMesh);
            potMaterial = Material("Metalpot.png", .25f);
            var names = new[] { "", "water.png", "meat.png", "vegetable.png", "mushroom.png", "mixed.png" };
            for (int i = 1; i < names.Length; i++) Liquids[i] = Material(names[i], .55f);
            for (int i = 2; i < names.Length; i++)
            {
                string stem = Path.GetFileNameWithoutExtension(names[i]);
                RawLiquids[i] = OptionalMaterial(stem + "_raw.png", Liquids[i]);
                StaleLiquids[i] = OptionalMaterial(stem + "_stale.png", Liquids[i]);
                SpoiledLiquids[i] = OptionalMaterial(stem + "_spoiled.png", StaleLiquids[i]);
            }
            WildWasteland.Core.Log.Always("[Survival] StewPot GLBs loaded from " + SurvivalModule.Assets + "; stewpot_collider.glb: single convex mesh.");
        }

        static Material OptionalMaterial(string file, Material fallback)
        {
            return File.Exists(Path.Combine(SurvivalModule.Assets, file)) ? Material(file, .55f) : fallback;
        }

        static Material Material(string file, float smoothness)
        {
            var texture = new Texture2D(2, 2) { name = "StewPot." + file };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(Path.Combine(SurvivalModule.Assets, file)))) throw new InvalidDataException(file);
            Object.DontDestroyOnLoad(texture);
            var material = new Material(Shader.Find("Standard")) { name = "StewPot." + file, mainTexture = texture };
            material.SetFloat("_Glossiness", smoothness); material.SetInt("_Cull", 0);
            Object.DontDestroyOnLoad(material);
            return material;
        }

        static void RegisterRefs(ES3Prefab es3, Transform node, string path)
        {
            es3.localRefs[node.gameObject] = Id(path + ":go");
            var components = node.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++) es3.localRefs[components[i]] = Id(path + ":" + components[i].GetType().Name + ":" + i);
            foreach (Transform child in node) RegisterRefs(es3, child, path + "/" + child.name);
        }

        static void RegisterMaterial(ES3Prefab es3, Material material, string name)
        {
            if (material == null) return;
            es3.localRefs[material] = Id("asset:material:" + name);
            if (material.mainTexture != null) es3.localRefs[material.mainTexture] = Id("asset:texture:" + name);
        }

        static long Id(string text)
        {
            ulong hash = 14695981039346656037UL;
            unchecked { for (int i = 0; i < text.Length; i++) { hash ^= text[i]; hash *= 1099511628211UL; } }
            return (long)(hash & 0x7fffffffffffffffUL);
        }
    }
}
