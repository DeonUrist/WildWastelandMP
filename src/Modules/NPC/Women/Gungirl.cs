using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;

namespace WildWasteland.Modules.NPC
{
    // Gungirl = a Flexa raider wearing the female body (Models/flexa_female.gltf + .png).
    //
    // Spawning: every vanilla Flexa spawn goes through PlayMaker's CreateObject (camp/POI spawners); a Harmony postfix
    // turns [Gungirl] Chance % of them into Gungirls. Flexa's own Health FSM creates her corpse "Flexa_Dead" with
    // CreateObject on death -> if the dying Flexa is a Gungirl, so is the corpse.
    //
    // Save/load without any save file of our own: the game (Easy Save 3) restores every enemy and corpse by prefab +
    // name + Transform, and nothing in the game reads the name (checked: the only "Flexa" string in any FSM is the corpse's
    // mouse-over label). So a Gungirl is simply NAMED Gungirl: the hook renames the fresh instance "Flexa(Clone)" ->
    // "Gungirl(Clone)" before the game's spawner appends its itemNameID number ("Gungirl(Clone)281"), the corpse likewise
    // ("Gungirl_Dead(Clone)N"), and after a load the scan finds Gungirl* / Gungirl_Dead* roots and swaps their body again.
    // Versions before 0.6.0 marked her with a root scale x = y * (1 + 1/4096) instead; such Flexas from older saves are
    // renamed on sight (the scale is left alone - harmless).
    //
    // 1.7.0: the same for Sprokka -> Shoota (Models/Sprokka_female.glb + sprokka_female.png, her armour vest hidden), both at
    // [General] FemalePopulation %. Each female type is a Variant: base prefab name, own name, model, texture, parts to hide.
    public static class Gungirl
    {
        public const string Name = "Gungirl", CorpseName = "Gungirl_Dead";

        private sealed class Variant
        {
            public string Base, Female;                 // prefab name ("Flexa") and her name ("Gungirl")
            public string BaseCorpse, FemaleCorpse;     // "Flexa_Dead", "Gungirl_Dead"
            public BepInEx.Configuration.ConfigEntry<string> ModelCfg, TexCfg, HideCfg;
            public SkinModel Model; public bool ModelTried;
            public Texture2D Tex; public bool TexTried;
            public readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();        // per bone layout
            public readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>(); // by material name

            public bool IsBase(string n) { return n == Base || n.StartsWith(Base + "(", StringComparison.Ordinal); }
            public bool IsBaseCorpse(string n) { return n.StartsWith(BaseCorpse, StringComparison.Ordinal); }
            public bool IsFemale(string n) { return n == Female || n.StartsWith(Female + "(", StringComparison.Ordinal); }
            public bool IsFemaleCorpse(string n) { return n.StartsWith(FemaleCorpse, StringComparison.Ordinal); }
        }
        private static Variant[] _variants;
        private static Variant[] Variants
        {
            get
            {
                if (_variants == null)
                    _variants = new[]
                    {
                        new Variant { Base = "Flexa", Female = Name, BaseCorpse = "Flexa_Dead", FemaleCorpse = CorpseName,
                                      ModelCfg = NpcModule.GungirlModel, TexCfg = NpcModule.GungirlTexture, HideCfg = NpcModule.GungirlHideParts },
                        new Variant { Base = "Sprokka", Female = "Shoota", BaseCorpse = "Sprokka_Dead", FemaleCorpse = "Shoota_Dead",
                                      ModelCfg = NpcModule.ShootaModel, TexCfg = NpcModule.ShootaTexture, HideCfg = NpcModule.ShootaHideParts },
                    };
                return _variants;
            }
        }
        // the female variant a root name belongs to (her or her corpse), or null
        private static Variant FemaleOf(string n) { foreach (var v in Variants) if (v.IsFemale(n) || v.IsFemaleCorpse(n)) return v; return null; }

        private static readonly HashSet<Mesh> _ours = new HashSet<Mesh>();
        private static readonly HashSet<int> _dressed = new HashSet<int>();     // root ids already dressed (a skinned prop never re-enters Apply)
        private static GameObject _prefab;
        private sealed class BodyState
        {
            internal GameObject Root;
            internal readonly List<SkinState> Skins = new List<SkinState>();
            internal readonly Dictionary<Renderer, bool> Parts = new Dictionary<Renderer, bool>();
            internal readonly List<KeyValuePair<UiTextSetText, FsmString>> Labels = new List<KeyValuePair<UiTextSetText, FsmString>>();
        }
        private struct SkinState { internal SkinnedMeshRenderer Renderer; internal Mesh Mesh; internal Material Material; }
        private struct PendingBody { internal GameObject Root; internal Variant Variant; internal int Id; }
        private static readonly Dictionary<int, BodyState> _original = new Dictionary<int, BodyState>();
        private static readonly Queue<PendingBody> _pending = new Queue<PendingBody>();
        private static readonly HashSet<int> _queued = new HashSet<int>();
        private static readonly List<int> _dead = new List<int>();
        private static readonly Stack<Transform> _walk = new Stack<Transform>();
        private static readonly List<GameObject> _roots = new List<GameObject>();
        private static readonly Dictionary<int, string> _prefabNames = new Dictionary<int, string>();
        // (WW perf, D9) discovery is event driven: a full walk when the world is ready (load / new game / scene), then the spawn events
        // (CreateObject hook, Senses registering an NPC, Patrols' DressCrew) and a slow safety pass every 60 s. Each object's name is read
        // once per scene (_seen); a Rigidbody root (a creature, car, item) is descended once, later passes stop at it (_bodies).
        private static readonly HashSet<int> _seen = new HashSet<int>(), _bodies = new HashSet<int>();
        private static bool _wantFull; private static float _lastPass = -1000f;
        private const float SafetyEvery = 60f;

        private static BodyState State(GameObject root)
        {
            BodyState state; int id = root.GetInstanceID();
            if (!_original.TryGetValue(id, out state)) _original[id] = state = new BodyState { Root = root };
            return state;
        }
        private static void Queue(GameObject root, Variant variant)
        {
            if (root == null || variant == null || _dressed.Contains(root.GetInstanceID()) || !_queued.Add(root.GetInstanceID())) return;
            _pending.Enqueue(new PendingBody { Root = root, Variant = variant, Id = root.GetInstanceID() });
        }

        internal static void Initialize(bool discover = true)
        {
            // Disk decoding and native skeleton warming happen at module startup, before gameplay jobs.
            foreach (var v in Variants) { Model(v); Texture(v); }
            foreach (var smr in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
            {
                if (smr == null || smr.gameObject.scene.IsValid() || smr.sharedMesh == null || smr.bones.Length < 10) continue;
                for (var t = smr.transform; t != null; t = t.parent)
                    foreach (var v in Variants)
                        if (v.IsBase(t.name) || v.IsBaseCorpse(t.name)) MeshFor(v, smr);
            }
            if (discover) BeginScan();
        }

        private static string PrefabOf(GameObject go)
        {
            string name; int id = go.GetInstanceID();
            if (_prefabNames.TryGetValue(id, out name)) return name;
            name = go.name; int cut = name.IndexOf('('); if (cut > 0) name = name.Substring(0, cut);
            if (_prefabNames.Count > 4096) _prefabNames.Clear();
            _prefabNames[id] = name; return name;
        }
        // ---------- identity = the name ----------
        private static bool IsFlexa(string name) { return name == "Flexa" || name.StartsWith("Flexa(", StringComparison.Ordinal); }
        private static bool IsFlexaCorpse(string name) { return name.StartsWith("Flexa_Dead", StringComparison.Ordinal); }
        private static bool IsGungirlName(string name) { return name == Name || name.StartsWith(Name + "(", StringComparison.Ordinal); }
        private static bool IsGungirlCorpse(string name) { return name.StartsWith(CorpseName, StringComparison.Ordinal); }

        // "Flexa(Clone)281" -> "Gungirl(Clone)281", "Flexa_Dead(Clone)" -> "Gungirl_Dead(Clone)"
        private static Variant Rename(GameObject go)
        {
            string n = go.name;
            foreach (var v in Variants)
            {
                if (v.IsBaseCorpse(n)) { go.name = v.FemaleCorpse + n.Substring(v.BaseCorpse.Length); return v; }
                if (v.IsBase(n)) { go.name = v.Female + n.Substring(v.Base.Length); return v; }
            }
            return null;
        }

        // the pre-0.6.0 marker (root scale x = y * (1 + 1/4096)) - read only, to convert Gungirls from older saves
        private static bool IsOldMarked(Transform t)
        {
            if (t == null) return false;
            var s = t.localScale;
            if (Mathf.Abs(s.y) < 1e-6f) return false;
            float r = s.x / s.y;
            return r > 1.0001f && r < 1.0005f;
        }

        // ---------- public API for other mods (reflection-friendly) ----------
        // Turns a live Flexa (or a Flexa_Dead corpse) into a Gungirl. Returns false if the model could not be applied.
        public static bool MakeGungirl(GameObject root)
        {
            if (!WomenFeature.Active || root == null) return false;
            var v = Rename(root) ?? FemaleOf(root.name);
            Queue(root, v); return v != null;
        }

        // a female raider of any kind (Gungirl, Shoota) or her corpse
        public static bool IsGungirl(GameObject root) { return root != null && FemaleOf(root.name) != null; }
        public static bool IsFemale(GameObject root) { return IsGungirl(root); }

        // ---------- hooks ----------
        // Harmony postfix on HutongGames.PlayMaker.Actions.CreateObject.OnEnter
        public static void AfterCreateObject(CreateObject __instance)
        {
            if (!WomenFeature.Active) return;
            try
            {
                var store = __instance.storeObject;
                GameObject go = store != null && !store.IsNone ? store.Value : null;
                if (go == null) return;
                var prefabVar = __instance.gameObject;
                if (prefabVar == null || prefabVar.Value == null) return;
                string pn = PrefabOf(prefabVar.Value);      // cached per prefab asset: no name string per spawn
                foreach (var v in Variants)
                {
                    if (pn == v.BaseCorpse)
                    {
                        var owner = __instance.Fsm != null ? __instance.Fsm.GameObject : null;
                        if (owner != null && v.IsFemale(owner.name))
                        {
                            Rename(go);                 // "Gungirl_Dead(Clone)"; the Health FSM appends the itemNameID number next
                            Queue(go, v);
                        }
                        return;
                    }
                    if (pn == v.Base)
                    {
                        if (!NpcModule.EnableFemaleNpc.Value) return;
                        if (UnityEngine.Random.Range(0, 100) >= NpcModule.FemalePopulation.Value) return;
                        Rename(go);                     // "Gungirl(Clone)"; the spawner appends the itemNameID number next
                        Queue(go, v);
                        return;
                    }
                }
            }
            catch (Exception e) { NpcModule.Log.LogError("CreateObject hook: " + e); }
        }

        internal static void DressCrew(GameObject root, string identity, bool randomize)
        {
            if (root == null) return;
            Variant variant = FemaleOf(root.name) ?? FemaleOf(identity ?? "");
            if (variant == null && randomize && NpcModule.EnableFemaleNpc.Value && NpcModule.FemaleCrew.Value
                && UnityEngine.Random.Range(0, 100) < NpcModule.FemalePopulation.Value) variant = Rename(root);
            if (variant != null) Queue(root, variant);
        }

        public static void OnSceneLoaded()
        {
            _walk.Clear(); _roots.Clear(); _pending.Clear(); _queued.Clear(); _prefabNames.Clear(); _seen.Clear(); _bodies.Clear();
            Sweep(); Voice.OnSceneLoaded(); BeginScan();
        }

        // a full walk (world ready / scene loaded / module start): every object looked at again; no player yet = as soon as there is one
        internal static void BeginScan() { _wantFull = true; StartPass(); }

        // "Female discovery" (5 s): a pending full walk, else the safety pass once a minute
        internal static void SafetyScan()
        {
            if (_walk.Count > 0 && !_wantFull) return;
            if (_wantFull || Time.unscaledTime >= _lastPass + SafetyEvery) StartPass();
        }

        private static void StartPass()
        {
            if (!WomenFeature.Active || GameRefs.Player == null) return;
            if (_wantFull) { _seen.Clear(); _bodies.Clear(); _walk.Clear(); }
            else if (_walk.Count > 0) return;
            _wantFull = false; _lastPass = Time.unscaledTime;
            if (_seen.Count > 300000) { _seen.Clear(); _bodies.Clear(); }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i); if (!scene.isLoaded) continue;
                _roots.Clear();
                if (_roots.Capacity < scene.rootCount) _roots.Capacity = scene.rootCount;
                scene.GetRootGameObjects(_roots);
                foreach (var go in _roots) if (go != null) _walk.Push(go.transform);
            }
            _roots.Clear();
        }

        internal static bool Step()
        {
            if (!WomenFeature.Active) return false;
            if (_pending.Count > 0)
            {
                var item = _pending.Dequeue();
                _queued.Remove(item.Id);
                if (item.Root != null)
                {
                    if (!_dressed.Contains(item.Root.GetInstanceID())) Apply(item.Root, item.Variant, "queued");
                }
                return _pending.Count > 0 || _walk.Count > 0;
            }
            if (_walk.Count == 0) return false;
            var t = _walk.Pop();
            if (t != null)
            {
                int id = t.GetInstanceID();
                bool first = _seen.Add(id);
                if (first) Registered(t.gameObject);
                if (first ? t.GetComponent<Rigidbody>() == null || _bodies.Add(id) : !_bodies.Contains(id))
                    for (int i = 0; i < t.childCount; i++) _walk.Push(t.GetChild(i));
            }
            return _pending.Count > 0 || _walk.Count > 0;
        }

        internal static void Sweep()
        {
            _dead.Clear();
            foreach (var pair in _original) if (pair.Value.Root == null) _dead.Add(pair.Key);
            foreach (int id in _dead) { _original.Remove(id); _dressed.Remove(id); _queued.Remove(id); }
            _dead.Clear();
        }

        internal static void Shutdown()
        {
            foreach (var state in _original.Values)
            {
                if (state.Root == null) continue;
                foreach (var skin in state.Skins)
                    if (skin.Renderer != null && _ours.Contains(skin.Renderer.sharedMesh))
                    { skin.Renderer.sharedMesh = skin.Mesh; skin.Renderer.sharedMaterial = skin.Material; }
                foreach (var part in state.Parts) if (part.Key != null) part.Key.enabled = part.Value;
                foreach (var label in state.Labels) label.Key.text = label.Value;
            }
            if (_variants != null) foreach (var variant in _variants)
            {
                foreach (var mesh in variant.Meshes.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
                foreach (var material in variant.Mats.Values) if (material != null) UnityEngine.Object.Destroy(material);
                if (variant.Tex != null) UnityEngine.Object.Destroy(variant.Tex);
            }
            _variants = null; _prefab = null; _ours.Clear(); _dressed.Clear(); _original.Clear();
            _pending.Clear(); _queued.Clear(); _walk.Clear(); _roots.Clear(); _prefabNames.Clear(); _seen.Clear(); _bodies.Clear(); _wantFull = false; _lastPass = -1000f;
        }

        // ---------- the body swap ----------
        private static bool Apply(GameObject root, Variant v, string why)
        {
            if (v == null) return false;
            int swapped = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || _ours.Contains(smr.sharedMesh)) continue;
                if (smr.bones == null || smr.bones.Length < 10) continue;     // only the body, not props (Sprokka's vest has 6 bones)
                var mesh = MeshFor(v, smr);
                if (mesh == null) return false;
                var mat = MaterialFor(v, smr.sharedMaterial);
                State(root).Skins.Add(new SkinState { Renderer = smr, Mesh = smr.sharedMesh, Material = smr.sharedMaterial });
                smr.sharedMesh = mesh;
                if (mat != null) smr.sharedMaterial = mat;
                swapped++;
            }
            int hidden = HideParts(root, v);
            if (swapped > 0) { Voice.Apply(root); Label(root); _dressed.Add(root.GetInstanceID()); }
            if (swapped > 0) NpcModule.Verbose(v.Female + ": " + root.name + " (" + why + ") body swapped, " + hidden + " part(s) hidden");
            return swapped > 0;
        }

        // The corpse's mouse-over label (ItemName FSM, state "over": UiTextSetText "Flexa") -> "Gungirl". The FSM's actions exist once it
        // is initialised; Voice's pending list calls this again after the body swap when the FSMs weren't ready yet.
        internal static int Label(GameObject root)
        {
            int n = 0;
            var v = root != null ? FemaleOf(root.name) : null;
            if (v == null) return 0;
            foreach (var f in root.GetComponents<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != "ItemName" || f.Fsm == null || !f.Fsm.Initialized) continue;
                foreach (var st in f.Fsm.States)
                {
                    if (st.Actions == null) continue;
                    foreach (var a in st.Actions)
                    {
                        var label = a as UiTextSetText;
                        if (label != null && label.text != null && label.text.Value == v.Base)
                        {
                            State(root).Labels.Add(new KeyValuePair<UiTextSetText, FsmString>(label, label.text));
                            label.text = new FsmString { Value = v.Female }; n++;
                        }
                    }
                }
            }
            return n;
        }

        private static int HideParts(GameObject root, Variant v)
        {
            string list = v.HideCfg.Value ?? "";
            if (list.Trim().Length == 0) return 0;
            var names = new List<string>();
            foreach (var s in list.Split(',')) { var n = s.Trim(); if (n.Length > 0) names.Add(n); }
            int count = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var sk = r as SkinnedMeshRenderer;
                if (!(r is MeshRenderer) && sk == null) continue;
                if (sk != null && (sk.sharedMesh == null || _ours.Contains(sk.sharedMesh) || (sk.bones != null && sk.bones.Length >= 10))) continue;   // never the body; skinned props (Sprokka's vest) yes
                foreach (var n in names)
                    if (r.gameObject.name.StartsWith(n, StringComparison.OrdinalIgnoreCase))
                    {
                        if (r.enabled) { State(root).Parts[r] = true; r.enabled = false; count++; }
                        break;
                    }
            }
            return count;
        }

        private static SkinModel Model(Variant v)
        {
            if (v.ModelTried) return v.Model;
            v.ModelTried = true;
            string path = ModPath(v.ModelCfg.Value);
            try
            {
                v.Model = Gltf.Load(path);
                NpcModule.Log.LogInfo(v.Female + " model: " + Path.GetFileName(path) + " - " + v.Model.Info);
            }
            catch (Exception e) { NpcModule.Log.LogError(v.Female + " model " + path + " could not be loaded: " + e.Message); v.Model = null; }
            return v.Model;
        }

        private static Mesh MeshFor(Variant vr, SkinnedMeshRenderer smr)
        {
            var bones = smr.bones;
            var names = new string[bones.Length];
            for (int i = 0; i < bones.Length; i++) names[i] = bones[i] != null ? bones[i].name : null;
            string key = string.Join("|", names);
            Mesh cached;
            if (vr.Meshes.TryGetValue(key, out cached)) return cached;
            vr.Meshes[key] = null;   // one attempt per layout
            var model = Model(vr);
            if (model == null) return null;

            // bind poses: the game's own (if the mesh hands them out), else the table read from the asset
            Matrix4x4[] bp = null;
            string bpSource = "game mesh";
            try
            {
                var orig = smr.sharedMesh.bindposes;
                if (orig != null && orig.Length == bones.Length) bp = orig;
            }
            catch (Exception) { }
            if (bp == null)
            {
                bpSource = "built-in table";
                bp = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    float[] v;
                    if (names[i] == null || !Bindposes.Human.TryGetValue(names[i], out v))
                    {
                        NpcModule.Log.LogError("Gungirl: no bind pose for bone " + names[i] + " of " + smr.name);
                        return null;
                    }
                    var m = Matrix4x4.identity;
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 4; c++) m[r, c] = v[r * 4 + c];
                    bp[i] = m;
                }
            }

            int rootBone = 0;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i] == smr.rootBone) rootBone = i;

            int[] idx4; float[] w4; List<string> unmapped;
            Gltf.Weights(model, names, rootBone, out idx4, out w4, out unmapped);

            int n = model.VertexCount;
            var verts = new Vector3[n]; var nrms = new Vector3[n]; var uvs = new Vector2[n]; var bws = new BoneWeight[n];
            for (int v = 0; v < n; v++)
            {
                verts[v] = new Vector3(model.Pos[v * 3], model.Pos[v * 3 + 1], model.Pos[v * 3 + 2]);
                nrms[v] = new Vector3(model.Nrm[v * 3], model.Nrm[v * 3 + 1], model.Nrm[v * 3 + 2]);
                uvs[v] = new Vector2(model.Uv[v * 2], model.Uv[v * 2 + 1]);
                bws[v] = new BoneWeight
                {
                    boneIndex0 = idx4[v * 4], weight0 = w4[v * 4],
                    boneIndex1 = idx4[v * 4 + 1], weight1 = w4[v * 4 + 1],
                    boneIndex2 = idx4[v * 4 + 2], weight2 = w4[v * 4 + 2],
                    boneIndex3 = idx4[v * 4 + 3], weight3 = w4[v * 4 + 3],
                };
            }
            var mesh = new Mesh { name = "Gungirl" };
            if (n > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = model.Tris;
            if (model.HasNormals) mesh.normals = nrms; else mesh.RecalculateNormals();
            mesh.boneWeights = bws;
            mesh.bindposes = bp;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // how far the model's skeleton is from the game's (should be ~0: same armature, unchanged bones)
            float worst = 0f; string worstName = "";
            var boneOf = new Dictionary<string, int>();
            for (int i = 0; i < names.Length; i++) if (names[i] != null && !boneOf.ContainsKey(names[i])) boneOf[names[i]] = i;
            for (int j = 0; j < model.Joints.Length; j++)
            {
                int b;
                if (!boneOf.TryGetValue(model.Joints[j], out b)) continue;
                Vector3 game = bp[b].inverse.GetColumn(3);
                var mine = new Vector3(model.JointPos[j * 3], model.JointPos[j * 3 + 1], model.JointPos[j * 3 + 2]);
                float d = (game - mine).magnitude;
                if (d > worst) { worst = d; worstName = model.Joints[j]; }
            }
            string msg = vr.Female + " mesh for " + smr.name + ": " + n + " vertices, bind poses from " + bpSource
                         + ", skeleton offset max " + (worst * 100f).ToString("0.0") + " cm (" + worstName + ")"
                         + (unmapped.Count > 0 ? ", joints without a game bone (use their parent): " + string.Join(", ", unmapped.ToArray()) : "");
            if (worst > 0.05f) NpcModule.Log.LogWarning(msg + " - the armature was moved in Blender; she will be deformed");
            else NpcModule.Log.LogInfo(msg);

            vr.Meshes[key] = mesh;
            _ours.Add(mesh);
            return mesh;
        }

        private static Material MaterialFor(Variant v, Material orig)
        {
            if (orig == null) return null;
            Material m;
            string key = orig.name;
            int cut = key.IndexOf(" (Instance)", StringComparison.Ordinal);
            if (cut > 0) key = key.Substring(0, cut);
            if (v.Mats.TryGetValue(key, out m) && m != null) return m;
            m = new Material(orig) { name = key + " (" + v.Female + ")" };
            var tex = Texture(v);
            if (tex != null) m.mainTexture = tex;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            v.Mats[key] = m;
            return m;
        }

        private static Texture2D Texture(Variant v)
        {
            if (v.TexTried) return v.Tex;
            v.TexTried = true;
            string path = ModPath(v.TexCfg.Value);
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Gungirl" };
                if (!ImageConversion.LoadImage(tex, bytes, true)) throw new InvalidDataException("not a PNG/JPG");
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                v.Tex = tex;
                NpcModule.Log.LogInfo(v.Female + " texture: " + Path.GetFileName(path) + " (" + tex.width + "x" + tex.height + ")");
            }
            catch (Exception e) { NpcModule.Log.LogError(v.Female + " texture " + path + " could not be loaded: " + e.Message); }
            return v.Tex;
        }

        internal static string ModPath(string rel)
        {
            rel = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(rel)) return rel;
            string path = Path.Combine(NpcModule.Instance.Dir, rel);
            if (File.Exists(path) || Directory.Exists(path)) return path;
            string legacy = Path.Combine(Assets.LegacyPluginDir("WomenOfWasteland"), rel);
            return File.Exists(legacy) || Directory.Exists(legacy) ? legacy : path;
        }

        // ---------- debug spawn ----------
        internal static void SpawnInFront()
        {
            var cam = GameRefs.MainCamera;
            if (cam == null) return;
            var prefab = FlexaPrefab();
            if (prefab == null) { NpcModule.Log.LogWarning("Gungirl: Flexa prefab not found"); return; }
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 at = cam.transform.position + fwd * 6f;
            RaycastHit hit;
            if (Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                at = hit.point;
            // Flexa's pivot is ~1 m above her feet (capsule centre -0.23, height 1.5): lift the pivot so the capsule's
            // bottom sits just above the ground - spawned with the pivot on the ground, she starts half inside the terrain
            // and falls through it.
            at += Vector3.up * (FootDepth(prefab) + 0.05f);
            var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(-fwd) * prefab.transform.rotation);
            Register(go, Name);
            Queue(go, Variants[0]);
            bool ok = true;
            NpcModule.Log.LogInfo("Gungirl: spawned " + go.name + " at " + at.ToString("F1") + (ok ? "" : " - but the model could not be applied, see the errors above"));
        }

        // distance from the root pivot down to the bottom of its solid capsule collider (world units)
        private static float FootDepth(GameObject prefab)
        {
            float depth = 0f;
            float sy = Mathf.Abs(prefab.transform.localScale.y);
            foreach (var cap in prefab.GetComponents<CapsuleCollider>())
            {
                if (cap.isTrigger || cap.direction != 1) continue;
                depth = Mathf.Max(depth, (cap.height * 0.5f - cap.center.y) * sy);
            }
            return depth > 0f ? depth : 1f;
        }

        // NPC detection registered an NPC: a Gungirl from a save (or a pre-0.6.0 marked Flexa) is dressed now
        internal static void Registered(GameObject owner)
        {
            try
            {
                if (!WomenFeature.Active || owner == null || _dressed.Contains(owner.GetInstanceID())) return;
                string n = owner.name;
                var fv = FemaleOf(n);
                if (fv != null) Queue(owner, fv);
                else if (IsFlexa(n) && IsOldMarked(owner.transform)) { Rename(owner); Queue(owner, Variants[0]); }
            }
            catch (Exception e) { NpcModule.Log.LogError("Gungirl: " + e); }
        }

        internal static GameObject FlexaPrefab()
        {
            if (_prefab != null) return _prefab;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == "Flexa" && !go.scene.IsValid() && go.transform.parent == null && go.GetComponent<Rigidbody>() != null)
                { _prefab = go; break; }
            return _prefab;
        }

        // the game's spawn recipe (same as Apocatremors/Apocaspawner): itemNameID number in the name + NewGO_ArrayList items list -> saved
        private static void Register(GameObject go, string prefabName)
        {
            int id = -1;
            var counterGo = GameRefs.ItemNameId;
            if (counterGo != null)
                foreach (var f in counterGo.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "itemNameID")
                    {
                        var v = f.FsmVariables.GetFsmInt("intName");
                        if (v != null) { v.Value += 1; id = v.Value; }
                        break;
                    }
            go.name = prefabName + "(Clone)" + (id >= 0 ? id.ToString() : "");
            var reg = GameRefs.NewGOArrayList;
            if (reg == null) return;
            foreach (var p in reg.GetComponents<PlayMakerArrayListProxy>())
                if ((p.referenceName ?? "").ToLowerInvariant().Contains("item")) { p.arrayList.Add(go); return; }
        }
    }
}
