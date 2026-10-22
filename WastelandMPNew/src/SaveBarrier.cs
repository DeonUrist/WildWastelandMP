using System;
using System.Collections.Generic;

namespace WastelandMPNew
{
    /// <summary>Disposition after waiting for the host's fixed participant set.</summary>
    public enum SaveDecision
    {
        /// <summary>Synchronization still has budget.</summary>
        Waiting,
        /// <summary>All required state has arrived.</summary>
        Confirmed,
        /// <summary>Synchronization expired; use confirmed host data and show diagnostics.</summary>
        LastConfirmedWithWarning,
        /// <summary>Integrity or write failure prohibits publication.</summary>
        Blocked
    }

    /// <summary>One total synchronization deadline, plus a sticky world-integrity failure independent of session teardown.</summary>
    public sealed class SaveBarrier
    {
        readonly HashSet<ulong> missing = new HashSet<ulong>();
        readonly IClock clock;
        double deadline;
        bool active;
        Guid token;

        /// <summary>First integrity/write failure; only a verified world reload may replace this barrier instance.</summary>
        public string Failure { get; private set; }

        /// <summary>Number of ready owners still awaiting actual applied reports.</summary>
        public int MissingCount { get { return missing.Count; } }

        /// <summary>Binds the barrier to the Core monotonic clock.</summary>
        public SaveBarrier(IClock clock)
        {
            this.clock = clock ?? throw new ArgumentNullException("clock");
        }

        /// <summary>Freezes the ready participant set; loading peers must be filtered by the caller.</summary>
        public Guid Begin(ulong[] readyOwners, double totalSeconds = 30)
        {
            if (active || Failure != null || readyOwners == null || readyOwners.Length > 256 || double.IsNaN(totalSeconds)
                || double.IsInfinity(totalSeconds) || totalSeconds <= 0) throw new InvalidOperationException("Save unavailable");
            missing.Clear(); for (int i = 0; i < readyOwners.Length; i++) if (readyOwners[i] != 0) missing.Add(readyOwners[i]);
            token = Guid.NewGuid(); active = true; deadline = clock.Now + totalSeconds; return token;
        }

        /// <summary>Accepts acknowledgement only after that owner's actual report was validated and applied to the host state.</summary>
        public bool ConfirmAppliedReport(Guid attempt, ulong peer)
        {
            return active && attempt == token && clock.Now < deadline && missing.Remove(peer);
        }

        /// <summary>Never converts terrain, commit or disk failure into a successful timeout.</summary>
        public void ReportFailure(string reason)
        {
            if (Failure == null) Failure = string.IsNullOrWhiteSpace(reason) ? "Unspecified world integrity failure" : reason;
        }

        /// <summary>Timeout allows only already confirmed host state; pending queues remain owned by their providers.</summary>
        public SaveDecision Evaluate(bool optionalWorkPending)
        {
            if (Failure != null) return SaveDecision.Blocked;
            if (!active) throw new InvalidOperationException("No save is active");
            if (missing.Count == 0 && !optionalWorkPending) return SaveDecision.Confirmed;
            return clock.Now >= deadline ? SaveDecision.LastConfirmedWithWarning : SaveDecision.Waiting;
        }

        /// <summary>Ends a transaction without clearing failure or implying durable publication.</summary>
        public void End()
        {
            active = false; missing.Clear(); token = Guid.Empty;
        }
    }
}
