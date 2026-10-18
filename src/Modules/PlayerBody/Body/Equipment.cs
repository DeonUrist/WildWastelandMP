using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildWasteland.Modules.PlayerBody
{
    internal sealed partial class Equipment
    {
        internal enum Kind { None, LongGun, Sidearm, Blade, Binocular }
        internal sealed class Mount
        {
            public GameObject Item, Visual;
            internal string Prefab = "";
            public Transform Anchor;
            public Renderer[] Renderers;
            public int Slot = -1;
            public float Depth;
            public Bounds Bounds;
            public Vector3 Barrel, Magazine;
            public bool Lance;
            public float LanceRadius;
            // (WW) long guns: the posed bounds depend only on the visual and on whether a backpack is worn - computed once per change
            // (was LocalBounds = GetComponentsInChildren<MeshFilter> every frame for mounts 1 and 2)
            public bool PosedValid, PosedBackpack;
            public Bounds Posed;
            // (WW perf) what Show last wrote to Renderers (-1 = nothing yet): written again only when it changes
            public int ShownOn = -1; public ShadowCastingMode ShownMode;
        }
        private readonly GameObject _root;
        private readonly System.Collections.Generic.Dictionary<string, Transform> _bones;
        private readonly bool _remote;
        private int _remoteHidden;
        private readonly EquipmentInventory _inventory = new EquipmentInventory();
        // Backpack; two long guns; two sidearms; two blades; binoculars; flashlight (8).
        private readonly Mount[] _mounts = new Mount[9];
        private readonly GameObject[] _chosen = new GameObject[9];
        private readonly int[] _slots = new int[9];
        // The equipped flashlight (QuickItems/Flashlight_Item): flashlight3 = Police (left belt, pointing forward), flashlight1 = Old and
        // flashlight2 = Military (left chest, pointing forward). Lens directions in the item meshes (read from the assets): flashlight3 +Y,
        // flashlight1/2 -X with +Y up. When the game's light is on (PlayerCamera/Flashlight active) a small point light glows at the lens.
        private const int FlashIndex = 8;
        private bool _flashPolice;
        private Light _glow;
        private Transform _gameFlashlight;
        private bool _visible, _shadow;
        private float _packDepth, _packWidth;

        public Equipment(Body body) : this(body.Root, body.Bones, false) { }
        internal Equipment(GameObject root, System.Collections.Generic.Dictionary<string, Transform> bones, bool remote)
        {
            _root = root; _bones = bones; _remote = remote;
            for (int i = 0; i < _mounts.Length; i++) _mounts[i] = new Mount();
        }

        internal int Snapshot(string[] names)
        {
            int hidden = 0;
            for (int i = 0; i < _mounts.Length; i++)
            {
                names[i] = _mounts[i].Item != null ? _mounts[i].Prefab : "";
                if (Drawn(i)) hidden |= 1 << i;
            }
            return hidden;
        }
        internal void ApplyRemote(GameObject[] items, int hidden)
        {
            _remoteHidden = hidden;
            for (int i = 0; i < _mounts.Length; i++) Update(i, items[i], -1);
            for (int i = 0; i < _mounts.Length; i++) Position(i);
            SetVisible(PlayerBodyModule.EquipmentOnBody.Value, false);
        }
        internal void Dispose()
        { for (int i = 0; i < _mounts.Length; i++) Update(i, null, -1); }

        internal static Kind Classify(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("binocular")) return Kind.Binocular;
            if (n.Contains("machete") || n.Contains("knife") || n.Contains("shiv")) return Kind.Blade;
            if (n.Contains("pistol") || n.Contains("revolver") || n.Contains("folk_17") || n.Contains("folk17") || n.Contains("smg") || n.Contains("borz")) return Kind.Sidearm;
            if (n.Contains("rifle") || n.Contains("shotgun") || n.Contains("akm") || n.Contains("m16") || n.Contains("redmark")
                || n.Contains("rochester") || n.Contains("slamberg") || n.Contains("slamfire") || n.Contains("blastlance")) return Kind.LongGun;
            return Kind.None;
        }

        // Reserve places before hiding the drawn object. Duplicate weapon types in
        // separate slots retain separate identities and never hide one another.
        internal static void Select(GameObject[] items, GameObject[] chosen, int[] slots)
        {
            Array.Clear(chosen, 0, chosen.Length);
            for (int i = 0; i < slots.Length; i++) slots[i] = -1;
            int longs = 0, sides = 0, blades = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                int place = -1;
                switch (Classify(items[i].name))
                {
                    case Kind.LongGun: if (longs++ < 2) place = longs; break;
                    case Kind.Sidearm: if (sides++ < 2) place = 2 + sides; break;
                    case Kind.Blade: if (blades++ < 2) place = 4 + blades; break;
                    case Kind.Binocular: if (chosen[7] == null) place = 7; break;
                }
                if (place >= 0) { chosen[place] = items[i]; slots[place] = i; }
            }
        }

        // (WW perf) the inventory is read 10 times a second (unscaled), and at once when the drawn weapon, the vanilla Weapons FSM state or the
        // PlayerCamera changes (was every frame: slot Finds + FirstItem + QuickItems Finds). Worst case: a slot change without a draw shows ~0.1 s late.
        private float _nextRead;
        private string _readDrawn, _readState;
        private Transform _seenCamera, _readCamera, _quick, _quickPack, _quickBino, _quickFlash;
        public void Tick()
        {
            string drawn = Game.DrawnWeapon, state = _inventory.WeaponState;
            var cam = Game.PlayerCamera;
            float now = Time.unscaledTime;
            if (now >= _nextRead || !ReferenceEquals(drawn, _readDrawn) || !ReferenceEquals(state, _readState) || !ReferenceEquals(cam, _seenCamera))
            {
                _nextRead = now + 0.1f; _readDrawn = drawn; _readState = state; _seenCamera = cam;
                if (_inventory.Read())
                {
                    Select(_inventory.Items, _chosen, _slots);
                    if (!ReferenceEquals(cam, _readCamera) || _quick == null) { _readCamera = cam; _quick = cam != null ? cam.Find("QuickItems") : null; _quickPack = _quickBino = _quickFlash = null; }
                    var quick = _quick;
                    if (quick != null)
                    {
                        if (_quickPack == null) _quickPack = quick.Find("Backpack_Item");
                        if (_quickBino == null) _quickBino = quick.Find("Binocular_Item");
                        if (_quickFlash == null) _quickFlash = quick.Find("Flashlight_Item");
                    }
                    _chosen[0] = EquipmentInventory.FirstItem(quick != null ? _quickPack : null);
                    var binocular = EquipmentInventory.FirstItem(quick != null ? _quickBino : null);
                    if (binocular != null) { _chosen[7] = binocular; _slots[7] = -1; }
                    _chosen[FlashIndex] = EquipmentInventory.FirstItem(quick != null ? _quickFlash : null); _slots[FlashIndex] = -1;
                    for (int i = 0; i < _mounts.Length; i++) Update(i, _chosen[i], _slots[i]);
                }
                _readState = _inventory.WeaponState;   // (Read may have re-found the Weapons FSM)
            }
            for (int i = 0; i < _mounts.Length; i++) { Position(i); Show(i); }
        }

        // (WW perf) each bone's neutral (bind pose) rotation, computed once (was a Matrix4x4 from Bindposes per mount per frame)
        private static readonly System.Collections.Generic.Dictionary<string, Quaternion> _neutral = new System.Collections.Generic.Dictionary<string, Quaternion>();
        private static Quaternion Neutral(string bone)
        {
            Quaternion q;
            if (_neutral.TryGetValue(bone, out q)) return q;
            float[] bind = Bindposes.Human[bone];
            var matrix = Matrix4x4.identity;
            for (int row = 0; row < 3; row++) for (int col = 0; col < 4; col++) matrix[row, col] = bind[row * 4 + col];
            return _neutral[bone] = matrix.rotation;
        }

        private void Update(int i, GameObject item, int slot)
        {
            var mount = _mounts[i]; mount.Slot = slot;
            if (ReferenceEquals(mount.Item, item) && (item == null || mount.Visual != null)) return;
            if (mount.Anchor != null) { mount.Anchor.gameObject.SetActive(false); UnityEngine.Object.Destroy(mount.Anchor.gameObject); }
            mount.Item = item; mount.Visual = null; mount.Anchor = null; mount.Renderers = null; mount.PosedValid = false; mount.ShownOn = -1;
            mount.Prefab = item != null ? item.name : "";
            int clone = mount.Prefab.IndexOf("(Clone)", StringComparison.Ordinal); if (clone >= 0) mount.Prefab = mount.Prefab.Substring(0, clone);
            int suffix = mount.Prefab.LastIndexOf(" (", StringComparison.Ordinal); if (suffix > 0 && mount.Prefab.EndsWith(")", StringComparison.Ordinal)) mount.Prefab = mount.Prefab.Substring(0, suffix);
            if (i == 0) _packDepth = _packWidth = 0;
            if (i == FlashIndex) { _glow = null; _remoteBeam = null; }   // destroyed with the old anchor
            if (item == null) return;
            mount.Anchor = new GameObject("Apocaplayer.Equipment." + i).transform;
            mount.Anchor.SetParent(_root.transform, false);
            mount.Visual = Props.CopyForMount(item, mount.Anchor);
            if (i == 0) mount.Visual.transform.localScale *= .8f;
            mount.Renderers = mount.Visual.GetComponentsInChildren<Renderer>(true);
            if (i == FlashIndex) { MountFlashlight(mount, item); return; }
            var bounds = LocalBounds(mount.Visual);
            mount.Bounds = bounds;
            mount.Lance = item.name.StartsWith("blastlance", StringComparison.OrdinalIgnoreCase);
            if (mount.Lance)
            {
                var shaft = LocalBounds(mount.Visual, true);
                mount.LanceRadius = Mathf.Min(shaft.size.x, shaft.size.z) * .5f;
                PlayerBodyModule.Verbose("Blast lance mount " + i + ": shaft radius=" + mount.LanceRadius);
            }
            Vector3 axis = MajorAxis(bounds.size), thin = ThinAxis(bounds.size, axis);
            Vector3 major = i >= 1 && i <= 6 ? Props.Barrel(mount.Visual) : axis;
            if (major.sqrMagnitude < .01f) major = axis;
            mount.Barrel = major;
            if (i == 1 || i == 2) mount.Magazine = MagazineDirection(mount.Visual, bounds, major, thin);
            Vector3 longDir = i == 0 ? Vector3.up : i == 7 ? Vector3.right : i <= 2 ? Vector3.up : Vector3.down;
            Vector3 face = i >= 3 && i <= 6 ? (i % 2 == 1 ? Vector3.right : Vector3.left) : Vector3.forward;
            Quaternion rotation = i == 0 ? Quaternion.Euler(0, 90, 0) : Quaternion.LookRotation(longDir, face) * Quaternion.Inverse(Quaternion.LookRotation(major, thin));
            if (i == 1 || i == 2) rotation = Quaternion.AngleAxis(i == 1 ? -35f : 35f, Vector3.forward) * rotation;
            mount.Visual.transform.localRotation = rotation;
            mount.Visual.transform.localPosition = -(rotation * bounds.center);
            mount.Depth = LocalBounds(mount.Visual).size.z;
            if (i == 0) { _packDepth = mount.Depth; _packWidth = LocalBounds(mount.Visual).size.x; }
            PlayerBodyModule.Verbose("Equipment mount " + i + ": " + item.name + " bounds=" + bounds.size);
        }

        private void MountFlashlight(Mount mount, GameObject item)
        {
            _flashPolice = item.name.ToLowerInvariant().Contains("flashlight3");
            // lens direction / up in the item mesh -> forward / up on the body
            Quaternion rotation = _flashPolice
                ? Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.up, Vector3.forward))
                : Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.left, Vector3.up));
            mount.Visual.transform.localRotation = rotation;
            mount.Visual.transform.localPosition = Vector3.zero;
            if (!_flashPolice) mount.Visual.transform.localScale *= .8f;   // the chest lights at 80 % (Denis)
            var b = LocalBounds(mount.Visual);
            mount.Visual.transform.localPosition = -b.center;
            mount.Bounds = b; mount.Depth = b.size.z;
            // the glow: just in front of the lens (the lens is the front face; on the old/military lights it is the upper part)
            var g = new GameObject("Apocaplayer.FlashlightGlow");
            g.transform.SetParent(mount.Anchor, false);
            float lensY = _flashPolice ? 0f : b.size.y * .25f;
            g.transform.localPosition = new Vector3(0f, lensY, b.size.z * .5f + .03f);
            _glow = g.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.range = .45f;
            _glow.intensity = 2.2f;
            _glow.color = new Color(1f, .93f, .78f);
            _glow.shadows = LightShadows.None;
            _glow.enabled = false;
            if (_remote) CreateRemoteBeam(mount, item, g.transform);
            PlayerBodyModule.Verbose("Equipment mount " + FlashIndex + ": " + item.name + (_flashPolice ? " (police: left belt)" : " (left chest)") + " bounds=" + b.size);
        }

        private bool FlashlightOn
        {
            get
            {
                if (_gameFlashlight == null && Game.PlayerCamera != null) _gameFlashlight = Game.PlayerCamera.Find("Flashlight");
                return _gameFlashlight != null && _gameFlashlight.gameObject.activeInHierarchy;
            }
        }

        private void PositionFlashlight(Mount mount, bool mirror)
        {
            string bone = _flashPolice ? "mixamorig:Hips" : "mixamorig:Spine2";
            Transform parent;
            if (!_bones.TryGetValue(bone, out parent)) return;
            if (mount.Anchor.parent != parent) mount.Anchor.SetParent(parent, false);
            float left = mirror ? 1f : -1f;   // the character's left
            Vector3 offset = _flashPolice
                ? new Vector3(left * .19f, -.06f, .05f)                                          // on the left of the belt, along the hip
                : new Vector3(left * .09f, .04f, .12f + mount.Bounds.size.z * .5f);         // left chest, against the jacket (1.6.2: 3 cm lower, 2 cm forward)
            Quaternion neutral = Neutral(bone);
            mount.Anchor.localRotation = neutral;
            mount.Anchor.localPosition = neutral * offset;
        }

        private void Position(int i)
        {
            var mount = _mounts[i]; if (mount.Anchor == null) return;
            bool mirror = _root.transform.localScale.x < 0;
            if (i == FlashIndex) { PositionFlashlight(mount, mirror); return; }
            string bone = i <= 2 ? "mixamorig:Spine2" : i <= 4 ? ((i == 3) != mirror ? "mixamorig:RightUpLeg" : "mixamorig:LeftUpLeg") : "mixamorig:Hips";
            Transform parent;
            if (!_bones.TryGetValue(bone, out parent)) return;
            if (mount.Anchor.parent != parent) mount.Anchor.SetParent(parent, false);
            float side = (i % 2 == 1 ? 1f : -1f) * (mirror ? -1f : 1f);
            bool backpack = _mounts[0].Visual != null;
            if (i == 1 || i == 2)
            {
                // with a backpack: one long gun on each side of it, barrel down (as before 2.1.11); without one: both crossed straight on her back
                // (2.2.2) at the same spot and the same depth, each against her back (not one on top of the other)
                Quaternion rotation = backpack
                    ? Quaternion.LookRotation(mount.Lance ? Vector3.up : Vector3.down, Vector3.back) * Quaternion.Inverse(Quaternion.LookRotation(mount.Barrel, mount.Magazine))
                    : Quaternion.AngleAxis(i == 1 ? -35f : 35f, Vector3.forward) * Quaternion.LookRotation(Vector3.up, Vector3.forward)
                        * Quaternion.Inverse(Quaternion.LookRotation(mount.Barrel, ThinAxis(mount.Bounds.size, MajorAxis(mount.Bounds.size))));
                mount.Visual.transform.localRotation = rotation;
                mount.Visual.transform.localPosition = -(rotation * mount.Bounds.center);
            }
            var posed = default(Bounds);
            if (i == 1 || i == 2)
            {
                if (!mount.PosedValid || mount.PosedBackpack != backpack) { mount.Posed = LocalBounds(mount.Visual); mount.PosedValid = true; mount.PosedBackpack = backpack; }
                posed = mount.Posed;
            }
            if (i == 1 || i == 2) mount.Depth = posed.size.z;
            Vector3 offset = i == 0 ? new Vector3(0, -.08f, -.08f - _packDepth * .5f)
                : i <= 2 ? backpack ? new Vector3(side * (_packWidth * .5f + (mount.Lance ? mount.LanceRadius : posed.size.x * .5f) + .005f), -.08f, -.08f - _packDepth * .5f)
                    : new Vector3(-.02f, 0f, -.08f - mount.Depth * .5f + (mount.Lance && i == 2 ? .02f : 0f))              // (2.2.5) the cross 2 cm to her left and 8 cm higher than 2.2.3 (Denis's screenshot: the middle of her back)
                : i <= 4 ? new Vector3(side * .11f, -.15f, .015f)
                : i <= 6 ? new Vector3(side * .20f, -.09f, .04f)
                : new Vector3(0, .03f, -.20f);
            Quaternion neutral = Neutral(bone);
            mount.Anchor.localRotation = neutral;
            mount.Anchor.localPosition = neutral * offset;
        }

        private static Vector3 MagazineDirection(GameObject model, Bounds bounds, Vector3 barrel, Vector3 thin)
        {
            foreach (var node in model.GetComponentsInChildren<Transform>(true))
            {
                string name = node.name.ToLowerInvariant();
                if (!name.Contains("magazine") && !name.StartsWith("mag_")) continue;
                Vector3 direction = Vector3.ProjectOnPlane(model.transform.InverseTransformPoint(node.position), barrel);
                if (direction.sqrMagnitude > .0001f) return direction.normalized;
            }
            // The magazine is the protruding edge in the gun's plane. Item pivots
            // sit near the grip; choose the farther extent perpendicular to the barrel.
            Vector3 across = Vector3.Cross(thin, barrel).normalized;
            int axis = Mathf.Abs(across.x) > .5f ? 0 : Mathf.Abs(across.y) > .5f ? 1 : 2;
            Vector3 directionAxis = Vector3.zero;
            directionAxis[axis] = Mathf.Abs(bounds.min[axis]) > Mathf.Abs(bounds.max[axis]) ? -1f : 1f;
            return Vector3.ProjectOnPlane(directionAxis, barrel).normalized;
        }

        public void SetVisible(bool body, bool shadow)
        {
            _visible = body; _shadow = shadow;
            for (int i = 0; i < _mounts.Length; i++) Show(i);
        }

        private bool Drawn(int i)
        { return _remote ? (_remoteHidden & (1 << i)) != 0 : !ModAPI.PlayerWeaponStowed && _mounts[i].Slot >= 0 && _mounts[i].Slot == _inventory.Drawn || i == 7 && Game.BinocularInUse; }
        private void Show(int i)
        {
            var mount = _mounts[i]; if (mount.Visual == null) return;
            bool drawn = Drawn(i);
            int on = !drawn && (_visible || _shadow) ? 1 : 0;
            var mode = _visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            if (on != mount.ShownOn || mode != mount.ShownMode)   // (WW perf) only on change (these copies are ours: nothing else writes them)
            {
                mount.ShownOn = on; mount.ShownMode = mode;
                foreach (var renderer in mount.Renderers)
                {
                    if (renderer == null) continue;
                    renderer.enabled = on == 1;
                    renderer.shadowCastingMode = mode;
                }
            }
            if (i == FlashIndex && _glow != null)
            {
                bool glow = _remote ? _remoteFlashlight.On : _visible && FlashlightOn;
                if (_glow.enabled != glow) _glow.enabled = glow;
            }
        }

        private static Vector3 MajorAxis(Vector3 size)
        { return size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward; }
        private static Vector3 ThinAxis(Vector3 size, Vector3 major)
        {
            if (major == Vector3.right) return size.y <= size.z ? Vector3.up : Vector3.forward;
            if (major == Vector3.up) return size.x <= size.z ? Vector3.right : Vector3.forward;
            return size.x <= size.y ? Vector3.right : Vector3.up;
        }
        internal static Bounds LocalBounds(GameObject model, bool lanceShaft = false)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null) continue;
                if (lanceShaft && filter.sharedMesh.name != "blastlance_1" && filter.sharedMesh.name != "blastlance_2") continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = model.transform.parent.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }
    }
}
