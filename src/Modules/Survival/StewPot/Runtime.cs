using System;
using System.Collections.Generic;
using System.Globalization;
using ES3Internal;
using HutongGames.PlayMaker;
using InsaneSystems.InputManager;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using Object = UnityEngine.Object;

namespace WildWasteland.Modules.Survival
{
    // All runtime state lives here, never on saved objects. Native saveItemVar owns the litres.
    public sealed partial class Runtime
    {
        internal sealed class Pot
        {
            internal GameObject Root;
            internal Transform Visual, Liquid;
            internal MeshRenderer LiquidRenderer;
            internal Collider Collider;
            internal Rigidbody Body;
            internal FsmFloat Amount;
            internal FsmInt Content;
            internal PlayMakerFSM Drink;
            internal WaterPour Stream;
            internal float Last = -1, Pose;
            internal int LastContent = -1, LastDecilitre = -1;
            internal bool Drinking, Pouring, Filling, ColliderReady;
            internal string Label;
            internal AudioSource Audio;
            internal Stew Stew;
            internal float NetworkAt;
            internal bool NetworkPouring;
        }

        static readonly List<Pot> pots = new List<Pot>();
        static readonly Dictionary<GameObject, Pot> byObject = new Dictionary<GameObject, Pot>();
        static readonly RaycastHit[] hits = new RaycastHit[64];
        static Transform effects;
        static readonly List<Transform> discovery = new List<Transform>();
        static readonly List<GameObject> roots = new List<GameObject>();
        static bool discoveryPending;
        static PlayMakerFSM menu, save, grab, useDrink;
        static FsmGameObject grabbed, hand, cameraObject, lookedAt;
        static FsmFloat thirst, hunger, sanity;
        static Transform cameraTransform;
        static Camera interactionCamera;
        static Text uiName, uiAmount, uiCapacity, uiUse, uiDetails;
        static bool failureLogged;
        static AudioClip drinkingClip;

        internal static bool DrinkInput(HutongGames.PlayMaker.Actions.GetButtonDown __instance)
        {
            if (useDrink == null || __instance.Fsm != useDrink.Fsm || grabbed == null || grabbed.Value == null) return true;
            Pot pot;
            // A held pot owns Use; do not also begin a can animation on a different object under the cursor.
            return !byObject.TryGetValue(grabbed.Value, out pot) || hand == null || hand.Value == null
                || !pot.Root.transform.IsChildOf(hand.Value.transform);
        }

        internal static void Init(Transform owner) { effects = owner; Clear(); discoveryPending = true; }

        internal static void BeginDiscovery()
        {
            discoveryPending = false;
            discovery.Clear(); roots.Clear();
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            scene.GetRootGameObjects(roots);
            for (int i = 0; i < roots.Count; i++) discovery.Add(roots[i].transform);
            roots.Clear();
        }

        internal static bool Discover()
        {
            if (discoveryPending) BeginDiscovery();
            if (discovery.Count == 0) return false;
            int index = discovery.Count - 1;
            var node = discovery[index]; discovery.RemoveAt(index);
            if (node == null) return discovery.Count != 0;
            var prefab = node.GetComponent<ES3Prefab>();
            if (prefab != null && prefab.prefabId == Factory.PrefabId) Register(node.gameObject);
            for (int i = 0; i < node.childCount; i++) discovery.Add(node.GetChild(i));
            return discovery.Count != 0;
        }

        internal static void Shutdown()
        {
            for (int i = pots.Count - 1; i >= 0; i--)
            {
                var pot = pots[i];
                if (pot.Root != null)
                {
                    Pose(pot, false, 1f);
                    if (pot.Audio != null && pot.Drinking) pot.Audio.Stop();
                    if (pot.Drink != null && pot.Drink.ActiveStateName == "on") pot.Drink.SendEvent("DrinkOFF");
                }
                Remove(i);
            }
            Clear(); discovery.Clear(); roots.Clear(); effects = null; discoveryPending = false;
        }

        internal static Pot FindPot(GameObject go)
        {
            Pot pot;
            return go != null && byObject.TryGetValue(go, out pot) ? pot : null;
        }

        internal static void Clear()
        {
            // sceneLoaded runs after new objects' Awake: keep registrations belonging to the new scene.
            for (int i = pots.Count - 1; i >= 0; i--) if (pots[i].Root == null) Remove(i);
            menu = save = grab = useDrink = null; grabbed = hand = cameraObject = lookedAt = null;
            thirst = hunger = sanity = null; cameraTransform = null; interactionCamera = null;
            uiName = uiAmount = uiCapacity = uiUse = uiDetails = null;
            StewLogic.Clear();
            NetworkReset();
            failureLogged = false;
        }

        internal static void Register(GameObject root)
        {
            if (root == Factory.Cached || byObject.ContainsKey(root)) return;
            var visual = root.transform.Find("StewPot.Visual");
            if (visual == null) return;
            var liquid = visual.Find("Liquid");
            var amount = Factory.Fsm(root, "LiquidAmount");
            var pot = new Pot
            {
                Root = root, Visual = visual, Liquid = liquid,
                LiquidRenderer = liquid.GetComponent<MeshRenderer>(), Collider = root.GetComponent<Collider>(),
                Body = root.GetComponent<Rigidbody>(), Audio = root.GetComponent<AudioSource>(),
                Amount = amount.FsmVariables.GetFsmFloat("Liquid"), Drink = Factory.Fsm(root, "Drink"),
                Content = Factory.Fsm(root, "saveItemVar").FsmVariables.GetFsmInt("StewContents"),
                Stew = new Stew(Factory.Fsm(root, "saveItemVar"))
            };
            byObject.Add(root, pot); pots.Add(pot);
            Core.Multiplayer.Register(root, false);
            if (SurvivalModule.Verbose.Value) WildWasteland.Core.Log.Always("[Survival] StewPot registered: " + root.name);
        }

        static void Remove(int index)
        {
            var pot = pots[index];
            byObject.Remove(pot.Root);
            if (pot.Stream != null) pot.Stream.Dispose();
            pot.Stew.Dispose();
            pots.RemoveAt(index);
        }

        internal static void LateTick()
        {
            if (pots.Count == 0) return;
            bool live = Multiplayer.Active ? GameRefs.WorldRunning : menu != null && menu.ActiveStateName == "play" && save != null && save.ActiveStateName == "isPlay" && Time.timeScale > 0;
            bool input = live && GameRefs.InGame && Application.isFocused && !ApocasetterBridge.InputBlocked && !WildWasteland.Core.UI.EditorSession.Busy;
            bool use = input && InputController.GetKeyActionIsActive("Use");
            float dt = live ? Time.deltaTime : 0;
            StewLogic.Clock += dt;
            NetworkTick(dt, live);
            GameObject heldItem = grabbed != null ? grabbed.Value : null;
            if (heldItem != null && (hand == null || hand.Value == null || !heldItem.transform.IsChildOf(hand.Value.transform))) heldItem = null;
            StewLogic.Interact(heldItem, interactionCamera, input, grab);
            for (int i = pots.Count - 1; i >= 0; i--)
            {
                var pot = pots[i];
                if (pot.Root == null) { Remove(i); continue; }
                if (live && !pot.ColliderReady)
                {
                    // Old saves restore the previous visual-mesh collider after prefab Awake.
                    var collider = pot.Collider as MeshCollider;
                    if (collider != null) { collider.convex = true; collider.sharedMesh = Factory.ColliderMesh; }
                    pot.ColliderReady = true;
                }
                bool authority = Core.Multiplayer.ItemAuthority(pot.Root);
                pot.Stew.Tick(pot, dt, live, effects);
                if (!pot.Root.activeInHierarchy) continue;
                bool held = hand != null && hand.Value != null && pot.Root.transform.IsChildOf(hand.Value.transform);
                bool nativeDrink = pot.Drink.ActiveStateName == "on";
                bool drinking = use && (held || nativeDrink) && pot.Amount.Value > .0001f && (Core.Multiplayer.Active || Core.Multiplayer.RequestItemAuthority(pot.Root));
                if (Core.Multiplayer.Active) NetworkDrink(pot, drinking);
                if (drinking && !Core.Multiplayer.Active)
                {
                    // Match native water-can consumption: .5 L/s and 6 thirst/s, including the final partial sip.
                    float sip = Mathf.Min(pot.Amount.Value, .5f * dt);
                    pot.Stew.Remove(sip / pot.Amount.Value, null, hunger, sanity);
                    pot.Amount.Value -= sip;
                    if (thirst != null) thirst.Value = Mathf.Max(0, thirst.Value - sip * 12f);
                }
                if (nativeDrink && !drinking) pot.Drink.SendEvent("DrinkOFF");
                if (pot.Drinking != drinking)
                {
                    pot.Drinking = drinking;
                    if (pot.Audio != null)
                    {
                        if (drinking && drinkingClip != null) { pot.Audio.clip = drinkingClip; pot.Audio.loop = true; pot.Audio.Play(); }
                        else pot.Audio.Stop();
                    }
                    Trace(pot, drinking ? "drinking" : "drinking stopped");
                }
                Pose(pot, drinking, dt);
                bool pouring = authority && SurvivalModule.AllowStewPotSpilling.Value && live && !drinking && !NetworkDrinking(pot) && pot.Pose < .001f && pot.Amount.Value > .0001f && Vector3.Dot(pot.Root.transform.up, Vector3.up) <= .0872f;
                if (pouring) Pour(pot, dt);
                else if (Core.Multiplayer.Client && pot.NetworkPouring && live) Pour(pot, 0, false);
                else if (pot.Stream != null) pot.Stream.SetVisible(false);
                if (pouring != pot.Pouring) { pot.Pouring = pouring; Trace(pot, pouring ? "pouring" : "pouring stopped"); }
                UpdateLiquid(pot);
                if (input && (held || StewLogic.Target == pot || (lookedAt != null && lookedAt.Value == pot.Root))) ShowLabel(pot);
            }
        }

        internal static void Resolve()
        {
            var globals = FsmVariables.GlobalVariables;
            if (menu == null) menu = GameRefs.Menu;
            if (save == null) save = GameRefs.SaveLoadGame;
            if (cameraObject == null || cameraObject.Value == null) cameraObject = globals.GetFsmGameObject("PlayerCamera");
            if (cameraObject.Value != null && cameraTransform == null)
            {
                cameraTransform = cameraObject.Value.transform;
                grab = Factory.Fsm(cameraObject.Value, "GrabItem");
                useDrink = Factory.Fsm(cameraObject.Value, "UseDrink");
                if (grab != null) grabbed = grab.FsmVariables.GetFsmGameObject("Item");
                var look = Factory.Fsm(cameraObject.Value, "ItemNameLook");
                if (look != null) lookedAt = look.FsmVariables.GetFsmGameObject("Item");
            }
            interactionCamera = GameRefs.MainCamera;
            hand = globals.GetFsmGameObject("Hand");
            thirst = globals.GetFsmFloat("Thirst");
            hunger = globals.GetFsmFloat("Hunger"); sanity = globals.GetFsmFloat("Sanity");
            if (uiName == null) uiName = Text(globals, "UI_ItemName");
            if (uiAmount == null) uiAmount = Text(globals, "UI_ItemName_2");
            if (uiCapacity == null) uiCapacity = Text(globals, "UI_ItemName_3");
            if (uiUse == null) uiUse = Text(globals, "UI_ItemUse");
            if (uiDetails == null) uiDetails = Text(globals, "UI_ItemName_4");
            if (Factory.Cached == null && cameraTransform != null)
            {
                try { Factory.Template(); }
                catch (Exception error) { if (!failureLogged) { failureLogged = true; WildWasteland.Core.Log.Warn("[Survival] StewPot model not ready: " + error); } }
            }
            if (drinkingClip == null && cameraTransform != null)
            {
                // Audio is borrowed once; no model, animator or arm assets are reused.
                var source = cameraTransform.Find("ItemAnim/Water Can Plastic Anim");
                var fsm = source != null ? Factory.Fsm(source.gameObject, "Animation") : null;
                if (fsm != null)
                    foreach (var state in fsm.FsmStates)
                        foreach (var action in state.Actions)
                        {
                            var clip = action as HutongGames.PlayMaker.Actions.SetAudioClip;
                            if (clip != null && clip.audioClip.Value is AudioClip) { drinkingClip = (AudioClip)clip.audioClip.Value; break; }
                        }
            }
        }

        static Text Text(FsmVariables globals, string name)
        {
            var go = globals.GetFsmGameObject(name).Value;
            return go != null ? go.GetComponent<Text>() : null;
        }

        static void Pose(Pot pot, bool drinking, float dt)
        {
            pot.Pose = Mathf.MoveTowards(pot.Pose, drinking ? 1 : 0, dt > 0 ? dt / .35f : 1);
            if (pot.Pose < .001f || cameraTransform == null)
            {
                pot.Visual.localPosition = Vector3.zero; pot.Visual.localRotation = Quaternion.identity;
                return;
            }
            // Animate the actual pot and its live liquid together. The physical root stays with native GrabItem.
            var target = cameraTransform.TransformPoint(new Vector3(0, -.30f, .48f));
            var rotation = cameraTransform.rotation * Quaternion.Euler(-32f, 0, 0);
            var sourcePosition = pot.Root.transform.position;
            pot.Visual.position = Vector3.Lerp(sourcePosition, target, Mathf.SmoothStep(0, 1, pot.Pose));
            pot.Visual.rotation = Quaternion.Slerp(pot.Root.transform.rotation, rotation, pot.Pose);
            pot.Visual.localScale = Vector3.one;
        }

        static void UpdateLiquid(Pot pot)
        {
            pot.Amount.Value = Mathf.Clamp(pot.Amount.Value, 0, Factory.Capacity);
            if (pot.Amount.Value <= .0001f && Core.Multiplayer.ItemAuthority(pot.Root)) { pot.Stew.Empty(); pot.Content.Value = (int)Contents.Water; }
            int type = Mathf.Clamp(pot.Content.Value, 1, 5);
            int materialKey = pot.Stew.MaterialKey(type);
            bool changed = Mathf.Abs(pot.Last - pot.Amount.Value) > .00001f;
            bool filling = pot.Last >= 0 && pot.Amount.Value > pot.Last + .001f;
            if (filling && !pot.Filling) Trace(pot, "filled");
            pot.Filling = filling;
            if (!changed && materialKey == pot.LastContent) return;
            pot.Last = pot.Amount.Value;
            pot.Liquid.gameObject.SetActive(pot.Amount.Value > .0001f);
            pot.Liquid.localPosition = Vector3.up * Mathf.Lerp(Factory.Bottom, Factory.Top, pot.Amount.Value / Factory.Capacity);
            if (materialKey != pot.LastContent) pot.LiquidRenderer.sharedMaterial = pot.Stew.Material(type);
            pot.LastContent = materialKey;
            int decilitre = Mathf.RoundToInt(pot.Amount.Value * 10);
            if (decilitre != pot.LastDecilitre)
            {
                pot.LastDecilitre = decilitre;
                pot.Label = pot.Amount.Value.ToString("0.0", CultureInfo.InvariantCulture) + " L";
            }
            if (pot.Body != null) pot.Body.mass = 3f + pot.Amount.Value;
        }

        static void ShowLabel(Pot pot)
        {
            if (uiName != null) uiName.text = "Stew Pot";
            if (uiAmount != null) uiAmount.text = pot.Label;
            if (uiCapacity != null) uiCapacity.text = "/ 20 L";
            if (uiUse != null) uiUse.text = StewLogic.Hint(pot);
            if (uiDetails != null) uiDetails.text = pot.Stew.Details;
        }

        static void Pour(Pot source, float dt, bool apply = true)
        {
            var tr = source.Root.transform;
            Vector3 low = Vector3.ProjectOnPlane(Vector3.down, tr.up).normalized;
            if (low.sqrMagnitude < .01f) low = tr.forward;
            Vector3 start = tr.TransformPoint(Vector3.up * Factory.Rim) + low * (Factory.Radius * tr.lossyScale.x);
            float distance = 6f;
            Pot receiver = null;
            int count = Physics.RaycastNonAlloc(start + Vector3.up * .002f, Vector3.down, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
            {
                var hit = hits[h];
                if (hit.collider == source.Collider || hit.collider.transform.IsChildOf(tr)) continue;
                if (hit.distance < distance) distance = hit.distance;
            }
            // Intersect the actual open mouth, not the solid collider's convex top or side wall.
            for (int i = 0; i < pots.Count; i++)
            {
                var pot = pots[i];
                if (pot == source || pot.Root == null || !pot.Root.activeInHierarchy || pot.Pose > .001f) continue;
                var other = pot.Root.transform;
                if (Vector3.Dot(other.up, Vector3.up) < .7f) continue;
                var mouth = other.TransformPoint(Vector3.up * Factory.Rim);
                float denominator = Vector3.Dot(Vector3.down, other.up);
                float t = Vector3.Dot(mouth - start, other.up) / denominator;
                if (t < 0 || t > distance + .015f) continue;
                var point = other.InverseTransformPoint(start + Vector3.down * t);
                if (point.x * point.x + point.z * point.z > Factory.Radius * Factory.Radius) continue;
                distance = t; receiver = pot;
            }
            float rate = Mathf.Lerp(2f, 8f, Mathf.Clamp01(-Vector3.Dot(tr.up, Vector3.up)));
            if (apply && receiver != null && !Core.Multiplayer.RequestItemAuthority(receiver.Root)) return;
            float volume = Mathf.Min(source.Amount.Value, rate * dt);
            float before = source.Amount.Value, accepted = 0;
            if (apply && receiver != null)
            {
                accepted = Mathf.Min(volume, Mathf.Max(0, Factory.Capacity - receiver.Amount.Value));
                if (accepted > 0)
                {
                    source.Stew.Remove(accepted / before, receiver.Stew, null, null);
                    receiver.Content.Value = StewLogic.MergeContents(receiver.Content.Value, source.Content.Value);
                }
                receiver.Amount.Value += accepted;
            }
            if (apply && volume > accepted) source.Stew.Remove((volume - accepted) / (before - accepted), null, null, null);
            if (apply) source.Amount.Value -= volume;
            if (source.Stream == null)
            {
                source.Stream = new WaterPour(effects);
            }
            source.Stream.Draw(start, Vector3.Cross(tr.up, low).normalized, distance, source.Amount.Value, rate, source.Content.Value);
        }

        static void Trace(Pot pot, string action)
        {
            if (SurvivalModule.Verbose.Value) WildWasteland.Core.Log.Always("[Survival] StewPot " + action + ": " + pot.Root.name + " " + pot.Amount.Value.ToString("0.00", CultureInfo.InvariantCulture) + "/20 L");
        }
    }
}
