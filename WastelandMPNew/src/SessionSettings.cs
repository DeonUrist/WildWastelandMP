using System;
using System.Collections.Generic;

namespace WastelandMPNew
{
    /// <summary>Read-through session settings; local preferences never receive host assignments or need rollback writes.</summary>
    public sealed class SessionSettings
    {
        readonly Dictionary<string, string> local;
        readonly HashSet<string> worldKeys;
        Dictionary<string, string> overlay;

        /// <summary>Captures validated local values and the explicit network schema at session start.</summary>
        public SessionSettings(IDictionary<string, string> local, string[] worldKeys)
        {
            if (local == null || worldKeys == null || worldKeys.Length > 4096) throw new ArgumentException("Invalid setting schema");
            this.local = new Dictionary<string, string>(local, StringComparer.Ordinal);
            this.worldKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < worldKeys.Length; i++)
            {
                string key = worldKeys[i];
                if (string.IsNullOrEmpty(key) || key.Length > 256 || !this.local.ContainsKey(key) || !this.worldKeys.Add(key)) throw new ArgumentException("Duplicate or absent world key");
            }
        }

        /// <summary>Installs a complete already type-validated host policy atomically; unknown, duplicate or missing keys reject the policy.</summary>
        public void ApplyHost(IDictionary<string, string> incoming)
        {
            if (incoming == null || incoming.Count != worldKeys.Count) throw new ArgumentException("World schema mismatch");
            Dictionary<string, string> next = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in incoming)
            {
                if (!worldKeys.Contains(pair.Key) || pair.Value == null || pair.Value.Length > 4096) throw new ArgumentException("Invalid world setting");
                next.Add(pair.Key, pair.Value);
            }
            overlay = next;
        }

        /// <summary>Returns a session-effective value; persistent config serialization continues to use the local provider.</summary>
        public string Get(string key)
        {
            string value; if (overlay != null && overlay.TryGetValue(key, out value)) return value;
            return local.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>Updates a local-only preference; world settings remain locked while a policy is installed.</summary>
        public bool SetLocal(string key, string value)
        {
            if (!local.ContainsKey(key) || value == null || value.Length > 4096 || (overlay != null && worldKeys.Contains(key))) return false;
            local[key] = value; return true;
        }

        /// <summary>Releases policy without writing the player's configuration.</summary>
        public void Clear()
        {
            overlay = null;
        }
    }
}
