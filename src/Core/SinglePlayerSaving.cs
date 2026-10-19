using System;
using System.Collections;
using System.IO;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Core
{
    // Offline uses the native serializer and the same journal/publication path, without network ownership steps.
    internal static class SinglePlayerSaving
    {
        internal static bool Serializing { get; private set; }
        static bool _requested;
        static Func<string, int, bool> _launch;
        static Action<string> _activeFile;
        internal static void Install()
        {
            var launcher = Multiplayer.Type("Game.GameLauncher");
            _launch = MultiplayerAccess.Call<Func<string, int, bool>>(launcher, "Launch", typeof(string), typeof(int));
            _activeFile = MultiplayerAccess.Call<Action<string>>(launcher, "set_ActiveSaveFile", typeof(string));
            SaveBus.StateChanged += StateChanged;
            Hooks.GetButtonDownUpdate.Prefix(Multiplayer.PatchOwner, ButtonDown);
            Hooks.GetButtonDo.Prefix(Multiplayer.PatchOwner, Button);
            Hooks.GetButtonUpUpdate.Prefix(Multiplayer.PatchOwner, ButtonUp);
            Hooks.GetMouseButtonDownUpdate.Prefix(Multiplayer.PatchOwner, MouseDown);
        }
        static bool Block(FsmBool result) { if (!Serializing) return true; if (result != null) result.Value = false; return false; }
        static bool ButtonDown(GetButtonDown action) { return Block(action.storeResult); }
        static bool Button(GetButton action) { return Block(action.storeResult); }
        static bool ButtonUp(GetButtonUp action) { return Block(action.storeResult); }
        static bool MouseDown(GetMouseButtonDown action) { return Block(action.storeResult); }
        static void StateChanged(string previous, string current)
        {
            if (current != "setSeed" || Multiplayer.SessionActive || MultiplayerHosting.Pending) return;
            // A new offline world must never inherit the previous world's cache/lineage or overwrite its slot.
            string file = "MP_sp-live-" + Guid.NewGuid().ToString("N") + ".es3";
            ES3Settings.defaultSettings.path = file; _activeFile(file);
            var variable = GameRefs.SaveLoadGame != null ? GameRefs.SaveLoadGame.FsmVariables.FindFsmString("SaveFile") : null;
            if (variable != null) variable.Value = file;
        }
        internal static bool Request()
        {
            if (Multiplayer.SessionActive || _requested || Serializing || !HostSaveSystem.LocalReady) return false;
            _requested = true; Scheduler.StartCoroutine(Save()); return true;
        }
        static IEnumerator Save()
        {
            float scale = Time.timeScale; int generation = SaveBus.Generation; bool completed = false, blocked = false;
            try
            {
                // Let an originating native multi-root save action finish being suppressed before starting ours.
                yield return null;
                if (generation != SaveBus.Generation || !HostSaveSystem.LocalReady) yield break;
                if (HostSaveSystem.DeferScheduledSave()) yield break;
                string file = HostSaveSystem.ActiveFile;
                var registry = GameRefs.NewGOArrayList != null ? FsmUtil.Find(GameRefs.NewGOArrayList, "Save_NewGO_ArrayList") : null;
                if (registry == null) throw new IOException("Native object registry is unavailable");
                GameRefs.SaveLoadGame.FsmVariables.FindFsmString("SaveFile").Value = file;
                registry.FsmVariables.FindFsmString("SaveFile").Value = file;
                ES3Settings.defaultSettings.path = file;
                Serializing = true; Time.timeScale = 0;
                blocked = !ApocasetterBridge.InputBlocked && ApocasetterBridge.SetInputBlock(true);
                HostSaveSystem.Begin(); HostSaveSystem.Stamp();
                // Activate objects exactly as the native save menu does before serializing all roots.
                var activate = FsmUtil.Find(GameRefs.GameManager, "SaveGameActivateItems"); if (activate != null) activate.SendEvent("Activate");
                PlayMakerFSM.BroadcastEvent("SaveGame");
                float deadline = Time.unscaledTime + 120f; bool observed = false;
                while (SaveBus.Generation == generation && Time.unscaledTime < deadline)
                {
                    Time.timeScale = 0;
                    if (SaveBus.Saving || SaveBus.State == "SaveGame") observed = true;
                    if (observed && SaveBus.State == "isPlay" && registry.ActiveStateName == "off" && !SaveBus.PendingAfterSave) break;
                    yield return null;
                }
                if (!observed || SaveBus.Generation != generation || SaveBus.State != "isPlay" || registry.ActiveStateName != "off" || SaveBus.PendingAfterSave)
                    throw new IOException("Offline serialization did not finish; previous files retained");
                completed = HostSaveSystem.Complete();
            }
            finally
            {
                if (!completed && (HostSaveSystem.Busy || HostSaveSystem.Requested)) HostSaveSystem.Abort(new IOException("Offline save did not finish; previous files retained"));
                Serializing = _requested = false;
                if (blocked) ApocasetterBridge.SetInputBlock(false);
                if (SaveBus.Generation == generation) Time.timeScale = scale;
                if (completed) HostSaveSystem.Notify(HostSaveSystem.Status);
            }
        }
        internal static void Load(string file)
        {
            if (Multiplayer.SessionActive || !HostSaveSystem.Ready || HostSaveSystem.RecoveryError != null || HostSaveSystem.Busy || HostSaveSystem.Requested) return;
            if (!MultiplayerHosting.IsSaveFile(file) || !File.Exists(HostSaveFiles.WorldPath(file)) || HostSaveSystem.Info(file).Multiplayer) return;
            if (!Multiplayer.InMainMenu)
            { if (HostSaveSystem.LocalReady) Scheduler.StartCoroutine(ReturnAndLoad(file)); return; }
            if (!_launch(file, 0)) HostSaveSystem.Notify("The save could not be loaded. Check the log.");
        }
        static IEnumerator ReturnAndLoad(string file)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            while (!Multiplayer.InMainMenu) yield return null;
            yield return null; yield return null;
            if (!_launch(file, 0)) HostSaveSystem.Notify("The save could not be loaded. Open LOAD GAME to retry.");
        }
    }
}
