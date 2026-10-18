using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static partial class MultiplayerRecovery
    {
        const string Header = "WWMP-PLAYERS-1";
        static Func<object, IDictionary> _records;
        static Action<string[]> _readLine;
        static Func<string, string> _path;
        static Func<object, string> _name, _seen, _prefab, _itemName, _signature, _slot;
        static Func<object, bool> _hasPosition;
        static Func<object, Vector3> _position;
        static Func<object, float> _yaw;
        static Func<object, IList> _items;
        static Func<object, byte[]> _vars;
        static Action<object, string> _setName;
        static Action<object, Dictionary<string, float>> _setStats;
        static Action<object, bool> _setHasPosition;
        static Action<object, Vector3> _setPosition;
        static Action<object, float> _setYaw;
        static Func<Vector3, Vector3> _global;
        static Func<List<KeyValuePair<int, string>>> _inventory;
        static Action<object, ulong, List<KeyValuePair<int, string>>> _setInventory;
        static object _hostRecord;
        static string _file, _worldFile, _snapshotId;
        static int _openedGeneration = -1;
        static string[] _closedLines;
        static readonly object WriteLock = new object();
        static readonly Dictionary<string, string[]> Writes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        static bool _writing, _writeSuccess;
        static string _writeError;

        static void StorageInstall()
        {
            var registry = Multiplayer.Type("Worlds.PlayerRegistry"); var record = Multiplayer.Type("Worlds.PlayerRecord");
            var item = Multiplayer.Type("Worlds.SavedItem"); var world = Multiplayer.Type("Net.WorldObjects");
            _records = MultiplayerAccess.Get<IDictionary>(registry, "records");
            _readLine = MultiplayerAccess.Call<Action<string[]>>(registry, "ReadLine", typeof(string[]));
            _path = MultiplayerAccess.Call<Func<string, string>>(Multiplayer.Type("Worlds.WorldStore"), "PathOf", typeof(string));
            _name = MultiplayerAccess.Get<string>(record, "Name"); _seen = MultiplayerAccess.Get<string>(record, "LastSeen");
            _hasPosition = MultiplayerAccess.Get<bool>(record, "HasPosition"); _position = MultiplayerAccess.Get<Vector3>(record, "Position"); _yaw = MultiplayerAccess.Get<float>(record, "Yaw");
            _items = MultiplayerAccess.Get<IList>(record, "Items");
            _slot = MultiplayerAccess.Get<string>(item, "Slot"); _prefab = MultiplayerAccess.Get<string>(item, "Prefab");
            _itemName = MultiplayerAccess.Get<string>(item, "Name"); _signature = MultiplayerAccess.Get<string>(item, "Signature"); _vars = MultiplayerAccess.Get<byte[]>(item, "Vars");
            _setName = MultiplayerAccess.Set<string>(record, "Name"); _setStats = MultiplayerAccess.Set<Dictionary<string, float>>(record, "Stats");
            _setHasPosition = MultiplayerAccess.Set<bool>(record, "HasPosition"); _setPosition = MultiplayerAccess.Set<Vector3>(record, "Position"); _setYaw = MultiplayerAccess.Set<float>(record, "Yaw");
            _global = MultiplayerAccess.Call<Func<Vector3, Vector3>>(Multiplayer.Type("Game.WorldOrigin"), "ToGlobal", typeof(Vector3));
            _inventory = MultiplayerAccess.Call<Func<List<KeyValuePair<int, string>>>>(world, "LocalInventory", Type.EmptyTypes);
            _setInventory = MultiplayerAccess.Call<Action<object, ulong, List<KeyValuePair<int, string>>>>(world, "SetInventory", record, typeof(ulong), typeof(List<KeyValuePair<int, string>>));
            _hostRecord = Activator.CreateInstance(record, true); MultiplayerAccess.Set<string>(record, "Key")(_hostRecord, "host");
            Patch(registry, "Open", null, "Opened");
            Patch(registry, "Close", "Closing", null);
        }
        static ES3Settings Settings(string file, ES3.Location location)
        { var settings = new ES3Settings(file); settings.location = location; return settings; }
        static void Opened(string __0)
        {
            if (!Multiplayer.Hosting || !MultiplayerHosting.IsSaveFile(__0)) return;
            try { OpenRecords(__0); }
            catch (Exception error)
            {
                _file = null; _closedLines = null;
                Log.Error("[Multiplayer] Could not read this world's player snapshot; hosting cancelled to preserve player data", error);
                _end("Could not read the world's player snapshot. Check the log.");
            }
        }
        static void OpenRecords(string file)
        {
            bool sameWorld = _worldFile == file && _openedGeneration == SaveBus.Generation && _closedLines != null;
            _worldFile = file; _file = _path(Path.ChangeExtension(file, ".player-checkpoint.txt"));
            _openedGeneration = SaveBus.Generation; Dirty.Clear(); Clean.Clear(); Waiting.Clear(); _collecting = false;
            string[] latest = null;
            try { if (File.Exists(_file)) latest = File.ReadAllLines(_file); }
            catch (IOException error) { Log.Warn("[Multiplayer] Previous checkpoint status unavailable: " + error.Message); }
            bool uncleanHost = false;
            if (latest != null) foreach (string line in latest) if (line == "dirty\thost") uncleanHost = true;
            string[] snapshot = null;
            if (sameWorld) snapshot = _closedLines;
            else if (File.Exists(_path(file)))
            {
                var settings = Settings(file, ES3.Location.File);
                if (ES3.KeyExists(SnapshotKey, settings)) snapshot = ES3.Load<string[]>(SnapshotKey, settings);
            }
            if (snapshot != null)
            {
                int seed; string[] header = snapshot.Length != 0 ? snapshot[0].Split('\t') : new string[0];
                if (header.Length != 4 || header[0] != Header || !int.TryParse(header[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                    throw new InvalidDataException("Invalid player snapshot header");
                if (seed != WorldSeed())
                {
                    snapshot = null; _records(null).Clear();
                    Log.Always("[Multiplayer] Player recovery: new world seed; old slot's player records were not imported");
                }
            }
            if (snapshot != null)
            {
                RestoreRecords(snapshot);
                if (!sameWorld)
                {
                    foreach (DictionaryEntry entry in _records(null)) if ((string)entry.Key != "host") _stats(entry.Value)[RecoveryKey] = 2;
                    _warning = uncleanHost ? "Unclean host exit: player state restored to this world save's snapshot." : "Player state restored to this world save's snapshot.";
                    Log.Always("[Multiplayer] Player recovery: world snapshot restored for " + _records(null).Count + " known players (including offline players)");
                }
            }
            else
            {
                _snapshotId = "legacy";
                Log.Always("[Multiplayer] Player recovery: legacy world has no embedded player snapshot; save this world once to establish rollback protection");
            }
            _closedLines = null;
            // Native world loading owns host gear and vitals; starting a host in a live world must not roll them back again.
            Dirty.Add("host"); _next = Time.unscaledTime + 300f;
            Checkpoint();
        }
        static void Closing()
        {
            if (_file == null) return;
            try
            {
                if (Multiplayer.Hosting && Multiplayer.WorldReady)
                {
                    Dirty.Remove("host");
                    _closedLines = CaptureLines(); QueueWrite(_file, _closedLines);
                }
            }
            catch (Exception error) { Log.Error("[Multiplayer] Final player checkpoint capture failed", error); }
            finally { _file = null; _collecting = false; Waiting.Clear(); }
        }
        static string Number(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        static int WorldSeed()
        {
            var fsm = GameRefs.SaveLoadGame; var seed = fsm != null ? fsm.FsmVariables.FindFsmInt("seed") : null;
            if (seed == null) throw new InvalidOperationException("Loaded world seed is unavailable");
            return seed.Value;
        }
        static string Field(string value) { return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
        static void AddRecord(List<string> lines, object record)
        {
            var stats = new List<string>();
            foreach (var pair in _stats(record)) stats.Add(pair.Key + "=" + Number(pair.Value));
            var position = _position(record); string key = Field(_key(record));
            lines.Add(string.Join("\t", new[] { "player", key, Field(_name(record)), Field(_seen(record)), _hasPosition(record) ? "1" : "0",
                Number(position.x), Number(position.y), Number(position.z), Number(_yaw(record)), string.Join(";", stats.ToArray()) }));
            foreach (object item in _items(record))
                lines.Add(string.Join("\t", new[] { "item", key, Field(_slot(item)), Field(_prefab(item)), Field(_itemName(item)), Field(_signature(item)), Convert.ToBase64String(_vars(item)) }));
        }
        static string[] CaptureLines()
        {
            _refresh();
            MultiplayerSleepers.StoreAll();
            var lines = new List<string>(); lines.Add(Header + "\t" + _snapshotId + "\t" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\t" + WorldSeed().ToString(CultureInfo.InvariantCulture));
            foreach (DictionaryEntry entry in _records(null)) if ((string)entry.Key != "host") AddRecord(lines, entry.Value);
            if (GameRefs.Player != null)
            {
                var stats = _capture(); _setStats(_hostRecord, stats); _setName(_hostRecord, Multiplayer.PlayerName);
                float health; _setHasPosition(_hostRecord, !stats.TryGetValue("p.Health", out health) || health >= 1);
                _setPosition(_hostRecord, _global(GameRefs.Player.transform.position)); _setYaw(_hostRecord, GameRefs.Player.transform.eulerAngles.y);
                _setInventory(_hostRecord, 0, _inventory()); AddRecord(lines, _hostRecord);
            }
            foreach (string key in Dirty) lines.Add("dirty\t" + Field(key));
            return lines.ToArray();
        }
        static void RestoreRecords(string[] lines)
        {
            if (lines.Length == 0 || !lines[0].StartsWith(Header + "\t", StringComparison.Ordinal)) throw new InvalidDataException("Invalid player snapshot header");
            var records = _records(null); var before = new Hashtable(records);
            try
            {
                records.Clear(); foreach (string line in lines) _readLine(line.Split('\t'));
                _snapshotId = lines[0].Split('\t')[1];
            }
            catch { records.Clear(); foreach (DictionaryEntry entry in before) records.Add(entry.Key, entry.Value); throw; }
        }
        internal static void SaveSnapshot(string file)
        {
            _snapshotId = Guid.NewGuid().ToString("N");
            ES3.Save<string[]>(SnapshotKey, CaptureLines(), Settings(file, ES3.Location.Cache));
            Log.Always("[Multiplayer] Player recovery: snapshot attached to world save (all known players)");
        }
        internal static bool Checkpoint()
        {
            if (_file == null || !Multiplayer.Hosting || !Multiplayer.WorldReady) return false;
            try { QueueWrite(_file, CaptureLines()); return true; }
            catch (Exception error) { Log.Error("[Multiplayer] Player checkpoint capture failed", error); _warning = "Player checkpoint could not be captured. Check the log."; return false; }
        }
        // Only immutable strings cross this boundary; workers never read Unity objects or the native registry.
        static void QueueWrite(string path, string[] lines)
        {
            lock (WriteLock)
            {
                Writes[path] = lines;
                if (_writing) return;
                _writing = true; ThreadPool.QueueUserWorkItem(WriteFiles);
            }
        }
        static void WriteFiles(object ignored)
        {
            while (true)
            {
                string path = null; string[] lines = null;
                lock (WriteLock)
                {
                    foreach (var pair in Writes) { path = pair.Key; lines = pair.Value; break; }
                    if (path == null) { _writing = false; return; }
                    Writes.Remove(path);
                }
                try
                {
                    string temp = path + ".tmp"; File.WriteAllLines(temp, lines);
                    if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
                    lock (WriteLock) _writeSuccess = true;
                }
                catch (Exception error) { lock (WriteLock) _writeError = error.Message; }
            }
        }
        static string TakeWriteError()
        { lock (WriteLock) { string error = _writeError; _writeError = null; return error; } }
        static bool TakeWriteSuccess()
        { lock (WriteLock) { bool success = _writeSuccess; _writeSuccess = false; return success; } }
        static bool WriteBusy() { lock (WriteLock) return _writing; }
    }
}
