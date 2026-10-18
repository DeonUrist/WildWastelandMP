using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Guns
{
    internal static partial class PerGunAmmo
    {
        sealed class NetworkMagazine
        {
            internal GameObject Item; internal string Type;
            internal FsmInt Rounds = new FsmInt("WWMagazineRounds") { Value = -1 };
            internal FsmBool Hammer = new FsmBool("WWMagazineHammer");
        }
        static readonly Dictionary<GameObject, NetworkMagazine> NetworkMagazines = new Dictionary<GameObject, NetworkMagazine>();
        static readonly List<GameObject> RemovedMagazines = new List<GameObject>();
        internal static void StartNetworkMagazines()
        {
            MultiplayerItems.Variables += MagazineVariables; MultiplayerItems.BeforeFlush += Capture;
        }
        internal static void StopNetworkMagazines()
        { MultiplayerItems.Variables -= MagazineVariables; MultiplayerItems.BeforeFlush -= Capture; }
        static void MagazineVariables(GameObject go, List<NamedVariable> values)
        {
            var fsm = FsmUtil.Find(go, "weaponType"); var type = fsm != null ? fsm.FsmVariables.FindFsmString("weaponType") : null;
            if (type == null || string.IsNullOrEmpty(type.Value)) return;
            NetworkMagazine entry;
            if (!NetworkMagazines.TryGetValue(go, out entry))
            {
                entry = new NetworkMagazine { Item = go, Type = type.Value }; Magazine magazine;
                if (_ledger.TryGet(go.name, type.Value, out magazine)) { entry.Rounds.Value = magazine.Rounds; entry.Hammer.Value = magazine.Hammer; }
                NetworkMagazines.Add(go, entry);
            }
            MultiplayerItems.Add(values, entry.Rounds); MultiplayerItems.Add(values, entry.Hammer);
        }
        static void CaptureNetworkMagazine(GameObject item, Magazine magazine)
        {
            NetworkMagazine entry;
            if (Multiplayer.Active && item != null && NetworkMagazines.TryGetValue(item, out entry) && !Multiplayer.OwnedElsewhere(item))
            { entry.Rounds.Value = magazine.Rounds; entry.Hammer.Value = magazine.Hammer; }
        }
        static void ReadNetworkMagazine(GameObject item, string type)
        {
            NetworkMagazine entry;
            if (Multiplayer.Active && NetworkMagazines.TryGetValue(item, out entry) && entry.Rounds.Value >= 0)
                _ledger.Import(item.name, type, new Magazine(entry.Rounds.Value, entry.Hammer.Value));
        }
        static void ImportNetworkMagazines()
        {
            RemovedMagazines.Clear();
            foreach (var pair in NetworkMagazines)
            {
                if (pair.Key == null) { RemovedMagazines.Add(pair.Key); continue; }
                if (Multiplayer.Hosting && pair.Value.Rounds.Value >= 0)
                    _ledger.Import(pair.Key.name, pair.Value.Type, new Magazine(pair.Value.Rounds.Value, pair.Value.Hammer.Value));
            }
            foreach (var item in RemovedMagazines) NetworkMagazines.Remove(item);
        }
    }
}
