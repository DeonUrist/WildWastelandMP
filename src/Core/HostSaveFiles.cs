using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;

namespace WildWasteland.Core
{
    // Durable undo journal. No world/sidecar mutation is permitted until every old byte is backed up.
    internal sealed class HostSaveFiles
    {
        internal sealed class Entry { internal string Path, Hash; internal bool Existed; internal long WriteTicks; }
        internal readonly List<Entry> Entries = new List<Entry>();
        internal readonly string DirectoryPath, Slot;
        internal bool Committed;
        internal static string WorldRoot { get { return Path.GetDirectoryName(Path.GetFullPath(new ES3Settings("MP_save-path-probe.es3") { location = ES3.Location.File }.FullPath)); } }
        internal static string Root { get { return Path.Combine(WorldRoot, "WildWastelandMP-Saves"); } }
        internal static string BackupRoot { get { return Path.Combine(Root, "Backups"); } }
        internal static string WorldPath(string file) { if (!MultiplayerHosting.IsSaveFile(file)) throw new IOException("Invalid save identity"); return Path.Combine(WorldRoot, file); }
        internal static string MetaPath(string file) { return WorldPath(file) + ".wwsave"; }
        internal static string ImagePath(string file) { return MetaPath(file) + ".png"; }
        internal HostSaveFiles(string slot, string operation)
        {
            WorldPath(slot); Slot = slot;
            DirectoryPath = Path.Combine(Path.Combine(BackupRoot, slot), DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            WriteDurable(Path.Combine(DirectoryPath, "operation.txt"), Encoding.UTF8.GetBytes(operation));
        }
        HostSaveFiles(string directory, string slot, bool recovered) { DirectoryPath = directory; Slot = slot; }
        internal static ES3Settings Disk(string file) { return new ES3Settings(file) { location = ES3.Location.File }; }
        static string Token(string path)
        {
            string full = Path.GetFullPath(path);
            foreach (string root in new[] { WorldRoot, Path.GetFullPath(Paths.ConfigPath), Path.GetFullPath(Assets.Root) })
            {
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { CheckLinks(full, root); return (root == WorldRoot ? "world/" : root == Path.GetFullPath(Paths.ConfigPath) ? "config/" : "assets/") + full.Substring(prefix.Length).Replace('\\', '/'); }
            }
            throw new IOException("Save participant is outside the save/config directories");
        }
        static string Resolve(string token)
        {
            string root = token.StartsWith("world/", StringComparison.Ordinal) ? WorldRoot : token.StartsWith("config/", StringComparison.Ordinal) ? Path.GetFullPath(Paths.ConfigPath) : token.StartsWith("assets/", StringComparison.Ordinal) ? Path.GetFullPath(Assets.Root) : null;
            if (root == null) throw new IOException("Invalid backup path");
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, token.Substring(token.IndexOf('/') + 1).Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)) throw new IOException("Unsafe backup path");
            CheckLinks(full, root);
            return full;
        }
        static void CheckLinks(string full, string root)
        {
            for (string path = full; path != null && !string.Equals(path, root, StringComparison.OrdinalIgnoreCase); path = Path.GetDirectoryName(path))
                if ((Directory.Exists(path) || File.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Save participant traverses a symbolic link");
        }
        internal static string Hash(string path)
        { using (var input = File.OpenRead(path)) using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(input)); }
        internal static void WriteDurable(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
        }
        // One read of the source (hashed while it is copied) and one read of the target, instead of a copy plus two or three extra
        // full reads. The verification is unchanged: the bytes written must hash like the bytes read.
        internal static string CopyVerified(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string sourceHash;
            using (var input = File.OpenRead(source)) using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None)) using (var sha = SHA256.Create())
            {
                var buffer = new byte[1 << 16]; int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { sha.TransformBlock(buffer, 0, read, null, 0); output.Write(buffer, 0, read); }
                sha.TransformFinalBlock(buffer, 0, 0); sourceHash = Convert.ToBase64String(sha.Hash);
                output.Flush(true);
            }
            if (sourceHash != Hash(target)) throw new IOException("Backup/copy verification failed: " + Path.GetFileName(source));
            return sourceHash;
        }
        internal static void Publish(string source, string target)
        {
            string stage = target + ".ww-publish"; string sourceHash = CopyVerified(source, stage);
            AtomicFile.Replace(stage, target);
            if (sourceHash != Hash(target)) throw new IOException("Published save verification failed");
        }
        internal void Prepare(IEnumerable<string> files)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                string full = Path.GetFullPath(path); if (!seen.Add(full)) continue;
                Token(full); var entry = new Entry { Path = full, Existed = File.Exists(full) };
                if (entry.Existed)
                {
                    // The hash is taken from the bytes that went into the backup copy, and the copy is verified against it.
                    entry.WriteTicks = File.GetLastWriteTimeUtc(full).Ticks; string copy = Path.Combine(DirectoryPath, "old-" + Entries.Count + ".bin"); entry.Hash = CopyVerified(full, copy);
                }
                Entries.Add(entry);
            }
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write("WWMP-SAVE-1"); writer.Write(Slot); writer.Write(Entries.Count);
                    foreach (var entry in Entries) { writer.Write(Token(entry.Path)); writer.Write(entry.Existed); writer.Write(entry.WriteTicks); writer.Write(entry.Hash ?? ""); }
                }
                string journal = Path.Combine(DirectoryPath, "undo.bin"); WriteDurable(journal + ".tmp", stream.ToArray());
            }
            var manifest = new StringBuilder(); for (int i = 0; i < Entries.Count; i++) manifest.Append(i).Append('\t').Append(Entries[i].Existed ? "previous file" : "previously absent").Append('\t').Append(Token(Entries[i].Path)).Append('\n');
            WriteDurable(Path.Combine(DirectoryPath, "files.txt"), Encoding.UTF8.GetBytes(manifest.ToString()));
            // The journal becomes recoverable last. A failed preparation never rolls back a later successful save.
            File.Move(Path.Combine(DirectoryPath, "undo.bin.tmp"), Path.Combine(DirectoryPath, "undo.bin"));
        }
        static void Mark(string path) { WriteDurable(path + ".tmp", new byte[] { 1 }); if (File.Exists(path)) File.Delete(path); File.Move(path + ".tmp", path); }
        internal static bool Marked(string path) { return File.Exists(path) && new FileInfo(path).Length == 1 && File.ReadAllBytes(path)[0] == 1; }
        internal void Commit()
        {
            Mark(Path.Combine(DirectoryPath, "committed")); Committed = true;
            // New-file staging is redundant after verified publication; old-* files remain the backup.
            try { foreach (string path in Directory.GetFiles(DirectoryPath, "new-*.bin")) File.Delete(path); }
            catch (Exception error) { Log.Warn("[System] Retaining redundant save staging: " + error.Message); }
        }
        internal void Rollback(bool clearCache = true)
        {
            // Verify the complete backup before restoring any participant.
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Existed && Hash(Path.Combine(DirectoryPath, "old-" + i + ".bin")) != Entries[i].Hash) throw new IOException("Backup verification failed; recovery retained");
            // Discovery captured all pre-existing slot companions. A new matching file belongs to the interrupted write.
            // This also handles an optional module creating its first sidecar after the undo journal was prepared.
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var entry in Entries) known.Add(entry.Path);
            foreach (string path in Participants(Slot)) if (File.Exists(path) && known.Add(Path.GetFullPath(path))) Entries.Add(new Entry { Path = Path.GetFullPath(path), Existed = false });
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.Existed) { Publish(Path.Combine(DirectoryPath, "old-" + i + ".bin"), entry.Path); File.SetLastWriteTimeUtc(entry.Path, new DateTime(entry.WriteTicks, DateTimeKind.Utc)); }
                else if (File.Exists(entry.Path)) File.Delete(entry.Path);
                foreach (string suffix in new[] { ".tmp", ".tmp.bak", ".ww-publish" }) if (File.Exists(entry.Path + suffix)) File.Delete(entry.Path + suffix);
            }
            if (clearCache) ES3File.cachedFiles.Remove(Slot);
            Mark(Path.Combine(DirectoryPath, "recovered"));
        }
        internal static HostSaveFiles Read(string directory)
        {
            using (var reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "undo.bin"))))
            {
                if (reader.ReadString() != "WWMP-SAVE-1") throw new IOException("Unknown save recovery journal");
                string slot = reader.ReadString(); WorldPath(slot); var tx = new HostSaveFiles(directory, slot, true);
                if (Path.GetFileName(Path.GetDirectoryName(directory)) != slot) throw new IOException("Backup folder and save identity disagree");
                int count = reader.ReadInt32(); if (count < 1 || count > 10000) throw new IOException("Invalid participant count");
                for (int i = 0; i < count; i++)
                {
                    string path = Resolve(reader.ReadString());
                    if (!Matches(Path.GetFileName(path), slot)) throw new IOException("Backup participant does not belong to this save");
                    var entry = new Entry { Path = path, Existed = reader.ReadBoolean(), WriteTicks = reader.ReadInt64(), Hash = reader.ReadString() };
                    if (entry.Existed && (entry.WriteTicks < DateTime.MinValue.Ticks || entry.WriteTicks > DateTime.MaxValue.Ticks || Convert.FromBase64String(entry.Hash).Length != 32)) throw new IOException("Invalid backup timestamp/hash");
                    tx.Entries.Add(entry);
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new IOException("Invalid recovery journal length");
                return tx;
            }
        }
        internal static void Recover()
        {
            if (!Directory.Exists(BackupRoot)) return;
            var journals = Directory.GetFiles(BackupRoot, "undo.bin", SearchOption.AllDirectories); Array.Sort(journals, StringComparer.Ordinal);
            foreach (string journal in journals)
            {
                string directory = Path.GetDirectoryName(journal);
                if (Marked(Path.Combine(directory, "committed")) || Marked(Path.Combine(directory, "recovered"))) continue;
                Read(directory).Rollback(); Log.Always("[System] Interrupted save recovered: " + directory);
            }
        }
        static bool Matches(string name, string file)
        {
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ww-publish", StringComparison.OrdinalIgnoreCase)) return false;
            string stem = Path.GetFileNameWithoutExtension(file);
            return name == file || name.StartsWith(file + ".", StringComparison.OrdinalIgnoreCase) || name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)
                || name == LegacyQuestName(file) + ".txt" || name.StartsWith(stem + "_seed", StringComparison.OrdinalIgnoreCase);
        }
        static string LegacyQuestName(string file) { var name = file.ToCharArray(); for (int i = 0; i < name.Length; i++) if (!char.IsLetterOrDigit(name[i])) name[i] = '_'; return new string(name); }
        static void Discover(string directory, string file, bool recurse, List<string> files)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string path in Directory.GetFiles(directory)) if (Matches(Path.GetFileName(path), file)) files.Add(path);
            if (recurse) foreach (string child in Directory.GetDirectories(directory))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) Discover(child, file, true, files);
        }
        internal static List<string> Participants(string file)
        {
            WorldPath(file); var result = new List<string> { WorldPath(file), MetaPath(file), ImagePath(file) };
            Discover(WorldRoot, file, false, result);
            Discover(Assets.ConfigDir, file, true, result);
            Discover(Path.Combine(Assets.ModuleDir("NPC"), "Saves"), file, false, result);
            result.Add(Path.Combine(Path.Combine(Assets.ModuleDir("NPC"), "Saves"), file + ".senses.txt"));
            foreach (string directory in new[] { "Apocapatrol/Saves", "NPCAI/Saves", "Apocaraider/Saves", "Apocaunloader/Saves", "Apocapocket", "ApocalypterQuests_saves", "ImmersiveMap/slots" })
                Discover(Path.Combine(Paths.ConfigPath, directory.Replace('/', Path.DirectorySeparatorChar)), file, true, result);
            string stem = Path.GetFileNameWithoutExtension(file);
            foreach (string suffix in new[] { ".players.txt", ".player-checkpoint.txt" }) result.Add(Path.Combine(WorldRoot, stem + suffix));
            for (int i = 1; i <= 10; i++) { result.Add(WorldPath(file) + ".bak" + i); result.Add(Path.Combine(WorldRoot, stem + ".players.txt.bak" + i)); }
            foreach (string part in new[] { "NPC/Saves/" + file + ".senses.txt", "Patrols/Saves/" + file + ".bin", "Patrols/Saves/" + file + ".bin.bak", "POI/Saves/" + file + ".poi.txt", "CleanHUD/Saves/" + file + ".fog.bin", "CleanHUD/Saves/" + file + ".shared.fog.bin", "CleanHUD/Saves/" + file + ".locations.txt", "Quests/Saves/" + file + ".crew.txt" })
                result.Add(Path.Combine(Assets.ConfigDir, part.Replace('/', Path.DirectorySeparatorChar)));
            return result;
        }
        internal static string Remap(string path, string from, string to)
        {
            string name = Path.GetFileName(path), stem = Path.GetFileNameWithoutExtension(from);
            if (name.StartsWith(from, StringComparison.OrdinalIgnoreCase)) name = to + name.Substring(from.Length);
            else if (name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(to) + name.Substring(stem.Length);
            else if (name == LegacyQuestName(from) + ".txt") name = LegacyQuestName(to) + ".txt";
            else if (name.StartsWith(stem + "_seed", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(to) + name.Substring(stem.Length);
            else throw new IOException("Unknown save companion filename");
            return Path.Combine(Path.GetDirectoryName(path), name);
        }
        internal static void Prune(string slot, int keep)
        {
            string directory = Path.Combine(BackupRoot, slot); if (!Directory.Exists(directory)) return;
            var dirs = Directory.GetDirectories(directory); Array.Sort(dirs, StringComparer.Ordinal); int retained = 0;
            for (int i = dirs.Length - 1; i >= 0; i--)
            {
                if (!Marked(Path.Combine(dirs[i], "committed"))) continue; // unresolved/recovered journals are never pruned
                if (retained++ < keep) continue;
                string full = Path.GetFullPath(dirs[i]); string prefix = Path.GetFullPath(BackupRoot) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe backup retention path");
                // A deleted snapshot is the only surviving copy; retain it until the user removes its backup explicitly.
                if (File.ReadAllText(Path.Combine(full, "operation.txt")) == "delete") continue;
                Directory.Delete(full, true);
            }
        }
    }
}
