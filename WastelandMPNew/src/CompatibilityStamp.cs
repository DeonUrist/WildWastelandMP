using System;

namespace WastelandMPNew
{
    /// <summary>Content-derived admission identity; platform identity and install credentials never enter this stamp.</summary>
    public sealed class CompatibilityStamp
    {
        readonly uint schema;
        readonly byte[] game, requiredMods, codecs, generation;

        /// <summary>Captures canonical SHA256 digests; the shell sorts required mod ids and settings keys before hashing.</summary>
        public CompatibilityStamp(uint schema, byte[] game, byte[] requiredMods, byte[] codecs, byte[] generation)
        {
            if (schema == 0) throw new ArgumentOutOfRangeException("schema");
            this.schema = schema; this.game = Digest(game); this.requiredMods = Digest(requiredMods);
            this.codecs = Digest(codecs); this.generation = Digest(generation);
        }

        /// <summary>Returns a bounded user-facing mismatch category, or null for an exact compatible world.</summary>
        public string Mismatch(CompatibilityStamp other)
        {
            if (other == null || schema != other.schema) return "Network schema differs";
            if (!Equal(game, other.game)) return "Game build differs";
            if (!Equal(requiredMods, other.requiredMods)) return "Required mod content differs";
            if (!Equal(codecs, other.codecs)) return "Registered event codecs differ";
            if (!Equal(generation, other.generation)) return "World generation settings differ";
            return null;
        }

        /// <summary>Clones one fixed-size digest to prevent callers mutating an admitted stamp.</summary>
        static byte[] Digest(byte[] value)
        {
            if (value == null || value.Length != 32) throw new ArgumentException("SHA256 digest required"); return (byte[])value.Clone();
        }

        /// <summary>Compares fixed-size digest bytes without an early mismatch exit.</summary>
        static bool Equal(byte[] a, byte[] b)
        {
            int diff = 0; for (int i = 0; i < 32; i++) diff |= a[i] ^ b[i]; return diff == 0;
        }
    }
}
