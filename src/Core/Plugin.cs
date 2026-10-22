using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WildWasteland
{
    /// <summary>
    /// Entry point (AGENTS.md 5.2): binds config, starts modules, applies the shared dispatchers and creates the Runner.
    /// The game destroys plugin GameObjects on scene load, so nothing runs on this component after Awake.
    /// The soft dependencies on the standalone mods only force them to load first, so the conflict check (5.8) sees them.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInIncompatibility(LegacyGUID)]
    [BepInDependency("ApocalypterMP", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocasetter", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocasaver", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocaplayer", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocaclimber", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.npcai", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.womenofwasteland", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.gunplay", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocaunloader", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.healthstacks", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.itweenguard", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.stewpot", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.gunplayhud", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.cleanhud", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("local.immersivemap", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocapocket", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocapatrol", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.warrig", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.merchantspotter", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocaraider", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.motorcyclemod", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("apocalypter.dogcompanion", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("local.apocalypter.duststorm", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.apocaspawner", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.denis.apocalypter.partadjustment", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.example.apocalypter.talk", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.burner.apocalypter.enginesim", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("apocalypter.worldamp", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.wildwastelandmp";
        public const string LegacyGUID = "com.denis.apocalypter.wildwasteland";
        public const string NAME = "Wild Wasteland MP";
        public const string VERSION = "0.1.55";

        public static Plugin Instance;
        public static ConfigFile Main, Settings, Tools;
        public static ConfigEntry<Key> ToolWindowKey;
        public static ConfigEntry<bool> EnableDebug, DeveloperMode;
        public static ConfigEntry<bool> VerboseLog;
        public static ConfigEntry<float> FrameBudgetMs;

        void Awake()
        {
            Instance = this;
            Core.Log.Source = Logger;
            try
            {
                Core.Assets.Init(Info.Location);
                Core.UI.Skin.Init(Info.Location);
                if (!File.Exists(Config.ConfigFilePath))
                {
                    var legacyConfig = Path.Combine(Paths.ConfigPath, LegacyGUID + ".cfg");
                    if (File.Exists(legacyConfig)) { File.Copy(legacyConfig, Config.ConfigFilePath); Config.Reload(); }
                }
                Core.Legacy.FirstRunMain = !File.Exists(Config.ConfigFilePath);
                var toolsPath = Path.Combine(Core.Assets.ConfigDir, "tools.cfg");
                Directory.CreateDirectory(Core.Assets.ConfigDir);
                Core.Legacy.FirstRunTools = !File.Exists(toolsPath);
                Main = Config;
                var settingsPath = Path.Combine(Core.Assets.ConfigDir, "settings.cfg");
                Core.Legacy.FirstRunMain = Core.Legacy.FirstRunMain && !File.Exists(settingsPath);
                if (!File.Exists(settingsPath) && File.Exists(Main.ConfigFilePath))
                    File.Copy(Main.ConfigFilePath, settingsPath);
                Settings = new ConfigFile(settingsPath, true);
                Tools = new ConfigFile(toolsPath, true);

                // Apocasetter hides its own opt-in entry; only these two settings are shown in F6.
                string oldKey = Core.Legacy.Get(Path.GetFileName(Main.ConfigFilePath), "Keys", "Tool window");
                bool hasWindowKey = Core.Legacy.Get(Path.GetFileName(Main.ConfigFilePath), "Keys", "In-Game Settings Button") != null;
                Main.Bind("General", "Apocasetter", true, "Lists Wild Wasteland in the Apocasetter Mods menu (F6).");
                ToolWindowKey = Main.Bind("Keys", "In-Game Settings Button", Key.F3,
                    "Opens Wild Wasteland settings: Game, Modules and, when enabled, Debug.");
                if (!hasWindowKey && oldKey != null) ToolWindowKey.SetSerializedValue(oldKey);
                EnableDebug = Main.Bind("General", "Enable Debug", false,
                    "Show Debug in Wild Wasteland settings: logging, switches, patrol editor access and the optional Developer menu.");

                RegisterModules();
                Core.ModuleHost.BindAll(Settings, Tools);

                VerboseLog = Settings.Bind("Debug", "VerboseLog", false, "Detailed info logging for every module (off for normal play).");
                DeveloperMode = Settings.Bind("Debug", "Developer mode", false, "Show Developer while Enable Debug is on. Contains advanced settings, editors and diagnostics.");
                FrameBudgetMs = Tools.Bind("Core", "Frame budget (ms)", 2f, new ConfigDescription("Sliced background work (bakes, scans) stops for the frame once Wild Wasteland used this much time.", new AcceptableValueRange<float>(0.5f, 8f)));
                Core.Log.Verbose = VerboseLog.Value;
                VerboseLog.SettingChanged += (s, a) => Core.Log.Verbose = VerboseLog.Value;
                Core.Scheduler.FrameBudgetMs = FrameBudgetMs.Value;
                FrameBudgetMs.SettingChanged += (s, a) => Core.Scheduler.FrameBudgetMs = FrameBudgetMs.Value;

                Core.Runner.Ensure();
                RegisterCoreJobs();
                Core.SaveTypes.Install();
                Core.ReplacementGuard.Prepare();
                Core.HealthStacks.Init();
                Core.ColorGrading.Init();
                Core.VehicleCollisionDamage.Install(Modules.Patrols.PatrolsModule.RamDamageInCar);
                Core.VehicleMeleeProtection.Install();
                Core.ITweenGuard.Init();
                Core.QuietFsm.Install();
                Core.VistasWorldGen.Init();
                Core.Multiplayer.Init();
                Core.OfflineSaveRuntime.Install();
                Core.HostSaveSystem.Install();
                Core.UI.ToolWindow.Init();
                SceneManager.sceneLoaded += OnSceneLoaded;

                // Reset audio before any module creates clips.
                var audioVoices = Modules.Patrols.PatrolsModule.AudioVoices;
                if (audioVoices != null) ApplyAudioVoices(audioVoices.Value);
                Core.ModuleHost.StartAll();
                if (Core.Multiplayer.Present) Core.MultiplayerLaunch.Install();
                Core.HostSaveSystem.RecoverFiles();
                Core.Log.Always(Core.ModuleHost.Summary());
            }
            catch (Exception e)
            {
                Core.Log.Error("Wild Wasteland failed to start", e);
            }
        }

        /// <summary>Module order = init order = config display order. Providers before consumers.</summary>
        static void RegisterModules()
        {
            Core.ModuleHost.Add(new Modules.PlayerBody.PlayerBodyModule());
            Core.ModuleHost.Add(new Modules.NPC.NpcModule());
            Core.ModuleHost.Add(new Modules.Guns.GunsModule());
            Core.ModuleHost.Add(new Modules.HUD.HudModule());
            Core.ModuleHost.Add(new Modules.CleanHUD.CleanHudModule());
            Core.ModuleHost.Add(new Modules.Patrols.PatrolsModule());
            Core.ModuleHost.Add(new Modules.POI.PoiModule());
            Core.ModuleHost.Add(new Modules.Weather.WeatherModule());
            Core.ModuleHost.Add(new Modules.Quests.QuestsModule());
            Core.ModuleHost.Add(new Modules.EngineSimCompat.EngineSimCompatModule());
            Core.ModuleHost.Add(new Modules.Survival.SurvivalModule());
            Core.ModuleHost.Add(new Modules.Performance.PerformanceModule());   // last: its PlayMaker hooks run after every gameplay module's
        }

        static void ApplyAudioVoices(int want)
        {
            try
            {
                var cfg = UnityEngine.AudioSettings.GetConfiguration();
                if (want <= 0 || cfg.numRealVoices == want) { Core.Log.Info("[Core] Audio: " + cfg.numRealVoices + " real voices (unchanged)"); return; }
                int before = cfg.numRealVoices;
                cfg.numRealVoices = want;
                if (cfg.numVirtualVoices < want) cfg.numVirtualVoices = want * 8;
                bool ok = UnityEngine.AudioSettings.Reset(cfg);
                Core.Log.Always("[Core] Audio: real voices " + before + " -> " + UnityEngine.AudioSettings.GetConfiguration().numRealVoices + (ok ? "" : " (Reset failed)"));
            }
            catch (Exception e) { Core.Log.Warn("[Core] Audio: could not set the voice count: " + e.Message); }
        }

        static void RegisterCoreJobs()
        {
            Core.Scheduler.EveryFrame(null, "Core.Frame", CoreFrame, Core.Phase.EarlyUpdate);
        }

        static void CoreFrame()
        {
            Core.Multiplayer.Tick();
            Core.UI.EditorSession.Tick();
            Core.SaveBus.Tick();
            Core.Hotkeys.Tick();
            Core.UI.ToolWindow.Tick();
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            Core.ReplacementGuard.SceneLoaded(s);
            Core.Runner.Ensure();
            Core.GameRefs.OnSceneLoaded(s, m);
            Core.Origin.Reset();
            Core.SaveBus.OnSceneLoaded();
            Core.UI.EditorSession.CancelAll();
            Core.Log.ResetRateLimits();
            Core.ModuleHost.SceneLoaded(s, m);
        }
    }
}
