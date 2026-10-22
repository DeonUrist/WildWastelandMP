using System;
using System.Collections.Generic;

namespace WastelandMPNew
{
    /// <summary>Bounded host request deduplication for one participant in one world epoch; completed ids are never silently evicted.</summary>
    public sealed class OperationJournal
    {
        /// <summary>Known operation state; absence is queried through TryGet.</summary>
        public enum Status
        {
            /// <summary>Authorization reserved; no replay may execute again.</summary>
            Reserved,
            /// <summary>Host mutation committed.</summary>
            Committed,
            /// <summary>Operation refused or aborted before credit.</summary>
            Rejected
        }
        readonly Dictionary<Guid, Status> operations = new Dictionary<Guid, Status>();
        readonly int capacity;

        /// <summary>Sets the maximum retained operations; a full journal requires a coordinated epoch rollover.</summary>
        public OperationJournal(int capacity = 65536)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity"); this.capacity = capacity;
        }

        /// <summary>Reserves only new operations before any native credit; a duplicate must return its existing result.</summary>
        public bool TryReserve(Guid operation)
        {
            if (operation == Guid.Empty || operations.ContainsKey(operation) || operations.Count >= capacity) return false;
            operations.Add(operation, Status.Reserved); return true;
        }

        /// <summary>Records one terminal result; a conflicting later result cannot overwrite the decision.</summary>
        public bool Finish(Guid operation, bool committed)
        {
            Status current; Status result = committed ? Status.Committed : Status.Rejected;
            if (!operations.TryGetValue(operation, out current)) return false;
            if (current != Status.Reserved) return current == result;
            operations[operation] = result; return true;
        }

        /// <summary>Returns the retained decision for a retry without granting the operation again.</summary>
        public bool TryGet(Guid operation, out Status status)
        {
            return operations.TryGetValue(operation, out status);
        }
    }
}
