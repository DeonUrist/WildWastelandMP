using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace WildWasteland.Modules.CleanHUD
{
    // Native Helveticrap by default, optional forcedSquare. Both atlases work before scene fonts are loaded.
    internal sealed class GlyphFont
    {
        internal struct G { internal float X, Y, W, H, Ox, Top, Adv; }
        internal Texture2D Atlas;
        internal float Size, Ascent, Descent, AtlasW, AtlasH;
        internal readonly Dictionary<char, G> Map = new Dictionary<char, G>();

        private static GlyphFont _game, _clean;
        internal static void Reset() { _game = _clean = null; }
        internal static GlyphFont Get(bool gameOnly = false)
        {
            bool clean = !gameOnly && CleanHudModule.CleanFont != null && CleanHudModule.CleanFont.Value;
            var cached = clean ? _clean : _game;
            if (cached != null && cached.Atlas != null) return cached;
            string name = clean ? "font" : "game_font";
            var f = new GlyphFont();
            f.Atlas = Assets.Tex(name + "_atlas");
            var asm = Assembly.GetExecutingAssembly();
            string json;
            using (var s = asm.GetManifestResourceStream("CleanHUD." + name + "_meta.json"))
            using (var r = new StreamReader(s)) json = r.ReadToEnd();
            var d = (Dictionary<string, object>)Json.Parse(json);
            f.Size = Json.Num(d, "size", 96); f.Ascent = Json.Num(d, "ascent", 58); f.Descent = Json.Num(d, "descent", 17);
            f.AtlasW = Json.Num(d, "w", 1024); f.AtlasH = Json.Num(d, "h", 512);
            var gl = Json.Obj(d, "glyphs");
            foreach (var kv in gl)
            {
                var a = (List<object>)kv.Value;
                if (kv.Key.Length != 1 || a.Count < 7) continue;
                f.Map[kv.Key[0]] = new G { X = F(a[0]), Y = F(a[1]), W = F(a[2]), H = F(a[3]), Ox = F(a[4]), Top = F(a[5]), Adv = F(a[6]) };
            }
            if (clean) _clean = f; else _game = f;
            return f;
        }
        private static float F(object o) { return o is double ? (float)(double)o : 0f; }

        internal float Measure(string text, float size, float spacing)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float s = size / Size, w = 0f; G g;
            for (int i = 0; i < text.Length; i++)
            {
                if (!Map.TryGetValue(text[i], out g) && !Map.TryGetValue(' ', out g)) continue;
                w += g.Adv * s + (i < text.Length - 1 ? spacing : 0f);
            }
            return w;
        }

        // The same atlas in IMGUI windows, sharing the menu's text metrics and assets.
        internal static void Draw(Rect rect, string text, float size, Color color, TextAnchor align)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text)) return;
            var font = Get(); if (font == null || font.Atlas == null) return;
            font.DrawRange(rect, text, 0, text.Length, size, color, align);
        }
        internal static void DrawGame(Rect rect, string text, float size, Color color, TextAnchor align)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text)) return;
            var font = Get(true); if (font == null || font.Atlas == null) return;
            font.DrawRange(rect, text, 0, text.Length, size, color, align);
        }

        private float TextWidth(string text, int start, int end, float size)
        {
            float width = 0; G glyph;
            for (int i = start; i < end; i++)
                if (Map.TryGetValue(char.ToUpperInvariant(text[i]), out glyph) || Map.TryGetValue('?', out glyph)) width += glyph.Adv;
            return width * size / Size;
        }

        private void DrawRange(Rect rect, string text, int start, int end, float size, Color color, TextAnchor align)
        {
            float width = TextWidth(text, start, end, size);
            if (width > rect.width && width > 0) { size *= rect.width / width; width = rect.width; }
            float scale = size / Size, x = rect.x;
            if (align == TextAnchor.MiddleCenter || align == TextAnchor.UpperCenter || align == TextAnchor.LowerCenter) x = rect.center.x - width / 2;
            else if (align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight) x = rect.xMax - width;
            float baseline = rect.center.y + (Ascent - Descent) * scale / 2;
            var previous = GUI.color; GUI.color = color;
            for (int i = start; i < end; i++)
            {
                char ch = char.ToUpperInvariant(text[i]); G glyph;
                if (!Map.TryGetValue(ch, out glyph) && !Map.TryGetValue('?', out glyph)) continue;
                if (ch != ' ' && glyph.W > 1 && glyph.H > 1)
                    GUI.DrawTextureWithTexCoords(new Rect(x + glyph.Ox * scale, baseline - glyph.Top * scale, glyph.W * scale, glyph.H * scale), Atlas,
                        new Rect(glyph.X / AtlasW, 1f - (glyph.Y + glyph.H) / AtlasH, glyph.W / AtlasW, glyph.H / AtlasH));
                x += glyph.Adv * scale;
            }
            GUI.color = previous;
        }

        private int LineEnd(string text, int start, float size, float width, out int next)
        {
            float used = 0; int space = -1; G glyph;
            for (int i = start; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\n') { next = i + 1; return i; }
                if (char.IsWhiteSpace(ch)) space = i;
                if (Map.TryGetValue(char.ToUpperInvariant(ch), out glyph) || Map.TryGetValue('?', out glyph)) used += glyph.Adv * size / Size;
                if (used > width && i > start)
                { int end = space > start ? space : i; next = end; while (next < text.Length && char.IsWhiteSpace(text[next]) && text[next] != '\n') next++; return end; }
            }
            next = text.Length; return text.Length;
        }

        internal static float WrappedHeight(string text, float size, float width)
        {
            if (string.IsNullOrEmpty(text)) return size * 1.2f;
            var font = Get(); int lines = 0, next;
            for (int start = 0; start < text.Length; start = next) { font.LineEnd(text, start, size, width, out next); lines++; }
            return lines * size * 1.2f;
        }

        internal static void DrawWrapped(Rect rect, string text, float size, Color color, TextAnchor align)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text)) return;
            var font = Get(); if (font == null || font.Atlas == null) return;
            float y = rect.y; int next;
            for (int start = 0; start < text.Length; start = next)
            {
                int end = font.LineEnd(text, start, size, rect.width, out next);
                font.DrawRange(new Rect(rect.x, y, rect.width, size * 1.2f), text, start, end, size, color, TextAnchor.MiddleLeft);
                y += size * 1.2f;
            }
        }
    }

    // Single-line text drawn from the GlyphFont atlas. Works with Shadow/Outline (they modify the mesh).
    public sealed class GlyphText : MaskableGraphic
    {
        private static readonly HashSet<GlyphText> Live = new HashSet<GlyphText>();
        protected override void OnEnable() { base.OnEnable(); Live.Add(this); SetAllDirty(); }
        protected override void OnDisable() { Live.Remove(this); base.OnDisable(); }
        internal static void RefreshFont() { foreach (var text in Live) if (text != null) text.SetAllDirty(); }
        [NonSerialized] public string Text = "";
        [NonSerialized] public float Size = 24f;
        [NonSerialized] public float Spacing = 0f;
        [NonSerialized] public TextAnchor Align = TextAnchor.MiddleLeft;
        [NonSerialized] internal int LeadingCharacters;
        [NonSerialized] internal Color LeadingColor = Color.white;

        public override Texture mainTexture { get { var f = GlyphFont.Get(); return f != null ? f.Atlas : null; } }

        public void Set(string t)
        {
            t = t ?? "";
            if (t == Text) return;
            Text = t;
            SetVerticesDirty();
        }

        public float Width { get { return GlyphFont.Get().Measure(Text, Size, Spacing); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (string.IsNullOrEmpty(Text)) return;
            var f = GlyphFont.Get();
            if (f == null || f.Atlas == null) return;
            float s = Size / f.Size;
            var r = GetPixelAdjustedRect();
            float w = f.Measure(Text, Size, Spacing);
            float x;
            switch (Align)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: x = r.center.x - w / 2f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x = r.xMax - w; break;
                default: x = r.xMin; break;
            }
            float baseline;
            switch (Align)
            {
                case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: baseline = r.yMax - f.Ascent * s; break;
                case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: baseline = r.yMin + f.Descent * s; break;
                default: baseline = r.center.y + (f.Descent - f.Ascent) * s / 2f; break;
            }
            Color32 c = color;
            var leading = LeadingColor; leading.a *= color.a;
            Color32 lead = leading;
            int v = 0;
            for (int i = 0; i < Text.Length; i++)
            {
                GlyphFont.G g;
                Color32 tint = i < LeadingCharacters ? lead : c;
                if (!f.Map.TryGetValue(Text[i], out g)) { if (f.Map.TryGetValue(' ', out g)) x += g.Adv * s + Spacing; continue; }
                if (g.W > 1f && g.H > 1f && Text[i] != ' ')
                {
                    float l = x + g.Ox * s, t = baseline + g.Top * s, rr = l + g.W * s, b = t - g.H * s;
                    float u0 = g.X / f.AtlasW, u1 = (g.X + g.W) / f.AtlasW, v1 = 1f - g.Y / f.AtlasH, v0 = 1f - (g.Y + g.H) / f.AtlasH;
                    vh.AddVert(new Vector3(l, b), tint, new Vector2(u0, v0));
                    vh.AddVert(new Vector3(l, t), tint, new Vector2(u0, v1));
                    vh.AddVert(new Vector3(rr, t), tint, new Vector2(u1, v1));
                    vh.AddVert(new Vector3(rr, b), tint, new Vector2(u1, v0));
                    vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v + 2, v + 3, v);
                    v += 4;
                }
                x += g.Adv * s + Spacing;
            }
        }
    }
}
