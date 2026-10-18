using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WildWasteland.Modules.CleanHUD
{
    // Row / tab / setting control: a Button whose look is driven by code (gold text + bar when hovered or selected).
    public sealed class MenuButton : Button
    {
        internal Action<bool> OnVisual;
        internal Action<int> OnHorizontal;
        internal bool Sticky;   // keep the highlighted look (current tab)
        private bool _hover;

        // Highlight follows the mouse only (no keyboard / selection highlight).
        public override void OnPointerEnter(PointerEventData e) { base.OnPointerEnter(e); _hover = true; Refresh(); }
        public override void OnPointerExit(PointerEventData e) { base.OnPointerExit(e); _hover = false; Refresh(); }
        protected override void OnDisable() { base.OnDisable(); _hover = false; }
        internal void Refresh() { if (OnVisual != null) OnVisual((_hover && IsInteractable()) || Sticky); }

        public override void OnMove(AxisEventData e)
        {
            if (OnHorizontal != null && (e.moveDir == MoveDirection.Left || e.moveDir == MoveDirection.Right))
            { OnHorizontal(e.moveDir == MoveDirection.Right ? 1 : -1); e.Use(); return; }
            base.OnMove(e);
        }
    }

    // Click / drag on a bar sets a 0..1 value.
    public sealed class BarDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        internal Action<float> OnValue;
        public void OnPointerDown(PointerEventData e) { Apply(e); }
        public void OnDrag(PointerEventData e) { Apply(e); }
        private void Apply(PointerEventData e)
        {
            var rt = (RectTransform)transform; Vector2 lp;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out lp)) return;
            var r = rt.rect;
            if (OnValue != null) OnValue(Mathf.Clamp01((lp.x - r.xMin) / Mathf.Max(1f, r.width)));
        }
    }

    public sealed class MenuScrollRect : ScrollRect
    {
        public override void OnBeginDrag(PointerEventData e) { }
        public override void OnDrag(PointerEventData e) { }
        public override void OnEndDrag(PointerEventData e) { }
    }

    // Arrow inside a row: handles its own down/click so the row button does not take the press.
    public sealed class ClickZone : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        internal Action OnClick;
        public void OnPointerDown(PointerEventData e) { }
        public void OnPointerClick(PointerEventData e) { if (OnClick != null) OnClick(); }
    }

    // ------------------------------------------------------------------ settings model
    internal sealed class SettingDef
    {
        internal string Tab, Label, Key, Kind, Path, Fmt;
        internal string FsmName, OnState;      // kind "fsm": toggles an FSM between two states (god mode, god hands)
        internal float Min, Max, Def;
        internal bool TitleOnly, GameOnly;
    }

    internal static class SettingsModel
    {
        internal static List<string> Tabs()
        {
            var l = new List<string> { "GRAPHICS", "AUDIO", "GAMEPLAY", "PLAYER & CAR", "CONTROLS" };
            if (CleanHudModule.EnableCheats.Value) l.Add("CHEATS");
            return l;
        }
        internal static readonly List<SettingDef> Defs = new List<SettingDef>();
        static SettingsModel()
        {
            // Graphics: display options (Settings.xml, applied with APPLY) + the game's SaveSettings.es3 keys (defaults = the game's UI defaults)
            D("GRAPHICS", "RESOLUTION", null, "res");
            D("GRAPHICS", "SCREEN MODE", null, "mode");
            D("GRAPHICS", "QUALITY", null, "quality");
            F("GRAPHICS", "FIELD OF VIEW", "FOV", 40, 120, 75, "Graphics_Canvas/Graphics/fov/FOV", "0");
            B("GRAPHICS", "AMBIENT OCCLUSION", "ambient_occlusion", true, "Graphics_Canvas/Graphics/ambient occlusion/ambient_occlusion");
            B("GRAPHICS", "BLOOM", "bloom", true, "Graphics_Canvas/Graphics/bloom/bloom");
            B("GRAPHICS", "COLOR GRADING", "color_grading", false, "Graphics_Canvas/Graphics/color grading/color_grading");
            B("GRAPHICS", "SCREEN SPACE SHADOWS", "screen_space_shadows", true, "Graphics_Canvas/Graphics/screen space shadows/screen_space_shadows");
            B("GRAPHICS", "SHARPEN", "sharpen", true, "Graphics_Canvas/Graphics/sharpen/sharpen");
            B("GRAPHICS", "SHOW FPS", "toggleFPS", false, "Graphics_Canvas/Graphics/show fps/show_fps");
            F("AUDIO", "MASTER VOLUME", "audio_volume", 0, 1, .3f, "Audio_Canvas/Audio/audio volume/audio_volume", "%");
            F("AUDIO", "AMBIENT MUSIC", "ambient_music", 0, 1, .3f, "Audio_Canvas/Audio/ambient music/ambient_music", "%");
            F("AUDIO", "AMBIENT SOUNDS", "ambient_volume", 0, 1, .15f, "Audio_Canvas/Audio/ambient volume/ambient_volume", "%");
            F("AUDIO", "BOSS FIGHT MUSIC", "boss_music", 0, 1, .3f, "Audio_Canvas/Audio/boss music/boss_music", "%");
            F("AUDIO", "ENGINE SOUND", "enginesoundvolume", 0, 1, .3f, "Audio_Canvas/Audio/engine sound volume/enginesoundvolume", "%");
            B("AUDIO", "FOOTSTEP SOUNDS", "toggleFootstepSounds", true, "Audio_Canvas/Audio/footstep sounds/footstep_sounds");
            var mm = F("AUDIO", "MENU MUSIC", "main_menu_music", 0, 1, .3f, "Canvas_MM/MainMenu/Music", "%"); mm.TitleOnly = true;
            B("GAMEPLAY", "DISABLE FATIGUE", "disableFatigue", false, "Gameplay_Canvas/Gameplay/disable fatigue/disable_fatigue");
            B("GAMEPLAY", "DISABLE THIRST / HUNGER", "disableThirstHunger", false, "Gameplay_Canvas/Gameplay/disable thirst hunger/disable_thirst_hunger");
            B("GAMEPLAY", "DISABLE INSANITY", "disableInsanity", false, "Gameplay_Canvas/Gameplay/disable insanity/disable_insanity");
            B("GAMEPLAY", "DISABLE SANDSTORM", "disableSandstorm", false, "Gameplay_Canvas/Gameplay/disable sandstorm/disable_sandstorm");
            F("PLAYER & CAR", "MOUSE SENSITIVITY X", "mouse_sensy_x", -10, 10, 2, "PlayerCar_Canvas/PlayerCar/mouse sensy x/mouse_sensy_x", "0.0");
            F("PLAYER & CAR", "MOUSE SENSITIVITY Y", "mouse_sensy_y", -10, 10, 2, "PlayerCar_Canvas/PlayerCar/mouse sensy y/mouse_sensy_y", "0.0");
            F("PLAYER & CAR", "ITEM ROTATE SPEED", "itemRotateSpeed", -10, 10, 2, "PlayerCar_Canvas/PlayerCar/item rotate/item_rotate", "0.0");
            B("PLAYER & CAR", "TOGGLE AIM DOWN SIGHTS", "toggleADS", false, "PlayerCar_Canvas/PlayerCar/aim down sights/aim_down_sights");
            B("PLAYER & CAR", "HEAD BOB", "toggleHeadbob", true, "PlayerCar_Canvas/PlayerCar/headbob/headbob");
            F("PLAYER & CAR", "STEERING SPEED", "steeringspeed", 10, 300, 50, "PlayerCar_Canvas/PlayerCar/steeringspeed/steeringspeed", "0");
            B("PLAYER & CAR", "AUTOMATIC GEARBOX", "automatic_gearbox", true, "PlayerCar_Canvas/PlayerCar/automatic gearbox/automatic_gearbox");
            B("PLAYER & CAR", "AUTO CLUTCH", "autoclutch", true, "PlayerCar_Canvas/PlayerCar/autoclutch/autoclutch");
            B("PLAYER & CAR", "H-SHIFTER", "hshifter", false, "PlayerCar_Canvas/PlayerCar/hshifter/hshifter");
            B("PLAYER & CAR", "SMOOTH INPUT", "smoothinput", true, "PlayerCar_Canvas/PlayerCar/smoothinput/smoothinput");
            B("PLAYER & CAR", "NORMALIZE INPUT", "normalizeinput", false, "PlayerCar_Canvas/PlayerCar/normalizeinput/normalizeinput");
            // CHEATS (shown with [Menu] EnableCheats)
            B("CHEATS", "PEACEFUL MODE", "peacefulMode", false, "Gameplay_Canvas/Gameplay/peaceful mode/peaceful_mode");
            B("CHEATS", "MAP CHEAT (WHOLE MAP)", "mapCheat", false, "Gameplay_Canvas/Gameplay/map cheat/map_cheat");
            B("CHEATS", "PERMA BASE (HIDDEN GAME OPTION)", "permaBase", false, "Gameplay_Canvas/Gameplay/perma base/perma_base");
            var gm = D("CHEATS", "GOD MODE: FLY, NO CLIP (F11)", null, "fsm"); gm.Path = "Player"; gm.FsmName = "GODMODE"; gm.OnState = "active"; gm.GameOnly = true;
            var gh = D("CHEATS", "GOD HANDS (F10)", null, "fsm"); gh.Path = "PlayerCameraHolder/PlayerCamera"; gh.FsmName = "GodHands"; gh.OnState = "on"; gh.GameOnly = true;
            var dbg = D("CHEATS", "DEBUG SCREEN", null, "screen"); dbg.Path = "MainMenu_Canvas/MainMenu/Debug"; dbg.Key = "Debug_Canvas"; dbg.GameOnly = true;
        }
        private static SettingDef D(string tab, string label, string key, string kind)
        { var d = new SettingDef { Tab = tab, Label = label, Key = key, Kind = kind }; Defs.Add(d); return d; }
        private static SettingDef B(string tab, string label, string key, bool def, string path)
        { var d = D(tab, label, key, "bool"); d.Def = def ? 1 : 0; d.Path = path; return d; }
        private static SettingDef F(string tab, string label, string key, float min, float max, float def, string path, string fmt)
        { var d = D(tab, label, key, "float"); d.Min = min; d.Max = max; d.Def = def; d.Path = path; d.Fmt = fmt; return d; }

        // ---- storage: the game's own UI control when it exists (in game it applies the change at once), plus SaveSettings.es3
        private static ES3Settings FileSet() { return new ES3Settings("SaveSettings.es3", ES3.Location.File); }
        private static ES3Settings CacheSet() { return new ES3Settings("SaveSettings.es3", ES3.Location.Cache); }

        internal static Transform FindPath(string path) { return SceneCache.FindPath(path); }

        private static PlayMakerFSM FsmOf(SettingDef d)
        {
            var t = FindPath(d.Path);
            if (t != null) foreach (var f in t.GetComponents<PlayMakerFSM>()) if (f.FsmName == d.FsmName) return f;
            return null;
        }

        internal static float Read(SettingDef d)
        {
            if (d.Key == "color_grading") return 0;
            if (d.Kind == "fsm") { var f = FsmOf(d); return f != null && f.ActiveStateName == d.OnState ? 1 : 0; }
            var t = FindPath(d.Path);
            if (t != null)
            {
                if (d.Kind == "bool") { var tg = t.GetComponent<Toggle>(); if (tg != null) return tg.isOn ? 1 : 0; }
                else { var sl = t.GetComponent<Slider>(); if (sl != null) return sl.value; }
            }
            try
            {
                var fs = FileSet();
                if (ES3.KeyExists(d.Key, fs))
                    return d.Kind == "bool" ? (ES3.Load<bool>(d.Key, fs) ? 1 : 0) : ES3.Load<float>(d.Key, fs);
            }
            catch (Exception e) { CleanHudModule.Warn("Settings read " + d.Key + ": " + e.Message); }
            return d.Def;
        }

        internal static void Write(SettingDef d, float v)
        {
            if (d.Key == "color_grading") v = 0;
            if (d.Kind == "fsm")
            {
                var f = FsmOf(d);
                if (f == null || !f.isActiveAndEnabled) return;
                bool on = f.ActiveStateName == d.OnState;
                if (on != v > .5f) f.SendEvent(on ? "back" : "next");   // the same transitions the F-key fires
                return;
            }
            var t = FindPath(d.Path);
            if (t != null)
            {
                if (d.Kind == "bool") { var tg = t.GetComponent<Toggle>(); if (tg != null && tg.isOn != (v > .5f)) tg.isOn = v > .5f; }
                else { var sl = t.GetComponent<Slider>(); if (sl != null) sl.value = v; }
            }
            try
            {
                if (d.Kind == "bool") { ES3.Save<bool>(d.Key, v > .5f, FileSet()); var cs = CacheSet(); if (ES3.FileExists(cs)) ES3.Save<bool>(d.Key, v > .5f, cs); }
                else { ES3.Save<float>(d.Key, v, FileSet()); var cs = CacheSet(); if (ES3.FileExists(cs)) ES3.Save<float>(d.Key, v, cs); }
            }
            catch (Exception e) { CleanHudModule.Warn("Settings write " + d.Key + ": " + e.Message); }
        }

        internal static string Format(SettingDef d, float v)
        {
            if (d.Kind == "bool" || d.Kind == "fsm") return v > .5f ? "ON" : "OFF";
            if (d.Fmt == "%") return Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            return v.ToString(d.Fmt ?? "0.0", CultureInfo.InvariantCulture);
        }

        internal static string ReadString(string key)
        {
            try { var fs = FileSet(); if (ES3.KeyExists(key, fs)) return ES3.Load<string>(key, fs); }
            catch { }
            return null;
        }
    }

    // Display options = the game's Settings.xml (screenWidth, screenHeight, screenMode, qualityLevel).
    internal sealed class Display
    {
        internal static readonly string[] ModeNames = { "FULLSCREEN", "BORDERLESS", "MAXIMIZED", "WINDOWED" };
        internal int W, H, Mode, Quality;
        internal readonly List<Vector2Int> Res = new List<Vector2Int>();
        private static string PathXml { get { return Path.Combine(Application.persistentDataPath, "Settings.xml"); } }

        internal static Display Load()
        {
            var d = new Display { W = Screen.width, H = Screen.height, Mode = (int)Screen.fullScreenMode, Quality = QualitySettings.GetQualityLevel() };
            try
            {
                if (File.Exists(PathXml))
                {
                    var x = File.ReadAllText(PathXml);
                    d.W = Tag(x, "screenWidth", d.W); d.H = Tag(x, "screenHeight", d.H); d.Mode = Tag(x, "screenMode", d.Mode); d.Quality = Tag(x, "qualityLevel", d.Quality);
                }
            }
            catch { }
            foreach (var r in Screen.resolutions) { var v = new Vector2Int(r.width, r.height); if (!d.Res.Contains(v)) d.Res.Add(v); }
            var cur = new Vector2Int(d.W, d.H);
            if (!d.Res.Contains(cur)) d.Res.Add(cur);
            d.Res.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            d.Mode = Mathf.Clamp(d.Mode, 0, 3);
            d.Quality = Mathf.Clamp(d.Quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            return d;
        }
        private static int Tag(string x, string tag, int def)
        {
            var m = Regex.Match(x, "<" + tag + ">\\s*(-?\\d+)\\s*</" + tag + ">");
            int v; return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }
        internal void Apply()
        {
            Screen.SetResolution(W, H, (FullScreenMode)Mode);
            QualitySettings.SetQualityLevel(Quality, true);
            Core.Log.Always("[CleanHUD] Display apply requested: " + ResText + " " + ModeNames[Mode] + ", quality " + QualityText);
            try
            {
                File.WriteAllText(PathXml, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Settings xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n" +
                    "  <screenWidth>" + W + "</screenWidth>\r\n  <screenHeight>" + H + "</screenHeight>\r\n  <screenMode>" + Mode + "</screenMode>\r\n  <qualityLevel>" + Quality + "</qualityLevel>\r\n</Settings>");
            }
            catch (Exception e) { CleanHudModule.Warn("Settings.xml not written: " + e.Message); }
        }
        internal string ResText { get { return W + "x" + H; } }
        internal string QualityText { get { var n = QualitySettings.names; return Quality >= 0 && Quality < n.Length ? n[Quality].ToUpperInvariant() : Quality.ToString(); } }
    }

    // ------------------------------------------------------------------ the menus
    // Title (level0 Canvas_MM) and pause (level1 MainMenu_Canvas) menus in the HUD style. The game's own buttons stay in place,
    // invisible (CanvasGroup alpha 0, no raycasts); every action here sends "Clicked" to the game's own button FSM.
    internal sealed partial class Menu
    {
        private enum Ctx { None, Title, Pause }
        private static readonly Color Gold = new Color(0xe2 / 255f, 0xbf / 255f, 0x6c / 255f, 1f);
        private static readonly Color White = new Color(0xf3 / 255f, 0xef / 255f, 0xe4 / 255f, 1f);
        private static readonly Color Dim = new Color(0xf3 / 255f, 0xef / 255f, 0xe4 / 255f, .5f);
        private static readonly Color BarBorder = new Color(214 / 255f, 203 / 255f, 176 / 255f, .55f);
        private static readonly Color Red = new Color(1f, .38f, .32f, 1f);

        internal static int BlockCancelFrame = -1;   // GetButtonDown("Cancel") is swallowed up to this frame (sub screen open)
        private static readonly bool UpdateChecksEnabled = false;
        private static bool _updateChecked;
        private static string _updateUrl;
        private MenuButton _updateButton;
        private MenuButton _discordButton, _githubButton;
        private MenuButton _mpHost, _mpSteam, _mpJoin, _mpDisconnect, _mpAddressButton, _mpPortButton, _mpNameButton;
        private GlyphText _mpStatus, _mpAddressText, _mpPortText, _mpNameText;
        private string _shownMPReason;
        private string _mpAddress, _mpPort, _mpMessage, _mpName;
        private int _mpEdit;
        private bool _mpReplace;

        internal static System.Collections.IEnumerator CheckUpdates()
        {
            if (!UpdateChecksEnabled || _updateChecked) yield break;
            using (var req = UnityEngine.Networking.UnityWebRequest.Get("https://api.github.com/repos/DeonUrist/WildWastelandMP/releases?per_page=100"))
            {
                req.timeout = 20;
                req.SetRequestHeader("User-Agent", "WildWastelandMP/" + Plugin.VERSION);
                req.SetRequestHeader("Cache-Control", "no-cache");
                yield return req.SendWebRequest();
                _updateChecked = true;
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) { CleanHudModule.Warn("WW MP update check: " + req.error); yield break; }
                try
                {
                    var releases = Json.Parse(req.downloadHandler.text) as List<object>;
                    if (releases == null) throw new FormatException("expected releases list");
                    var latest = new Version(Plugin.VERSION);
                    foreach (var item in releases)
                    {
                        var release = item as Dictionary<string, object>; Version version;
                        string tag = Json.Text(release, "tag_name", "");
                        if (Json.Bool(release, "draft", false) || !Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out version) || version <= latest) continue;
                        latest = version; _updateUrl = "https://github.com/DeonUrist/WildWastelandMP/releases/tag/" + Uri.EscapeDataString(tag);
                    }
                    CleanHudModule.Log("WW MP update check: installed " + Plugin.VERSION + (_updateUrl == null ? "; no newer release" : "; update available " + latest));
                }
                catch (Exception e) { _updateUrl = null; CleanHudModule.Warn("WW MP update check: " + e.Message); }
            }
        }

        private Ctx _ctx;
        private int _sceneHandle = -1;
        private Canvas _van;              // Canvas_MM or MainMenu_Canvas
        private GameObject _root;
        private RectTransform _rootRt;
        private RawImage _background;
        private int _backgroundWidth, _backgroundHeight;
        private readonly Dictionary<CanvasGroup, bool> _added = new Dictionary<CanvasGroup, bool>();
        private struct GroupState { internal float Alpha; internal bool Raycasts, Interactable; }
        private readonly Dictionary<CanvasGroup, GroupState> _original = new Dictionary<CanvasGroup, GroupState>();
        private readonly Dictionary<Transform, CanvasGroup> _hiddenGroups = new Dictionary<Transform, CanvasGroup>();
        private Button _modsButton;
        private Button _modsFallback;
        private float _nextFallbackScan;
        private Button _languageButton;
        private bool _languageShown;
        private readonly List<MenuButton> _accentButtons = new List<MenuButton>();
        private float _nextModsScan;
        private static readonly string[] TitleHidden = { "Canvas_MM/MainMenu", "Canvas_MM/Game Name", "Canvas_MM/EnterSeedText", "Canvas_MM/MusicText", "Canvas_MM/Version_text", "Canvas_MM/row_bg", "Canvas_MM/LoadingText" };
        private static readonly string[] PauseHidden = { "MainMenu_Canvas/MainMenu", "MainMenu_Canvas/row_bg" };
        private static readonly KeyCode[] Modifiers = { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt };
        private readonly List<Button> _buttonBuffer = new List<Button>();
        private readonly LoadingScreen _loadingScreen;
        internal Menu(LoadingScreen loadingScreen) { _loadingScreen = loadingScreen; }
        private string _screen = "main";
        private RectTransform _main, _sub, _modal, _status;
        private GlyphText _statusText;
        private Image _statusBar;
        private float _nextScan, _nextLabels;
        private readonly List<Action> _nextFrame = new List<Action>();
        private readonly List<Action> _later = new List<Action>();
        private bool _modsShown;
        private Canvas _vanillaWatch;     // a game screen we opened from our settings (Controls)
        private float _vanillaWatchSince;
        // title seed editing
        private bool _seedEdit;
        private string _seed = "";
        private GlyphText _seedText;
        private GlyphText _continueSub;
        private MenuButton _continueBtn;
        // settings
        private string _tab = "GRAPHICS";
        private Display _display;
        private bool _settingsDirty;
        // key bindings (CONTROLS)
        private sealed class KeyRow { internal KeyBind B; internal MenuButton Btn; internal int Sel; internal readonly List<GlyphText> Txt = new List<GlyphText>(); internal readonly List<Image[]> Frames = new List<Image[]>(); }
        private const float BoxW = 156, KeyRH = 48;
        private string _keyTab = "MOVEMENT";
        private List<KeyBind> _keys;
        private RectTransform _keyView, _keyContent, _keyThumb;
        private GlyphText _keyHint;
        private float _keyScrollY;
        private string _keySelect, _keyMsg;
        private bool _keyMsgBad;
        private GameObject _keyLastSel;
        private KeyRow _capRow;
        private int _capSlot;
        private float _capSince;
        private bool _capMouseUp;
        private GameObject _capBlock;
        private GlyphText _warnText;
        private bool _warnChecked;

        // ---------------------------------------------------------------- tick
        internal void Tick()
        {
            if (!CleanHudModule.Running) return;
            if (CleanHudModule.MenuEnabled.Value)
            {
                HideGo(SettingsModel.FindPath("Canvas/TutorialImage"));
                HideGo(SettingsModel.FindPath("MainMenu_Canvas/MainMenu/TutorialArrow"));
            }
            if (WildWasteland.Core.UI.EditorSession.Busy || WildWasteland.Core.ApocasetterBridge.InputBlocked)
            {
                if (_mpEdit != 0) { _mpEdit = 0; _mpMessage = null; }
                if (WildWasteland.Core.UI.EditorSession.Busy) BlockCancelFrame = Time.frameCount + 1;
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                return;
            }
            RunQueued();
            if (!CleanHudModule.MenuEnabled.Value) { Teardown(); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != _sceneHandle) { Teardown(); _sceneHandle = scene.handle; _nextScan = 0; }
            if (_loadingScreen.Active) { if (_root != null && _root.activeSelf) _root.SetActive(false); return; }
            if (_van == null && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + .5f;
                var t = SettingsModel.FindPath("Canvas_MM");
                if (t != null) { _van = t.GetComponent<Canvas>(); _ctx = Ctx.Title; }
                else { t = SettingsModel.FindPath("MainMenu_Canvas"); if (t != null) { _van = t.GetComponent<Canvas>(); _ctx = Ctx.Pause; } }
                if (_van != null) Build();
            }
            if (_van == null || _root == null) return;

            if (_vanillaWatch != null)
            {
                // our settings opened a game screen (Controls); come back when it closes
                if (_vanillaWatch.enabled || Time.unscaledTime - _vanillaWatchSince < .3f) { _root.SetActive(false); return; }
                _vanillaWatch = null;
                var sc = SettingsModel.FindPath("Settings_Canvas"); if (sc != null) { var c = sc.GetComponent<Canvas>(); if (c != null) c.enabled = false; }
                _van.enabled = true;
                ShowSettings();
            }

            bool visible = _van.enabled && _van.gameObject.activeInHierarchy;
            if (_root.activeSelf != visible) _root.SetActive(visible);
            if (!visible) { if (_mpEdit != 0) { _mpEdit = 0; _mpMessage = null; } if (_screen != "main" || _modal != null) ResetToMain(); return; }
            bool mpEditing = _mpEdit != 0;
            TickMultiplayerInput();
            if (_background != null && (_backgroundWidth != Screen.width || _backgroundHeight != Screen.height))
            {
                _backgroundWidth = Screen.width; _backgroundHeight = Screen.height;
                float imageAspect = _background.texture.width / (float)_background.texture.height, screenAspect = _backgroundWidth / (float)Mathf.Max(1, _backgroundHeight);
                if (screenAspect > imageAspect) { float h = imageAspect / screenAspect; _background.uvRect = new Rect(0, (1 - h) / 2, 1, h); }
                else { float w = screenAspect / imageAspect; _background.uvRect = new Rect((1 - w) / 2, 0, w, 1); }
            }
            HideVanilla();
            TickReference();
            _main.gameObject.SetActive(_screen == "main" || _screen == "multiplayer");
            if (_sub != null) _sub.gameObject.SetActive(_screen != "main");
            if (_status != null) _status.gameObject.SetActive(_screen != "reference");
            bool showUpdate = _screen == "main" && _modal == null && _updateUrl != null;
            if (_updateButton != null && _updateButton.gameObject.activeSelf != showUpdate) _updateButton.gameObject.SetActive(showUpdate);
            bool showDiscord = _screen == "main" && _modal == null;
            if (_discordButton != null && _discordButton.gameObject.activeSelf != showDiscord) _discordButton.gameObject.SetActive(showDiscord);
            if (_githubButton != null && _githubButton.gameObject.activeSelf != showDiscord) _githubButton.gameObject.SetActive(showDiscord);

            if (_screen != "main" || _modal != null || _seedEdit) BlockCancelFrame = Time.frameCount + 1;
            if (_capRow != null) { TickCapture(); return; }
            TickKeyScroll();
            if (!mpEditing && Input.GetKeyDown(KeyCode.Escape))
            {
                if (_seedEdit) EndSeedEdit(false);
                else if (_modal != null) CloseModal();
                else if (_screen != "main") Back();
            }
            if (_seedEdit) TickSeed();
            if (Time.unscaledTime >= _nextLabels) { _nextLabels = Time.unscaledTime + .5f; RefreshLabels(); }
            // mouse only: a clicked button must not stay selected (Enter / arrows would act on it unseen)
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.transform.IsChildOf(_root.transform)) es.SetSelectedGameObject(null);
        }

        private void RunQueued()
        {
            if (_later.Count > 0) { var a = _later.ToArray(); _later.Clear(); foreach (var x in a) Safe(x); }
            if (_nextFrame.Count > 0) { _later.AddRange(_nextFrame); _nextFrame.Clear(); }
        }
        private static void Safe(Action a) { try { a(); } catch (Exception e) { CleanHudModule.Throttled("menu", e); } }

        internal void Teardown() { Teardown(false); }
        private void Teardown(bool keepVanilla)
        {
            Core.ReplacementGuard.MenuModalOpen = false;
            if (!keepVanilla) { _van = null; _ctx = Ctx.None; }
            RestoreVanilla();
            ForgetReference();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _background = null; _backgroundWidth = _backgroundHeight = 0;
            _root = null; _main = _sub = _modal = _status = null; _sound = null; _screen = "main"; _seedEdit = false; _vanillaWatch = null;
            _capRow = null; _capBlock = null; _keyView = _keyContent = _keyThumb = null; _keyHint = null; _warnText = null; _warnChecked = false;
            _modsButton = null; _nextModsScan = 0; _nextFrame.Clear(); _later.Clear(); BlockCancelFrame = -1;
            _languageButton = null; _languageShown = false;
            _updateButton = _discordButton = _githubButton = null;
            _mpHost = _mpSteam = _mpJoin = _mpDisconnect = _mpAddressButton = _mpPortButton = _mpNameButton = null;
            _mpStatus = _mpAddressText = _mpPortText = _mpNameText = null; _mpEdit = 0; _mpMessage = null;
        }

        private static bool IsActive(string path) { var t = SettingsModel.FindPath(path); return t != null && t.gameObject.activeInHierarchy; }

        // ---------------------------------------------------------------- vanilla access
        private string[] VanillaHidden()
        {
            return _ctx == Ctx.Title ? TitleHidden : PauseHidden;
        }
        private void HideVanilla()
        {
            foreach (var p in VanillaHidden()) HideGo(SettingsModel.FindPath(p));
            var mods = FindModsButton(); if (mods != null) HideGo(mods.transform);
            var fallback = FindModsFallback(); if (fallback != null) HideGo(fallback.transform);
        }
        private void HideGo(Transform t)
        {
            if (t == null) return;
            CanvasGroup cg;
            if (!_hiddenGroups.TryGetValue(t, out cg) || cg == null)
            {
                cg = t.GetComponent<CanvasGroup>();
                if (cg == null) { cg = t.gameObject.AddComponent<CanvasGroup>(); _added[cg] = true; }
                else if (!_added.ContainsKey(cg))
                {
                    _added[cg] = false;
                    _original[cg] = new GroupState { Alpha = cg.alpha, Raycasts = cg.blocksRaycasts, Interactable = cg.interactable };
                }
                _hiddenGroups[t] = cg;
            }
            if (cg.alpha != 0f) cg.alpha = 0f;
            cg.blocksRaycasts = false; cg.interactable = false;
        }
        private void RestoreVanilla()
        {
            foreach (var kv in _added)
            {
                if (kv.Key == null) continue;
                if (kv.Value) UnityEngine.Object.Destroy(kv.Key);
                else { var state = _original[kv.Key]; kv.Key.alpha = state.Alpha; kv.Key.blocksRaycasts = state.Raycasts; kv.Key.interactable = state.Interactable; }
            }
            _added.Clear();
            _original.Clear(); _hiddenGroups.Clear();
        }

        private static PlayMakerFSM ButtonFsm(string path)
        {
            var t = SettingsModel.FindPath(path); if (t == null) return null;
            foreach (var f in t.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Continue" || f.FsmName == "NewGame" || f.FsmName == "resetFSM") return f;
            return null;
        }
        private static bool Click(string path)
        {
            var f = ButtonFsm(path);
            if (f == null || !f.isActiveAndEnabled) { CleanHudModule.Warn("Menu: game button not available: " + path); return false; }
            f.SendEvent("Clicked");
            return true;
        }
        private static PlayMakerFSM Fsm(string objName, string fsmName)
        {
            var root = SettingsModel.FindPath(objName);
            var go = root != null ? root.gameObject : null;
            if (go != null) foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == fsmName) return f;
            return null;
        }
        private PlayMakerFSM _sound;
        private void Sound()
        {
            if (_sound == null)
            {
                var t = SettingsModel.FindPath(_ctx == Ctx.Title ? "Canvas_MM/MenuSounds" : "MainMenu_Canvas/MenuSounds");
                if (t != null) foreach (var f in t.GetComponents<PlayMakerFSM>()) if (f.FsmName == "ButtonSound") _sound = f;
            }
            if (_sound != null && _sound.isActiveAndEnabled) _sound.SendEvent("Activate");
        }
        private Button FindModsButton()
        {
            if (_van == null) return null;
            if (_modsButton != null) return _modsButton;
            if (Time.unscaledTime < _nextModsScan) return FindModsFallback();
            _nextModsScan = Time.unscaledTime + .5f;
            _buttonBuffer.Clear(); _van.GetComponentsInChildren<Button>(true, _buttonBuffer);
            foreach (var b in _buttonBuffer)
            {
                if (b is MenuButton) continue;
                if (b.name == "Apocasetter_Mods") { _modsButton = b; return b; }
                var tx = b.GetComponentInChildren<Text>(true);
                if (tx != null && tx.text != null && string.Equals(tx.text.Trim(), "MODS", StringComparison.OrdinalIgnoreCase)) { _modsButton = b; return b; }
            }
            return FindModsFallback();
        }

        private Button FindModsFallback()
        {
            if (_modsFallback != null) return _modsFallback;
            if (!WildWasteland.Core.ApocasetterBridge.Present || Time.unscaledTime < _nextFallbackScan) return null;
            _nextFallbackScan = Time.unscaledTime + .5f;
            var root = GameObject.Find("Apocasetter.MenuFallback");
            var t = root != null ? root.transform.Find("Apocasetter_Mods") : null;
            if (t != null) _modsFallback = t.GetComponent<Button>();
            return _modsFallback;
        }

        private Button FindLanguageButton()
        {
            if (_van == null) return null;
            if (_languageButton == null)
            {
                var t = _van.transform.Find("ApocaLanguage_Button");
                if (t != null) _languageButton = t.GetComponent<Button>();
            }
            return _languageButton != null && _languageButton.isActiveAndEnabled ? _languageButton : null;
        }

        // ---------------------------------------------------------------- UI building helpers
        private static RectTransform R(Transform p, string n, float x, float y, float w, float h)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(p, false);
            var t = (RectTransform)go.transform;
            t.anchorMin = t.anchorMax = new Vector2(0, 1); t.pivot = new Vector2(0, 1);
            t.anchoredPosition = new Vector2(x, -y); t.sizeDelta = new Vector2(w, h);
            return t;
        }
        private static Image Img(Transform p, string n, float x, float y, float w, float h, Color c, bool ray = false)
        { var i = R(p, n, x, y, w, h).gameObject.AddAccent<Image>(); i.color = c; i.raycastTarget = ray; return i; }
        private static GlyphText T(Transform p, string n, float x, float y, float w, float h, float size, TextAnchor a, Color c, string text)
        {
            var g = R(p, n, x, y, w, h).gameObject.AddAccent<GlyphText>();
            g.Size = size; g.Align = a; g.color = c; g.raycastTarget = false; g.Set(text);
            return g;
        }
        private static void Shade(Graphic g, float a)
        {
            var s = g.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, a); s.effectDistance = new Vector2(0, -2);
        }
        private static Sprite _plate;
        private static Texture2D _menuBackground;
        internal static void DisposeAssets()
        {
            if (_plate != null) UnityEngine.Object.Destroy(_plate); _plate = null;
            if (_menuBackground != null) UnityEngine.Object.Destroy(_menuBackground); _menuBackground = null;
        }
        internal static Texture2D MenuBackground()
        {
            if (_menuBackground != null) return _menuBackground;
            Texture2D texture = null;
            try
            {
                string path = Path.Combine(Core.Assets.Root, "LoadingScreens", "BG_MAIN.png");
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "CleanHUD.BG_MAIN", hideFlags = HideFlags.DontUnloadUnusedAsset, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!texture.LoadImage(File.ReadAllBytes(path), true)) throw new FormatException("PNG could not be decoded");
                _menuBackground = texture; texture = null;
                CleanHudModule.Log("Menu background BG_MAIN.png loaded");
            }
            catch (Exception e) { CleanHudModule.Warn("Menu background BG_MAIN.png: " + e.Message); }
            finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
            return _menuBackground;
        }
        internal static Sprite PlateSprite()
        {
            if (_plate != null) return _plate;
            var t = Assets.Tex("panel_menu"); if (t == null) return null;
            _plate = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(110, 110, 110, 110));
            _plate.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _plate;
        }
        // Smoked plate whose gold frame sits exactly on the content rect (x, y, w, h).
        private static void Plate(Transform p, float x, float y, float w, float h)
        {
            var i = Img(p, "plate", x - 40, y - 40, w + 80, h + 80, Color.white);
            i.sprite = PlateSprite(); i.type = Image.Type.Sliced; i.raycastTarget = true;
        }
        private static Image[] Frame(Transform p, float x, float y, float w, float h, float b, Color c)
        {
            return new[] { Img(p, "t", x, y, w, b, c), Img(p, "b", x, y + h - b, w, b, c),
                Img(p, "l", x, y + b, b, h - 2 * b, c), Img(p, "r", x + w - b, y + b, b, h - 2 * b, c) };
        }

        // Menu row: gold bar + label; gold when hovered / selected.
        private MenuButton Row(Transform p, float x, float y, float w, float h, string label, float size, Action click, out GlyphText txt)
        {
            var rt = R(p, label, x, y, w, h);
            var hit = rt.gameObject.AddAccent<Image>(); hit.color = new Color(1, 1, 1, 0); hit.raycastTarget = true;
            var bar = Img(rt, "bar", 0, (h - size * .9f) / 2f, 7, size * .9f, Gold); bar.enabled = false;
            var t = T(rt, "label", 22, 0, w - 22, h, size, TextAnchor.MiddleLeft, White, label);
            Shade(t, .6f);
            var b = rt.gameObject.AddComponent<MenuButton>();
            b.transition = Selectable.Transition.None; b.targetGraphic = hit;
            b.OnVisual = on => { bar.enabled = on && b.interactable; t.color = !b.interactable ? Dim : on ? Gold : White; };
            if (click != null) b.onClick.AddListener(() => { Sound(); Safe(click); });
            b.Refresh();
            txt = t;
            return b;
        }
        private MenuButton Row(Transform p, float x, float y, float w, float h, string label, float size, Action click)
        { GlyphText t; return Row(p, x, y, w, h, label, size, click, out t); }

        private MenuButton CenteredRow(Transform p, float x, float y, float w, float h, string label, float size, Action click)
        {
            GlyphText text; var row = Row(p, x, y, w, h, label, size, click, out text);
            var rect = (RectTransform)text.transform; rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(w, h);
            text.Align = TextAnchor.MiddleCenter; text.SetVerticesDirty();
            return row;
        }

        private static void SetEnabled(MenuButton b, bool on) { b.interactable = on; b.Refresh(); }

        // Menus are mouse driven: nothing is pre-selected (a selected button would show no highlight but still take Enter).
        private void SelectFirst(Transform scope)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // ---------------------------------------------------------------- build
        private void Build()
        {
            Teardown(true);
            _root = new GameObject("CleanHUD.Menu", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(_root, _van.gameObject.scene);
            var canvas = _root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = Math.Max(_van.sortingOrder + 10, 50);
            var cs = _root.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _root.AddComponent<GraphicRaycaster>();
            _rootRt = (RectTransform)_root.transform;

            var background = MenuBackground();
            if (background != null)
            {
                _background = R(_rootRt, "background", 0, 0, 0, 0).gameObject.AddAccent<RawImage>();
                var rt = _background.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.anchoredPosition = rt.sizeDelta = Vector2.zero;
                _background.texture = background; _background.raycastTarget = false;
            }

            // title / heading (top left)
            var head = R(_rootRt, "head", 0, 0, 1920, 1080);
            var title = T(head, "title", 120, 120, 1200, 120, 104, TextAnchor.MiddleLeft, Gold, "APOCALYPTER"); title.Spacing = 6; Shade(title, .8f);
            string sub = _ctx == Ctx.Pause ? "PAUSED" : VersionText();
            T(head, "sub", 126, 228, 900, 36, 24, TextAnchor.MiddleLeft, Dim, sub);
            if (_ctx == Ctx.Title)
            {
                _updateButton = CenteredRow(_rootRt, 0, 0, 390, 64, "UPDATE AVAILABLE", 30, () => Application.OpenURL(_updateUrl));
                var rt = (RectTransform)_updateButton.transform; rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one; rt.anchoredPosition = new Vector2(-60, -60);
                rt.sizeDelta = new Vector2(390, 100);
                var plate = R(rt, "plate", -40, -40, 470, 180).gameObject.AddAccent<RawImage>();
                plate.texture = Assets.Tex("panel_weapon"); plate.raycastTarget = false; plate.transform.SetAsFirstSibling();
                T(rt, "mod", 0, 62, 390, 30, 20, TextAnchor.MiddleCenter, Gold, "WILD WASTELAND");
                _updateButton.gameObject.SetActive(false);
            }
            _discordButton = CenteredRow(_rootRt, 0, 0, 200, 48, "DISCORD", 26, () => Application.OpenURL("https://discord.gg/f56btNbPg"));
            var discordRect = (RectTransform)_discordButton.transform;
            discordRect.anchorMin = discordRect.anchorMax = discordRect.pivot = new Vector2(1, 0); discordRect.anchoredPosition = new Vector2(-60, 204);
            _githubButton = CenteredRow(_rootRt, 0, 0, 200, 48, "GITHUB", 26, () => Application.OpenURL("https://github.com/DeonUrist/WildWastelandMP"));
            var githubRect = (RectTransform)_githubButton.transform;
            githubRect.anchorMin = githubRect.anchorMax = githubRect.pivot = new Vector2(1, 0); githubRect.anchoredPosition = new Vector2(-60, 140);
            Plate(discordRect, 0, 0, 200, 112);
            var linksPlate = discordRect.GetChild(discordRect.childCount - 1); linksPlate.GetComponent<Image>().raycastTarget = false; linksPlate.SetAsFirstSibling();

            _status = R(_rootRt, "status", 0, 0, 1920, 1080);
            _status.anchorMin = _status.anchorMax = new Vector2(0, 0); _status.pivot = new Vector2(0, 0); _status.anchoredPosition = Vector2.zero;
            _statusText = T(_status, "status", 136, 1080 - 96, 1184, 40, 26, TextAnchor.MiddleLeft, White, "");
            Shade(_statusText, .95f);
            _statusBar = Img(_status, "bar", 120, 1080 - 86, 6, 20, Gold); _statusBar.enabled = false;

            BuildMain();
            _screen = "main";
            CleanHudModule.Log("Menu attached to " + _van.name + " (" + _ctx + ")");
            _nextFrame.Add(() => SelectFirst(_main));
        }

        private string VersionText()
        {
            var t = SettingsModel.FindPath("Canvas_MM/Version_text") ?? SettingsModel.FindPath("MainMenu_Canvas/MainMenu/Version_text");
            var tx = t != null ? t.GetComponent<Text>() : null;
            return tx != null && !string.IsNullOrEmpty(tx.text) ? tx.text.ToUpperInvariant() : "";
        }

        private void BuildMain()
        {
            _accentButtons.Clear();
            _main = R(_rootRt, "main", 0, 0, 1920, 1080);
            _main.anchorMin = Vector2.zero; _main.anchorMax = Vector2.one; _main.sizeDelta = Vector2.zero;
            const float X = 120, Y = 320, W = 500, RH = 60;
            var rows = new List<KeyValuePair<string, Action>>();
            _languageShown = FindLanguageButton() != null;
            if (_ctx == Ctx.Title)
            {
                rows.Add(new KeyValuePair<string, Action>("CONTINUE", ContinueLatest));
                rows.Add(new KeyValuePair<string, Action>("NEW GAME", NewGame));
                rows.Add(new KeyValuePair<string, Action>("MAP SEED", BeginSeedEdit));
                rows.Add(new KeyValuePair<string, Action>("LOAD GAME", () => ShowSlots(true)));
                if (Core.Multiplayer.Present) rows.Add(new KeyValuePair<string, Action>("MULTIPLAYER", BuildMultiplayer));
                rows.Add(new KeyValuePair<string, Action>("WILD WASTELAND", WildWasteland.Core.UI.ToolWindow.OpenFromMenu));
                rows.Add(new KeyValuePair<string, Action>("SETTINGS", ShowSettings));
                rows.Add(new KeyValuePair<string, Action>("MODS", OpenMods));
                if (_languageShown) rows.Add(new KeyValuePair<string, Action>("LANGUAGE", OpenLanguage));
                rows.Add(new KeyValuePair<string, Action>("QUIT", () => Confirm("QUIT THE GAME?", () => Click("Canvas_MM/MainMenu/Quit"))));
            }
            else
            {
                rows.Add(new KeyValuePair<string, Action>("CONTINUE", Resume));
                rows.Add(new KeyValuePair<string, Action>("SAVE GAME", OpenSave));
                if (Core.Multiplayer.Present) rows.Add(new KeyValuePair<string, Action>("MULTIPLAYER", BuildMultiplayer));
                rows.Add(new KeyValuePair<string, Action>("WILD WASTELAND", WildWasteland.Core.UI.ToolWindow.OpenFromMenu));
                rows.Add(new KeyValuePair<string, Action>("SETTINGS", ShowSettings));
                rows.Add(new KeyValuePair<string, Action>("CODEX", () => ShowReference(false)));
                rows.Add(new KeyValuePair<string, Action>("TUTORIAL", () => ShowReference(true)));
                rows.Add(new KeyValuePair<string, Action>("CREDITS", () => Click("MainMenu_Canvas/MainMenu/Credits")));
                rows.Add(new KeyValuePair<string, Action>("MODS", OpenMods));
                if (_languageShown) rows.Add(new KeyValuePair<string, Action>("LANGUAGE", OpenLanguage));
                rows.Add(new KeyValuePair<string, Action>("QUIT TO MENU", () => Confirm("QUIT TO MAIN MENU?", QuitToMenu)));
            }
            bool mods = FindModsButton() != null; _modsShown = mods;
            int n = 0; foreach (var r in rows) if (r.Key != "MODS" || mods) n++;
            Plate(_main, X, Y, W, n * RH + 32);
            float y = Y + 16;
            foreach (var r in rows)
            {
                if (r.Key == "MODS" && !mods) continue;
                GlyphText t;
                var b = Row(_main, X + 8, y, W - 16, RH, r.Key, 34, r.Value, out t);
                if (r.Key == "WILD WASTELAND")
                {
                    t.GetComponent<AccentTint>().enabled = false;
                    var label = t;
                    var visual = b.OnVisual;
                    b.OnVisual = on => { visual(on); label.color = b.interactable ? Gold : Dim; };
                    b.Refresh();
                }
                if (r.Key == "CONTINUE" && _ctx == Ctx.Title)
                {
                    _continueBtn = b;
                    _continueSub = T(b.transform, "slot", 200, 0, W - 16 - 216, RH, 20, TextAnchor.MiddleRight, Dim, "");
                }
                if (r.Key == "MAP SEED")
                {
                    _seed = CurrentSeed();
                    var box = R(b.transform, "seedbox", W - 16 - 190, 12, 170, RH - 24);
                    Frame(box, 0, 0, 170, RH - 24, 1.5f, BarBorder);
                    _seedText = T(box, "seed", 0, 0, 158, RH - 24, 26, TextAnchor.MiddleRight, White, _seed);
                }
                y += RH;
            }
            float accentWidth = AccentTheme.Names.Length * 40 + 28;
            var accents = R(_main, "accents", 0, 0, accentWidth, 44);
            accents.anchorMin = accents.anchorMax = accents.pivot = new Vector2(.5f, 0); accents.anchoredPosition = new Vector2(0, 48);
            Plate(accents, 0, 0, accentWidth, 44);
            for (int i = 0; i < AccentTheme.Names.Length; i++)
            {
                int choice = AccentTheme.DisplayOrder[i];
                var rt = R(accents, "accent " + AccentTheme.Names[choice], 16 + i * 40, 8, 36, 28);
                var hit = rt.gameObject.AddComponent<Image>(); hit.color = Color.clear;
                var square = R(rt, "square", 6, 2, 24, 24).gameObject.AddComponent<Image>(); square.color = AccentTheme.Colors[choice]; square.raycastTarget = false;
                var frame = Frame(rt, 3, -1, 30, 30, 1.5f, Gold);
                var button = rt.gameObject.AddComponent<MenuButton>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
                button.OnVisual = hover => { foreach (var line in frame) line.enabled = hover || AccentTheme.Index == choice; };
                button.onClick.AddListener(() => { Sound(); AccentTheme.Setting.Value = AccentTheme.Names[choice]; foreach (var swatch in _accentButtons) swatch.Refresh(); });
                _accentButtons.Add(button); button.Refresh();
            }
            RefreshLabels();
        }

        private void BuildMultiplayer()
        {
            if (!Core.Multiplayer.Present) return;
            ResetToMain(); _screen = "multiplayer";
            Core.ReplacementGuard.MenuModalOpen = true;
            _mpEdit = 0; _mpAddress = Core.Multiplayer.UdpAddress; _mpPort = Core.Multiplayer.UdpPort.ToString(CultureInfo.InvariantCulture); _mpName = Core.Multiplayer.PlayerName;
            var panel = _sub = R(_rootRt, "multiplayer", 700, 320, 660, 550);
            Plate(panel, 0, 0, 660, 550);
            T(panel, "heading", 30, 14, 536, 44, 34, TextAnchor.MiddleLeft, Gold, "MULTIPLAYER");
            CenteredRow(panel, 600, 14, 48, 44, "X", 28, Back);
            _mpStatus = T(panel, "status", 30, 62, 600, 44, 24, TextAnchor.MiddleLeft, White, "");
            _mpNameButton = Row(panel, 8, 112, 644, 48, "", 26, () => BeginMultiplayerInput(3), out _mpNameText);
            _mpAddressButton = Row(panel, 8, 164, 644, 48, "", 26, () => BeginMultiplayerInput(1), out _mpAddressText);
            _mpPortButton = Row(panel, 8, 216, 644, 48, "", 26, () => BeginMultiplayerInput(2), out _mpPortText);
            _mpSteam = Row(panel, 8, 276, 644, 56, "HOST (STEAM)", 30, () => { if (SaveMultiplayerEndpoint()) Core.UI.ToolWindow.OpenHostSetup(true); });
            _mpHost = Row(panel, 8, 338, 644, 56, "HOST (IP)", 30, () => { if (SaveMultiplayerEndpoint()) Core.UI.ToolWindow.OpenHostSetup(false); });
            _mpJoin = Row(panel, 8, 400, 644, 56, "JOIN", 32, () =>
            {
                if (Core.Multiplayer.SessionActive || !SaveMultiplayerEndpoint()) return;
                if (_ctx == Ctx.Pause) { Confirm("RETURN TO MENU TO JOIN?", QuitToMenu); return; }
                Core.Multiplayer.JoinFromMenu(); RefreshLabels();
            });
            _mpDisconnect = Row(panel, 8, 462, 644, 56, "DISCONNECT", 30, () => Confirm("DISCONNECT THE SESSION?", () => { Core.Multiplayer.DisconnectFromMenu(); RefreshLabels(); }));
            RefreshMultiplayer();
        }
        private void RefreshMultiplayer()
        {
            if (_mpStatus == null) return;
            bool active = Core.Multiplayer.SessionActive;
            SetEnabled(_mpHost, Core.Multiplayer.CanHost); SetEnabled(_mpSteam, Core.Multiplayer.CanHost);
            SetEnabled(_mpJoin, !active && (_ctx == Ctx.Pause || Core.Multiplayer.CanJoin)); SetEnabled(_mpDisconnect, active);
            SetEnabled(_mpAddressButton, !active); SetEnabled(_mpPortButton, !active); SetEnabled(_mpNameButton, !active);
            string name = "NAME  " + _mpName + (_mpEdit == 3 ? "_" : "");
            _mpNameText.Size = Fit(name, 26, 600, 16); _mpNameText.Set(name);
            string status = _mpMessage ?? Core.Multiplayer.ConnectionStatus;
            _mpStatus.Size = Fit(status, 24, 600, 16); _mpStatus.Set(status);
            string address = "HOST IP  " + _mpAddress + (_mpEdit == 1 ? "_" : "");
            _mpAddressText.Size = Fit(address, 26, 600, 16); _mpAddressText.Set(address);
            _mpPortText.Set("UDP PORT  " + _mpPort + (_mpEdit == 2 ? "_" : ""));
            string reason = Core.Multiplayer.EndReason;
            if (active) _shownMPReason = null;
            else if (!string.IsNullOrEmpty(reason) && reason != _shownMPReason && _modal == null)
            { _shownMPReason = reason; MultiplayerReason(reason); }
        }
        private void MultiplayerReason(string reason)
        {
            var lines = TalkDialogue.Wrap(reason, 816, 24); float height = 152 + lines.Count * 30;
            _modal = R(_rootRt, "connection_reason", 0, 0, 1920, 1080);
            Core.ReplacementGuard.MenuModalOpen = true;
            var dim = _modal.gameObject.AddAccent<Image>(); dim.color = new Color(0, 0, 0, .55f); dim.raycastTarget = true;
            var box = R(_modal, "box", 510, (1080 - height) / 2, 900, height);
            Plate(box, 0, 0, 900, height);
            T(box, "heading", 42, 20, 816, 44, 32, TextAnchor.MiddleLeft, Gold, "CONNECTION CLOSED");
            float y = 76;
            foreach (string line in lines) { T(box, "reason", 42, y, 816, 30, 24, TextAnchor.MiddleLeft, White, line); y += 30; }
            CenteredRow(box, 320, height - 68, 260, 56, "OK", 30, CloseModal);
            _nextFrame.Add(() => SelectFirst(_modal));
        }
        private void BeginMultiplayerInput(int field)
        {
            if (Core.Multiplayer.SessionActive) return;
            _mpEdit = field; _mpReplace = true; _mpMessage = "TYPE TO REPLACE; ENTER TO APPLY; ESC TO CANCEL";
            BlockCancelFrame = Time.frameCount + 1; RefreshMultiplayer();
        }
        private bool SaveMultiplayerEndpoint()
        {
            _mpEdit = 0; _mpMessage = Core.Multiplayer.SetEndpoint(_mpAddress, _mpPort, _mpName);
            if (_mpMessage == null) _mpName = Core.Multiplayer.PlayerName;
            RefreshMultiplayer(); return _mpMessage == null;
        }
        private void TickMultiplayerInput()
        {
            if (_mpEdit == 0) return;
            if (_screen != "multiplayer" || _modal != null || !Application.isFocused || Core.Multiplayer.SessionActive) { _mpEdit = 0; _mpMessage = null; return; }
            BlockCancelFrame = Time.frameCount + 1;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _mpEdit = 0; _mpMessage = null; _mpAddress = Core.Multiplayer.UdpAddress; _mpPort = Core.Multiplayer.UdpPort.ToString(CultureInfo.InvariantCulture); _mpName = Core.Multiplayer.PlayerName;
                RefreshMultiplayer(); return;
            }
            if (Input.inputString.Length == 0) return;
            string value = _mpEdit == 1 ? _mpAddress : _mpEdit == 2 ? _mpPort : _mpName;
            foreach (char ch in Input.inputString)
            {
                if (ch == '\n' || ch == '\r')
                { if (_mpEdit == 1) _mpAddress = value; else if (_mpEdit == 2) _mpPort = value; else _mpName = value; SaveMultiplayerEndpoint(); return; }
                if (ch == '\b') { value = _mpReplace ? "" : value.Length > 0 ? value.Substring(0, value.Length - 1) : ""; _mpReplace = false; }
                else if ((_mpEdit == 3 ? !char.IsControl(ch) && ch != '<' && ch != '>' : ch >= '0' && ch <= '9' || _mpEdit == 1 && ch == '.') && (_mpReplace || value.Length < (_mpEdit == 1 ? 15 : _mpEdit == 2 ? 5 : 48)))
                { if (_mpReplace) value = ""; _mpReplace = false; value += ch; }
            }
            if (_mpEdit == 1) _mpAddress = value; else if (_mpEdit == 2) _mpPort = value; else _mpName = value;
            RefreshMultiplayer();
        }

        private void RefreshLabels()
        {
            RefreshMultiplayer();
            var language = FindLanguageButton();
            if (language != null) HideGo(language.transform);
            if (_rootRt != null) HideGo(_rootRt.Find("ApocaLanguage_Button"));
            if (_screen == "main" && _modal == null && !_seedEdit && _main != null && ((FindModsButton() != null) != _modsShown || (language != null) != _languageShown))
            {
                // Optional mods add their buttons a moment after the menu appears
                UnityEngine.Object.Destroy(_main.gameObject); BuildMain(); _nextFrame.Add(() => SelectFirst(_main)); return;
            }
            if (_warnText != null)
            {
                if (!_warnChecked && KeyCatalog.ScanDone && (WildWasteland.Core.GameRefs.GameManager == null || SceneCache.KeysReady)) { _warnChecked = true; try { KeyCatalog.Collect(); } catch (Exception e) { CleanHudModule.Warn("Keys: " + e.Message); } }
                int n = KeyCatalog.ClashCount;
                _warnText.Set(_screen == "main" && _warnChecked && n > 0 ? n + (n == 1 ? " KEY IS" : " KEYS ARE") + " USED TWICE  -  SETTINGS > CONTROLS" : "");
            }
            if (_ctx == Ctx.Title && _continueBtn != null)
            {
                int slot; var label = LatestSave(out slot);
                SetEnabled(_continueBtn, slot > 0);
                if (_continueSub != null) _continueSub.Set(slot > 0 ? label : "NO SAVE");
            }
            if (_ctx == Ctx.Pause && _statusText != null)
            {
                string s;
                if (IsActive("MainMenu_Canvas/MainMenu/Saving game text")) s = "SAVING GAME" + new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);
                else
                {
                    var dv = SettingsModel.FindPath("MainMenu_Canvas/MainMenu/Distance/distance_value");
                    var tx = dv != null ? dv.GetComponent<Text>() : null;
                    s = tx != null && !string.IsNullOrEmpty(tx.text) ? "DISTANCE TRAVELLED  " + tx.text.Trim() + " KM" : "";
                }
                _statusText.Set(s);
                _statusBar.enabled = !string.IsNullOrEmpty(s);
            }
        }

        // ---------------------------------------------------------------- actions
        private void ResetToMain()
        {
            CloseReference();
            CloseModal();
            if (_seedEdit) EndSeedEdit(false);
            if (_sub != null) { UnityEngine.Object.Destroy(_sub.gameObject); _sub = null; }
            if (_settingsDirty) SaveSettingsBroadcast();
            if (_capBlock != null) UnityEngine.Object.Destroy(_capBlock);
            _capRow = null; _capBlock = null; _keyView = _keyContent = _keyThumb = null; _keyHint = null;
            if (_screen == "settings") _warnChecked = false;
            _screen = "main"; _mpEdit = 0; _mpMessage = null;
            Core.ReplacementGuard.MenuModalOpen = false;
        }
        private void Back()
        {
            ResetToMain();
            _main.gameObject.SetActive(true);
            _nextFrame.Add(() => SelectFirst(_main));
        }

        private void OpenMods()
        {
            var b = FindModsButton();
            if (b != null) b.onClick.Invoke();
        }

        private void OpenLanguage()
        {
            var entries = ApocaLanguageMenu.Read(); if (entries.Count == 0) return;
            CloseModal();
            _modal = R(_rootRt, "languages", 0, 0, 1920, 1080);
            Core.ReplacementGuard.MenuModalOpen = true;
            var blocker = _modal.gameObject.AddComponent<Image>(); blocker.color = Color.clear;
            _modal.gameObject.AddComponent<ClickZone>().OnClick = CloseModal;
            float height = Mathf.Min(660, entries.Count * 40 + 84), x = 660, y = 320;
            Plate(_modal, x, y, 480, height);
            T(_modal, "heading", x + 28, y + 12, 350, 42, 30, TextAnchor.MiddleLeft, Gold, "LANGUAGE");
            CenteredRow(_modal, x + 410, y + 12, 48, 42, "X", 28, CloseModal);
            var view = R(_modal, "list", x + 8, y + 64, 464, height - 80);
            view.gameObject.AddComponent<RectMask2D>();
            var content = R(view, "content", 0, 0, 464, entries.Count * 40);
            var scroll = view.gameObject.AddComponent<MenuScrollRect>(); scroll.viewport = view; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true; scroll.inertia = false; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; GlyphText glyph;
                var button = Row(content, 0, i * 40, 464, 40, entry.Name, 26, () => { entry.Choose(); CloseModal(); }, out glyph);
                button.name = "language " + entry.Code;
                glyph.Size = Fit(entry.Name, 26, 430, 18); button.Sticky = entry.Selected; button.Refresh();
            }
            BlockCancelFrame = Time.frameCount + 1;
        }

        private void Resume()
        {
            var menu = Fsm("__GameManager__", "Menu");
            if (menu == null) return;
            PlayMakerFSM.BroadcastEvent("Save Settings");
            menu.SendEvent("Activate");
            // the pause state answers Activate; if not, enter play directly
            _nextFrame.Add(() => { if (menu != null && menu.ActiveStateName == "pause") menu.Fsm.SetState("play"); });
        }

        private void QuitToMenu()
        {
            if (!Click("MainMenu_Canvas/MainMenu/Quit_To_Menu")) return;
            // the game's "Really Quit?" box (hidden) becomes active next frame; press its Yes
            _nextFrame.Add(() => _nextFrame.Add(() => Click("MainMenu_Canvas/MainMenu/Quit_To_Menu/Really Quit/Yes_Quit")));
        }

        private void NewGame()
        {
            if (_seedEdit) EndSeedEdit(true);
            ApplySeed();
            BeginLoad("Canvas_MM/MainMenu/NewGame");
        }

        private bool BeginLoad(string path)
        {
            _loadingScreen.Begin();
            if (Click(path)) return true;
            _loadingScreen.End(); return false;
        }

        private static string SaveFile(int n) { return Path.Combine(Application.persistentDataPath, "SaveGame" + n + ".es3"); }
        private static bool SlotUsed(int n) { var f = new FileInfo(SaveFile(n)); return f.Exists && f.Length > 1024; }

        private string LatestSave(out int slot)
        {
            slot = 0; DateTime best = DateTime.MinValue;
            for (int n = 1; n <= 10; n++)
            {
                if (!SlotUsed(n)) continue;
                var t = File.GetLastWriteTime(SaveFile(n));
                if (t > best) { best = t; slot = n; }
            }
            return slot > 0 ? SlotLabel(slot, true) : "";
        }

        private static readonly Regex SeedPart = new Regex(@"\s*\(Seed:[^)]*\)", RegexOptions.Compiled);
        private string SlotLabel(int n, bool shortForm)
        {
            string s = null;
            var path = _ctx == Ctx.Title ? "LoadGame_Canvas/LoadGame/load slot " + n + "/load " + n + " time" : "SaveGame_Canvas/SaveGame/save slot " + n + "/save " + n + " time";
            var t = SettingsModel.FindPath(path);
            var tx = t != null ? t.GetComponent<Text>() : null;
            if (tx != null && !string.IsNullOrEmpty(tx.text)) s = tx.text;
            if (string.IsNullOrEmpty(s)) s = SettingsModel.ReadString("saveTime" + n);
            if (string.IsNullOrEmpty(s)) s = SlotUsed(n) ? File.GetLastWriteTime(SaveFile(n)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "";
            s = SeedPart.Replace(s ?? "", "").Trim().ToUpperInvariant();
            if (shortForm && s.Length > 26) s = s.Substring(0, 25) + "…";
            return s;
        }

        private void ContinueLatest()
        {
            int slot; LatestSave(out slot);
            if (slot <= 0) return;
            BeginLoad("LoadGame_Canvas/LoadGame/load slot " + slot + "/load_game_" + slot);
        }

        private void OpenSave()
        {
            if (Core.Multiplayer.Client) { Core.MultiplayerSaving.RequestClientSave(); Resume(); return; }
            if (Core.Multiplayer.Hosting) { Core.MultiplayerSaving.RequestSave(); Resume(); return; }
            var incar = Fsm("Player", "InCar");
            if (incar != null && incar.ActiveStateName == "InCar") { Confirm("GET OUT OF THE CAR TO SAVE", null); return; }
            ShowSlots(false);
        }

        // ---------------------------------------------------------------- seed
        private string CurrentSeed()
        {
            var t = SettingsModel.FindPath("Canvas_MM/MainMenu/Seed");
            var f = t != null ? t.GetComponent<InputField>() : null;
            if (f != null && !string.IsNullOrEmpty(f.text)) return f.text;
            var g = t != null ? t.GetComponent<PlayMakerFSM>() : null;
            if (g != null) { var v = g.FsmVariables.FindFsmInt("seed"); if (v != null) return v.Value.ToString(CultureInfo.InvariantCulture); }
            var tx = t != null ? t.Find("Text") : null;
            var tt = tx != null ? tx.GetComponent<Text>() : null;
            return tt != null && !string.IsNullOrEmpty(tt.text) ? tt.text : "12345";
        }
        private void BeginSeedEdit() { _seedEdit = true; _seedText.color = Gold; }
        private void TickSeed()
        {
            foreach (var ch in Input.inputString)
            {
                if (ch == '\b') { if (_seed.Length > 0) _seed = _seed.Substring(0, _seed.Length - 1); }
                else if (ch == '\n' || ch == '\r') { EndSeedEdit(true); return; }
                else if (char.IsDigit(ch) && _seed.Length < 9) _seed += ch;
            }
            _seedText.Set(_seed + (((int)(Time.unscaledTime * 2f)) % 2 == 0 ? "_" : " "));
        }
        private void EndSeedEdit(bool apply)
        {
            _seedEdit = false;
            if (_seed.Length == 0) _seed = CurrentSeed();
            _seedText.Set(_seed); _seedText.color = White;
            if (apply) ApplySeed();
        }
        private void ApplySeed()
        {
            var t = SettingsModel.FindPath("Canvas_MM/MainMenu/Seed");
            if (t == null) return;
            var f = t.GetComponent<InputField>();
            if (f != null) { f.text = _seed; if (f.onEndEdit != null) f.onEndEdit.Invoke(_seed); }
            int v;
            if (int.TryParse(_seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                foreach (var g in t.GetComponents<PlayMakerFSM>()) { var s = g.FsmVariables.FindFsmInt("seed"); if (s != null) s.Value = v; }
        }

        // ---------------------------------------------------------------- modal
        private void Confirm(string question, Action yes)
        {
            CloseModal();
            _modal = R(_rootRt, "modal", 0, 0, 1920, 1080);
            Core.ReplacementGuard.MenuModalOpen = true;
            _modal.anchorMin = Vector2.zero; _modal.anchorMax = Vector2.one; _modal.sizeDelta = Vector2.zero; _modal.anchoredPosition = Vector2.zero;
            var dim = _modal.gameObject.AddAccent<Image>(); dim.color = new Color(0, 0, 0, .55f); dim.raycastTarget = true;
            var box = R(_modal, "box", 0, 0, 640, yes != null ? 236 : 172);
            box.anchorMin = box.anchorMax = new Vector2(.5f, .5f); box.pivot = new Vector2(.5f, .5f); box.anchoredPosition = Vector2.zero;
            Plate(box, 0, 0, 640, yes != null ? 236 : 172);
            T(box, "q", 0, 22, 640, 50, 34, TextAnchor.MiddleCenter, White, question);
            if (yes != null)
            {
                CenteredRow(box, 40, 96, 260, 64, "YES", 34, () => { CloseModal(); yes(); });
                CenteredRow(box, 340, 96, 260, 64, "NO", 34, CloseModal);
            }
            else Row(box, 190, 92, 260, 64, "OK", 34, CloseModal);
            _nextFrame.Add(() => SelectFirst(_modal));
        }
        private void CloseModal()
        {
            if (_modal == null) return;
            UnityEngine.Object.Destroy(_modal.gameObject); _modal = null;
            Core.ReplacementGuard.MenuModalOpen = _screen == "multiplayer";
            _nextFrame.Add(() => SelectFirst(_screen == "main" ? _main : _sub));
        }

        // ---------------------------------------------------------------- load / save slots
        private void ShowSlots(bool load)
        {
            ResetToMain();
            _screen = load ? "load" : "save";
            _main.gameObject.SetActive(false);
            _sub = R(_rootRt, _screen, 0, 0, 1920, 1080);
            const float X = 120, Y = 280, W = 860, RH = 52;
            Plate(_sub, X, Y, W, 84 + 10 * RH + 16 + 64 + 24);
            T(_sub, "head", X + 30, Y + 18, W - 60, 50, 40, TextAnchor.MiddleLeft, Gold, load ? "LOAD GAME" : "SAVE GAME");
            float y = Y + 84;
            for (int n = 1; n <= 10; n++)
            {
                int slot = n;
                bool used = SlotUsed(n);
                string label = used ? SlotLabel(n, false) : "EMPTY";
                GlyphText t;
                var b = Row(_sub, X + 8, y, W - 16, RH, " " + n.ToString(CultureInfo.InvariantCulture), 30, () =>
                {
                    if (load) { if (BeginLoad("LoadGame_Canvas/LoadGame/load slot " + slot + "/load_game_" + slot)) ResetToMain(); }
                    else if (SlotUsed(slot)) Confirm("OVERWRITE SLOT " + slot + "?", () => SaveTo(slot));
                    else SaveTo(slot);
                }, out t);
                var lt = T(b.transform, "name", 90, 0, W - 16 - 110, RH, 26, TextAnchor.MiddleLeft, used ? White : Dim, label);
                var bt = b;
                var baseVisual = b.OnVisual;
                b.OnVisual = on => { baseVisual(on); lt.color = !bt.interactable ? Dim : on ? Gold : (used ? White : Dim); };
                if (load && !used) SetEnabled(b, false);
                b.Refresh();
                y += RH;
            }
            Row(_sub, X + 8, y + 16, 300, 64, "BACK", 34, Back);
            _nextFrame.Add(() => SelectFirst(_sub));
        }

        private void SaveTo(int slot)
        {
            if (Click("SaveGame_Canvas/SaveGame/save slot " + slot + "/save_game_" + slot)) Back();
        }

        // ---------------------------------------------------------------- settings
        private void ShowSettings()
        {
            ResetToMain();
            _screen = "settings";
            _main.gameObject.SetActive(false);
            _display = Display.Load();
            _keys = null; _keyMsg = null; _keySelect = null; _keyScrollY = 0;
            BuildSettings();
        }

        private void BuildSettings()
        {
            if (_sub != null) UnityEngine.Object.Destroy(_sub.gameObject);
            _sub = R(_rootRt, "settings", 0, 0, 1920, 1080);
            const float X = 120, Y = 280, W = 1240, H = 740;
            Plate(_sub, X, Y, W, H);
            // tabs
            float ty = Y + 24;
            MenuButton first = null;
            var tabs = SettingsModel.Tabs();
            if (!tabs.Contains(_tab)) _tab = tabs[0];
            foreach (var tab in tabs)
            {
                string tb = tab;
                var b = Row(_sub, X + 8, ty, 290, 60, tab, 30, () => { _tab = tb; _keyMsg = null; _keySelect = null; _keyScrollY = 0; BuildSettings(); });
                b.Sticky = tab == _tab; b.Refresh();
                if (first == null || b.Sticky) first = b;
                ty += 60;
            }
            Row(_sub, X + 8, Y + H - 84, 290, 64, "BACK", 34, Back);
            Img(_sub, "divider", X + 318, Y + 24, 1.5f, H - 48, new Color(Gold.r, Gold.g, Gold.b, .35f));
            // rows
            const float RX = X + 340, RW = W - 340 - 24, RH = 54;
            T(_sub, "head", RX + 22, Y + 22, RW, 50, 40, TextAnchor.MiddleLeft, Gold, _tab);
            float y = Y + 92;
            GameObject focus = first != null ? first.gameObject : null;
            _keyView = _keyContent = _keyThumb = null; _keyHint = null;
            if (_tab == "CONTROLS")
            {
                var f = BuildKeys(_sub, RX, Y, RW, H);
                if (f != null) focus = f;
            }
            else
                foreach (var d in SettingsModel.Defs)
                {
                    if (d.Tab != _tab) continue;
                    if (d.TitleOnly && _ctx != Ctx.Title) continue;
                    if (d.GameOnly && _ctx != Ctx.Pause) continue;
                    SettingRow(_sub, RX, y, RW, RH, d);
                    y += RH;
                }
            if (_tab == "GRAPHICS")
            {
                const float AW = 300, AH = 50;
                var apply = CenteredRow(_sub, RX + RW - AW, Y + H - AH - 14, AW, AH, "APPLY DISPLAY", 26, () => _display.Apply());
                apply.Sticky = true; apply.Refresh();
                apply.GetComponent<Image>().color = new Color(Gold.r, Gold.g, Gold.b, .15f);
                Frame(apply.transform, 0, 0, AW, AH, 1.5f, Gold);
                T(_sub, "displayhint", RX + 22, Y + H - AH - 14, RW - AW - 40, AH, 20, TextAnchor.MiddleLeft, Dim, "SELECT DISPLAY OPTIONS, THEN APPLY");
            }
            if (_tab == "CHEATS")
                T(_sub, "cheatnote", RX + 22, Y + H - 52, RW - 44, 36, 20, TextAnchor.MiddleLeft, Dim, "CHEATS ENABLED IN WILD WASTELAND SETTINGS");
            _keyLastSel = null;
            _nextFrame.Add(() => SelectFirst(_sub));
        }

        private void SettingRow(Transform p, float x, float y, float w, float h, SettingDef d)
        {
            GlyphText lbl;
            MenuButton b = null;
            const float CW = 300;                  // control column (right)
            float cx = w - CW - 24;
            switch (d.Kind)
            {
                case "bool": case "fsm":
                {
                    float v = SettingsModel.Read(d);
                    b = Row(p, x, y, w, h, d.Label, 26, null, out lbl);
                    var box = R(b.transform, "box", cx, (h - 28) / 2f, 28, 28);
                    Frame(box, 0, 0, 28, 28, 2f, Gold);
                    var fill = Img(box, "on", 6, 6, 16, 16, Gold);
                    var vt = T(b.transform, "value", cx + 44, 0, 120, h, 24, TextAnchor.MiddleLeft, White, "");
                    Action show = () => { fill.enabled = v > .5f; vt.Set(SettingsModel.Format(d, v)); };
                    show();
                    if (d.Key == "color_grading") { SetEnabled(b, false); break; }
                    Action flip = () => { v = v > .5f ? 0 : 1; SettingsModel.Write(d, v); if (d.Kind == "bool") _settingsDirty = true; show(); };
                    b.onClick.AddListener(() => { Sound(); flip(); });
                    b.OnHorizontal = dir => flip();
                    break;
                }
                case "float":
                {
                    float v = SettingsModel.Read(d);
                    b = Row(p, x, y, w, h, d.Label, 26, null, out lbl);
                    var bar = R(b.transform, "bar", cx, (h - 20) / 2f, 220, 20);
                    var back = bar.gameObject.AddAccent<Image>(); back.color = new Color(0, 0, 0, .25f); back.raycastTarget = true;
                    var fill = R(bar, "fill", 1.5f, 1.5f, 0, 17).gameObject.AddAccent<RawImage>();
                    fill.texture = Assets.VGradient(.75f); fill.color = Gold; fill.raycastTarget = false;
                    Frame(bar, 0, 0, 220, 20, 1.5f, BarBorder);
                    var vt = T(b.transform, "value", cx + 232, 0, 70, h, 24, TextAnchor.MiddleRight, White, "");
                    Action show = () =>
                    {
                        float t01 = Mathf.InverseLerp(d.Min, d.Max, v);
                        fill.rectTransform.sizeDelta = new Vector2(217f * t01, 17f); fill.enabled = t01 > 0;
                        vt.Set(SettingsModel.Format(d, v));
                    };
                    show();
                    Action<float> set = nv => { v = Mathf.Clamp(nv, d.Min, d.Max); SettingsModel.Write(d, v); _settingsDirty = true; show(); };
                    var drag = bar.gameObject.AddComponent<BarDrag>(); drag.OnValue = t01 => set(Mathf.Lerp(d.Min, d.Max, t01));
                    float step = (d.Max - d.Min) / 20f;
                    b.OnHorizontal = dir => set(v + dir * step);
                    break;
                }
                case "res": case "mode": case "quality":
                {
                    b = Row(p, x, y, w, h, d.Label, 26, null, out lbl);
                    var vt = T(b.transform, "value", cx + 30, 0, 240, h, 24, TextAnchor.MiddleCenter, White, "");
                    var left = T(b.transform, "<", cx, 0, 30, h, 26, TextAnchor.MiddleCenter, Gold, "<");
                    var right = T(b.transform, ">", cx + 270, 0, 30, h, 26, TextAnchor.MiddleCenter, Gold, ">");
                    string kind = d.Kind;
                    Action show = () => vt.Set(kind == "res" ? _display.ResText : kind == "mode" ? Display.ModeNames[_display.Mode] : _display.QualityText);
                    show();
                    Action<int> step = dir =>
                    {
                        if (kind == "res")
                        {
                            int i = _display.Res.IndexOf(new Vector2Int(_display.W, _display.H));
                            i = (i + dir + _display.Res.Count) % Math.Max(1, _display.Res.Count);
                            if (_display.Res.Count > 0) { _display.W = _display.Res[i].x; _display.H = _display.Res[i].y; }
                        }
                        else if (kind == "mode") _display.Mode = (_display.Mode + dir + 4) % 4;
                        else { int qn = Math.Max(1, QualitySettings.names.Length); _display.Quality = (_display.Quality + dir + qn) % qn; }
                        show();
                    };
                    b.onClick.AddListener(() => { Sound(); step(1); });
                    b.OnHorizontal = step;
                    left.raycastTarget = true; left.gameObject.AddComponent<ClickZone>().OnClick = () => { Sound(); step(-1); };
                    right.raycastTarget = true; right.gameObject.AddComponent<ClickZone>().OnClick = () => { Sound(); step(1); };
                    break;
                }
                case "screen":
                    b = Row(p, x, y, w, h, d.Label, 26, () => OpenScreen(d.Path, d.Key), out lbl);
                    T(b.transform, "hint", cx, 0, CW, h, 20, TextAnchor.MiddleLeft, Dim, "THE GAME'S OWN SCREEN");
                    break;
            }
        }

        // Open one of the game's screens (Debug) and come back to our settings when it closes.
        private void OpenScreen(string button, string canvas)
        {
            var ctl = SettingsModel.FindPath(canvas);
            var c = ctl != null ? ctl.GetComponent<Canvas>() : null;
            if (c == null) { CleanHudModule.Warn("Menu: game screen not found: " + canvas); return; }
            if (!Click(button)) return;
            _van.enabled = false;
            _vanillaWatch = c; _vanillaWatchSince = Time.unscaledTime;
        }

        // ---------------------------------------------------------------- key bindings (CONTROLS)
        private static float Fit(string s, float size, float w, float min)
        {
            var f = GlyphFont.Get();
            while (size > min && f.Measure(s, size, 0) > w) size -= 1f;
            return size;
        }
        private static string Clip(string s, float size, float w)
        {
            var f = GlyphFont.Get();
            if (f.Measure(s, size, 0) <= w) return s;
            while (s.Length > 1 && f.Measure(s + "…", size, 0) > w) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "…";
        }
        private static string KeyId(KeyBind b) { return (b.Group ?? "") + "|" + b.Tab + "|" + b.Label + "|" + b.Order; }
        private static bool HasClash(KeyBind b) { foreach (var s in b.Slots) if (s.Clash.Count > 0) return true; return false; }

        private GameObject BuildKeys(Transform p, float X, float Y, float W, float H)
        {
            if (_keys == null) _keys = KeyCatalog.Collect();
            int n = KeyCatalog.ClashCount;
            T(p, "dups", X + 300, Y + 22, W - 324, 50, 22, TextAnchor.MiddleRight, n > 0 ? Red : Dim,
                n > 0 ? n + (n == 1 ? " KEY IS" : " KEYS ARE") + " USED TWICE (RED)" : "NO KEY IS USED TWICE");

            // sub tabs by type
            var font = GlyphFont.Get();
            var tabs = KeyCatalog.Tabs;
            float size = 22, pad = 18, total = 0;
            foreach (var t in tabs) total += font.Measure(t, size, 0) + pad * 2 + 14;
            if (total > W - 44) { size = 19; pad = 12; total = 0; foreach (var t in tabs) total += font.Measure(t, size, 0) + pad * 2 + 14; }
            float tx = X + 22, ty = Y + 80;
            GameObject tabFocus = null;
            foreach (var tab in tabs)
            {
                string tb = tab;
                bool clash = false;
                foreach (var b in _keys) if (b.Tab == tab && HasClash(b)) { clash = true; break; }
                float w = font.Measure(tab, size, 0) + pad * 2 + 14;
                var rt = R(p, "ktab " + tab, tx, ty, w, 40);
                var hit = rt.gameObject.AddAccent<Image>(); hit.color = new Color(1, 1, 1, 0); hit.raycastTarget = true;
                var lbl = T(rt, "label", pad, 0, w - pad * 2 - 14, 40, size, TextAnchor.MiddleLeft, White, tab);
                var line = Img(rt, "line", pad, 37, w - pad * 2 - 14, 3, Gold);
                if (clash) Img(rt, "dot", w - pad - 10, 15, 9, 9, Red);
                var mb = rt.gameObject.AddComponent<MenuButton>();
                mb.transition = Selectable.Transition.None; mb.targetGraphic = hit;
                mb.Sticky = tab == _keyTab;
                mb.OnVisual = on => { lbl.color = on ? Gold : White; line.enabled = mb.Sticky; };
                mb.onClick.AddListener(() => { Sound(); _keyTab = tb; _keyMsg = null; _keySelect = "tab:" + tb; _keyScrollY = 0; BuildSettings(); });
                mb.Refresh();
                if (_keySelect == "tab:" + tab) tabFocus = rt.gameObject;
                tx += w;
            }

            float c2 = W - 34 - BoxW, c1 = c2 - 14 - BoxW;
            T(p, "h1", X + c1, Y + 128, BoxW, 24, 17, TextAnchor.MiddleCenter, Dim, _keyTab == "MODS" ? "KEY" : "KEY / +");
            if (_keyTab != "MODS") T(p, "h2", X + c2, Y + 128, BoxW, 24, 17, TextAnchor.MiddleCenter, Dim, "ALTERNATIVE / -");

            // scrolling list
            float vy = Y + 154, vh = H - 154 - 66;
            _keyView = R(p, "view", X, vy, W, vh);
            var vi = _keyView.gameObject.AddAccent<Image>(); vi.color = new Color(1, 1, 1, 0); vi.raycastTarget = true;
            _keyView.gameObject.AddComponent<RectMask2D>();
            _keyContent = R(_keyView, "content", 0, 0, W - 14, 10);
            float y = 0;
            string group = null;
            bool hardHead = false;
            GameObject focus = null;
            foreach (var b in _keys)
            {
                if (b.Tab != _keyTab) continue;
                string head = null;
                if (b.Tab == "MODS") { if (b.Group != group) { group = b.Group; head = group; } }
                else if (!hardHead && b.Order >= 10000) { hardHead = true; head = "HARDCODED IN THE GAME (CAN'T BE CHANGED)"; }
                if (head != null)
                {
                    if (y > 0) y += 8;
                    T(_keyContent, "head", 22, y, W - 60, 34, 21, TextAnchor.MiddleLeft, Gold, head);
                    Img(_keyContent, "rule", 22, y + 33, W - 72, 1.5f, new Color(Gold.r, Gold.g, Gold.b, .3f));
                    y += 38;
                }
                var go = KeyRowUI(_keyContent, y, W - 14, b, c1, c2);
                if (_keySelect != null && _keySelect == KeyId(b)) focus = go;
                y += KeyRH;
            }
            if (y == 0) T(_keyContent, "none", 22, 0, W - 60, KeyRH, 22, TextAnchor.MiddleLeft, Dim, "NO KEYS");
            _keyContent.sizeDelta = new Vector2(W - 14, y);
            var sr = _keyView.gameObject.AddComponent<MenuScrollRect>();
            sr.content = _keyContent; sr.viewport = _keyView; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.inertia = false; sr.scrollSensitivity = 48;
            _keyContent.anchoredPosition = new Vector2(0, Mathf.Clamp(_keyScrollY, 0, Mathf.Max(0, y - vh)));
            // scroll bar
            if (y > vh)
            {
                var track = Img(p, "track", X + W - 18, vy, 18, vh, new Color(Gold.r, Gold.g, Gold.b, .15f), true);
                _keyThumb = Img(track.transform, "thumb", 0, 0, 0, 0, Gold, true).rectTransform;
                _keyThumb.pivot = new Vector2(.5f, .5f); _keyThumb.sizeDelta = new Vector2(-10, 0);
                var scrollbar = track.gameObject.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.handleRect = _keyThumb; scrollbar.targetGraphic = _keyThumb.GetComponent<Image>();
                scrollbar.transition = Selectable.Transition.None; sr.verticalScrollbar = scrollbar;
            }
            _keyHint = T(p, "hint", X + 22, Y + H - 52, W - 44, 36, 20, TextAnchor.MiddleLeft, Dim, "");
            ShowKeyHint();
            return focus ?? tabFocus;
        }

        private GameObject KeyRowUI(Transform p, float y, float W, KeyBind kb, float c1, float c2)
        {
            var row = new KeyRow { B = kb };
            var rt = R(p, kb.Label, 0, y, W, KeyRH);
            var hit = rt.gameObject.AddAccent<Image>(); hit.color = new Color(1, 1, 1, 0); hit.raycastTarget = true;
            var bar = Img(rt, "bar", 0, 10, 6, KeyRH - 20, Gold); bar.enabled = false;
            string clash = null;
            foreach (var s in kb.Slots)
                if (s.Clash.Count > 0) { clash = "ALSO " + KeyCatalog.KeyText(s.Key) + ": " + s.Clash[0] + (s.Clash.Count > 1 ? " (+" + (s.Clash.Count - 1) + ")" : ""); break; }
            string note = clash ?? kb.Note;
            float lw = c1 - 34;
            float ls = Fit(kb.Label, 24, lw, 16);
            var lbl = T(rt, "label", 22, note != null ? 1 : 0, lw, note != null ? 30 : KeyRH, ls, TextAnchor.MiddleLeft, White, Clip(kb.Label, ls, lw));
            if (note != null) T(rt, "note", 22, 28, lw, 18, 15, TextAnchor.MiddleLeft, clash != null ? Red : Dim, Clip(note, 15, lw));
            for (int i = 0; i < kb.Slots.Count && i < 2; i++)
            {
                var s = kb.Slots[i];
                var box = R(rt, "slot" + i, i == 0 ? c1 : c2, (KeyRH - 34) / 2f, BoxW, 34);
                var bg = box.gameObject.AddAccent<Image>(); bg.color = new Color(0, 0, 0, .28f); bg.raycastTarget = true;
                row.Frames.Add(Frame(box, 0, 0, BoxW, 34, 1.5f, BarBorder));
                var text = s.Text ?? KeyCatalog.KeyText(s.Key);
                row.Txt.Add(T(box, "key", 4, 0, BoxW - 8, 34, Fit(text, 22, BoxW - 12, 14), TextAnchor.MiddleCenter, White, text));
                int si = i;
                box.gameObject.AddComponent<ClickZone>().OnClick = () => { row.Sel = si; StartCapture(row, si); };
            }
            var b = rt.gameObject.AddComponent<MenuButton>();
            b.transition = Selectable.Transition.None; b.targetGraphic = hit;
            row.Btn = b;
            b.OnVisual = on => { bar.enabled = on; lbl.color = on ? Gold : White; PaintRow(row, on); };
            b.OnHorizontal = d => { row.Sel = Mathf.Clamp(row.Sel + d, 0, Math.Max(0, kb.Slots.Count - 1)); b.Refresh(); };
            b.onClick.AddListener(() => { Sound(); StartCapture(row, row.Sel); });
            b.Refresh();
            return rt.gameObject;
        }

        private static void PaintRow(KeyRow row, bool hot)
        {
            for (int i = 0; i < row.Txt.Count; i++)
            {
                var s = row.B.Slots[i];
                bool clash = s.Clash.Count > 0, fixd = s.Set == null;
                var fc = clash ? Red : (hot && i == row.Sel && !fixd) ? Gold : fixd ? new Color(BarBorder.r, BarBorder.g, BarBorder.b, .25f) : BarBorder;
                foreach (var im in row.Frames[i]) im.color = fc;
                row.Txt[i].color = clash ? Red : fixd ? Dim : White;
            }
        }

        private void ShowKeyHint()
        {
            if (_keyHint == null) return;
            if (_keyMsg != null) { _keyHint.color = _keyMsgBad ? Red : Gold; _keyHint.Set(Clip(_keyMsg, 20, _keyHint.rectTransform.sizeDelta.x)); return; }
            _keyHint.color = Dim;
            _keyHint.Set("CLICK A KEY TO CHANGE IT    GREY KEYS ARE FIXED");
        }

        private void StartCapture(KeyRow row, int si)
        {
            if (_capRow != null || si < 0 || si >= row.B.Slots.Count) return;
            var s = row.B.Slots[si];
            if (s.Set == null)
            {
                _keyMsg = row.B.Why != null ? row.B.Why : row.B.Note == "AXIS" ? "MOUSE AND JOYSTICK AXES ARE SET IN THE GAME'S INPUT FILE" : row.B.Mod ? "THIS KEY IS HARDCODED IN THE MOD (" + row.B.Group + ")" : "THIS KEY IS HARDCODED IN THE GAME";
                _keyMsgBad = false; ShowKeyHint(); return;
            }
            _capRow = row; _capSlot = si; _capSince = Time.unscaledTime; _capMouseUp = !Input.GetMouseButton(0);
            row.Txt[si].Size = Fit("PRESS A KEY", 22, BoxW - 12, 14); row.Txt[si].Set("PRESS A KEY"); row.Txt[si].color = Gold;
            foreach (var im in row.Frames[si]) im.color = Gold;
            // swallow clicks while waiting for the key
            var blk = R(_rootRt, "capture", 0, 0, 1920, 1080);
            blk.anchorMin = Vector2.zero; blk.anchorMax = Vector2.one; blk.sizeDelta = Vector2.zero; blk.anchoredPosition = Vector2.zero;
            var bi = blk.gameObject.AddAccent<Image>(); bi.color = new Color(0, 0, 0, 0); bi.raycastTarget = true;
            _capBlock = blk.gameObject;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _keyMsg = row.B.Label + ": PRESS A KEY    BACKSPACE: CLEAR    ESC: CANCEL" + (s.Shortcut ? "    HOLD CTRL / SHIFT / ALT FOR A COMBINATION" : "");
            _keyMsgBad = false; ShowKeyHint();
        }

        private static KeyCode[] _allKeys;
        private void TickCapture()
        {
            if (_capRow.Txt[_capSlot] == null) { EndCapture(null); return; }
            if (!_capMouseUp && !Input.GetMouseButton(0)) _capMouseUp = true;
            if (Time.unscaledTime - _capSince < .15f) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { EndCapture(null); return; }
            if (Input.GetKeyDown(KeyCode.Backspace)) { EndCapture(KeyCode.None); return; }
            var s = _capRow.B.Slots[_capSlot];
            if (_allKeys == null)
            {
                var l = new List<KeyCode>();
                foreach (KeyCode k in Enum.GetValues(typeof(KeyCode))) if (k != KeyCode.None && !l.Contains(k)) l.Add(k);
                _allKeys = l.ToArray();
            }
            foreach (var k in _allKeys)
            {
                bool mouse = k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6;
                if (mouse && (!s.Mouse || (k == KeyCode.Mouse0 && !_capMouseUp))) continue;
                if (s.Shortcut && KeyCatalog.IsModifier(k)) continue;      // wait for the main key of a combination
                if (k >= KeyCode.JoystickButton0 && !s.Mouse) continue;
                if (Input.GetKeyDown(k)) { EndCapture(k); return; }
            }
            if (s.Shortcut)
            {
                // a modifier pressed and released alone becomes the key
                foreach (var k in Modifiers)
                    if (Input.GetKeyUp(k)) { EndCapture(k); return; }
            }
        }

        private void EndCapture(KeyCode? key)
        {
            var row = _capRow; int si = _capSlot;
            _capRow = null;
            if (_capBlock != null) UnityEngine.Object.Destroy(_capBlock);
            _capBlock = null;
            if (row == null) return;
            var id = KeyId(row.B);
            if (key.HasValue)
            {
                var s = row.B.Slots[si];
                KeyCode[] mods = null;
                if (s.Shortcut && key.Value != KeyCode.None && !KeyCatalog.IsModifier(key.Value))
                {
                    var l = new List<KeyCode>();
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) l.Add(KeyCode.LeftControl);
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) l.Add(KeyCode.LeftShift);
                    if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) l.Add(KeyCode.LeftAlt);
                    mods = l.ToArray();
                }
                string err;
                try { err = s.Set(key.Value, mods); }
                catch (Exception e) { err = "COULD NOT SET THE KEY: " + e.Message.ToUpperInvariant(); }
                _keys = KeyCatalog.Collect();
                if (err != null) { _keyMsg = err; _keyMsgBad = true; }
                else
                {
                    KeySlot ns = null;
                    foreach (var b in _keys) if (KeyId(b) == id && si < b.Slots.Count) ns = b.Slots[si];
                    string kt = ns != null ? (ns.Text ?? KeyCatalog.KeyText(ns.Key)) : KeyCatalog.KeyText(key.Value);
                    if (ns != null && ns.Clash.Count > 0)
                    { _keyMsg = kt + " IS ALSO USED BY " + string.Join(", ", ns.Clash.ToArray()); _keyMsgBad = true; }
                    else { _keyMsg = row.B.Label + ": " + (key.Value == KeyCode.None ? "CLEARED" : kt); _keyMsgBad = false; }
                    CleanHudModule.Log("Keys: " + row.B.Who + " = " + kt + (ns != null && ns.Clash.Count > 0 ? " (also used by " + string.Join(", ", ns.Clash.ToArray()) + ")" : ""));
                }
            }
            else { _keyMsg = null; }
            _keySelect = id;
            _keyScrollY = _keyContent != null ? _keyContent.anchoredPosition.y : 0;
            BuildSettings();
        }

        // keep the selected row in view (keyboard navigation)
        private void TickKeyScroll()
        {
            if (_keyContent == null || _keyView == null) return;
            float vh = _keyView.rect.height, ch = _keyContent.sizeDelta.y;
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != _keyLastSel)
            {
                _keyLastSel = sel;
                if (sel != null && sel.transform.parent == _keyContent && !Input.GetMouseButton(0))
                {
                    var rt = (RectTransform)sel.transform;
                    float top = -rt.anchoredPosition.y, bot = top + rt.sizeDelta.y;
                    var pos = _keyContent.anchoredPosition;
                    if (top - 40 < pos.y) pos.y = top - 40; else if (bot > pos.y + vh) pos.y = bot - vh + 4;
                    pos.y = Mathf.Clamp(pos.y, 0, Mathf.Max(0, ch - vh));
                    _keyContent.anchoredPosition = pos;
                }
            }
        }

        private void SaveSettingsBroadcast()
        {
            _settingsDirty = false;
            if (_ctx == Ctx.Pause) PlayMakerFSM.BroadcastEvent("Save Settings");
        }
    }

    // Esc must not resume the game while one of our sub screens or dialogs is open.
    internal static class CancelHook
    {
        internal static bool Prefix(HutongGames.PlayMaker.Actions.GetButtonDown __instance)
        {
            if (!CleanHudModule.Running || !CleanHudModule.MenuEnabled.Value || (!Core.LoadingProgress.ScreenVisible && Menu.BlockCancelFrame < Time.frameCount)) return true;
            var b = __instance.buttonName;
            return b == null || b.Value != "Cancel";
        }
    }
}
