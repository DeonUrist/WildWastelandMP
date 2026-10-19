using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerHosting
    {
        internal sealed class SaveChoice { internal string File, Name; internal bool Automatic; }
        static Func<IList> _worlds;
        static Func<string, int, object> _create;
        static Func<object, string> _file, _name;
        static Func<string, string> _path;
        static Func<string, int, bool> _launch;
        internal static bool Pending { get; private set; }
        internal static string Error;
        internal static readonly string[] StarterCars = { "RANDOM", "Poloska", "Junker", "PipeRat", "Crossbreed", "TinyTyrant", "Halfbreed" };
        static string _starterCar;
        internal static void Install()
        {
            var store = Multiplayer.Type("Worlds.WorldStore"); var world = Multiplayer.Type("Worlds.MpWorld");
            _worlds = MultiplayerAccess.Call<Func<IList>>(store, "Load", Type.EmptyTypes);
            _create = MultiplayerAccess.Call<Func<string, int, object>>(store, "Create", typeof(string), typeof(int));
            _file = MultiplayerAccess.Get<string>(world, "SaveFile"); _name = MultiplayerAccess.Get<string>(world, "Name");
            _path = MultiplayerAccess.Call<Func<string, string>>(store, "PathOf", typeof(string));
            _launch = MultiplayerAccess.Call<Func<string, int, bool>>(Multiplayer.Type("Game.GameLauncher"), "Launch", typeof(string), typeof(int));
            Hooks.CreateObjectEnter.Prefix(null, BeforeStarterCar);
        }
        static bool BeforeStarterCar(HutongGames.PlayMaker.Actions.CreateObject action)
        {
            if (!Pending || string.IsNullOrEmpty(_starterCar)) return true;
            var fsm = action.Fsm;
            if (fsm.Name != "ItemSpawner" || fsm.GameObject.name != "CarSpawn_StarterBase" || action.State.Name != "spawn") return true;
            var parent = fsm.GameObject.transform.parent;
            if (parent == null || parent.name != "Starter_Area") return true;
            var list = fsm.GameObject.GetComponent<PlayMakerArrayListProxy>();
            if (list != null && list.referenceName == "CarSpawn")
                for (int i = 0; i < list.arrayList.Count; i++)
                {
                    var prefab = list.arrayList[i] as GameObject;
                    if (prefab == null || prefab.name != _starterCar) continue;
                    action.gameObject.Value = prefab; fsm.Variables.FindFsmInt("index").Value = i;
                    Log.Always("[Multiplayer] Starter vehicle selected: " + _starterCar); _starterCar = null; return true;
                }
            Log.Warn("[Multiplayer] Selected starter vehicle is unavailable: " + _starterCar); _starterCar = null; return true;
        }
        internal static bool IsSaveFile(string name)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || !name.EndsWith(".es3", StringComparison.Ordinal) || name == "MP_client.es3") return false;
            if (name.StartsWith("SaveGame", StringComparison.Ordinal))
            { int slot; return int.TryParse(name.Substring(8, name.Length - 12), out slot) && slot >= 1 && slot <= 10; }
            if (!name.StartsWith("MP_", StringComparison.Ordinal) || name.Length <= 7) return false;
            for (int i = 3; i < name.Length - 4; i++) if (!char.IsLetterOrDigit(name[i]) && name[i] != '-') return false;
            return true;
        }
        internal static List<SaveChoice> Saves()
        {
            var result = new List<SaveChoice>(); var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (object world in _worlds()) names[_file(world)] = _name(world);
            string directory = Path.GetDirectoryName(_path("SaveGame1.es3"));
            if (!Directory.Exists(directory)) return result;
            var files = Directory.GetFiles(directory, "*.es3"); Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                string file = Path.GetFileName(path), name;
                if (!IsSaveFile(file) || new FileInfo(path).Length <= 1024) continue;
                if (!names.TryGetValue(file, out name)) name = Path.GetFileNameWithoutExtension(file);
                var info = HostSaveSystem.Info(file, name);
                result.Add(new SaveChoice { File = file, Name = info.Name, Automatic = info.Automatic });
            }
            return result;
        }
        internal static string CurrentFile
        {
            get
            {
                var fsm = GameRefs.SaveLoadGame; var file = fsm != null ? fsm.FsmVariables.FindFsmString("SaveFile") : null;
                return file != null ? Path.GetFileName(file.Value) : SaveBus.Slot;
            }
        }
        internal static string Validate(string name, string file, string seed)
        {
            if (!Multiplayer.CanHost) return "Hosting is unavailable while a session, save or load is active.";
            if (!HostSaveSystem.Ready) return HostSaveSystem.RecoveryError ?? "The server save system is not ready.";
            if (HostSaveSystem.RecoveryError != null) return HostSaveSystem.RecoveryError;
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return "Enter a world name.";
            foreach (char ch in name) if (char.IsControl(ch) || ch == '<' || ch == '>') return "Use a world name without control characters or markup.";
            int value;
            if (Multiplayer.InMainMenu && file == null && !int.TryParse(seed, out value)) return "Enter a whole-number seed.";
            if (file != null && (!IsSaveFile(file) || !File.Exists(_path(file)))) return "That save is no longer available.";
            return null;
        }
        internal static void Start(string name, string file, string seed, bool steam, string starterCar)
        {
            Error = Validate(name, file, seed);
            if (Error != null) { Log.Warn("[Multiplayer] " + Error); return; }
            if (!Multiplayer.InMainMenu) { Multiplayer.HostCurrentWorld(steam, name.Trim()); return; }
            _starterCar = file == null && starterCar != "RANDOM" ? starterCar : null;
            Pending = true; Scheduler.StartCoroutine(Launch(name.Trim(), file, seed, steam));
        }
        static IEnumerator Launch(string name, string file, string seed, bool steam)
        {
            try
            {
                int value = 0; if (file == null) { int.TryParse(seed, out value); file = _file(_create(name, value)); }
                if (!_launch(file, value)) { Error = "Could not start the world; check the console."; Log.Warn("[Multiplayer] " + Error); yield break; }
                while (Multiplayer.InMainMenu || GameRefs.SaveLoadState != "isPlay") yield return null;
                Multiplayer.HostCurrentWorld(steam, name);
                if (!Multiplayer.Hosting) { Error = "Could not open the host transport; check the console."; Log.Warn("[Multiplayer] " + Error); }
            }
            finally { _starterCar = null; Pending = false; }
        }
    }
}
