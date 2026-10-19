using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WildWasteland.Core
{
    // Opt-in local launcher. Ordinary Steam/menu sessions retain their normal paths and identity.
    internal static class MultiplayerLaunch
    {
        static string _role, _saves, _world, _saveFile;
        static Func<bool> _menu;
        internal static void Install()
        {
            _role = Environment.GetEnvironmentVariable("WWMP_ROLE");
            if (_role != "host" && _role != "client" && _role != "client2") return;
            if (!Multiplayer.Present) throw new InvalidOperationException("WildWastelandMP launcher requires ApocalypterMP.dll");
            _saves = Environment.GetEnvironmentVariable("WWMP_SAVES");
            _world = Environment.GetEnvironmentVariable("WWMP_WORLD") ?? "WWMP_Local";
            if (string.IsNullOrEmpty(_saves) || !Path.IsPathRooted(_saves) || _world.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("Invalid WildWastelandMP launcher paths");
            // SaveBus and the guest sidecar bridge accept native MP_ identities, including '-' but no extra '_'.
            string identity = _world.StartsWith("MP_", StringComparison.Ordinal) ? _world.Substring(3) : _world;
            if (identity.Length < 1 || identity.Length > 80) throw new InvalidOperationException("Invalid local world name");
            var name = identity.ToCharArray(); for (int i = 0; i < name.Length; i++) if (!char.IsLetterOrDigit(name[i]) && name[i] != '-') name[i] = '-';
            _saveFile = "MP_" + new string(name) + ".es3";
            Directory.CreateDirectory(_saves);
            string legacy = Path.Combine(_saves, _world + ".es3"), canonical = Path.Combine(_saves, _saveFile);
            if (legacy != canonical && File.Exists(legacy) && !File.Exists(canonical))
            {
                File.Copy(legacy, canonical);
                string players = Path.ChangeExtension(legacy, ".players.txt"), copy = Path.ChangeExtension(canonical, ".players.txt");
                if (File.Exists(players) && !File.Exists(copy)) File.Copy(players, copy);
                Log.Always("[MP launcher] Copied the first launcher save to native MP identity " + _saveFile + "; original retained");
            }
            var owner = Multiplayer.PatchOwner;
            Patch(owner, AccessTools.PropertyGetter(typeof(ES3Settings), "FullPath"), "ES3Path");
            Patch(owner, AccessTools.Method(Multiplayer.Type("Worlds.WorldStore"), "PathOf"), "SavePath");
            Patch(owner, AccessTools.PropertyGetter(typeof(Modules.CleanHUD.Display), "PathXml"), "XmlPath");
            Patch(owner, AccessTools.Method(typeof(Modules.CleanHUD.Menu), "SaveFile"), "SavePath");
            if (!Patches.Patch(owner, AccessTools.Method(typeof(AkshayDhotre.GraphicSettingsMenu.GraphicSettingSaveManager), "Awake"), prefix: AccessTools.Method(typeof(MultiplayerLaunch), "GraphicsPath"))
                || !Patches.Patch(owner, AccessTools.Method(Multiplayer.Type("Net.NetSession"), "LocalKey"), prefix: AccessTools.Method(typeof(MultiplayerLaunch), "PlayerKey"))
                || !Patches.Patch(owner, AccessTools.Method(Multiplayer.Type("Net.NetSession"), "LocalPlayerName"), prefix: AccessTools.Method(typeof(MultiplayerLaunch), "PlayerName")))
                throw new InvalidOperationException("WildWastelandMP launcher hooks failed");
            var launcher = Multiplayer.Type("Game.GameLauncher");
            _menu = MultiplayerAccess.Call<Func<bool>>(launcher, "get_InMainMenu", Type.EmptyTypes);
            MultiplayerAccess.Get<ConfigEntry<string>>(Multiplayer.Type("Plugin"), "UdpAddress")(null).Value = Environment.GetEnvironmentVariable("WWMP_ADDRESS") ?? "127.0.0.1";
            int port; if (!int.TryParse(Environment.GetEnvironmentVariable("WWMP_PORT"), out port)) port = 27861;
            MultiplayerAccess.Get<ConfigEntry<int>>(Multiplayer.Type("Plugin"), "UdpPort")(null).Value = port;
            Scheduler.StartCoroutine(Start());
            Log.Always("[MP launcher] " + _role + "; saves " + _saves);
        }
        static void Patch(IModule owner, System.Reflection.MethodInfo method, string postfix)
        { if (!Patches.Patch(owner, method, postfix: AccessTools.Method(typeof(MultiplayerLaunch), postfix))) throw new InvalidOperationException("Launcher path hook failed: " + postfix); }
        static void SavePath(ref string __result) { if (!string.IsNullOrEmpty(__result)) __result = Path.Combine(_saves, Path.GetFileName(__result)); }
        static void ES3Path(ES3Settings __instance, ref string __result) { if (__instance.location == ES3.Location.File) SavePath(ref __result); }
        static void XmlPath(ref string __result) { __result = Path.Combine(_saves, "Settings.xml"); }
        static void GraphicsPath(AkshayDhotre.GraphicSettingsMenu.GraphicSettingSaveManager __instance) { __instance.filePath = _saves.Replace('\\', '/') + "/"; }
        static bool PlayerKey(ref string __result) { __result = "WWMP-local-" + _role; return false; }
        static bool PlayerName(ref string __result) { __result = _role == "host" ? "Host" : _role == "client2" ? "Client 2" : "Client"; return false; }
        static IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            while (!_menu()) yield return null;
            SetTitle();
            Log.Always("[MP launcher] " + _role + " ready at the title menu; choose HOST or JOIN manually");
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SetWindowText(IntPtr window, string text);
        static void SetTitle()
        {
            try { using (var process = System.Diagnostics.Process.GetCurrentProcess()) SetWindowText(process.MainWindowHandle, "WildWastelandMP - " + _role.ToUpperInvariant()); }
            catch (Exception e) { Log.Warn("[MP launcher] Window label: " + e.Message); }
        }
    }
}
