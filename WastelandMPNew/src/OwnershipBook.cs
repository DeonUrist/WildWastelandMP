using System;
using System.Collections.Generic;

namespace WastelandMPNew
{
    /// <summary>Host-assigned entity ownership with monotonic revisions; never populated from a guest claim alone.</summary>
    public sealed class OwnershipBook
    {
        struct Lease
        {
            internal ulong Peer;
            internal ulong Revision;
        }
        readonly Dictionary<long, Lease> entities = new Dictionary<long, Lease>();
        readonly int capacity;
        ulong nextRevision = 1;

        /// <summary>Sets a world entity cap so untrusted requests cannot grow dictionaries indefinitely.</summary>
        public OwnershipBook(int capacity = 65536)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity"); this.capacity = capacity;
        }

        /// <summary>Commits a host-validated handoff and returns the revision that subsequent owner messages must carry.</summary>
        public ulong Assign(long entity, ulong peer)
        {
            if (entity == 0 || peer == 0 || nextRevision == ulong.MaxValue || (!entities.ContainsKey(entity) && entities.Count >= capacity))
                throw new InvalidOperationException("Ownership allocation refused");
            ulong revision = nextRevision++; entities[entity] = new Lease { Peer = peer, Revision = revision }; return revision;
        }

        /// <summary>Rejects packets from a former owner even when its peer id is later reused.</summary>
        public bool IsOwner(ulong peer, long entity, ulong revision)
        {
            Lease lease; return entities.TryGetValue(entity, out lease) && lease.Peer == peer && lease.Revision == revision;
        }

        /// <summary>Removes a despawned entity; reassignment still receives a newer revision.</summary>
        public void Remove(long entity)
        {
            entities.Remove(entity);
        }
    }
}
