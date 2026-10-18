using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Modules.CleanHUD.Api
{
    // Style of one compass marker. Icons are white silhouettes tinted by Color.
    public sealed class Marker
    {
        // Built-in: diamond, dot, skull, flag, car, camp, cross, arrow; or a key given to Compass.RegisterIcon.
        public string Icon = "diamond";
        public string Label = "";
        public Color Color = new Color(0.886f, 0.749f, 0.424f, 1f);
        public bool ShowDistance = true;   // "428 m", "1.9 km"
        public float MaxDistance = 0f;     // metres; 0 = no limit
        public bool PinToEdge = false;     // stay at the strip edge while outside the visible arc
        public int Priority = 0;           // higher draws on top

        public Marker Clone()
        {
            return new Marker { Icon = Icon, Label = Label, Color = Color, ShowDistance = ShowDistance, MaxDistance = MaxDistance, PinToEdge = PinToEdge, Priority = Priority };
        }
    }

    // Compass marker API, v1. Call from the main thread. Ids are never reused within a session.
    // Soft dependency for other mods: keep every call in one class marked [MethodImpl(MethodImplOptions.NoInlining)]
    // and check Chainloader.PluginInfos.ContainsKey("com.denis.apocalypter.cleanhud") first.
    public static class Compass
    {
        public const int Version = 1;

        internal sealed class Entry
        {
            internal int Id;
            internal string Owner;
            internal Transform Target;
            internal bool HasTarget;
            internal Func<Vector3> Getter;
            internal Vector3 MapLocal;
            internal bool Anchored;
            internal Marker Style;
            internal bool Broken;
            internal bool Identified;
            internal string IconKey; internal int IconRevision; internal Texture Icon;   // HUD's resolved icon for IconKey
        }

        internal static readonly List<Entry> Entries = new List<Entry>();
        internal static int IconRevision;   // bumped whenever CustomIcons changes or the HUD's icon cache is rebuilt
        internal static readonly Dictionary<string, Texture2D> CustomIcons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static int _next = 1;

        // Follows a Transform (safe across the 1000 m floating-origin shift). Removed automatically when the object is destroyed.
        public static int Add(string owner, Transform target, Marker style)
        {
            if (target == null) throw new ArgumentNullException("target");
            return Put(new Entry { Owner = owner ?? "", Target = target, HasTarget = true, Style = (style ?? new Marker()).Clone() });
        }

        // World position supplied by the caller every frame.
        public static int Add(string owner, Func<Vector3> worldPosition, Marker style)
        {
            if (worldPosition == null) throw new ArgumentNullException("worldPosition");
            return Put(new Entry { Owner = owner ?? "", Getter = worldPosition, Style = (style ?? new Marker()).Clone() });
        }

        // Fixed point relative to the MapMagic root (the floating-origin anchor). The HUD adds the anchor's position every frame.
        public static int AddAnchored(string owner, Vector3 mapLocal, Marker style)
        {
            return Put(new Entry { Owner = owner ?? "", MapLocal = mapLocal, Anchored = true, Style = (style ?? new Marker()).Clone() });
        }

        public static bool Update(int id, Marker style)
        {
            var e = Get(id); if (e == null || style == null) return false;
            e.Style = style.Clone(); return true;
        }

        public static bool Move(int id, Vector3 mapLocal)
        {
            var e = Get(id); if (e == null || !e.Anchored) return false;
            e.MapLocal = mapLocal; return true;
        }

        public static bool Remove(int id)
        {
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Id == id) { Entries.RemoveAt(i); return true; }
            return false;
        }

        public static int RemoveAll(string owner)
        {
            return Entries.RemoveAll(e => string.Equals(e.Owner, owner ?? "", StringComparison.Ordinal));
        }

        public static bool Exists(int id) { return Get(id) != null; }
        public static int Count { get { return Entries.Count; } }

        // White silhouette on transparent; tinted by Marker.Color. The texture stays owned by the caller.
        public static void RegisterIcon(string key, Texture2D icon)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key");
            if (icon == null) CustomIcons.Remove(key); else CustomIcons[key] = icon;
            IconRevision++;
        }

        // MapMagic root used for AddAnchored; null until the world exists.
        public static Transform MapRoot { get { return WildWasteland.Modules.CleanHUD.Hud.MapRoot(); } }

        private static Entry Get(int id) { for (int i = 0; i < Entries.Count; i++) if (Entries[i].Id == id) return Entries[i]; return null; }
        private static int Put(Entry e) { e.Id = _next++; Entries.Add(e); return e.Id; }
    }
}
