using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildWasteland.Core.UI
{
    /// <summary>Standard groups of the F7 tool window (left column order).</summary>
    public static class ToolGroups
    {
        public const string General = "Wild Wasteland";
        public const string Player = "Player body & animation";
        public const string Npc = "NPC AI & navigation";
        public const string Weapons = "Weapons & HUD";
        public const string Vehicles = "Patrols & vehicles";
        public const string Poses = "Seat & turret poses";
        public const string Merchants = "Merchants";
        public const string Debug = "Debug & performance";
        public static readonly string[] Order = { General, Player, Npc, Weapons, Vehicles, Poses, Merchants, Debug };
    }

    /// <summary>
    /// One page of the F7 tool window. Override <see cref="Draw"/> and lay out rows with the <see cref="ToolLayout"/> helpers;
    /// the window measures the height from the layout cursor, so pages need not know their size in advance.
    /// </summary>
    public abstract class ToolPage
    {
        public string Group = ToolGroups.General;
        public string Title = "Page";
        /// <summary>Owning module (null = Core). Pages of a module that is not running are shown with a note and drawn read-only unless <see cref="ShowWhenModuleOff"/>.</summary>
        public IModule Owner;
        public bool ShowWhenModuleOff;
        public bool DebugOnly;
        /// <summary>Editors and in-game tools: drawn first on the Debug page.</summary>
        public bool IsTool;
        /// <summary>Called when the page becomes visible / the window opens.</summary>
        public virtual void OnShow() { }
        /// <summary>Called every frame while the window is open on this page (before drawing; use for live data).</summary>
        public virtual void Update() { }
        public abstract void Draw(ToolLayout l);
    }

    /// <summary>A page built from a delegate, for small pages.</summary>
    public sealed class ActionPage : ToolPage
    {
        readonly Action<ToolLayout> _draw;
        public ActionPage(IModule owner, string group, string title, Action<ToolLayout> draw) { Owner = owner; Group = group; Title = title; _draw = draw; }
        public override void Draw(ToolLayout l) { _draw(l); }
    }

    /// <summary>
    /// Shows config entries (any ConfigFile) as editable rows. Sections are matched exactly or by prefix ("Patrols / Pose*").
    /// Changes are written immediately (ConfigEntry setters raise SettingChanged; the file is saved by BepInEx).
    /// </summary>
    public sealed class ConfigPage : ToolPage
    {
        readonly ConfigFile _file;
        readonly string[] _sections;
        readonly Func<ConfigDefinition, bool> _filter;
        public string Intro;

        public ConfigPage(IModule owner, string group, string title, ConfigFile file, params string[] sections)
        { Owner = owner; Group = group; Title = title; _file = file; _sections = sections; }
        public ConfigPage(IModule owner, string group, string title, ConfigFile file, Func<ConfigDefinition, bool> filter)
        { Owner = owner; Group = group; Title = title; _file = file; _filter = filter; }

        bool Match(ConfigDefinition d)
        {
            if (_filter != null) return _filter(d);
            foreach (var s in _sections)
            {
                if (s.EndsWith("*")) { if (d.Section.StartsWith(s.Substring(0, s.Length - 1), StringComparison.Ordinal)) return true; }
                else if (d.Section == s) return true;
            }
            return false;
        }

        readonly List<ConfigDefinition> _keys = new List<ConfigDefinition>();
        int _count = -1;

        public override void Draw(ToolLayout l)
        {
            if (_count != _file.Count) { _count = _file.Count; _keys.Clear(); foreach (var k in _file.Keys) if (Match(k)) _keys.Add(k); }
            bool any = false;
            foreach (var k in _keys) if (l.CanShowEntry(_file[k])) { any = true; break; }
            if (!any) return;   // every entry hidden (Debug page filter): no intro, no headings
            if (Intro != null) l.Text(Intro);
            string section = null;
            foreach (var k in _keys)
            {
                ConfigEntryBase e = _file[k]; if (!l.CanShowEntry(e)) continue;
                if (k.Section != section) { section = k.Section; l.Heading(section); }
                l.Entry(e);
            }
        }
    }

    /// <summary>Row-based layout over the ledger widgets, in the scroll view's local space.</summary>
    public sealed class ToolLayout
    {
        public float X, Y, Width;
        public const float Row = 30, LabelWidth = 0.46f;
        internal string Prefix;            // unique key prefix per page for number buffers
        internal Func<ConfigEntryBase, bool> EntryFilter;
        internal bool DeduplicateEntries;
        internal string SearchContext;
        internal bool Searching { get { return _search.Length != 0; } }
        internal bool SearchResults;
        string _search = "", _category, _categoryHeading, _heading, _pendingHeading;
        bool _insideEntry;
        readonly HashSet<string> _expanded = new HashSet<string>();
        readonly HashSet<ConfigEntryBase> _drawn = new HashSet<ConfigEntryBase>();
        static ConfigEntryBase _rebinding;
        static GUIStyle _wrap, _wrapSource;

        internal void Begin(float width, string prefix, string search = null) { X = 0; Y = 4; Width = width; Prefix = prefix; EntryFilter = null; DeduplicateEntries = false; _drawn.Clear(); _search = (search ?? "").Trim(); SearchContext = _category = _categoryHeading = _heading = _pendingHeading = null; _insideEntry = SearchResults = false; }
        internal bool Matches(string text) { return !Searching || _insideEntry || Contains(text) || Contains(_category) || Contains(SearchContext) || Contains(_heading); }
        bool Contains(string text) { return text != null && text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0; }
        internal void SearchSection(string text) { SearchContext = text; _heading = _pendingHeading = null; _insideEntry = false; }
        internal bool Category(string key, string title)
        {
            SearchSection(null); _category = title; _categoryHeading = null;
            if (Searching) { _categoryHeading = title; return true; }
            bool expanded = _expanded.Contains(key);
            if (Button((expanded ? "[-]  " : "[+]  ") + title)) { expanded = !expanded; if (expanded) _expanded.Add(key); else _expanded.Remove(key); }
            return expanded;
        }
        bool ShowRow(string text)
        {
            if (!Matches(text)) return false;
            SearchResults = true;
            if (_categoryHeading != null) { DrawHeading(_categoryHeading); _categoryHeading = null; }
            if (_pendingHeading != null) { DrawHeading(_pendingHeading); _pendingHeading = null; }
            return true;
        }
        internal bool CanShowEntry(ConfigEntryBase e) { return e != null && MultiplayerSettings.CanEdit(e) && (EntryFilter == null || EntryFilter(e)) && (!DeduplicateEntries || !_drawn.Contains(e)) && (Matches(e.Definition.Section) || Matches(e.Definition.Key) || Matches(PageLabel(e)) || Matches(e.Description != null ? e.Description.Description : null)); }
        bool BeginEntry(ConfigEntryBase e) { if (!CanShowEntry(e)) return false; if (DeduplicateEntries && GUI.enabled) _drawn.Add(e); _insideEntry = true; ShowRow(null); return true; }

        public Rect Next(float h = Row)
        {
            if (Ledger.SettingsActive && h <= 40) h = h <= 22 ? 46 : 54;
            var r = new Rect(X + (Ledger.SettingsActive ? 0 : 8), Y, Width - (Ledger.SettingsActive ? 0 : 16), h); Y += h + 4; return r;
        }
        public void Space(float h = 8) { if (!Searching) Y += h; }

        void Caption(Rect row, string text, float fraction = LabelWidth, GUIStyle style = null, Color? color = null)
        {
            float inset = Ledger.SettingsActive ? 22 : 0;
            Ledger.RowHighlight(row);
            var ink = color ?? (Ledger.SettingsActive && GUI.enabled && row.Contains(Event.current.mousePosition) ? Ledger.Accent : Ledger.Text);
            Ledger.Label(new Rect(row.x + inset, row.y, row.width * fraction - inset, row.height), text, style ?? Ledger.Body, ink);
        }

        public void Heading(string text)
        {
            _heading = text;
            if (Searching) { _pendingHeading = text; return; }
            DrawHeading(text);
        }

        void DrawHeading(string text)
        {
            if (Ledger.SettingsActive)
            {
                Y += 12; Ledger.Label(new Rect(X + 22, Y, Width - 44, 40), text, Ledger.Heading, Ledger.Accent);
                Y += 48; return;
            }
            Y += 6;
            Ledger.Label(new Rect(X + 8, Y, Width - 16, 24), text.ToUpperInvariant(), Ledger.Heading, Ledger.Accent);
            Y += 24; Ledger.Rule(X + 8, Y, Width - 16); Y += 6;
        }

        public void Text(string text, Color? color = null)
        {
            if (!ShowRow(text)) return;
            if (_wrap == null || _wrapSource != Ledger.Small) { _wrap = new GUIStyle(Ledger.Small) { wordWrap = true }; _wrapSource = Ledger.Small; }
            var st = _wrap;
            float inset = Ledger.SettingsActive ? 22 : 8;
            float h = Mathf.Max(20, Ledger.TextHeight(text, st, Width - inset * 2));
            Ledger.Label(new Rect(X + inset, Y, Width - inset * 2, h), text, st, color ?? Ledger.Muted, TextAnchor.UpperLeft);
            Y += h + (Ledger.SettingsActive ? 10 : 4);
        }

        public void Value(string label, string value)
        {
            if (!ShowRow(label) && !ShowRow(value)) return;
            var r = Next(22);
            Caption(r, label, LabelWidth, Ledger.Small, Ledger.Muted);
            Ledger.Label(new Rect(r.x + r.width * LabelWidth, r.y, r.width * (1 - LabelWidth), r.height), value, Ledger.Body, Ledger.Text);
        }

        public bool Button(string text, bool enabled = true, bool primary = false, float width = 0)
        {
            if (!ShowRow(text)) return false;
            var r = Next(32); if (width > 0) r.width = Mathf.Min(r.width, width);
            return Ledger.Button(r, text, enabled, primary);
        }

        /// <summary>Two or more buttons on one row; returns the index clicked or -1.</summary>
        public int Buttons(params string[] labels)
        {
            bool show = false; foreach (var label in labels) if (ShowRow(label)) { show = true; break; }
            if (!show) return -1;
            var r = Next(32); float w = (r.width - (labels.Length - 1) * 8) / labels.Length; int hit = -1;
            for (int i = 0; i < labels.Length; i++) if (Ledger.Button(new Rect(r.x + i * (w + 8), r.y, w, r.height), labels[i])) hit = i;
            return hit;
        }

        public bool Toggle(string label, bool value)
        {
            if (!ShowRow(label)) return value;
            var r = Next(26);
            return Ledger.Toggle(r, value, label);
        }

        // labels pages give their entry toggles (Debug "Switches" block reuses them); a lookup + reference compare per call, no allocation
        static readonly Dictionary<ConfigEntryBase, string> _labels = new Dictionary<ConfigEntryBase, string>();
        internal string PageLabel(ConfigEntryBase e) { string s; return e != null && _labels.TryGetValue(e, out s) ? s : null; }

        public void Toggle(ConfigEntry<bool> e, string label = null)
        {
            if (label != null && e != null) { string old; if (!_labels.TryGetValue(e, out old) || !ReferenceEquals(old, label)) _labels[e] = label; }
            if (!BeginEntry(e)) return;
            bool v = Toggle(label ?? e.Definition.Key, e.Value);
            if (v != e.Value) e.Value = v;
            _insideEntry = false;
        }

        public int Choices(string label, int selected, params string[] choices)
        {
            if (!ShowRow(label)) return selected;
            if (Ledger.SettingsActive && choices.Length == 2 && choices[0] == "Off" && choices[1] == "On") return Toggle(label, selected == 1) ? 1 : 0;
            var r = Next(28);
            float fraction = Ledger.SettingsActive ? LabelWidth : .34f;
            Caption(r, label, fraction);
            float x = r.x + r.width * fraction, w = (r.width * (1 - fraction) - (choices.Length - 1) * 6) / choices.Length;
            for (int i = 0; i < choices.Length; i++)
            {
                var option = new Rect(x + i * (w + 6), r.y, w, r.height);
                if (Ledger.SettingsActive ? Ledger.Choice(option, choices[i], i == selected) : Ledger.Button(option, choices[i], true, i == selected)) selected = i;
            }
            return selected;
        }

        public float Slider(string label, string key, float value, float min, float max)
        {
            if (!ShowRow(label) && !ShowRow(key)) return value;
            var r = Next(26);
            Caption(r, label);
            float x = r.x + r.width * LabelWidth, w = r.width * (1 - LabelWidth);
            float s = Ledger.SettingsActive ? Ledger.Slider(new Rect(x, r.center.y - 10, w - 120, 20), value, min, max)
                : GUI.HorizontalSlider(new Rect(x, r.y + 9, w - 96, 12), value, min, max);
            if (s != value) { Ledger.ClearNumbers(); value = s; }
            value = Ledger.Number(new Rect(x + w - (Ledger.SettingsActive ? 112 : 88), r.y, Ledger.SettingsActive ? 112 : 88, r.height), Prefix + key, value, min, max, () => { }, true);
            return value;
        }

        /// <summary>Two rebindable keys on one row: label, key button, second label, second key button.</summary>
        public void KeyPair(string label, ConfigEntryBase a, string label2, ConfigEntryBase b)
        {
            if (!ShowRow(label) && !ShowRow(label2)) return;
            var r = Next(28);
            Caption(r, label, .34f);
            float x = r.x + r.width * 0.34f, w = r.width * 0.66f, bw = (w - 6) * 0.3f, lw = (w - 6) * 0.4f;
            KeyButton(new Rect(x, r.y, bw, r.height), a);
            Ledger.Label(new Rect(x + bw + 6, r.y, lw, r.height), label2, Ledger.Body, Ledger.Text, TextAnchor.MiddleCenter);
            KeyButton(new Rect(x + bw + 6 + lw, r.y, w - bw - 6 - lw, r.height), b);
        }

        static void KeyButton(Rect r, ConfigEntryBase e)
        {
            if (e == null) return;
            bool waiting = ReferenceEquals(_rebinding, e);
            if (Ledger.KeyButton(r, waiting ? "press a key" : e.BoxedValue.ToString())) _rebinding = waiting ? null : e;
        }

        /// <summary>Generic editor row for any BepInEx entry (bool, numbers with/without range, enum, Key, string, Vector3...).</summary>
        public void Entry(ConfigEntryBase e)
        {
            if (!BeginEntry(e)) return;
            string label = e.Definition.Key;
            var t = e.SettingType;
            if (t == typeof(bool))
            {
                bool v = (bool)e.BoxedValue;
                bool n = Toggle(label, v);
                if (n != v) e.BoxedValue = n;
            }
            else if (t == typeof(float) || t == typeof(int) || t == typeof(double))
            {
                float min, max; bool ranged = Range(e, out min, out max);
                float v = Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture);
                float n;
                if (ranged) n = Slider(label, e.Definition.Section + "/" + label, v, min, max);
                else
                {
                    var r = Next(26);
                    Caption(r, label);
                    n = Ledger.Number(new Rect(r.x + r.width * LabelWidth, r.y, 140, r.height), Prefix + e.Definition.Section + "/" + label, v, float.MinValue, float.MaxValue, () => { });
                }
                if (t == typeof(int)) n = Mathf.Round(n);
                if (Math.Abs(n - v) > 1e-6f)
                {
                    if (t == typeof(int)) e.BoxedValue = (int)n;
                    else if (t == typeof(double)) e.BoxedValue = (double)n;
                    else e.BoxedValue = n;
                }
            }
            else if (t == typeof(Key) || t == typeof(KeyCode))
            {
                var r = Next(26);
                Caption(r, label);
                bool waiting = ReferenceEquals(_rebinding, e);
                if (Ledger.KeyButton(new Rect(r.x + r.width * LabelWidth, r.y, Ledger.SettingsActive ? r.width * (1 - LabelWidth) : 200, r.height), waiting ? "press a key (Esc = none)" : e.BoxedValue.ToString())) _rebinding = waiting ? null : e;
            }
            else if (t.IsEnum || ListValues(e) != null)
            {
                var r = Next(26);
                Caption(r, label);
                object[] values = t.IsEnum ? ToObjects(Enum.GetValues(t)) : ListValues(e);
                int i = Array.IndexOf(values, e.BoxedValue);
                float x = r.x + r.width * LabelWidth;
                float arrow = Ledger.SettingsActive ? 44 : 30, valueWidth = Ledger.SettingsActive ? r.width * (1 - LabelWidth) - 100 : 200;
                if (Ledger.Button(new Rect(x, r.y, arrow, r.height), "<")) e.BoxedValue = values[(i - 1 + values.Length) % values.Length];
                Ledger.Label(new Rect(x + arrow + 6, r.y, valueWidth, r.height), e.BoxedValue.ToString(), Ledger.Body, Ledger.Text, TextAnchor.MiddleCenter);
                if (Ledger.Button(new Rect(x + arrow + valueWidth + 12, r.y, arrow, r.height), ">")) e.BoxedValue = values[(i + 1) % values.Length];
            }
            else
            {
                var r = Next(26);
                Caption(r, label);
                string key = Prefix + e.Definition.Section + "/" + label;
                string cur; if (!_text.TryGetValue(key, out cur)) cur = e.GetSerializedValue();
                var rr = new Rect(r.x + r.width * LabelWidth, r.y, r.width * (1 - LabelWidth) - 70, r.height);
                string next = Ledger.TextField(rr, key, cur);
                if (next != cur) _text[key] = next;
                string pending; bool dirty = _text.TryGetValue(key, out pending) && pending != e.GetSerializedValue();
                if (Ledger.Button(new Rect(rr.xMax + 6, r.y, 60, r.height), "SET", dirty))
                {
                    try { e.SetSerializedValue(pending); _text.Remove(key); } catch (Exception ex) { Log.Warn("F7: " + label + ": " + ex.Message); }
                }
            }
            var desc = e.Description != null ? e.Description.Description : null;
            if (!string.IsNullOrEmpty(desc) && desc.Length < 400) { Y -= 2; Text(desc, new Color(Ledger.Muted.r, Ledger.Muted.g, Ledger.Muted.b, 0.75f)); }
            _insideEntry = false;
        }

        static readonly Dictionary<string, string> _text = new Dictionary<string, string>();

        static object[] ToObjects(Array a) { var o = new object[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = a.GetValue(i); return o; }

        static object[] ListValues(ConfigEntryBase e)
        {
            var av = e.Description != null ? e.Description.AcceptableValues : null;
            if (av == null) return null;
            var p = av.GetType().GetProperty("AcceptableValues");
            if (p == null) return null;
            var arr = p.GetValue(av, null) as Array;
            return arr != null ? ToObjects(arr) : null;
        }

        static bool Range(ConfigEntryBase e, out float min, out float max)
        {
            min = max = 0;
            var av = e.Description != null ? e.Description.AcceptableValues : null;
            if (av == null) return false;
            var t = av.GetType();
            var pmin = t.GetProperty("MinValue"); var pmax = t.GetProperty("MaxValue");
            if (pmin == null || pmax == null) return false;
            min = Convert.ToSingle(pmin.GetValue(av, null), CultureInfo.InvariantCulture);
            max = Convert.ToSingle(pmax.GetValue(av, null), CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Key capture for rebinding rows; called by the window each frame.</summary>
        internal static void PollRebind()
        {
            if (_rebinding == null) return;
            var kb = Keyboard.current; if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame)
            {
                if (_rebinding.SettingType == typeof(Key)) _rebinding.BoxedValue = Key.None; else _rebinding.BoxedValue = KeyCode.None;
                _rebinding = null; return;
            }
            foreach (var kc in kb.allKeys)
            {
                if (kc == null || !kc.wasPressedThisFrame) continue;
                if (_rebinding.SettingType == typeof(Key)) _rebinding.BoxedValue = kc.keyCode;
                else
                {
                    try { _rebinding.SetSerializedValue(KeyCodeName(kc.keyCode)); } catch { }
                }
                _rebinding = null; return;
            }
        }
        internal static bool Rebinding { get { return _rebinding != null; } }
        internal static void CancelRebind() { _rebinding = null; }

        static string KeyCodeName(Key k)
        {
            string n = k.ToString();
            if (n.StartsWith("Digit")) return "Alpha" + n.Substring(5);
            if (n.StartsWith("Numpad")) return "Keypad" + n.Substring(6);
            if (n == "Enter") return "Return";
            if (n == "LeftCtrl") return "LeftControl";
            if (n == "RightCtrl") return "RightControl";
            return n;
        }
    }
}
