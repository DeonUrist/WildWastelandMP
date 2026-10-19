using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.UI;

namespace WildWasteland.Core
{
    // Only the host writes the world. The ordered peer marker follows its item/player flush.
    internal static class MultiplayerSaving
    {
        internal static bool Frozen { get; private set; }
        static bool _serializing, _preparing, _wroteWorld, _snapshotPrepared;
        static float _scale, _warn;
        static float _phaseRetry;
        static ulong _token;
        static byte _phase;
        static IEnumerator _flush;
        static bool _flushed;
        static int _generation;
        static bool _hostRole;
        static bool _completed;
        static Func<ArrayList> _itemList;
        static Action<object, bool> _saving;
        static object[] _excluded;
        static Func<object, Vector3> _position;
        static Func<object, Quaternion> _rotation;
        static readonly HashSet<ulong> Waiting = new HashSet<ulong>();
        static readonly List<ulong> Gone = new List<ulong>();
        static Action _report;
        static Func<string, Action, IEnumerator> _save;
        static bool _blocked;
        static Button _saveButton;
        static Text _saveLabel;
        static bool _saveInteractable, _saveCaptured;
        static Color _saveColor;
        static float _nextUi;
        static Func<ulong, string> _playerName;
        static Action<string> _notice;
        static float _requestAt;
        static readonly Dictionary<ulong, float> SaveRequests = new Dictionary<ulong, float>();
        internal static void Install()
        {
            var saver = Multiplayer.Type("Worlds.WorldSaver");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Net.NetSession"), "OnPeerDisconnected"), transpiler: AccessTools.Method(typeof(MultiplayerSaving), "DisconnectCheckpointOnly")))
                throw new InvalidOperationException("MP disconnect world-save suppression hook failed");
            _playerName = MultiplayerAccess.Call<Func<ulong, string>>(Multiplayer.Type("Net.NetSession"), "NameOf", typeof(ulong));
            _notice = MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("UI.Chat"), "Notice", typeof(string));
            _itemList = MultiplayerAccess.Call<Func<ArrayList>>(Multiplayer.Type("Net.WorldObjects"), "GameItemList", Type.EmptyTypes);
            _saving = MultiplayerAccess.Set<bool>(saver, "<Saving>k__BackingField");
            var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
            _position = MultiplayerAccess.Get<Vector3>(tracked, "Position"); _rotation = MultiplayerAccess.Get<Quaternion>(tracked, "Rotation");
            _report = MultiplayerAccess.Call<Action>(Multiplayer.Type("Net.NetSession"), "SendPlayerSave", Type.EmptyTypes);
            _save = MultiplayerAccess.Call<Func<string, Action, IEnumerator>>(saver, "Save", typeof(string), typeof(Action));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(saver, "Save"), prefix: AccessTools.Method(typeof(MultiplayerSaving), "DeferCallback"), postfix: AccessTools.Method(typeof(MultiplayerSaving), "Wrap"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Game.PauseControl"), "Tick"), prefix: AccessTools.Method(typeof(MultiplayerSaving), "PauseTick"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(PlayMakerFSM), "BroadcastEvent", new[] { typeof(FsmEvent) }), prefix: AccessTools.Method(typeof(MultiplayerSaving), "Broadcast")))
                throw new InvalidOperationException("MP host save barrier could not be installed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(saver, "Tick"), prefix: AccessTools.Method(typeof(MultiplayerSaving), "NotFrozen"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(typeof(Button), "Press"), prefix: AccessTools.Method(typeof(MultiplayerSaving), "BeforePress")))
                throw new InvalidOperationException("MP save input guard could not be installed");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(Multiplayer.Type("Net.WorldObjects"), "ExcludeFromSave"), prefix: AccessTools.Method(typeof(MultiplayerSaving), "BeforeExclude")))
                throw new InvalidOperationException("MP save exclusion cleanup hook failed");
            var world = Multiplayer.Type("Net.WorldObjects");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "FollowTarget"), prefix: AccessTools.Method(typeof(MultiplayerSaving), "FrozenPose"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "SampleCar"), transpiler: AccessTools.Method(typeof(MultiplayerSaving), "FrozenCarSample")))
                throw new InvalidOperationException("MP frozen object/car pose hooks failed");
            Hooks.SendEventEnter.Prefix(Multiplayer.PatchOwner, SendEvent);
            Multiplayer.Message += Receive; Multiplayer.SessionChanged += Changed;
            SaveBus.StateChanged += SaveState;
            Scheduler.Gui(Multiplayer.PatchOwner, "Server save fallback screen", DrawFallback, WantsFallback);
            Log.Always("[Multiplayer] Vehicle saves ready: latest owner car pose and ordered passenger reports");
        }
        static IEnumerable<CodeInstruction> DisconnectCheckpointOnly(IEnumerable<CodeInstruction> instructions)
        {
            var request = AccessTools.Method(Multiplayer.Type("Worlds.WorldSaver"), "Request"); int count = 0;
            foreach (var code in instructions)
            {
                // OnPeerLeft already captures the departing player's state and writes its checkpoint.
                if (code.Calls(request)) { code.opcode = OpCodes.Nop; code.operand = null; count++; }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected MP disconnect save requests: " + count);
        }
        static void SaveState(string previous, string current) { if (_serializing && current == "SaveGame") _wroteWorld = true; }
        static void Changed() { SaveRequests.Clear(); _requestAt = 0; if (!Multiplayer.Active) { Waiting.Clear(); _preparing = _serializing = false; DisposeFlush(); SetFrozen(false); RestoreSaveButton(); } }
        static void DisposeFlush() { var flush = _flush; _flush = null; (flush as IDisposable)?.Dispose(); }
        static void BeforeExclude(bool __1)
        { if (_serializing && __1 && _excluded == null) { var list = _itemList(); if (list != null) _excluded = list.ToArray(); } }
        static void RestoreExcluded()
        {
            var excluded = _excluded; _excluded = null; if (excluded == null) return;
            var list = _itemList(); if (list == null) return;
            foreach (var item in excluded) { var go = item as GameObject; if (go != null && !list.Contains(go)) list.Add(go); }
        }
        static bool PauseTick() { if (!Frozen && !SinglePlayerSaving.Serializing) return true; Time.timeScale = 0f; return false; }
        static bool NotFrozen() { return !Multiplayer.Hosting && !Frozen; } // SYSTEM owns the sole host autosave clock.
        static bool BeforePress(Button __instance)
        {
            if (Frozen || SinglePlayerSaving.Serializing) return false;
            if (HostSaveSystem.RecoveryError != null && __instance != null && __instance.name.StartsWith("load_game_", StringComparison.Ordinal)) return false;
            if (HostSaveSystem.Ready && !Multiplayer.Client && __instance != null && (__instance.name == "LoadGame" || __instance.name == "LoadGameSlots" || __instance.name.StartsWith("load_game_", StringComparison.Ordinal))) { UI.ToolWindow.OpenSaves(); return false; }
            if (!Multiplayer.Client && HostSaveSystem.LocalWorld && __instance != null && (__instance.name == "Save_Game" || __instance.name == "Save_Game_slots") && __instance.transform.parent != null && __instance.transform.parent.name == "MainMenu") { UI.ToolWindow.OpenSaves(true); return false; }
            if (Multiplayer.Client && __instance != null && (__instance == _saveButton || (__instance.name == "Save_Game" || __instance.name == "Save_Game_slots") && __instance.transform.parent != null && __instance.transform.parent.name == "MainMenu")) { RequestClientSave(); return false; }
            return true;
        }
        static bool FrozenPose(object __0)
        {
            if (!Frozen) return true;
            var root = Multiplayer.TrackedObject(__0);
            if (root != null) root.transform.SetPositionAndRotation(Origin.Scene(_position(__0)), _rotation(__0));
            return false;
        }
        static IEnumerable<CodeInstruction> FrozenCarSample(IEnumerable<CodeInstruction> instructions, ILGenerator generator, System.Reflection.MethodBase __originalMethod)
        {
            // Cars have their own accepted snapshot buffer; Tracked.Position can still be the spawn pose.
            var codes = new List<CodeInstruction>(instructions); if (codes.Count == 0) throw new InvalidOperationException("Empty native car sampler");
            var list = __originalMethod.GetParameters()[0].ParameterType; var resume = generator.DefineLabel(); codes[0].labels.Add(resume);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(MultiplayerSaving), "Frozen"));
            yield return new CodeInstruction(OpCodes.Brfalse, resume);
            yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(list, "Count"));
            yield return new CodeInstruction(OpCodes.Ldc_I4_1); yield return new CodeInstruction(OpCodes.Sub);
            yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(list, "Item"));
            yield return new CodeInstruction(OpCodes.Ret);
            foreach (var code in codes) yield return code;
        }
        static void SetFrozen(bool value)
        {
            if (value == Frozen) return;
            if (value) { _scale = Time.timeScale; Frozen = true; Time.timeScale = 0f; _blocked = !ApocasetterBridge.InputBlocked && ApocasetterBridge.SetInputBlock(true); }
            else { Frozen = false; Time.timeScale = _scale; if (_blocked) ApocasetterBridge.SetInputBlock(false); _blocked = false; }
            Log.Always("[Multiplayer] Server save: " + (value ? "gameplay paused" : "gameplay resumed"));
        }
        static bool AllowSave()
        {
            if (!Multiplayer.Active)
            {
                if (SinglePlayerSaving.Serializing || !HostSaveSystem.LocalWorld) return true;
                if (HostSaveSystem.LocalReady) HostSaveSystem.Request("Save " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                return false;
            }
            if (_serializing)
            {
                // Put player records into the same cache before any native world flush can reach disk.
                if (!_snapshotPrepared) { MultiplayerRecovery.SaveSnapshot(Multiplayer.SaveFile); HostSaveSystem.Stamp(); _snapshotPrepared = true; }
                return true;
            }
            if (Multiplayer.Hosting && !_preparing) HostSaveSystem.Request("Manual " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return false;
        }
        internal static bool RequestSave()
        {
            if (!Multiplayer.Hosting || Frozen || !Multiplayer.WorldReady) return false;
            string file = Multiplayer.SaveFile; var fsm = GameRefs.SaveLoadGame;
            if (!MultiplayerHosting.IsSaveFile(file) || fsm == null) return false;
            var slot = fsm.FsmVariables.FindFsmString("SaveFile"); if (slot == null) return false;
            slot.Value = file; ES3Settings.defaultSettings.path = file;
            var list = GameRefs.NewGOArrayList != null ? FsmUtil.Find(GameRefs.NewGOArrayList, "Save_NewGO_ArrayList") : null;
            var listFile = list != null ? list.FsmVariables.FindFsmString("SaveFile") : null; if (listFile != null) listFile.Value = file;
            Scheduler.StartCoroutine(_save("host request", null));
            return true;
        }
        internal static void RequestClientSave()
        {
            if (!Multiplayer.Client || !Multiplayer.WorldReady || Frozen || Time.unscaledTime < _requestAt) return;
            _requestAt = Time.unscaledTime + 5f;
            Multiplayer.Send(142, EmptySaveRequest); _notice("Save request sent to the host.");
        }
        static void EmptySaveRequest(BinaryWriter writer) { }
        static bool Broadcast(FsmEvent __0) { return __0 == null || __0.Name != "SaveGame" || AllowSave(); }
        static bool SendEvent(HutongGames.PlayMaker.Actions.SendEvent action)
        {
            if (action.sendEvent == null || action.sendEvent.Name != "SaveGame" || AllowSave()) return true;
            action.Finish(); return false;
        }
        static void DeferCallback(ref Action __1, out Action __state) { __state = null; if (Multiplayer.Active) { __state = __1; __1 = null; } }
        static void Wrap(ref IEnumerator __result, Action __state) { if (Multiplayer.Active) __result = Save(__result, __state); }
        static IEnumerator Save(IEnumerator original, Action after)
        {
            // A host reset must finish safe owner placement before the save barrier freezes those owners.
            while (Multiplayer.Hosting && MultiplayerPlayerReset.Pending) yield return null;
            if (!Multiplayer.Hosting || _preparing || _serializing) yield break;
            if (HostSaveSystem.DeferScheduledSave()) yield break;
            _preparing = true; _token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            _generation = SaveBus.Generation; _hostRole = true;
            _wroteWorld = _snapshotPrepared = _completed = false;
            SetFrozen(true); _warn = Time.unscaledTime + 15f;
            try
            {
                // Freeze and drain queued hits before taking the final owner snapshots.
                StartPhase(1);
                while (Multiplayer.Hosting && (Waiting.Count != 0 || MultiplayerParts.Pending || MultiplayerEdits.Busy)) { Prune(); Warn(); yield return null; }
                if (!Multiplayer.Hosting) yield break;
                StartPhase(2);
                while (Multiplayer.Hosting && (Waiting.Count != 0 || MultiplayerParts.Pending || MultiplayerEdits.Busy)) { Prune(); Warn(); yield return null; }
                if (!Multiplayer.Hosting) yield break;
                // MP glides remote poses in real time; let the acknowledged final samples reach the saved transforms.
                yield return new WaitForSecondsRealtime(.35f);
                while (MultiplayerRecovery.CheckpointWriting) yield return null;
                if (HostSaveSystem.DeferScheduledSave()) yield break;
                if (!BeginTransaction()) yield break;
                _serializing = true; _preparing = false;
                Log.Always("[Multiplayer] Server save: all connected owners flushed; writing host world");
                while (Multiplayer.Hosting && _generation == SaveBus.Generation && Advance(original)) yield return original.Current;
                while (Multiplayer.Hosting && _generation == SaveBus.Generation && SaveBus.State == "isPlay" && SaveBus.PendingAfterSave) yield return null;
                if (_wroteWorld && Multiplayer.Hosting && _generation == SaveBus.Generation && SaveBus.State == "isPlay" && Multiplayer.SaveFile != null)
                { _completed = HostSaveSystem.Complete(); }
            }
            finally
            {
                try { (original as IDisposable)?.Dispose(); }
                finally
                {
                    try
                    {
                        if (!_completed && (HostSaveSystem.Busy || HostSaveSystem.Requested)) HostSaveSystem.Abort(new IOException("The synchronized world save did not finish; previous files retained."));
                        if (_generation == SaveBus.Generation) { RestoreExcluded(); _saving(null, false); } else _excluded = null;
                    }
                    finally
                    {
                        if (Multiplayer.Hosting) Multiplayer.Send(100, w => { w.Write(_token); w.Write((byte)0); });
                        Waiting.Clear(); _preparing = _serializing = false; SetFrozen(false);
                        if (_completed) { HostSaveSystem.Notify(HostSaveSystem.Status); MultiplayerRecovery.Checkpoint(); if (after != null) after(); }
                    }
                }
            }
        }
        static bool BeginTransaction()
        {
            try { HostSaveSystem.Begin(); return true; }
            catch (Exception error) { HostSaveSystem.Abort(error); return false; }
        }
        static bool Advance(IEnumerator original)
        {
            try { return original.MoveNext(); }
            catch (Exception error) { HostSaveSystem.Fail(error.ToString()); return false; }
        }
        static void StartPhase(byte phase)
        {
            _phase = phase; Waiting.Clear();
            _phaseRetry = Time.unscaledTime + 1f;
            foreach (ulong peer in MultiplayerReliable.ConnectedPeers()) Waiting.Add(peer);
            Multiplayer.Send(100, w => { w.Write(_token); w.Write(phase); });
        }
        static void Prune()
        {
            Gone.Clear(); var peers = MultiplayerReliable.ConnectedPeers();
            foreach (ulong peer in Waiting) if (!peers.Contains(peer)) Gone.Add(peer);
            foreach (ulong peer in Gone) Waiting.Remove(peer);
            if (Time.unscaledTime >= _phaseRetry)
            {
                _phaseRetry = Time.unscaledTime + 1f;
                foreach (ulong peer in Waiting) Multiplayer.Send(100, w => { w.Write(_token); w.Write(_phase); }, peer);
            }
        }
        static void Warn()
        {
            if (Time.unscaledTime < _warn) return;
            _warn = Time.unscaledTime + 15f;
            Log.Warn("[Multiplayer] Server save waiting for owner state; gameplay stays paused (no partial save)");
        }
        internal static void Tick()
        {
            SaveButton();
            if (Frozen && (_generation != SaveBus.Generation || _hostRole != Multiplayer.Hosting || !Multiplayer.Active))
            { DisposeFlush(); Waiting.Clear(); _preparing = _serializing = false; _excluded = null; SetFrozen(false); }
            if (Frozen) Time.timeScale = 0f;
            if (!Multiplayer.Client || _flush == null || !Multiplayer.WorldReady || MultiplayerParts.Pending || !MultiplayerParty.PlayerReady) return;
            try { if (_flush.MoveNext()) return; DisposeFlush(); _report(); _flushed = true; }
            catch (Exception error) { _flushed = false; Log.Error("[Multiplayer] Owner save flush failed; waiting for a retry", error); DisposeFlush(); return; }
            Multiplayer.Send(101, w => { w.Write(_token); w.Write(_phase); });
        }
        static bool WantsFallback() { return (Frozen || SinglePlayerSaving.Serializing) && !LoadingProgress.ScreenVisible; }
        static void DrawFallback()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            WildWasteland.Modules.CleanHUD.GlyphFont.DrawGame(new Rect(0, 0, Screen.width, Screen.height), "SAVING...", 36 * Screen.height / 1080f, Color.white, TextAnchor.MiddleCenter);
        }
        static void RestoreSaveButton()
        {
            if (_saveCaptured && _saveButton != null) _saveButton.interactable = _saveInteractable;
            if (_saveCaptured && _saveLabel != null) _saveLabel.color = _saveColor;
            _saveCaptured = false;
        }
        static void SaveButton()
        {
            if (!Multiplayer.Client) { RestoreSaveButton(); return; }
            if (_saveButton == null && Time.unscaledTime >= _nextUi)
            {
                _nextUi = Time.unscaledTime + .5f;
                var root = GameObject.Find("MainMenu_Canvas"); var row = root != null ? root.transform.Find("MainMenu/Save_Game") : null;
                if (row != null) { _saveButton = row.GetComponent<Button>(); _saveLabel = row.GetComponentInChildren<Text>(true); _saveCaptured = false; }
            }
            if (_saveButton == null) return;
            if (!_saveCaptured) { _saveCaptured = true; _saveInteractable = _saveButton.interactable; if (_saveLabel != null) _saveColor = _saveLabel.color; }
            _saveButton.interactable = !Frozen; if (_saveLabel != null) _saveLabel.color = _saveColor;
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == 142)
            {
                if (!Multiplayer.Hosting || from == 0 || Multiplayer.PlayerFor(from) == null || reader.BaseStream.Position != reader.BaseStream.Length) return;
                float last; if (SaveRequests.TryGetValue(from, out last) && Time.unscaledTime - last < 5f) return;
                SaveRequests[from] = Time.unscaledTime;
                string message = _playerName(from) + " wants to save"; _notice(message); Log.Always("[Multiplayer] " + message);
                return;
            }
            if (kind != 100 && kind != 101) return;
            ulong token = reader.ReadUInt64(); byte phase = reader.ReadByte();
            if (reader.BaseStream.Position != reader.BaseStream.Length || phase > 2) return;
            if (kind == 101)
            { if (Multiplayer.Hosting && token == _token && phase == _phase) Waiting.Remove(from); return; }
            if (!Multiplayer.Client || from != 0) return;
            if (phase == 0) { if (token == _token) { DisposeFlush(); SetFrozen(false); } return; }
            if (Frozen && token == _token && phase == _phase)
            {
                if (_flush == null)
                { if (_flushed) Multiplayer.Send(101, w => { w.Write(_token); w.Write(_phase); }); else _flush = MultiplayerItems.FlushOwned(); }
                return;
            }
            _token = token; _phase = phase; SetFrozen(true);
            _generation = SaveBus.Generation; _hostRole = false;
            _flushed = false; DisposeFlush(); _flush = MultiplayerItems.FlushOwned();
        }
    }
}
