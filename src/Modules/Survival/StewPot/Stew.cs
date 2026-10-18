using System;
using System.Collections;
using System.Globalization;
using System.IO;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using InsaneSystems.InputManager;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace WildWasteland.Modules.Survival
{
    internal sealed class Stew
    {
        internal readonly FsmFloat Raw, Cooked, Insanity, SpoiledNutrition, StaleSeconds;
        // 0=fresh, 1=stale warning, 2=spoiled penalty already applied.
        internal readonly FsmInt Spoiled;
        internal bool Heating;
        internal AudioSource Boiling;
        internal string Details = "";
        float nextCook, lastCook, nextSpoil, coldSeconds, nextDetails;

        internal float Nutrition { get { return Raw.Value + Cooked.Value; } }

        internal Stew(PlayMakerFSM save)
        {
            var vars = save.FsmVariables;
            Raw = vars.GetFsmFloat("RawNutrition"); Cooked = vars.GetFsmFloat("CookedNutrition");
            Insanity = vars.GetFsmFloat("StewInsanity"); SpoiledNutrition = vars.GetFsmFloat("SpoiledNutrition");
            StaleSeconds = vars.GetFsmFloat("StaleSeconds"); Spoiled = vars.GetFsmInt("StewSpoiled");
            lastCook = StewLogic.Clock;
            nextCook = lastCook + 1f + (save.GetInstanceID() & 255) / 255f;
            nextSpoil = lastCook + 5f;
        }

        internal void Tick(Runtime.Pot pot, float dt, bool live, Transform owner)
        {
            bool authority = WildWasteland.Core.Multiplayer.ItemAuthority(pot.Root);
            if (!authority) { lastCook = StewLogic.Clock; nextCook = lastCook + 2f; nextSpoil = lastCook + 5f; coldSeconds = 0; }
            if (live && authority)
            {
                if (!Heating) coldSeconds += dt;
                if (StewLogic.Clock >= nextCook)
                {
                    float elapsed = StewLogic.Clock - lastCook;
                    lastCook = StewLogic.Clock; nextCook = lastCook + 2f;
                    bool heat = pot.Root.activeInHierarchy && pot.Amount.Value > .0001f && pot.Pose < .001f
                        && Vector3.Dot(pot.Root.transform.up, Vector3.up) > .5f && StewLogic.OnFire(pot.Collider);
                    if (heat && !Heating)
                    {
                        SpoiledNutrition.Value = StaleSeconds.Value = 0; Spoiled.Value = 0; coldSeconds = 0;
                        StewLogic.Trace(pot, "boiling started; spoilage reset");
                    }
                    if (Heating && !heat) StewLogic.Trace(pot, "boiling stopped");
                    Heating = heat;
                    if (heat)
                    {
                        float converted = Mathf.Min(Raw.Value, SurvivalModule.CookingRate.Value * elapsed);
                        Raw.Value -= converted; Cooked.Value += converted * SurvivalModule.NutritionMultiplier.Value;
                        Insanity.Value = Mathf.Max(0, Insanity.Value - SurvivalModule.InsanityBoilRate.Value * elapsed);
                        coldSeconds = 0;
                    }
                    nextDetails = 0;
                }
                if (StewLogic.Clock >= nextSpoil)
                {
                    nextSpoil = StewLogic.Clock + 5f;
                    if (!Heating && Nutrition > .0001f && Spoiled.Value != 2)
                    {
                        SpoiledNutrition.Value += SurvivalModule.SpoilingRate.Value * coldSeconds;
                        if (SpoiledNutrition.Value > Nutrition)
                        {
                            if (Spoiled.Value == 1) StaleSeconds.Value += coldSeconds;
                            else { Spoiled.Value = 1; StaleSeconds.Value = 0; StewLogic.Trace(pot, "stale"); }
                            if (StaleSeconds.Value >= 300f)
                            {
                                float lost = Nutrition * .75f;
                                Raw.Value *= .25f; Cooked.Value *= .25f; Insanity.Value += lost;
                                Spoiled.Value = 2; StewLogic.Trace(pot, "spoiled; nutrition quartered");
                            }
                        }
                        else { Spoiled.Value = 0; StaleSeconds.Value = 0; }
                    }
                    coldSeconds = 0; nextDetails = 0;
                }
            }
            if (Boiling == null && live && Heating && StewLogic.BoilingClip != null)
            {
                var go = new GameObject("StewPot.Boiling"); go.transform.SetParent(owner, false);
                Boiling = go.AddComponent<AudioSource>(); Boiling.clip = StewLogic.BoilingClip;
                Boiling.loop = true; Boiling.playOnAwake = false; Boiling.spatialBlend = 1;
                Boiling.volume = .6f; Boiling.minDistance = .8f; Boiling.maxDistance = 15f;
            }
            if (Boiling != null)
            {
                Boiling.transform.position = pot.Root.transform.position;
                bool audible = live && Heating && pot.Root.activeInHierarchy && pot.Amount.Value > .0001f;
                if (audible && !Boiling.isPlaying) Boiling.Play();
                else if (!audible && Boiling.isPlaying) Boiling.Stop();
            }
            if (StewLogic.Clock >= nextDetails)
            {
                nextDetails = StewLogic.Clock + .5f;
                string status = Spoiled.Value == 2 ? "Spoiled" : Spoiled.Value == 1 ? "Stale" : Raw.Value > .0001f ? "Raw" : "Cooked";
                Details = Nutrition > .0001f || Insanity.Value > .0001f
                    ? status + (Heating ? " - Boiling" : "") + "\nNutrition: " + Nutrition.ToString("0.0", CultureInfo.InvariantCulture)
                        + " (raw: " + Raw.Value.ToString("0.0", CultureInfo.InvariantCulture) + ")\nInsanity: +" + Insanity.Value.ToString("0.0", CultureInfo.InvariantCulture)
                    : Heating ? "Boiling water" : "";
            }
        }

        // Call before reducing donor litres so every dissolved value follows exactly the same fraction.
        internal void Remove(float fraction, Stew destination, FsmFloat hunger, FsmFloat sanity)
        {
            fraction = Mathf.Clamp01(fraction);
            float raw = Raw.Value * fraction, cooked = Cooked.Value * fraction, insanity = Insanity.Value * fraction;
            float spoiled = SpoiledNutrition.Value * fraction;
            Raw.Value -= raw; Cooked.Value -= cooked; Insanity.Value -= insanity; SpoiledNutrition.Value -= spoiled;
            if (destination != null)
            {
                destination.Raw.Value += raw; destination.Cooked.Value += cooked;
                destination.Insanity.Value += insanity; destination.SpoiledNutrition.Value += spoiled;
                if (raw + cooked > .0001f)
                {
                    destination.Spoiled.Value = Mathf.Max(destination.Spoiled.Value, Spoiled.Value);
                    destination.StaleSeconds.Value = Mathf.Max(destination.StaleSeconds.Value, StaleSeconds.Value);
                }
                destination.nextDetails = 0;
            }
            if (hunger != null) hunger.Value = Mathf.Max(0, hunger.Value - raw - cooked);
            if (sanity != null) sanity.Value = Mathf.Clamp(sanity.Value + insanity, 0, 100);
        }

        internal void Empty()
        {
            Raw.Value = Cooked.Value = Insanity.Value = SpoiledNutrition.Value = StaleSeconds.Value = 0;
            Spoiled.Value = 0; Heating = false; coldSeconds = 0; nextDetails = 0;
        }

        internal int MaterialKey(int type)
        {
            if (type == (int)Contents.Water) return type;
            return type + (Spoiled.Value == 2 ? 18 : Spoiled.Value == 1 ? 12 : Raw.Value > .0001f ? 6 : 0);
        }

        internal Material Material(int type)
        {
            if (type == (int)Contents.Water) return Factory.Liquids[type];
            return Spoiled.Value == 2 ? Factory.SpoiledLiquids[type] : Spoiled.Value == 1 ? Factory.StaleLiquids[type]
                : Raw.Value > .0001f ? Factory.RawLiquids[type] : Factory.Liquids[type];
        }

        internal void Dispose() { if (Boiling != null) Object.Destroy(Boiling.gameObject); }
    }

    internal static class StewLogic
    {
        internal static float Clock;
        internal static AudioClip BoilingClip;
        internal static Runtime.Pot Target;
        internal static string AddHint;
        static readonly Collider[] fireHits = new Collider[64];
        static readonly RaycastHit[] aimHits = new RaycastHit[64];
        static GameObject heldFood;
        static float foodNutrition, foodInsanity, nextAim, noticeUntil;
        static bool foodRaw, foodValid;
        static string notice;
        static Runtime.Pot noticePot;

        internal static void Clear()
        {
            heldFood = null; foodValid = false; Target = noticePot = null; AddHint = notice = null; nextAim = noticeUntil = 0;
        }

        internal static bool OnFire(Collider collider)
        {
            if (collider == null || !collider.enabled) return false;
            var bounds = collider.bounds;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * .005f,
                fireHits, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var fire = fireHits[i];
                if (fire == null || !fire.isTrigger || !fire.CompareTag("Fire")) continue;
                Vector3 direction; float distance;
                if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                    fire, fire.transform.position, fire.transform.rotation, out direction, out distance)) return true;
            }
            return false;
        }

        internal static void Interact(GameObject held, Camera camera, bool input, PlayMakerFSM grab)
        {
            if (held != heldFood)
            {
                heldFood = held; foodValid = ReadFood(held); nextAim = 0;
                AddHint = "Add meat: " + InputController.GetKeyAction("Use_2").KeyName;
            }
            if (!input || !foodValid || held == null || camera == null) { Target = null; return; }
            bool add = InputController.GetKeyActionIsDown("Use_2");
            if (Clock >= nextAim || add)
            {
                nextAim = Clock + .1f; Target = null;
                Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                int count = Physics.RaycastNonAlloc(ray, aimHits, 2f, ~0, QueryTriggerInteraction.Ignore);
                float closest = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    var hit = aimHits[i];
                    if (hit.collider.transform.IsChildOf(held.transform) || hit.distance >= closest) continue;
                    closest = hit.distance; Target = Runtime.FindPot(hit.collider.attachedRigidbody != null
                        ? hit.collider.attachedRigidbody.gameObject : hit.collider.gameObject);
                }
            }
            if (!add || Target == null) return;
            if (WildWasteland.Core.Multiplayer.Active) { Runtime.NetworkAdd(held, Target); return; }
            if (!WildWasteland.Core.Multiplayer.RequestItemAuthority(Target.Root)) return;
            var stew = Target.Stew;
            if (stew.Nutrition + foodNutrition > Target.Amount.Value * 25f + .00001f)
            {
                noticePot = Target; notice = "add more water"; noticeUntil = Clock + 3f;
                Trace(Target, "ingredient rejected: add more water"); return;
            }
            if (foodRaw) stew.Raw.Value += foodNutrition; else stew.Cooked.Value += foodNutrition;
            stew.Insanity.Value += foodInsanity;
            Target.Content.Value = MergeContents(Target.Content.Value, (int)Contents.Meat);
            Trace(Target, "meat added: nutrition +" + foodNutrition.ToString("0.0", CultureInfo.InvariantCulture)
                + ", insanity +" + foodInsanity.ToString("0.0", CultureInfo.InvariantCulture));
            noticePot = Target; notice = "Meat added"; noticeUntil = Clock + 2f;
            var itemName = Factory.Fsm(held, "ItemName"); if (itemName != null) itemName.SendEvent("OffItem");
            if (grab != null) grab.SendEvent("not_Hold");
            Object.Destroy(held); foodValid = false;
        }

        static bool ReadFood(GameObject go)
        { return ReadIngredient(go, out foodNutrition, out foodInsanity, out foodRaw); }
        internal static bool ReadIngredient(GameObject go, out float foodNutrition, out float foodInsanity, out bool foodRaw)
        {
            foodNutrition = foodInsanity = 0; foodRaw = false;
            if (go == null || go.name.IndexOf("rotten", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            var id = Factory.Fsm(go, "ID"); var food = Factory.Fsm(go, "Food");
            if (id == null || food == null) return false;
            string category = id.FsmVariables.GetFsmString("ID").Value;
            if (category != "meat" && category != "meat_lizard") return false;
            var cook = Factory.Fsm(go, "Cook"); foodRaw = cook != null;
            foreach (var state in food.FsmStates)
                foreach (var action in state.Actions)
                {
                    if (!action.Enabled) continue;
                    var add = action as FloatAdd;
                    if (add != null && add.floatVariable.Name == "Hunger" && add.add.Value < 0) foodNutrition -= add.add.Value;
                    var insanity = action as FsmFloatAdd;
                    if (insanity != null && insanity.fsmName.Value == "SanityAdd" && insanity.variableName.Value == "SanityAdd")
                        foodInsanity += Mathf.Max(0, insanity.add.Value * 5f);
                }
            return foodNutrition > 0;
        }

        internal static string Hint(Runtime.Pot pot)
        {
            if (noticePot == pot && Clock < noticeUntil) return notice;
            if (Target == pot && foodValid) return AddHint;
            return pot.Amount.Value > .0001f ? "Drink: hold Use" : "Empty - pour water into the pot";
        }

        internal static int MergeContents(int current, int added)
        {
            if (added <= (int)Contents.Water) return current;
            return current <= (int)Contents.Water || current == added ? added : (int)Contents.Mixed;
        }

        internal static IEnumerator LoadSound()
        {
            if (BoilingClip != null) yield break;
            string folder = Path.Combine(Path.GetDirectoryName(SurvivalModule.Assets), "Sounds");
            string path = Path.Combine(folder, "boiling_water.mp3");
            if (!File.Exists(path)) path = Path.Combine(folder, "boilng_water.mp3");
            if (!File.Exists(path)) { WildWasteland.Core.Log.Warn("[Survival] StewPot boiling sound missing: " + path); yield break; }
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { WildWasteland.Core.Log.Warn("[Survival] StewPot boiling sound: " + request.error); yield break; }
                BoilingClip = DownloadHandlerAudioClip.GetContent(request);
                if (BoilingClip != null) { Object.DontDestroyOnLoad(BoilingClip); WildWasteland.Core.Log.Always("[Survival] StewPot boiling sound loaded: " + Path.GetFileName(path)); }
            }
        }

        internal static void Trace(Runtime.Pot pot, string text)
        {
            if (SurvivalModule.Verbose.Value) WildWasteland.Core.Log.Always("[Survival] StewPot " + text + ": " + pot.Root.name);
        }
    }
}
