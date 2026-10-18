using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;

namespace WildWasteland.Core
{
    internal static partial class MultiplayerParty
    {
        // Cumulative entitlements and receipts travel with the native per-player stats record.
        // Reports may be delayed or duplicated; host decisions include every unacknowledged debit.
        static readonly string[] Resources = { "g.Money", "a.ammo_9mm", "a.ammo_22caliber", "a.ammo_12gauge", "a.ammo_20gauge", "a.ammo_556", "a.ammo_762", "a.ammo_3006", "a.ammo_arrow" };
        static readonly string[][] DebitOwed = Keys("ww.debitOwed"), DebitPaid = Keys("ww.debitPaid");
        static readonly string[] CreditOwed = { "ww.creditOwed0", "ww.creditOwed1", "ww.creditOwed2", "ww.creditOwed3" };
        static readonly string[] CreditPaid = { "ww.creditPaid0", "ww.creditPaid1", "ww.creditPaid2", "ww.creditPaid3" };
        static readonly long[] Debits = new long[Resources.Length];
        static long _credit;
        static Func<PlayMakerFSM> _ammo;
        static string[][] Keys(string prefix)
        {
            var keys = new string[Resources.Length][];
            for (int i = 0; i < keys.Length; i++) { keys[i] = new string[4]; for (int j = 0; j < 4; j++) keys[i][j] = prefix + i + "." + j; }
            return keys;
        }
        static int Resource(string name) { return Array.IndexOf(Resources, name); }
        internal static int Balance(ulong peer, string name)
        {
            if (!Multiplayer.Hosting || peer == 0) return -1;
            int index = Resource(name); var record = _record(peer); if (index < 0 || record == null) return -1;
            var stats = _stats(record); float value;
            if (!stats.TryGetValue(name, out value) || float.IsNaN(value) || float.IsInfinity(value)) return -1;
            long balance = (long)value - (Read(stats, DebitOwed[index]) - Read(stats, DebitPaid[index]));
            if (index == 0) balance += Read(stats, CreditOwed) - Read(stats, CreditPaid) + Read(stats, OwedKeys) - Read(stats, PaidKeys);
            return (int)Math.Max(0, Math.Min(int.MaxValue, balance));
        }
        internal static bool Adjust(ulong peer, string name, int amount)
        {
            int index = Resource(name), balance = Balance(peer, name);
            if (index < 0 || balance < 0 || (long)balance + amount < 0 || (long)balance + amount > int.MaxValue || (index != 0 && amount > 0)) return false;
            var stats = _stats(_record(peer)); var keys = amount >= 0 ? CreditOwed : DebitOwed[index];
            long before = Read(stats, keys), delta = Math.Abs((long)amount); if (before > long.MaxValue - delta) return false;
            Write(stats, keys, before + delta); _nextSnapshot = 0; return true;
        }
        static void ResetTransactions() { Array.Clear(Debits, 0, Debits.Length); _credit = 0; }
        static void CaptureTransactions(Dictionary<string, float> stats)
        { Write(stats, CreditPaid, _credit); for (int i = 0; i < Debits.Length; i++) Write(stats, DebitPaid[i], Debits[i]); }
        static void RestoreTransactions(Dictionary<string, float> stats)
        { _credit = Read(stats, CreditPaid); for (int i = 0; i < Debits.Length; i++) Debits[i] = Read(stats, DebitPaid[i]); }
        static void Preserve(Dictionary<string, float> before, Dictionary<string, float> after, Dictionary<string, float> report, string[] owed, string[] paid)
        {
            long total = Read(before, owed); Write(after, owed, total);
            Write(after, paid, Math.Min(total, Math.Max(Read(before, paid), Read(report, paid))));
        }
        static void PreserveTransactions(Dictionary<string, float> before, Dictionary<string, float> after, Dictionary<string, float> report)
        {
            Preserve(before, after, report, CreditOwed, CreditPaid);
            for (int i = 0; i < Debits.Length; i++) Preserve(before, after, report, DebitOwed[i], DebitPaid[i]);
        }
        static void SendTransactions(ulong peer, Dictionary<string, float> stats)
        {
            Multiplayer.Send(4, w => { w.Write(Read(stats, CreditOwed)); for (int i = 0; i < Debits.Length; i++) w.Write(Read(stats, DebitOwed[i])); }, peer);
        }
        static void ReceiveTransactions(ulong peer, BinaryReader reader)
        {
            if (!Multiplayer.Client || peer != 0 || !Multiplayer.WorldReady || !_restored || _restore(null) != null) return;
            long credit = reader.ReadInt64(); var owed = new long[Debits.Length];
            for (int i = 0; i < owed.Length; i++) owed[i] = reader.ReadInt64();
            if (reader.BaseStream.Position != reader.BaseStream.Length || credit < _credit) return;
            for (int i = 0; i < owed.Length; i++) if (owed[i] < Debits[i]) return;
            if (_ammo == null) _ammo = MultiplayerAccess.Call<Func<PlayMakerFSM>>(Multiplayer.Type("Game.PlayerVitals"), "AmmoFsm", Type.EmptyTypes);
            var ammo = _ammo(); var counters = new FsmInt[Resources.Length]; counters[0] = FsmVariables.GlobalVariables.FindFsmInt("Money");
            for (int i = 1; i < counters.Length; i++) counters[i] = ammo != null ? ammo.FsmVariables.FindFsmInt(Resources[i].Substring(2)) : null;
            var values = new int[counters.Length];
            for (int i = 0; i < counters.Length; i++)
            {
                long delta = owed[i] - Debits[i], addition = i == 0 ? credit - _credit : 0;
                if (counters[i] == null) { if (delta != 0 || addition != 0) return; continue; }
                long value = (long)counters[i].Value + addition - delta;
                if (value < 0 || value > int.MaxValue) return;
                values[i] = (int)value;
            }
            for (int i = 0; i < counters.Length; i++) { if (counters[i] != null) counters[i].Value = values[i]; Debits[i] = owed[i]; }
            _credit = credit;
        }
    }
}
