using System;
using System.Security.Cryptography;

namespace WastelandMPNew
{
    /// <summary>One bounded, token-bound snapshot transfer; no decompression or world mutation during assembly.</summary>
    public sealed class SnapshotAssembler
    {
        readonly byte[] data, digest;
        readonly bool[] received;
        readonly Guid token;
        readonly int chunkBytes;
        readonly double deadline;
        int chunks;
        bool taken, invalid;

        /// <summary>Reserves memory only after admission and validated metadata; defaults to a four MiB ceiling per transfer.</summary>
        public SnapshotAssembler(Guid token, int length, int chunkBytes, byte[] sha256, double deadline, int maximumBytes = 4 * 1024 * 1024)
        {
            if (token == Guid.Empty || length < 1 || length > maximumBytes || chunkBytes < 1 || chunkBytes > 1024
                || sha256 == null || sha256.Length != 32 || double.IsNaN(deadline) || double.IsInfinity(deadline)) throw new ArgumentException("Invalid snapshot metadata");
            this.token = token; this.chunkBytes = chunkBytes; this.deadline = deadline;
            data = new byte[length]; received = new bool[(length - 1) / chunkBytes + 1]; digest = (byte[])sha256.Clone();
        }

        /// <summary>Accepts exact chunk boundaries; conflicting duplicates invalidate the entire transfer.</summary>
        public bool Add(Guid attempt, int offset, byte[] bytes, int start, int count, double now)
        {
            if (taken || invalid || attempt != token || double.IsNaN(now) || now >= deadline || offset < 0 || offset >= data.Length || offset % chunkBytes != 0
                || count != Math.Min(chunkBytes, data.Length - offset) || bytes == null || start < 0 || start > bytes.Length - count) return false;
            int index = offset / chunkBytes;
            if (received[index])
            {
                for (int i = 0; i < count; i++) if (data[offset + i] != bytes[start + i]) { invalid = true; return false; }
                return true;
            }
            Buffer.BlockCopy(bytes, start, data, offset, count); received[index] = true; chunks++; return true;
        }

        /// <summary>Transfers ownership of the assembled bytes once, only after full SHA256 validation.</summary>
        public byte[] Take(double now)
        {
            if (taken || invalid || double.IsNaN(now) || now >= deadline || chunks != received.Length) return null;
            byte[] actual; using (SHA256 sha = SHA256.Create()) actual = sha.ComputeHash(data);
            int diff = 0; for (int i = 0; i < digest.Length; i++) diff |= digest[i] ^ actual[i];
            if (diff != 0) { invalid = true; return null; }
            taken = true; return data;
        }
    }
}
