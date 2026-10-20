using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildWasteland.Core
{
    // Standalone native-menu adapters. No multiplayer assembly, player identity or redirected save root.
    internal static class OfflineSaveRuntime
    {
        internal static string ActiveFile, SelectedFile;
        static PlayMakerFSM _health, _inCar;
        static bool _warnedHealth;
        static bool _leaving;
        static int _latestVersion = -1;
        static string _latestFile;
        internal static bool InMainMenu { get { return SceneManager.GetActiveScene().name == "MainMenu"; } }
        internal static bool InCar
        {
            get { if (_inCar == null && GameRefs.Player != null) _inCar = FsmUtil.Find(GameRefs.Player, "InCar"); return _inCar != null && _inCar.ActiveStateName != "OnFoot"; }
        }
        internal static bool IsSaveFile(string file) { return MultiplayerHosting.IsSaveFile(file); }
        internal static float Health()
        {
            if (_health == null && GameRefs.Player != null) _health = FsmUtil.Find(GameRefs.Player, "Health");
            var value = _health != null ? _health.FsmVariables.FindFsmFloat("Health") : null;
            if (value != null) return value.Value;
            // Match native PlayerVitals.Health's default; absence is not evidence of death.
            if (!_warnedHealth) { _warnedHealth = true; Log.Warn("[System] Player Health FSM is unavailable; using the native 100 HP fallback for save eligibility"); }
            return 100f;
        }
        internal static float PlayerDistance(Vector3 position, Transform player) { return player != null ? Vector3.Distance(position, player.position) : float.PositiveInfinity; }
        internal static void SetActive(string file) { if (!IsSaveFile(file)) throw new IOException("Invalid save identity"); ActiveFile = file; }
        internal static void Install()
        {
            Hooks.ProcessEvent.Prefix(null, NativeMenuEvent);
            SaveBus.StateChanged += StateChanged;
            SceneManager.sceneLoaded += SceneLoaded;
            Scheduler.Gui(null, "Native saving fallback", DrawSaving, WantsSaving);
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        { _health = _inCar = null; _warnedHealth = false; if (scene.name == "MainMenu") { ActiveFile = null; HostSaveSystem.CatalogueVersion++; } }
        static void StateChanged(string previous, string current)
        {
            if (current == "LoadGame")
            {
                string file = SelectedFile ?? SaveBus.Slot;
                if (IsSaveFile(file)) SetActive(file);
            }
            if (current == "isPlay") SelectedFile = null;
        }
        internal static IEnumerable<HostSaveSystem.SaveInfo> Saves()
        {
            if (!Directory.Exists(HostSaveFiles.WorldRoot)) yield break;
            foreach (string path in Directory.GetFiles(HostSaveFiles.WorldRoot, "*.es3"))
            {
                string file = Path.GetFileName(path);
                if (!IsSaveFile(file) || (file.StartsWith("MP_sp-live-", StringComparison.OrdinalIgnoreCase) || HostSaveSystem.Info(file).Multiplayer) || new FileInfo(path).Length <= 1024) continue;
                yield return HostSaveSystem.Info(file);
            }
        }
        internal static string Latest()
        {
            if (_latestVersion == HostSaveSystem.CatalogueVersion) return _latestFile;
            _latestVersion = HostSaveSystem.CatalogueVersion;
            string file = null; DateTime best = DateTime.MinValue;
            foreach (var save in Saves()) { var stamp = File.GetLastWriteTimeUtc(HostSaveFiles.WorldPath(save.File)); if (stamp > best) { best = stamp; file = save.File; } }
            _latestFile = file; return file;
        }
        internal static bool Launch(string file)
        {
            if (!InMainMenu || !IsSaveFile(file) || !File.Exists(HostSaveFiles.WorldPath(file))) return false;
            // Use the native loadgame FSM: it owns scene loading, global restoration and terrain admission.
            var canvas = GameObject.Find("LoadGame_Canvas");
            var node = canvas != null ? canvas.transform.Find("LoadGame/loadgame") : null;
            var fsm = node != null ? FsmUtil.Find(node.gameObject, "NewGame") : null;
            if (fsm == null || !fsm.Fsm.Initialized) return false;
            var variable = fsm.FsmVariables.FindFsmString("SaveFile"); if (variable == null) return false;
            SelectedFile = file; SetActive(file); ES3Settings.defaultSettings.path = file; variable.Value = file;
            fsm.gameObject.SetActive(true); fsm.enabled = true; fsm.SendEvent("Clicked"); return true;
        }
        static bool NativeMenuEvent(Fsm fsm, FsmEvent ev, FsmEventData data)
        {
            if (Multiplayer.Present || !HostSaveSystem.Installed || fsm == null || ev == null || ev.Name != "Clicked" || fsm.GameObject == null || SinglePlayerSaving.Serializing) return true;
            string name = fsm.GameObject.name;
            if (HostSaveSystem.LocalReady && fsm.Name == "Continue" && (name.StartsWith("save_game_", StringComparison.Ordinal) || name == "savegame" || name == "Yes_Save" || name == "Save_Game_slots" || name == "Save_Game"))
            { UI.ToolWindow.OpenSaves(true); return false; }
            if (HostSaveSystem.LocalReady && !_leaving && name == "Yes_Quit")
            {
                HostSaveSystem.SaveBeforeExit(() => { _leaving = true; try { fsm.Event("Clicked"); } finally { _leaving = false; } }); return false;
            }
            if (InMainMenu && fsm.Name == "NewGame" && name == "loadgame")
            {
                if (!HostSaveSystem.Ready) { UI.ToolWindow.OpenSaves(false); return false; }
                var selected = fsm.Variables.FindFsmString("SaveFile");
                if (selected != null && IsSaveFile(selected.Value)) { SelectedFile = selected.Value; SetActive(selected.Value); }
            }
            if (InMainMenu && fsm.Name == "NewGame" && name == "LoadGameSlots")
            { UI.ToolWindow.OpenSaves(false); return false; }
            // Older native single-file LOAD/Continue buttons also choose the newest published SP snapshot.
            if (InMainMenu && fsm.Name == "NewGame" && name == "LoadGame" && fsm.GameObject.transform.parent != null && fsm.GameObject.transform.parent.name == "MainMenu")
            { var file = Latest(); if (file != null) Launch(file); return false; }
            return true;
        }
        static bool WantsSaving() { return SinglePlayerSaving.Serializing; }
        static void DrawSaving()
        {
            if (LoadingProgress.ScreenVisible || Event.current.type != EventType.Repaint) return;
            int depth = GUI.depth; GUI.depth = -10000; var color = GUI.color;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = color;
            Modules.CleanHUD.GlyphFont.DrawGame(new Rect(0, Screen.height * .42f, Screen.width, 90), HostSaveSystem.AutomaticSaving ? "AUTOSAVING" : "SAVING", 48 * Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), new Color(.886f, .749f, .424f), TextAnchor.MiddleCenter);
            GUI.depth = depth;
        }
    }
}
