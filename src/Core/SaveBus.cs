using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WildWasteland.Core
{
    /// <summary>
    /// The only watcher of the game's SaveLoadGame FSM (AGENTS.md 5.5). Reads ActiveStateName once per frame and raises:
    ///  - <see cref="StateChanged"/>(previous, current) for modules that used to poll the FSM themselves (ported watchers),
    ///  - IModule.OnSaveBegin ("SaveGame"), OnSaveEnd (back to isPlay after a save), OnLoadBegin ("LoadGame"),
    ///    OnWorldReady (first isPlay after LoadGame / setSeed / a new scene).
    /// <see cref="AfterSave"/> runs a callback ~1 s after the save returned to isPlay (ES3 cache rule: writes done earlier are overwritten
    /// by the game's cache flush). Slot = ES3Settings.defaultSettings.path, valid when it matches SaveGameN.es3.
    /// Module save data formats are kept verbatim from the source mods (save compatibility); see docs/decisions.md.
    /// </summary>
    public static class SaveBus
    {
        public delegate void StateChange(string previous, string current);
        /// <summary>Raised on every SaveLoadGame state change (also when the FSM is first found: previous = null).</summary>
        public static event StateChange StateChanged;
        // Save participants report swallowed IO/serialization failures so the host cannot publish a partial snapshot.
        public static void ReportFailure(string message) { HostSaveSystem.Fail(message); }
        internal static event Action<UI.ToolLayout> CleanupSettings;
        internal static void DrawCleanupSettings(UI.ToolLayout layout) { var draw = CleanupSettings; if (draw != null) draw(layout); }
        internal static event Action SaveCancelled;
        internal static void CancelSave()
        {
            _after.Clear(); Saving = false; _saveSlot = null;
            var cancel = SaveCancelled; if (cancel != null) cancel();
            var registry = GameRefs.NewGOArrayList != null ? FsmUtil.Find(GameRefs.NewGOArrayList, "Save_NewGO_ArrayList") : null;
            if (registry != null) registry.Fsm.SetState("off");
            if (GameRefs.SaveLoadGame != null) GameRefs.SaveLoadGame.Fsm.SetState("isPlay");
        }

        public static string State { get; private set; }
        public static bool Saving { get; private set; }
        public static bool Loading { get; private set; }
        public static int Generation { get; private set; }
        /// <summary>SaveLoadGame not in isPlay (a save or load is running). Spawns should wait.</summary>
        public static bool Busy { get { return State != null && State != "isPlay"; } }
        internal static bool PendingAfterSave { get { return _after.Count != 0; } }

        static bool _pendingWorldReady = true;
        static string _saveSlot;
        static readonly List<KeyValuePair<float, Action>> _after = new List<KeyValuePair<float, Action>>();
        static readonly Regex SlotRx = new Regex(@"^SaveGame([1-9]|10)\.es3$", RegexOptions.IgnoreCase);

        /// <summary>Current save slot file name ("SaveGame3.es3") or null.</summary>
        public static string Slot
        {
            get
            {
                try
                {
                    var p = ES3Settings.defaultSettings.path;
                    var n = string.IsNullOrEmpty(p) ? null : Path.GetFileName(p);
                    return n != null && (SlotRx.IsMatch(n) || IsMultiplayerSlot(n)) ? n : null;
                }
                catch { return null; }
            }
        }

        public static bool IsMultiplayerSlot(string name)
        {
            if (Multiplayer.Client || name == null || ((Multiplayer.Hosting || MultiplayerHosting.Pending) && name != Multiplayer.SaveFile) || name == "MP_client.es3"
                || !name.StartsWith("MP_", StringComparison.Ordinal) || !name.EndsWith(".es3", StringComparison.Ordinal)) return false;
            for (int i = 3; i < name.Length - 4; i++) if (!char.IsLetterOrDigit(name[i]) && name[i] != '-') return false;
            return name.Length > 7;
        }

        /// <summary>Run <paramref name="a"/> once, <paramref name="delay"/> unscaled seconds after the next isPlay following a save.</summary>
        public static void AfterSave(Action a, float delay = 1f) { _after.Add(new KeyValuePair<float, Action>(-delay, a)); }

        internal static void OnSceneLoaded() { Generation++; State = null; Saving = Loading = false; _pendingWorldReady = true; _after.Clear(); }

        /// <summary>Called once per frame by Core (EarlyUpdate).</summary>
        internal static void Tick()
        {
            var fsm = GameRefs.SaveLoadGame;
            string now = fsm != null && fsm.Fsm.Initialized ? fsm.ActiveStateName : null;
            if (now != State)
            {
                var prev = State;
                State = now;
                if (now != null) Changed(prev, now);
            }
            if (_after.Count > 0 && State == "isPlay")
            {
                float t = Time.unscaledTime;
                for (int i = _after.Count - 1; i >= 0; i--)
                {
                    var kv = _after[i];
                    if (kv.Key < 0) { _after[i] = new KeyValuePair<float, Action>(t - kv.Key, kv.Value); continue; }   // arm
                    if (t < kv.Key) continue;
                    _after.RemoveAt(i);
                    try { kv.Value(); } catch (Exception e) { Log.Error("SaveBus AfterSave callback", e); }
                }
            }
        }

        static void Changed(string prev, string now)
        {
            if (Log.Verbose) Log.Info("SaveLoadGame: " + (prev ?? "-") + " -> " + now);
            var h = StateChanged;
            if (h != null) foreach (StateChange d in h.GetInvocationList()) { try { d(prev, now); } catch (Exception e) { Log.Error("SaveBus.StateChanged handler " + d.Method.DeclaringType + "." + d.Method.Name, e); } }

            switch (now)
            {
                case "SaveGame":
                    Saving = true; _saveSlot = Slot;
                    ModuleHost.Broadcast(ModuleHost.Event.SaveBegin, _saveSlot);
                    break;
                case "LoadGame":
                    Generation++;
                    Loading = true; _pendingWorldReady = true; _after.Clear();
                    ModuleHost.Broadcast(ModuleHost.Event.LoadBegin, Slot);
                    break;
                case "setSeed":
                case "Start":
                    Generation++;
                    _pendingWorldReady = true;
                    break;
                case "isPlay":
                    if (Saving) { Saving = false; ModuleHost.Broadcast(ModuleHost.Event.SaveEnd, _saveSlot); }
                    if (_pendingWorldReady) { _pendingWorldReady = false; Loading = false; ModuleHost.Broadcast(ModuleHost.Event.WorldReady, Slot); }
                    break;
            }
        }
    }
}
