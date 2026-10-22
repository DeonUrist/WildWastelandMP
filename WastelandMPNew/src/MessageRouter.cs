using System;
using System.Collections.Generic;

namespace WastelandMPNew
{
    /// <summary>Explicit wire schema and authority gate; the transport supplies sender identity outside the payload.</summary>
    public sealed class MessageRouter
    {
        sealed class Route
        {
            internal SourceAuthority Authority;
            internal int MaxBytes;
            internal ApplyPayload Apply;
        }
        readonly Dictionary<ushort, Route> routes = new Dictionary<ushort, Route>();
        readonly IAuthorityView authority;

        /// <summary>Attaches host/owner queries without depending on gameplay module classes.</summary>
        public MessageRouter(IAuthorityView authority)
        {
            this.authority = authority ?? throw new ArgumentNullException("authority");
        }

        /// <summary>Registers once during startup; duplicate wire ids fail before a session starts.</summary>
        public void Register(ushort kind, SourceAuthority source, int maxBytes, ApplyPayload apply)
        {
            if (kind == 0 || maxBytes < 0 || maxBytes > FrameCodec.MaxBytes - FrameCodec.HeaderBytes || apply == null
                || source < SourceAuthority.Host || source > SourceAuthority.AnyAdmitted) throw new ArgumentException("Invalid route");
            routes.Add(kind, new Route { Authority = source, MaxBytes = maxBytes, Apply = apply });
        }

        /// <summary>Dispatches only admitted senders; owner entity/revision must come from a validated decoder, never unrelated metadata.</summary>
        public bool Apply(ulong sender, ushort kind, long entity, ulong ownershipRevision, byte[] bytes, int offset, int count)
        {
            Route route;
            if (!authority.IsReady(sender) || !routes.TryGetValue(kind, out route) || bytes == null || offset < 0 || count < 0
                || count > route.MaxBytes || offset > bytes.Length - count) return false;
            if (route.Authority == SourceAuthority.Host && sender != authority.Host) return false;
            if (route.Authority == SourceAuthority.EntityOwner && (entity == 0 || !authority.IsOwner(sender, entity, ownershipRevision))) return false;
            route.Apply(sender, bytes, offset, count); return true;
        }
    }
}
