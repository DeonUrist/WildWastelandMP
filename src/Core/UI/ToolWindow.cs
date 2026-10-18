using System;
using System.Collections.Generic;
using UnityEngine;
using CleanHud = WildWasteland.Modules.CleanHUD;

namespace WildWasteland.Core.UI
{
    /// <summary>
    /// The F7 tool window (key rebindable in F6 [Keys] "Tool window"): every debug, editor and technical setting of all
    /// modules in one place, grouped by type, drawn with the Apocapatrol ledger skin. Pauses the game while open
    /// (shared <see cref="EditorSession"/>, so only one editor/tool is open at a time). Modules add pages with <see cref="Add"/>
    /// (or return them from IModule.ToolPages); tools that open their own window (e.g. the patrol ledger) close this one first.
    /// </summary>
    public static class ToolWindow
    {
        static readonly List<ToolPage> _pages = new List<ToolPage>();
        static readonly EditorSession _session = new EditorSession();
        static ToolPage _current;
        static Vector2 _navScroll, _scroll;
        static float _contentHeight = 400, _navHeight = 400;
        static string _debugSearch = "", _developerSearch = "";
        static Rect _rect;
        const float Width = 1920, Height = 1080;
        static readonly Action<Rect, string, float, Color, TextAnchor> SettingsText = CleanHud.GlyphFont.Draw;
        static readonly Action<Rect, string, float, Color, TextAnchor> SettingsWrap = CleanHud.GlyphFont.DrawWrapped;
        static readonly Func<string, float, float, float> SettingsHeight = CleanHud.GlyphFont.WrappedHeight;
        static bool _open;
        static bool _fromMenu;
        static Action _deferred;
        public static bool IsOpen { get { return _open; } }
        /// <summary>The window is open or has a deferred action to run: the GUI runner must stay on.</summary>
        internal static bool NeedsGui { get { return _open || _deferred != null; } }

        internal static void Init()
        {
            Hotkeys.Register(null, Plugin.ToolWindowKey, Toggle, null, true);
            CorePages.Register();
            SaveBus.StateChanged += OnSaveState;
        }

        static bool _newGame, _openNewGame;
        static void OnSaveState(string previous, string current)
        {
            if (current == "LoadGame" || current == "generateTerrain 2") { _newGame = _openNewGame = false; }
            else if (current == "setSeed" || current == "generateTerrain") _newGame = true;
            else if (current == "isPlay" && _newGame) { _newGame = false; _openNewGame = !Multiplayer.Client && !MultiplayerHosting.Pending; }
        }
        internal static void Tick()
        {
            if (LoadingProgress.MenusBlocked) { if (_open) Close(); return; }
            if (_open && !_session.Open) Close();
            if (!_openNewGame || !GameRefs.InGame || EditorSession.Busy || ApocasetterBridge.InputBlocked) return;
            Open(CorePages.GamePage);
            if (_open) { _openNewGame = false; Log.Always("[Core] New game ready: opened Game settings"); }
        }

        public static void Add(ToolPage p) { if (p != null && !_pages.Contains(p)) _pages.Add(p); }
        public static void Remove(ToolPage p) { _pages.Remove(p); if (_current == p) _current = null; }

        public static void Toggle() { if (_open) Close(); else Open(null); }

        public static void Open(ToolPage page)
        { Open(page, GameRefs.Menu != null && GameRefs.Menu.ActiveStateName == "pause"); }

        internal static void OpenFromMenu() { Open(CorePages.GamePage, true); }
        internal static void OpenHostSetup(bool steam) { Open(new HostSettingsPage(steam), true); }

        static void Open(ToolPage page, bool fromMenu)
        {
            if (LoadingProgress.MenusBlocked) return;
            if (!_open)
            {
                if (EditorSession.Busy || !_session.Acquire(true, fromMenu)) { Log.Info("Settings: another editor is open or the game is paused"); return; }
                _open = true;
                _fromMenu = fromMenu;
                CollectModulePages();
                Log.Always("[Core] Settings opened: CleanHUD atlas UI, 54-unit rows");
            }
            if (page != null) { _current = page; _scroll = Vector2.zero; Ledger.ClearNumbers(); ToolLayout.CancelRebind(); }
            if (_current == null && _pages.Count > 0) _current = Sorted()[0];
            if (_current != null) _current.OnShow();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false; _session.Close(); Ledger.ClearNumbers(); ToolLayout.CancelRebind();
            if (_current is HostSettingsPage) { _current = CorePages.GamePage; _scroll = Vector2.zero; }
        }

        /// <summary>Close the window and run <paramref name="a"/> on the next frame (after the session was released), e.g. to open another editor.</summary>
        public static void CloseThen(Action a) { Close(); _deferred = a; }

        static bool _collected;
        static void CollectModulePages()
        {
            if (_collected) return;
            _collected = true;
            foreach (var e in ModuleHost.Entries)
            {
                IEnumerable<ToolPage> pages = null;
                try { pages = e.Module.ToolPages(); } catch (Exception ex) { Log.Error("[" + e.Module.Id + "] ToolPages", ex); }
                if (pages == null) continue;
                foreach (var p in pages) { if (p.Owner == null) p.Owner = e.Module; CorePages.AddDebugPage(p); }
            }
        }

        static List<ToolPage> Sorted()
        {
            var l = new List<ToolPage>();
            foreach (var p in _pages) if (Visible(p)) l.Add(p);
            l.Sort((a, b) =>
            {
                int ga = GroupIndex(a.Group), gb = GroupIndex(b.Group);
                if (ga != gb) return ga.CompareTo(gb);
                return _pages.IndexOf(a).CompareTo(_pages.IndexOf(b));
            });
            return l;
        }
        static bool Visible(ToolPage p)
        {
            if (Multiplayer.Client && (p == CorePages.WorldPage || p.DebugOnly || p == WildWasteland.Modules.Performance.PerformancePage.Instance)) return false;
            return (!p.DebugOnly || Plugin.EnableDebug.Value) && (p != CorePages.DeveloperPage || Plugin.DeveloperMode.Value);
        }
        static int GroupIndex(string g) { int i = Array.IndexOf(ToolGroups.Order, g); return i < 0 ? ToolGroups.Order.Length : i; }

        /// <summary>Called from the Runner's OnGUI.</summary>
        internal static void Draw()
        {
            if (_deferred != null && !EditorSession.Busy && Event.current.type == EventType.Repaint)
            {
                var d = _deferred; _deferred = null;
                try { d(); } catch (Exception e) { Log.Error("F7 deferred action", e); }
            }
            if (!_open) return;
            if (_current != null && !Visible(_current))
            { _current = !Multiplayer.Client && Plugin.EnableDebug.Value ? CorePages.DebugPage : CorePages.GamePage; _scroll = Vector2.zero; Ledger.ClearNumbers(); ToolLayout.CancelRebind(); _current.OnShow(); }
            if (Event.current.type == EventType.Layout)
            {
                _session.Maintain();
                ToolLayout.PollRebind();
                if (!ToolLayout.Rebinding && Event.current != null && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
                if (_current != null) { try { _current.Update(); } catch (Exception e) { Log.Error("F7 page " + _current.Title, e); } }
            }
            if (Skin.GameFont == null) { var f = ApocasetterBridge.GameFont; if (f != null) Skin.SetGameFont(f); }
            int depth = GUI.depth; GUI.depth = -900;
            float s = Mathf.Min(Screen.width / Width, Screen.height / Height);
            var accent = CleanHud.CleanHudModule.Running ? CleanHud.AccentTheme.Resolve(CleanHud.DialogueWidgets.Gold) : CleanHud.DialogueWidgets.Gold;
            var border = new Color(214 / 255f, 203 / 255f, 176 / 255f, .55f);
            if (CleanHud.CleanHudModule.Running) border = CleanHud.AccentTheme.Resolve(border);
            var old = Ledger.BeginSettings(accent, border, CleanHud.Assets.Tex("panel_menu"), CleanHud.Assets.VGradient(.75f), SettingsText, SettingsWrap, SettingsHeight); var m = GUI.matrix;
            if (_fromMenu && CleanHud.CleanHudModule.Running && CleanHud.CleanHudModule.MenuEnabled.Value)
            {
                var background = CleanHud.Menu.MenuBackground();
                if (background != null) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), background, ScaleMode.ScaleAndCrop);
            }
            Ledger.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, .6f));
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - Width * s) / 2, (Screen.height - Height * s) / 2, 0), Quaternion.identity, new Vector3(s, s, 1));
            _rect = new Rect(0, 0, Width, Height);
            GUI.BeginGroup(_rect);
            try { DrawWindow(); }
            finally { GUI.EndGroup(); Ledger.EndSettings(); GUI.skin = old; GUI.matrix = m; GUI.depth = depth; }
        }

        static readonly ToolLayout _layout = new ToolLayout();

        static void DrawWindow()
        {
            var hostSetup = _current as HostSettingsPage;
            if (hostSetup != null) hostSetup.Before();
            Ledger.Label(new Rect(120, 120, 1420, 104), "WILD WASTELAND", Ledger.Brand, Ledger.Accent);
            Ledger.Label(new Rect(126, 228, 900, 36), "CONFIGURATION  /  " + Plugin.VERSION, Ledger.Small, Ledger.Muted);
            Ledger.Window(new Rect(120, 280, 1680, 704));
            if (hostSetup != null) hostSetup.DrawActions(new Rect(150, 900, 1604, 64));
            else if (Ledger.Button(new Rect(128, 900, 290, 64), "BACK", true, false, true, null, true)) { Close(); return; }
            Ledger.Label(new Rect(126, 1012, 600, 44), "CHANGES APPLY IMMEDIATELY", Ledger.Small, Ledger.Muted);
            Ledger.Label(new Rect(1340, 1012, 460, 44), Plugin.ToolWindowKey.Value + " / ESC TO CLOSE", Ledger.Small, Ledger.Muted, TextAnchor.MiddleRight);
            if (hostSetup == null && CleanHud.CleanHudModule.Running && CleanHud.AccentTheme.Setting != null)
                for (int i = 0; i < CleanHud.AccentTheme.DisplayOrder.Length; i++)
                {
                    int choice = CleanHud.AccentTheme.DisplayOrder[i];
                    var swatch = new Rect(_rect.width / 2 - 156 + i * 40, 1020, 30, 30);
                    bool selected = choice == CleanHud.AccentTheme.Index || swatch.Contains(Event.current.mousePosition);
                    Ledger.Panel(swatch, Ledger.Field, selected ? Ledger.Accent : Ledger.Line);
                    Ledger.Fill(new Rect(swatch.x + 4, swatch.y + 4, 22, 22), CleanHud.AccentTheme.Colors[choice]);
                    if (GUI.Button(swatch, GUIContent.none, GUIStyle.none)) CleanHud.AccentTheme.Setting.Value = CleanHud.AccentTheme.Names[choice];
                }

            if (hostSetup == null)
            {
            var pages = Sorted();
            // ---- left: groups and pages
            var nav = new Rect(128, 304, 290, 572);
            _navScroll = Ledger.Scroll(nav, _navScroll, _navHeight, w =>
            {
                float y = 0; string group = null;
                foreach (var p in pages)
                {
                    if (p.Group != group)
                    {
                        group = p.Group;
                        if (group != ToolGroups.General)
                        {
                            y += 18; Ledger.Label(new Rect(22, y, w - 44, 30), group, Ledger.Small, Ledger.Muted); y += 38;
                        }
                    }
                    bool off = p.Owner != null && !ModuleHost.IsRunning(p.Owner);
                    var r = new Rect(0, y, w, 60);
                    bool highlighted = p == _current || r.Contains(Event.current.mousePosition);
                    Ledger.RowHighlight(r, p == _current);
                    Ledger.Label(new Rect(r.x + 22, r.y, r.width - 30, r.height), p.Title + (off ? "  (OFF)" : ""), Ledger.Heading, off ? Ledger.Muted : highlighted ? Ledger.Accent : Ledger.Text);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none) && p != _current) { _current = p; _scroll = Vector2.zero; Ledger.ClearNumbers(); ToolLayout.CancelRebind(); p.OnShow(); }
                    y += 60;
                }
                _navHeight = y + 6;
            });
            }

            // ---- right: page content
            var body = hostSetup != null ? new Rect(128, 280, 1648, 604) : new Rect(460, 280, 1316, 704);
            if (hostSetup == null) Ledger.Fill(new Rect(438, 304, 1.5f, 656), new Color(Ledger.Accent.r, Ledger.Accent.g, Ledger.Accent.b, .35f));
            if (_current == null) return;
            Ledger.Label(new Rect(body.x + 22, body.y + 22, body.width - 44, 50), _current.Title, Ledger.DialogTitle, Ledger.Accent);
            if ((_current == CorePages.GamePage || _current == CorePages.WorldPage || _current == CorePages.HudPage) && Ledger.Button(new Rect(body.xMax - 182, body.y + 22, 160, 50), "RESET"))
            {
                CorePages.ResetSettings();
                _debugSearch = _developerSearch = ""; _scroll = Vector2.zero;
            }
            bool searchable = _current == CorePages.DebugPage || _current == CorePages.DeveloperPage;
            string search = _current == CorePages.DeveloperPage ? _developerSearch : _debugSearch;
            if (searchable)
            {
                Ledger.Label(new Rect(body.xMax - 642, body.y + 22, 122, 50), "SEARCH", Ledger.Small, Ledger.Muted);
                var field = new Rect(body.xMax - 510, body.y + 22, 410, 50);
                Ledger.Panel(field, Ledger.Field, Ledger.Line);
                string next = Ledger.TextField(field, _current.Title + "/Search", search);
                if (Ledger.Button(new Rect(body.xMax - 90, field.y, 68, field.height), "X", search.Length > 0)) { next = ""; GUI.FocusControl(null); }
                if (next != search)
                {
                    if (_current == CorePages.DeveloperPage) _developerSearch = next; else _debugSearch = next;
                    search = next; _scroll = Vector2.zero; Ledger.ClearNumbers(); ToolLayout.CancelRebind();
                }
            }
            var inner = new Rect(body.x, body.y + 92, body.width, body.height - 116);
            var page = _current;
            _scroll = Ledger.Scroll(inner, _scroll, _contentHeight, w =>
            {
                _layout.Begin(w, page.Title + "/", searchable ? search : null);
                bool off = page.Owner != null && !ModuleHost.IsRunning(page.Owner);
                if (off)
                {
                    var e = ModuleHost.Of(page.Owner);
                    _layout.Text(page.Owner.DisplayName + " is not running (" + (e != null ? e.State.ToString() : "?") + "). Settings can still be edited; live tools need the module on (F6 or the Modules page).", Ledger.Danger);
                }
                bool wasEnabled = GUI.enabled;
                if (off && !page.ShowWhenModuleOff && !(page is ConfigPage)) GUI.enabled = false;
                try { page.Draw(_layout); }
                catch (Exception ex) { _layout.Text("Page error: " + ex.Message, Ledger.Danger); Log.Error("F7 page " + page.Title, ex); }
                GUI.enabled = wasEnabled;
                if (_layout.Searching && !_layout.SearchResults)
                {
                    Ledger.Label(new Rect(22, 4, w - 44, 54), "No matching controls.", Ledger.Body, Ledger.Muted);
                    _layout.Y = 62;
                }
                _contentHeight = _layout.Y + 12;
            });
            if (hostSetup != null) hostSetup.DrawPopup();
        }
    }
}
