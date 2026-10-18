using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Survival
{
    public sealed partial class Runtime
    {
        const byte DrinkIntent = 146, SipResult = 147, Ingredient = 148, Presentation = 149;
        sealed class Drinker { internal Pot Pot; internal float Until; }
        static readonly Dictionary<ulong, Drinker> drinkers = new Dictionary<ulong, Drinker>();
        static readonly List<ulong> stopped = new List<ulong>();
        static ulong potEpoch; static float nextPotPresentation; static uint sipSequence, lastSip;
        static readonly Dictionary<ulong, float> nextIngredient = new Dictionary<ulong, float>();
        internal static void NetworkInit()
        { Multiplayer.Message += PotMessage; MultiplayerItems.ServerAuthority += HostPot; Multiplayer.SessionChanged += NetworkReset; NetworkReset(); }
        internal static void NetworkStop()
        { Multiplayer.Message -= PotMessage; MultiplayerItems.ServerAuthority -= HostPot; Multiplayer.SessionChanged -= NetworkReset; NetworkReset(); }
        static bool HostPot(GameObject root) { return root.transform.Find("StewPot.Visual") != null; }
        static void NetworkReset()
        { drinkers.Clear(); nextIngredient.Clear(); potEpoch = MultiplayerParts.Epoch; nextPotPresentation = 0; sipSequence = lastSip = 0; }
        static void PotEpoch() { if (potEpoch != MultiplayerParts.Epoch) NetworkReset(); }
        static bool Reach(ulong peer, GameObject root)
        {
            var player = Multiplayer.PlayerFor(peer);
            return root != null && root.activeInHierarchy && player != null && (peer == 0 || Multiplayer.RemoteTargetable(player))
                && (root.transform.position - player.transform.position).sqrMagnitude <= 16f;
        }
        static bool NetworkDrinking(Pot pot)
        { foreach (var drinker in drinkers.Values) if (drinker.Pot == pot && drinker.Until > Time.unscaledTime) return true; return false; }
        static void NetworkDrink(Pot pot, bool active)
        {
            if (!Multiplayer.WorldReady || MultiplayerSaving.Frozen) return;
            PotEpoch();
            if (!active && !pot.Drinking || active && Time.unscaledTime < pot.NetworkAt) return;
            pot.NetworkAt = Time.unscaledTime + .1f; int id = Multiplayer.ObjectId(pot.Root); if (id == 0) return;
            if (Multiplayer.Hosting) BeginDrink(0, pot, active);
            else Multiplayer.Send(DrinkIntent, w => { w.Write(potEpoch); w.Write(id); w.Write(active); });
        }
        static void BeginDrink(ulong peer, Pot pot, bool active)
        {
            if (!active) { Drinker existing; if (drinkers.TryGetValue(peer, out existing) && existing.Pot == pot) drinkers.Remove(peer); return; }
            if (!Reach(peer, pot.Root)) return;
            foreach (var pair in drinkers) if (pair.Key != peer && pair.Value.Pot == pot && pair.Value.Until > Time.unscaledTime) return;
            drinkers[peer] = new Drinker { Pot = pot, Until = Time.unscaledTime + .3f };
        }
        internal static void NetworkAdd(GameObject food, Pot pot)
        {
            if (!Multiplayer.WorldReady || MultiplayerSaving.Frozen || food == null) return;
            PotEpoch(); int item = Multiplayer.ObjectId(food), target = Multiplayer.ObjectId(pot.Root); if (item == 0 || target == 0) return;
            if (Multiplayer.Hosting) AddIngredient(0, food, pot);
            else Multiplayer.Send(Ingredient, w => { w.Write(potEpoch); w.Write(item); w.Write(target); });
        }
        static void AddIngredient(ulong peer, GameObject food, Pot pot)
        {
            float next; if (nextIngredient.TryGetValue(peer, out next) && Time.unscaledTime < next) return;
            nextIngredient[peer] = Time.unscaledTime + .15f;
            if (food == null || !Reach(peer, pot.Root) || !Reach(peer, food) || Multiplayer.OwnerOf(food) != peer) return;
            float nutrition, insanity; bool raw;
            if (!StewLogic.ReadIngredient(food, out nutrition, out insanity, out raw) || pot.Stew.Nutrition + nutrition > pot.Amount.Value * 25f + .00001f) return;
            if (raw) pot.Stew.Raw.Value += nutrition; else pot.Stew.Cooked.Value += nutrition;
            pot.Stew.Insanity.Value += insanity; pot.Content.Value = StewLogic.MergeContents(pot.Content.Value, (int)Contents.Meat);
            // Consumption reaches the original owner before native despawn, preventing its held-item recovery from recreating the food.
            MultiplayerItems.Consume(food);
            if (Core.Log.Verbose) Core.Log.Info("[Multiplayer] Pot ingredient committed: player " + peer + " -> " + Multiplayer.ObjectId(pot.Root));
        }
        static void NetworkTick(float dt, bool live)
        {
            if (!Multiplayer.Active) return;
            PotEpoch(); if (potEpoch == 0 || !Multiplayer.Hosting || !Multiplayer.WorldReady || !live || MultiplayerSaving.Frozen) return;
            stopped.Clear();
            foreach (var pair in drinkers)
            {
                var pot = pair.Value.Pot;
                if (pot.Root == null || Time.unscaledTime >= pair.Value.Until || !Reach(pair.Key, pot.Root) || pot.Amount.Value <= .0001f) { stopped.Add(pair.Key); continue; }
                float sip = Mathf.Min(pot.Amount.Value, .5f * dt), fraction = sip / pot.Amount.Value;
                float nutrition = pot.Stew.Nutrition * fraction, insanity = pot.Stew.Insanity.Value * fraction;
                pot.Stew.Remove(fraction, null, null, null); pot.Amount.Value -= sip;
                if (pair.Key == 0) ApplySip(sip * 12f, nutrition, insanity);
                else
                {
                    uint sequence = ++sipSequence;
                    Multiplayer.Send(SipResult, w => { w.Write(potEpoch); w.Write(sequence); w.Write(sip * 12f); w.Write(nutrition); w.Write(insanity); }, pair.Key);
                }
            }
            foreach (ulong peer in stopped) drinkers.Remove(peer);
            if (Time.unscaledTime < nextPotPresentation) return;
            nextPotPresentation = Time.unscaledTime + .25f;
            foreach (var pot in pots)
            {
                if (pot.Root == null) continue;
                int id = Multiplayer.ObjectId(pot.Root); if (id == 0) continue;
                bool heating = pot.Stew.Heating, pouring = pot.Pouring;
                Multiplayer.Send(Presentation, w => { w.Write(potEpoch); w.Write(id); w.Write(heating); w.Write(pouring); }, reliable: false);
            }
        }
        static void ApplySip(float water, float nutrition, float insanity)
        {
            var vars = FsmVariables.GlobalVariables;
            var thirstValue = vars.FindFsmFloat("Thirst"); var hungerValue = vars.FindFsmFloat("Hunger"); var sanityValue = vars.FindFsmFloat("Sanity");
            if (thirstValue != null) thirstValue.Value = Mathf.Max(0, thirstValue.Value - water);
            if (hungerValue != null) hungerValue.Value = Mathf.Max(0, hungerValue.Value - nutrition);
            if (sanityValue != null) sanityValue.Value = Mathf.Clamp(sanityValue.Value + insanity, 0, 100);
        }
        static void PotMessage(ulong from, byte kind, BinaryReader reader)
        {
            if (kind < DrinkIntent || kind > Presentation || !Multiplayer.WorldReady) return;
            PotEpoch(); if (reader.ReadUInt64() != potEpoch || potEpoch == 0) return;
            if (kind == SipResult)
            {
                if (!Multiplayer.Client || from != 0) return;
                uint sequence = reader.ReadUInt32(); float water = reader.ReadSingle(), nutrition = reader.ReadSingle(), insanity = reader.ReadSingle();
                if (sequence <= lastSip || !(water >= 0 && water <= 120) || !(nutrition >= 0 && nutrition <= 10000) || !(insanity >= 0 && insanity <= 10000)
                    || reader.BaseStream.Position != reader.BaseStream.Length) return;
                lastSip = sequence; ApplySip(water, nutrition, insanity); return;
            }
            int id = reader.ReadInt32(); var root = Multiplayer.ObjectById(id);
            if (kind == Presentation)
            {
                bool heating = reader.ReadBoolean(), pouring = reader.ReadBoolean();
                if (!Multiplayer.Client || from != 0 || reader.BaseStream.Position != reader.BaseStream.Length) return;
                var pot = FindPot(root); if (pot != null) { pot.Stew.Heating = heating; pot.NetworkPouring = pouring; } return;
            }
            if (!Multiplayer.Hosting || from == 0 || MultiplayerSaving.Frozen) return;
            if (kind == DrinkIntent) { bool active = reader.ReadBoolean(); var pot = FindPot(root); if (pot != null && reader.BaseStream.Position == reader.BaseStream.Length) BeginDrink(from, pot, active); return; }
            int target = reader.ReadInt32(); var recipient = FindPot(Multiplayer.ObjectById(target));
            if (recipient != null && reader.BaseStream.Position == reader.BaseStream.Length) AddIngredient(from, root, recipient);
        }
    }
}
