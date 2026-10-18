using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildWasteland.Core
{
    /// <summary>
    /// All WW keyboard hotkeys (AGENTS.md 5.5 InputBus). Entries are InputSystem <see cref="Key"/> config entries in the
    /// main cfg [Keys] section so Apocasetter (F6) can rebind them and show conflicts. Polled once per frame in EarlyUpdate;
    /// nothing fires while Apocasetter's window blocks input. Game-owned keys (F2 heal, F9 hide UI, F11 god mode) are reported as conflicts.
    /// The PlayMaker button hooks (GetButtonDown etc.) are in <see cref="Hooks"/>.
    /// </summary>
    public static class Hotkeys
    {
        public sealed class Binding
        {
            public IModule Owner; public ConfigEntry<Key> Entry; public Action Pressed; public Func<bool> When; public bool AllowWhilePaused;
        }
        static readonly List<Binding> _b = new List<Binding>();
        static readonly Key[] GameKeys = { Key.F2, Key.F9, Key.F11 };

        /// <param name="when">optional extra gate (e.g. InGame); null = always.</param>
        /// <param name="allowWhilePaused">fire even when timeScale == 0 (for closing tool windows).</param>
        public static Binding Register(IModule owner, ConfigEntry<Key> entry, Action pressed, Func<bool> when = null, bool allowWhilePaused = false)
        {
            var b = new Binding { Owner = owner, Entry = entry, Pressed = pressed, When = when, AllowWhilePaused = allowWhilePaused };
            _b.Add(b);
            return b;
        }
        public static void RemoveAll(IModule owner) { for (int i = _b.Count - 1; i >= 0; i--) if (_b[i].Owner == owner) _b.RemoveAt(i); }

        public static bool Pressed(Key k)
        {
            if (k == Key.None) return false;
            var kb = Keyboard.current;
            try { return kb != null && kb[k].wasPressedThisFrame; } catch { return false; }
        }
        public static bool Held(Key k)
        {
            if (k == Key.None) return false;
            var kb = Keyboard.current;
            try { return kb != null && kb[k].isPressed; } catch { return false; }
        }

        internal static void Tick()
        {
            if (_b.Count == 0 || !Application.isFocused || Multiplayer.Typing || MultiplayerSaving.Frozen) return;
            bool blocked = ApocasetterBridge.InputBlocked;
            for (int i = 0; i < _b.Count; i++)
            {
                var b = _b[i];
                if (!MultiplayerSettings.CanEdit(b.Entry)) continue;
                var k = b.Entry.Value;
                if (k == Key.None || !Pressed(k)) continue;
                if (blocked && !(b.AllowWhilePaused && UI.EditorSession.Busy)) continue;
                if (!b.AllowWhilePaused && Time.timeScale <= 0f) continue;
                if (b.When != null && !b.When()) continue;
                try { b.Pressed(); } catch (Exception e) { ModuleHost.ReportHookException(b.Owner, "hotkey " + b.Entry.Definition.Key, e); }
            }
        }

        /// <summary>Logs duplicate WW bindings and bindings on game keys. Called after Init and on SettingChanged.</summary>
        public static void CheckConflicts()
        {
            var seen = new Dictionary<Key, string>();
            foreach (var b in _b)
            {
                var k = b.Entry.Value; if (k == Key.None) continue;
                string name = b.Entry.Definition.Key, other;
                if (Array.IndexOf(GameKeys, k) >= 0) Log.Warn("[Keys] " + name + " = " + k + " is a game key (F2 heal / F9 hide UI / F11 god mode)");
                if (k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LeftCtrl || k == Key.RightCtrl) continue;   // modifiers are shared by design
                if (seen.TryGetValue(k, out other) && other != name) Log.Warn("[Keys] " + name + " and " + other + " both use " + k);
                else seen[k] = name;
            }
        }
    }
}
