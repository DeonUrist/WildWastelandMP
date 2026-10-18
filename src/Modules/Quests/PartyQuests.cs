using System;
using System.Collections;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    // The committed main uses the optional original QuestBook. Never run a second engine on a guest.
    internal static partial class PartyQuests
    {
        const int ChunkSize = 900, MaxBook = 512 * 1024;
        static object _engine, _book;
        static Func<object, string> _serialize;
        static Func<object, string, bool> _deserialize;
        static Action<object, Transform> _refresh;
        static Func<object, int> _reward;
        static byte[] _received, _outgoing;
        static bool[] _chunks;
        static uint _receiveVersion, _sendVersion, _appliedVersion;
        static ulong _session, _receiveSession;
        static int _receivedChunks, _sendChunk;
        static float _nextSnapshot, _nextRequest;
        static string _previous;
        internal static bool Enabled { get { return _engine != null && Multiplayer.Present; } }
        internal static void Init(QuestsModule owner, object engine, object talk)
        {
            if (!Multiplayer.Present || engine == null) return;
            _engine = engine; var type = engine.GetType();
            _book = MultiplayerAccess.Get<object>(type, "book")(engine); var bookType = _book.GetType();
            _serialize = MultiplayerAccess.Call<Func<object, string>>(bookType, "Serialize", Type.EmptyTypes);
            _deserialize = MultiplayerAccess.Call<Func<object, string, bool>>(bookType, "Deserialize", typeof(string));
            _refresh = MultiplayerAccess.Call<Action<object, Transform>>(type, "RefreshLines", typeof(Transform));
            _reward = MultiplayerAccess.Get<int>(type.Assembly.GetType("WildWasteland.Modules.Quests.Quest", true), "Reward");
            Patch(owner, type, "Pay", "AuthorityOnly");
            foreach (string method in new[] { "OnEnemyKilled", "AskForWork", "AcceptQuest", "RunTalkAction", "TurnIn", "Complete", "ChangeRep", "SaveBook", "LoadBook", "HireNpc", "DealRep" })
            {
                var target = AccessTools.Method(type, method);
                if (target != null) Core.Patches.Patch(owner, target, prefix: AccessTools.Method(typeof(PartyQuests), "AuthorityOnly"));
            }
            InitInteractions(owner, talk);
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Reset;
            Reset();
            Scheduler.Interval(owner, "Party quest book", .1f, Tick);
            Log.Always("[Multiplayer] Shared AQ1 quest book/reputation mirror ready; quest decisions belong to the host");
        }
        static bool AuthorityOnly() { return !Multiplayer.Client; }
        static void Patch(IModule owner, Type type, string name, string prefix = null, string postfix = null)
        {
            if (!Core.Patches.Patch(owner, AccessTools.Method(type, name), prefix: prefix == null ? null : AccessTools.Method(typeof(PartyQuests), prefix), postfix: postfix == null ? null : AccessTools.Method(typeof(PartyQuests), postfix)))
                throw new InvalidOperationException("Multiplayer quest hook failed: " + type.FullName + "." + name);
        }
        internal static void Shutdown()
        {
            Multiplayer.Message -= Receive; Multiplayer.SessionChanged -= Reset;
            StopInteractions();
            _engine = _book = null; Reset();
        }
        static void Reset()
        {
            _received = _outgoing = null; _chunks = null; _receivedChunks = _sendChunk = 0;
            _sendVersion = _receiveVersion = _appliedVersion = 0; _previous = null; _nextSnapshot = _nextRequest = 0;
            _session = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); _receiveSession = 0;
            ResetInteractions();
        }
        internal static void ClientFrame()
        {
            ClientInteraction();
        }
        static void Tick()
        {
            if (!Enabled || !Multiplayer.WorldReady) return;
            InteractionTick();
            float now = Time.unscaledTime;
            if (Multiplayer.Client)
            {
                if (now >= _nextRequest) { _nextRequest = now + 3f; Multiplayer.Send(16, w => { w.Write(_receiveSession); w.Write(_appliedVersion); }); }
                return;
            }
            if (_outgoing == null && now >= _nextSnapshot)
            {
                _nextSnapshot = now + 1f; string text = _serialize(_book);
                if (text == _previous) return;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                if (bytes.Length > MaxBook) { Log.Warn("[Multiplayer] Quest book exceeds synchronization limit"); return; }
                _previous = text; _outgoing = bytes; _sendChunk = 0; _sendVersion++;
            }
            // Bound reliable bursts; do not place a whole large journal in one UDP datagram/frame.
            for (int sent = 0; sent < 4 && _outgoing != null; sent++)
            {
                int index = _sendChunk++, offset = index * ChunkSize, count = Math.Min(ChunkSize, _outgoing.Length - offset);
                Multiplayer.Send(17, w => { w.Write(_session); w.Write(_sendVersion); w.Write(_outgoing.Length); w.Write(index); w.Write((ushort)count); w.Write(_outgoing, offset, count); });
                if (_sendChunk * ChunkSize >= _outgoing.Length) _outgoing = null;
            }
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (!Enabled || !Multiplayer.WorldReady) return;
            if (kind == 16 && Multiplayer.Hosting) { ulong requestedSession = reader.ReadUInt64(); uint requestedVersion = reader.ReadUInt32(); if (requestedSession != _session || requestedVersion != _sendVersion) { _previous = null; _nextSnapshot = 0; } return; }
            if (kind == 21) { ReceiveDismiss(from, reader); return; }
            if (kind >= 18 && kind <= 20) { ReceiveInteraction(from, kind, reader); return; }
            if (kind != 17 || !Multiplayer.Client || from != 0) return;
            ulong session = reader.ReadUInt64(); uint version = reader.ReadUInt32(); int length = reader.ReadInt32(), index = reader.ReadInt32(), count = reader.ReadUInt16();
            if (length < 1 || length > MaxBook || index < 0 || index >= (length + ChunkSize - 1) / ChunkSize
                || count != Math.Min(ChunkSize, length - index * ChunkSize) || reader.BaseStream.Length - reader.BaseStream.Position != count) return;
            if (_receiveSession == session && version < _receiveVersion) return;
            if (_received == null || session != _receiveSession || version != _receiveVersion)
            {
                _received = new byte[length]; _chunks = new bool[(length + ChunkSize - 1) / ChunkSize];
                _receiveSession = session; _receiveVersion = version; _receivedChunks = 0;
            }
            if (_received.Length != length || _chunks[index]) return;
            if (reader.Read(_received, index * ChunkSize, count) != count) return;
            _chunks[index] = true; _receivedChunks++;
            if (_receivedChunks != _chunks.Length) return;
            string serialized = Encoding.UTF8.GetString(_received);
            if (!_deserialize(Activator.CreateInstance(_book.GetType()), serialized)) { Log.Warn("[Multiplayer] Host quest book was not valid AQ1"); return; }
            _deserialize(_book, serialized);
            _appliedVersion = version;
            var book = _book as QuestBook;
            if (book != null) { Factions.Overrides.Clear(); foreach (var pair in book.Overrides) Factions.Overrides[pair.Key] = pair.Value; }
            _refresh(_engine, GameRefs.Player != null ? GameRefs.Player.transform : null);
            var engine = _engine as Plugin; if (engine != null) engine.RefreshPartyRows();
        }
    }
}

