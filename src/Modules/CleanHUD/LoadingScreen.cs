using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using WildWasteland.Core;

namespace WildWasteland.Modules.CleanHUD
{
    internal sealed class LoadingScreen : IDisposable
    {
        private sealed class ScreenImage { internal string Name; internal Texture2D Texture; internal DateTime Stamp; }
        private readonly string _dir = Path.Combine(Core.Assets.Root, "LoadingScreens");
        private readonly Dictionary<string, ScreenImage> _images = new Dictionary<string, ScreenImage>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ScreenImage> _screens = new List<ScreenImage>();
        private readonly System.Random _random = new System.Random();
        private Dictionary<string, string> _tips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly GlyphText[] _tipLines = new GlyphText[8];
        private static readonly string[] Labels = { "LOADING.", "LOADING..", "LOADING..." };
        private static readonly string[] SavingLabels = { "SAVING.", "SAVING..", "SAVING..." };
        private static readonly string[] AutosavingLabels = { "AUTOSAVING.", "AUTOSAVING..", "AUTOSAVING..." };
        private GameObject _root;
        private RawImage _background;
        private GlyphText _label;
        private GlyphText _stage;
        private RectTransform _progressFill;
        private float _nextProgress, _shownProgress;
        private Transform _nativeGame, _nativeTitle, _nativeSlots;
        private ScreenImage _current;
        private string _currentTip = "";
        private int _width, _height;
        private float _nextFind;
        private bool _completed;
        private bool _saving, _autosaving, _saveFinished;
        private bool _nativeLocated;
        private bool _serverSaving;
        internal bool Active { get; private set; }

        internal LoadingScreen()
        {
            try { Reload(); Build(); SceneLoaded(); }
            catch { Dispose(); throw; }
        }

        private void Reload()
        {
            try
            {
                Directory.CreateDirectory(_dir);
                string config = Path.Combine(_dir, "screens.json");
                if (!File.Exists(config))
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CleanHUD.LoadingScreens.screens.json"))
                    using (var reader = new StreamReader(stream)) File.WriteAllText(config, reader.ReadToEnd());
                var root = Json.Parse(File.ReadAllText(config)) as Dictionary<string, object>;
                var entries = Json.Arr(root, "screens");
                if (entries == null) throw new FormatException("screens.json needs a screens array");
                var tips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    var item = entry as Dictionary<string, object>;
                    string image = Json.Text(item, "image", "");
                    if (ScreenNumber(image) > 0) tips[image] = Json.Text(item, "text", "");
                }
                _tips = tips;
            }
            catch (Exception e) { CleanHudModule.Warn("Loading screen text: " + e.Message); }
            try
            {
                string[] files = Directory.GetFiles(_dir, "*.png");
                Array.Sort(files, CompareScreens);
                _screens.Clear();
                foreach (string path in files)
                {
                    string name = Path.GetFileName(path);
                    if (ScreenNumber(name) <= 0) continue;
                    ScreenImage image;
                    if (!_images.TryGetValue(name, out image)) { image = new ScreenImage { Name = name }; _images.Add(name, image); }
                    DateTime stamp = File.GetLastWriteTimeUtc(path);
                    if (image.Texture == null || image.Stamp != stamp)
                    {
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "CleanHUD.Loading." + name, hideFlags = HideFlags.DontUnloadUnusedAsset, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                        try
                        {
                            if (!texture.LoadImage(File.ReadAllBytes(path), true)) throw new FormatException("PNG could not be decoded");
                            if (image.Texture != null) UnityEngine.Object.Destroy(image.Texture);
                            image.Texture = texture; image.Stamp = stamp; texture = null;
                        }
                        catch (Exception e) { CleanHudModule.Warn("Loading image " + name + ": " + e.Message); }
                        finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
                    }
                    if (image.Texture != null) _screens.Add(image);
                }
            }
            catch (Exception e) { CleanHudModule.Warn("Loading images: " + e.Message); }
        }

        private static int ScreenNumber(string path)
        {
            string name = Path.GetFileName(path);
            if (name.Length <= 10 || !name.StartsWith("Screen", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return 0;
            int number;
            return int.TryParse(name.Substring(6, name.Length - 10), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0 ? number : 0;
        }
        private static int CompareScreens(string a, string b) { return ScreenNumber(a).CompareTo(ScreenNumber(b)); }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static GlyphText Text(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var text = Rect(parent, name, anchor, position, size).gameObject.AddAccent<GlyphText>();
            text.Size = fontSize; text.Align = TextAnchor.UpperLeft; text.color = color; text.raycastTarget = false;
            var shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .95f); shadow.effectDistance = new Vector2(0, -2);
            return text;
        }
        private void Build()
        {
            _root = new GameObject("CleanHUD.Loading", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
            var scaler = _root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _root.AddComponent<GraphicRaycaster>();
            var black = Rect(_root.transform, "opaque background", Vector2.zero, Vector2.zero, Vector2.zero).gameObject.AddAccent<Image>();
            Stretch(black.rectTransform); black.color = Color.black;
            _background = Rect(_root.transform, "screen image", Vector2.zero, Vector2.zero, Vector2.zero).gameObject.AddAccent<RawImage>();
            Stretch(_background.rectTransform); _background.raycastTarget = false;
            _label = Text(_root.transform, "loading", Vector2.zero, new Vector2(144, 140), new Vector2(720, 80), 56, new Color(.886f, .749f, .424f, 1f));
            var progress = Rect(_root.transform, "progress", new Vector2(.5f, 0), new Vector2(0, 64), new Vector2(1632, 28));
            var frame = progress.gameObject.AddAccent<Image>(); frame.color = DialogueWidgets.Gold; frame.raycastTarget = false;
            var inside = Rect(progress, "track", Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(inside); inside.offsetMin = new Vector2(2, 2); inside.offsetMax = new Vector2(-2, -2);
            var track = inside.gameObject.AddAccent<Image>(); track.color = new Color(0, 0, 0, .88f); track.raycastTarget = false;
            _progressFill = Rect(inside, "fill", Vector2.zero, Vector2.zero, Vector2.zero);
            _progressFill.anchorMax = new Vector2(0, 1); _progressFill.offsetMin = new Vector2(3, 3); _progressFill.offsetMax = new Vector2(-3, -3);
            var fill = _progressFill.gameObject.AddAccent<Image>(); fill.color = DialogueWidgets.Gold; fill.raycastTarget = false;
            _stage = Text(_root.transform, "loading stage", Vector2.zero, new Vector2(144, 100), new Vector2(1632, 26), 22, DialogueWidgets.Gold);
            for (int i = 0; i < _tipLines.Length; i++)
                _tipLines[i] = Text(_root.transform, "tip " + i, new Vector2(0, 1), new Vector2(144, -108 - 44 * i), new Vector2(1100, 44), 30, new Color(.953f, .937f, .894f, 1f));
            _root.SetActive(false);
        }

        internal void Begin(bool saving = false, bool autosaving = false)
        {
            if (Active || !CleanHudModule.Running || (!CleanHudModule.MenuEnabled.Value && !MultiplayerSaving.Frozen)) return;
            LoadingProgress.Begin();
            Reload();
            _saving = saving; _autosaving = autosaving; _saveFinished = false;
            _nextProgress = _shownProgress = 0f;
            _current = _screens.Count > 0 ? _screens[_random.Next(_screens.Count)] : null;
            _currentTip = "";
            if (_current != null) _tips.TryGetValue(_current.Name, out _currentTip);
            if (_root == null) Build();
            ShowScreen(); UpdateProgress(); _completed = false; Active = true; _root.SetActive(true);
            var events = UnityEngine.EventSystems.EventSystem.current; if (events != null) events.SetSelectedGameObject(null);
            CleanHudModule.Log((_autosaving ? "Autosaving " : _saving ? "Saving " : "Loading ") + (_current != null ? _current.Name : "(no Screen*.png found)"));
        }
        private void ShowScreen()
        {
            _background.texture = _current != null ? _current.Texture : null;
            _background.enabled = _background.texture != null;
            _width = _height = 0;
            _label.Set((_autosaving ? AutosavingLabels : _saving ? SavingLabels : Labels)[0]);
            foreach (var line in _tipLines) { line.Set(""); line.gameObject.SetActive(false); }
            int index = 0; var current = new StringBuilder(); var font = GlyphFont.Get();
            foreach (string paragraph in (_currentTip ?? "").Replace("\r", "").Split('\n'))
            {
                foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (current.Length > 0 && font.Measure(candidate, 30, 0) > 1100)
                    {
                        if (index == _tipLines.Length) return;
                        _tipLines[index].Set(current.ToString()); _tipLines[index++].gameObject.SetActive(true); current.Length = 0;
                    }
                    if (current.Length > 0) current.Append(' '); current.Append(word);
                }
                if (current.Length > 0 && index < _tipLines.Length) { _tipLines[index].Set(current.ToString()); _tipLines[index++].gameObject.SetActive(true); current.Length = 0; }
            }
        }
        internal void End()
        {
            Active = false; _completed = false; _saving = _autosaving = _saveFinished = false;
            LoadingProgress.ScreenVisible = false;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }
        internal void EndSaving() { if (_autosaving) End(); }
        private void SaveFinished() { _saveFinished = true; }
        internal void AfterEnterState(HutongGames.PlayMaker.Fsm fsm, HutongGames.PlayMaker.FsmState state)
        {
            if (!CleanHudModule.Running || state == null || state.Name != "clicked" || fsm.Name != "Continue") return;
            var go = fsm.GameObject; if (go == null || go.transform.parent == null) return;
            if ((go.name == "savegame" && go.transform.parent.name == "SaveGame") || (go.name == "Yes_Save" && go.transform.parent.name == "Really Save")) Begin(true);
        }
        internal void StateChanged(string previous, string current)
        {
            if (current == "Start" || current == "setSeed" || current == "LoadGame" || current == "generateTerrain" || current == "generateTerrain 2") Begin();
            else if (current == "wait" || current == "SaveGame") Begin(true);
            else if (current == "isPlay") { _completed = true; if (_saving && !_autosaving) SaveBus.AfterSave(SaveFinished); }
        }
        internal void SceneLoaded()
        {
            if (Active && _root == null) { Build(); ShowScreen(); UpdateProgress(); _root.SetActive(true); }
            _nativeGame = _nativeTitle = _nativeSlots = null; _nativeLocated = false; _nextFind = 0;
            FindNative();
            if (Active && SceneCache.FindPath("Canvas_MM") != null && !NativeLoading()) End();
            if (NativeLoading()) Begin();
        }
        private void FindNative()
        {
            if (_nativeLocated && (_nativeGame != null || _nativeTitle != null || _nativeSlots != null)) return;
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + .5f;
            if (_nativeGame == null) _nativeGame = SceneCache.FindPath("Canvas/LoadingScreen");
            if (_nativeTitle == null) _nativeTitle = SceneCache.FindPath("Canvas_MM/LoadingText");
            if (_nativeSlots == null) _nativeSlots = SceneCache.FindPath("LoadGame_Canvas/LoadGame/LoadingText");
            _nativeLocated = _nativeGame != null || _nativeTitle != null || _nativeSlots != null;
        }
        private static bool Visible(Transform root) { return root != null && root.gameObject.activeInHierarchy; }
        private bool NativeLoading() { return Visible(_nativeGame) || Visible(_nativeTitle) || Visible(_nativeSlots); }
        internal void Tick()
        {
            if (MultiplayerSaving.Frozen)
            {
                _serverSaving = true;
                if (!Active) Begin(true);
                _saving = true; _saveFinished = false;
            }
            else if (_serverSaving) { _serverSaving = false; End(); }
            if (_serverSaving) { DrawSaving(); return; }
            if (!CleanHudModule.Running || !CleanHudModule.MenuEnabled.Value) { End(); return; }
            FindNative();
            bool native = NativeLoading();
            if (!Active && native) Begin();
            if (!Active) return;
            if (_saving && !_autosaving && _saveFinished && !ApocasaverLoading.SaveActive) { CleanHudModule.Log("Saving complete; menus unlocked"); End(); return; }
            if (!_saving && _completed && !native) { End(); return; }
            if (_root == null) { Build(); ShowScreen(); _root.SetActive(true); }
            var labels = _autosaving ? AutosavingLabels : _saving ? SavingLabels : Labels;
            _label.Set(labels[(int)(Time.unscaledTime * 2f) % labels.Length]);
            if (Time.unscaledTime >= _nextProgress) { _nextProgress = Time.unscaledTime + .2f; UpdateProgress(); }
            if (_background.texture != null && (_width != Screen.width || _height != Screen.height))
            {
                _width = Screen.width; _height = Screen.height;
                float imageAspect = _background.texture.width / (float)_background.texture.height, screenAspect = _width / (float)Mathf.Max(1, _height);
                if (screenAspect > imageAspect) { float h = imageAspect / screenAspect; _background.uvRect = new UnityEngine.Rect(0, (1 - h) / 2, 1, h); }
                else { float w = screenAspect / imageAspect; _background.uvRect = new UnityEngine.Rect((1 - w) / 2, 0, w, 1); }
            }
        }
        private void DrawSaving()
        {
            if (_root == null) { Build(); ShowScreen(); _root.SetActive(true); }
            _label.Set(SavingLabels[(int)(Time.unscaledTime * 2f) % SavingLabels.Length]);
            _stage.Set("Server is saving the world");
            _progressFill.gameObject.SetActive(false);
        }
        private void UpdateProgress()
        {
            float fraction; string detail;
            LoadingProgress.Read(_saving, out fraction, out detail);
            if (_saving && SaveBus.State == "isPlay") { fraction = _completed ? .95f : .02f; detail = _completed ? "Finishing save" : "Preparing save"; }
            _shownProgress = Mathf.Max(_shownProgress, fraction);
            _stage.Set(detail);
            _progressFill.anchorMax = new Vector2(_shownProgress, 1);
            _progressFill.gameObject.SetActive(_shownProgress > .005f);
        }
        public void Dispose()
        {
            End(); if (_root != null) UnityEngine.Object.Destroy(_root); _root = null;
            foreach (var image in _images.Values) if (image.Texture != null) UnityEngine.Object.Destroy(image.Texture);
            _images.Clear(); _screens.Clear();
        }
    }
}
