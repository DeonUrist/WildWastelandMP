using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    internal static partial class PartyQuests
    {
        delegate bool Match(GameObject go, PlayMakerFSM name, string token, out string display);
        static Match _match;
        static Func<GameObject, bool> _forSale, _tooEmpty;
        static Type _itemType;
        static Func<object, GameObject> _itemObject;
        static Func<object, int> _stack;
        static Func<object, bool> _reserve;
        static Func<object, bool> _held;
        static Func<object, float> _distance;
        static Func<object, HutongGames.PlayMaker.FsmInt> _quantity;
        static Action<object, float> _setDistance;
        static Action<object, GameObject> _setItem;
        static Action<object, int> _setStack;
        static Action<object, bool> _setHeld;
        static Action<object, string> _setDisplay;
        static Action<object, HutongGames.PlayMaker.FsmInt> _setQuantity;
        static readonly List<GameObject> Owned = new List<GameObject>();
        static readonly HashSet<GameObject> Found = new HashSet<GameObject>();
        static void InitInventory(IModule owner, Type game)
        {
            _itemType = game.Assembly.GetType("WildWasteland.Modules.Quests.ItemHit", true);
            _match = MultiplayerAccess.Call<Match>(game, "Matches", typeof(GameObject), typeof(PlayMakerFSM), typeof(string), typeof(string).MakeByRefType());
            _forSale = MultiplayerAccess.Call<Func<GameObject, bool>>(game, "ForSale", typeof(GameObject));
            _tooEmpty = MultiplayerAccess.Call<Func<GameObject, bool>>(game, "TooEmpty", typeof(GameObject));
            _itemObject = MultiplayerAccess.Get<GameObject>(_itemType, "Go"); _stack = MultiplayerAccess.Get<int>(_itemType, "Stack"); _reserve = MultiplayerAccess.Get<bool>(_itemType, "Reserve");
            _held = MultiplayerAccess.Get<bool>(_itemType, "Held"); _distance = MultiplayerAccess.Get<float>(_itemType, "Dist"); _setDistance = MultiplayerAccess.Set<float>(_itemType, "Dist");
            _quantity = MultiplayerAccess.Get<HutongGames.PlayMaker.FsmInt>(_itemType, "Qty");
            _setItem = MultiplayerAccess.Set<GameObject>(_itemType, "Go"); _setStack = MultiplayerAccess.Set<int>(_itemType, "Stack"); _setHeld = MultiplayerAccess.Set<bool>(_itemType, "Held");
            _setDisplay = MultiplayerAccess.Set<string>(_itemType, "Display"); _setQuantity = MultiplayerAccess.Set<HutongGames.PlayMaker.FsmInt>(_itemType, "Qty");
            Patch(owner, game, "FindItems", postfix: "PartyItems"); Patch(owner, game, "TakeItems", "BeforeTakeItems");
            Patch(owner, game, "RegisterSpawned", postfix: "QuestObjectRegistered");
        }
        static void QuestObjectRegistered(GameObject __0) { if (Multiplayer.Hosting) Multiplayer.Register(__0, false); }
        static void PartyItems(string __0, Vector3 __1, float __2, bool __3, object __result)
        {
            if (!Multiplayer.Hosting) return;
            var hits = __result as IList; if (hits == null) return;
            ulong peer = _context ? _contextPeer : 0;
            Found.Clear();
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                var go = _itemObject(hits[i]);
                if (go != null && Multiplayer.OwnerOf(go) != 0 && Multiplayer.OwnerOf(go) != peer) { hits.RemoveAt(i); continue; }
                if (go != null) Found.Add(go);
            }
            if (!__3 || peer == 0) return;
            Multiplayer.OwnedItems(peer, Owned);
            foreach (var go in Owned)
            {
                if (Found.Contains(go) || _forSale(go) || _tooEmpty(go)) continue;
                var itemName = FsmUtil.Find(go, "ItemName"); string display;
                if (itemName == null || !_match(go, itemName, __0, out display)) continue;
                var quantityFsm = FsmUtil.Find(go, "Quantity");
                var quantity = quantityFsm != null ? quantityFsm.FsmVariables.FindFsmInt("Quantity") : null;
                int count = quantity != null ? Math.Max(0, quantity.Value) : 1; if (count == 0) continue;
                var hit = Activator.CreateInstance(_itemType);
                _setItem(hit, go); _setStack(hit, count); _setHeld(hit, true); _setDisplay(hit, display); _setQuantity(hit, quantity);
                hits.Add(hit); Found.Add(go);
            }
        }
        static void BeforeTakeItems(object __0, ref int __1, Vector3? __2)
        {
            if (!Multiplayer.Hosting) return;
            var hits = __0 as IList; if (hits == null) return;
            var ordered = new object[hits.Count]; hits.CopyTo(ordered, 0);
            if (__2.HasValue) foreach (var hit in ordered)
            {
                var go = _itemObject(hit); if (go == null || _held(hit)) continue;
                var delta = go.transform.position - __2.Value; delta.y = 0; _setDistance(hit, delta.magnitude);
            }
            Array.Sort(ordered, (a, b) => _held(a) != _held(b) ? (_held(a) ? 1 : -1) : _distance(a).CompareTo(_distance(b)));
            // Follow native ground/held ordering. Revoke only the remote stacks actually used, before consuming them.
            // Leave local ground items/reserve ammo to the original transaction with its count reduced accordingly.
            int remaining = __1;
            foreach (var hit in ordered)
            {
                if (remaining <= 0) break;
                if (_reserve(hit) && RemotePeer != 0)
                {
                    var reserve = hit as ItemHit; int boxes = Math.Min(_stack(hit), remaining);
                    if (reserve != null && reserve.Qty != null && MultiplayerParty.Adjust(RemotePeer, "a." + reserve.Qty.Name, -checked(boxes * reserve.PerBox)))
                    { remaining -= boxes; hits.Remove(hit); __1 -= boxes; }
                    continue;
                }
                var go = _itemObject(hit); if (go == null && !_reserve(hit)) continue;
                int take = Math.Min(_stack(hit), remaining); remaining -= take;
                if (go == null || Multiplayer.OwnerOf(go) == 0) continue;
                Multiplayer.ReturnItemToHost(go, _contextNpc != null ? _contextNpc.position + Vector3.up : _contextPlayer != null ? _contextPlayer.position + Vector3.up : go.transform.position);
                var quantity = _quantity(hit);
                if (quantity != null && _stack(hit) > take) quantity.Value = _stack(hit) - take;
                else { if (quantity != null) quantity.Value = 0; UnityEngine.Object.Destroy(go); }
                hits.Remove(hit); __1 -= take;
            }
        }
    }
}

