using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;

namespace WildWasteland.Core
{
    // Host world values are session overlays. Serializing an overlay always writes the player's original preference.
    internal static class MultiplayerSettings
    {
        static readonly Dictionary<ConfigEntryBase, string> Originals = new Dictionary<ConfigEntryBase, string>();
        static readonly Dictionary<string, ConfigEntryBase> World = new Dictionary<string, ConfigEntryBase>();
        static readonly List<ConfigEntryBase> WorldEntries = new List<ConfigEntryBase>();
        static readonly Dictionary<ConfigEntryBase, string> Sent = new Dictionary<ConfigEntryBase, string>();
        static bool _writing, _guest, _session;
        static int _cursor;
        static float _nextSend, _nextRequest;

        internal static void Install()
        {
            var setters = new HashSet<Type>();
            foreach (var file in new[] { Plugin.Main, Plugin.Settings, Plugin.Tools })
                foreach (var key in file.Keys)
                {
                    var entry = file[key];
                    if (setters.Add(entry.GetType()))
                        Require(AccessTools.PropertySetter(entry.GetType(), "Value"), "BeforeSet");
                    if (IsWorld(entry)) { World.Add(Id(entry), entry); WorldEntries.Add(entry); }
                }
            Require(AccessTools.Method(typeof(ConfigEntryBase), "GetSerializedValue"), "SerializeOriginal");
            Multiplayer.Message += Receive;
        }
        static void Require(System.Reflection.MethodInfo target, string prefix)
        {
            if (!Patches.Patch(Multiplayer.PatchOwner, target, prefix: AccessTools.Method(typeof(MultiplayerSettings), prefix)))
                throw new InvalidOperationException("Multiplayer setting guard failed: " + prefix);
        }
        static bool Ours(ConfigEntryBase e)
        { return e != null && (ReferenceEquals(e.ConfigFile, Plugin.Main) || ReferenceEquals(e.ConfigFile, Plugin.Settings) || ReferenceEquals(e.ConfigFile, Plugin.Tools)); }
        static string Module(ConfigEntryBase e) { return e.Definition.Section.Split('/')[0].Trim(); }
        static bool Performance(ConfigEntryBase e) { return Module(e) == "Performance"; }
        internal static bool Debug(ConfigEntryBase e)
        {
            string section = e.Definition.Section, key = e.Definition.Key;
            return ReferenceEquals(e, Plugin.EnableDebug) || ReferenceEquals(e, Plugin.DeveloperMode) || ReferenceEquals(e, Plugin.VerboseLog)
                || section == "Debug" || section.EndsWith("/ Debug", StringComparison.Ordinal) || section.EndsWith("/ Editors", StringComparison.Ordinal)
                || key.IndexOf("Verbose", StringComparison.OrdinalIgnoreCase) >= 0 || key.EndsWith("Log", StringComparison.Ordinal)
                || key == "ShowNav" || key == "DebugMap" || key == "EnableCheats" || key == "Real patrol names" || key == "hide route type";
        }
        internal static bool Local(ConfigEntryBase e)
        {
            if (ReferenceEquals(e, MultiplayerNames.FloatingNames)) return true;
            if (Debug(e) || Performance(e) || ReferenceEquals(e.ConfigFile, Plugin.Tools)) return false;
            if (NavigationModule(e)) return false;
            if (NavigationWorld(e)) return false;
            if (ReferenceEquals(e.ConfigFile, Plugin.Main) || e.Definition.Section == "Keys") return true;
            switch (Module(e))
            {
                case "HUD": case "CleanHUD": case "PlayerBody": return true;
                case "Guns": return e.Definition.Key == "ToggleIronsights" || e.Definition.Key == "IronsightWheelZoom"
                    || e.Definition.Key == "Tracers" || e.Definition.Key == "PlayerGunTracers";
                default: return false;
            }
        }
        // Navigation policy belongs to the world; guests retain only the two display switches.
        static bool NavigationWorld(ConfigEntryBase e)
        {
            if (Module(e) != "CleanHUD") return false;
            string section = e.Definition.Section, key = e.Definition.Key;
            if (section == "CleanHUD / Map") return key != "Enabled" && key != "BinocularZoom";
            return section == "CleanHUD / HUD" && (key == "ShowConvoys" || key == "ShowLocations"
                || key == "AmbiguousPatrols" || key == "AmbiguousLocations");
        }
        static bool IsWorld(ConfigEntryBase e)
        { return NavigationModule(e) || NavigationWorld(e) || !ReferenceEquals(e.ConfigFile, Plugin.Main) && !Local(e) && !Debug(e) && !Performance(e)
            && Module(e) != "Core" && Module(e) != "PlayerBody" && Module(e) != "CleanHUD" && Module(e) != "HUD"; }
        static bool NavigationModule(ConfigEntryBase e)
        { return e.Definition.Section == "CleanHUD" && e.Definition.Key == "Enabled"; }
        static string Id(ConfigEntryBase e)
        { return (ReferenceEquals(e.ConfigFile, Plugin.Tools) ? "T" : "S") + "\n" + e.Definition.Section + "\n" + e.Definition.Key; }
        internal static byte[] CaptureWorld()
        {
            using (var stream = new MemoryStream())
            {
                using (var gzip = new GZipStream(stream, CompressionMode.Compress, true)) using (var writer = new BinaryWriter(gzip))
                { writer.Write(WorldEntries.Count); foreach (var entry in WorldEntries) { string value = entry.GetSerializedValue(); writer.Write(Id(entry)); writer.Write(value); Sent[entry] = value; } }
                return stream.ToArray();
            }
        }
        internal static void ApplyWorld(byte[] data)
        {
            var incoming = new Dictionary<ConfigEntryBase, string>();
            using (var compressed = new MemoryStream(data, false)) using (var gzip = new GZipStream(compressed, CompressionMode.Decompress)) using (var raw = new MemoryStream())
            {
                var buffer = new byte[4096]; int length;
                while ((length = gzip.Read(buffer, 0, buffer.Length)) > 0) { if (raw.Length + length > 1048576) throw new InvalidDataException("World settings too large"); raw.Write(buffer, 0, length); }
                raw.Position = 0;
                using (var reader = new BinaryReader(raw))
                {
                    int count = reader.ReadInt32(); if (count != WorldEntries.Count) throw new InvalidDataException("WW world settings schema mismatch");
                    for (int i = 0; i < count; i++)
                    {
                        string id = reader.ReadString(), value = reader.ReadString(); ConfigEntryBase entry;
                        if (!World.TryGetValue(id, out entry) || incoming.ContainsKey(entry)) throw new InvalidDataException("Unknown/duplicate WW world setting");
                        incoming.Add(entry, value);
                    }
                    if (raw.Position != raw.Length) throw new InvalidDataException("Trailing world settings");
                }
            }
            // Configure feature values before enabling modules that were disabled on this machine.
            foreach (var pair in incoming) if (pair.Key.Definition.Key != "Enabled" || pair.Key.Definition.Section.IndexOf('/') >= 0) Overlay(pair.Key, pair.Value);
            foreach (var pair in incoming) if (pair.Key.Definition.Key == "Enabled" && pair.Key.Definition.Section.IndexOf('/') < 0) Overlay(pair.Key, pair.Value);
        }
        internal static bool CanEdit(ConfigEntryBase e)
        {
            if (!Ours(e)) return true;
            if (MultiplayerSaving.Frozen) return false;
            if (Multiplayer.SessionActive && Performance(e) && e.Definition.Key == "Enabled") return false;
            return !Multiplayer.Client || Local(e);
        }
        static bool BeforeSet(ConfigEntryBase __instance) { return _writing || CanEdit(__instance); }
        static bool SerializeOriginal(ConfigEntryBase __instance, ref string __result)
        { string original; if (!Originals.TryGetValue(__instance, out original)) return true; __result = original; return false; }
        static void Overlay(ConfigEntryBase entry, string serialized)
        {
            if (!Originals.ContainsKey(entry)) Originals.Add(entry, entry.GetSerializedValue());
            bool save = entry.ConfigFile.SaveOnConfigSet; entry.ConfigFile.SaveOnConfigSet = false;
            _writing = true;
            try { entry.SetSerializedValue(serialized); }
            finally { _writing = false; entry.ConfigFile.SaveOnConfigSet = save; }
        }
        internal static void Changed()
        {
            bool guest = Multiplayer.Client, session = Multiplayer.SessionActive;
            if (guest == _guest && session == _session) return;
            _guest = guest; _session = session;
            // Remove the serialization overlay before restoring; setters remain privileged for this operation only.
            var old = new List<KeyValuePair<ConfigEntryBase, string>>(Originals); Originals.Clear();
            foreach (var pair in old)
            {
                bool save = pair.Key.ConfigFile.SaveOnConfigSet; pair.Key.ConfigFile.SaveOnConfigSet = false; _writing = true;
                try { pair.Key.SetSerializedValue(pair.Value); }
                finally { _writing = false; pair.Key.ConfigFile.SaveOnConfigSet = save; }
            }
            if (session)
                foreach (var module in ModuleHost.Entries)
                    if (module.Module.Id == "Performance") Overlay(module.Config.Enabled, "false");
            if (guest)
                foreach (var file in new[] { Plugin.Main, Plugin.Settings, Plugin.Tools })
                    foreach (var key in file.Keys)
                        if (file[key].SettingType == typeof(bool) && Debug(file[key])) Overlay(file[key], "false");
            _nextSend = _nextRequest = 0; _cursor = 0; Sent.Clear();
            Log.Always("[Multiplayer] Settings: " + (guest ? "host world/fog/marker settings; local HUD and map/compass on/off; debug unavailable" : session ? "host world authority; Performance locked off" : "local preferences restored"));
        }
        internal static void Tick()
        {
            if (!Multiplayer.WorldReady) return;
            float now = UnityEngine.Time.unscaledTime;
            if (Multiplayer.Client)
            {
                if (now >= _nextRequest) { _nextRequest = now + 3f; Multiplayer.Send(40, w => { }); }
                return;
            }
            if (now < _nextSend || WorldEntries.Count == 0) return;
            // A bounded rolling snapshot also covers late joins and changes made outside the WW menu.
            for (int n = 0; n < 4 && _cursor < WorldEntries.Count; n++, _cursor++)
            {
                var entry = WorldEntries[_cursor]; string id = Id(entry), value = entry.GetSerializedValue(), previous;
                if (Sent.TryGetValue(entry, out previous) && previous == value) continue;
                if (Encoding.UTF8.GetByteCount(id) + Encoding.UTF8.GetByteCount(value) > 1050) continue;
                Multiplayer.Send(41, w => { w.Write(id); w.Write(value); });
                Sent[entry] = value;
            }
            if (_cursor >= WorldEntries.Count) { _cursor = 0; _nextSend = now + 1f; }
        }
        static void Receive(ulong from, byte kind, BinaryReader reader)
        {
            if (kind == 40 && Multiplayer.Hosting) { _nextSend = 0; return; }
            if (kind != 41 || !Multiplayer.Client || from != 0) return;
            string id = reader.ReadString(), value = reader.ReadString(); ConfigEntryBase entry;
            if (reader.BaseStream.Position != reader.BaseStream.Length || !World.TryGetValue(id, out entry)) return;
            if (entry.GetSerializedValue() == value && !Originals.ContainsKey(entry)) return;
            Overlay(entry, value);
        }
    }
}
