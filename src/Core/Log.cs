using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace WildWasteland.Core
{
    /// <summary>
    /// One log for the whole mod. Info is gated by [Debug] VerboseLog (AGENTS.md 5.5): callers must check
    /// <see cref="Verbose"/> before building strings on hot paths. Warnings/errors are always written and rate-limited per message.
    /// </summary>
    public static class Log
    {
        public static ManualLogSource Source;
        public static bool Verbose;

        static readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        const int RepeatLimit = 5;

        public static void Info(string msg) { if (Verbose && Source != null) Source.LogInfo(msg); }
        /// <summary>Always logged (startup summary, migrations, module state changes).</summary>
        public static void Always(string msg) { if (Source != null) Source.LogInfo(msg); }
        public static void Warn(string msg) { if (Source != null && Allow(msg)) Source.LogWarning(msg); }
        public static void Error(string msg) { HostSaveSystem.Fail(msg); if (Source != null && Allow(msg)) Source.LogError(msg); }
        public static void Error(string msg, Exception e) { HostSaveSystem.Fail(msg); if (Source != null && Allow(msg)) Source.LogError(msg + ": " + e); }

        static bool Allow(string msg)
        {
            int n;
            _counts.TryGetValue(msg, out n);
            if (n >= RepeatLimit) return false;
            _counts[msg] = n + 1;
            if (n + 1 == RepeatLimit && Source != null) Source.LogWarning("(further repeats of the previous message are suppressed)");
            return true;
        }

        /// <summary>Called on scene load so a message may appear again in a new session.</summary>
        public static void ResetRateLimits() { _counts.Clear(); }
    }
}
