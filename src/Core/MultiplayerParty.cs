using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    // Wallets stay independent. A quest pays every connected member the full reward.
    // MP player records persist money and receipts together in the same stats snapshot.
    internal static partial class MultiplayerParty
    {
        static readonly string[] PaidKeys = { "ww.questPaid0", "ww.questPaid1", "ww.questPaid2", "ww.questPaid3" };
        static readonly string[] OwedKeys = { "ww.questOwed0", "ww.questOwed1", "ww.questOwed2", "ww.questOwed3" };
        static readonly string[] Bosses = { "Boss_Alpha_Nightwalker", "Boss_Scorpion_King", "Boss_Buzzgut", "Boss_Terror_of_the_Night", "Boss_Duke_Ironjaw", "Boss_Collage_Teacher", "Boss_Black_Juggernaut" };
        static Func<object, object> _restore;
        static Func<ulong, object> _record;
        static Func<object, Dictionary<string, float>> _stats;
        static Func<IList> _players;
        static Func<object, ulong> _id;
        static Func<IEnumerable> _records;
        static Func<BinaryReader, Dictionary<string, float>> _readStats;
        sealed class ReportState { internal Dictionary<string, float> Before, Report; }
        static ulong _followerPeer;
        static long _paid;
        static bool _restored;
        static float _nextSnapshot;

        internal static void Install()
        {
            var session = Multiplayer.Type("Net.NetSession"); var record = Multiplayer.Type("Worlds.PlayerRecord");
            _restore = MultiplayerAccess.Get<object>(session, "pendingRestore");
            _record = MultiplayerAccess.Call<Func<ulong, object>>(session, "RecordOf", typeof(ulong));
            _stats = MultiplayerAccess.Get<Dictionary<string, float>>(record, "Stats");
            _players = MultiplayerAccess.Call<Func<IList>>(session, "Players", Type.EmptyTypes);
            _id = MultiplayerAccess.Get<ulong>(Multiplayer.Type("Net.NetSession+PlayerInfo"), "Id");
            _records = MultiplayerAccess.Call<Func<IEnumerable>>(Multiplayer.Type("Worlds.PlayerRegistry"), "get_All", Type.EmptyTypes);
            _readStats = MultiplayerAccess.Call<Func<BinaryReader, Dictionary<string, float>>>(record, "ReadStats", typeof(BinaryReader));
            var vitals = Multiplayer.Type("Game.PlayerVitals");
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(vitals, "Capture"), postfix: AccessTools.Method(typeof(MultiplayerParty), "Capture"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(vitals, "Apply"), postfix: AccessTools.Method(typeof(MultiplayerParty), "Restore"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(session, "TryRestorePlayer"), prefix: AccessTools.Method(typeof(MultiplayerParty), "BeforeRestore"), postfix: AccessTools.Method(typeof(MultiplayerParty), "AfterRestore"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(record, "ReadReport"), prefix: AccessTools.Method(typeof(MultiplayerParty), "BeforeReport"), postfix: AccessTools.Method(typeof(MultiplayerParty), "AfterReport")))
                throw new InvalidOperationException("Could not install multiplayer reward receipts");
        }
        internal static void Reset() { MultiplayerWorld.Reset(); _followerPeer = 0; _paid = 0; _restored = false; _nextSnapshot = 0; ResetTransactions(); MultiplayerCompass.Clear(); }
        static long Read(Dictionary<string, float> stats, string[] keys)
        {
            long total = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                float value;
                if (stats.TryGetValue(keys[i], out value) && !float.IsNaN(value) && value >= 0 && value <= 65535)
                    total |= (long)(ushort)value << (i * 16);
            }
            return Math.Max(0, total);
        }
        static void Write(Dictionary<string, float> stats, string[] keys, long total)
        { for (int i = 0; i < keys.Length; i++) stats[keys[i]] = (ushort)(total >> (i * 16)); }
        static void Capture(Dictionary<string, float> __result)
        { if (Multiplayer.Client && _restored && __result != null) { Write(__result, PaidKeys, _paid); CaptureTransactions(__result); } }
        static void Restore(Dictionary<string, float> __0)
        { if (Multiplayer.Client && __0 != null && __0.ContainsKey(PaidKeys[0])) { _paid = Read(__0, PaidKeys); RestoreTransactions(__0); } }
        static void BeforeRestore(out object __state) { __state = Multiplayer.Client ? _restore(null) : null; }
        static void AfterRestore(object __state)
        { if (Multiplayer.Client && __state != null && _restore(null) == null) { _paid = Read(_stats(__state), PaidKeys); RestoreTransactions(_stats(__state)); _restored = true; } }
        static void BeforeReport(object __instance, BinaryReader __0, out ReportState __state)
        {
            __state = null; if (!Multiplayer.Hosting) return;
            long position = __0.BaseStream.Position;
            try { __0.BaseStream.Position += 16; __state = new ReportState { Before = _stats(__instance), Report = _readStats(__0) }; }
            finally { __0.BaseStream.Position = position; }
        }
        static void AfterReport(object __instance, ReportState __state)
        {
            if (!Multiplayer.Hosting || __state == null) return;
            var stats = _stats(__instance); long owed = Read(__state.Before, OwedKeys), before = Read(__state.Before, PaidKeys);
            // ReadReport replaces Stats; keep host entitlement and monotonic receipts, including on death.
            long paid = Math.Min(owed, Math.Max(before, Read(__state.Report, PaidKeys)));
            Write(stats, OwedKeys, owed); Write(stats, PaidKeys, paid);
            PreserveTransactions(__state.Before, stats, __state.Report);
            float following; if (__state.Before.TryGetValue("ww.hire", out following)) stats["ww.hire"] = following;
        }
        internal static void Follow(ulong peer)
        {
            if (!Multiplayer.Hosting) return;
            foreach (var record in _records()) _stats(record).Remove("ww.hire");
            var owner = peer != 0 ? _record(peer) : null; if (owner != null) _stats(owner)["ww.hire"] = 1;
            _followerPeer = peer;
        }
        internal static GameObject FollowPlayer { get { return Multiplayer.PlayerFor(_followerPeer) ?? GameRefs.Player; } }
        internal static float PlayerHealth(GameObject player)
        {
            ulong peer = Multiplayer.PeerFor(player); var record = peer != 0 ? _record(peer) : null; float value;
            return record != null && _stats(record).TryGetValue("p.Health", out value) ? value : float.NaN;
        }
        internal static void Award(int reward)
        {
            if (!Multiplayer.Hosting || reward <= 0) return;
            foreach (var player in _players())
            {
                ulong peer = _id(player); if (peer == 0) continue;
                var record = _record(peer); if (record == null) continue;
                var stats = _stats(record); long owed = Read(stats, OwedKeys);
                Write(stats, OwedKeys, checked(owed + reward));
            }
            _nextSnapshot = 0;
            Log.Always("[Multiplayer] Party quest payout: " + reward + " to each connected player's own wallet");
        }
        internal static void Tick()
        {
            MultiplayerCompass.Tick();
            MultiplayerWorld.Tick();
            if (Multiplayer.Client && Multiplayer.WorldReady && _restore(null) == null) _restored = true;
            if (!Multiplayer.Hosting || !Multiplayer.WorldReady || Time.unscaledTime < _nextSnapshot) return;
            _nextSnapshot = Time.unscaledTime + .5f;
            foreach (var player in _players())
            {
                ulong peer = _id(player); if (peer == 0) continue;
                var record = _record(peer); if (record == null) continue;
                float following; if (_stats(record).TryGetValue("ww.hire", out following) && following == 1) _followerPeer = peer;
                long owed = Read(_stats(record), OwedKeys); byte flags = BossFlags();
                Multiplayer.Send(3, w => { w.Write(owed); w.Write(flags); }, peer);
                SendTransactions(peer, _stats(record));
            }
        }
        internal static bool PlayerReady { get { return _restored && _restore(null) == null; } }
        static byte BossFlags()
        {
            byte result = 0;
            for (int i = 0; i < Bosses.Length; i++) { var flag = FsmVariables.GlobalVariables.FindFsmBool(Bosses[i]); if (flag != null && flag.Value) result |= (byte)(1 << i); }
            return result;
        }
        internal static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == 5 || kind == 6) { MultiplayerWorld.Receive(from, kind, reader); return; }
            if (kind == 4) { ReceiveTransactions(from, reader); return; }
            if (kind != 3 || !Multiplayer.Client || from != 0 || !Multiplayer.WorldReady || !_restored || _restore(null) != null) return;
            long owed = reader.ReadInt64(); byte flags = reader.ReadByte();
            if (owed < _paid || owed < 0) return;
            var money = FsmVariables.GlobalVariables.FindFsmInt("Money");
            if (money != null && owed > _paid)
            {
                long balance = checked((long)money.Value + owed - _paid);
                if (balance > int.MaxValue) { Log.Warn("[Multiplayer] Quest payout would overflow Money; receipt remains pending"); return; }
                money.Value = (int)balance; _paid = owed;
            }
            for (int i = 0; i < Bosses.Length; i++) { var flag = FsmVariables.GlobalVariables.FindFsmBool(Bosses[i]); if (flag != null) flag.Value = (flags & (1 << i)) != 0; }
        }
    }
}
