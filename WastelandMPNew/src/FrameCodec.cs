using System;

namespace WastelandMPNew
{
    /// <summary>Small fixed envelope unrelated to ApocalypterMP protocol 21 or WW extension packets.</summary>
    public static class FrameCodec
    {
        /// <summary>Envelope bytes before the payload.</summary>
        public const int HeaderBytes = 28;
        /// <summary>Datagram cap leaves space for the authenticated UDP wrapper below typical MTUs.</summary>
        public const int MaxBytes = 1200;
        /// <summary>Independent schema; incompatible changes require a new version.</summary>
        public const ushort Version = 1;
        const uint Magic = 0x324E5757;

        /// <summary>Writes a frame into a reusable buffer; no reflection, streams or temporary arrays.</summary>
        public static int Write(byte[] target, ushort kind, ulong epoch, ulong sequence, Lane lane, bool ack, byte[] body, int offset, int count)
        {
            if (target == null || target.Length < HeaderBytes + count || count < 0 || count > MaxBytes - HeaderBytes
                || epoch == 0 || sequence == 0 || (byte)lane > 3 || (ack && count != 0))
                throw new ArgumentException("Invalid frame bounds or identity");
            if (count > 0 && (body == null || offset < 0 || offset > body.Length - count)) throw new ArgumentException("Invalid payload");
            Put(target, 0, Magic, 4); Put(target, 4, Version, 2); Put(target, 6, kind, 2);
            Put(target, 8, epoch, 8); Put(target, 16, sequence, 8);
            target[24] = (byte)lane; target[25] = ack ? (byte)1 : (byte)0; Put(target, 26, (ulong)count, 2);
            if (count != 0) Buffer.BlockCopy(body, offset, target, HeaderBytes, count);
            return HeaderBytes + count;
        }

        /// <summary>Rejects malformed headers and trailing bytes before any decoder runs.</summary>
        public static bool TryRead(byte[] bytes, int count, out ushort kind, out ulong epoch, out ulong sequence, out Lane lane, out bool ack)
        {
            kind = 0; epoch = sequence = 0; lane = Lane.Control; ack = false;
            if (bytes == null || count < HeaderBytes || count > MaxBytes || count > bytes.Length
                || Get(bytes, 0, 4) != Magic || Get(bytes, 4, 2) != Version || bytes[24] > 3 || bytes[25] > 1
                || Get(bytes, 26, 2) != (ulong)(count - HeaderBytes)) return false;
            kind = (ushort)Get(bytes, 6, 2); epoch = Get(bytes, 8, 8); sequence = Get(bytes, 16, 8);
            lane = (Lane)bytes[24]; ack = bytes[25] == 1;
            return epoch != 0 && sequence != 0 && (!ack || count == HeaderBytes);
        }

        /// <summary>Writes an unsigned little-endian integer of a fixed width.</summary>
        static void Put(byte[] bytes, int at, ulong value, int width)
        {
            for (int i = 0; i < width; i++) bytes[at + i] = (byte)(value >> (8 * i));
        }

        /// <summary>Reads an unsigned little-endian integer from validated bounds.</summary>
        static ulong Get(byte[] bytes, int at, int width)
        {
            ulong value = 0; for (int i = 0; i < width; i++) value |= (ulong)bytes[at + i] << (8 * i); return value;
        }
    }
}
