using System;
using System.IO;
using BepInEx.Bootstrap;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.NPC
{
    internal sealed class WomenFeature : INpcAppearance
    {
        internal const string GUID = "com.denis.apocalypter.womenofwasteland";
        internal static bool Active;
        private static WomenFeature _api;
        internal static void InitializePresentation(NpcModule module)
        {
            BepInEx.PluginInfo standalone;
            if (Chainloader.PluginInfos.TryGetValue(GUID, out standalone) && !ReferenceEquals(standalone.Instance, null)) return;
            InstallAssets(module.Dir); Active = true; Gungirl.Initialize(false);
            Scheduler.Sliced(module, "Female MP presentation", 500, Gungirl.Step);
            Scheduler.Interval(module, "Female MP cache cleanup", 10f, Gungirl.Sweep);
        }

        internal static void Initialize(NpcModule module)
        {
            BepInEx.PluginInfo standalone;
            if (Chainloader.PluginInfos.TryGetValue(GUID, out standalone) && !ReferenceEquals(standalone.Instance, null))
            {
                Core.Log.Always("[NPC] Women of Wasteland: " + ReplacementGuard.Status(GUID) + ". Built-in female bodies wait until restart.");
                return;
            }
            InstallAssets(module.Dir);
            Active = true;
            Gungirl.Initialize();
            Voice.EnsureLoading();
            _api = new WomenFeature(); Services.NpcAppearance = _api;
            Hooks.CreateObjectEnter.Postfix(module, Gungirl.AfterCreateObject);
            Scheduler.Sliced(module, "Female restoration", 500, Gungirl.Step);
            Scheduler.Interval(module, "Female discovery", 5f, Gungirl.SafetyScan).RunNow();   // (WW perf) a full walk only when the world is ready, then every 60 s (Gungirl)
            Scheduler.Interval(module, "Female voices", .25f, Voice.Tick).RunNow();
            Scheduler.Interval(module, "Female cache cleanup", 10f, Gungirl.Sweep);
            Hotkeys.Register(module, NpcModule.FemaleSpawnKey, Gungirl.SpawnInFront, CanSpawn);
            Core.Log.Always("[NPC] Women of Wasteland 1.0.0 integrated; female crew conversion available.");
        }

        private static bool CanSpawn() { return Active && GameRefs.InGame; }

        public void Dress(GameObject root, string identity, bool randomize)
        {
            if (Active) Gungirl.DressCrew(root, identity, randomize);
        }

        internal static void Shutdown()
        {
            Active = false;
            if (ReferenceEquals(Services.NpcAppearance, _api)) Services.NpcAppearance = null;
            _api = null;
            Voice.Shutdown();
            Gungirl.Shutdown();
        }

        private static void InstallAssets(string directory)
        {
            string legacyRoot = Assets.LegacyPluginDir("WomenOfWasteland");
            foreach (string folder in new[] { "Models", "Sounds" })
            {
                string old = Path.Combine(legacyRoot, folder);
                if (!Directory.Exists(old)) continue;
                foreach (string source in Directory.GetFiles(old, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(directory, source.Substring(legacyRoot.Length + 1));
                    if (File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target);
                }
            }
            const string prefix = "NPC.WomenOfWasteland.";
            var assembly = typeof(WomenFeature).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string file = resource.Substring(prefix.Length), relative;
                if (file.StartsWith("Models.", StringComparison.Ordinal)) relative = Path.Combine("Models", file.Substring(7));
                else if (file.StartsWith("Sounds.Gungirl.", StringComparison.Ordinal)) relative = Path.Combine("Sounds/Gungirl", file.Substring(15));
                else continue;
                string target = Path.Combine(directory, relative);
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string legacy = Path.Combine(Assets.LegacyPluginDir("WomenOfWasteland"), relative);
                if (File.Exists(legacy)) File.Copy(legacy, target);
                else using (var input = assembly.GetManifestResourceStream(resource))
                    using (var output = File.Create(target)) input.CopyTo(output);
            }
        }
    }
}
