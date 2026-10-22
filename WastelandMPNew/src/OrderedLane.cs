using System;

namespace WastelandMPNew
{
    /// <summary>One peer/epoch/stream bounded ordered channel with selective acknowledgements and retry.</summary>
    public sealed class OrderedLane
    {
        sealed class Slot
        {
            internal readonly byte[] Bytes = new byte[FrameCodec.MaxBytes];
            internal int Count;
            internal ulong Sequence;
            internal double RetryAt;
        }
        readonly Slot[] outgoing, incoming;
        readonly byte[] ack = new byte[FrameCodec.HeaderBytes];
        readonly IPacketLink link;
        readonly IClock clock;
        readonly Lane lane;
        readonly ulong epoch;
        readonly double retrySeconds;
        ulong sendNext = 1, sendFloor = 1, receiveNext = 1;
        bool delivering, failed;

        /// <summary>Allocates fixed windows once; the caller owns the link and ticks all lanes fairly.</summary>
        public OrderedLane(IPacketLink link, IClock clock, ulong epoch, Lane lane, int window = 32, double retrySeconds = .25)
        {
            if (link == null || clock == null || epoch == 0 || (byte)lane > 3 || window < 1 || window > 256
                || double.IsNaN(retrySeconds) || double.IsInfinity(retrySeconds) || retrySeconds <= 0) throw new ArgumentException("Invalid lane options");
            this.link = link; this.clock = clock; this.epoch = epoch; this.lane = lane; this.retrySeconds = retrySeconds;
            outgoing = new Slot[window]; incoming = new Slot[window];
            for (int i = 0; i < window; i++) { outgoing[i] = new Slot(); incoming[i] = new Slot(); }
        }

        /// <summary>Last accepted outbound sequence; snapshot completion must wait for acknowledgement.</summary>
        public ulong LastQueued { get { return sendNext - 1; } }

        /// <summary>Whether every queued packet through the supplied sequence has been acknowledged.</summary>
        public bool Acknowledged(ulong sequence)
        {
            return sequence < sendFloor;
        }

        /// <summary>Copies once into a retained slot; false preserves the caller's unsent work.</summary>
        public bool TryQueue(ushort kind, byte[] bytes, int offset, int count)
        {
            if (failed || !link.Authenticated || sendNext == ulong.MaxValue || sendNext - sendFloor >= (ulong)outgoing.Length) return false;
            Slot slot = outgoing[(int)(sendNext % (ulong)outgoing.Length)];
            slot.Count = FrameCodec.Write(slot.Bytes, kind, epoch, sendNext, lane, false, bytes, offset, count);
            slot.Sequence = sendNext++; slot.RetryAt = clock.Now; return true;
        }

        /// <summary>Attempts a bounded number of due sends, retaining packets on socket backpressure.</summary>
        public int Tick(int packetBudget)
        {
            int attempts = 0; double now = clock.Now;
            if (failed) return 0;
            for (ulong sequence = sendFloor; sequence < sendNext && attempts < packetBudget; sequence++)
            {
                Slot slot = outgoing[(int)(sequence % (ulong)outgoing.Length)];
                if (slot.Sequence != sequence || slot.RetryAt > now) continue;
                attempts++; if (link.TrySend(slot.Bytes, 0, slot.Count)) slot.RetryAt = now + retrySeconds;
                else break;
            }
            return attempts;
        }

        /// <summary>Validates and stores one frame; application delivery happens separately through Drain.</summary>
        public bool Receive(byte[] bytes, int count)
        {
            ushort kind; ulong remoteEpoch, sequence; Lane remoteLane; bool isAck;
            if (failed || !link.Authenticated || !FrameCodec.TryRead(bytes, count, out kind, out remoteEpoch, out sequence, out remoteLane, out isAck)
                || remoteEpoch != epoch || remoteLane != lane) return false;
            if (isAck)
            {
                if (kind != 0 || sequence < sendFloor || sequence >= sendNext) return false;
                Slot sent = outgoing[(int)(sequence % (ulong)outgoing.Length)];
                if (sent.Sequence == sequence) sent.Sequence = 0;
                while (sendFloor < sendNext && outgoing[(int)(sendFloor % (ulong)outgoing.Length)].Sequence == 0) sendFloor++;
                return true;
            }
            if (sequence >= receiveNext && sequence - receiveNext >= (ulong)incoming.Length) return false;
            if (sequence >= receiveNext)
            {
                Slot slot = incoming[(int)(sequence % (ulong)incoming.Length)];
                if (slot.Sequence == 0) { Buffer.BlockCopy(bytes, 0, slot.Bytes, 0, count); slot.Count = count; slot.Sequence = sequence; }
                else if (slot.Sequence != sequence) return false;
            }
            FrameCodec.Write(ack, 0, epoch, sequence, lane, true, null, 0, 0);
            link.TrySend(ack, 0, ack.Length); return true;
        }

        /// <summary>Delivers at most the budget in order; exceptions propagate so the session can disconnect without saving partial state.</summary>
        public int Drain(Action<ushort, byte[], int, int> apply, int packetBudget)
        {
            if (apply == null) throw new ArgumentNullException("apply");
            if (delivering || failed) throw new InvalidOperationException("Reentrant or failed delivery");
            int count = 0; delivering = true;
            try
            {
                while (count < packetBudget)
                {
                    Slot slot = incoming[(int)(receiveNext % (ulong)incoming.Length)];
                    if (slot.Sequence != receiveNext) break;
                    ushort kind = (ushort)(slot.Bytes[6] | slot.Bytes[7] << 8);
                    apply(kind, slot.Bytes, FrameCodec.HeaderBytes, slot.Count - FrameCodec.HeaderBytes);
                    slot.Sequence = 0; receiveNext++; count++;
                }
            }
            catch { failed = true; throw; }
            finally { delivering = false; }
            return count;
        }
    }
}
