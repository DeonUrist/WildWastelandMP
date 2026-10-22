using System;
using System.Collections.Generic;

namespace WastelandMPNew
{
    /// <summary>Bounded host-side admission state; each connection has a separate attempt token, independent of authentication.</summary>
    public sealed class AdmissionBook
    {
        sealed class Entry
        {
            internal Guid Token;
            internal JoinStage Stage;
            internal double Deadline;
        }
        readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        readonly IClock clock;
        readonly int capacity;

        /// <summary>Sets the maximum connected peers, including guests still loading.</summary>
        public AdmissionBook(IClock clock, int capacity = 16)
        {
            if (clock == null || capacity < 1 || capacity > 256) throw new ArgumentException("Invalid admission limits");
            this.clock = clock; this.capacity = capacity;
        }

        /// <summary>Begins a fresh connection attempt; reusing a peer id requires removing its old entry first.</summary>
        public Guid Connect(ulong peer, double timeoutSeconds = 180)
        {
            if (peer == 0 || entries.ContainsKey(peer) || entries.Count >= capacity || double.IsNaN(timeoutSeconds)
                || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0) throw new InvalidOperationException("Admission unavailable");
            Guid token = Guid.NewGuid(); entries.Add(peer, new Entry { Token = token, Stage = JoinStage.Connected, Deadline = clock.Now + timeoutSeconds }); return token;
        }

        /// <summary>Advances exactly one milestone after its actual proof; the caller must verify manifest/digest/placement, not trust a boolean in a packet.</summary>
        public bool Advance(ulong peer, Guid token, JoinStage expected, JoinStage next)
        {
            Entry entry;
            if (!entries.TryGetValue(peer, out entry) || entry.Token != token || entry.Stage != expected
                || expected >= JoinStage.Ready || (int)next != (int)expected + 1 || clock.Now >= entry.Deadline) return false;
            entry.Stage = next; return true;
        }

        /// <summary>Readiness requires the final placement acknowledgement, not only receipt of snapshot chunks.</summary>
        public bool IsReady(ulong peer)
        {
            Entry entry; return entries.TryGetValue(peer, out entry) && entry.Stage == JoinStage.Ready;
        }

        /// <summary>Detects timed-out loading peers; the coordinator closes them outside collection enumeration.</summary>
        public bool Expired(ulong peer)
        {
            Entry entry; return entries.TryGetValue(peer, out entry) && entry.Stage < JoinStage.Ready && clock.Now >= entry.Deadline;
        }

        /// <summary>Revokes admission so delayed packets cannot recreate avatars or modify the world.</summary>
        public void Remove(ulong peer)
        {
            entries.Remove(peer);
        }
    }
}
