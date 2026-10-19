using System;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.CleanHUD
{
    // The native sheet owns opening and data. This view borrows its textures and reads live text/FSM variables.
    internal static partial class InventoryStyle
    {
        private sealed class TextMirror { internal Text Source; internal GlyphText Target; internal string Shown; internal bool SourceColor; }
        private sealed class ImageMirror { internal RawImage Source, Target; }
        private sealed class AmmoRow { internal FsmInt Count, Capacity; internal GlyphText Text; internal MenuButton Button; internal int Kind, Shown = int.MinValue, Max = int.MinValue; }
        private sealed class Slot
        {
            internal PlayMakerFSM State, Take; internal FsmGameObject Item; internal bool CanDrop;
            internal MenuButton Button; internal RawImage Image; internal GlyphText Empty, DropHint;
        }
        private static readonly Color White = new Color(.953f, .937f, .894f);
        private static readonly EditorSession Session = new EditorSession();
        private static readonly List<TextMirror> Texts = new List<TextMirror>();
        private static readonly List<ImageMirror> Images = new List<ImageMirror>();
        private static readonly List<AmmoRow> Ammo = new List<AmmoRow>();
        private static readonly List<Slot> Slots = new List<Slot>();
        private static readonly List<Graphic> Native = new List<Graphic>();
        private static readonly List<float> Alphas = new List<float>();
        private static Transform _native;
        private static RectTransform _view, _canvasRect;
        private static Canvas _canvas;
        private static PlayMakerFSM _sheet;
        private static EventSystem _events;
        private static GraphicRaycaster _raycaster;
        private static bool _eventsOn, _raycasterOn, _addedRaycaster, _hidden;
        private static float _lookup;
        private static int _openedFrame;
        private static InsaneSystems.InputManager.KeyAction _key;
        private static GlyphText _hint;
        private static MenuButton _pressed;
        private static KeyCode _shownKey;

        internal static void Tick()
        {
            try { TickCore(); }
            catch { Restore(); throw; }
        }
        private static void TickCore()
        {
            if (!CleanHudModule.Enabled.Value) { if (_native != null || Session.Open) Restore(); return; }
            if (_native == null && Time.unscaledTime >= _lookup)
            {
                _lookup = Time.unscaledTime + 1f;
                _native = SceneCache.FindPath("PlayerSheet_Canvas/PlayerSheet");
                if (_native != null)
                {
                    _canvas = _native.GetComponentInParent<Canvas>(); _canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
                    _sheet = FsmUtil.Find(GameRefs.GameManager, "PlayerSheet");
                    var storage = InsaneSystems.InputManager.InputStorage.Singleton;
                    if (storage != null && storage.Keys != null) foreach (var key in storage.Keys) if (key != null && key.Name == "Player Sheet") { _key = key; break; }
                }
            }
            if (_native == null || _canvas == null || _sheet == null) { if (Session.Open || _view != null) Restore(); return; }
            bool open = _sheet.isActiveAndEnabled && _sheet.ActiveStateName == "on" && _canvas.enabled && GameRefs.InPlay;
            if (!open) { Close(false); return; }
            if (!Session.Open)
            {
                if (!Session.Acquire()) { _sheet.SendEvent("back"); return; }
                _openedFrame = Time.frameCount;
                if (_view == null) Build();
                _events = EventSystem.current;
                if (_events == null) { var t = SceneCache.FindPath("EventSystem"); if (t != null) _events = t.GetComponent<EventSystem>(); }
                if (_events != null) { _eventsOn = _events.enabled; _events.enabled = true; _events.SetSelectedGameObject(null); }
                _raycaster = _canvas.GetComponent<GraphicRaycaster>();
                if (_raycaster == null) { _raycaster = _canvas.gameObject.AddComponent<GraphicRaycaster>(); _addedRaycaster = true; }
                _raycasterOn = _raycaster.enabled; _raycaster.enabled = true;
                _view.gameObject.SetActive(true); SelectTab(false);
                if (Core.Log.Verbose) Core.Log.Info("[CleanHUD] Inventory opened: native data, F3 modal input");
                if (Core.Log.Verbose && ApocasetterBridge.InputBlocked) Core.Log.Info("[CleanHUD] Inventory: equipment pointer owns hover/click while Apocasetter blocks native UI");
            }
            Session.Maintain(); Menu.BlockCancelFrame = Time.frameCount + 2;
            if (Application.isFocused && Time.frameCount > _openedFrame && (Pressed(_key != null ? _key.Key : KeyCode.Tab)
                || (_key != null && Pressed(_key.AlternativeKey)) || Pressed(KeyCode.Escape)))
            { if (_resetDialog != null && _resetDialog.gameObject.activeSelf) HideReset(); else if (_dropDialog != null && _dropDialog.gameObject.activeSelf) HideDrop(); else Close(true); return; }
            float scale = _canvasRect != null ? Mathf.Min(_canvasRect.rect.width / 1920f, _canvasRect.rect.height / 1080f) : 1f;
            var localScale = Vector3.one * Mathf.Max(.1f, scale); if (_view.localScale != localScale) _view.localScale = localScale;
            for (int i = 0; i < Native.Count; i++) if (Native[i] != null && Native[i].canvasRenderer.GetAlpha() != 0) Native[i].canvasRenderer.SetAlpha(0);
            _hidden = true; Refresh(); RefreshExtra();
            EquipmentPointer();
            if (_events != null && _events.currentSelectedGameObject != null && _events.currentSelectedGameObject.transform.IsChildOf(_view)) _events.SetSelectedGameObject(null);
        }
        private static bool Pressed(KeyCode key) { return key != KeyCode.None && Input.GetKeyDown(key); }
        private static void EquipmentPointer()
        {
            if (!ApocasetterBridge.InputBlocked) { _pressed = null; _dragging = false; return; }
            // EditorSession blocks native EventSystem.Update and Button.Press as well as gameplay input.
            // Dispatch only this modal's controls, retaining normal press/release behavior.
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            MenuButton hovered = null;
            foreach (var button in Buttons)
            {
                bool over = Application.isFocused && button.IsActive() && button.IsInteractable()
                    && ModalButton(button)
                    && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, Input.mousePosition, camera);
                button.OnVisual(over || button.Sticky);
                if (over) hovered = button;
            }
            if (!Application.isFocused) { _pressed = null; _dragging = false; return; }
            DropPointer(camera);
            if (Input.GetMouseButtonDown(0)) _pressed = hovered;
            if (Input.GetMouseButtonUp(0))
            {
                var pressed = _pressed; _pressed = null;
                if (pressed != null && ReferenceEquals(pressed, hovered)) pressed.onClick.Invoke();
            }
        }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        { return DialogueWidgets.Rect(parent, name, x, y, w, h); }
        private static GlyphText Label(Transform parent, string text, float x, float y, float w, float h, float size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        { var label = DialogueWidgets.Text(parent, "label", x, y, w, h, size, color, text); label.Align = align; return label; }
        private static RectTransform Panel(Transform parent, string name, float x, float y, float w, float h, string heading)
        {
            var root = Rect(parent, name, x, y, w, h);
            var plate = Rect(root, "plate", -40, -40, w + 80, h + 80).gameObject.AddAccent<Image>();
            plate.sprite = Menu.PlateSprite(); plate.type = Image.Type.Sliced; plate.raycastTarget = false;
            if (!string.IsNullOrEmpty(heading)) Label(root, heading, 20, 7, w - 40, 34, 27, DialogueWidgets.Gold);
            return root;
        }
        private static Text TextAt(string path) { var t = _native.Find(path); return t != null ? t.GetComponent<Text>() : null; }
        private static RawImage ImageAt(string path) { var t = _native.Find(path); return t != null ? t.GetComponent<RawImage>() : null; }
        private static void MirrorText(GlyphText label, string path, bool color = false)
        {
            var source = TextAt(path); if (source != null) Texts.Add(new TextMirror { Source = source, Target = label, SourceColor = color });
        }
        private static RawImage Picture(Transform parent, string path, float x, float y, float w, float h)
        {
            var image = Rect(parent, "picture", x, y, w, h).gameObject.AddAccent<RawImage>(); image.raycastTarget = false;
            var source = ImageAt(path); image.enabled = source != null;
            if (source != null) { image.texture = source.texture; image.uvRect = source.uvRect; Images.Add(new ImageMirror { Source = source, Target = image }); }
            return image;
        }
        private static void Build()
        {
            Native.Clear(); Alphas.Clear(); _native.GetComponentsInChildren(true, Native);
            foreach (var graphic in Native) Alphas.Add(graphic.canvasRenderer.GetAlpha());
            _view = Rect(_canvas.transform, "CleanHUD.Inventory", 0, 0, 1920, 1080);
            _view.anchorMin = _view.anchorMax = _view.pivot = new Vector2(.5f, .5f);
            var intercept = _view.gameObject.AddAccent<Image>(); intercept.color = Color.clear; intercept.raycastTarget = true;
            Panel(_view, "inventory", 136, 132, 1648, 720, "");
            BuildTabs();
            Label(_view, "DAY", 1220, 145, 60, 32, 22, DialogueWidgets.Gold);
            MirrorText(Label(_view, "", 1286, 145, 70, 32, 22, White), "day/day number");
            Label(_view, "TIME", 1450, 145, 70, 32, 22, DialogueWidgets.Gold);
            MirrorText(Label(_view, "", 1536, 145, 150, 32, 22, White), "time/time number");
            var factions = Panel(_inventory, "factions", 164, 202, 340, 292, "FACTIONS");
            Picture(factions, "factions/coyotes_flag", 20, 70, 74, 74);
            Label(factions, "COYOTES", 112, 72, 210, 32, 25, White);
            MirrorText(Label(factions, "FRIENDLY", 112, 108, 210, 30, 23, Color.green), "factions/coyotes_flag/coyotes/coyotes_relation", true);
            Picture(factions, "factions/scrapyard_flag", 20, 178, 74, 74);
            Label(factions, "SCRAPYARD", 112, 178, 210, 32, 25, White);
            MirrorText(Label(factions, "HOSTILE", 112, 214, 210, 30, 23, new Color(.95f, .2f, .1f)), "factions/scrapyard_flag/scrapyard/Text", true);
            var portrait = Panel(_inventory, "survivor", 772, 202, 296, 420, "SURVIVOR");
            Picture(portrait, "player/player_icon", 59, 48, 178, 356);
            Equipment("BINOCULAR", "binocular", "Binocular_Item", 544, 202);
            Equipment("ARMOR", "armor", "Armor_Item", 544, 420);
            Equipment("FLASHLIGHT", "flashlight", "Flashlight_Item", 1088, 202);
            Equipment("BACKPACK", "backpack", "Backpack_Item", 1088, 420);
            BuildAmmo(); BuildWarlords(); BuildPlayers(); BuildDrop(); BuildReset();
            _hint = Label(_view, "", 544, 822, 752, 24, 16, White, TextAnchor.MiddleCenter);
            _shownKey = KeyCode.None; _view.gameObject.SetActive(false);
        }
        private static void Equipment(string title, string path, string slotName, float x, float y)
        {
            var root = Panel(_inventory, path, x, y, 208, 202, title);
            var slot = new Slot(); slot.Image = Picture(root, "player/" + path + "/Image", 44, 48, 120, 120);
            slot.Empty = Label(root, "EMPTY", 20, 85, 168, 42, 22, new Color(.5f, .5f, .47f), TextAnchor.MiddleCenter);
            slot.DropHint = Label(root, "DROP", 108, 172, 80, 24, 18, DialogueWidgets.Gold, TextAnchor.MiddleRight);
            if (path == "armor") { Label(root, "+", 20, 172, 18, 24, 20, White); MirrorText(Label(root, "0", 40, 172, 64, 24, 20, White), "player/armor/armor max"); }
            if (path == "backpack") { Label(root, "+", 20, 172, 18, 24, 20, White); MirrorText(Label(root, "0", 40, 172, 50, 24, 20, White), "player/backpack/backpack max"); Label(root, "%", 90, 172, 24, 24, 20, White); }
            var quick = SceneCache.FindPath("PlayerCameraHolder/PlayerCamera/QuickItems/" + slotName);
            if (quick != null)
            {
                slot.State = FsmUtil.Find(quick.gameObject, "SlotEmptyFull"); slot.Take = FsmUtil.Find(quick.gameObject, "TakeItem");
                if (slot.State != null) slot.Item = slot.State.FsmVariables.FindFsmGameObject("item");
                if (slot.Take != null && slotName == "Binocular_Item") slot.CanDrop = true;
                if (slot.Take != null) foreach (var state in slot.Take.FsmStates) if (state.Name == "dropCurrent") slot.CanDrop = true;
            }
            var hit = Rect(root, "drop", 0, 40, 208, 162).gameObject.AddAccent<Image>(); hit.color = Color.clear;
            slot.Button = hit.gameObject.AddComponent<MenuButton>(); slot.Button.transition = Selectable.Transition.None;
            slot.Button.targetGraphic = hit; slot.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            slot.Button.OnVisual = hover => { hit.color = hover ? new Color(.886f, .749f, .424f, .14f) : Color.clear; slot.DropHint.gameObject.SetActive(hover); };
            slot.Button.onClick.AddListener(() => Drop(slot)); Slots.Add(slot); Buttons.Add(slot.Button); slot.Button.Refresh();
        }
        private static void BuildAmmo()
        {
            var root = Panel(_inventory, "ammo", 1316, 202, 440, 594, "AMMO & SUPPLIES");
            var ammo = FsmUtil.Find(GameRefs.GameManager, "Ammo");
            string[] names = { "ARROW", ".22", "9MM", "20GA", "12GA", "5.56MM", "7.62MM", ".30-06", "GRENADE", "BATTERY", "NUTS", "BANDAGES", "FIRST AID" };
            var keys = MultiplayerInventory.Keys;
            for (int i = 0; i < names.Length; i++)
            {
                float y = 58 + i * 38 + (i >= 8 ? 24 : 0);
                Label(root, names[i], 24, y, 230, 34, 25, White);
                var text = Label(root, "", 264, y, 150, 34, 25, White, TextAnchor.MiddleRight);
                int kind = i;
                var button = Hit(root, 12, y, 416, 36, () => ShowDrop(kind));
                Ammo.Add(new AmmoRow { Text = text, Button = button, Kind = i, Count = MultiplayerInventory.Count(i),
                    Capacity = ammo != null && i < 10 ? ammo.FsmVariables.FindFsmInt("ammo_" + keys[i] + "_capacity") : null });
            }
            var line = Rect(root, "divider", 24, 374, 392, 1).gameObject.AddAccent<Image>(); line.color = DialogueWidgets.Gold; line.raycastTarget = false;
        }
        private static void BuildWarlords()
        {
            var root = Panel(_inventory, "warlords", 544, 658, 752, 154, "WARLORDS");
            string[] keys = { "Alpha_Nightwalker", "Scorpion_King", "Buzzgut", "Terror_of_the_Night", "Duke_Ironjaw", "Professor", "Black_Juggernaut" };
            string[] first = { "ALPHA", "SCORPION", "BUZZGUT", "TERROR OF", "DUKE", "PROFESSOR", "BLACK" };
            string[] second = { "NIGHTWALKER", "KING", "", "THE NIGHT", "IRONJAW", "", "JUGGERNAUT" };
            for (int i = 0; i < keys.Length; i++)
            {
                float x = 18 + i * 103;
                Picture(root, "bosses/" + keys[i] + "/Image", x + 14, 43, 64, 64);
                Label(root, first[i], x - 3, 112, 101, 18, 13, White, TextAnchor.MiddleCenter);
                Label(root, second[i], x - 3, 130, 101, 18, 13, White, TextAnchor.MiddleCenter);
            }
        }
        private static void Refresh()
        {
            foreach (var mirror in Texts) if (mirror.Source != null)
            {
                var text = mirror.Source.text ?? "";
                if (mirror.Shown != text) { mirror.Shown = text; mirror.Target.Set(text.ToUpperInvariant()); }
                if (mirror.SourceColor && mirror.Target.color != mirror.Source.color) mirror.Target.color = mirror.Source.color;
            }
            foreach (var mirror in Images) if (mirror.Source != null)
            {
                if (mirror.Target.texture != mirror.Source.texture) mirror.Target.texture = mirror.Source.texture;
                if (mirror.Target.uvRect != mirror.Source.uvRect) mirror.Target.uvRect = mirror.Source.uvRect;
            }
            foreach (var row in Ammo)
            {
                if (row.Kind >= 11) row.Count = MultiplayerInventory.Count(row.Kind);
                int count = row.Count != null ? row.Count.Value : 0, max = row.Kind >= 11 ? 3 : row.Capacity != null ? row.Capacity.Value : 0;
                row.Button.interactable = count > 0 && Multiplayer.WorldReady && Multiplayer.LocalTargetable && !MultiplayerSaving.Frozen && !SaveBus.Busy;
                if (count == row.Shown && max == row.Max) continue;
                row.Shown = count; row.Max = max; row.Text.Set(count.ToString(CultureInfo.InvariantCulture) + (row.Capacity != null || row.Kind >= 11 ? " / " + max.ToString(CultureInfo.InvariantCulture) : ""));
            }
            foreach (var slot in Slots)
            {
                var item = slot.Item != null ? slot.Item.Value : null;
                bool full = item != null && slot.State != null && item.transform.parent == slot.State.transform;
                bool image = full && slot.Image.texture != null; if (slot.Image.enabled != image) slot.Image.enabled = image;
                if (slot.Empty.gameObject.activeSelf == full) slot.Empty.gameObject.SetActive(!full);
                bool enabled = full && slot.CanDrop;
                if (slot.Button.interactable != enabled) { slot.Button.interactable = enabled; slot.Button.Refresh(); }
            }
            var key = _key != null ? _key.Key : KeyCode.Tab;
            if (key != _shownKey) { _shownKey = key; _hint.Set("CLICK AN ITEM OR SUPPLY TO DROP   /   " + key.ToString().ToUpperInvariant() + " / ESC CLOSE"); }
        }
        private static void Drop(Slot slot)
        {
            if (!Session.Open || !Application.isFocused || !slot.CanDrop || slot.Take == null || slot.State == null || slot.Item == null) return;
            var item = slot.Item.Value; if (item == null || item.transform.parent != slot.State.transform) return;
            bool binocular = slot.State.gameObject.name == "Binocular_Item";
            if (binocular)
            {
                // Binocular TakeItem has no dropCurrent. Use the same detach/ItemDrop/Visibility recipe.
                var camera = slot.State.transform.parent.parent;
                var point = camera.Find("ItemDrop"); var visibility = FsmUtil.Find(item, "Visibility");
                if (point == null || visibility == null) { CleanHudModule.Warn("Inventory: binocular drop needs native ItemDrop and Visibility"); return; }
                if (slot.State.ActiveStateName == "use")
                {
                    var anim = camera.Find("ItemAnim/Binocular Anim");
                    var animation = anim != null ? FsmUtil.Find(anim.gameObject, "Animation") : null;
                    if (animation != null) animation.SendEvent("Deactivate");
                }
                item.transform.SetParent(null, true); item.transform.position = point.position;
                visibility.SendEvent("Activate"); slot.Take.Fsm.SetState("off");
            }
            else
            {
                // Cancel dropCurrent's next-frame takeWeapon continuation.
                slot.Take.Fsm.SetState("dropCurrent"); slot.Take.Fsm.SetState("off");
            }
            if (item.transform.parent == slot.State.transform) { CleanHudModule.Warn("Inventory: native equipment drop did not detach the item"); return; }
            slot.State.Fsm.SetState("empty");
            var capacity = FsmUtil.Find(GameRefs.GameManager, "ArmorBackpack");
            if (slot.State.gameObject.name == "Armor_Item" && capacity != null)
            { var value = capacity.FsmVariables.FindFsmFloat("armor_capacity"); if (value != null) value.Value = 0; }
            if (slot.State.gameObject.name == "Backpack_Item" && capacity != null)
            {
                var value = capacity.FsmVariables.FindFsmFloat("backpack_capacity"); if (value != null) value.Value = 0;
                var backpack = FsmUtil.Find(GameRefs.GameManager, "Backpack");
                if (backpack != null) { var multiply = backpack.FsmVariables.FindFsmFloat("multiply"); if (multiply != null) multiply.Value = 1; backpack.Fsm.SetState("set"); }
            }
            if (slot.State.gameObject.name == "Flashlight_Item")
            { var camera = GameRefs.PlayerCameraHolder != null ? GameRefs.PlayerCameraHolder.transform.Find("PlayerCamera") : null; var light = camera != null ? FsmUtil.Find(camera.gameObject, "Flashlight") : null; if (light != null) { light.Fsm.SetState("off"); light.Fsm.SetState("noFlashlight"); } }
            Refresh();
            if (Core.Log.Verbose) Core.Log.Info("[CleanHUD] Inventory: dropped " + item.name + (binocular ? " through native Visibility" : " through native TakeItem"));
        }
        private static void Close(bool native)
        {
            _pressed = null; HideDrop(); HideReset();
            if (native && _sheet != null && _sheet.ActiveStateName == "on") _sheet.SendEvent("back");
            if (_view != null && _view.gameObject.activeSelf) _view.gameObject.SetActive(false);
            if (_hidden)
            {
                for (int i = 0; i < Native.Count; i++) if (Native[i] != null) Native[i].canvasRenderer.SetAlpha(Alphas[i]);
                _hidden = false;
            }
            if (!Session.Open) return;
            if (_events != null) _events.enabled = _eventsOn;
            if (_raycaster != null) _raycaster.enabled = _raycasterOn;
            Session.Close();
            if (Core.Log.Verbose) Core.Log.Info("[CleanHUD] Inventory closed");
        }
        internal static void Restore()
        {
            Close(Session.Open); if (ReferenceEquals(EditorSession.Owner, Session)) Session.Close(true);
            if (_view != null) UnityEngine.Object.Destroy(_view.gameObject);
            if (_addedRaycaster && _raycaster != null) UnityEngine.Object.Destroy(_raycaster);
            _view = _canvasRect = null; _canvas = null; _native = null; _sheet = null; _events = null; _raycaster = null;
            _addedRaycaster = false; _key = null; _lookup = 0; Texts.Clear(); Images.Clear(); Slots.Clear(); Ammo.Clear(); Native.Clear(); Alphas.Clear();
            ClearExtra();
        }
    }
}
