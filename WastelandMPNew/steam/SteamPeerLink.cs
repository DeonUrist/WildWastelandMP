using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace WastelandMPNew.Steam
{
    /// <summary>One Steam-authenticated connection; a fixed ring separates SDK polling from application dispatch.</summary>
    public sealed class SteamPeerLink : IPacketLink
    {
        readonly byte[][] packets = new byte[32][];
        readonly int[] lengths = new int[32];
        readonly HSteamNetConnection connection;
        int read, count;
        bool disposed;
        internal bool Connected;

        /// <summary>Transport-authenticated Steam user id, distinct from an arbitrary display name.</summary>
        public ulong SteamUserId { get; private set; }

        /// <summary>Creates a preallocated queue for an SDK connection admitted by the lobby owner.</summary>
        internal SteamPeerLink(HSteamNetConnection connection, ulong steamUserId)
        {
            this.connection = connection; SteamUserId = steamUserId;
            for (int i = 0; i < packets.Length; i++) packets[i] = new byte[FrameCodec.MaxBytes];
        }

        /// <summary>True only after Steam reports connection establishment for the expected identity.</summary>
        public bool Authenticated { get { return Connected && !disposed; } }

        /// <summary>Sends one unreliable SDK message; OrderedLane supplies application ordering and retry uniformly across links.</summary>
        public bool TrySend(byte[] bytes, int offset, int length)
        {
            if (!Authenticated || bytes == null || offset < 0 || length < 1 || length > FrameCodec.MaxBytes || offset > bytes.Length - length) return false;
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                long message;
                return SteamNetworkingSockets.SendMessageToConnection(connection, IntPtr.Add(handle.AddrOfPinnedObject(), offset), (uint)length,
                    Constants.k_nSteamNetworkingSend_UnreliableNoDelay, out message) == EResult.k_EResultOK;
            }
            finally { handle.Free(); }
        }

        /// <summary>Copies a queued datagram into the caller's reusable buffer.</summary>
        public bool TryReceive(byte[] destination, out int length)
        {
            length = 0;
            if (!Authenticated || count == 0 || destination == null || destination.Length < FrameCodec.MaxBytes) return false;
            length = lengths[read]; Buffer.BlockCopy(packets[read], 0, destination, 0, length);
            read = (read + 1) % packets.Length; count--; return true;
        }

        /// <summary>Copies an SDK-owned buffer while it is valid; false requires disconnect, never partial-world continuation.</summary>
        internal bool Enqueue(IntPtr source, int length)
        {
            if (!Authenticated || source == IntPtr.Zero || length < 1 || length > FrameCodec.MaxBytes || count == packets.Length) return false;
            int at = (read + count) % packets.Length; Marshal.Copy(source, packets[at], 0, length); lengths[at] = length; count++; return true;
        }

        /// <summary>Closes exactly this connection and clears queued application data.</summary>
        public void Dispose()
        {
            if (disposed) return; disposed = true; Connected = false; count = 0;
            SteamNetworkingSockets.CloseConnection(connection, 0, "Wasteland session closed", false);
        }
    }
}
