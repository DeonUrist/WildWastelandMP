using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static partial class MultiplayerItems
    {
        static Func<object, byte[]> _spawn;
        static Action<GameObject> _dropHand;
        static readonly Dictionary<int, float> CreateStarted = new Dictionary<int, float>();
        static readonly List<int> FinishedCreates = new List<int>();
        internal static string InventoryAdoptionFailure
        {
            get
            {
                if (!Multiplayer.Client) return null;
                var pending = _pendingCreates(null); FinishedCreates.Clear();
                foreach (var pair in CreateStarted) if (!pending.Contains(pair.Key)) FinishedCreates.Add(pair.Key);
                foreach (int id in FinishedCreates) CreateStarted.Remove(id);
                foreach (DictionaryEntry entry in pending)
                {
                    int id = (int)entry.Key; float started;
                    if (!CreateStarted.TryGetValue(id, out started)) { CreateStarted[id] = Time.unscaledTime; continue; }
                    if (Time.unscaledTime - started >= 10f)
                    {
                        var root = entry.Value as GameObject;
                        return "The host did not adopt " + (root != null ? root.name : "a locally created item") + " within 10 seconds. Inventory synchronization was not acknowledged.";
                    }
                }
                return null;
            }
        }
        static void InstallLifecycle()
        {
            var world = Multiplayer.Type("Net.WorldObjects"); var tracked = Multiplayer.Type("Net.WorldObjects+Tracked");
            _spawn = MultiplayerAccess.Call<Func<object, byte[]>>(world, "BuildSpawn", tracked);
            _dropHand = MultiplayerAccess.Call<Action<GameObject>>(world, "DropFromHand", typeof(GameObject));
            if (!Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "OnGone"), prefix: AccessTools.Method(typeof(MultiplayerItems), "BeforeGone"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "PrefabNameOf"), prefix: AccessTools.Method(typeof(MultiplayerItems), "BeforePrefabName"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "OnClientCreated"), postfix: AccessTools.Method(typeof(MultiplayerItems), "AfterCreated"))
                || !Patches.Patch(Multiplayer.PatchOwner, AccessTools.Method(world, "RemoveLocallySpawnedObjects"), postfix: AccessTools.Method(typeof(MultiplayerItems), "RemoveLooseHeldCopy")))
                throw new InvalidOperationException("MP object lifecycle authority hooks failed");
            Multiplayer.SessionChanged += ResetCreates;
        }
        static void ResetCreates() { if (!Multiplayer.SessionActive) CreateStarted.Clear(); }
        static void BeforePrefabName(ref string __0)
        { if (__0 != null && __0.StartsWith("MP_", StringComparison.Ordinal)) __0 = __0.Substring(3); }
        static void AfterCreated(GameObject __0)
        {
            if (!Multiplayer.Client || __0 == null) return;
            foreach (DictionaryEntry entry in _pendingCreates(null))
                if (ReferenceEquals(entry.Value, __0) && !CreateStarted.ContainsKey((int)entry.Key)) CreateStarted[(int)entry.Key] = Time.unscaledTime;
        }
        static bool BeforeGone(ulong __0, BinaryReader __1)
        {
            if (!Multiplayer.Hosting) return true;
            long position = __1.BaseStream.Position; int id;
            try { id = __1.ReadInt32(); if (__1.BaseStream.Position != __1.BaseStream.Length) return false; }
            finally { __1.BaseStream.Position = position; }
            var root = Multiplayer.ObjectById(id); var tracked = Multiplayer.TrackedState(root);
            if (root == null || tracked == null || __0 == 0 || !MultiplayerSleepers.WelcomedPeer(__0)) return false;
            if (ResourceLocked(id)) return false; // the tokenized resource completion owns any native DestroySelf
            ulong owner = _owner(tracked); var player = Multiplayer.PlayerFor(__0);
            if (owner == __0 || owner == 0 && _kind(tracked) == 0 && player != null && Multiplayer.RemoteTargetable(player)
                && (player.transform.position - root.transform.position).sqrMagnitude <= 16f && !ServerOwned(root)) return true;
            _capture(tracked); MultiplayerReliable.Send(_spawn(tracked), __0);
            if (Log.Verbose) Log.Info("[Multiplayer] Object deletion rejected outside ownership/interaction reach: " + id + " from " + __0);
            return false;
        }
        static void RemoveLooseHeldCopy()
        {
            if (!Multiplayer.Client || GameRefs.PlayerCameraHolder == null) return;
            var hand = GameRefs.PlayerCameraHolder.transform.Find("PlayerCamera/Hand"); if (hand == null) return;
            // Native restores equipped slots independently; a loose GrabItem copy has no saved slot to reconcile.
            for (int i = hand.childCount - 1; i >= 0; i--)
            {
                var root = hand.GetChild(i).gameObject;
                if (Multiplayer.ObjectId(root) != 0 || root.GetComponent<Rigidbody>() == null || FsmUtil.Find(root, "saveItemVar") == null) continue;
                bool adopting = false;
                foreach (DictionaryEntry entry in _pendingCreates(null)) if (ReferenceEquals(entry.Value, root)) { adopting = true; break; }
                if (adopting) continue;
                _dropHand(root);
                var registry = GameRefs.NewGOArrayList;
                if (registry != null) foreach (var list in registry.GetComponents<PlayMakerArrayListProxy>()) if (list.referenceName == "ArrayList_Items") list.arrayList.Remove(root);
                UnityEngine.Object.Destroy(root);
                if (Log.Verbose) Log.Info("[Multiplayer] Removed untracked loose hand copy before host inventory restoration");
            }
        }
    }
}
