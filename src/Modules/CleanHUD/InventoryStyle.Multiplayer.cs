using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using WildWasteland.Core;

namespace WildWasteland.Modules.CleanHUD
{
    internal static partial class InventoryStyle
    {
        sealed class PlayerLine
        {
            internal RectTransform Root; internal Image Color;
            internal GlyphText Name, Vitals, Bolts, Position, Vehicle;
            internal MenuButton Reset; internal MultiplayerRoster.Player Player;
        }
        sealed class PlayerList
        {
            internal GlyphText Heading, PageLabel; internal MenuButton Previous, Next;
            internal int Page; internal bool Sleeping;
            internal readonly List<MultiplayerRoster.Player> Players = new List<MultiplayerRoster.Player>();
            internal readonly List<PlayerLine> Lines = new List<PlayerLine>();
        }
        static readonly List<MenuButton> Buttons = new List<MenuButton>();
        static RectTransform _inventory, _playersView, _dropDialog, _dropBar, _dropFill;
        static MenuButton _inventoryTab, _playersTab;
        static GlyphText _dropTitle, _dropAmount;
        static PlayerList _activePlayers, _sleepingPlayers;
        static bool _playersSelected, _dragging;
        static int _dropKind = -1, _dropQuantity, _dropMax;
        static float _nextPlayers;
        static RectTransform _resetDialog;
        static GlyphText _resetTitle, _resetName, _resetAction, _resetWarning, _resetStatus;
        static MenuButton _resetConfirm;
        static MultiplayerRoster.Player _resetTarget;

        static bool ModalButton(MenuButton button)
        {
            if (_resetDialog != null && _resetDialog.gameObject.activeSelf) return button.transform.IsChildOf(_resetDialog);
            return _dropDialog == null || !_dropDialog.gameObject.activeSelf || button.transform.IsChildOf(_dropDialog);
        }
        static void BuildReset()
        {
            _resetDialog = Rect(_view, "reset confirmation", 0, 0, 1920, 1080);
            var shade = _resetDialog.gameObject.AddAccent<Image>(); shade.color = new Color(0, 0, 0, .8f);
            var panel = Panel(_resetDialog, "confirm reset", 500, 340, 920, 380, "");
            _resetTitle = Label(panel, "", 30, 16, 860, 44, 28, DialogueWidgets.Gold, TextAnchor.MiddleCenter);
            _resetName = Label(panel, "", 30, 64, 860, 40, 26, White, TextAnchor.MiddleCenter);
            _resetAction = Label(panel, "", 30, 124, 860, 36, 23, White, TextAnchor.MiddleCenter);
            _resetWarning = Label(panel, "", 30, 164, 860, 36, 21, White, TextAnchor.MiddleCenter);
            _resetStatus = Label(panel, "", 30, 204, 860, 36, 20, White, TextAnchor.MiddleCenter);
            ActionButton(panel, "CANCEL", 110, 282, 260, 52, HideReset);
            _resetConfirm = ActionButton(panel, "RESET", 550, 282, 260, 52, ConfirmReset);
            _resetDialog.gameObject.SetActive(false);
        }
        static void ShowReset(MultiplayerRoster.Player player)
        {
            if (!Session.Open || !Application.isFocused || !MultiplayerPlayerReset.CanReset(player)) return;
            HideDrop(); _pressed = null; _resetTarget = player;
            _resetTitle.Set(player.Sleeping ? "REMOVE SLEEPING PLAYER?" : "RESET PLAYER?");
            Fitted(_resetName, player.Name + "   (" + player.Position.ToString("F1") + ")", 26);
            _resetAction.Set(player.Sleeping ? "Remove this sleeping body from the world?" : "Move this player to the starting location?");
            _resetWarning.Set("Saved inventory, stats and respawn selection will be kept.");
            _resetStatus.Set(player.Sleeping ? "They can rejoin with their saved character." : "A seated player will leave their vehicle.");
            _resetConfirm.interactable = true; _resetDialog.gameObject.SetActive(true);
        }
        static void HideReset()
        { if (_resetDialog != null) _resetDialog.gameObject.SetActive(false); _resetTarget = null; }
        static void ConfirmReset()
        {
            if (!Session.Open || !Application.isFocused || _resetTarget == null) return;
            if (MultiplayerPlayerReset.Reset(_resetTarget)) { HideReset(); _nextPlayers = 0; }
            else { _resetStatus.Set("Player changed or reset is busy. Cancel and try again."); _resetConfirm.interactable = false; }
        }

        static MenuButton Hit(Transform parent, float x, float y, float w, float h, Action action)
        {
            var image = Rect(parent, "hit", x, y, w, h).gameObject.AddAccent<Image>(); image.color = Color.clear;
            var button = image.gameObject.AddComponent<MenuButton>(); button.transition = Selectable.Transition.None;
            button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.OnVisual = over => image.color = over ? new Color(.886f, .749f, .424f, .14f) : Color.clear;
            button.onClick.AddListener(() => action()); Buttons.Add(button); return button;
        }
        static MenuButton ActionButton(Transform parent, string text, float x, float y, float w, float h, Action action)
        {
            var button = Hit(parent, x, y, w, h, action);
            Label(button.transform, text, 0, 0, w, h, 23, DialogueWidgets.Gold, TextAnchor.MiddleCenter); return button;
        }
        static void BuildTabs()
        {
            _inventory = Rect(_view, "inventory content", 0, 0, 1920, 1080);
            _inventoryTab = ActionButton(_view, "INVENTORY", 156, 139, 200, 40, () => SelectTab(false));
            _playersTab = ActionButton(_view, "PLAYERS", 370, 139, 190, 40, () => SelectTab(true));
        }
        static void SelectTab(bool players)
        {
            HideDrop(); HideReset(); _pressed = null; _playersSelected = players; _nextPlayers = 0;
            _inventory.gameObject.SetActive(!players); _playersView.gameObject.SetActive(players);
            _inventoryTab.Sticky = !players; _playersTab.Sticky = players;
            _inventoryTab.Refresh(); _playersTab.Refresh();
        }
        static void BuildDrop()
        {
            _dropDialog = Rect(_view, "drop quantity", 0, 0, 1920, 1080);
            var shade = _dropDialog.gameObject.AddAccent<Image>(); shade.color = new Color(0, 0, 0, .8f);
            var panel = Panel(_dropDialog, "quantity", 570, 350, 780, 340, "");
            _dropTitle = Label(panel, "", 30, 20, 720, 44, 28, DialogueWidgets.Gold, TextAnchor.MiddleCenter);
            _dropAmount = Label(panel, "", 30, 78, 720, 40, 26, White, TextAnchor.MiddleCenter);
            _dropBar = Rect(panel, "quantity slider", 120, 146, 540, 36);
            var track = _dropBar.gameObject.AddAccent<Image>(); track.color = new Color(.2f, .2f, .18f);
            _dropFill = Rect(_dropBar, "fill", 0, 0, 540, 36);
            var fill = _dropFill.gameObject.AddAccent<Image>(); fill.color = DialogueWidgets.Gold; fill.raycastTarget = false;
            var drag = _dropBar.gameObject.AddComponent<BarDrag>(); drag.OnValue = SetDropFraction;
            ActionButton(panel, "-", 40, 138, 64, 50, () => SetDropQuantity(_dropQuantity - 1));
            ActionButton(panel, "+", 676, 138, 64, 50, () => SetDropQuantity(_dropQuantity < _dropMax ? _dropQuantity + 1 : _dropMax));
            ActionButton(panel, "CANCEL", 80, 242, 260, 52, HideDrop);
            ActionButton(panel, "DROP", 440, 242, 260, 52, ConfirmDrop);
            _dropDialog.gameObject.SetActive(false);
        }
        static void ShowDrop(int kind)
        {
            if (!Session.Open || !Application.isFocused || !Multiplayer.WorldReady || MultiplayerSaving.Frozen || SaveBus.Busy || !Multiplayer.LocalTargetable) return;
            var count = MultiplayerInventory.Count(kind); if (count == null || count.Value < 1) return;
            _dropKind = kind; _dropMax = count.Value; _dropTitle.Set("DROP " + (kind == 12 ? "FIRST AID" : kind == 11 ? "BANDAGES" : kind == 10 ? "NUTS" : MultiplayerInventory.Keys[kind].ToUpperInvariant()));
            _dropDialog.gameObject.SetActive(true); SetDropQuantity(1);
        }
        static void HideDrop() { if (_dropDialog != null) _dropDialog.gameObject.SetActive(false); _dropKind = -1; _dragging = false; }
        static void SetDropFraction(float fraction)
        { SetDropQuantity((int)Math.Min(_dropMax, Math.Round(1d + (_dropMax - 1d) * Mathf.Clamp01(fraction)))); }
        static void SetDropQuantity(int quantity)
        {
            _dropQuantity = Mathf.Clamp(quantity, 1, Mathf.Max(1, _dropMax));
            _dropAmount.Set(_dropQuantity.ToString(CultureInfo.InvariantCulture) + " / " + _dropMax.ToString(CultureInfo.InvariantCulture));
            _dropFill.sizeDelta = new Vector2(_dropMax <= 1 ? 540 : 12 + 528f * (_dropQuantity - 1f) / (_dropMax - 1f), 36);
        }
        static void ConfirmDrop()
        {
            if (_dropKind < 0 || !Session.Open || !Application.isFocused) return;
            if (MultiplayerInventory.Drop(_dropKind, _dropQuantity)) { HideDrop(); Refresh(); }
            else _dropAmount.Set("UNABLE TO DROP - SUPPLIES RETAINED");
        }
        static void DropPointer(Camera camera)
        {
            if (_dropKind < 0) { _dragging = false; return; }
            if (Input.GetMouseButtonDown(0)) _dragging = RectTransformUtility.RectangleContainsScreenPoint(_dropBar, Input.mousePosition, camera);
            if (_dragging && Input.GetMouseButton(0))
            {
                Vector2 point;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_dropBar, Input.mousePosition, camera, out point))
                    SetDropFraction((point.x - _dropBar.rect.xMin) / _dropBar.rect.width);
            }
            if (Input.GetMouseButtonUp(0)) _dragging = false;
        }
        static void BuildPlayers()
        {
            _playersView = Rect(_view, "players content", 164, 202, 1592, 600);
            _activePlayers = BuildPlayerList(0, false); _sleepingPlayers = BuildPlayerList(302, true);
            _playersView.gameObject.SetActive(false);
        }
        static PlayerList BuildPlayerList(float y, bool sleeping)
        {
            var list = new PlayerList { Sleeping = sleeping };
            var root = Rect(_playersView, sleeping ? "sleeping players" : "active players", 0, y, 1592, 286);
            list.Heading = Label(root, "", 0, 0, 1000, 40, 27, DialogueWidgets.Gold);
            list.Previous = ActionButton(root, "<", 1300, 0, 60, 40, () => { if (list.Page > 0) list.Page--; _nextPlayers = 0; });
            list.PageLabel = Label(root, "", 1360, 0, 156, 40, 20, White, TextAnchor.MiddleCenter);
            list.Next = ActionButton(root, ">", 1516, 0, 60, 40, () => { if ((list.Page + 1) * 4 < list.Players.Count) list.Page++; _nextPlayers = 0; });
            Label(root, "PLAYER", 52, 48, 340, 32, 20, DialogueWidgets.Gold);
            Label(root, "HP / ARMOR", 410, 48, 200, 32, 20, DialogueWidgets.Gold);
            Label(root, "NUTS", 620, 48, 130, 32, 20, DialogueWidgets.Gold);
            Label(root, "COORDINATES (X, Y, Z)", 770, 48, 400, 32, 20, DialogueWidgets.Gold);
            Label(root, "VEHICLE", 1190, 48, 260, 32, 20, DialogueWidgets.Gold);
            for (int i = 0; i < 4; i++)
            {
                var line = new PlayerLine { Root = Rect(root, "player", 0, 88 + i * 46, 1592, 42) };
                line.Color = Rect(line.Root, "color", 8, 9, 24, 24).gameObject.AddComponent<Image>(); line.Color.raycastTarget = false;
                line.Name = Label(line.Root, "", 52, 0, 340, 42, 23, White);
                line.Vitals = Label(line.Root, "", 410, 0, 200, 42, 23, White);
                line.Bolts = Label(line.Root, "", 620, 0, 130, 42, 23, White);
                line.Position = Label(line.Root, "", 770, 0, 400, 42, 22, White);
                line.Vehicle = Label(line.Root, "", 1190, 0, 260, 42, 23, White);
                line.Reset = ActionButton(line.Root, "RESET", 1470, 0, 106, 42, () => ShowReset(line.Player));
                line.Reset.gameObject.SetActive(false); list.Lines.Add(line);
            }
            return list;
        }
        static void RefreshExtra()
        {
            _playersTab.interactable = Multiplayer.WorldReady;
            if (_resetTarget != null && (!Multiplayer.Hosting || !Multiplayer.WorldReady || SaveBus.Busy || MultiplayerSaving.Frozen)) HideReset();
            if (_dropKind >= 0)
            {
                var count = MultiplayerInventory.Count(_dropKind);
                if (count == null || count.Value < 1 || !Multiplayer.WorldReady || !Multiplayer.LocalTargetable || MultiplayerSaving.Frozen || SaveBus.Busy) HideDrop();
                else if (_dropMax != count.Value) { _dropMax = count.Value; SetDropQuantity(_dropQuantity); }
            }
            if (!_playersSelected) return;
            MultiplayerRoster.Refresh();
            if (Time.unscaledTime < _nextPlayers) return; _nextPlayers = Time.unscaledTime + .5f;
            _activePlayers.Players.Clear(); _sleepingPlayers.Players.Clear();
            foreach (var player in MultiplayerRoster.Rows) (player.Sleeping ? _sleepingPlayers : _activePlayers).Players.Add(player);
            RefreshPlayerList(_activePlayers); RefreshPlayerList(_sleepingPlayers);
        }
        static string Number(float value) { return value < 0 ? "--" : value.ToString("0", CultureInfo.InvariantCulture); }
        static void Fitted(GlyphText label, string text, float size)
        {
            float width = GlyphFont.Get().Measure(text, size, label.Spacing);
            float fit = width > label.rectTransform.rect.width ? size * label.rectTransform.rect.width / width : size;
            if (label.Size != fit) { label.Size = fit; label.SetVerticesDirty(); } label.Set(text);
        }
        static void RefreshPlayerList(PlayerList list)
        {
            int pages = Mathf.Max(1, (list.Players.Count + 3) / 4); list.Page = Mathf.Clamp(list.Page, 0, pages - 1);
            list.Heading.Set((list.Sleeping ? "SLEEPING PLAYERS (" : "ACTIVE PLAYERS (") + list.Players.Count + ")");
            list.PageLabel.Set((list.Page + 1) + " / " + pages);
            list.Previous.interactable = list.Page > 0; list.Next.interactable = list.Page + 1 < pages;
            for (int i = 0; i < list.Lines.Count; i++)
            {
                var line = list.Lines[i]; int index = list.Page * 4 + i; bool shown = index < list.Players.Count;
                line.Root.gameObject.SetActive(shown); if (!shown) { line.Player = null; continue; }
                var player = list.Players[index]; line.Player = player;
                line.Reset.gameObject.SetActive(Multiplayer.Hosting); line.Reset.interactable = MultiplayerPlayerReset.CanReset(player);
                line.Color.color = player.Color; Fitted(line.Name, player.Name, 23);
                line.Vitals.Set(player.Downed ? "DOWNED" : Number(player.Health) + " / " + Number(player.Armor));
                line.Bolts.Set(Number(player.Bolts));
                line.Position.Set(player.HasPosition ? player.Position.x.ToString("F1", CultureInfo.InvariantCulture) + ", " + player.Position.y.ToString("F1", CultureInfo.InvariantCulture) + ", " + player.Position.z.ToString("F1", CultureInfo.InvariantCulture) : "CONNECTING");
                Fitted(line.Vehicle, player.Vehicle ?? "", 23);
            }
        }
        static void ClearExtra()
        {
            Buttons.Clear(); _inventory = _playersView = _dropDialog = _dropBar = _dropFill = null;
            _inventoryTab = _playersTab = null; _activePlayers = _sleepingPlayers = null;
            _dropTitle = _dropAmount = null; _playersSelected = _dragging = false; _dropKind = -1; _nextPlayers = 0;
            _resetDialog = null; _resetTarget = null; _resetTitle = _resetName = _resetAction = _resetWarning = _resetStatus = null; _resetConfirm = null;
        }
    }
}
