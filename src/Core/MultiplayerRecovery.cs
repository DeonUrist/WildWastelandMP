using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // Player-only checkpoints never enter the world-save barrier. A reload always uses the world's embedded copy.
    internal static partial class MultiplayerRecovery
    {
        const string SnapshotKey = "WildWastelandMP.PlayerSnapshot";
        const string RecoveryKey = "ww.recovery";
        static Func<object, Dictionary<string, float>> _stats;
        static Func<object, string> _key;
        static Func<Dictionary<string, float>> _capture;
        static Action _report, _refresh;
        static Action<string> _notice, _end;
        static Func<ulong, object> _record;
        static Func<object, object> _pendingRestore;
        static Func<object, bool> _hasAll;
        static Func<object, ulong?> _spawning;
        static readonly HashSet<string> Dirty = new HashSet<string>(StringComparer.Ordinal);
        static readonly HashSet<ulong> Clean = new HashSet<ulong>();
        static readonly HashSet<ulong> Waiting = new HashSet<ulong>();
        static float _next, _deadline;
        static ulong _token, _leaveToken;
        static bool _collecting, _leaving, _left, _quitAllowed;
        static string _warning;
        static PlayMakerFSM _armor;
        static readonly string[] ArmorFields = { "armor", "armor_capacity", "backpack_capacity" };

        internal static void Install()
        {
            var session = Multiplayer.Type("Net.NetSession"); var record = Multiplayer.Type("Worlds.PlayerRecord");
            var vitals = Multiplayer.Type("Game.PlayerVitals");
            _stats = MultiplayerAccess.Get<Dictionary<string, float>>(record, "Stats");
            _key = MultiplayerAccess.Get<string>(record, "Key");
            _record = MultiplayerAccess.Call<Func<ulong, object>>(session, "RecordOf", typeof(ulong));
            _pendingRestore = MultiplayerAccess.Get<object>(session, "pendingRestore");
            _hasAll = MultiplayerAccess.Call<Func<object, bool>>(Multiplayer.Type("Net.WorldObjects"), "HasAll", record);
            _spawning = MultiplayerAccess.Get<ulong?>(session, "spawnNextToPeer");
            _capture = MultiplayerAccess.Call<Func<Dictionary<string, float>>>(vitals, "Capture", Type.EmptyTypes);
            _report = MultiplayerAccess.Call<Action>(session, "SendPlayerSave", Type.EmptyTypes);
            _refresh = MultiplayerAccess.Call<Action>(session, "RefreshRecords", Type.EmptyTypes);
            _notice = MultiplayerAccess.Call<Action<string>>(Multiplayer.Type("UI.Chat"), "Notice", typeof(string));
            _end = MultiplayerAccess.Call<Action<string>>(session, "End", typeof(string));
            StorageInstall();
            Patch(vitals, "Capture", null, "CaptureArmor");
            Patch(vitals, "Apply", null, "ApplyArmor");
            Patch(Multiplayer.Type("Net.WorldObjects"), "OnPeerLeft", null, "PeerLeft");
            Patch(Multiplayer.Type("Worlds.PlayerRegistry"), "GetOrCreate", null, "PlayerOpened");
            Patch(session, "TryRestorePlayer", "BeforeRestore", "AfterRestore");
            Patch(session, "AnnounceClosing", "HostClosing", null);
            Patch(Multiplayer.Type("Worlds.QuitPatches"), "Intercept", "QuitToMenu", null);
            Multiplayer.Message += Receive;
            Multiplayer.SessionChanged += Changed;
            Application.wantsToQuit += WantsQuit;
            Scheduler.Interval(Multiplayer.PatchOwner, "Player recovery checkpoints", .25f, Tick);
            Log.Always("[Multiplayer] Player recovery ready (5-minute checkpoints; world reload restores the saved snapshot)");
        }
        static void Patch(Type type, string method, string before, string after)
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(type, method),
                prefix: before == null ? null : AccessTools.Method(typeof(MultiplayerRecovery), before),
                postfix: after == null ? null : AccessTools.Method(typeof(MultiplayerRecovery), after)))
                throw new InvalidOperationException("Player recovery hook failed: " + type.Name + "." + method);
        }
        static PlayMakerFSM Armor()
        { if (_armor == null && GameRefs.GameManager != null) _armor = FsmUtil.Find(GameRefs.GameManager, "ArmorBackpack"); return _armor; }
        static void CaptureArmor(Dictionary<string, float> __result)
        {
            if (!Multiplayer.Active || __result == null) return;
            var fsm = Armor(); if (fsm == null) return;
            foreach (string name in ArmorFields)
            { var value = fsm.FsmVariables.FindFsmFloat(name); if (value != null) __result["ww." + name] = value.Value; }
        }
        static void ApplyArmor(Dictionary<string, float> __0)
        {
            if (!Multiplayer.Active || __0 == null) return;
            var fsm = Armor(); if (fsm == null) return;
            foreach (string name in ArmorFields)
            {
                float saved; var value = fsm.FsmVariables.FindFsmFloat(name);
                if (value != null && __0.TryGetValue("ww." + name, out saved) && !float.IsNaN(saved) && !float.IsInfinity(saved)) value.Value = Mathf.Max(0, saved);
            }
        }
        static void PlayerOpened(string __0)
        { if (Multiplayer.Hosting && __0 != "host") Dirty.Add(__0); }
        static void HostClosing()
        { if (Multiplayer.Hosting) foreach (ulong peer in MultiplayerReliable.ConnectedPeers()) Clean.Add(peer); }
        static void PeerLeft(ulong __0, object __1)
        {
            if (!Multiplayer.Hosting || __1 == null) return;
            bool clean = Clean.Remove(__0); string key = _key(__1);
            if (clean) { Dirty.Remove(key); _stats(__1).Remove(RecoveryKey); }
            else { Dirty.Add(key); _stats(__1)[RecoveryKey] = 1; }
            Waiting.Remove(__0);
            // Native has just refreshed and detached this owner's items; retain that validated state, including offline players.
            Checkpoint();
        }
        static void BeforeRestore(out object __state) { __state = Multiplayer.Client ? _pendingRestore(null) : null; }
        static void AfterRestore(object __state)
        {
            if (__state == null || !Multiplayer.Client || _pendingRestore(null) != null) return;
            if (!_hasAll(__state))
            { _warning = "Player recovery incomplete: some carried items could not be restored. Check the log."; Log.Warn("[Multiplayer] Player restore timed out before all inventory objects arrived"); return; }
            float reason;
            if (_stats(__state).TryGetValue(RecoveryKey, out reason))
                _warning = reason == 2 ? "Player state restored to this world save's snapshot." : "Unclean disconnect: your latest server player checkpoint was restored.";
        }
        static void Changed()
        {
            if (Multiplayer.Active) return;
            _collecting = false; Waiting.Clear(); Clean.Clear(); _armor = null; _warning = null;
        }
        static void Tick()
        {
            string error = TakeWriteError();
            if (error != null) { Log.Warn("[Multiplayer] Player checkpoint write failed: " + error); if (Multiplayer.Hosting) _warning = "Player checkpoint could not be written. Check the log."; }
            if (TakeWriteSuccess()) Log.Always("[Multiplayer] Player checkpoint written (player data only; no world save or pause)");
            if (_warning != null && Multiplayer.WorldReady && !LoadingProgress.MenusBlocked && !SaveBus.Busy)
            { string warning = _warning; _warning = null; _notice(warning); Log.Always("[Multiplayer] " + warning); }
            if (!Multiplayer.Hosting || !Multiplayer.WorldReady || MultiplayerSaving.Frozen || SaveBus.Busy || _file == null) return;
            if (_collecting)
            {
                if (Waiting.Count != 0 && Time.unscaledTime < _deadline) return;
                if (Waiting.Count != 0) Log.Warn("[Multiplayer] Player checkpoint: some owners did not report; their last validated state was retained");
                _collecting = false; Waiting.Clear(); Checkpoint();
                return;
            }
            if (Time.unscaledTime < _next) return;
            _collecting = true; _token++; _deadline = Time.unscaledTime + 15f; _next = Time.unscaledTime + 300f;
            Waiting.Clear(); foreach (ulong peer in MultiplayerReliable.ConnectedPeers()) Waiting.Add(peer);
            Multiplayer.Send(143, w => w.Write(_token));
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind < 143 || kind > 145) return;
            ulong token = reader.ReadUInt64();
            if (kind == 143)
            {
                if (Multiplayer.Client && from == 0 && reader.BaseStream.Position == reader.BaseStream.Length)
                    Scheduler.StartCoroutine(Report(token, false));
                return;
            }
            if (kind == 144)
            {
                bool clean = reader.ReadBoolean();
                if (!Multiplayer.Hosting || reader.BaseStream.Position != reader.BaseStream.Length) return;
                var record = _record(from); if (from == 0 || record == null) return;
                if (clean)
                {
                    Dirty.Remove(_key(record));
                    if (Checkpoint()) { Clean.Add(from); Multiplayer.Send(145, w => w.Write(token), from); }
                    else Dirty.Add(_key(record));
                }
                else if (_collecting && token == _token) Waiting.Remove(from);
                return;
            }
            if (kind == 145 && Multiplayer.Client && from == 0 && token == _leaveToken && reader.BaseStream.Position == reader.BaseStream.Length) _left = true;
        }
        static IEnumerator Report(ulong token, bool clean)
        {
            float deadline = Time.unscaledTime + 10f;
            while (Multiplayer.Client && Time.unscaledTime < deadline && (!Multiplayer.WorldReady || !MultiplayerParty.PlayerReady || MultiplayerSaving.Frozen || _spawning(null).HasValue)) yield return null;
            if (!Multiplayer.Client || !Multiplayer.WorldReady || !MultiplayerParty.PlayerReady || MultiplayerSaving.Frozen || _spawning(null).HasValue) yield break;
            while (!FlushInventory())
            { if (!Multiplayer.Client || Time.unscaledTime >= deadline) yield break; yield return null; }
            try { _report(); Multiplayer.Send(144, w => { w.Write(token); w.Write(clean); }); }
            catch (Exception error) { Log.Error("[Multiplayer] Player checkpoint report failed", error); }
        }
        static bool FlushInventory()
        {
            try { return MultiplayerItems.FlushInventory(); }
            catch (Exception error) { Log.Error("[Multiplayer] Player inventory flush failed", error); return false; }
        }
        internal static void Leave(Action then)
        {
            if (_leaving) return;
            if (!Multiplayer.Client || !Multiplayer.WorldReady) { then(); return; }
            _leaving = true; Scheduler.StartCoroutine(LeaveRoutine(then));
        }
        static IEnumerator LeaveRoutine(Action then)
        {
            try
            {
                _left = false; _leaveToken = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                yield return Report(_leaveToken, true);
                float deadline = Time.unscaledTime + 5f;
                while (Multiplayer.Client && !_left && Time.unscaledTime < deadline) yield return null;
                if (!_left) Log.Warn("[Multiplayer] Clean exit was not acknowledged; recovery remains available");
                then();
            }
            finally { _leaving = false; }
        }
        static bool QuitToMenu(Action __0, ref bool __result)
        {
            if (!Multiplayer.Client || !Multiplayer.WorldReady) return true;
            Leave(EndClient); __result = false; return false;
        }
        static void EndClient() { _end("Disconnected"); }
        static bool WantsQuit()
        {
            if (_quitAllowed || !Multiplayer.Active || !Multiplayer.WorldReady) return true;
            if (_leaving) return false;
            if (Multiplayer.Hosting) { Scheduler.StartCoroutine(QuitHost()); return false; }
            Leave(QuitApplication); return false;
        }
        static IEnumerator QuitHost()
        {
            _leaving = true;
            while (Multiplayer.Hosting && (MultiplayerSaving.Frozen || SaveBus.Busy)) yield return null;
            _leaving = false;
            Multiplayer.DisconnectFromMenu();
            _leaving = true;
            float deadline = Time.unscaledTime + 3f;
            while (WriteBusy() && Time.unscaledTime < deadline) yield return null;
            _quitAllowed = true; Application.Quit();
        }
        static void QuitApplication() { _quitAllowed = true; _end("Disconnected"); Application.Quit(); }
    }
}
