using System;
using System.Collections.Generic;
using System.Globalization;
using ApiCompass = WildWasteland.Modules.CleanHUD.Api.Compass;
using UnityEngine;
using UnityEngine.UI;
using InsaneSystems.InputManager;

namespace WildWasteland.Modules.CleanHUD
{
    // The HUD itself. Every number below is the approved mockup's geometry in 1920x1080 design units
    // (measured from the mockup DOM), so the in-game result matches it.
    internal sealed class Hud : IDisposable
    {
        static RectTransform _saveCompass;
        internal static Vector2 SaveWarningAnchor()
        {
            if (_saveCompass == null || !_saveCompass.gameObject.activeInHierarchy) return new Vector2(Screen.width / 2f, Screen.height * 120f / 1080f);
            var point = _saveCompass.TransformPoint(new Vector3(_saveCompass.rect.center.x, _saveCompass.rect.yMin, 0));
            var screen = RectTransformUtility.WorldToScreenPoint(null, point);
            return new Vector2(screen.x, Screen.height - screen.y);
        }
        // ---- palette (mockup tokens) ----
        private static readonly Color Gold = C(0xe2, 0xbf, 0x6c, 1f);
        private static readonly Color HudWhite = C(0xf3, 0xef, 0xe4, 1f);
        private static readonly Color DimWhite = C(0xec, 0xe4, 0xce, 1f);
        private static readonly Color BarBorder = new Color(214 / 255f, 203 / 255f, 176 / 255f, .55f);
        private static readonly Color BarBack = new Color(0, 0, 0, .25f);
        private static readonly Color KeyBack = new Color(20 / 255f, 16 / 255f, 10 / 255f, .55f);
        private static readonly Color KeyDark = C(0x17, 0x13, 0x0b, 1f);
        private static readonly Color SepColor = new Color(236 / 255f, 228 / 255f, 206 / 255f, .7f);
        private static readonly Color TickColor = new Color(240 / 255f, 234 / 255f, 218 / 255f, .85f);
        private static Color C(int r, int g, int b, float a) { return new Color(r / 255f, g / 255f, b / 255f, a); }

        private static readonly float[] SlotX = { 0, 86, 172, 344, 516, 688, 860, 1032 };
        private static readonly float[] SlotW = { 64, 64, 150, 150, 150, 150, 150, 150 };
        private static readonly string[] SlotKey = { "F1", "F2", "1", "2", "3", "4", "5", "6" };
        private static readonly string[] SlotAction = { "Use Bandage", "Use First Aid", "Weapon 1", "Weapon 2", "Weapon 3", "Item 4", "Item 5", "Item 6" };
        private static readonly KeyCode[] DefaultSlotKeys = { KeyCode.F1, KeyCode.F2, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6 };
        private static readonly Dictionary<string, Vector2> GroupSize = new Dictionary<string, Vector2> {
            { "compass", new Vector2(1100, 96) }, { "vitals", new Vector2(452, 126) }, { "weapon", new Vector2(356, 156) }, { "slots", new Vector2(666, 96) },
            { "insanity", new Vector2(180, 60) }, { "log", MessageLog.Size } };
        private static readonly string[] Dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private readonly Bindings _src;
        private readonly LayoutFile _layoutFile;
        private readonly GameObject _root;
        private readonly Font _font;
        private readonly Dictionary<string, RectTransform> _groups = new Dictionary<string, RectTransform>();
        private int _appliedRevision = -1;
        private bool _disposed;
        private bool _wasVisible;

        // vitals: two plates, compact (mockup) and "show needs"
        private sealed class Bars
        {
            internal GameObject Root;
            internal RawImage HpFill, ArFill;
            internal Text HpNum, ArNum;
            internal float FillW, FillH;
            internal float HpFrac = -1, ArFrac = -1;
        }
        private Bars _compact, _needs, _bars;
        private Text _boltsNum;
        private int _boltsValue = int.MinValue;
        private RawImage _sanIcon;
        private Text _sanNum;
        private RectTransform _vehicle;
        private readonly VehicleStatusIcon[] _vehicleIcons = new VehicleStatusIcon[3];
        private Text _vehicleVolumeText;
        private bool _vehicleIgnition, _vehicleBrake, _vehiclePlaying;
        private string _vehicleVolume;
        private int _vehicleFrame = -2;
        private float _vehicleVolumeWidth;
        private RectTransform _driving;
        private Text _speed, _rpm, _gear;
        private float _nextDrivingText;
        private readonly RawImage[] _needIcon = new RawImage[3];
        private readonly Text[] _needNum = new Text[3];
        private int _hpValue = int.MinValue, _arValue = int.MinValue, _sanValue = int.MinValue;
        private readonly int[] _needValues = { int.MinValue, int.MinValue, int.MinValue };
        private readonly int[] _charges = { int.MinValue, int.MinValue };
        private WeaponInfo _weaponInfo;
        private readonly List<string> _codexParts = new List<string>();
        private static readonly Dictionary<string, Texture> MarkerIcons = new Dictionary<string, Texture>();
        private bool _needsOn, _numbersOn;
        private float _scaleOn = -1f;
        // weapon
        private RawImage _wIcon, _grenIcon;
        private Text _grenNum;
        // message log (top left) + event texts it takes over
        private MessageLog _log;
        private QuestTracker _quests;
        private string _lastUiText = "";
        private string _lastUiSource;
        private bool _codexWas, _savingWas;
        private int _grenLast = int.MinValue;
        private bool _weaponDirty = true;
        private int _iconRevision = -1;
        private Text _cal, _mag, _res, _dash;
        private Image _sep;
        private GameObject _lastWeapon;
        private string _lastMag, _lastRes, _lastCal;
        // slots
        private readonly RawImage[] _slotIcon = new RawImage[8];
        private readonly Text[] _slotCnt = new Text[2];
        private readonly Image[] _keyBack = new Image[8];
        private readonly Text[] _keyText = new Text[8];
        private readonly CanvasGroup[] _keyGroup = new CanvasGroup[8];
        private readonly RectTransform[,] _keyEdges = new RectTransform[8, 4];
        private readonly KeyCode[] _slotKeys = (KeyCode[])DefaultSlotKeys.Clone(), _shownKeys = new KeyCode[8];
        private readonly GameObject[] _slotItem = new GameObject[6];
        private readonly bool[] _ordinary = new bool[6];
        private readonly Texture[] _slotTexture = new Texture[6];
        private readonly int[] _iconAttempts = new int[6];
        private int _lastSlotCount;
        private float _nextItemIcon;
        private int _lastSel = -2;
        // compass
        private RectTransform _cmp;
        private readonly List<Image> _ticks = new List<Image>();
        private readonly List<Text> _letters = new List<Text>();
        private readonly List<MarkerView> _markers = new List<MarkerView>();
        private RawImage _caret;
        private RectTransform _markerRoot, _insanity;
        private static readonly ShownOrder ByDrawOrder = new ShownOrder();

        private sealed class MarkerView { internal RectTransform Rt; internal RawImage Icon; internal Text Dist; internal CanvasGroup Group; internal int Distance = int.MinValue; internal bool Km; internal float NextText; internal ApiCompass.Entry Entry; }
        private struct Shown { internal ApiCompass.Entry E; internal float Rel, Dist; internal bool Edge; }
        // far first, near last (on top); priority wins over distance
        private sealed class ShownOrder : IComparer<Shown>
        { public int Compare(Shown a, Shown b) { return a.E.Style.Priority != b.E.Style.Priority ? a.E.Style.Priority.CompareTo(b.E.Style.Priority) : b.Dist.CompareTo(a.Dist); } }

        internal bool Valid { get { return !_disposed && _src.Canvas != null && _root != null; } }

        private Hud(Bindings src, LayoutFile layout)
        {
            _src = src; _layoutFile = layout;
            _font = FindFont(src);
            _root = new GameObject("CleanHUD", typeof(RectTransform));
            _src.HudRoot = _root.transform;
            _root.transform.SetParent(src.Canvas.transform, false);
            if (src.DesignUI != null) _root.transform.SetSiblingIndex(src.DesignUI.GetSiblingIndex() + 1);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; rt.pivot = new Vector2(.5f, .5f);
            try
            {
                // Nested canvases (no sorting override, no raycaster: nothing here takes input) keep HUD changes from rebatching
                // the game's whole canvas, and compass motion from rebatching the rest of the HUD. Draw order is unchanged.
                SubCanvas(_root);
                foreach (var id in Layout.Ids) Group(id);
                _insanity = _groups["insanity"];
                SubCanvas(_groups["compass"].gameObject);
                BuildVitals(); BuildWeapon(); BuildSlots(); BuildCompass();
                BuildDriving();
                _quests = new QuestTracker(_root.transform, _font, Gold, HudWhite);
                Canvas.willRenderCanvases += BeforeRender;
            }
            catch { UnityEngine.Object.Destroy(_root); throw; }
        }

        internal static Hud TryCreate(LayoutFile layout)
        {
            var src = Bindings.Find(); if (src == null) return null;
            var hud = new Hud(src, layout);
            CleanHudModule.Log("CleanHUD attached to the game HUD (font: " + (hud._font != null ? hud._font.name : "none") + ").");
            return hud;
        }

        private static Font FindFont(Bindings src)
        {
            if (src.NativeFontSource != null && src.NativeFontSource.font != null) return src.NativeFontSource.font;
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        internal static Transform MapRoot()
        {
            return WildWasteland.Core.Origin.AnchorTransform;
        }

        // ------------------------------------------------------------------ builders
        private void SubCanvas(GameObject go)
        {
            var c = go.AddComponent<Canvas>(); c.overrideSorting = false;
            c.additionalShaderChannels = _src.Canvas.additionalShaderChannels;
        }
        private void Group(string id)
        {
            var go = new GameObject(id, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            var t = (RectTransform)go.transform;
            t.pivot = new Vector2(0, 1); t.sizeDelta = GroupSize[id];
            _groups[id] = t;
        }

        private static RectTransform R(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = (RectTransform)go.transform;
            t.anchorMin = t.anchorMax = new Vector2(0, 1); t.pivot = new Vector2(0, 1);
            t.anchoredPosition = new Vector2(x, -y); t.sizeDelta = new Vector2(w, h);
            return t;
        }
        private static void Place(RectTransform t, float x, float y, float w, float h)
        {
            var p = new Vector2(x, -y); if (t.anchoredPosition != p) t.anchoredPosition = p;
            var s = new Vector2(w, h); if (t.sizeDelta != s) t.sizeDelta = s;
        }

        private static Image Img(Transform p, string n, float x, float y, float w, float h, Color c)
        { var g = R(p, n, x, y, w, h).gameObject.AddAccent<Image>(); g.color = c; g.raycastTarget = false; return g; }

        private static RawImage Raw(Transform p, string n, float x, float y, float w, float h, Texture tex, Color c)
        { var g = R(p, n, x, y, w, h).gameObject.AddAccent<RawImage>(); g.texture = tex; g.color = c; g.raycastTarget = false; return g; }

        private Text Txt(Transform p, string n, float x, float y, float w, float h, int size, TextAnchor a, Color c)
        {
            var t = R(p, n, x, y, w, h).gameObject.AddAccent<Text>();
            t.font = _font; t.fontSize = size; t.alignment = a; t.color = c; t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            return t;
        }
        private static void Drop(Graphic g, float alpha)
        {
            var s = g.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, alpha); s.effectDistance = new Vector2(0, -1);
            var o = g.gameObject.AddComponent<Shadow>(); o.effectColor = new Color(0, 0, 0, alpha * .4f); o.effectDistance = new Vector2(0, -2.5f);
        }
        private static void Frame(Transform p, float x, float y, float w, float h, float b, Color c)
        {
            Img(p, "t", x, y, w, b, c); Img(p, "b", x, y + h - b, w, b, c);
            Img(p, "l", x, y + b, b, h - 2 * b, c); Img(p, "r", x + w - b, y + b, b, h - 2 * b, c);
        }
        private static Texture Glyph(string name) { return Assets.Tex("g_" + name); }

        // HP / ARMOR panel 452x126 (mockup): rows centred at y 34 and 76.
        private void BuildVitals()
        {
            var g = _groups["vitals"];
            var boltTexture = MapIcons.Get("bolt"); Graphic bolt;
            if (boltTexture != null) bolt = Raw(g, "bolts icon", 22, -36, 26, 26, boltTexture, Gold);
            else
            {
                var fallback = R(g, "bolts icon", 22, -36, 52, 52).gameObject.AddAccent<VehicleStatusIcon>();
                fallback.Kind = 3; fallback.color = Gold; fallback.raycastTarget = false; fallback.rectTransform.localScale = new Vector3(.5f, .5f, 1); bolt = fallback;
            }
            var bo = bolt.gameObject.AddComponent<Outline>(); bo.effectColor = Color.black; bo.effectDistance = boltTexture != null ? new Vector2(1, -1) : new Vector2(2, -2);
            _boltsNum = Txt(g, "bolts", 56, -38, 240, 30, 24, TextAnchor.MiddleLeft, HudWhite);
            var bn = _boltsNum.gameObject.AddComponent<Outline>(); bn.effectColor = Color.black; bn.effectDistance = new Vector2(1, -1);
            _compact = new Bars { Root = R(g, "compact", 0, 0, 452, 126).gameObject, FillW = 257f, FillH = 23f };
            var c = _compact.Root.transform;
            Raw(c, "panel", -40, -40, 532, 206, Assets.Tex("panel_vitals"), Color.white);
            for (int i = 0; i < 2; i++)
            {
                float cy = 34 + 42 * i;
                Raw(c, i == 0 ? "hp icon" : "armor icon", 22, cy - 14, 28, 28, Glyph(i == 0 ? "plus" : "shield"), Gold);
                Txt(c, i == 0 ? "HP" : "ARMOR", 62, cy - 15, 96, 30, 24, TextAnchor.MiddleLeft, Gold).text = i == 0 ? "HP" : "ARMOR";
                BuildBar(_compact, c, i, 168, cy, 260, 26);
            }

            // "Show needs": same plate as the weapon (356x156). HP / ARMOR, then water / food / sleep spread evenly on the weapon row's line (y 118).
            _needs = new Bars { Root = R(g, "needs", 0, 0, 356, 156).gameObject, FillW = 183f, FillH = 21f };
            var n = _needs.Root.transform;
            Raw(n, "panel", -40, -40, 436, 236, Assets.Tex("panel_weapon"), Color.white);
            for (int i = 0; i < 2; i++)
            {
                float cy = 34 + 38 * i;
                Raw(n, i == 0 ? "hp icon" : "armor icon", 22, cy - 13, 26, 26, Glyph(i == 0 ? "plus" : "shield"), Gold);
                Txt(n, i == 0 ? "HP" : "ARMOR", 58, cy - 15, 84, 30, 22, TextAnchor.MiddleLeft, Gold).text = i == 0 ? "HP" : "ARMOR";
                BuildBar(_needs, n, i, 146, cy, 186, 24);
            }
            string[] needGlyph = { "water", "food", "sleep" };
            for (int i = 0; i < 3; i++)
            {
                _needIcon[i] = Raw(n, needGlyph[i] + " icon", 0, 118 - 12, 24, 24, Glyph(needGlyph[i]), Gold);
                _needNum[i] = Txt(n, needGlyph[i], 0, 118 - 15, 60, 30, 22, TextAnchor.MiddleLeft, HudWhite);
            }
            _needs.Root.SetActive(false);
            _bars = _compact;
            BuildInsanity();
            BuildVehicleStatus();
            _log = new MessageLog(_groups["log"], _font, Gold, HudWhite);
        }

        // Insanity: top-right of the screen, no plate, shown only while insanity is above 0. Number right-aligned, icon to its left.
        private void BuildInsanity()
        {
            var g = _groups["insanity"];
            _sanIcon = Raw(g, "insanity icon", 0, 4, 52, 52, Glyph("sanity"), Gold); Drop(_sanIcon, .9f);
            _sanNum = Txt(g, "insanity", 60, 0, 120, 60, 40, TextAnchor.MiddleRight, HudWhite);
            var s = _sanNum.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, .95f); s.effectDistance = new Vector2(0, -1.5f);
            var o = _sanNum.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .45f); o.effectDistance = new Vector2(1, 1);
            g.gameObject.SetActive(false);
        }

        private void BuildVehicleStatus()
        {
            _vehicle = R(_root.transform, "vehicle status", 0, 0, 180, 60);
            for (int i = 0; i < 3; i++)
            {
                var icon = R(_vehicle, i == 0 ? "ignition" : i == 1 ? "handbrake" : "cassette", 0, 4, 52, 52).gameObject.AddAccent<VehicleStatusIcon>();
                icon.Kind = i; icon.color = Gold; icon.raycastTarget = false; Drop(icon, .9f);
                _vehicleIcons[i] = icon;
            }
            _vehicleVolumeText = Txt(_vehicle, "volume", 0, 0, 120, 60, 40, TextAnchor.MiddleLeft, HudWhite);
            Drop(_vehicleVolumeText, .9f);
            _vehicle.gameObject.SetActive(false);
        }

        private void BuildDriving()
        {
            _driving = R(_groups["weapon"], "driving", 0, -58, 356, 28);
            _speed = Txt(_driving, "speed", 0, 0, 110, 28, 20, TextAnchor.MiddleLeft, HudWhite); Drop(_speed, .9f);
            _rpm = Txt(_driving, "RPM", 114, 0, 122, 28, 20, TextAnchor.MiddleCenter, Gold); Drop(_rpm, .9f);
            _gear = Txt(_driving, "gear", 240, 0, 116, 28, 20, TextAnchor.MiddleRight, HudWhite); Drop(_gear, .9f);
            _driving.gameObject.SetActive(false);
        }

        private void TickDriving()
        {
            bool visible = ChaseCameraHud.Refresh();
            if (_driving.gameObject.activeSelf && !visible) ChaseCameraHud.TraceHidden();
            if (_driving.gameObject.activeSelf != visible) _driving.gameObject.SetActive(visible);
            if (!visible) return;
            float scale = ChaseCameraHud.Scale;
            var ls = new Vector3(scale, scale, 1); if (_driving.localScale != ls) _driving.localScale = ls;
            var ap = new Vector2(356 - 356 * scale, 40 + 8 * scale + ChaseCameraHud.Gap); if (_driving.anchoredPosition != ap) _driving.anchoredPosition = ap;
            var color = ChaseCameraHud.Fraction >= .9f ? new Color(.95f, .22f, .12f) : Gold;
            _rpm.color = color;
            if (Time.unscaledTime >= _nextDrivingText)
            {
                _nextDrivingText = Time.unscaledTime + .1f;
                _speed.text = Mathf.RoundToInt(ChaseCameraHud.Speed).ToString(CultureInfo.InvariantCulture) + " KM/H";
                _rpm.text = (Mathf.RoundToInt(ChaseCameraHud.Rpm / 10) * 10).ToString(CultureInfo.InvariantCulture) + " RPM";
                _gear.text = "GEAR " + ChaseCameraHud.Gear;
            }
            ChaseCameraHud.HideNative();
        }

        internal void SetVehicleStatus(bool ignitionOff, bool handbrakeOn, bool playing, string volume)
        {
            _vehicleIgnition = ignitionOff; _vehicleBrake = handbrakeOn; _vehiclePlaying = playing;
            _vehicleVolume = volume; _vehicleFrame = Time.frameCount;
        }

        private void TickVehicleStatus()
        {
            bool visible = WildWasteland.Core.GameRefs.InGame && _vehicleFrame >= Time.frameCount - 1 && (_vehicleIgnition || _vehicleBrake || _vehiclePlaying);
            if (_vehicle.gameObject.activeSelf != visible) _vehicle.gameObject.SetActive(visible);
            if (!visible) return;
            if (_vehicleIcons[0].enabled != _vehicleIgnition) _vehicleIcons[0].enabled = _vehicleIgnition;
            if (_vehicleIcons[1].enabled != _vehicleBrake) _vehicleIcons[1].enabled = _vehicleBrake;
            if (_vehicleIcons[2].enabled != _vehiclePlaying) _vehicleIcons[2].enabled = _vehiclePlaying;
            if (_vehicleVolumeText.enabled != _vehiclePlaying) _vehicleVolumeText.enabled = _vehiclePlaying;
            if (_vehiclePlaying && _vehicleVolumeText.text != (_vehicleVolume ?? ""))
            {
                _vehicleVolumeText.text = _vehicleVolume ?? "";
                _vehicleVolumeWidth = Mathf.Ceil(_vehicleVolumeText.preferredWidth);
            }
            float right = _insanity.gameObject.activeSelf ? _sanIcon.rectTransform.anchoredPosition.x - 24f : 180f;
            if (_vehiclePlaying)
            {
                right -= _vehicleVolumeWidth;
                Place(_vehicleVolumeText.rectTransform, right, 0, _vehicleVolumeWidth, 60);
                right -= 62f; Place(_vehicleIcons[2].rectTransform, right, 4, 52, 52); right -= 24f;
            }
            if (_vehicleBrake) { right -= 52f; Place(_vehicleIcons[1].rectTransform, right, 4, 52, 52); right -= 24f; }
            if (_vehicleIgnition) { right -= 52f; Place(_vehicleIcons[0].rectTransform, right, 4, 52, 52); }
        }

        // Bar with 1.5 px border; fill and the optional centred value sit inside the border.
        private void BuildBar(Bars b, Transform p, int row, float x, float cy, float w, float h)
        {
            float ix = x + 1.5f, iy = cy - h / 2f + 1.5f, iw = w - 3f, ih = h - 3f;
            Img(p, "bar back", ix, iy, iw, ih, BarBack);
            var fill = Raw(p, "fill", ix, iy, iw, ih, Assets.VGradient(row == 0 ? .75f : .72f), Color.white);
            Frame(p, x, cy - h / 2f, w, h, 1.5f, BarBorder);
            // 22 px = the needs values; the bar's inner height (21-23) holds it, and 3-4 digits never reach the bar ends.
            var num = Txt(p, "value", ix, iy, iw, ih, 22, TextAnchor.MiddleCenter, HudWhite);
            var o = num.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .85f); o.effectDistance = new Vector2(1, -1);
            num.enabled = false;
            if (row == 0) { b.HpFill = fill; b.HpNum = num; } else { b.ArFill = fill; b.ArNum = num; }
        }

        // Weapon panel 356x156: icon right-aligned in 200x64 at (132,18); row centred at y 118.
        private void BuildWeapon()
        {
            var g = _groups["weapon"];
            Raw(g, "panel", -40, -40, 436, 236, Assets.Tex("panel_weapon"), Color.white);
            _wIcon = Raw(g, "icon", 132, 18, 200, 64, null, Gold);
            _wIcon.enabled = false;
            // grenades: top-left corner of the plate
            _grenIcon = Raw(g, "grenade icon", 24, 22, 30, 30, Glyph("grenade"), Gold);
            _grenNum = Txt(g, "grenades", 60, 22, 70, 30, 24, TextAnchor.MiddleLeft, HudWhite);
            Img(g, "cal bar", 26, 107, 7, 22, Gold);
            _cal = Txt(g, "ammo type", 43, 103, 220, 30, 24, TextAnchor.MiddleLeft, Gold);
            _mag = Txt(g, "mag", 0, 78, 200, 80, 64, TextAnchor.MiddleRight, HudWhite);
            _sep = Img(g, "sep", 0, 96, 2, 44, SepColor);
            _res = Txt(g, "reserve", 0, 93, 160, 50, 36, TextAnchor.MiddleLeft, HudWhite);
            _dash = Txt(g, "melee", 132, 98, 200, 40, 30, TextAnchor.MiddleRight, new Color(HudWhite.r, HudWhite.g, HudWhite.b, .6f));
            _dash.text = "—";
        }

        // F1 F2 1 2 3, no background: icon box 52 high, key badge 34x34 at y 62.
        private void BuildSlots()
        {
            var g = _groups["slots"];
            for (int i = 0; i < 8; i++)
            {
                float x = SlotX[i], w = SlotW[i];
                RawImage ic;
                if (i < 2) ic = Raw(g, SlotKey[i] + " icon", x + 12, 6, 40, 40, Glyph(i == 0 ? "bandage" : "medkit"), Gold);
                else ic = Raw(g, SlotKey[i] + " icon", x, 0, w, 52, null, Gold);
                Drop(ic, .85f);
                _slotIcon[i] = ic;
                if (i < 2)
                {
                    var cnt = Txt(g, SlotKey[i] + " count", x + 36, -13, 40, 30, 20, TextAnchor.MiddleRight, HudWhite);
                    var sh = cnt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, .8f); sh.effectDistance = new Vector2(0, -1);
                    _slotCnt[i] = cnt;
                }
                var key = R(g, SlotKey[i] + " key", x, 62, w, 34);
                _keyGroup[i] = key.gameObject.AddComponent<CanvasGroup>();
                _keyGroup[i].blocksRaycasts = false; _keyGroup[i].interactable = false;
                var kt = Txt(key, "label", 0, 0, w, 34, 20, TextAnchor.MiddleCenter, Gold);
                kt.text = SlotKey[i];
                float kw = Mathf.Max(34f, Mathf.Ceil(kt.preferredWidth) + 16f);
                float kx = (w - kw) / 2f;
                _keyBack[i] = Img(key, "back", kx, 0, kw, 34, KeyBack);
                Frame(key, kx, 0, kw, 34, 2f, Gold);
                _keyEdges[i, 0] = (RectTransform)key.Find("t"); _keyEdges[i, 1] = (RectTransform)key.Find("b");
                _keyEdges[i, 2] = (RectTransform)key.Find("l"); _keyEdges[i, 3] = (RectTransform)key.Find("r");
                kt.transform.SetAsLastSibling();
                Place((RectTransform)kt.transform, kx, 0, kw, 34);
                _keyText[i] = kt;
                _shownKeys[i] = DefaultSlotKeys[i];
                if (i >= 5) { key.gameObject.SetActive(false); ic.enabled = false; }
            }
        }

        // Strip compass 1100x96: line at y 30 (3 px), labels from y 40, markers from y 13. Visible arc from layout (160 deg).
        private void BuildCompass()
        {
            _cmp = _groups["compass"];
            _saveCompass = _cmp;
            Raw(_cmp, "line shadow", 0, 25.5f, 1100, 16, Assets.CompassShadow(), Color.white);
            Raw(_cmp, "line", 0, 30, 1100, 3, Assets.CompassLine(), Color.white);
            for (int i = 0; i < 24; i++) Tick(i).enabled = false;
            for (int i = 0; i < 8; i++) Letter(i).enabled = false;
            // Markers draw above the strip in pool order (= sorted order), the caret above them; no per-frame sibling moves.
            _markerRoot = R(_cmp, "markers", 0, 0, 0, 0);
            _markerRoot.anchorMin = Vector2.zero; _markerRoot.anchorMax = Vector2.one; _markerRoot.offsetMin = _markerRoot.offsetMax = Vector2.zero;
            _caret = Raw(_cmp, "caret", 543, 16, 14, 9, Assets.Caret(), Gold);
        }
        // Ticks/letters added later (wider arc) go below the markers, as before.
        private void BelowMarkers(Transform t) { if (_markerRoot != null) t.SetSiblingIndex(_markerRoot.GetSiblingIndex()); }

        private Image Tick(int i)
        {
            while (_ticks.Count <= i)
            {
                var t = Img(_cmp, "tick", 0, 0, 2, 6, TickColor);
                var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .3f); o.effectDistance = new Vector2(.75f, .75f);
                BelowMarkers(t.transform); _ticks.Add(t);
            }
            return _ticks[i];
        }
        private Text Letter(int i)
        {
            while (_letters.Count <= i)
            {
                var t = Txt(_cmp, "dir", 0, 0, 100, 30, 26, TextAnchor.MiddleCenter, HudWhite);
                var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, .95f); s.effectDistance = new Vector2(0, -1);
                var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .45f); o.effectDistance = new Vector2(1, 1);
                BelowMarkers(t.transform); _letters.Add(t);
            }
            return _letters[i];
        }
        private MarkerView Mark(int i)
        {
            while (_markers.Count <= i)
            {
                var rt = R(_markerRoot, "marker", 0, 0, 160, 70);
                var mv = new MarkerView { Rt = rt, Group = rt.gameObject.AddComponent<CanvasGroup>() };
                mv.Group.blocksRaycasts = false; mv.Group.interactable = false;
                mv.Icon = Raw(rt, "icon", 62, 0, 36, 36, null, Color.white); Drop(mv.Icon, .9f);
                mv.Dist = Txt(rt, "distance", 0, 31.5f, 160, 30, 21, TextAnchor.MiddleCenter, HudWhite);
                var s = mv.Dist.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, .95f); s.effectDistance = new Vector2(0, -1);
                var o = mv.Dist.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .45f); o.effectDistance = new Vector2(1, 1);
                _markers.Add(mv);
            }
            return _markers[i];
        }

        // ------------------------------------------------------------------ layout
        private Vector2 SizeOf(string id) { return id == "vitals" && _needsOn ? new Vector2(356, 156) : GroupSize[id]; }

        // Each group is anchored to its nearest screen edge and scales around that point (pivot = anchor),
        // so a bigger HUD grows away from the edge instead of off the screen.
        private void ApplyLayout(Layout l)
        {
            float global = Mathf.Clamp(CleanHudModule.Scale.Value, 0.5f, 1.5f);
            foreach (var id in Layout.Ids)
            {
                var p = l.Elements[id]; var size = SizeOf(id); var t = _groups[id];
                float x = p.CenterX ? Mathf.Round(960f - size.x / 2f) : p.X, y = p.Y;
                if (id == "vitals" && _needsOn) y = p.Y + 126f - 156f - 8f; // bottom level with the weapon plate
                float w = size.x * p.Scale, h = size.y * p.Scale;
                float cx = x + w / 2f, cy = y + h / 2f;
                float ax = cx < 640 ? 0f : cx > 1280 ? 1f : .5f;
                float ay = cy < 432 ? 0f : cy > 648 ? 1f : .5f;
                t.sizeDelta = size;
                t.anchorMin = t.anchorMax = new Vector2(ax, 1f - ay);
                t.pivot = new Vector2(ax, 1f - ay);
                float px = x + ax * w, py = y + ay * h;
                t.anchoredPosition = new Vector2(px - ax * 1920f, -(py - ay * 1080f));
                t.localScale = new Vector3(p.Scale * global, p.Scale * global, 1f);
            }
            var insanity = _groups["insanity"];
            _vehicle.anchorMin = insanity.anchorMin; _vehicle.anchorMax = insanity.anchorMax; _vehicle.pivot = insanity.pivot;
            _vehicle.anchoredPosition = insanity.anchoredPosition; _vehicle.sizeDelta = insanity.sizeDelta; _vehicle.localScale = insanity.localScale;
            _compact.Root.SetActive(!_needsOn); _needs.Root.SetActive(_needsOn);
            _bars = _needsOn ? _needs : _compact;
            foreach (var b in new[] { _compact, _needs })
            {
                b.HpFill.color = l.HpColor; b.ArFill.color = l.ArmorColor; b.HpFrac = -1; b.ArFrac = -1;
                b.HpNum.enabled = _numbersOn; b.ArNum.enabled = _numbersOn;
            }
            _hpValue = _arValue = _sanValue = int.MinValue;
            for (int i = 0; i < 3; i++) _needValues[i] = int.MinValue;
            _charges[0] = _charges[1] = int.MinValue;
            _wIcon.color = l.IconTint;
            for (int i = 0; i < 2; i++) _slotIcon[i].color = l.IconTint;
            _lastSel = -2; _weaponDirty = true; _lastCal = null; _lastMag = null; _lastRes = null; _grenLast = int.MinValue;
            for (int i = 0; i < 6; i++) { _slotItem[i] = null; _slotTexture[i] = null; _iconAttempts[i] = 0; }
            _lastSlotCount = 0;
            _caret.enabled = l.HeadingCaret;
        }

        // ------------------------------------------------------------------ per frame
        private float _nextScan;
        internal void Tick()
        {
            if (_iconRevision != Assets.IconRevision) { _iconRevision = Assets.IconRevision; _lastSel = -2; _weaponDirty = true; }
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 1f; _src.Refresh(); RefreshSlotKeys(); }
            bool visible = _src.Visible;
            if (_root.activeSelf != visible) _root.SetActive(visible);
            MessageHook.Active = visible;
            if (!visible) { ChaseCameraHud.TraceState(_rpm, false); if (_wasVisible) _src.Restore(); _wasVisible = false; return; }
            _wasVisible = true;
            _src.RefreshHiddenIfNeeded();
            bool panels = !Binoculars.Raised;
            if (_groups["vitals"].gameObject.activeSelf != panels) _groups["vitals"].gameObject.SetActive(panels);
            if (_groups["weapon"].gameObject.activeSelf != panels) _groups["weapon"].gameObject.SetActive(panels);
            if (_groups["slots"].gameObject.activeSelf != panels) _groups["slots"].gameObject.SetActive(panels);
            var l = _layoutFile.Current;
            bool needs = CleanHudModule.ShowNeeds.Value, nums = CleanHudModule.ShowHealthNumbers.Value; float sc = CleanHudModule.Scale.Value;
            if (_appliedRevision != _layoutFile.Revision || needs != _needsOn || nums != _numbersOn || !Mathf.Approximately(sc, _scaleOn))
            {
                _appliedRevision = _layoutFile.Revision; _needsOn = needs; _numbersOn = nums; _scaleOn = sc;
                ApplyLayout(l);
            }
            try { TickVitals(); } catch (Exception e) { CleanHudModule.Throttled("vitals", e); }
            _quests.Tick(_insanity, _cmp);
            TickVehicleStatus();
            TickDriving();
            ChaseCameraHud.TraceState(_rpm, true);
            _src.Pocket.Read();
            int sel = _src.Selected;
            try { TickSlots(l, sel); } catch (Exception e) { CleanHudModule.Throttled("slots", e); }
            try { TickWeapon(sel); } catch (Exception e) { CleanHudModule.Throttled("weapon", e); }
            try { TickCompass(l); } catch (Exception e) { CleanHudModule.Throttled("compass", e); }
            try { TickEvents(); _log.Tick(); } catch (Exception e) { CleanHudModule.Throttled("log", e); }
            // The vanilla hide runs in willRenderCanvases (BeforeRender), after this tick in the same frame.
        }

        private void TickVitals()
        {
            if (_src.Money != null && _src.Money.Value != _boltsValue) { _boltsValue = _src.Money.Value; _boltsNum.text = _boltsValue.ToString(CultureInfo.InvariantCulture); }
            float hpV = Bindings.Number(_src.Health), arV = Bindings.Number(_src.Armor), max = Bindings.Number(_src.ArmorMax);
            float hp = Mathf.Clamp01(hpV / 100f);
            float ar = max > 0 ? Mathf.Clamp01(arV / max) : 0f;
            var b = _bars;
            if (!Mathf.Approximately(hp, b.HpFrac)) { b.HpFrac = hp; b.HpFill.rectTransform.sizeDelta = new Vector2(b.FillW * hp, b.FillH); b.HpFill.enabled = hp > 0; }
            if (!Mathf.Approximately(ar, b.ArFrac)) { b.ArFrac = ar; b.ArFill.rectTransform.sizeDelta = new Vector2(b.FillW * ar, b.FillH); b.ArFill.enabled = ar > 0; }
            if (_numbersOn)
            {
                int hs = Mathf.CeilToInt(hpV), a = Mathf.RoundToInt(arV);
                if (hs != _hpValue) { _hpValue = hs; b.HpNum.text = hs.ToString(CultureInfo.InvariantCulture); }
                if (a != _arValue) { _arValue = a; b.ArNum.text = a.ToString(CultureInfo.InvariantCulture); }
            }
            // insanity (top right of the screen) while above 0
            float sanV = CleanHudModule.ShowInsanity.Value ? Bindings.Number(_src.Sanity) : 0f;
            var sg = _insanity.gameObject;
            if (sg.activeSelf != sanV > 0f) sg.SetActive(sanV > 0f);
            if (sanV > 0f)
            {
                int s = Mathf.CeilToInt(sanV);
                if (s != _sanValue)
                {
                    _sanValue = s; _sanNum.text = s.ToString(CultureInfo.InvariantCulture);
                    float w = Mathf.Ceil(_sanNum.preferredWidth);
                    Place(_sanIcon.rectTransform, 180f - w - 10f - 52f, 4f, 52f, 52f);
                }
            }
            if (!_needsOn) return;
            // water / food / sleep: three equal columns across the bar width (x 22..332), each item centred in its column
            bool flash = Time.unscaledTime % 1f < .5f;
            for (int i = 0; i < 3; i++)
            {
                int v = Mathf.RoundToInt(Bindings.Number(i == 0 ? _src.Thirst : i == 1 ? _src.Hunger : _src.Fatigue));
                var color = v >= 80 && flash ? (v > 90 ? Core.UI.Skin.Red : Core.UI.Skin.Yellow) : HudWhite;
                _needNum[i].color = color;
                _needIcon[i].color = v >= 80 ? color : Gold;
                if (v == _needValues[i]) continue;
                _needValues[i] = v; _needNum[i].text = v.ToString(CultureInfo.InvariantCulture);
                float nw = Mathf.Ceil(_needNum[i].preferredWidth), iw = 24f + 6f + nw;
                float centre = 22f + 310f * (2 * i + 1) / 6f, left = Mathf.Round(centre - iw / 2f);
                Place(_needIcon[i].rectTransform, left, 106f, 24f, 24f);
                Place(_needNum[i].rectTransform, left + 30f, 103f, Mathf.Max(nw, 60f), 30f);
            }
        }

        private void TickSlots(Layout l, int sel)
        {
            for (int i = 0; i < 2; i++)
            {
                int n = Bindings.Charges(i == 0 ? _src.Bandage : _src.FirstAid);
                if (_charges[i] != n) { _charges[i] = n; _slotCnt[i].text = n.ToString(CultureInfo.InvariantCulture); }
                var c = l.IconTint; c.a = n > 0 ? 1f : .3f; _slotIcon[i].color = c;
            }
            int count = _src.SlotCount;
            bool geometry = count != _lastSlotCount, changed = sel != _lastSel || geometry;
            bool retry = Time.unscaledTime >= _nextItemIcon && Core.GameRefs.InGame;
            if (retry) _nextItemIcon = Time.unscaledTime + .5f;
            for (int k = 0; k < 6; k++)
            {
                var item = k < count ? _src.Item(k) : null; bool ordinary = _src.Ordinary(k);
                bool content = !ReferenceEquals(item, _slotItem[k]) || ordinary != _ordinary[k];
                if (content) { _slotItem[k] = item; _ordinary[k] = ordinary; _slotTexture[k] = null; _iconAttempts[k] = 0; changed = true; _weaponDirty = true; }
                if (item != null && (!ordinary && changed || ordinary && _slotTexture[k] == null && _iconAttempts[k] < 20 && (content || retry)))
                {
                    var tex = ordinary ? ApocapocketInventory.ItemIcon(item) : Assets.WeaponIcon(item);
                    if (ordinary) _iconAttempts[k]++;
                    if (tex != _slotTexture[k]) { _slotTexture[k] = tex; changed = true; _weaponDirty = true; }
                }
            }
            for (int i = 0; i < count + 2; i++) if (geometry || _shownKeys[i] != _slotKeys[i])
            { LayoutSlotKey(i, count); changed = true; }
            if (!changed) return;
            _lastSel = sel; _lastSlotCount = count;
            for (int k = 0; k < 6; k++)
            {
                int i = k + 2; var item = _slotItem[k]; bool on = sel == k && (item != null || _src.Pocket.Active);
                var ri = _slotIcon[i];
                _keyGroup[i].gameObject.SetActive(k < count);
                var tex = _slotTexture[k];
                ri.enabled = k < count && tex != null;
                if (tex != null)
                {
                    ri.texture = tex;
                    Fit(ri.rectTransform, tex, SlotLeft(i, count) + 11f, 4f, SlotWidth(i, count) - 22f, 44f, false);
                    var col = on ? l.IconTint : (l.InactiveTint ? l.IconTint : DimWhite);
                    col.a = on ? 1f : .62f * .55f;
                    ri.color = col;
                }
                _keyGroup[i].alpha = item == null ? .45f : 1f;
                _keyBack[i].color = on ? Gold : KeyBack;
                _keyText[i].color = on ? KeyDark : Gold;
            }
        }

        private static float SlotLeft(int i, int count) { return i < 2 ? SlotX[i] : 172f + (i - 2) * 516f / count; }
        private static float SlotWidth(int i, int count) { return i < 2 ? SlotW[i] : 516f / count - 22f; }
        private void RefreshSlotKeys()
        {
            var st = InputStorage.Singleton;
            for (int i = 0; i < 8; i++)
            {
                _slotKeys[i] = DefaultSlotKeys[i];
                if (st == null || st.Keys == null) continue;
                foreach (var a in st.Keys) if (a != null && a.Name == SlotAction[i]) { _slotKeys[i] = a.Key; break; }
            }
        }
        private void LayoutSlotKey(int i, int count)
        {
            float w = SlotWidth(i, count); var kt = _keyText[i];
            _shownKeys[i] = _slotKeys[i]; kt.text = KeyCatalog.KeyText(_slotKeys[i]); kt.fontSize = 20;
            float desired = Mathf.Max(34f, Mathf.Ceil(kt.preferredWidth) + 16f);
            if (desired > w) kt.fontSize = Mathf.Max(8, Mathf.FloorToInt(20f * (w - 16f) / Mathf.Max(1f, kt.preferredWidth)));
            float kw = Mathf.Min(w, Mathf.Max(34f, Mathf.Ceil(kt.preferredWidth) + 16f)), x = (w - kw) / 2f;
            Place((RectTransform)_keyGroup[i].transform, SlotLeft(i, count), 62, w, 34);
            Place(_keyBack[i].rectTransform, x, 0, kw, 34); Place(kt.rectTransform, x, 0, kw, 34);
            Place(_keyEdges[i, 0], x, 0, kw, 2); Place(_keyEdges[i, 1], x, 32, kw, 2);
            Place(_keyEdges[i, 2], x, 2, 2, 30); Place(_keyEdges[i, 3], x + kw - 2, 2, 2, 30);
        }

        // Fit a texture into a box keeping its aspect; centred, or right-aligned when rightAlign.
        private static void Fit(RectTransform t, Texture tex, float bx, float by, float bw, float bh, bool rightAlign)
        {
            float s = Mathf.Min(bw / tex.width, bh / tex.height);
            float w = Mathf.Round(tex.width * s), h = Mathf.Round(tex.height * s);
            float x = rightAlign ? bx + bw - w : bx + (bw - w) / 2f;
            Place(t, x, by + (bh - h) / 2f, w, h);
        }

        private void TickWeapon(int sel)
        {
            var item = sel >= 0 ? _src.Item(sel) : null;
            if (!ReferenceEquals(item, _lastWeapon) || _weaponDirty)
            {
                _lastWeapon = item; _weaponDirty = false; _lastMag = null; _lastRes = null;
                _weaponInfo = item == null ? null : _src.Ordinary(sel) ? new WeaponInfo("", "ITEM", "item") : Assets.Describe(item, Bindings.ItemName(item));
                // bare hands (holstered, or an empty slot drawn) = fist
                var tex = item == null ? Glyph("fist") : _src.Ordinary(sel) ? _slotTexture[sel] : Assets.WeaponIcon(item);
                _wIcon.enabled = tex != null;
                if (tex != null) { _wIcon.texture = tex; Fit(_wIcon.rectTransform, tex, 132, 18, 200, 64, true); }
            }
            var info = _weaponInfo;
            int gren = _src.Grenades != null ? Mathf.Max(0, _src.Grenades.Value) : 0;
            if (gren != _grenLast)
            {
                _grenLast = gren; _grenNum.text = gren.ToString(CultureInfo.InvariantCulture);
                var gc = _wIcon.color; gc.a = gren > 0 ? 1f : .45f; _grenIcon.color = gc;
                var tc = HudWhite; tc.a = gren > 0 ? 1f : .45f; _grenNum.color = tc;
            }
            string cal = info == null ? "UNARMED" : info.Caliber;
            if (cal != _lastCal) { _lastCal = cal; _cal.text = cal; }
            bool gun = info != null && info.Kind == "gun", thrown = info != null && info.Kind == "thrown", melee = info != null && info.Kind == "melee";
            _dash.enabled = melee;
            _sep.enabled = gun; _res.enabled = gun; _mag.enabled = gun || thrown;
            if (!gun && !thrown) return;
            string mag = thrown ? "1" : Bindings.Digits(_src.AmmoInWeapon);
            string res = thrown ? "" : Bindings.Digits(_src.AmmoReserve);
            if (mag.Length == 0) mag = "0";
            if (gun && res.Length == 0) res = "0";
            if (mag == _lastMag && res == _lastRes) return;
            _lastMag = mag; _lastRes = res;
            _mag.text = mag; _res.text = res;
            float right = 332f;
            if (gun)
            {
                float resW = Mathf.Max(70f, Mathf.Ceil(_res.preferredWidth));
                float resX = 332f - resW;
                Place(_res.rectTransform, resX, 93, Mathf.Max(resW, 160f), 50);
                float sepX = resX - 14f - 2f;
                Place(_sep.rectTransform, sepX, 96, 2, 44);
                right = sepX - 14f;
            }
            Place(_mag.rectTransform, right - 200f, 78, 200, 80);
        }

        private readonly List<Shown> _shown = new List<Shown>();
        private void TickCompass(Layout l)
        {
            bool show = CleanHudModule.ShowCompass.Value;
            if (_cmp.gameObject.activeSelf != show) _cmp.gameObject.SetActive(show);
            if (!show) return;
            var cam = WildWasteland.Core.GameRefs.MainCamera;
            Transform eye = cam != null ? cam.transform : _src.PlayerCamera;
            if (eye == null) return;
            float heading = eye.eulerAngles.y;
            float chaseHeading;
            if (ChaseCameraHud.TryHeading(out chaseHeading)) heading = chaseHeading;
            float arc = l.VisibleArc, half = arc / 2f, ppd = 1100f / arc, cx = 550f;
            int ti = 0, li = 0;
            for (int d = 0; d < 360; d += 15)
            {
                float rel = Mathf.DeltaAngle(heading, d);
                if (Mathf.Abs(rel) > half + 2f) continue;
                float x = cx + rel * ppd;
                bool is90 = d % 90 == 0, is45 = d % 45 == 0;
                float fade = Assets.Fade(x / 1100f);
                float len = is90 ? 14f : is45 ? 10f : 6f;
                var t = Tick(ti++);
                if (!t.enabled) t.enabled = true;
                Place(t.rectTransform, x - 1f, is45 ? 30f - len : 33f, 2f, len);
                Fade(t, fade);   // colour stays TickColor; the renderer alpha fades without a mesh rebuild
                if (is90 || (is45 && l.EightPoint))
                {
                    var txt = Letter(li++);
                    if (!txt.enabled) txt.enabled = true;
                    int size = is90 ? 26 : 19;
                    if (txt.fontSize != size) txt.fontSize = size;
                    var s = Dirs[d / 45]; if (txt.text != s) txt.text = s;
                    float cy = 40f + .81f * size / 2f;
                    Place(txt.rectTransform, x - 50f, cy - 15f, 100f, 30f);
                    Fade(txt, fade * (is90 ? 1f : .8f));
                }
            }
            for (int i = ti; i < _ticks.Count; i++) if (_ticks[i].enabled) _ticks[i].enabled = false;
            for (int i = li; i < _letters.Count; i++) if (_letters[i].enabled) _letters[i].enabled = false;

            // markers from the API
            _shown.Clear();
            var eyePos = eye.position;
            var entries = ApiCompass.Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                Vector3 pos;
                if (!Resolve(e, out pos)) { if (e.HasTarget && e.Target == null) entries.RemoveAt(i); continue; }
                var dv = pos - eyePos;
                float dist = dv.magnitude;
                if (e.Style.MaxDistance > 0 && dist > e.Style.MaxDistance * (e.Owner == "WildWasteland.Locations" || e.Owner == "WildWasteland.VehicleGroups" ? VantageDiscovery.Scale : 1f)) continue;
                float bearing = Mathf.Atan2(dv.x, dv.z) * Mathf.Rad2Deg;
                float rel = Mathf.DeltaAngle(heading, bearing);
                bool edge = false;
                if (Mathf.Abs(rel) > half)
                {
                    if (!(l.PinOffArc || e.Style.PinToEdge)) continue;
                    rel = Mathf.Sign(rel) * half; edge = true;
                }
                _shown.Add(new Shown { E = e, Rel = rel, Dist = dist, Edge = edge });
            }
            _shown.Sort(ByDrawOrder);
            int mi = 0; float now = Time.unscaledTime;
            for (int si = 0; si < _shown.Count; si++)
            {
                var s = _shown[si];
                bool ambiguous = s.E.Owner == "WildWasteland.VehicleGroups" ? CleanHudModule.AmbiguousPatrols.Value && !s.E.Identified
                    : s.E.Owner == "WildWasteland.Locations" && CleanHudModule.AmbiguousLocations.Value && !LocationMarkers.Known(s.E.Target);
                var tex = IconFor(s.E, ambiguous && s.E.Owner == "WildWasteland.Locations" ? "unknown" : s.E.Style.Icon);
                if (tex == null) continue;
                var mv = Mark(mi++);
                if (!mv.Rt.gameObject.activeSelf) { mv.Rt.gameObject.SetActive(true); mv.Distance = int.MinValue; }
                float x = cx + s.Rel * ppd;
                Place(mv.Rt, x - 80f, 13f, 160f, 70f);
                mv.Icon.texture = tex; mv.Icon.color = ambiguous ? Color.white : s.E.Style.Color;
                float alpha = s.Edge ? .55f : 1f; if (mv.Group.alpha != alpha) mv.Group.alpha = alpha;
                if (mv.Dist.enabled != s.E.Style.ShowDistance) mv.Dist.enabled = s.E.Style.ShowDistance;
                if (s.E.Style.ShowDistance)
                {
                    // The text (Shadow + Outline mesh) is rewritten at most 5 times a second per view, or at once when the view
                    // takes another marker; values and format are unchanged.
                    bool km = s.Dist >= 1000f;
                    int distance = Mathf.RoundToInt(km ? s.Dist / 100f : s.Dist);
                    if ((mv.Distance != distance || mv.Km != km || !ReferenceEquals(mv.Entry, s.E)) && (now >= mv.NextText || !ReferenceEquals(mv.Entry, s.E)))
                    {
                        mv.Distance = distance; mv.Km = km; mv.NextText = now + .2f;
                        mv.Dist.text = string.IsNullOrEmpty(s.E.Style.Label) ? DistanceText(distance, km) : s.E.Style.Label + "\n" + DistanceText(distance, km);
                        mv.Dist.fontSize = string.IsNullOrEmpty(s.E.Style.Label) ? 21 : 16;
                        mv.Dist.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, string.IsNullOrEmpty(s.E.Style.Label) ? 30 : 48);
                    }
                }
                mv.Entry = s.E;
            }
            for (int i = mi; i < _markers.Count; i++) if (_markers[i].Rt.gameObject.activeSelf) { _markers[i].Rt.gameObject.SetActive(false); _markers[i].Entry = null; }
        }

        private static void Fade(Graphic g, float alpha) { var r = g.canvasRenderer; if (r.GetAlpha() != alpha) r.SetAlpha(alpha); }

        // "428 m" / "1.9 km" strings, built once per value.
        private static readonly Dictionary<int, string> MetreText = new Dictionary<int, string>(), KmText = new Dictionary<int, string>();
        private static string DistanceText(int distance, bool km)
        {
            var cache = km ? KmText : MetreText; string text;
            if (cache.TryGetValue(distance, out text)) return text;
            text = km ? (distance / 10f).ToString("0.0", CultureInfo.InvariantCulture) + " km" : distance.ToString(CultureInfo.InvariantCulture) + " m";
            if (cache.Count < 4096) cache[distance] = text;
            return text;
        }

        private static bool Resolve(ApiCompass.Entry e, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (e.HasTarget) { if (e.Target == null) return false; pos = e.Target.position; return true; }
            if (e.Anchored) { var r = MapRoot(); if (r == null) return false; pos = r.position + e.MapLocal; return true; }
            if (e.Getter != null && !e.Broken)
            {
                try { pos = e.Getter(); return true; }
                catch (Exception ex) { e.Broken = true; CleanHudModule.Warn("Compass marker " + e.Id + " (" + e.Owner + ") position callback threw; marker hidden: " + ex.Message); }
            }
            return false;
        }

        private static Texture IconFor(ApiCompass.Entry e, string key)
        {
            if (ReferenceEquals(e.IconKey, key) && e.IconRevision == ApiCompass.IconRevision && e.Icon != null) return e.Icon;
            e.IconKey = key; e.IconRevision = ApiCompass.IconRevision; e.Icon = IconFor(key); return e.Icon;
        }
        private static Texture IconFor(string key)
        {
            if (string.IsNullOrEmpty(key)) key = "diamond";
            Texture2D t;
            if (ApiCompass.CustomIcons.TryGetValue(key, out t) && t != null) return t;
            Texture icon;
            if (!MarkerIcons.TryGetValue(key, out icon)) { icon = MapIcons.Get(key) ?? Assets.Tex("g_" + key.ToLowerInvariant()) ?? MapIcons.Get("diamond"); MarkerIcons[key] = icon; }
            return icon;
        }

        // Event texts that are not FSM timed messages: Canvas/UIText changes, the codex unlock card, the "Saving Game" banner.
        private void TickEvents()
        {
            var ut = _src.UIText;
            string source = ut != null && ut.gameObject.activeInHierarchy ? (ut.text ?? "") : "";
            if (source != _lastUiSource)
            {
                _lastUiSource = source;
                string s = source.Trim();
                if (s.Length > 0 && s != _lastUiText) MessageHook.Push(s);
                _lastUiText = s;
            }
            bool codex = _src.Codex != null && _src.Codex.gameObject.activeInHierarchy;
            if (codex && !_codexWas)
            {
                var parts = _codexParts; parts.Clear();
                foreach (var t in _src.CodexTexts) { if (t == null) continue; var x = (t.text ?? "").Trim(); if (x.Length > 0 && !parts.Contains(x)) parts.Add(x); }
                MessageHook.Push(parts.Count > 0 ? string.Join(" - ", parts.ToArray()) : "New content unlocked in codex");
            }
            _codexWas = codex;
            bool saving = _src.Saving != null && _src.Saving.gameObject.activeInHierarchy;
            if (saving && !_savingWas) MessageHook.Push("Saving game");
            _savingWas = saving;
        }

        private void BeforeRender() { if (Valid && _root.activeSelf) _src.Hide(); }

        public void Dispose()
        {
            if (_saveCompass == _cmp) _saveCompass = null;
            if (_disposed) return; _disposed = true;
            MessageHook.Active = false;
            if (_quests != null) _quests.Dispose();
            Canvas.willRenderCanvases -= BeforeRender;
            _src.Restore();
            MarkerIcons.Clear(); ApiCompass.IconRevision++;
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }
}
