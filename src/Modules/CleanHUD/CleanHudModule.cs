using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.CleanHUD
{
    public sealed class CleanHudModule : ModuleBase, IVehicleStatusDisplay, ICompassMarkers, IPlayerCompassMarkers
    {
        internal const string GUID = "com.denis.apocalypter.cleanhud";
        internal static ConfigEntry<bool> Enabled, ShowNeeds, ShowHealthNumbers, ShowInsanity, MenuEnabled, CleanFont, EnableCheats;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<bool> ShowConvoys, ShowLocations, ShowPlayers, ShowPlayersOnMap;
        internal static ConfigEntry<bool> AmbiguousPatrols, AmbiguousLocations;
        internal static ConfigEntry<bool> ShowCompass, MapEnabled, DrivingHudOnlyThirdPerson;
        internal static ConfigEntry<MapDetail> MapMarkers, MapRoutes, MapTrafficMarkers;
        internal static ConfigEntry<bool> FogOfWar, SharedVision;
        internal static ConfigEntry<float> FogRadius, FogEdge, FogOpacity, BinocularZoom;
        internal static LayoutFile Layout;
        private Hud _hud;
        private Menu _menu;
        private Coroutine _updateCheck;
        private LoadingScreen _loadingScreen;
        private QuestMap _questMap;
        private float _retry;
        private Job _attach;
        internal static bool Running;
        private static readonly Dictionary<string, float> _throttle = new Dictionary<string, float>();

        public override string Id { get { return "CleanHUD"; } }
        public override string DisplayName { get { return "Clean HUD & menus"; } }
        public override string[] ConflictsWith { get { return new[] { GUID }; } }

        public override void BindConfig(ModuleConfig cfg)
        {
            Enabled = Bind(cfg, "HUD", "Enabled", true, "Replace the native HUD. Off restores it immediately.", "HUD", "General");
            ShowNeeds = Bind(cfg, "HUD", "ShowNeeds", true, "Show water, food and sleep beneath HP and armor.");
            ShowHealthNumbers = Bind(cfg, "HUD", "ShowHealthNumbers", true, "Show HP and armor values inside their bars.");
            ShowInsanity = Bind(cfg, "HUD", "ShowInsanity", true, "Show insanity in the top right while above zero.");
            ShowConvoys = cfg.Player("HUD", "ShowConvoys", true, "Compass icons and distances for vehicle groups: merchants yellow, Coyotes green, patrols red and convoys orange.");
            ShowLocations = cfg.Player("HUD", "ShowLocations", true, "Compass icons and distances for native locations within 1 km.");
            ShowPlayers = cfg.Player("HUD", "ShowPlayers", true, "Show multiplayer teammates, their names and distances on the Wild Wasteland compass. This is your own display preference.");
            ShowPlayersOnMap = cfg.Player("HUD", "ShowPlayersOnMap", true, "Show colored direction arrows for all multiplayer players, including you, on the map. This is your own HUD preference.");
            AmbiguousPatrols = cfg.Player("HUD", "AmbiguousPatrols", true, "Show all compass vehicle groups with white vehicle icons, keeping their distances.");
            AmbiguousLocations = cfg.Player("HUD", "AmbiguousLocations", true, "Show undiscovered compass locations as neutral question marks. Approaching within 100 m or spotting through binoculars remembers their real icons and colors. Quest flags are unchanged.");
            ShowCompass = cfg.Player("HUD", "Compass", true, "Show the compass, including its direction strip and all markers.");
            DrivingHudOnlyThirdPerson = cfg.Player("HUD", "DrivingHudOnlyThirdPerson", true, "Show the gear/RPM/speed HUD only while driving in third person. Off allows either camera view; the source HUD enable switch still applies.");
            MapEnabled = cfg.Player("Map", "Enabled", true, "Enable the native debug map on the game's Map key.");
            MapMarkers = cfg.Player("Map", "Markers", MapDetail.Ambiguous, "Off hides map markers; Ambiguous shows question marks until locations are discovered within 100 m or spotted through binoculars; On shows their icons and colors. Discoveries are saved. Map icons have no name labels.");
            bool savedTrafficMode = Legacy.Get(cfg.Main.ConfigFilePath, cfg.Section("Map"), "TrafficMarkersMode") != null;
            MapTrafficMarkers = cfg.Player("Map", "TrafficMarkersMode", MapDetail.Ambiguous, "World traffic markers: moving dots and names of patrols, convoys, merchants and foot travelers. Off hides them; Ambiguous shows gray Unknown; On shows their colors and names. Independent of location markers; fog of war still applies.");
            if (!savedTrafficMode)
            {
                string traffic = Legacy.Get(cfg.Main.ConfigFilePath, cfg.Section("Map"), "TrafficMarkers");
                if (traffic == "false") MapTrafficMarkers.Value = MapDetail.Off;
                else if (traffic == "true") MapTrafficMarkers.Value = MapMarkers.Value == MapDetail.Ambiguous ? MapDetail.Ambiguous : MapDetail.On;
            }
            bool savedRoutes = Legacy.Get(cfg.Main.ConfigFilePath, cfg.Section("Map"), "Routes") != null;
            MapRoutes = cfg.Player("Map", "Routes", MapDetail.Off, "Route lines of world traffic only (the moving markers are World traffic markers). Off hides them; Ambiguous draws them gray; On in faction colors.");
            if (!savedRoutes)
            {
                string section = "Patrols / Traffic tuning";
                if (Legacy.Get(cfg.Tools.ConfigFilePath, section, "DebugMap") == "false") MapRoutes.Value = MapDetail.Off;
                else if (Legacy.Get(cfg.Tools.ConfigFilePath, section, "hide route type") == "true") MapRoutes.Value = MapDetail.Ambiguous;
            }
            FogOfWar = cfg.Player("Map", "FogOfWar", true, "Hide the parts of the map you have not explored yet. Exploring is recorded while this is off.");
            cfg.Migrate(FogOfWar, "local.immersivemap.cfg", "Fog", "Enabled");
            SharedVision = cfg.Player("Map", "SharedVision", false, "Share explored map areas with everyone in the multiplayer world. Off restores each player's personal exploration, which keeps recording while sharing is on. Controlled by the host.");
            FogRadius = cfg.Player("Map", "FogRevealRadius", 500f, "Base range in meters. Exploration reveals twice this radius; high ground extends it up to threefold (4 km radial cap), with raised binoculars doubling the viewed cone again.", new AcceptableValueRange<float>(50f, 2000f));
            cfg.Migrate(FogRadius, "local.immersivemap.cfg", "Fog", "RevealRadius");
            FogEdge = cfg.Player("Map", "FogEdgeFade", 200f, "Width in meters of the soft edge, measured inward from the reveal radius. 0 = hard edge.", new AcceptableValueRange<float>(0f, 1000f));
            cfg.Migrate(FogEdge, "local.immersivemap.cfg", "Fog", "EdgeFadeDistance");
            FogOpacity = cfg.Player("Map", "FogOpacity", 1f, "1 hides unexplored areas completely; lower values leave them faintly visible.", new AcceptableValueRange<float>(0f, 1f));
            cfg.Migrate(FogOpacity, "local.immersivemap.cfg", "Fog", "FogOpacity");
            BinocularZoom = cfg.Player("Map", "BinocularZoom", 1f, "Remembered additional binocular magnification. Scroll while binoculars are raised to change it from 1x to 2x.", new AcceptableValueRange<float>(1f, 2f));
            MenuEnabled = Bind(cfg, "Menu", "Enabled", true, "Replace the title and pause menus with CleanHUD's menus.");
            CleanFont = cfg.Player("Menu", "CleanFont", false, "Use CleanHUD's forcedSquare font instead of the game's Helveticrap font. Applies to CleanHUD and its settings text.");
            CleanFont.SettingChanged += (sender, args) => GlyphText.RefreshFont();
            AccentTheme.Setting = cfg.Player("Menu", "Accent", "Yellow", "CleanHUD text and border accent; also selectable at the bottom of the title and pause menus.", new AcceptableValueList<string>(AccentTheme.Names));
            EnableCheats = Bind(cfg, "Menu", "EnableCheats", false, "Add the game's cheat controls to the menu settings.");
            bool saved = Legacy.Get(cfg.Main.ConfigFilePath, cfg.Section("HUD"), "Scale") != null;
            Scale = cfg.Player("HUD", "Scale", 1f, "HUD scale; each panel grows away from its screen edge.", new AcceptableValueRange<float>(0.5f, 1.5f));
            cfg.Migrate(Scale, GUID + ".cfg", "HUD", "Scale");
            if (!saved) Import(Scale, "HUD", "Scale");
            Enabled.SettingChanged += HudChanged;
            MenuEnabled.SettingChanged += MenuChanged;
            MapEnabled.SettingChanged += MapChanged;
        }

        // Newly absorbed settings migrate even when Wild Wasteland was already installed.
        private static ConfigEntry<bool> Bind(ModuleConfig cfg, string section, string oldKey, bool def, string desc, string key = null, string oldSection = null)
        {
            string currentKey = key ?? oldKey;
            bool saved = Legacy.Get(cfg.Main.ConfigFilePath, cfg.Section(section), currentKey) != null;
            var entry = cfg.Player(section, currentKey, def, desc);
            cfg.Migrate(entry, GUID + ".cfg", oldSection ?? section, oldKey);
            if (!saved) Import(entry, oldSection ?? section, oldKey);
            return entry;
        }

        private static void Import(ConfigEntryBase entry, string section, string key)
        {
            string raw = Legacy.Get(GUID + ".cfg", section, key);
            if (raw != null) try { entry.SetSerializedValue(raw); } catch (Exception e) { Warn("Could not migrate " + key + ": " + e.Message); }
        }

        public override void Init()
        {
            Running = true; _retry = 0; _throttle.Clear();
            AccentTheme.Init();
            ChaseCameraHud.Clear(); ChaseCameraHud.Install(this);
            ApocapocketInventory.Install(this);
            MessageHook.Clear(); Bindings.Clear(); Menu.BlockCancelFrame = -1;
            var dir = Core.Assets.ModuleConfigDir(Id);
            foreach (string name in new[] { "layout.json", "game_keys.txt" })
            {
                string dest = Path.Combine(dir, name), old = Path.Combine(Path.Combine(Paths.ConfigPath, "CleanHUD"), name);
                if (!File.Exists(dest) && File.Exists(old)) File.Copy(old, dest);
            }
            Layout = new LayoutFile(Path.Combine(dir, "layout.json"));
            Layout.Poll(true);
            Assets.Preload();
            SceneCache.Reset();
            Binoculars.Init(this);
            VistasBanners.Init(this);
            VistasEngineHud.Install(this);
            Scheduler.Interval(this, "Vantage terrain", .2f, VantageDiscovery.Tick);
            LocationMarkers.Init();
            MapFog.Init();
            MultiplayerWorld.Ready += PersonalWorldReady; MultiplayerWorld.Leaving += SavePersonalWorld;
            MultiplayerItems.BeforeFlush += SavePersonalWorld;
            MultiplayerNotifications.Present += MessageHook.MultiplayerNotice;
            Scheduler.Interval(this, "MP exploration persistence", 30f, SavePersonalWorld);
            _loadingScreen = new LoadingScreen();
            ApocasaverLoading.Install(this, _loadingScreen);
            _menu = new Menu(_loadingScreen);
            _updateCheck = Scheduler.StartCoroutine(Menu.CheckUpdates());
            SaveBus.StateChanged += _loadingScreen.StateChanged;
            Services.VehicleStatus = this;
            Services.Compass = this;
            _questMap = new QuestMap(); Services.Map = _questMap;
            TalkDialogue.Install(this);
            QuestNotifications.Install(this);
            QuestTracker.Install(this);
            QuestJournal.Install(this);
            Core.Patches.Patch(this, AccessTools.Method(typeof(UiTextSetText), "OnEnter"), postfix: AccessTools.Method(typeof(MessageHook), "Postfix"));
            Hooks.GetButtonDownUpdate.Prefix(this, CancelHook.Prefix);
            Hooks.EnterState.Postfix(this, PauseCursorFix.AfterEnterState);
            Hooks.EnterState.Postfix(this, _loadingScreen.AfterEnterState);
            Scheduler.Sliced(this, "Scene key discovery", 500, SceneCache.Step);
            Scheduler.Sliced(this, "Weapon icons", 500, Assets.RenderStep);
            Scheduler.Sliced(this, "Accent panels", 500, AccentTheme.Step);
            Scheduler.Interval(this, "Layout reload", 2f, PollLayout);
            Scheduler.EveryFrame(this, "Inventory window", InventoryStyle.Tick, Phase.LateUpdateLast);
            Scheduler.Interval(this, "Location markers", .5f, LocationMarkers.Tick);
            Scheduler.Interval(this, "Quest map lookup", 1f, _questMap.FindMap);
            Scheduler.EveryFrame(this, "Quest map markers", _questMap.Tick, Phase.LateUpdateLast);
            Scheduler.EveryFrame(this, "Weather map areas", TickWeatherMap, Phase.LateUpdateLast);
            Scheduler.Gui(this, "Player map marker", _questMap.DrawPlayer, _questMap.PlayerShown);
            Scheduler.Interval(this, "Map fog exploration", .2f, MapFog.Paint);
            Scheduler.Sliced(this, "Map fog painting", 500, MapFog.PaintStep);
            Scheduler.EveryFrame(this, "Dialogue window", TalkDialogue.Tick, Phase.LateUpdateLast);
            Scheduler.EveryFrame(this, "Quest journal", QuestJournal.Tick, Phase.LateUpdateLast);
            Scheduler.EveryFrame(this, "Quest dialogue", QuestDialogue.Tick, Phase.LateUpdateLast);
            _attach = Scheduler.Interval(this, "HUD attachment", 1f, AttachHud, Phase.LateUpdateLast);
            _attach.RunNow();
            Scheduler.EveryFrame(this, "HUD", TickHud, Phase.LateUpdateLast);
            Scheduler.EveryFrame(this, "Menus", _menu.Tick, Phase.LateUpdateLast);
            Scheduler.EveryFrame(this, "Loading screen", _loadingScreen.Tick, Phase.LateUpdateLast);
            Core.Log.Always("[CleanHUD] CleanHUD 1.5.1 integrated; layout: " + Layout.Path);
        }

        private readonly WeatherMap _weatherMap = new WeatherMap();
        private void TickWeatherMap() { _weatherMap.Tick(_questMap); }
        private void PollLayout() { Layout.Poll(false); }
        private void MapChanged(object sender, EventArgs args) { if (Running && _questMap != null) _questMap.ApplyEnabled(); }
        private void AttachHud()
        {
            ChaseCameraHud.Install(this);
            if (!KeyCatalog.ScanStarted) KeyCatalog.StartScan();
            if (!Enabled.Value || Time.unscaledTime < _retry) return;
            if (_hud != null && !_hud.Valid) Release();
            if (_hud == null) _hud = Hud.TryCreate(Layout);
        }
        private void TickHud()
        {
            if (!Enabled.Value) return;
            if (_hud != null && !_hud.Valid) Release();
            if (_hud == null) return;
            try { _hud.Tick(); }
            catch { Release(); _retry = Time.unscaledTime + 5f; throw; }
        }
        private void Release() { if (_hud != null) { _hud.Dispose(); _hud = null; } }
        public bool TryShow(bool ignitionOff, bool handbrakeOn, bool playing, string volume)
        {
            if (!Running || !Enabled.Value || _hud == null || !_hud.Valid) return false;
            _hud.SetVehicleStatus(ignitionOff, handbrakeOn, playing, volume);
            return true;
        }
        bool ICompassMarkers.VehiclesEnabled { get { return Running && Enabled.Value && ShowCompass.Value && ShowConvoys.Value; } }
        int ICompassMarkers.Add(string owner, Func<Vector3> position, string icon, Color color)
        { return Api.Compass.Add(owner, position, new Api.Marker { Icon = icon, Color = color, ShowDistance = true, MaxDistance = owner == "WildWasteland.VehicleGroups" ? 1000f : 0f }); }
        bool ICompassMarkers.Exists(int id) { return Api.Compass.Exists(id); }
        bool IPlayerCompassMarkers.PlayersEnabled { get { return ShowPlayers.Value; } }
        int IPlayerCompassMarkers.AddPlayer(string owner, Func<Vector3> position, string name, Color color)
        { return Api.Compass.Add(owner, position, new Api.Marker { Icon = "player", Label = name, Color = color, ShowDistance = true, PinToEdge = true, Priority = 100 }); }
        bool ICompassMarkers.Remove(int id) { return Api.Compass.Remove(id); }
        void ICompassMarkers.Identify(int id) { foreach (var entry in Api.Compass.Entries) if (entry.Id == id) { entry.Identified = true; break; } }
        void ICompassMarkers.RemoveAll(string owner) { Api.Compass.RemoveAll(owner); }
        private void HudChanged(object sender, EventArgs args)
        {
            if (!Running) return;
            if (!Enabled.Value) Release(); else if (_attach != null) _attach.RunNow();
            if (_questMap != null) _questMap.FindMap();
        }
        private void MenuChanged(object sender, EventArgs args)
        {
            if (!Running || MenuEnabled.Value) return;
            if (_menu != null) _menu.Teardown();
            if (_loadingScreen != null) _loadingScreen.End();
        }
        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _weatherMap.Clear();
            Binoculars.Reset();
            VantageDiscovery.Reset(); VistasBanners.Reset();
            InventoryStyle.Restore();
            LocationMarkers.Begin(false);
            if (_questMap != null) _questMap.SceneLoaded(); TalkDialogue.Hide(); QuestJournal.Hide(); QuestDialogue.Hide();
            Release(); _menu.Teardown(); SceneCache.Reset(); MessageHook.Clear(); Bindings.Clear(); Assets.SceneLoaded();
            _loadingScreen.SceneLoaded();
            _retry = 0; _attach.RunNow();
        }
        public override void OnLoadBegin(string slot) { VantageDiscovery.Reset(); VistasBanners.Reset(); InventoryStyle.Restore(); LocationMarkers.Begin(false); MapFog.Reset(); QuestJournal.Hide(); _loadingScreen.Begin(); Release(); Bindings.Clear(); MessageHook.Clear(); Assets.SceneLoaded(); }
        public override void OnSaveEnd(string slot) { LocationMarkers.SaveEnded(slot); }
        public override void OnOriginShift(Vector3 delta) { MapFog.OriginShifted(); VantageDiscovery.OriginShifted(delta); }
        void PersonalWorldReady() { LocationMarkers.WorldReady(MultiplayerWorld.PersonalSlot); MapFog.WorldReady(MultiplayerWorld.PersonalSlot); }
        void SavePersonalWorld() { string slot = MultiplayerWorld.GuestSlot; if (slot != null) { LocationMarkers.SaveEnded(slot); MapFog.SaveEnded(slot); } }
        public override void OnWorldReady() { PersonalWorldReady(); SceneCache.Reset(); SceneCache.BeginKeys(); _attach.RunNow(); }
        public override void Shutdown()
        {
            SavePersonalWorld(); MultiplayerWorld.Ready -= PersonalWorldReady; MultiplayerWorld.Leaving -= SavePersonalWorld;
            MultiplayerItems.BeforeFlush -= SavePersonalWorld;
            MultiplayerNotifications.Present -= MessageHook.MultiplayerNotice;
            _weatherMap.Clear();
            Binoculars.Shutdown();
            VantageDiscovery.Reset(); VistasBanners.Shutdown();
            VistasEngineHud.Clear();
            InventoryStyle.Restore();
            Running = false; Release();
            Scheduler.StopCoroutine(_updateCheck); _updateCheck = null;
            ChaseCameraHud.Clear();
            TalkDialogue.Dispose();
            QuestTracker.Clear();
            QuestJournal.Clear(); QuestDialogue.Dispose(); QuestNotifications.Clear();
            if (ReferenceEquals(Services.Map, _questMap)) Services.Map = null;
            if (_questMap != null) _questMap.Dispose(); _questMap = null;
            if (ReferenceEquals(Services.VehicleStatus, this)) Services.VehicleStatus = null;
            if (ReferenceEquals(Services.Compass, this)) Services.Compass = null;
            LocationMarkers.Shutdown();
            MapFrame.Restore(); MapFog.Shutdown();
            if (_menu != null) _menu.Teardown(); _menu = null;
            ApocasaverLoading.Clear();
            ApocapocketInventory.Clear();
            if (_loadingScreen != null) { SaveBus.StateChanged -= _loadingScreen.StateChanged; _loadingScreen.Dispose(); _loadingScreen = null; }
            MessageHook.Clear(); Bindings.Clear(); Menu.BlockCancelFrame = -1;
            SceneCache.Clear(); Assets.Dispose(); Menu.DisposeAssets(); GlyphFont.Reset();
            AccentTheme.Clear();
            Api.Compass.Entries.Clear(); Api.Compass.CustomIcons.Clear(); Api.Compass.IconRevision++; _attach = null;
        }
        public override IEnumerable<ToolPage> ToolPages()
        {
            yield return new ConfigPage(this, ToolGroups.Weapons, "Clean HUD & menus", Config.Main, "CleanHUD / HUD", "CleanHUD / Menu");
        }
        internal static void Log(string s) { Core.Log.Always("[CleanHUD] " + s); }
        internal static void Warn(string s) { Core.Log.Warn("[CleanHUD] " + s); }
        internal static void Throttled(string key, Exception e)
        {
            float next;
            if (_throttle.TryGetValue(key, out next) && Time.unscaledTime < next) return;
            _throttle[key] = Time.unscaledTime + 10f;
            Core.Log.Error("[CleanHUD] " + key + " failed", e);
        }
    }
}
