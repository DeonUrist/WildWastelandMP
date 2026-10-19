using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core.UI;
using CleanHud = WildWasteland.Modules.CleanHUD;

namespace WildWasteland.Core
{
    // Loaded plugins keep runners/coroutines/patches: disable through the installer's pre-load operation.
    internal static class ReplacementGuard
    {
        static readonly Dictionary<string, string> _status = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> _names = new List<string>();
        static bool _title;
        internal static bool MenuModalOpen;
        static int _failed;
        static GUIContent _message;
        static readonly GUIContent Heading = new GUIContent("RESTART REQUIRED");
        static readonly GUIContent MultiplayerRequired = new GUIContent("Apocalypter Multiplayer 0.4.1 is missing.\nInstall it from Nexus Mods, then restart the game.");
        static readonly string[,] Recommended = {
            { "apocalypter.worldamp", "Vista World Gen", "73" },
            { "com.fived.apocalypter.fallout_nv_radio", "Apocalypter Radio", "74" },
            { "local.apocalypter.chasecamera", "ApocaChaseCam", "44" },
            { "apocalypter.dogcompanion", "Dog Companion", "55" },
            { "com.denis.apocalypter.apocapocket", "Apocapocket", "7" }
        };
        static readonly List<int> _missing = new List<int>();
        static bool _checked, _missingMultiplayer;
        static ConfigEntry<bool> _showRecommended;
        static Vector2 _scroll;
        static readonly Action<Rect, string, float, Color, TextAnchor> NoticeText = CleanHud.GlyphFont.Draw, NoticeWrap = CleanHud.GlyphFont.DrawWrapped;
        static readonly Func<string, float, float, float> NoticeHeight = CleanHud.GlyphFont.WrappedHeight;

        internal static void Prepare()
        {
            _showRecommended = Plugin.Settings.Bind("Debug", "Recommended mods", false, "Show optional mod recommendations at the title menu.");
            foreach (var entry in ModuleHost.Entries)
            {
                var guids = entry.Module.ConflictsWith;
                if (guids != null) foreach (string guid in guids) Disable(guid);
            }
            Disable("local.immersivemap");
            Disable("com.denis.apocalypter.womenofwasteland");
            Disable(HealthStacks.GUID);
            Disable(ITweenGuard.GUID);
            Scheduler.Interval(null, "Core.Replacement notice", .5f, Tick).RunNow();
            Scheduler.Gui(null, "Core.Replacement notice", Draw, WantsNotice);
            RebuildNotice();
        }
        internal static void LateConflict(string guid) { Disable(guid); RebuildNotice(); }
        static void RebuildNotice()
        {
            if (_names.Count == 0) return;
            var text = new StringBuilder();
            text.Append(_names.Count).Append(_names.Count == 1 ? " standalone mod should be removed" : " standalone mods should be removed");
            text.Append(" from your active mods because Wild Wasteland replaces them:\n\n");
            for (int i = 0; i < _names.Count; i++) text.Append("- ").Append(_names[i]).Append('\n');
            if (_failed < _names.Count) text.Append("\nRestart the game. Apocasetter will disable the queued mods. Their files and configs are kept.");
            if (_failed != 0) text.Append("\n\nDisable the mods marked 'disable manually' before restarting. Automatic disabling was unavailable for them; see LogOutput.log for details.");
            _message = new GUIContent(text.ToString());
        }

        static void Disable(string guid)
        {
            if (_status.ContainsKey(guid)) return;
            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(guid, out info) || ReferenceEquals(info.Instance, null)) return;
            string error;
            bool queued = ApocasetterBridge.QueueDisable(info, out error);
            _status[guid] = queued ? "disable queued in Apocasetter; restart the game for Wild Wasteland to take over"
                : "automatic disable unavailable (" + error + "); disable the standalone mod and restart";
            _names.Add(info.Metadata.Name + (queued ? "" : " (disable manually)"));
            if (!queued) _failed++;
            Log.Warn("[Core] Replacing " + info.Metadata.Name + ": " + _status[guid] + ". Configs are preserved.");
        }

        internal static string Status(string guid)
        {
            string status;
            return _status.TryGetValue(guid, out status) ? status : "disable the standalone mod and restart";
        }

        internal static void SceneLoaded(Scene scene) { _title = scene.buildIndex == 0; }
        static void Tick()
        {
            SceneLoaded(SceneManager.GetActiveScene());
            if (_checked) return;
            _checked = true;
            PluginInfo multiplayer;
            _missingMultiplayer = !Chainloader.PluginInfos.TryGetValue("ApocalypterMP", out multiplayer) || multiplayer == null || ReferenceEquals(multiplayer.Instance, null);
            if (_missingMultiplayer) Log.Warn("[Core] Required multiplayer mod missing: https://www.nexusmods.com/apocalypter/mods/10");
            for (int i = 0; i < Recommended.GetLength(0); i++)
            {
                PluginInfo info;
                if (!Chainloader.PluginInfos.TryGetValue(Recommended[i, 0], out info) || ReferenceEquals(info.Instance, null)) _missing.Add(i);
            }
            Log.Always("[Core] Recommended mods not active: " + _missing.Count);
        }
        static bool WantsNotice() { return _title && (_missingMultiplayer || _message != null || _showRecommended.Value && _missing.Count > 0) && !MenuModalOpen && !LoadingProgress.ScreenVisible; }
        static void Draw()
        {
            if (LoadingProgress.ScreenVisible || MenuModalOpen || ToolWindow.IsOpen || EditorSession.Busy || ApocasetterBridge.InputBlocked) return;
            bool recommendations = _showRecommended.Value && _missing.Count > 0;
            if (!_missingMultiplayer && _message == null && !recommendations) return;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float width = 810f, inset = 30f;
            float requiredTextHeight = _missingMultiplayer ? NoticeHeight(MultiplayerRequired.text, 26f, width - inset * 2 - 30f) : 0;
            float requiredHeight = _missingMultiplayer ? requiredTextHeight + 171f : 0;
            float warningHeight = _message != null ? NoticeHeight(_message.text, 26f, width - inset * 2 - 30f) + 111f : 0;
            float recommendationsTop = requiredHeight + warningHeight;
            float contentHeight = recommendationsTop + (recommendations ? 141f + 54f * _missing.Count : 0);
            float height = Mathf.Min(contentHeight, Screen.height / scale - 540f);
            var panel = new Rect(Screen.width / scale - width - inset - 40f, Mathf.Max(inset + 40f, (Screen.height / scale - height) * .5f), width, height);
            int depth = GUI.depth; Color color = GUI.color; var matrix = GUI.matrix;
            var accent = CleanHud.CleanHudModule.Running ? CleanHud.AccentTheme.Resolve(CleanHud.DialogueWidgets.Gold) : CleanHud.DialogueWidgets.Gold;
            var border = new Color(214 / 255f, 203 / 255f, 176 / 255f, .55f);
            if (CleanHud.CleanHudModule.Running) border = CleanHud.AccentTheme.Resolve(border);
            var skin = Ledger.BeginSettings(accent, border, CleanHud.Assets.Tex("panel_menu"), CleanHud.Assets.VGradient(.75f), NoticeText, NoticeWrap, NoticeHeight);
            GUI.depth = -800; GUI.color = Color.white;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            try
            {
                Ledger.Window(panel);
                var view = new Rect(panel.x + inset, panel.y + 18f, width - inset * 2, height - 36f);
                float rowWidth = view.width - 30f;
                _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, rowWidth, contentHeight - 36f));
                if (_missingMultiplayer)
                {
                    Ledger.Label(new Rect(0, 3f, rowWidth, 45f), "REQUIRED MOD MISSING", Ledger.Heading, Ledger.Accent);
                    NoticeWrap(new Rect(0, 63f, rowWidth, requiredTextHeight), MultiplayerRequired.text, 26f, Ledger.Text, TextAnchor.MiddleLeft);
                    if (Ledger.Button(new Rect(0, requiredHeight - 90f, rowWidth, 54f), "Download Apocalypter Multiplayer", gameFont: true)) Application.OpenURL("https://www.nexusmods.com/apocalypter/mods/10");
                }
                if (_message != null)
                {
                    Ledger.Label(new Rect(0, requiredHeight + 3f, rowWidth, 45f), Heading.text, Ledger.Heading, Ledger.Accent);
                    NoticeWrap(new Rect(0, requiredHeight + 63f, rowWidth, warningHeight - 96f), _message.text, 26f, Ledger.Text, TextAnchor.MiddleLeft);
                }
                if (recommendations)
                {
                    Ledger.Label(new Rect(0, recommendationsTop + 3f, rowWidth, 45f), "RECOMMENDED MODS", Ledger.Heading, Ledger.Accent);
                    Ledger.Label(new Rect(0, recommendationsTop + 54f, rowWidth, 51f), "Optional additions (not active). Click to view mod.", Ledger.Small, Ledger.Muted);
                    for (int n = 0; n < _missing.Count; n++)
                    {
                        int i = _missing[n];
                        var row = new Rect(0, recommendationsTop + 105f + 54f * n, rowWidth, 54f);
                        if (Ledger.Button(row, Recommended[i, 1], gameFont: true)) Application.OpenURL("https://www.nexusmods.com/apocalypter/mods/" + Recommended[i, 2]);
                    }
                }
                GUI.EndScrollView();
            }
            finally { Ledger.EndSettings(); GUI.skin = skin; GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
        }
    }
}
