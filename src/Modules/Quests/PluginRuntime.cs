using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Bootstrap;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    internal sealed partial class Plugin : IQuestApi
    {
        internal string DataDir;
        int _generation;
        string _sessionSlot;
        bool _newWorld;
        int _aimFrame = -1, _aimComputed;
        string _resumeText, _resumeSlot; int _resumeWorld;
        string[] _resumeCrew;
        readonly List<QuestRow> _rows = new List<QuestRow>();
        readonly Dictionary<string, int> _emptyScans = new Dictionary<string, int>();
        public bool Ready { get { return ready; } }
        public bool TrackerEnabled { get { return showTracker.Value; } }
        public bool JournalOpen { get { return showLog; } }
        public string JournalText { get { return logText; } }
        public string JournalKey { get { return logKey.Value.ToString(); } }
        public int Completed { get { return book.TotalDone; } }
        public IList<QuestRow> Rows { get { return _rows; } }
        public IQuestConversation Conversation { get { return bridge; } }
        internal object ConversationHost { get { return bridge.Instance; } }
        bool TrackerStyled { get { return Services.QuestUi != null && Services.QuestUi.Tracker; } }
        bool JournalStyled { get { return Services.QuestUi != null && Services.QuestUi.Journal; } }
        internal bool DialogueStyled { get { return Services.QuestUi != null && Services.QuestUi.Conversation; } }
        internal bool WantsGui { get { return ready && (bridge.NativeOpen || bridge.Prompt.Length > 0 || showLog || toasts.Count > 0 || (!TrackerStyled && showTracker.Value && trackerLines.Count > 0)); } }

        internal void Start(QuestsModule owner, HireSettings hiring, VoiceSettings voices)
        {
            I = this; Log = new QuestLogger(); logged.Clear(); DataDir = owner.Dir; _generation++;
            foreach (var file in QuestContent.Import(DataDir)) Core.Log.Always("[Quests] imported missing content: " + file);
            ApplyTakeoverSettings(); ApplyFactionSettings(); ParseGenTypes();
            Game.MinLiquidFraction = minLiquid.Value;
            factionsPath = Path.Combine(DataDir, "factions.txt");
            Factions.Refresh(factionsPath, Warn); defs = QuestFile.Load(Path.Combine(DataDir, "quests.txt"), Warn);
            Hire.Init(hiring, book, AddToast, ChangeRep); Voice.Init(voices, DataDir);
            Takeover.Stamps = book.GiverStamp; Takeover.Log = Info;
            Core.Hooks.CreateObjectEnter.Postfix(owner, Death);
            Core.Hooks.GetButtonDownUpdate.Prefix(owner, BlockButtonDown);
            Core.Hooks.GetButtonDo.Prefix(owner, BlockButton);
            Core.Hooks.GetButtonUpUpdate.Prefix(owner, BlockButtonUp);
            Core.Hooks.GetMouseButtonDownUpdate.Prefix(owner, BlockMouse);
            Hotkeys.Register(owner, logKey, ToggleJournal, CanJournal);
            Hotkeys.Register(owner, hiring.dismissKey, DismissFollower, CanDismiss);
            Hotkeys.Register(owner, dumpKey, DumpFaction, CanJournal);
            Hotkeys.Register(owner, QuestsModule.InteractKey, OpenInteraction, CanInteract);
            bridge.InstallOptional(owner);
            Core.Log.Always("[Quests] Native Quests 1.8.2 ready: " + defs.Count + " authored quests; AQ1 / ApocalypterQuests retained; Talk optional.");
        }
        static void Warn(string text) { Core.Log.Warn("[Quests] " + text); }
        static void Info(string text) { Core.Log.Info("[Quests] " + text); }
        internal void ApplyConfig() { ApplyTakeoverSettings(); ApplyFactionSettings(); ParseGenTypes(); Game.MinLiquidFraction = minLiquid.Value; }
        static void Death(CreateObject action)
        {
            if (I == null || !I.ready || action.Fsm == null || action.Fsm.Name != "Health" || action.Fsm.ActiveStateName != "spawn") return;
            var go = action.Owner; if (go == null || go == GameRefs.Player) return;
            string dead = action.storeObject != null && action.storeObject.Value != null ? Game.BaseName(action.storeObject.Value.name) : "";
            I.OnEnemyKilled(Game.BaseName(go.name), dead, go.transform.position, go.tag, go);
        }
        static bool BlockButtonDown(GetButtonDown action) { return I == null || !I.bridge.NativeOpen; }
        static bool BlockButton(GetButton action) { return I == null || !I.bridge.NativeOpen; }
        static bool BlockButtonUp(GetButtonUp action) { return I == null || !I.bridge.NativeOpen; }
        static bool BlockMouse(GetMouseButtonDown action) { return I == null || !I.bridge.NativeOpen; }
        bool CanJournal() { return ready && GameRefs.InGame && !bridge.TalkingNow; }
        bool CanDismiss() { return ready && Hire.Active && GameRefs.InGame; }
        void ToggleJournal() { if (!GamePaused()) showLog = !showLog; }
        void DumpFaction() { var p = Game.Player(); if (p != null) { FactionDump.Run(p.position, 60f, Path.Combine(DataDir, "faction_dump.txt"), Factions.PeopleWords); AddToast("Wrote faction_dump.txt"); } }

        internal void SceneReset()
        {
            _generation++; ready = false; _sessionSlot = null; _newWorld = false;
            _resumeText = _resumeSlot = null; _resumeCrew = null;
            bridge.Close(); ResetSession(); deferred.Clear(); showLog = false; toasts.Clear(); _rows.Clear();
            Takeover.Shutdown(); Factions.Overrides.Clear(); CrewHistory.Clear(); QuestFollower.Release(); Game.ResetWorld();
        }
        internal void StateChanged(string previous, string state)
        {
            if (Multiplayer.Client)
            {
                if (state == "LoadGame" || state == "setSeed" || state == "Start") { SceneReset(); book.Clear(); }
                if (state == "isPlay") ready = true;
                return;
            }
            if (state == "LoadGame" || state == "setSeed" || state == "Start")
            {
                bool fresh = state != "LoadGame";
                SceneReset(); _newWorld = fresh; CrewHistory.Begin(fresh); book.Clear();
            }
            if (state == "isPlay" && !ready)
            {
                _sessionSlot = SaveBus.Slot; if (_sessionSlot == null) return;
                ready = true; CrewHistory.Load(_newWorld);
                bool resume = !_newWorld && _resumeText != null && _resumeSlot == _sessionSlot && _resumeWorld == SaveBus.Generation;
                if (resume && book.Deserialize(_resumeText)) { RestoreBook(); CrewHistory.Restore(_resumeCrew); saveBlocked = false; }
                else if (!_newWorld) LoadBook(); else { saveBlocked = false; book.Clear(); }
                _resumeText = _resumeSlot = null; _resumeCrew = null;
                _newWorld = false; RefreshLines(Game.Player()); SnapshotRows();
                Core.Log.Always("[Quests] World ready: " + _sessionSlot + ", " + book.Active.Count + " active quests" + (saveBlocked ? "; unreadable data protected" : ""));
            }
            // Write both cached copies and disk before native flushing; save-and-quit never reaches AfterSave.
            if ((state == "wait" || state == "SaveGame") && ready && _sessionSlot == SaveBus.Slot)
            {
                SaveBook(); CrewHistory.Save(_sessionSlot, CrewHistory.Snapshot());
                if (state == "SaveGame")
                {
                    int generation = _generation; string slot = _sessionSlot;
                    SaveBus.AfterSave(delegate { if (I == this && ready && generation == _generation && slot == _sessionSlot && slot == SaveBus.Slot) SaveBook(); });
                }
            }
        }
        void CaptureBook()
        {
            book.Overrides.Clear(); foreach (var item in Factions.Overrides) book.Overrides[item.Key] = item.Value;
            book.Walks.Clear(); book.Walks.AddRange(Takeover.ActiveWalks());
        }
        void RestoreBook()
        {
            Factions.Overrides.Clear(); foreach (var item in book.Overrides) Factions.Overrides[item.Key] = item.Value;
            Takeover.RestoreWalks(book.Walks); Hire.Reset();
        }
        void SnapshotRows()
        {
            _rows.Clear();
            for (int i = 0; i < trackerLines.Count && i < book.Active.Count; i++)
                _rows.Add(new QuestRow { Id = book.Active[i].Id, Text = trackerLines[i], Color = MapLink.QuestColor(book.Active[i].Id) });
        }
        internal void RefreshPartyRows() { SnapshotRows(); }
        internal static bool Returning(Quest q) { return q.Type == QType.Deliver && q.Place == "WW:return-empty-delivery"; }
        bool DeliveryRecipient(Quest q, Vector3 position, float radius, bool returning)
        {
            foreach (var f in QuestWorld.People)
            {
                if (f == null || f.FsmName != "Health" || f.ActiveStateName != "health" || !f.gameObject.activeInHierarchy) continue;
                var go = f.gameObject;
                if (returning && go.name != q.GiverId) continue;
                var hp = f.FsmVariables.GetFsmFloat("Health"); if (hp == null || hp.Value <= 0) continue;
                if (Game.HDist(f.transform.position, position) > radius || Game.IsPlayerEnemy(go) || !Person(Game.BaseName(go.name))) continue;
                if (!Factions.Friendly(Factions.ByKey(q.Home), Factions.OfObject(go.tag, Game.BaseName(go.name), go.transform.position))) continue;
                if (returning) q.GiverPos = Game.Rel(f.transform.position);
                return true;
            }
            return false;
        }
        internal void Stop()
        {
            if (ready && !saveBlocked && _sessionSlot != null && _sessionSlot == SaveBus.Slot)
            { CaptureBook(); _resumeText = book.Serialize(); _resumeSlot = _sessionSlot; _resumeWorld = SaveBus.Generation; _resumeCrew = CrewHistory.Snapshot(); }
            _generation++; ready = false; bridge.Close(); bridge.Uninstall(); ResetSession();
            Hire.Shutdown(); Voice.Shutdown(); QuestFollower.Release(); Takeover.Shutdown(); Factions.Reset();
            Game.ResetWorld(); CrewHistory.Clear(); _rows.Clear(); toasts.Clear(); deferred.Clear(); I = null;
        }
    }
}
