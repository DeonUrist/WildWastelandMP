using System;

namespace WastelandMPNew
{
    /// <summary>Typed envelope delivery after transport polling; the adapter chooses explicit per-kind domain decoders.</summary>
    public delegate void FrameHandler(Lane lane, ushort kind, byte[] buffer, int offset, int count);

    /// <summary>One authenticated peer's independently paced control, world, combat and disposable presentation streams.</summary>
    public sealed class PeerChannel : IDisposable
    {
        readonly IPacketLink link;
        readonly IClock clock;
        readonly ulong epoch;
        readonly OrderedLane control, world, combat;
        readonly LatestLane presentation;
        readonly FrameHandler handler;
        readonly Action<ushort, byte[], int, int> onControl, onWorld, onCombat, onPresentation;
        readonly byte[] receive = new byte[FrameCodec.MaxBytes];
        bool disposed, pumping;
        int phase;

        /// <summary>Constructs fixed windows once; the shell must negotiate a fresh nonzero epoch before creating a channel.</summary>
        public PeerChannel(IPacketLink link, IClock clock, ulong epoch, FrameHandler handler)
        {
            if (link == null || clock == null || epoch == 0 || handler == null) throw new ArgumentException("Invalid peer channel");
            this.link = link; this.clock = clock; this.epoch = epoch; this.handler = handler;
            control = new OrderedLane(link, clock, epoch, Lane.Control);
            world = new OrderedLane(link, clock, epoch, Lane.World);
            combat = new OrderedLane(link, clock, epoch, Lane.Combat);
            presentation = new LatestLane(link, epoch);
            onControl = Control; onWorld = World; onCombat = Combat; onPresentation = Presentation;
        }

        /// <summary>Queues persistent traffic or sends a superseding presentation bundle once; false means the producer retains work.</summary>
        public bool TrySend(Lane lane, ushort kind, byte[] bytes, int offset, int count)
        {
            if (disposed) return false;
            switch (lane)
            {
                case Lane.Control: return control.TryQueue(kind, bytes, offset, count);
                case Lane.World: return world.TryQueue(kind, bytes, offset, count);
                case Lane.Combat: return combat.TryQueue(kind, bytes, offset, count);
                case Lane.Presentation: return presentation.TrySend(kind, bytes, offset, count);
                default: return false;
            }
        }

        /// <summary>Returns the last queued world sequence for a transfer's final receipt proof.</summary>
        public ulong WorldSequence { get { return world.LastQueued; } }

        /// <summary>Receipt acknowledgement is distinct from proof that the world snapshot was applied successfully.</summary>
        public bool WorldReceived(ulong finalSequence)
        {
            return world.Acknowledged(finalSequence);
        }

        /// <summary>Bounds datagram reads and work between callbacks; exceptions close the channel to prevent partial-operation replay.</summary>
        public void Pump(int receiveBudget = 16, int perLaneBudget = 4, double secondsBudget = .0005)
        {
            if (disposed) return;
            if (pumping || receiveBudget < 1 || receiveBudget > 256 || perLaneBudget < 1 || perLaneBudget > 64
                || double.IsNaN(secondsBudget) || double.IsInfinity(secondsBudget) || secondsBudget <= 0) throw new InvalidOperationException("Invalid or reentrant pump");
            double end = clock.Now + secondsBudget; pumping = true;
            try
            {
                for (int step = 0; step < 7 && !disposed && clock.Now < end; step++)
                {
                    int current = phase; phase = (phase + 1) % 7;
                    switch (current)
                    {
                        case 0: control.Drain(onControl, perLaneBudget); break;
                        case 1: combat.Drain(onCombat, perLaneBudget); break;
                        case 2: world.Drain(onWorld, perLaneBudget); break;
                        case 3: control.Tick(perLaneBudget); break;
                        case 4: combat.Tick(perLaneBudget); break;
                        case 5: world.Tick(perLaneBudget); break;
                        case 6: ReceiveFrames(receiveBudget, end); break;
                    }
                }
            }
            catch { Dispose(); throw; }
            finally { pumping = false; }
        }

        /// <summary>Reads a bounded batch, with phase rotation preserving send/delivery progress under sustained incoming traffic.</summary>
        void ReceiveFrames(int budget, double end)
        {
            for (int i = 0; i < budget && !disposed && clock.Now < end; i++)
            {
                int count; if (!link.TryReceive(receive, out count)) break;
                ushort kind; ulong remoteEpoch, sequence; Lane lane; bool ack;
                if (!FrameCodec.TryRead(receive, count, out kind, out remoteEpoch, out sequence, out lane, out ack) || remoteEpoch != epoch) continue;
                switch (lane)
                {
                    case Lane.Control: control.Receive(receive, count); break;
                    case Lane.World: world.Receive(receive, count); break;
                    case Lane.Combat: combat.Receive(receive, count); break;
                    case Lane.Presentation: presentation.Receive(receive, count, onPresentation); break;
                }
            }
        }

        /// <summary>Delivers control messages for admission before gameplay routing becomes available.</summary>
        void Control(ushort kind, byte[] bytes, int offset, int count)
        {
            if (!disposed) handler(Lane.Control, kind, bytes, offset, count);
        }

        /// <summary>Delivers complete persistent world messages through the shell's authority decoder.</summary>
        void World(ushort kind, byte[] bytes, int offset, int count)
        {
            if (!disposed) handler(Lane.World, kind, bytes, offset, count);
        }

        /// <summary>Keeps damage and revive ordering independent of bulk world transfer.</summary>
        void Combat(ushort kind, byte[] bytes, int offset, int count)
        {
            if (!disposed) handler(Lane.Combat, kind, bytes, offset, count);
        }

        /// <summary>Delivers a complete superseding presentation bundle, never independent loss-sensitive effects.</summary>
        void Presentation(ushort kind, byte[] bytes, int offset, int count)
        {
            if (!disposed) handler(Lane.Presentation, kind, bytes, offset, count);
        }

        /// <summary>Idempotently closes the link; Core revokes admission and clears per-peer entity/presentation state separately.</summary>
        public void Dispose()
        {
            if (disposed) return; disposed = true; link.Dispose();
        }
    }
}
