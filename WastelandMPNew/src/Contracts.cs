using System;

namespace WastelandMPNew
{
    /// <summary>Independent streams; order is guaranteed within one stream only.</summary>
    public enum Lane : byte
    {
        /// <summary>Admission and session management.</summary>
        Control,
        /// <summary>Persistent world changes.</summary>
        World,
        /// <summary>Damage, revive and ordered restore.</summary>
        Combat,
        /// <summary>Disposable superseding visual state.</summary>
        Presentation
    }

    /// <summary>Which authenticated participant can originate a message.</summary>
    public enum SourceAuthority
    {
        /// <summary>Only the world host.</summary>
        Host,
        /// <summary>Only the host-assigned current entity owner.</summary>
        EntityOwner,
        /// <summary>Any fully admitted peer, with additional domain validation.</summary>
        AnyAdmitted
    }

    /// <summary>Admission milestones; transport connectivity alone never enables gameplay.</summary>
    public enum JoinStage
    {
        /// <summary>Authenticated transport only.</summary>
        Connected,
        /// <summary>Required schema and mod build match.</summary>
        Compatible,
        /// <summary>World generation policy installed before launch.</summary>
        SettingsApplied,
        /// <summary>Complete validated snapshot applied.</summary>
        SnapshotApplied,
        /// <summary>Safe placement applied locally.</summary>
        Placed,
        /// <summary>Host has accepted final placement proof.</summary>
        Ready,
        /// <summary>Departed or rejected connection.</summary>
        Closed
    }

    /// <summary>Monotonic seconds supplied by Core; never use scaled game time.</summary>
    public interface IClock
    {
        /// <summary>Seconds since an arbitrary fixed start.</summary>
        double Now { get; }
    }

    /// <summary>A link to exactly one remote participant; buffers are borrowed until the next receive.</summary>
    public interface IPacketLink : IDisposable
    {
        /// <summary>Authentication was established independently of a packet's claimed identity.</summary>
        bool Authenticated { get; }
        /// <summary>Bounded nonblocking send; false means backpressure or link failure.</summary>
        bool TrySend(byte[] bytes, int offset, int count);
        /// <summary>Copies one datagram into the caller buffer; never invokes gameplay callbacks.</summary>
        bool TryReceive(byte[] destination, out int count);
    }

    /// <summary>ModAPI-facing queries; an adapter obtains providers through Core on every call.</summary>
    public interface IAuthorityView
    {
        /// <summary>The authoritative host's transport-bound participant id.</summary>
        ulong Host { get; }
        /// <summary>True only after settings, complete snapshot and placement acknowledgement.</summary>
        bool IsReady(ulong peer);
        /// <summary>Current entity ownership; revision changes on every handoff.</summary>
        bool IsOwner(ulong peer, long entity, ulong revision);
    }

    /// <summary>Applies an already decoded and validated remote event using ModEvents.ApplyRemote.</summary>
    public delegate void ApplyPayload(ulong sender, byte[] buffer, int offset, int count);
}
