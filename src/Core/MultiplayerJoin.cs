using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Native Hello runs before the guest's scene exists. Capture once the guest requests its part epoch,
    // then feed spawns into the ordered world lane without a synchronous all-object burst.
    internal static class MultiplayerJoin
    {
        sealed class Transfer { internal readonly List<int> Ids = new List<int>(); internal int Next; internal bool Ready; internal uint FinalSequence; }
        static readonly Dictionary<ulong, Transfer> Transfers = new Dictionary<ulong, Transfer>();
        static readonly List<ulong> Gone = new List<ulong>();
        static Func<object, IDictionary> _objects;
        static Func<object, GameObject> _object;
        static Action<object> _capture;
        static Func<object, byte[]> _spawn;
        internal static void Install()
        {
            var world = Multiplayer.Type("Net.WorldObjects"); var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
            _objects = MultiplayerAccess.Get<IDictionary>(world, "objects"); _object = MultiplayerAccess.Get<GameObject>(tracked, "Object");
            _capture = MultiplayerAccess.Call<Action<object>>(world, "Capture", tracked);
            _spawn = MultiplayerAccess.Call<Func<object, byte[]>>(world, "BuildSpawn", tracked);
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "SendAllTo"), prefix: AccessTools.Method(typeof(MultiplayerJoin), "Begin"))) throw new InvalidOperationException("MP incremental join hook failed");
            Multiplayer.SessionChanged += Changed;
            MultiplayerPlayers.Left += Left;
            Scheduler.Interval(Multiplayer.PatchOwner, "MP join object stream", .05f, Tick);
        }
        static void Changed() { if (!Multiplayer.SessionActive) Transfers.Clear(); }
        static void Left(ulong peer) { Transfers.Remove(peer); }
        static bool Begin(ulong __0)
        { if (!Multiplayer.Hosting) return true; if (!Transfers.ContainsKey(__0)) Transfers.Add(__0, new Transfer()); return false; }
        internal static bool Joining(ulong peer) { Transfer transfer; return Transfers.TryGetValue(peer, out transfer) && (!transfer.Ready || transfer.Next < transfer.Ids.Count || !MultiplayerReliable.Acknowledged(peer, transfer.FinalSequence)); }
        internal static void Ready(ulong peer)
        {
            Transfer transfer; if (!Transfers.TryGetValue(peer, out transfer)) { transfer = new Transfer(); Transfers.Add(peer, transfer); }
            transfer.Ids.Clear(); transfer.Next = 0; transfer.FinalSequence = 0; transfer.Ready = true;
            foreach (DictionaryEntry entry in _objects(null)) transfer.Ids.Add((int)entry.Key);
        }
        static void Tick()
        {
            if (!Multiplayer.Hosting || !Multiplayer.WorldReady || Transfers.Count == 0) return;
            var objects = _objects(null); Gone.Clear();
            foreach (var pair in Transfers)
            {
                if (!MultiplayerSleepers.WelcomedPeer(pair.Key)) { Gone.Add(pair.Key); continue; }
                var transfer = pair.Value; if (!transfer.Ready) continue;
                int sent = 0;
                while (transfer.Next < transfer.Ids.Count && sent < 16 && MultiplayerReliable.CanQueue(pair.Key, 64))
                {
                    int id = transfer.Ids[transfer.Next++]; if (!objects.Contains(id) || _object(objects[id]) == null) continue;
                    _capture(objects[id]); MultiplayerReliable.Send(_spawn(objects[id]), pair.Key); sent++;
                    transfer.FinalSequence = MultiplayerReliable.QueuedSequence(pair.Key);
                }
            }
            foreach (ulong peer in Gone) Transfers.Remove(peer);
        }
    }
}
