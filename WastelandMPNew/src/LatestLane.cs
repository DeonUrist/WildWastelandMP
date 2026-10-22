using System;

namespace WastelandMPNew
{
    /// <summary>Unreliable latest-only presentation samples; never use for damage, inventory, despawn or persistent edits.</summary>
    public sealed class LatestLane
    {
        readonly byte[] send = new byte[FrameCodec.MaxBytes];
        readonly IPacketLink link;
        readonly ulong epoch;
        ulong next = 1, last;

        /// <summary>Creates a presentation stream for one authenticated connection and epoch.</summary>
        public LatestLane(IPacketLink link, ulong epoch)
        {
            if (link == null || epoch == 0) throw new ArgumentException("Invalid latest stream"); this.link = link; this.epoch = epoch;
        }

        /// <summary>Sends once without queuing; drops on backpressure rather than blocking world or combat traffic.</summary>
        public bool TrySend(ushort kind, byte[] bytes, int offset, int count)
        {
            if (!link.Authenticated || next == ulong.MaxValue) return false;
            int size = FrameCodec.Write(send, kind, epoch, next++, Lane.Presentation, false, bytes, offset, count);
            return link.TrySend(send, 0, size);
        }

        /// <summary>Discards duplicates and stale samples; callers use one stream per independent superseding state domain.</summary>
        public bool Receive(byte[] bytes, int count, Action<ushort, byte[], int, int> apply)
        {
            ushort kind; ulong remoteEpoch, sequence; Lane lane; bool ack;
            if (apply == null || !link.Authenticated || !FrameCodec.TryRead(bytes, count, out kind, out remoteEpoch, out sequence, out lane, out ack)
                || remoteEpoch != epoch || lane != Lane.Presentation || ack || sequence <= last) return false;
            last = sequence; apply(kind, bytes, FrameCodec.HeaderBytes, count - FrameCodec.HeaderBytes); return true;
        }
    }
}
