using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildWasteland.Modules.CleanHUD
{
    internal sealed class WeaponInfo
    {
        internal string Key, Caliber, Kind; // Kind: gun, melee, thrown
        internal WeaponInfo(string key, string cal, string kind) { Key = key; Caliber = cal; Kind = kind; }
    }

    internal static class Assets
    {
        private static readonly Dictionary<string, Texture2D> Embedded = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> Rendered = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> RenderFailed = new HashSet<string>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static Texture2D _white;
        private static readonly Queue<GameObject> IconQueue = new Queue<GameObject>();
        private static readonly HashSet<string> Queued = new HashSet<string>();
        private static IEnumerator _render;
        private static string _renderKey;
        internal static int IconRevision;

        // Calibers as printed on the game's own item names, e.g. "M16A1 (5.56mm)".
        internal static readonly Dictionary<string, WeaponInfo> Weapons = Table();
        private static Dictionary<string, WeaponInfo> Table()
        {
            var t = new Dictionary<string, WeaponInfo>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, string> a = (k, c, kind) => t[k] = new WeaponInfo(k, c, kind);
            a("folk_17", "9MM", "gun"); a("9mm_borz_smg", "9MM", "gun");
            a("22_pipe_pistol", ".22LR", "gun"); a("22_pipe_revolver", ".22LR", "gun"); a("22_pipe_smg", ".22LR", "gun");
            a("akms", "7.62MM", "gun"); a("akm_trash", "7.62MM", "gun"); a("akm_drum", "7.62MM", "gun");
            a("m16a1", "5.56MM", "gun");
            a("redmark_m11", ".30-06", "gun"); a("redmark_m11_scoped", ".30-06", "gun");
            a("rochester_m24", "12GA", "gun"); a("rochester_m24_chopped", "12GA", "gun");
            a("slamberg_500", "12GA", "gun"); a("slamberg_500_chopped", "12GA", "gun");
            a("slamfire_shotgun", "20GA", "gun");
            a("crossbow", "ARROW", "gun");
            a("blastlance_1", "THROWN", "thrown"); a("blastlance_2", "THROWN", "thrown");
            a("machete", "MELEE", "melee"); a("old_knife", "MELEE", "melee"); a("shiv", "MELEE", "melee"); a("pipe_wrench", "MELEE", "melee");
            return t;
        }

        private static readonly Regex CloneSuffix = new Regex(@"\(Clone\)\d*$|\s*\(\d+\)$", RegexOptions.Compiled);
        internal static string KeyOf(GameObject go)
        {
            if (go == null) return null;
            var n = go.name;
            for (int i = 0; i < 3; i++) n = CloneSuffix.Replace(n, "");
            n = n.Trim();
            return n.StartsWith("MP_", StringComparison.Ordinal) ? n.Substring(3) : n;
        }

        private static readonly Regex Paren = new Regex(@"\(([^)]+)\)\s*$", RegexOptions.Compiled);
        // Unknown (modded) weapon: caliber from the item name "Name (cal)", kind from the Reload FSM.
        internal static WeaponInfo Describe(GameObject item, string itemName)
        {
            var key = KeyOf(item);
            WeaponInfo w;
            if (key != null && Weapons.TryGetValue(key, out w)) return w;
            string cal = null;
            if (!string.IsNullOrEmpty(itemName))
            {
                var m = Paren.Match(itemName);
                if (m.Success) cal = m.Groups[1].Value.Trim().ToUpperInvariant();
            }
            bool gun = Bindings.Fsm(item, "Reload") != null;
            if (cal == null) cal = gun ? "AMMO" : "MELEE";
            w = new WeaponInfo(key ?? "", cal, gun ? "gun" : "melee");
            if (key != null) Weapons[key] = w;
            return w;
        }

        internal static Texture2D Tex(string name)
        {
            Texture2D t;
            if (Embedded.TryGetValue(name, out t) && t != null) return t;
            if (Missing.Contains(name)) return null;
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("CleanHUD." + name + ".png"))
            {
                if (s == null) { Missing.Add(name); return null; }
                var buf = new byte[s.Length];
                int read = 0; while (read < buf.Length) { int r = s.Read(buf, read, buf.Length - read); if (r <= 0) break; read += r; }
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "CleanHUD." + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                t.LoadImage(buf, false);
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            Embedded[name] = t;
            if (name.StartsWith("panel_", StringComparison.Ordinal)) AccentTheme.RegisterPanel(t);
            return t;
        }

        internal static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "CleanHUD.white", hideFlags = HideFlags.DontUnloadUnusedAsset };
                    var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                    _white.SetPixels32(px); _white.Apply(false, true);
                }
                return _white;
            }
        }

        // Vertical gradient, white at the top to `bottom` grey (bar fill = colour * gradient, as linear-gradient(c, shade(c)) in the mockup).
        internal static Texture2D VGradient(float bottom)
        {
            var key = "vgrad" + bottom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Texture2D t; if (Embedded.TryGetValue(key, out t) && t != null) return t;
            t = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontUnloadUnusedAsset };
            for (int y = 0; y < 32; y++) { float v = Mathf.Lerp(bottom, 1f, y / 31f); t.SetPixel(0, y, new Color(v, v, v, 1)); }
            t.Apply(false, true); Embedded[key] = t; return t;
        }

        // Compass line: horizontal fade (0..12% / 88..100%) x vertical colour ramp.
        internal static Texture2D CompassLine()
        {
            Texture2D t; if (Embedded.TryGetValue("cmpline", out t) && t != null) return t;
            const int W = 512, H = 4;
            t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontUnloadUnusedAsset };
            var top = new Color(1f, 1f, 1f, .9f); var bot = new Color(205 / 255f, 196 / 255f, 170 / 255f, .85f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = Color.Lerp(bot, top, y / (float)(H - 1));
                    c.a *= Fade((x + .5f) / W);
                    t.SetPixel(x, y, c);
                }
            t.Apply(false, true); Embedded["cmpline"] = t; return t;
        }
        // Soft shadow under the line (box-shadow 0 2px 4px rgba(0,0,0,.6)).
        internal static Texture2D CompassShadow()
        {
            Texture2D t; if (Embedded.TryGetValue("cmpshadow", out t) && t != null) return t;
            const int W = 512, H = 16;
            t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontUnloadUnusedAsset };
            for (int y = 0; y < H; y++)
            {
                float d = Mathf.Abs((H - 1 - y) - 7.5f) / 4f; // rows from the top; centre 2px below the 3px line
                float a = .6f * Mathf.Exp(-d * d);
                for (int x = 0; x < W; x++) t.SetPixel(x, y, new Color(0, 0, 0, a * Fade((x + .5f) / W)));
            }
            t.Apply(false, true); Embedded["cmpshadow"] = t; return t;
        }
        internal static float Fade(float t)
        {
            if (t < .12f) return Mathf.Clamp01(t / .12f);
            if (t > .88f) return Mathf.Clamp01((1f - t) / .12f);
            return 1f;
        }
        internal static Texture2D Caret()
        {
            Texture2D t; if (Embedded.TryGetValue("caret", out t) && t != null) return t;
            const int W = 28, H = 18;
            t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fy = (H - 1 - y + .5f) / H; // 0 top .. 1 bottom
                    float half = (1f - fy) * .5f;
                    float a = Mathf.Clamp01((half - Mathf.Abs((x + .5f) / W - .5f)) * W);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply(false, true); Embedded["caret"] = t; return t;
        }

        // Weapon icon: embedded flat render for the vanilla weapons, otherwise a silhouette rendered at runtime from the item's own meshes.
        internal static Texture2D WeaponIcon(GameObject item)
        {
            var key = KeyOf(item);
            if (string.IsNullOrEmpty(key)) return null;
            var t = Tex(key);
            if (t != null) return t;
            if (Rendered.TryGetValue(key, out t) && t != null) return t;
            if (RenderFailed.Contains(key)) return null;
            if (Queued.Add(key)) IconQueue.Enqueue(item);
            return null;
        }

        internal static bool RenderStep()
        {
            if (_render == null)
            {
                if (IconQueue.Count == 0) return false;
                var item = IconQueue.Dequeue();
                if (item == null) return IconQueue.Count > 0;
                BeginRender(item, KeyOf(item));
            }
            try { if (_render.MoveNext()) return true; }
            catch (Exception e) { RenderFailed.Add(_renderKey); Queued.Remove(_renderKey); CleanHudModule.Warn("Icon render failed: " + e.Message); }
            var disposable = _render as IDisposable; if (disposable != null) disposable.Dispose();
            _render = null; _renderKey = null;
            return IconQueue.Count > 0;
        }

        private static void BeginRender(GameObject item, string key)
        {
            _renderKey = key;
            _render = IconRenderer.Render(item, key, texture =>
            {
                if (texture != null) Rendered[key] = texture; else RenderFailed.Add(key);
                Queued.Remove(key); IconRevision++;
            });
        }

        internal static void Dispose()
        {
            MapIcons.Dispose();
            SceneLoaded();
            foreach (var t in Rendered.Values) if (t != null) UnityEngine.Object.Destroy(t);
            Rendered.Clear(); RenderFailed.Clear();
            foreach (var t in Embedded.Values) if (t != null) UnityEngine.Object.Destroy(t);
            Embedded.Clear(); Missing.Clear();
            if (_white != null) UnityEngine.Object.Destroy(_white); _white = null;
            IconRenderer.Clear();
        }

        internal static void SceneLoaded()
        {
            var disposable = _render as IDisposable; if (disposable != null) disposable.Dispose();
            _render = null; _renderKey = null; IconQueue.Clear(); Queued.Clear();
        }
        internal static void Preload()
        {
            MapIcons.Preload();
            const string prefix = "CleanHUD.";
            foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".png", StringComparison.Ordinal))
                    Tex(name.Substring(prefix.Length, name.Length - prefix.Length - 4));
            var white = White; VGradient(.75f); VGradient(.72f); CompassLine(); CompassShadow(); Caret();
            GlyphFont.Get();
            IconRenderer.Preload();
        }
    }

    // Orthographic side view (muzzle to the left), white unlit on transparent, cropped. Same framing as the offline renders.
    internal static class IconRenderer
    {
        private const int Layer = 31, W = 1024, H = 384;
        private static Dictionary<string, GameObject> _prefabs;
        internal static void Clear() { _prefabs = null; }
        internal static void Preload() { Prefab(""); }

        private static GameObject Prefab(string key)
        {
            if (_prefabs == null)
            {
                _prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (go != null && !go.scene.IsValid() && go.transform.parent == null && !_prefabs.ContainsKey(go.name)) _prefabs[go.name] = go;
            }
            GameObject p; return _prefabs.TryGetValue(key, out p) ? p : null;
        }

        internal static IEnumerator Render(GameObject item, string key, Action<Texture2D> complete)
        {
            var src = Prefab(key) ?? item;
            bool fromPrefab = src != item;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) { complete(null); yield break; }
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetColor("_Color", Color.white);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetInt("_ZWrite", 1);
            var rig = new GameObject("CleanHUD.IconRig") { hideFlags = HideFlags.HideAndDontSave };
            rig.transform.position = new Vector3(0f, -4000f, 0f);
            RenderTexture rt = null; Camera cam = null; Texture2D raw = null, outT = null;
            try
            {
                var b = new Bounds(); bool any = false;
                foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mf.sharedMesh == null || mr == null) continue;
                    // On the prefab, respect its default visibility; a held instance has its renderers switched off by the game.
                    if (fromPrefab && (!mr.enabled || !mf.gameObject.activeInHierarchy)) continue;
                    var m = src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var go = new GameObject("part") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
                    go.transform.SetParent(rig.transform, false);
                    go.transform.localPosition = m.GetColumn(3);
                    go.transform.localRotation = m.rotation;
                    go.transform.localScale = m.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    var mb = mf.sharedMesh.bounds;
                    for (int cx = -1; cx <= 1; cx += 2) for (int cy = -1; cy <= 1; cy += 2) for (int cz = -1; cz <= 1; cz += 2)
                    {
                        var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3(cx, cy, cz)));
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                    yield return null;
                }
                if (!any) { complete(null); yield break; }
                // Long axis decides the view: weapons are modelled along local Z (blast lances along Y).
                var e = b.size;
                Vector3 fwd, up;
                if (e.y > e.z && e.y > e.x) { fwd = Vector3.forward; up = Vector3.right; }
                else { fwd = Vector3.right; up = Vector3.up; }
                var camGo = new GameObject("CleanHUD.IconCam") { hideFlags = HideFlags.HideAndDontSave };
                cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.cullingMask = 1 << Layer;
                cam.allowHDR = false; cam.allowMSAA = true;
                float aspect = W / (float)H;
                float hgt = Mathf.Abs(Vector3.Dot(e, up)), len = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
                cam.orthographicSize = Mathf.Max(hgt * .5f, len * .5f / aspect) * 1.06f;
                float depth = Mathf.Max(e.x, Mathf.Max(e.y, e.z)) + 1f;
                cam.nearClipPlane = 0.01f; cam.farClipPlane = depth * 2f + 2f;
                var centre = rig.transform.TransformPoint(b.center);
                var wf = rig.transform.TransformDirection(fwd); var wu = rig.transform.TransformDirection(up);
                camGo.transform.position = centre - wf * depth;
                camGo.transform.rotation = Quaternion.LookRotation(wf, wu);
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                try
                {
                    RenderTexture.active = rt;
                    raw = new Texture2D(W, H, TextureFormat.RGBA32, false);
                    raw.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    raw.Apply(false);
                }
                finally { RenderTexture.active = prev; }
                var px = raw.GetPixels32();
                UnityEngine.Object.Destroy(raw); raw = null;
                int x0 = W, y0 = H, x1 = -1, y1 = -1;
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        var a = px[y * W + x].r;
                        if (a > 8) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                    }
                    yield return null;
                }
                if (x1 < 0) { complete(null); yield break; }
                const int pad = 12;
                int cw = x1 - x0 + 1 + pad * 2, ch = y1 - y0 + 1 + pad * 2;
                outT = new Texture2D(cw, ch, TextureFormat.RGBA32, true) { name = "CleanHUD.icon." + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontUnloadUnusedAsset };
                var opx = new Color32[cw * ch];
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < cw; x++)
                    {
                        int sx = x0 + x - pad, sy = y0 + y - pad;
                        byte a = (sx >= 0 && sy >= 0 && sx < W && sy < H) ? px[sy * W + sx].r : (byte)0;
                        opx[y * cw + x] = new Color32(255, 255, 255, a);
                    }
                    yield return null;
                }
                outT.SetPixels32(opx); outT.Apply(true, false);
                CleanHudModule.Log("Rendered a flat icon for " + key + " (" + cw + "x" + ch + ")");
                complete(outT); outT = null;
            }
            finally
            {
                if (raw != null) UnityEngine.Object.Destroy(raw);
                if (outT != null) UnityEngine.Object.Destroy(outT);
                if (cam != null) { cam.targetTexture = null; UnityEngine.Object.Destroy(cam.gameObject); }
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                UnityEngine.Object.Destroy(rig);
                UnityEngine.Object.Destroy(mat);
            }
        }
    }
}
