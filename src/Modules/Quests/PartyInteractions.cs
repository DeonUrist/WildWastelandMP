using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    internal static partial class PartyQuests
    {
        sealed class Conversation
        {
            internal ulong Nonce; internal uint Sequence; internal int Npc;
            internal object Offer; internal int PendingFor; internal bool RepMenu;
            internal string Reply = ""; internal readonly List<Choice> Choices = new List<Choice>();
            internal float LastRequest;
        }
        sealed class Choice { internal string Key, Text; }
        static readonly Dictionary<ulong, Conversation> Conversations = new Dictionary<ulong, Conversation>();
        static object _talk, _talkBridge;
        static Type _buttonType;
        static Func<object, object> _getBridge, _getOffer;
        static Action<object, object> _setOffer;
        static Func<object, int> _getPending;
        static Action<object, int> _setPending;
        static Func<object, bool> _getRepMenu, _talking;
        static Action<object, bool> _setRepMenu;
        static Func<object, IList> _getButtons;
        static Func<object, string> _key, _text;
        static Func<object, Action> _click;
        static Action<object, string> _setKey, _setText;
        static Action<object, Action> _setClick;
        static Action<object> _build;
        static Action<object, object> _open, _bind, _sync;
        static Func<object, object, bool> _needsSync;
        static Action<object, string> _say;
        static Func<object, Transform> _npc;
        static Func<Transform, bool> _friendly;
        static Func<string, string> _baseName;
        static Transform _contextPlayer, _contextNpc;
        static ulong _contextPeer;
        static bool _context;
        internal static ulong RemotePeer { get { return Multiplayer.Hosting && _context ? _contextPeer : 0; } }
        internal static bool DismissRequested()
        {
            if (!Multiplayer.Client) return false;
            var book = _book as QuestBook;
            if (book != null && !string.IsNullOrEmpty(book.HireName))
                Multiplayer.Send(21, w => { w.Write(book.HireName); w.Write(book.HireUntil); });
            return true;
        }
        static void ReceiveDismiss(ulong peer, BinaryReader reader)
        {
            if (!Multiplayer.Hosting || Multiplayer.PlayerFor(peer) == null) return;
            string name = reader.ReadString(); double until = reader.ReadDouble(); var book = _book as QuestBook;
            if (reader.BaseStream.Position != reader.BaseStream.Length || book == null || !Hire.Active || name != book.HireName || until != book.HireUntil) return;
            Hire.Dismiss("Your party sent {name} away."); _previous = null; _nextSnapshot = 0;
        }
        static readonly System.Text.StringBuilder Reply = new System.Text.StringBuilder();
        static ulong _clientNonce;
        static uint _clientSequence;
        static int _clientNpc;
        static string _clientKey = "";
        static float _clientSend, _clientPoll;
        static Choice[] _clientChoices;
        static uint _replySequence;
        static string _replyText;
        static bool _waiting;

        static void InitInteractions(IModule owner, object talk)
        {
            var type = _engine.GetType(); var asm = type.Assembly; _talk = talk;
            var bridge = asm.GetType("WildWasteland.Modules.Quests.TalkBridge", true); _buttonType = asm.GetType("WildWasteland.Modules.Quests.TalkBtn", true);
            _getBridge = MultiplayerAccess.Get<object>(type, "bridge"); _talkBridge = _getBridge(_engine);
            _getOffer = MultiplayerAccess.Get<object>(type, "pendingOffer"); _setOffer = MultiplayerAccess.Set<object>(type, "pendingOffer");
            _getPending = MultiplayerAccess.Get<int>(type, "pendingFor"); _setPending = MultiplayerAccess.Set<int>(type, "pendingFor");
            _getRepMenu = MultiplayerAccess.Get<bool>(type, "repMenuOpen"); _setRepMenu = MultiplayerAccess.Set<bool>(type, "repMenuOpen");
            _getButtons = MultiplayerAccess.Get<IList>(type, "wantButtons");
            _key = MultiplayerAccess.Get<string>(_buttonType, "Key"); _text = MultiplayerAccess.Get<string>(_buttonType, "Text"); _click = MultiplayerAccess.Get<Action>(_buttonType, "Click");
            _setKey = MultiplayerAccess.Set<string>(_buttonType, "Key"); _setText = MultiplayerAccess.Set<string>(_buttonType, "Text"); _setClick = MultiplayerAccess.Set<Action>(_buttonType, "Click");
            _build = MultiplayerAccess.Call<Action<object>>(type, "BuildButtons", Type.EmptyTypes);
            _open = MultiplayerAccess.Call<Action<object, object>>(type, "OnTalkOpen", typeof(object));
            _bind = MultiplayerAccess.Call<Action<object, object>>(bridge, "Bind", typeof(object));
            _sync = MultiplayerAccess.Call<Action<object, object>>(bridge, "SyncButtons", typeof(List<>).MakeGenericType(_buttonType));
            _needsSync = MultiplayerAccess.Call<Func<object, object, bool>>(bridge, "NeedsSync", typeof(List<>).MakeGenericType(_buttonType));
            _say = MultiplayerAccess.Call<Action<object, string>>(bridge, "Say", typeof(string));
            _npc = MultiplayerAccess.Get<Transform>(bridge, "Npc");
            _talking = MultiplayerAccess.Get<bool>(bridge, "TalkingNow");
            var game = asm.GetType("WildWasteland.Modules.Quests.Game", true);
            _baseName = MultiplayerAccess.Call<Func<string, string>>(game, "BaseName", typeof(string));
            _friendly = npc => npc != null && !Game.IsPlayerEnemy(npc.gameObject);
            Patch(owner, type, "OnTalkOpen", "TalkOpened"); Patch(owner, type, "OnTalkClose", "TalkClosed");
            Patch(owner, type, "Defer", "DeferredAction"); Patch(owner, type, "BuildButtons", "BuildAllowed");
            Patch(owner, bridge, "Bind", "BindAllowed"); Patch(owner, bridge, "get_Npc", postfix: "ContextNpc");
            Patch(owner, bridge, "get_NpcName", postfix: "ContextName"); Patch(owner, bridge, "get_TalkingNow", postfix: "ContextTalking");
            Patch(owner, bridge, "Say", "DialogueLine"); Patch(owner, bridge, "You", "PlayerLine");
            Patch(owner, game, "Player", postfix: "ContextPlayer");
            InitInventory(owner, game);
        }
        static void StopInteractions() { ResetInteractions(); _talk = _talkBridge = null; _friendly = null; }
        static void ResetInteractions()
        {
            Conversations.Clear(); _context = false; _contextPlayer = _contextNpc = null; _contextPeer = 0;
            _clientNonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); _clientSequence = 0; _clientNpc = 0;
            _waiting = false; _clientChoices = null; _clientPoll = 0;
        }
        static bool TalkOpened(object __0)
        {
            if (!Multiplayer.Client) return true;
            if (__0 != null) _bind(_talkBridge, __0);
            _getButtons(_engine).Clear(); ClientInteraction(); return false;
        }
        static bool TalkClosed()
        { if (Multiplayer.Client) { _clientNpc = 0; _waiting = false; _getButtons(_engine).Clear(); } return !_context; }
        static bool DeferredAction(Action __0) { if (!_context) return true; if (__0 != null) __0(); return false; }
        static bool BuildAllowed() { return !Multiplayer.Client; }
        static bool BindAllowed() { return !_context; }
        static void ContextNpc(ref Transform __result) { if (_context) __result = _contextNpc; }
        static void ContextName(ref string __result) { if (_context) __result = _contextNpc != null ? _baseName(_contextNpc.name) : ""; }
        static void ContextTalking(ref bool __result) { if (_context) __result = _contextNpc != null; }
        static void ContextPlayer(ref Transform __result) { if (_context) __result = _contextPlayer; }
        static bool DialogueLine(string __0) { if (!_context) return true; AddReply(__0); return false; }
        static bool PlayerLine(string __0) { if (!_context) return true; AddReply("You: " + __0); return false; }
        static void AddReply(string line) { if (Reply.Length < 320) { if (Reply.Length > 0) Reply.Append('\n'); Reply.Append(line); } }

        static void ClientInteraction()
        {
            if (!Enabled || !Multiplayer.Client || _talkBridge == null || !Multiplayer.WorldReady) return;
            if (!_talking(_talkBridge)) { _clientNpc = 0; _waiting = false; return; }
            var npc = _npc(_talkBridge); int id = npc != null ? Multiplayer.ObjectId(npc.gameObject) : 0;
            if (id == 0) return;
            if (_clientNpc != id) { _clientNpc = id; RequestChoice(""); }
            if (_waiting && Time.unscaledTime >= _clientSend) SendChoice();
            if (!_waiting && Time.unscaledTime >= _clientPoll) { _clientPoll = Time.unscaledTime + 2f; RequestChoice(""); }
            var buttons = _getButtons(_engine); if (_needsSync(_talkBridge, buttons)) _sync(_talkBridge, buttons);
        }
        static void RequestChoice(string key)
        {
            if (_waiting && key.Length > 0) return;
            _clientSequence++; _clientKey = key; _waiting = true; _clientChoices = null; _clientPoll = Time.unscaledTime + 2f;
            SendChoice();
        }
        static void SendChoice()
        {
            _clientSend = Time.unscaledTime + 1f;
            Multiplayer.Send(18, w => { w.Write(_clientNonce); w.Write(_clientSequence); w.Write(_clientNpc); w.Write(_clientKey); });
        }
        static void InteractionTick()
        {
            if (Multiplayer.Client) ClientInteraction();
            if (!Multiplayer.Hosting) return;
            // Disconnected conversations have no engine state ownership or Unity references.
            List<ulong> expired = null;
            foreach (var pair in Conversations) if (Multiplayer.PlayerFor(pair.Key) == null || Time.unscaledTime - pair.Value.LastRequest > 30f)
            { if (expired == null) expired = new List<ulong>(); expired.Add(pair.Key); }
            if (expired != null) foreach (var peer in expired) Conversations.Remove(peer);
        }
        static bool AllowedChoice(string key)
        {
            return key == "work" || key == "accept" || key == "decline" || key.StartsWith("turn:", StringComparison.Ordinal)
                || key.StartsWith("drop:", StringComparison.Ordinal) || key.StartsWith("back:", StringComparison.Ordinal)
                || key == "hire" || key == "dismiss" || key == "repmenu" || key == "repback"
                || key.StartsWith("repbuy:", StringComparison.Ordinal) || key.StartsWith("repsell:", StringComparison.Ordinal);
        }
        static void ReceiveInteraction(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == 18 && Multiplayer.Hosting)
            {
                ulong nonce = reader.ReadUInt64(); uint sequence = reader.ReadUInt32(); int id = reader.ReadInt32(); string key = reader.ReadString();
                if (key.Length > 128 || sequence == 0 || reader.BaseStream.Position != reader.BaseStream.Length) return;
                var player = Multiplayer.PlayerFor(from); var npc = Multiplayer.ObjectById(id);
                if (player == null || npc == null || _friendly == null || Vector3.Distance(player.transform.position, npc.transform.position) > 8f) return;
                var health = FsmUtil.Find(npc, "Health"); var hp = health != null ? health.FsmVariables.FindFsmFloat("Health") : null;
                if (hp == null || hp.Value <= 0 || !_friendly(npc.transform)) return;
                Conversation conversation;
                if (!Conversations.TryGetValue(from, out conversation) || conversation.Nonce != nonce)
                { conversation = new Conversation { Nonce = nonce }; Conversations[from] = conversation; }
                if (sequence < conversation.Sequence) return;
                if (sequence == conversation.Sequence) { SendConversation(from, conversation); return; }
                if (Time.unscaledTime - conversation.LastRequest < .1f) return;
                bool opening = id != conversation.Npc;
                if (opening) { if (key.Length != 0) return; conversation.Npc = id; conversation.Offer = null; conversation.PendingFor = 0; conversation.RepMenu = false; }
                conversation.Sequence = sequence; conversation.LastRequest = Time.unscaledTime;
                RunConversation(from, player.transform, npc.transform, conversation, key, opening);
                SendConversation(from, conversation); return;
            }
            if (!Multiplayer.Client || from != 0 || !_waiting) return;
            ulong clientNonce = reader.ReadUInt64(); uint reply = reader.ReadUInt32();
            if (clientNonce != _clientNonce || reply != _clientSequence) return;
            if (kind == 19)
            {
                int count = reader.ReadByte(); string text = reader.ReadString(); if (count > 24 || text.Length > 400) return;
                _replySequence = reply; _replyText = text; _clientChoices = new Choice[count];
                if (count == 0) ApplyConversation();
            }
            else if (kind == 20 && _replySequence == reply && _clientChoices != null)
            {
                int index = reader.ReadByte(); string key = reader.ReadString(), text = reader.ReadString();
                if (index >= _clientChoices.Length || key.Length > 128 || text.Length > 220) return;
                _clientChoices[index] = new Choice { Key = key, Text = text };
                for (int i = 0; i < _clientChoices.Length; i++) if (_clientChoices[i] == null) return;
                ApplyConversation();
            }
        }
        static void RunConversation(ulong peer, Transform player, Transform npc, Conversation conversation, string key, bool opening)
        {
            object hostOffer = _getOffer(_engine); int hostPending = _getPending(_engine); bool hostRep = _getRepMenu(_engine);
            var buttons = _getButtons(_engine); var hostButtons = new object[buttons.Count]; buttons.CopyTo(hostButtons, 0);
            _context = true; _contextPlayer = player; _contextNpc = npc; _contextPeer = peer; Reply.Length = 0;
            try
            {
                _setOffer(_engine, conversation.Offer); _setPending(_engine, conversation.PendingFor); _setRepMenu(_engine, conversation.RepMenu);
                if (opening) _open(_engine, _talk);
                _build(_engine);
                if (key.Length > 0 && AllowedChoice(key))
                    for (int i = 0; i < buttons.Count; i++) if (_key(buttons[i]) == key) { var click = _click(buttons[i]); if (click != null) click(); break; }
                _build(_engine); conversation.Choices.Clear();
                for (int i = 0; i < buttons.Count && conversation.Choices.Count < 24; i++)
                {
                    string buttonKey = _key(buttons[i]); if (!AllowedChoice(buttonKey)) continue;
                    conversation.Choices.Add(new Choice { Key = buttonKey, Text = Limit(_text(buttons[i]), 220) });
                }
                conversation.Offer = _getOffer(_engine); conversation.PendingFor = _getPending(_engine);
                conversation.RepMenu = _getRepMenu(_engine);
                conversation.Reply = Limit(Reply.ToString(), 400); _previous = null; _nextSnapshot = 0;
            }
            finally
            {
                _setOffer(_engine, hostOffer); _setPending(_engine, hostPending); _setRepMenu(_engine, hostRep);
                buttons.Clear(); foreach (var button in hostButtons) buttons.Add(button);
                _context = false; _contextPlayer = _contextNpc = null; _contextPeer = 0;
            }
        }
        static string Limit(string value, int max)
        {
            if (value == null) return "";
            int length = Math.Min(value.Length, max);
            while (length > 0 && (char.IsHighSurrogate(value[length - 1]) || System.Text.Encoding.UTF8.GetByteCount(value.Substring(0, length)) > max)) length--;
            return length == value.Length ? value : value.Substring(0, length);
        }
        static void SendConversation(ulong peer, Conversation conversation)
        {
            Multiplayer.Send(19, w => { w.Write(conversation.Nonce); w.Write(conversation.Sequence); w.Write((byte)conversation.Choices.Count); w.Write(conversation.Reply); }, peer);
            for (int i = 0; i < conversation.Choices.Count; i++)
            {
                int index = i; var choice = conversation.Choices[i];
                Multiplayer.Send(20, w => { w.Write(conversation.Nonce); w.Write(conversation.Sequence); w.Write((byte)index); w.Write(choice.Key); w.Write(choice.Text); }, peer);
            }
        }
        static void ApplyConversation()
        {
            _waiting = false; var buttons = _getButtons(_engine); buttons.Clear();
            foreach (var choice in _clientChoices)
            {
                var button = Activator.CreateInstance(_buttonType); string key = choice.Key;
                _setKey(button, key); _setText(button, choice.Text); _setClick(button, delegate { RequestChoice(key); }); buttons.Add(button);
            }
            if (_replyText.Length > 0) _say(_talkBridge, _replyText);
            _sync(_talkBridge, buttons);
        }
        internal static void RemoteProgress(Action tick)
        {
            if (!Enabled || !Multiplayer.Hosting || _context) return;
            _context = true;
            try
            {
                for (int i = 0; i < Multiplayer.RemoteCount; i++)
                {
                    var player = Multiplayer.RemoteAt(i); if (player == null || !player.activeInHierarchy) continue;
                    _contextPlayer = player.transform; _contextNpc = null; _contextPeer = Multiplayer.PeerFor(player); tick();
                }
            }
            finally { _context = false; _contextPlayer = _contextNpc = null; _contextPeer = 0; }
        }
    }
}

