using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;
using Player = WildWasteland.Modules.PlayerBody.PlayerBodyModule;
using HudSettings = WildWasteland.Modules.HUD.HudModule;
using Patrols = WildWasteland.Modules.Patrols.PatrolsModule;
using CleanHudSettings = WildWasteland.Modules.CleanHUD.CleanHudModule;

namespace WildWasteland.Core.UI
{
    /// <summary>Pages owned by Core: module overview (on/off), performance, Harmony inventory, core settings.</summary>
    internal static class CorePages
    {
        internal static ToolPage GamePage, WorldPage, HudPage, DebugPage, DeveloperPage;
        static readonly List<ToolPage> _debugPages = new List<ToolPage>();
        static ConfigPage _settings;
        static ConfigEntry<bool> _poi;
        internal static void AddDebugPage(ToolPage page) { _debugPages.Add(page); }

        internal static void Register()
        {
            GamePage = new GameSettingsPage("PLAYER", DrawPlayer);
            WorldPage = new GameSettingsPage("WORLD", DrawWorld);
            HudPage = new GameSettingsPage("HUD", DrawHud);
            ToolWindow.Add(GamePage); ToolWindow.Add(WorldPage); ToolWindow.Add(HudPage);
            ToolWindow.Add(new ActionPage(null, ToolGroups.General, "Modules", Modules));
            ToolWindow.Add(WildWasteland.Modules.Performance.PerformancePage.Instance);   // #133: separate Performance tab
            DebugPage = new ActionPage(null, ToolGroups.General, "Debug", Debug) { DebugOnly = true };
            DeveloperPage = new ActionPage(null, ToolGroups.General, "Developer", Developer) { DebugOnly = true };
            ToolWindow.Add(DebugPage); ToolWindow.Add(DeveloperPage);
            _settings = new ConfigPage(null, ToolGroups.General, "Other settings", Plugin.Settings, d => true) { Intro = "Remaining gameplay values and switches." };
            foreach (var e in ModuleHost.Entries) if (e.Module.Id == "POI") _poi = e.Config.Enabled;
        }

        static string F(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        internal static void ResetSettings()
        {
            var excluded = new HashSet<ConfigEntryBase>();
            foreach (var m in ModuleHost.Entries)
                if (m.Module.Id != "Performance" && m.Module.Id != "POI") excluded.Add(m.Config.Enabled);
            var entries = new List<ConfigEntryBase>();
            foreach (var file in new[] { Plugin.Settings, Plugin.Tools })
                foreach (var key in file.Keys) if (!excluded.Contains(file[key]) && MultiplayerSettings.CanEdit(file[key])) entries.Add(file[key]);
            if (MultiplayerSettings.CanEdit(Plugin.EnableDebug)) entries.Add(Plugin.EnableDebug);
            var drivingHud = WildWasteland.Modules.CleanHUD.ChaseCameraHud.HudEnabled;
            if (drivingHud != null) entries.Add(drivingHud);
            var files = new Dictionary<ConfigFile, bool>();
            foreach (var e in entries) if (!files.ContainsKey(e.ConfigFile)) files.Add(e.ConfigFile, e.ConfigFile.SaveOnConfigSet);
            foreach (var file in files.Keys) file.SaveOnConfigSet = false;
            try
            {
                var preset = WildWasteland.Modules.Performance.PerformanceModule.PresetEntry;
                if (MultiplayerSettings.CanEdit(preset)) preset.BoxedValue = preset.DefaultValue;
                foreach (var e in entries) e.BoxedValue = e.DefaultValue;
            }
            finally
            {
                foreach (var pair in files) pair.Key.SaveOnConfigSet = pair.Value;
                foreach (var file in files.Keys) file.Save();
            }
            Ledger.ClearNumbers(); ToolLayout.CancelRebind(); GUI.FocusControl(null);
            Log.Always("[Core] Settings reset: Game, Performance, Debug and Developer restored to defaults");
        }

        sealed class GameSettingsPage : ToolPage
        {
            readonly System.Action<ToolLayout> _draw;
            internal GameSettingsPage(string title, System.Action<ToolLayout> draw) { Group = ToolGroups.General; Title = title; _draw = draw; }
            public override void OnShow()
            {
                WildWasteland.Modules.PlayerBody.InventoryModel.PrepareSettingsPictures();
                WildWasteland.Modules.HUD.CrosshairController.PreparePreview();
            }
            public override void Draw(ToolLayout l) { _draw(l); }
        }

        static void Switch(ToolLayout l, string label, ConfigEntry<bool> entry)
        {
            bool value = l.Choices(label, entry.Value ? 1 : 0, "Off", "On") == 1;
            if (value != entry.Value) entry.Value = value;
        }

        static void DrawPlayer(ToolLayout l)
        {
            l.Heading("Player");
            BodySelection(l);
            int body = !Player.BodyFirstPerson.Value ? 0 : Player.FullBodyOnlyOnFoot.Value ? 1 : 2;
            int next = l.Choices("Full Body", body, "Off", "Only on foot", "On");
            if (next != body) { Player.FullBodyOnlyOnFoot.Value = next == 1; Player.BodyFirstPerson.Value = next != 0; }
            int arms = !Player.EmptyHandArms.Value ? 0 : Player.FirstPersonArmsOnlyOnFoot.Value ? 1 : 2;
            next = l.Choices("First Person Arms", arms, "Off", "On foot", "On");
            if (next != arms) { Player.FirstPersonArmsOnlyOnFoot.Value = next == 1; Player.EmptyHandArms.Value = next != 0; }
            Switch(l, "Climbing", Player.ClimbingOn);
            Switch(l, "Third Person Camera", Player.ThirdPersonOnFoot);
            Switch(l, "Occlusion", Player.OcclusionPrototype);   // third person on foot only; in cars it stays off
            l.Text("Full Body shows your body in first person. Use the game's Change Camera key for third person.");
            if (!ModuleHost.IsRunning(Player.Instance)) l.Text("Enable Player body & animation in Modules to use these player settings.");
        }
        internal static void DrawWorld(ToolLayout l)
        {
            l.Heading("World");
            if (Multiplayer.Client) l.Text("World settings are controlled by the host.");
            else
            {
            int vehicles = !Patrols.ConvoyEnabled.Value ? 0 : Patrols.FuryRoad.Value ? 2 : 1;
            int next = l.Choices("Enemy Vehicles", vehicles, "Loner", "New arrival", "Fury road");
            if (next != vehicles)
            {
                Patrols.ConvoyEnabled.Value = next != 0;
                Patrols.FuryRoad.Value = next == 2;
            }
            l.Text("Loner: no automatic patrols. New arrival: groups unlock with distance and boss kills. Fury road: all patrol types are available from the start.");
            Switch(l, "Foot patrols", Patrols.FootPatrols);
            if (_poi != null) Switch(l, "New POI", _poi);
            var weather = Services.Weather;
            if (weather != null)
            {
                weather.MapAreas = l.Choices("Weather storm areas on map", weather.MapAreas ? 1 : 0, "Off", "On") == 1;
            }
            NavigationSettings(l);
            }
        }
        static void DrawHud(ToolLayout l)
        {
            l.Heading("HUD");
            CrosshairSelection(l);
            Switch(l, "Grabbing dot", HudSettings.SelectionDot);
            bool damage = HudSettings.HudEnabled.Value && HudSettings.DamageNumbers.Value == 2;
            bool show = l.Choices("Floating damage", damage ? 1 : 0, "Off", "On") == 1;
            if (show != damage) { HudSettings.DamageNumbers.Value = show ? 2 : 0; if (show) HudSettings.HudEnabled.Value = true; }
            FeedbackSwitch(l, "Hit markers", HudSettings.HitMarker);
            FeedbackSwitch(l, "Headshot sound", HudSettings.HeadshotSound);
            Switch(l, "Toggle Ironsights", WildWasteland.Modules.Guns.GunsModule.ToggleIronsights);
            l.KeyPair("Shout", WildWasteland.Modules.NPC.NpcModule.ShoutKey, "Shout modifier", WildWasteland.Modules.NPC.NpcModule.ShoutModifier);
            if (!ModuleHost.IsRunning(HudSettings.Instance)) l.Text("Enable Combat HUD in Modules to use these HUD settings.");
            Switch(l, "Needs", CleanHudSettings.ShowNeeds);
            Switch(l, "HP and armor numbers", CleanHudSettings.ShowHealthNumbers);
            Switch(l, "Insanity", CleanHudSettings.ShowInsanity);
            Switch(l, "Vehicle Hints", Player.VehicleHotkeyHint);
            var drivingHud = WildWasteland.Modules.CleanHUD.ChaseCameraHud.HudEnabled;
            if (drivingHud != null)
            {
                int mode = !drivingHud.Value ? 0 : CleanHudSettings.DrivingHudOnlyThirdPerson.Value ? 1 : 2;
                int selected = l.Choices("Gear/RPM/Speed HUD", mode, "Off", "3rd person", "On");
                if (selected != mode) { CleanHudSettings.DrivingHudOnlyThirdPerson.Value = selected == 1; drivingHud.Value = selected != 0; }
            }
            Switch(l, "Vehicle Icons", Player.VehicleStatusHint);
            Switch(l, "Compass", CleanHudSettings.ShowCompass);
            if (Multiplayer.Present)
            {
                Switch(l, "Floating names", MultiplayerNames.FloatingNames);
                Switch(l, "Players on compass", CleanHudSettings.ShowPlayers);
                Switch(l, "Players on map", CleanHudSettings.ShowPlayersOnMap);
            }
            Switch(l, "Map", CleanHudSettings.MapEnabled);
            if (Multiplayer.Client) l.Text("Fog of war, locations, patrol markers and routes follow the host's World settings.");
            l.Entry(CleanHudSettings.Scale);
        }

        static void NavigationSettings(ToolLayout l)
        {
            l.Text("Map and compass world settings");
            CompassDetail(l, "Patrols on compass", CleanHudSettings.ShowConvoys, CleanHudSettings.AmbiguousPatrols);
            CompassDetail(l, "Locations on compass (1 km)", CleanHudSettings.ShowLocations, CleanHudSettings.AmbiguousLocations);
            Switch(l, "Fog of war", CleanHudSettings.FogOfWar);
            if (Multiplayer.Present) Switch(l, "Shared vision", CleanHudSettings.SharedVision);
            CleanHudSettings.MapMarkers.Value = (MapDetail)l.Choices("Map markers", (int)CleanHudSettings.MapMarkers.Value, "Off", "Ambiguous", "On");
            CleanHudSettings.MapTrafficMarkers.Value = (MapDetail)l.Choices("World traffic markers", (int)CleanHudSettings.MapTrafficMarkers.Value, "Off", "Ambiguous", "On");
            CleanHudSettings.MapRoutes.Value = (MapDetail)l.Choices("Map Routes", (int)CleanHudSettings.MapRoutes.Value, "Off", "Ambiguous", "On");
            l.Entry(CleanHudSettings.FogEdge); l.Entry(CleanHudSettings.FogOpacity);
        }

        static void CompassDetail(ToolLayout l, string label, ConfigEntry<bool> show, ConfigEntry<bool> ambiguous)
        {
            int current = !show.Value ? 0 : ambiguous.Value ? 1 : 2;
            int next = l.Choices(label, current, "Off", "Ambiguous", "On");
            if (next != current) { ambiguous.Value = next == 1; show.Value = next != 0; }
        }

        static void FeedbackSwitch(ToolLayout l, string label, ConfigEntry<bool> entry)
        {
            bool value = HudSettings.HudEnabled.Value && entry.Value;
            bool next = l.Choices(label, value ? 1 : 0, "Off", "On") == 1;
            if (next != value) { entry.Value = next; if (next) HudSettings.HudEnabled.Value = true; }
        }

        static void BodySelection(ToolLayout l)
        {
            var order = WildWasteland.Modules.PlayerBody.CharacterPortraits.Order;
            var r = l.Next(288); float width = (r.width - 8 * (order.Length - 1)) / order.Length;
            for (int i = 0; i < order.Length; i++)
            {
                int choice = order[i];
                var card = new Rect(r.x + i * (width + 8), r.y, width, r.height);
                bool selected = (int)Player.Character.Value == choice;
                Ledger.Panel(card, selected ? Ledger.Selected : Ledger.Field, selected ? Ledger.Accent : Ledger.Line);
                var texture = WildWasteland.Modules.PlayerBody.CharacterPortraits.Picture(choice);
                if (texture != null) GUI.DrawTexture(new Rect(card.x + 6, card.y + 6, card.width - 12, card.height - 12), texture, ScaleMode.ScaleToFit);
                if (GUI.Button(card, GUIContent.none, GUIStyle.none) && !selected) Player.Character.Value = (Player.Gender)choice;
            }
        }

        static readonly string[] _crosshairs = { "None", "Vanilla", "Lines", "Dot" };
        static void CrosshairSelection(ToolLayout l)
        {
            l.Text("Crosshair");
            var r = l.Next(74); float width = (r.width - 24) / 4;
            for (int i = 0; i < _crosshairs.Length; i++)
            {
                var card = new Rect(r.x + i * (width + 8), r.y, width, r.height);
                bool selected = HudSettings.Crosshair.Value == i;
                Ledger.Panel(card, selected ? Ledger.Selected : Ledger.Field, selected ? Ledger.Accent : Ledger.Line);
                float previewSize = i == 3 ? 27 : 36;
                WildWasteland.Modules.HUD.CrosshairController.DrawPreview(new Rect(card.center.x - previewSize / 2, card.y + 23 - previewSize / 2, previewSize, previewSize), i);
                Ledger.Label(new Rect(card.x, card.yMax - 29, card.width, 26), _crosshairs[i], Ledger.Body, selected ? Ledger.Accent : Ledger.Text, TextAnchor.MiddleCenter);
                if (GUI.Button(card, GUIContent.none, GUIStyle.none) && !selected) HudSettings.Crosshair.Value = i;
            }
        }

        static void Modules(ToolLayout l)
        {
            l.Text("Turn feature groups on or off. Changes apply immediately.");
            foreach (var e in ModuleHost.Entries)
            {
                if (Multiplayer.Client && !MultiplayerSettings.CanEdit(e.Config.Enabled)) continue;
                bool enabled = GUI.enabled; GUI.enabled = enabled && e.State != ModuleState.OffConflict && MultiplayerSettings.CanEdit(e.Config.Enabled);
                if (e.Config != null) l.Toggle(e.Config.Enabled, e.Module.DisplayName);
                GUI.enabled = enabled;
                if (e.State == ModuleState.OffConflict) l.Text("Standalone replacement: " + e.Conflict, Ledger.Danger);
                else if (e.State == ModuleState.InitFailed || e.State == ModuleState.DisabledAfterErrors) l.Text("Could not start. Switch off and on to retry: " + e.LastError, Ledger.Danger);
                if (e.Module.Id == "Performance" && Multiplayer.SessionActive) l.Text("Performance optimizations are locked off during multiplayer.");
            }
        }

        static bool DebugSetting(ConfigEntryBase e)
        {
            if (VerboseSetting(e) || ReferenceEquals(e, Plugin.DeveloperMode) || ReferenceEquals(e, MultiplayerNames.FloatingNames)) return false;
            if (ReferenceEquals(e, CleanHudSettings.ShowNeeds) || ReferenceEquals(e, CleanHudSettings.ShowHealthNumbers)
                || ReferenceEquals(e, CleanHudSettings.ShowInsanity) || ReferenceEquals(e, CleanHudSettings.Scale)
                || ReferenceEquals(e, CleanHudSettings.ShowConvoys) || ReferenceEquals(e, CleanHudSettings.ShowLocations)
                || ReferenceEquals(e, CleanHudSettings.AmbiguousPatrols) || ReferenceEquals(e, CleanHudSettings.AmbiguousLocations)
                || ReferenceEquals(e, CleanHudSettings.ShowCompass) || ReferenceEquals(e, CleanHudSettings.ShowPlayers) || ReferenceEquals(e, CleanHudSettings.ShowPlayersOnMap) || ReferenceEquals(e, CleanHudSettings.MapEnabled)
                || ReferenceEquals(e, CleanHudSettings.MapMarkers) || ReferenceEquals(e, CleanHudSettings.MapRoutes)
                || ReferenceEquals(e, CleanHudSettings.FogOfWar) || ReferenceEquals(e, CleanHudSettings.SharedVision) || ReferenceEquals(e, CleanHudSettings.MapTrafficMarkers)
                || ReferenceEquals(e, CleanHudSettings.DrivingHudOnlyThirdPerson)) return false;
            if (ReferenceEquals(e, Player.Character) || ReferenceEquals(e, Player.BodyFirstPerson) || ReferenceEquals(e, Player.FullBodyOnlyOnFoot)
                || ReferenceEquals(e, Player.VehicleHotkeyHint) || ReferenceEquals(e, Player.VehicleStatusHint)
                || ReferenceEquals(e, Player.EmptyHandArms) || ReferenceEquals(e, Player.FirstPersonArmsOnlyOnFoot) || ReferenceEquals(e, Player.OcclusionPrototype)
                || ReferenceEquals(e, Player.ThirdPersonOnFoot) || ReferenceEquals(e, Player.ClimbingOn) || ReferenceEquals(e, Patrols.FuryRoad)
                || ReferenceEquals(e, Patrols.ConvoyEnabled) || ReferenceEquals(e, Patrols.FootPatrols) || ReferenceEquals(e, HudSettings.SelectionDot)
                || ReferenceEquals(e, HudSettings.Crosshair) || ReferenceEquals(e, HudSettings.HitMarker) || ReferenceEquals(e, HudSettings.HeadshotSound)
                || ReferenceEquals(e, HudSettings.DamageNumbers) || ReferenceEquals(e, WildWasteland.Modules.Guns.GunsModule.ToggleIronsights)
                || ReferenceEquals(e, WildWasteland.Modules.NPC.NpcModule.ShoutKey) || ReferenceEquals(e, WildWasteland.Modules.NPC.NpcModule.ShoutModifier)) return false;
            foreach (var m in ModuleHost.Entries) if (ReferenceEquals(e, m.Config.Enabled)) return false;
            // tools.cfg holds the fine tuning (offsets, poses, AI and animation constants): it stays in the file and keeps working, but the
            // Debug page only shows its debug switches (logs, overlays, editors)
            if (ReferenceEquals(e.ConfigFile, Plugin.Tools)) return ToolsDebugSwitch(e);
            return true;
        }

        static bool ToolsDebugSwitch(ConfigEntryBase e)
        {
            if (e.SettingType != typeof(bool)) return false;
            string sec = e.Definition.Section, key = e.Definition.Key;
            if (sec.EndsWith("/ Debug", System.StringComparison.Ordinal) || sec.EndsWith("/ Editors", System.StringComparison.Ordinal)) return true;
            return key == "Log" || key.IndexOf("Verbose", System.StringComparison.Ordinal) >= 0 || key == "DebugMap" || key == "Real patrol names" || key == "hide route type";
        }

        static bool VerboseSetting(ConfigEntryBase e)
        {
            if (e.SettingType != typeof(bool)) return false;
            if (ReferenceEquals(e, WildWasteland.Modules.NPC.NpcModule.ShowNav)) return true;
            string key = e.Definition.Key;
            return key.IndexOf("Verbose", System.StringComparison.OrdinalIgnoreCase) >= 0 || key == "Log" || key.EndsWith("Log", System.StringComparison.OrdinalIgnoreCase);
        }

        static void Logging(ToolLayout l, ConfigFile file, IModule owner)
        {
            foreach (var key in file.Keys)
            {
                var e = file[key];
                if (BelongsTo(e, owner) && !ReferenceEquals(e, Plugin.VerboseLog) && VerboseSetting(e))
                    l.Toggle((ConfigEntry<bool>)e, ReferenceEquals(e, WildWasteland.Modules.NPC.NpcModule.ShowNav)
                        ? "Show navigation, ghosts and NPC states in the world" : key.Section + " / " + key.Key);
            }
        }

        static bool WeatherTuning(ConfigEntryBase e)
        { return e.Definition.Section == "Weather / Storms" && (e.Definition.Key == "Hurricanes per km²" || e.Definition.Key == "Front width (km)" || e.Definition.Key == "Depth (km)"); }

        static bool RetainedDebugSetting(ConfigEntryBase e)
        {
            if (WeatherTuning(e) || ReferenceEquals(e, CleanHudSettings.FogRadius) || VerboseSetting(e) || ReferenceEquals(e, Patrols.MenuKey) || ReferenceEquals(e, Patrols.TemplateDumpKey)) return true;
            return e.SettingType == typeof(bool) && DebugSetting(e) && !ReferenceEquals(e, Player.WeaponAdjust)
                && !ReferenceEquals(e, Patrols.TurretSpotEditor) && !ReferenceEquals(e, Patrols.BusPassengerPoseEditorEnabled);
        }

        static bool DeveloperSetting(ConfigEntryBase e) { return DebugSetting(e) && !RetainedDebugSetting(e); }

        static IModule _categoryOwner;
        static bool BelongsTo(ConfigEntryBase e, IModule owner)
        {
            if (ReferenceEquals(e, Patrols.MenuKey) || ReferenceEquals(e, Patrols.TemplateDumpKey)) return owner != null && owner.Id == "Patrols";
            string section = e.Definition.Section; int cut = section.IndexOf(" / ", System.StringComparison.Ordinal);
            foreach (var m in ModuleHost.Entries)
                if (section == m.Module.Id || (cut == m.Module.Id.Length && string.CompareOrdinal(section, 0, m.Module.Id, 0, cut) == 0)) return ReferenceEquals(owner, m.Module);
            return owner == null;
        }
        static bool RemainingDeveloperSetting(ConfigEntryBase e) { return DeveloperSetting(e) && BelongsTo(e, _categoryOwner); }
        static bool HasDebugSettings(ConfigFile file, IModule owner)
        {
            foreach (var key in file.Keys) if (BelongsTo(file[key], owner) && RetainedDebugSetting(file[key])) return true;
            return false;
        }

        static void Debug(ToolLayout l)
        {
            l.DeduplicateEntries = true;
            EnsureSwitches();
            for (int i = -1; i < ModuleHost.Entries.Count; i++)
            {
                var owner = i < 0 ? null : ModuleHost.Entries[i].Module;
                if (owner != null && !HasDebugSettings(Plugin.Settings, owner) && !HasDebugSettings(Plugin.Tools, owner)) continue;
                if (!l.Category("Debug/" + (owner != null ? owner.Id : "Core"), CategoryTitle(owner))) continue;
                if (owner != null && owner.Id == "CleanHUD")
                {
                    l.Heading("Fog of war tuning");
                    l.Entry(CleanHudSettings.FogRadius);
                }
                if (owner != null && owner.Id == "Weather")
                {
                    l.Heading("Storm tuning");
                    foreach (var key in Plugin.Settings.Keys) if (WeatherTuning(Plugin.Settings[key])) l.Entry(Plugin.Settings[key]);
                }
                if (owner != null && owner.Id == "Patrols")
                {
                    l.Heading("Patrol editor");
                    l.Entry(Patrols.MenuKey); l.Entry(Patrols.TemplateDumpKey);
                    if (l.Button("Open the patrol editor", Patrols.Running && GameRefs.InPlay, true, 320))
                        ToolWindow.CloseThen(() => { if (Patrols.Instance != null && !Patrols.Instance.OpenEditor()) Log.Warn("[Patrols] the patrol editor opens only in play (not in a menu or while another editor is open)"); });
                }
                l.Heading("Logging");
                if (owner == null)
                {
                    l.Toggle(Plugin.VerboseLog, "VerboseLog");
                    l.Text("General verbose logging. Module log switches retain their own behavior; NPC traces also work with general logging off.");
                }
                Logging(l, Plugin.Settings, owner); Logging(l, Plugin.Tools, owner);
                l.EntryFilter = RetainedDebugSetting;
                l.Heading("Switches");
                if (owner != null && owner.Id == "Patrols") l.Toggle(WildWasteland.Modules.Patrols.WarRig.S.GroundRecoveryEnabled, "Ground recovery");
                foreach (var s in _switches) if (ReferenceEquals(s.Module, owner)) l.Toggle(s.Entry, l.PageLabel(s.Entry) ?? s.Label);
                l.EntryFilter = null;
                if (owner == null) l.Toggle(Plugin.DeveloperMode, "Developer mode");
            }
        }

        static string CategoryTitle(IModule owner) { return owner == null ? "Core" : owner.Id + " / " + owner.DisplayName + (ModuleHost.IsRunning(owner) ? "" : " (module off)"); }

        static void Developer(ToolLayout l)
        {
            l.DeduplicateEntries = true;
            for (int i = -1; i < ModuleHost.Entries.Count; i++)
            {
                var owner = i < 0 ? null : ModuleHost.Entries[i].Module;
                if (!l.Category("Developer/" + (owner != null ? owner.Id : "Core"), CategoryTitle(owner))) continue;
                if (owner != null && owner.Id == "Performance")
                {
                    l.EntryFilter = null;
                    WildWasteland.Modules.Performance.PerformancePage.Instance.DrawDeveloper(l);
                }
                l.EntryFilter = DeveloperSetting;
                foreach (var p in _debugPages) if (ReferenceEquals(p.Owner, owner) && p.IsTool) DrawDebugPage(l, p);
                foreach (var p in _debugPages) if (ReferenceEquals(p.Owner, owner) && !p.IsTool) DrawDebugPage(l, p);
                _categoryOwner = owner; l.EntryFilter = RemainingDeveloperSetting;
                l.SearchSection("Other settings"); l.Prefix = "Developer/";
                _settings.Draw(l);
                l.EntryFilter = null;
                if (owner == null)
                {
                    l.SearchSection("Core diagnostics");
                    if (l.Button("Write startup summary to the log", true, false, 360)) Log.Always(ModuleHost.Summary());
                }
                l.SearchSection("Harmony patches"); Harmony(l, owner);
            }
        }

        // Switches block (RC12): every bool the Debug page shows further down, grouped by module, drawn once here (deduplication hides
        // the lower copies). The sorted list is rebuilt only when either file's entry count changes, not per OnGUI event.
        sealed class SwitchItem { internal ConfigEntry<bool> Entry; internal string Label, Section; internal int Order, Index; internal IModule Module; internal string Heading, HeadingOff; }
        sealed class SwitchOrder : IComparer<SwitchItem>
        {
            public int Compare(SwitchItem a, SwitchItem b)
            {
                int c = a.Order.CompareTo(b.Order); if (c != 0) return c;
                c = string.CompareOrdinal(a.Section, b.Section); if (c != 0) return c;
                c = string.Compare(a.Label, b.Label, System.StringComparison.OrdinalIgnoreCase); if (c != 0) return c;
                return a.Index.CompareTo(b.Index);
            }
        }
        static readonly List<SwitchItem> _switches = new List<SwitchItem>();
        static readonly SwitchOrder _switchOrder = new SwitchOrder();
        static int _switchCount = -1;

        static void BuildSwitches()
        {
            _switches.Clear();
            int index = 0;
            AddSwitches(Plugin.Settings, ref index); AddSwitches(Plugin.Tools, ref index);
            _switches.Sort(_switchOrder);
            for (int i = 0; i < _switches.Count; i++)   // same label twice in one module: name the sub-section
                for (int j = i + 1; j < _switches.Count && _switches[j].Order == _switches[i].Order; j++)
                    if (string.Equals(_switches[i].Label, _switches[j].Label, System.StringComparison.OrdinalIgnoreCase) && _switches[i].Section != _switches[j].Section)
                    { _switches[i].Label += " (" + _switches[i].Section + ")"; _switches[j].Label += " (" + _switches[j].Section + ")"; }
        }

        static void AddSwitches(ConfigFile file, ref int index)
        {
            if (file == null) return;
            foreach (var key in file.Keys)
            {
                var e = file[key] as ConfigEntry<bool>;
                index++;
                if (e == null || !DebugSetting(e)) continue;
                string sec = key.Section; int cut = sec.IndexOf(" / ", System.StringComparison.Ordinal);
                string prefix = cut >= 0 ? sec.Substring(0, cut) : sec;
                string label = ReferenceEquals(e, CleanHudSettings.Enabled) ? "Clean HUD" : ReferenceEquals(e, CleanHudSettings.MenuEnabled) ? "Clean menus" : ReferenceEquals(e, CleanHudSettings.CleanFont) ? "CleanFont" : Readable(key.Key);
                var item = new SwitchItem { Entry = e, Label = label, Section = sec, Order = -1, Index = index, Heading = "Core", HeadingOff = "Core" };
                for (int m = 0; m < ModuleHost.Entries.Count; m++)
                {
                    var me = ModuleHost.Entries[m];
                    if (me.Module.Id != prefix) continue;
                    item.Order = m; item.Module = me.Module; item.Heading = me.Module.DisplayName; item.HeadingOff = me.Module.DisplayName + " (module off)";
                    break;
                }
                _switches.Add(item);
            }
        }

        /// <summary>"TurretSpotEditor" -> "Turret spot editor"; keys with spaces are kept as they are.</summary>
        static string Readable(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOf(' ') >= 0) return key;
            var sb = new System.Text.StringBuilder(key.Length + 8);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool split = i > 0 && char.IsUpper(c) && (char.IsLower(key[i - 1]) || (i + 1 < key.Length && char.IsLower(key[i + 1]) && char.IsUpper(key[i - 1])));
                if (split) sb.Append(' ');
                sb.Append(split && i + 1 < key.Length && char.IsLower(key[i + 1]) ? char.ToLowerInvariant(c) : c);
            }
            return sb.ToString();
        }

        static void EnsureSwitches()
        {
            int count = (Plugin.Settings != null ? Plugin.Settings.Count : 0) + (Plugin.Tools != null ? Plugin.Tools.Count : 0);
            if (count != _switchCount) { _switchCount = count; BuildSwitches(); }
        }

        static void DrawDebugPage(ToolLayout l, ToolPage p)
        {
            l.SearchSection(p.Group + " / " + p.Title);
            l.Heading(p.Group + " / " + p.Title);
            l.Prefix = p.Group + "/" + p.Title + "/";
            bool enabled = GUI.enabled;
            bool off = p.Owner != null && !ModuleHost.IsRunning(p.Owner);
            if (off) l.Text(p.Owner.DisplayName + " is off. Settings remain editable; live tools need the module enabled.");
            if (off && !p.ShowWhenModuleOff && !(p is ConfigPage)) GUI.enabled = false;
            try { p.Draw(l); } catch (System.Exception ex) { l.Text("Page error: " + ex.Message, Ledger.Danger); Log.Error("Debug page " + p.Title, ex); }
            finally { GUI.enabled = enabled; }
        }

        static readonly Dictionary<string, double[]> _sum = new Dictionary<string, double[]>();

        internal static void Performance(ToolLayout l)
        {
            l.Text("Managed time of every Wild Wasteland job, measured by the scheduler. Budget: average <= 0.5 ms per frame, worst <= 2 ms. Times while this window is open are from the paused game; close it and reopen to read live numbers.");
            l.Value("Frames measured", Scheduler.Frames.ToString(CultureInfo.InvariantCulture));
            l.Value("WW average / worst frame (ms)", F(Scheduler.AvgFrameMs) + " / " + F(Scheduler.MaxFrameMs));
            if (l.Button("Reset statistics", true, false, 220)) Scheduler.ResetStats();
            _sum.Clear();
            foreach (var j in Scheduler.AllJobs())
            {
                string owner = j.Owner != null ? j.Owner.Id : "Core";
                double[] s; if (!_sum.TryGetValue(owner, out s)) { s = new double[2]; _sum[owner] = s; }
                s[0] += j.TotalMs; if (j.MaxMs > s[1]) s[1] = j.MaxMs;
            }
            l.Heading("Per module (avg ms/frame, worst job ms)");
            double frames = Mathf.Max(1, Scheduler.Frames);
            foreach (var kv in _sum) l.Value(kv.Key, F(kv.Value[0] / frames) + "   worst " + F(kv.Value[1]));
            l.Heading("Jobs (avg ms/frame, last, worst, calls, errors)");
            foreach (var j in Scheduler.AllJobs())
                l.Value(j.ToString(), F(j.TotalMs / frames) + "  " + F(j.LastMs) + "  " + F(j.MaxMs) + "  " + j.Calls + (j.Exceptions > 0 ? "  ERR " + j.Exceptions : "") + (j.Enabled ? "" : "  (off)"));
        }

        static void Harmony(ToolLayout l, IModule module)
        {
            string owner = module != null ? module.Id : "Core";
            l.Heading("Harmony patches");
            if (module == null)
            {
                l.Text("Shared PlayMaker targets go through Core dispatchers; module patches are listed inside their module category.");
                l.Value("Conflicts", Patches.Conflicts.ToString(CultureInfo.InvariantCulture));
                l.Text(Hooks.Summary());
            }
            foreach (var r in Patches.Records)
            {
                if (r.Owner != owner) continue;
                l.Text((r.Ok ? "" : "FAILED ") + r.Kind + "  " + r.Target + (r.Ok ? "" : "  - " + r.Error), r.Ok ? (Color?)null : Ledger.Danger);
            }
        }
    }
}
