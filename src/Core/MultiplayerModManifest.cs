using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BepInEx.Bootstrap;

namespace WildWasteland.Core
{
    internal static class MultiplayerModManifest
    {
        sealed class Mod { internal string Name, Version, Build; }
        static SortedDictionary<string, Mod> _local;
        internal static void Reset() { _local = null; _hash = null; }
        static byte[] _hash;
        internal static byte[] Hash()
        {
            if (_hash != null) return _hash;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                var mods = Local(); writer.Write((ushort)mods.Count);
                foreach (var pair in mods) { writer.Write(pair.Key); writer.Write(pair.Value.Version); writer.Write(pair.Value.Build); }
                using (var hash = SHA256.Create()) _hash = hash.ComputeHash(stream.ToArray());
                return _hash;
            }
        }
        static SortedDictionary<string, Mod> Local()
        {
            if (_local != null) return _local;
            var mods = new SortedDictionary<string, Mod>(StringComparer.Ordinal);
            // Capture at join time, after chainloading. MVID identifies the loaded build even if a DLL on disk was replaced.
            foreach (var pair in Chainloader.PluginInfos)
            {
                var info = pair.Value;
                if (ReferenceEquals(info.Instance, null)) continue;
                var mod = new Mod { Name = info.Metadata.Name, Version = info.Metadata.Version.ToString(), Build = info.Instance.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("N") };
                Validate(info.Metadata.GUID, mod); mods.Add(info.Metadata.GUID, mod);
            }
            if (mods.Count == 0 || mods.Count > 512 || !mods.ContainsKey(Plugin.GUID)) throw new InvalidDataException("Cannot verify the loaded multiplayer mod list.");
            _local = mods; return mods;
        }
        static void Validate(string id, Mod mod)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 256 || string.IsNullOrEmpty(mod.Name) || mod.Name.Length > 256
                || string.IsNullOrEmpty(mod.Version) || mod.Version.Length > 64 || mod.Build == null || mod.Build.Length != 32)
                throw new InvalidDataException("Invalid multiplayer mod identity.");
        }
        internal static void Write(BinaryWriter writer)
        {
            var mods = Local(); writer.Write((ushort)mods.Count);
            foreach (var pair in mods) { writer.Write(pair.Key); writer.Write(pair.Value.Name); writer.Write(pair.Value.Version); writer.Write(pair.Value.Build); }
        }
        internal static string Check(BinaryReader reader, bool hosting)
        {
            int count = reader.ReadUInt16();
            if (count == 0 || count > 512) throw new InvalidDataException("Invalid multiplayer mod count.");
            var remote = new SortedDictionary<string, Mod>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                var mod = new Mod { Name = reader.ReadString(), Version = reader.ReadString(), Build = reader.ReadString() };
                Validate(id, mod);
                if (remote.ContainsKey(id)) throw new InvalidDataException("Duplicate multiplayer mod identity.");
                remote.Add(id, mod);
            }
            var host = hosting ? Local() : remote; var guest = hosting ? remote : Local();
            foreach (var pair in host)
            {
                Mod other;
                if (!guest.TryGetValue(pair.Key, out other)) return "Missing client mod: " + pair.Value.Name + " (" + pair.Key + "). Host requires " + pair.Value.Version + ". Install the host's mod set.";
                if (pair.Value.Version != other.Version) return "Mod version mismatch: " + pair.Value.Name + " (" + pair.Key + "). Host: " + pair.Value.Version + "; client: " + other.Version + ". Install the host's version.";
            }
            foreach (var pair in guest)
                if (!host.ContainsKey(pair.Key)) return "Extra client mod: " + pair.Value.Name + " (" + pair.Key + ") " + pair.Value.Version + ". It is absent on the host. Use the host's mod set.";
            foreach (var pair in host)
                if (pair.Value.Build != guest[pair.Key].Build) return "Mod build mismatch: " + pair.Value.Name + " (" + pair.Key + ") " + pair.Value.Version + ". The DLL builds differ. Install the host's exact DLL.";
            return null;
        }
    }
}
