using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // The active native file is a working location. Published manual saves and autosaves have separate identities.
    internal static class HostSaveSystem
    {
        internal const string CommitKey = "WildWastelandMP.SaveCommit", PlayerKey = "WildWastelandMP.PlayerSnapshot";
        internal sealed class SaveInfo
        {
            internal string File, Name, Kind, Date;
            internal string World;
            internal bool Multiplayer;
            internal int? Players;
            internal float? Kilometres;
            internal bool Automatic { get { return Kind == "auto"; } }
            internal string Distance { get { return Kilometres.HasValue ? Kilometres.Value.ToString("0.0", CultureInfo.InvariantCulture) + " km" : "Distance not recorded"; } }
        }
        internal static ConfigEntry<bool> Autosave, WhileDriving, Screenshots;
        internal static ConfigEntry<int> Interval, Warning, AutoKeep, BackupKeep;
        internal static string Status = "", RecoveryError;
        internal static bool ReloadRequired;
        internal static bool Busy { get { return _transaction != null; } }
        internal static bool Requested { get { return _requested; } }
        internal static bool Exiting { get { return _quitting; } }
        internal static bool Ready, Installed;
        internal static bool AutomaticSaving { get { return _automatic; } }
        internal static bool LocalWorld { get { return !Multiplayer.Client && (!Multiplayer.SessionActive || Multiplayer.Hosting) && !Multiplayer.InMainMenu && GameRefs.Player != null && GameRefs.SaveLoadGame != null; } }
        internal static bool LocalReady { get { return LocalWorld && SaveBus.State == "isPlay"; } }
        internal static bool LocalUnsafe { get { return _health() < 1 || Multiplayer.Active && (!Multiplayer.LocalTargetable || MultiplayerRespawn.Unsafe); } }
        internal static string ResourceProblem
        {
            get
            {
                if (!Multiplayer.Active) return null;
                return MultiplayerItems.ResourceFailure ?? (MultiplayerItems.ResourceBusy ? "Saving deferred: finish the current shared item interaction, or release the drink control, then try again." : null);
            }
        }
        internal static string ActiveFile { get { return Multiplayer.Hosting ? Multiplayer.SaveFile : !Multiplayer.Present ? OfflineSaveRuntime.ActiveFile ?? SaveBus.Slot : SaveBus.Slot; } }
        internal static string CurrentWorldName { get { if (Multiplayer.Hosting) return _worldName(); if (_localGeneration == SaveBus.Generation && _localWorldName != null) return _localWorldName; string file = ActiveFile; if (file != null && File.Exists(HostSaveFiles.MetaPath(file))) return Info(file).World; var seed = GameRefs.SaveLoadGame != null ? GameRefs.SaveLoadGame.FsmVariables.FindFsmInt("seed") : null; return seed != null ? "World " + seed.Value : "My world"; } }
        internal static int CatalogueVersion;
        static bool _requested, _automatic, _quitting, _scheduled;
        static string _destination, _name, _error, _stamp;
        static string _world;
        static string _localWorldName;
        static int _localGeneration = -1;
        static bool _multiplayer;
        static int? _savedPlayers;
        static Func<string> _worldName;
        static byte[] _picture;
        static float _kilometres = -1;
        static HostSaveFiles _transaction;
        static string[] _oldKeys;
        static float _nextAuto, _countdown = -1, _saveNow = -1;
        static string _session;
        static Action<string> _notice;
        static Action<string> _announceClose;
        static Func<bool> _anyCar, _hostCar;
        static Func<float> _health;
        static Action<object, bool> _setQuitting;
        static readonly HashSet<string> ProtectedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const byte NoticePacket = 167;
        static string _pendingNotice;
        static bool _broadcastNotice;
        internal static void Install()
        {
            if (Installed) return;
            var cfg = Plugin.Settings;
            Autosave = cfg.Bind("System / Saving", "Autosave", true, "Create separate autosaves for offline or hosted worlds. Manual saves are never replaced by autosaving.");
            Interval = cfg.Bind("System / Saving", "Autosave minutes", 10, new ConfigDescription("Real minutes between autosaves.", new AcceptableValueRange<int>(1, 120)));
            Warning = cfg.Bind("System / Saving", "Warning seconds", 30, new ConfigDescription("Autosave countdown in the top-left message log. 0 disables pre-save warnings and the final warning delay. Applies to everyone in a hosted world.", new AcceptableValueRange<int>(0, 120)));
            WhileDriving = cfg.Bind("System / Saving", "Autosave while driving", false, "Allow autosaves while the offline player, host or guests are driving or riding in any vehicle. Manual and exit saves are always available in vehicles.");
            AutoKeep = cfg.Bind("System / Saving", "Autosaves to keep", 5, new ConfigDescription("Number of separate autosaves per world lineage. Manual saves are not pruned.", new AcceptableValueRange<int>(1, 100)));
            BackupKeep = cfg.Bind("System / Saving", "Backups to keep", 5, new ConfigDescription("Verified previous versions per save. Interrupted recovery and deleted-save backups are always retained.", new AcceptableValueRange<int>(1, 100)));
            Screenshots = cfg.Bind("System / Saving", "Save screenshots", true, "Capture a gameplay thumbnail for each save.");
            Autosave.SettingChanged += ScheduleChanged; Interval.SettingChanged += ScheduleChanged; Warning.SettingChanged += ScheduleChanged; WhileDriving.SettingChanged += ScheduleChanged;
            if (Multiplayer.Present)
            {
                _notice = MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("UI.Chat"), "Notice", typeof(string));
                _announceClose = MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("Net.NetSession"), "AnnounceClosing", typeof(string));
                _anyCar = MultiplayerAccess.Call<Func<bool>>(Multiplayer.Type("Net.NetSession"), "AnyoneInCar", Type.EmptyTypes);
                _hostCar = MultiplayerAccess.Call<Func<bool>>(Multiplayer.Type("Worlds.WorldSaver"), "HostInCar", Type.EmptyTypes);
                _health = MultiplayerAccess.Call<Func<float>>(Multiplayer.Type("Game.PlayerVitals"), "Health", Type.EmptyTypes);
                _worldName = MultiplayerAccess.Call<Func<string>>(Multiplayer.Type("Net.NetSession"), "get_WorldName", Type.EmptyTypes);
                _setQuitting = MultiplayerAccess.Set<bool>(Multiplayer.Type("Worlds.WorldSaver"), "<Quitting>k__BackingField");
                Require(Multiplayer.Type("Worlds.WorldSaver"), "SaveThenQuit", "QuitInstead");
                Require(Multiplayer.Type("Worlds.WorldSaver"), "RotateBackups", "SkipNativeBackups");
                Type saveRoutine = null;
                foreach (var nested in Multiplayer.Type("Worlds.WorldSaver").GetNestedTypes(System.Reflection.BindingFlags.NonPublic)) if (nested.Name.StartsWith("<Save>", StringComparison.Ordinal)) { saveRoutine = nested; break; }
                if (saveRoutine == null || !Patches.Patch(null, AccessTools.Method(saveRoutine, "MoveNext"), transpiler: AccessTools.Method(typeof(HostSaveSystem), "SingleWorldWrite")))
                    throw new InvalidOperationException("System save hook failed: WorldSaver.Save world write");
            }
            else
            {
                _notice = Modules.CleanHUD.MessageHook.Push;
                _health = OfflineSaveRuntime.Health;
                _hostCar = () => OfflineSaveRuntime.InCar;
            }
            Hooks.ActivateGameObjectDo.Prefix(null, VehicleSaveButton);
            Require(typeof(ES3PlayMaker.ActionBase), "HandleError", "NativeError");
            if (!Patches.Patch(null, AccessTools.Method(typeof(ES3File), "Sync", new[] { typeof(ES3Settings) }), prefix: AccessTools.Method(typeof(HostSaveSystem), "BeforeSync"))) throw new InvalidOperationException("Native end-of-frame world-write guard failed");
            if (!Patches.Patch(null, AccessTools.Method(typeof(ES3Internal.ES3IO), "CommitBackup"), finalizer: AccessTools.Method(typeof(HostSaveSystem), "StorageError"))) throw new InvalidOperationException("Native disk failure observation hook failed");
            if (!Patches.Patch(null, AccessTools.Method(typeof(ES3PlayMaker.PMDataWrapper), "ApplyVariables"), postfix: AccessTools.Method(typeof(HostSaveSystem), "LoadedPath"))) throw new InvalidOperationException("Save identity hook failed");
            HostSaveCleanup.Install();
            HostSaveCombat.Install(); HostSaveWarning.Install();
            SinglePlayerSaving.Install();
            HostWorldHistory.Install(); HostSaveCompatibility.Install();
            VehicleLoadPlacement.Install();
            SaveBus.StateChanged += StateChanged;
            Multiplayer.Message += ReceiveNotice;
            Scheduler.Interval(null, "Server autosave warning", .25f, Tick);
            Log.Always("[System] Autosave warnings: transparent top-left log countdown; 0 seconds disables pre-save warnings");
            Installed = true;
            Log.Always("[System] Saves ready: native offline and optional host saves, separate manual/autosaves, complete backups and interrupted-write recovery");
        }
        internal static void RecoverFiles()
        {
            if (Autosave == null) return;
            try { HostSaveFiles.Recover(); Ready = true; RecoveryError = null; ApocasaverExclusion.Install(); }
            catch (Exception error) { Ready = false; RecoveryError = "Save recovery needs attention: " + error.Message; Log.Error("[System] " + RecoveryError, error); }
        }
        static bool SkipNativeBackups() { return !Multiplayer.Hosting; }
        // Native WorldSaver.Save flushes the ES3 cache to disk, then Complete() flushes it again after the modules' AfterSave writers
        // (patrols, quests) have added their keys. Under a WWMP transaction the native flush is skipped: the second one is the one
        // that counts, and Validate reads the file after it. The first save of a new world still writes (native checks File.Exists).
        static void NativeWorldWrite(string file)
        {
            if (Busy && _transaction != null && Multiplayer.Hosting && File.Exists(HostSaveFiles.WorldPath(file))) return;
            ES3.StoreCachedFile(file);
        }
        static IEnumerable<CodeInstruction> SingleWorldWrite(IEnumerable<CodeInstruction> instructions)
        {
            var store = AccessTools.Method(typeof(ES3), "StoreCachedFile", new[] { typeof(string) }); int count = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(store)) { code.operand = AccessTools.Method(typeof(HostSaveSystem), "NativeWorldWrite"); count++; }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected ES3.StoreCachedFile calls in WorldSaver.Save: " + count);
        }
        static Exception StorageError(Exception __exception) { if (__exception != null) Fail("World persistence failed: " + __exception.Message); return __exception; }
        static bool BeforeSync(ES3Settings settings)
        {
            if (!Installed) return true;
            string file = settings != null ? Path.GetFileName(settings.path) : SaveBus.Slot;
            if (file == null || !MultiplayerHosting.IsSaveFile(file)) return true;
            if (RecoveryError != null) return false;
            if ((LocalWorld || MultiplayerHosting.Pending) && file == ActiveFile) ProtectedSlots.Add(file);
            return !ProtectedSlots.Contains(file) || Busy && file == ActiveFile;
        }
        static void StateChanged(string previous, string current)
        { if (current == "LoadGame" || current == "setSeed") { ReloadRequired = false; _session = null; _pendingNotice = null; } }
        static void Require(Type type, string method, string prefix)
        { if (!Patches.Patch(null, AccessTools.Method(type, method), prefix: AccessTools.Method(typeof(HostSaveSystem), prefix))) throw new InvalidOperationException("System save hook failed: " + method); }
        static void LoadedPath(Fsm fsm)
        {
            VehicleLoadPlacement.Loaded();
            // A snapshot carries the original global SaveFile. Keep the file explicitly chosen by the launcher.
            if (fsm == null || fsm.GameObject == null || fsm.GameObject.name != "SaveLoadGame" || fsm.Name != "SaveLoadGame") return;
            string file = Multiplayer.Present ? Multiplayer.SaveFile ?? ActiveFile : OfflineSaveRuntime.SelectedFile ?? ActiveFile;
            if (!MultiplayerHosting.IsSaveFile(file)) return;
            var variable = fsm.Variables.FindFsmString("SaveFile"); if (variable != null) variable.Value = file;
            ES3Settings.defaultSettings.path = file;
        }
        static bool NativeError(string msg) { if (HostWorldHistory.NativeError(msg)) return false; Fail("Native save error: " + msg); return true; }
        internal static void Fail(string message) { if (Busy && _error == null) _error = message; }
        internal static void Notify(string text)
        {
            Status = text; Log.Always("[System] " + text);
            _pendingNotice = text; _broadcastNotice = Multiplayer.Hosting;
            DeliverNotice();
        }
        static void DeliverNotice()
        {
            if (_pendingNotice == null || !(LocalReady || Multiplayer.WorldReady) || LoadingProgress.MenusBlocked) return;
            string text = _pendingNotice; bool broadcast = _broadcastNotice; _pendingNotice = null;
            if (text.StartsWith("Save warning:", StringComparison.Ordinal)) HostSaveWarning.Notice(text);
            else _notice(text);
            if (broadcast && Multiplayer.Hosting) Multiplayer.Send(NoticePacket, writer => { writer.Write(MultiplayerParts.Epoch); writer.Write(text.StartsWith("Save warning:", StringComparison.Ordinal) ? text : text.Length > 250 ? text.Substring(0, 250) : text); });
        }
        internal static void Warn(string text)
        { Log.Warn("[System] " + text); _pendingNotice = text; _broadcastNotice = Multiplayer.Hosting; DeliverNotice(); }
        static void ReceiveNotice(ulong peer, byte kind, BinaryReader reader)
        {
            if (kind != NoticePacket || !Multiplayer.Client || peer != 0 || reader.ReadUInt64() != MultiplayerParts.Epoch) return;
            string text = reader.ReadString(); if (text.Length > (text.StartsWith("Save warning:", StringComparison.Ordinal) ? 1024 : 250) || reader.BaseStream.Position != reader.BaseStream.Length) return;
            if (text.StartsWith("Save warning:", StringComparison.Ordinal)) Log.Warn("[System] " + text);
            _pendingNotice = text; _broadcastNotice = false; DeliverNotice();
        }
        internal static SaveInfo Info(string file, string fallback = null)
        {
            var info = new SaveInfo { File = file, Name = fallback ?? Path.GetFileNameWithoutExtension(file), World = "World not recorded", Kind = "legacy", Date = "", Multiplayer = file.StartsWith("MP_", StringComparison.Ordinal) && !file.StartsWith("MP_sp-", StringComparison.Ordinal) };
            string path = HostSaveFiles.MetaPath(file);
            try { if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 5 || lines[0] != "WWMP-SAVE-1") throw new IOException("Invalid save description: " + file);
                info.Name = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1])); info.Kind = lines[2]; info.Date = lines[3];
                float km;
                if (lines.Length >= 6 && float.TryParse(lines[5], NumberStyles.Float, CultureInfo.InvariantCulture, out km) && km >= 0 && !float.IsInfinity(km)) info.Kilometres = km;
                if (lines.Length >= 9)
                {
                    info.World = Encoding.UTF8.GetString(Convert.FromBase64String(lines[6])); info.Multiplayer = lines[7] == "mp";
                    int players; if (info.Multiplayer && int.TryParse(lines[8], out players) && players >= 1) info.Players = players;
                }
            }
            else if (File.Exists(HostSaveFiles.WorldPath(file))) info.Date = File.GetLastWriteTime(HostSaveFiles.WorldPath(file)).ToString("yyyy-MM-dd HH:mm"); }
            catch (Exception error) { Log.Warn("[System] Save description unreadable; world retained: " + file + ": " + error.Message); }
            return info;
        }
        internal static string Lineage(string file)
        {
            string path = HostSaveFiles.MetaPath(file);
            if (File.Exists(path)) { var lines = File.ReadAllLines(path); if (lines.Length >= 5 && MultiplayerHosting.IsSaveFile(lines[4])) return lines[4]; }
            return file;
        }
        static byte[] Description(string name, string kind, string lineage)
        { return Encoding.UTF8.GetBytes(string.Join("\n", new[] { "WWMP-SAVE-1", Convert.ToBase64String(Encoding.UTF8.GetBytes(name)), kind, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), lineage, _kilometres >= 0 ? _kilometres.ToString("R", CultureInfo.InvariantCulture) : "", Convert.ToBase64String(Encoding.UTF8.GetBytes(_world ?? "My world")), _multiplayer ? "mp" : "sp", _savedPlayers.HasValue ? _savedPlayers.Value.ToString(CultureInfo.InvariantCulture) : "" })); }
        static float DistanceTravelled()
        {
            var fsm = GameRefs.GameManager != null ? FsmUtil.Find(GameRefs.GameManager, "DistanceTravelled") : null;
            var distance = fsm != null && fsm.Fsm.Initialized ? fsm.FsmVariables.FindFsmFloat("distance") : null;
            float km = distance != null ? distance.Value : -1;
            return km >= 0 && !float.IsInfinity(km) ? km : -1;
        }
        internal static bool Request(string name, string overwrite = null, bool automatic = false, bool scheduled = false, string worldName = null)
        {
            if (!LocalReady || MultiplayerSaving.Frozen || Busy || _requested || _quitting) return false;
            if (!Ready || RecoveryError != null || ReloadRequired) { Notify(RecoveryError ?? "Reload a save before saving again after a failed write."); return false; }
            if (ResourceProblem != null) { Notify(ResourceProblem); return false; }
            if (LocalUnsafe) { Notify("Saving deferred while the host is downed or respawning."); return false; }
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) { Notify("Enter a save name."); return false; }
            foreach (char ch in name) if (char.IsControl(ch) || ch == '<' || ch == '>') { Notify("Use a save name without control characters or markup."); return false; }
            if (name.Length > 64 || (overwrite != null && !MultiplayerHosting.IsSaveFile(overwrite))) { Notify("Invalid save name or destination."); return false; }
            if (worldName != null) { if (worldName.Trim().Length == 0 || worldName.Length > 64) return false; foreach (char ch in worldName) if (char.IsControl(ch) || ch == '<' || ch == '>') return false; }
            if (overwrite != null && Info(overwrite).Automatic) { Notify("Create a manual save instead of overwriting an autosave."); return false; }
            _world = Multiplayer.Hosting ? CurrentWorldName : worldName ?? CurrentWorldName; _multiplayer = Multiplayer.Hosting; _savedPlayers = null;
            if (!_multiplayer) { _localWorldName = _world; _localGeneration = SaveBus.Generation; }
            _automatic = automatic; _scheduled = scheduled; _name = name.Trim(); _destination = overwrite; _requested = true;
            if (!(Multiplayer.Hosting ? MultiplayerSaving.RequestSave() : SinglePlayerSaving.Request())) { _requested = false; Notify("The current world's save target is unavailable."); return false; }
            return true;
        }
        // Recheck at the barrier as queued owner damage may arrive during the warning or flush.
        internal static bool DeferScheduledSave()
        {
            if (!_scheduled || !(HostSaveCombat.Check() || LocalUnsafe || ResourceProblem != null || Multiplayer.Hosting && MultiplayerHandshake.HasJoiningPeers)) return false;
            _requested = _scheduled = false; _countdown = _saveNow = -1;
            HostSaveWarning.Show((byte)(Warning.Value > 0 ? 2 : 0));
            Status = ResourceProblem ?? "Autosave deferred until players are ready and combat has ended."; Log.Always("[System] " + Status);
            return true;
        }
        // Called only after all owners have frozen and flushed, before the native serializer starts.
        internal static void Begin()
        {
            if (RecoveryError != null || ReloadRequired) throw new IOException(RecoveryError ?? "Reload required after a failed save");
            if (ResourceProblem != null) throw new IOException(ResourceProblem);
            if (LocalUnsafe) throw new IOException("Saving deferred: the host became downed or respawning before serialization");
            string file = ActiveFile; HostSaveFiles.WorldPath(file);
            if (!_requested) { _automatic = true; _name = "Autosave " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); _destination = null; _world = CurrentWorldName; _multiplayer = Multiplayer.Hosting; _savedPlayers = null; }
            _error = null; _picture = null; _stamp = Guid.NewGuid().ToString("N");
            _oldKeys = File.Exists(HostSaveFiles.WorldPath(file)) ? ES3.GetKeys(HostSaveFiles.Disk(file)) : new string[0];
            var tx = new HostSaveFiles(file, "working save"); tx.Prepare(HostSaveFiles.Participants(file)); _transaction = tx;
            _kilometres = DistanceTravelled();
            if (Screenshots.Value) CapturePicture();
            HostSaveCleanup.Prepare();
        }
        internal static void Stamp()
        {
            HostWorldHistory.Merge(); var settings = new ES3Settings(ActiveFile) { location = ES3.Location.Cache };
            ES3.Save<string>(CommitKey, _stamp, settings);
            if (_multiplayer)
            {
                int count = 0; foreach (string line in ES3.Load<string[]>(PlayerKey, settings)) if (line.StartsWith("player\t", StringComparison.Ordinal)) count++;
                _savedPlayers = count;
            }
            HostSaveCleanup.SaveProtection(); HostSaveCompatibility.SavePoses(); UltimateToolsCompat.Save();
        }
        static void CapturePicture()
        {
            var camera = GameRefs.MainCamera; if (camera == null || Application.isBatchMode) return;
            RenderTexture old = RenderTexture.active, target = camera.targetTexture; RenderTexture image = null; Texture2D pixels = null;
            try
            {
                image = RenderTexture.GetTemporary(480, 270, 24); camera.targetTexture = image; camera.Render(); RenderTexture.active = image;
                pixels = new Texture2D(480, 270, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 480, 270), 0, 0); pixels.Apply(); _picture = pixels.EncodeToPNG();
            }
            catch (Exception error) { Log.Warn("[System] Save thumbnail unavailable: " + error.Message); }
            finally { camera.targetTexture = target; RenderTexture.active = old; if (image != null) RenderTexture.ReleaseTemporary(image); if (pixels != null) UnityEngine.Object.Destroy(pixels); }
        }
        static void Validate(string file, string stamp)
        {
            var settings = HostSaveFiles.Disk(file); var keys = new HashSet<string>(ES3.GetKeys(settings), StringComparer.Ordinal);
            foreach (string key in new[] { "global", "Player", "newGOArray_Cars", "newGOArray_Items", "newGOArray_Sandstorms", CommitKey })
                if (!keys.Contains(key)) throw new IOException("World save is missing " + key);
            if (stamp != null && ES3.Load<string>(CommitKey, settings) != stamp) throw new IOException("World save did not persist this transaction");
            if (_multiplayer)
            {
                var players = ES3.Load<string[]>(PlayerKey, settings);
                if (players == null || players.Length == 0 || !players[0].StartsWith("WWMP-PLAYERS-1\t", StringComparison.Ordinal)) throw new IOException("Player snapshot is invalid");
            }
            if (file == ActiveFile) foreach (string key in _oldKeys) if (!keys.Contains(key) && !HostSaveCleanup.RemovesKey(key)) throw new IOException("Previous save record was lost: " + key);
        }
        internal static bool Complete()
        {
            if (!Busy) return false;
            string source = ActiveFile, target = _destination ?? (_multiplayer ? "MP_" : "MP_sp-") + (_automatic ? "auto-" : "save-") + Guid.NewGuid().ToString("N") + ".es3";
            try
            {
                if (_error != null) throw new IOException(_error);
                ES3.StoreCachedFile(source); Validate(source, _stamp);
                if (target == source)
                {
                    HostSaveFiles.WriteDurable(HostSaveFiles.MetaPath(source), Description(_name, "manual", Lineage(source)));
                    if (_picture != null) HostSaveFiles.WriteDurable(HostSaveFiles.ImagePath(source), _picture);
                    else if (File.Exists(HostSaveFiles.ImagePath(source))) File.Delete(HostSaveFiles.ImagePath(source));
                    _transaction.Commit();
                }
                else
                {
                    PublishSnapshot(source, target);
                    // Autosaving / Save As must not overwrite the save the host loaded. Keep the fresh in-memory cache.
                    _transaction.Rollback(false); _transaction.Commit();
                }
                _transaction = null; _requested = false; Schedule();
                CatalogueVersion++;
                HostWorldHistory.Accepted();
                try { HostSaveCleanup.Commit(); } catch (Exception cleanup) { HostSaveSystem.ReloadRequired = true; Log.Error("[System] Save committed, but cleanup could not finish; reload before saving", cleanup); }
                Status = (_automatic ? "Autosave" : "Save") + " completed: " + _name;
                Log.Always("[System] " + Status);
                try { HostSaveFiles.Prune(source, BackupKeep.Value); HostSaveFiles.Prune(target, BackupKeep.Value); if (_automatic) PruneAutosaves(Lineage(target), target); }
                catch (Exception error) { Log.Warn("[System] Retention deferred; saves retained: " + error.Message); }
                return true;
            }
            catch (Exception error) { Abort(error); return false; }
        }
        static void PublishSnapshot(string source, string target)
        {
            var copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in HostSaveFiles.Participants(source)) if (File.Exists(path))
            {
                // Native .bak and checkpoint files are recovery history, not the new snapshot's authority.
                if (path.EndsWith(".wwsave", StringComparison.Ordinal) || path.EndsWith(".wwsave.png", StringComparison.Ordinal) || Path.GetFileName(path).IndexOf(".bak", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith(".player-checkpoint.txt", StringComparison.Ordinal)) continue;
                copies[HostSaveFiles.Remap(path, source, target)] = path;
            }
            var participants = HostSaveFiles.Participants(target); foreach (string path in copies.Keys) participants.Add(path);
            var tx = new HostSaveFiles(target, _automatic ? "autosave" : "manual save"); tx.Prepare(participants);
            try
            {
                // Copy the whole snapshot into private staging before publishing any destination file.
                int i = 0; var stages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in copies)
                {
                    string path = Path.Combine(tx.DirectoryPath, "new-" + i++ + ".bin"); HostSaveFiles.CopyVerified(pair.Value, path);
                    if (pair.Value.EndsWith(".senses.txt", StringComparison.Ordinal))
                    {
                        string text = File.ReadAllText(path), header = "v1 " + source;
                        if (!text.StartsWith(header + "\n", StringComparison.Ordinal) && !text.StartsWith(header + "\r\n", StringComparison.Ordinal)) throw new IOException("NPC senses companion identity is invalid");
                        HostSaveFiles.WriteDurable(path, Encoding.UTF8.GetBytes("v1 " + target + text.Substring(header.Length)));
                    }
                    stages[pair.Key] = path;
                }
                foreach (var pair in stages) HostSaveFiles.Publish(pair.Value, pair.Key);
                foreach (var entry in tx.Entries) if (!stages.ContainsKey(entry.Path) && entry.Path != HostSaveFiles.MetaPath(target) && entry.Path != HostSaveFiles.ImagePath(target) && File.Exists(entry.Path)) File.Delete(entry.Path);
                HostSaveFiles.WriteDurable(HostSaveFiles.MetaPath(target), Description(_name, _automatic ? "auto" : "manual", Lineage(source)));
                if (_picture != null) HostSaveFiles.WriteDurable(HostSaveFiles.ImagePath(target), _picture);
                else if (File.Exists(HostSaveFiles.ImagePath(target))) File.Delete(HostSaveFiles.ImagePath(target));
                Validate(target, _stamp); tx.Commit();
            }
            catch { try { tx.Rollback(); } catch (Exception error) { RecoveryError = "Snapshot recovery required: " + error.Message; } throw; }
        }
        internal static void Abort(Exception error)
        {
            var tx = _transaction;
            if (tx != null) try { SaveBus.CancelSave(); } catch (Exception cancel) { _error = "Native save cancellation failed: " + cancel.Message; }
            _transaction = null;
            HostSaveCleanup.Rollback();
            if (tx != null) try { tx.Rollback(); } catch (Exception recovery) { RecoveryError = "Save recovery required at " + tx.DirectoryPath + ": " + recovery.Message; }
            // A failed write leaves live module state ahead of disk. Require a reload before another world write.
            if (tx != null) ReloadRequired = true;
            _requested = false; _picture = null; Status = "Save failed: " + error.Message;
            Log.Error("[System] " + Status, error); _pendingNotice = Status; _broadcastNotice = Multiplayer.Hosting;
        }
        internal static void Delete(string file, bool expiredAutosave = false)
        {
            if (Busy || _requested || MultiplayerSaving.Frozen || (LocalWorld && file == ActiveFile)) throw new IOException("Cannot delete the running world's source save");
            if (RecoveryError != null) throw new IOException(RecoveryError);
            var tx = new HostSaveFiles(file, expiredAutosave ? "expired autosave" : "delete"); tx.Prepare(HostSaveFiles.Participants(file));
            try { foreach (var entry in tx.Entries) if (File.Exists(entry.Path)) File.Delete(entry.Path); tx.Commit(); ES3File.cachedFiles.Remove(file); CatalogueVersion++; }
            catch { tx.Rollback(); throw; }
        }
        internal static List<string> Backups(string file)
        {
            var result = new List<string>(); string directory = Path.Combine(HostSaveFiles.BackupRoot, file);
            if (Directory.Exists(directory)) foreach (string path in Directory.GetDirectories(directory))
                if (HostSaveFiles.Marked(Path.Combine(path, "committed")) && File.Exists(Path.Combine(path, "undo.bin")))
                { var backup = HostSaveFiles.Read(path); foreach (var entry in backup.Entries) if (entry.Path == HostSaveFiles.WorldPath(file) && entry.Existed) { result.Add(path); break; } }
            result.Sort(StringComparer.Ordinal); result.Reverse(); return result;
        }
        internal static List<string> DeletedBackups()
        {
            var result = new List<string>(); if (!Directory.Exists(HostSaveFiles.BackupRoot)) return result;
            foreach (string directory in Directory.GetDirectories(HostSaveFiles.BackupRoot))
            {
                string file = Path.GetFileName(directory);
                if (MultiplayerHosting.IsSaveFile(file) && !File.Exists(HostSaveFiles.WorldPath(file))) result.AddRange(Backups(file));
            }
            return result;
        }
        internal static void RestoreBackup(string directory)
        {
            if (!Multiplayer.InMainMenu || Busy || _requested) throw new IOException("Restore backups from the title menu only");
            string full = Path.GetFullPath(directory), prefix = Path.GetFullPath(HostSaveFiles.BackupRoot) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Invalid backup directory");
            var backup = HostSaveFiles.Read(full); bool hasWorld = false;
            var paths = HostSaveFiles.Participants(backup.Slot);
            foreach (var entry in backup.Entries) { paths.Add(entry.Path); if (entry.Path == HostSaveFiles.WorldPath(backup.Slot) && entry.Existed) hasWorld = true; }
            if (!hasWorld) throw new IOException("This backup predates the first save; there is no previous world to restore");
            var undo = new HostSaveFiles(backup.Slot, "restore backup"); undo.Prepare(paths);
            try
            {
                backup.Rollback();
                // Legacy backups need not contain WW's new marker, but their ES3 records must remain readable.
                var keys = new HashSet<string>(ES3.GetKeys(HostSaveFiles.Disk(backup.Slot)));
                if (!keys.Contains("global") || !keys.Contains("Player") || !keys.Contains("newGOArray_Items")) throw new IOException("Backup world is incomplete");
                undo.Commit(); CatalogueVersion++; Status = "Backup restored: " + backup.Slot;
            }
            catch { undo.Rollback(); throw; }
        }
        internal static void LeaveWithoutSaving()
        {
            if (Busy || MultiplayerSaving.Frozen || !LocalWorld || (!ReloadRequired && RecoveryError == null && MultiplayerItems.ResourceFailure == null)) return;
            if (Multiplayer.Hosting) MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("Net.NetSession"), "End", typeof(string))("Host left without saving after a save failure");
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
        static void PruneAutosaves(string lineage, string newest)
        {
            var files = new List<string>();
            foreach (string path in Directory.GetFiles(HostSaveFiles.WorldRoot, "MP_*.es3.wwsave"))
            {
                string file = Path.GetFileName(path).Substring(0, Path.GetFileName(path).Length - 7);
                if (MultiplayerHosting.IsSaveFile(file) && Info(file).Automatic && Lineage(file) == lineage && File.Exists(HostSaveFiles.WorldPath(file))) files.Add(file);
            }
            files.Sort(CompareNewest); int kept = 0;
            foreach (string file in files) { if (kept++ < AutoKeep.Value || file == newest || file == ActiveFile) continue; Delete(file, true); HostSaveFiles.Prune(file, 0); }
        }
        static int CompareNewest(string a, string b) { return File.GetLastWriteTimeUtc(HostSaveFiles.WorldPath(b)).CompareTo(File.GetLastWriteTimeUtc(HostSaveFiles.WorldPath(a))); }
        static void Schedule() { _nextAuto = Time.unscaledTime + Interval.Value * 60f; _countdown = _saveNow = -1; _scheduled = false; HostSaveWarning.Show(0); }
        static void ScheduleChanged(object sender, EventArgs args) { Schedule(); }
        static void Tick()
        {
            DeliverNotice();
            if (!LocalWorld) { _session = null; if (!Multiplayer.Client) HostSaveWarning.Show(0); return; }
            if (_session != ActiveFile) { _session = ActiveFile; Schedule(); }
            bool combat = HostSaveCombat.Check();
            if (!Autosave.Value || Busy || _requested || _quitting || RecoveryError != null || ReloadRequired)
            { _countdown = _saveNow = -1; HostSaveWarning.Show(0); return; }
            float now = Time.unscaledTime;
            int warning = Mathf.Min(Mathf.Max(0, Warning.Value), Interval.Value * 60);
            if (now < _nextAuto - warning) return;
            bool eligible = !combat && !SaveBus.Busy && !MultiplayerSaving.Frozen && GameRefs.WorldRunning && !UI.EditorSession.Busy && !LocalUnsafe && ResourceProblem == null
                && (!Multiplayer.Hosting || !MultiplayerHandshake.HasJoiningPeers) && (WhileDriving.Value || (Multiplayer.Hosting ? !_anyCar() : !_hostCar()));
            if (!eligible)
            {
                _countdown = _saveNow = -1; HostSaveWarning.Show((byte)(warning > 0 ? 2 : 0));
                if (ResourceProblem != null && Status != ResourceProblem) { Status = ResourceProblem; if (MultiplayerItems.ResourceFailure != null) Notify(Status); }
                return;
            }
            if (warning == 0)
            {
                HostSaveWarning.Show(0);
                Request("Autosave " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), null, true, true);
                return;
            }
            if (_countdown < 0) _countdown = now + warning;
            int seconds = Mathf.CeilToInt(_countdown - now);
            if (seconds <= 0)
            {
                HostSaveWarning.Show(3);
                if (_saveNow < 0) { _saveNow = now + 3f; return; }
                if (now >= _saveNow && !Request("Autosave " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), null, true, true))
                { _countdown = _saveNow = -1; }
                return;
            }
            HostSaveWarning.Show(1, seconds);
        }
        static bool VehicleSaveButton(HutongGames.PlayMaker.Actions.ActivateGameObject action)
        {
            if (!Ready || action.Fsm == null || action.Fsm.Name != "InCar" || action.Fsm.GameObject != GameRefs.Player) return true;
            var target = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
            if (target == null || target.transform.parent == null || target.transform.parent.name != "MainMenu") return true;
            if (target.name != "Save_Game_slots" && target.name != "get out to save") return true;
            target.SetActive(target.name == "Save_Game_slots"); return false;
        }
        static bool QuitInstead(Action __0)
        {
            if (!Multiplayer.Hosting) return true;
            SaveBeforeExit(__0);
            return false;
        }
        internal static void SaveBeforeExit(Action then)
        { if (!_quitting) { _quitting = true; Scheduler.StartCoroutine(Quit(then)); } }
        static IEnumerator Quit(Action leave)
        {
            try
            {
                while (MultiplayerSaving.Frozen || Busy || _requested) yield return null;
                if (RecoveryError != null || ReloadRequired) { Notify("Save and exit cancelled: resolve the save failure or return to the menu without saving."); yield break; }
                _quitting = false; bool requested = Request("Exit autosave " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), null, true); _quitting = true;
                if (!requested) { Notify("Exit cancelled because a new save could not start."); yield break; }
                if (Multiplayer.Hosting) _setQuitting(null, true);
                while (_requested || MultiplayerSaving.Frozen || Busy) yield return null;
                if (RecoveryError != null || ReloadRequired || !Status.StartsWith("Autosave completed:", StringComparison.Ordinal)) { Notify("Exit cancelled because the save failed."); yield break; }
                if (Multiplayer.Hosting) _announceClose("The host closed the world");
                yield return new WaitForSecondsRealtime(.4f);
                leave();
            }
            finally { _quitting = false; if (_setQuitting != null) _setQuitting(null, false); }
        }
    }
}
