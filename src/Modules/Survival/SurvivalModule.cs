using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using ES3Internal;
using HutongGames.PlayMaker;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.Survival
{
    public sealed class SurvivalModule : ModuleBase
    {
        internal const string GUID = "com.denis.apocalypter.stewpot";
        internal static string Assets;
        internal static ConfigEntry<bool> Verbose, AllowStewPotSpilling;
        internal static ConfigEntry<float> Scale, NutritionMultiplier, CookingRate, InsanityBoilRate, SpoilingRate;
        private GameObject _effects;
        private Coroutine _sound;

        public override string Id { get { return "Survival"; } }
        public override string DisplayName { get { return "Survival"; } }
        public override string[] ConflictsWith { get { return new[] { GUID }; } }

        public override void BindConfig(ModuleConfig cfg)
        {
            // Snapshot the existing file before Bind saves newly introduced entries.
            Legacy.Get(cfg.Tools.ConfigFilePath, cfg.Section("Model"), "Scale");
            Scale = Bind(cfg, "Model", "Scale", .2f, "Pot model scale. Restart to apply.", .1f, .5f);
            Verbose = cfg.Tool("Debug", "VerboseLog", false, "Log Stew Pot filling, drinking, pouring, cooking and spoilage.");
            Migrate(Verbose, "Debug", "VerboseLog");
            AllowStewPotSpilling = cfg.Tool("Debug", "AllowStewPotSpilling", false, "Allow tilted stew pots to spill their contents, including pouring into another pot. Applies immediately.");
            NutritionMultiplier = Bind(cfg, "Stew Pot", "Nutrition conversion multiplier", 2f, "Cooked nutrition produced per raw nutrition point.", 1f, 10f);
            CookingRate = Bind(cfg, "Stew Pot", "Cooking rate", .25f, "Raw nutrition converted per unpaused second. Dog meat: 5 nutrition / 20 seconds.", .001f, 100f);
            InsanityBoilRate = Bind(cfg, "Stew Pot", "Insanity boilaway rate", .25f, "Insanity removed per unpaused second over a native Fire trigger.", 0f, 100f);
            SpoilingRate = Bind(cfg, "Stew Pot", "Spoiling rate", 15f / 5760f, "Spoiled nutrition accumulated per unpaused second.", 0f, 100f);
        }

        private ConfigEntry<float> Bind(ModuleConfig cfg, string section, string key, float value, string description, float min, float max)
        {
            var entry = cfg.Tool(section, key, value, description, new AcceptableValueRange<float>(min, max));
            Migrate(entry, section == "Model" ? "Model" : "Debug", key);
            return entry;
        }

        private static void Migrate(ConfigEntryBase entry, string section, string key)
        {
            if (Legacy.Get(entry.ConfigFile.ConfigFilePath, entry.Definition.Section, entry.Definition.Key) != null) return;
            string raw = Legacy.Get(GUID + ".cfg", section, key);
            if (raw == null) return;
            try { entry.SetSerializedValue(raw); Core.Log.Always("[Survival] migrated Stew Pot [" + section + "] " + key + " = " + raw); }
            catch (Exception e) { Core.Log.Warn("[Survival] could not migrate " + key + ": " + e.Message); }
        }

        public override void Init()
        {
            InstallAssets();
            Assets = Path.Combine(Dir, "Models");
            _effects = new GameObject("StewPot.Runner") { hideFlags = HideFlags.HideAndDontSave };
            _effects.transform.SetParent(Runner.Host.transform, false);
            Runtime.Init(_effects.transform);
            Runtime.NetworkInit();
            Multiplayer.PrefabRequested += MultiplayerPrefab;
            MultiplayerItems.Variables += MultiplayerVariables;
            Hooks.GetPrefab.Resolver(this, ResolvePrefab);
            Hooks.GetButtonDownUpdate.Prefix(this, Runtime.DrinkInput);
            Hooks.CreateObjectEnter.Prefix(this, NaturalLoot.BeforeCreate);
            Core.Patches.Patch(this, AccessTools.Method(typeof(ES3Prefab), "Awake"), postfix: AccessTools.Method(typeof(SurvivalModule), "PrefabAwake"));
            SpawnerBridge.Install(this);
            _sound = Scheduler.StartCoroutine(StewLogic.LoadSound());
            Scheduler.EveryFrame(this, "Survival.StewPot", Runtime.LateTick, Phase.LateUpdateLast);
            Scheduler.Interval(this, "Survival.References", 1f, Runtime.Resolve).RunNow();
            Scheduler.Sliced(this, "Survival.Existing pots", 500, Runtime.Discover);
            Core.Log.Always("[Survival] Stew Pot 0.1.1 integrated; native pot saves retained.");
        }

        private static ES3Prefab ResolvePrefab(long id)
        {
            return id == Factory.PrefabId ? Factory.Template().GetComponent<ES3Prefab>() : null;
        }

        static GameObject MultiplayerPrefab(string name) { return name == "StewPot" ? Factory.Template() : null; }
        static void MultiplayerVariables(GameObject go, List<NamedVariable> values)
        {
            if (go.transform.Find("StewPot.Visual") == null) return;
            var save = FsmUtil.Find(go, "saveItemVar"); if (save == null) return;
            foreach (var value in save.FsmVariables.IntVariables) MultiplayerItems.Add(values, value);
            foreach (var value in save.FsmVariables.FloatVariables) MultiplayerItems.Add(values, value);
        }

        private static void PrefabAwake(ES3Prefab __instance)
        {
            if (__instance.prefabId == Factory.PrefabId) Runtime.Register(__instance.gameObject);
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Runtime.Clear(); }
        public override void OnWorldReady() { Runtime.BeginDiscovery(); }
        public override void Shutdown()
        {
            Multiplayer.PrefabRequested -= MultiplayerPrefab; MultiplayerItems.Variables -= MultiplayerVariables;
            Runtime.NetworkStop();
            if (_sound != null) Scheduler.StopCoroutine(_sound);
            _sound = null;
            Runtime.Shutdown(); SpawnerBridge.Shutdown();
            if (_effects != null) UnityEngine.Object.Destroy(_effects);
            _effects = null;
        }

        public override IEnumerable<ToolPage> ToolPages()
        {
            return new ToolPage[] { new ConfigPage(this, ToolGroups.General, "Survival / Stew Pot", Config.Tools,
                "Survival / Model", "Survival / Stew Pot") { Intro = "Spawn through Apocaspawner: Camp & Farming > Stew Pot. Native Use drinks; Secondary use adds held meat. Model scale needs a restart." } };
        }

        private void InstallAssets()
        {
            const string prefix = "Survival.";
            var assembly = typeof(SurvivalModule).Assembly;
            foreach (string folder in new[] { "Models", "Sounds" })
            {
                string destination = Path.Combine(Dir, folder);
                Directory.CreateDirectory(destination);
                string legacy = Path.Combine(Core.Assets.LegacyPluginDir("StewPot"), folder);
                if (!Directory.Exists(legacy)) legacy = Path.Combine(BepInEx.Paths.BepInExRootPath,
                    "cache", "Apocasetter", "disabled", GUID, "plugins", "StewPot", folder);
                if (Directory.Exists(legacy)) foreach (string file in Directory.GetFiles(legacy))
                {
                    string target = Path.Combine(destination, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target);
                }
                string resourcePrefix = prefix + folder + ".";
                foreach (string resource in assembly.GetManifestResourceNames())
                {
                    if (!resource.StartsWith(resourcePrefix, StringComparison.Ordinal)) continue;
                    string target = Path.Combine(destination, resource.Substring(resourcePrefix.Length));
                    if (File.Exists(target)) continue;
                    using (var input = assembly.GetManifestResourceStream(resource))
                    using (var output = File.Create(target)) input.CopyTo(output);
                }
            }
        }
    }
}
