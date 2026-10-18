using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using WildWasteland.Core;
using WildWasteland.Core.UI;
using Patches = WildWasteland.Core.Patches;

namespace WildWasteland.Modules.Quests
{
    internal partial class TalkBridge
    {
        internal bool NativeOpen;
        internal int Generation;
        internal bool ExternalAvailable;
        internal readonly EditorSession Session = new EditorSession();
        Transform _nativeNpc;
        string _nativeName = "";
        readonly List<string> _chat = new List<string>();
        readonly List<QuestChoice> _choices = new List<QuestChoice>();
        readonly Dictionary<string, Action> _buttons = new Dictionary<string, Action>();
        bool _pending;
        public bool Open { get { return NativeOpen; } }
        public string Name { get { return _nativeName; } }
        public string Prompt { get; internal set; }
        public int Revision { get; private set; }
        public IList<string> Chat { get { return _chat; } }
        public IList<QuestChoice> Choices { get { return _choices; } }

        internal void InstallOptional(QuestsModule owner)
        {
            Prompt = ""; ExternalAvailable = false;
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(Plugin.TalkGuid, out info) || ReferenceEquals(info.Instance, null)) return;
            if (!Init(info.Instance.GetType())) { Plugin.Log.LogWarning("[Quests] Talk adapter unavailable; using native quest interaction."); return; }
            Bind(info.Instance); ExternalAvailable = true;
            if (!Patches.Patch(owner, AccessTools.Method(TalkType, "Open"), postfix: AccessTools.Method(typeof(TalkBridge), "Opened"))
                || !Patches.Patch(owner, AccessTools.Method(TalkType, "Close"), postfix: AccessTools.Method(typeof(TalkBridge), "Closed")))
                throw new InvalidOperationException("Optional Talk conversation hooks unavailable.");
            TalkApiLink.Init(info.Instance.GetType().Assembly, Plugin.I, "OnTalkAction");
            Core.Log.Always("[Quests] Optional Apocatalker content adapter ready.");
        }
        static void Opened(object __instance) { if (Plugin.I != null) Plugin.I.ExternalOpened(__instance); }
        static void Closed() { if (Plugin.I != null) Plugin.I.ExternalClosed(); }
        internal bool Begin(Transform npc)
        {
            if (NativeOpen || npc == null || !Session.Acquire()) return false;
            Generation++; NativeOpen = true; _nativeNpc = npc; _nativeName = Game.BaseName(npc.name);
            _chat.Clear(); _choices.Clear(); _buttons.Clear(); _pending = false; Prompt = ""; mineSig = ""; Revision++;
            AddLine("What do you need?"); Plugin.I.OnTalkOpen(null); return true;
        }
        void AddLine(string text) { if (_chat.Count >= 100) _chat.RemoveAt(0); _chat.Add(text); Revision++; }
        void NativeButtons(List<TalkBtn> want)
        {
            _buttons.Clear(); _choices.Clear(); _pending = false;
            foreach (var b in want) { _choices.Add(new QuestChoice { Key = b.Key, Text = b.Text }); _buttons[b.Key] = b.Click; }
            Revision++;
        }
        public void Choose(string key)
        {
            Action click;
            if (!NativeOpen || _pending || !_buttons.TryGetValue(key, out click)) return;
            _pending = true; click();
        }
        public void Close()
        {
            if (!NativeOpen) { Prompt = ""; return; }
            if (Plugin.I != null) Plugin.I.OnTalkClose();
            Generation++; NativeOpen = false; _nativeNpc = null; _pending = false; Prompt = "";
            _buttons.Clear(); _choices.Clear(); Voice.Stop(); Session.Close(); Revision++;
        }
        internal void Uninstall()
        {
            Close(); ClearButtons(); Session.Close(true); TalkApiLink.Shutdown(); ExternalAvailable = false;
            if (skinTex != null && !skinFromPanel) UnityEngine.Object.Destroy(skinTex);
            skinTex = null; Instance = null; TalkType = null;
        }
    }

    internal sealed partial class Plugin
    {
        Transform _interaction;
        Vector2 _fallbackChat, _fallbackChoices;
        readonly RaycastHit[] _interactionHits = new RaycastHit[32];
        public void ExternalOpened(object host) { if (ready && bridge.ExternalAvailable && bridge.TalkingNow) OnTalkOpen(host); }
        public void ExternalClosed() { if (bridge.ExternalAvailable) { OnTalkClose(); bridge.Generation++; Voice.Stop(); deferred.Clear(); } }
        bool CanInteract() { return ready && _interaction != null && !bridge.ExternalAvailable && !bridge.TalkingNow && GameRefs.InGame && !GamePaused() && !EditorSession.Busy; }
        void OpenInteraction() { if (CanInteract()) { showLog = false; bridge.Begin(_interaction); } }
        internal void InteractionFrame()
        {
            if (!bridge.NativeOpen) return;
            if (!Application.isFocused || !ready || !bridge.Session.Open || !GameRefs.InPlay || bridge.Npc == null
                || GameRefs.Player == null || Game.HDist(bridge.Npc.position, GameRefs.Player.transform.position) > 8f || Input.GetKeyDown(KeyCode.Escape))
            { bridge.Close(); deferred.Clear(); return; }
            bridge.Session.Maintain();
        }
        internal void FindInteraction()
        {
            _interaction = null; bridge.Prompt = "";
            if (bridge.ExternalAvailable || bridge.TalkingNow || GamePaused() || EditorSession.Busy || ApocasetterBridge.InputBlocked || !Application.isFocused) return;
            var camera = GameRefs.MainCamera; if (camera == null) return;
            Vector3 eye = camera.transform.position; float best = float.PositiveInfinity;
            foreach (var f in QuestWorld.People)
            {
                if (f == null || f.FsmName != "Health" || (!Multiplayer.Client && f.ActiveStateName != "health") || !f.gameObject.activeInHierarchy) continue;
                var go = f.gameObject; if (go == GameRefs.Player || Game.IsPlayerEnemy(go) || (Services.FollowerVehicles != null && Services.FollowerVehicles.IsSeated(go))) continue;
                string name = Game.BaseName(go.name);
                if (!Person(name) || name.IndexOf("Dead", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                Vector3 aim = f.transform.position + Vector3.up * 1.2f, delta = aim - eye; float distance = delta.magnitude;
                if (GameRefs.Player == null || Game.HDist(f.transform.position, GameRefs.Player.transform.position) > 4f || distance < .01f || Vector3.Dot(camera.transform.forward, delta / distance) < .85f) continue;
                var hp = f.FsmVariables.GetFsmFloat("Health"); if (hp == null || hp.Value <= 0) continue;
                bool blocked = false;
                int count = Physics.RaycastNonAlloc(eye, delta / distance, _interactionHits, distance, ~0, QueryTriggerInteraction.Ignore);
                if (count == _interactionHits.Length) continue;
                for (int i = 0; i < count; i++)
                {
                    var hit = _interactionHits[i]; if (hit.collider == null || hit.transform.IsChildOf(f.transform)) continue;
                    if (GameRefs.Player != null && hit.transform.IsChildOf(GameRefs.Player.transform)) continue;
                    blocked = true; break;
                }
                float score = 1f - Vector3.Dot(camera.transform.forward, delta / distance) + distance * .01f;
                if (!blocked && score < best) { best = score; _interaction = f.transform; }
            }
            if (_interaction != null) bridge.Prompt = "[" + QuestsModule.InteractKey.Value + "] Talk to " + Game.BaseName(_interaction.name);
        }
        static bool Person(string name)
        {
            string n = Util.Norm(name);
            foreach (string word in Factions.PeopleWords) if (word.Length > 0 && n.Contains(word)) return true;
            return Hire.CostFor(name) >= 0 || n.Contains("gunnar") || n.Contains("mechanic");
        }
        void FilterCrewButtons()
        {
            var tr = bridge.Npc; bool crew = CrewHistory.Blocked(tr);
            bool seated = tr != null && Services.FollowerVehicles != null && Services.FollowerVehicles.IsSeated(tr.gameObject);
            for (int i = wantButtons.Count - 1; i >= 0; i--)
            {
                string key = wantButtons[i].Key;
                if ((crew && (key == "work" || key == "accept")) || (seated && key == "hire")) wantButtons.RemoveAt(i);
            }
        }
        internal void DrawConversation()
        {
            if (!ready) return;
            if (!bridge.NativeOpen)
            {
                if (bridge.Prompt.Length > 0 && !DialogueStyled && GameRefs.InGame)
                    GUI.Label(new Rect(Screen.width * .5f - 240, Screen.height * .65f, 480, 40), bridge.Prompt);
                return;
            }
            if (DialogueStyled) return;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 900f); var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            try
            {
                var rect = new Rect(Screen.width / scale * .5f - 450, 45, 900, 800); GUI.Box(rect, bridge.Name);
                GUILayout.BeginArea(new Rect(rect.x + 25, rect.y + 45, 850, 710));
                _fallbackChat = GUILayout.BeginScrollView(_fallbackChat, GUILayout.Height(380));
                foreach (string line in bridge.Chat) GUILayout.Label(line); GUILayout.EndScrollView();
                _fallbackChoices = GUILayout.BeginScrollView(_fallbackChoices, GUILayout.Height(240));
                foreach (var choice in bridge.Choices) if (GUILayout.Button(choice.Text)) bridge.Choose(choice.Key);
                GUILayout.EndScrollView(); if (GUILayout.Button("Close")) bridge.Close(); GUILayout.EndArea();
            }
            finally { GUI.matrix = matrix; }
        }
    }
}
