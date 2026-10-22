using System;
using System.Collections.Generic;
using Steamworks;

namespace WastelandMPNew.Steam
{
    /// <summary>Own lobby and socket lifecycle using the game's existing Steamworks.NET runtime; never initializes or shuts down Steam globally.</summary>
    public sealed class SteamSession : IDisposable
    {
        const string ProtocolKey = "ww2mp.schema";
        const string HostKey = "ww2mp.host";
        const string Protocol = "1";
        const int VirtualPort = 27;
        readonly Dictionary<uint, SteamPeerLink> links = new Dictionary<uint, SteamPeerLink>();
        readonly IntPtr[] messages = new IntPtr[64];
        readonly CallResult<LobbyCreated_t> created;
        readonly CallResult<LobbyEnter_t> entered;
        readonly Callback<SteamNetConnectionStatusChangedCallback_t> changed;
        readonly bool hosting;
        readonly int capacity;
        readonly IClock clock;
        double requestDeadline;
        bool requestPending;
        HSteamListenSocket listener = HSteamListenSocket.Invalid;
        HSteamNetPollGroup pollGroup;
        CSteamID lobby = CSteamID.Nil, host = CSteamID.Nil;
        bool disposed;

        /// <summary>First failure; Core observes it and tears down the entire gameplay session outside SDK callbacks.</summary>
        public string Failure { get; private set; }

        /// <summary>Current lobby id, suitable for the platform invite overlay.</summary>
        public ulong LobbyId { get { return lobby.m_SteamID; } }

        /// <summary>Constructs resources only after Core has established game-owned Steam readiness and explicit user intent.</summary>
        public SteamSession(bool hosting, bool gameSteamReady, IClock clock, int capacity = 16)
        {
            if (!gameSteamReady || clock == null || capacity < 2 || capacity > 32) throw new InvalidOperationException("Steam or capacity unavailable");
            this.hosting = hosting; this.capacity = capacity; this.clock = clock;
            SteamNetworkingUtils.InitRelayNetworkAccess();
            pollGroup = SteamNetworkingSockets.CreatePollGroup();
            if (pollGroup.m_HSteamNetPollGroup == 0) throw new InvalidOperationException("Steam poll group creation failed");
            created = CallResult<LobbyCreated_t>.Create(Created);
            entered = CallResult<LobbyEnter_t>.Create(Entered);
            changed = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(ConnectionChanged);
        }

        /// <summary>Creates a friends-only lobby only after the selected host world is fully ready.</summary>
        public void CreateHostLobby(bool selectedWorldReady)
        {
            if (disposed || !hosting || lobby.IsValid() || created.IsActive() || !selectedWorldReady) throw new InvalidOperationException("Host world not ready or request already active");
            created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, capacity));
            requestPending = true; requestDeadline = clock.Now + 45;
        }

        /// <summary>Joins one explicitly chosen lobby; world launch waits for the separate ModAPI admission handshake.</summary>
        public void JoinLobby(ulong id)
        {
            if (disposed || hosting || lobby.IsValid() || entered.IsActive() || id == 0) throw new InvalidOperationException("Lobby join unavailable");
            entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
            requestPending = true; requestDeadline = clock.Now + 45;
        }

        /// <summary>Looks up a connection by Steam-authenticated identity; Core separately tracks admission milestones.</summary>
        public SteamPeerLink FindPeer(ulong steamId)
        {
            foreach (SteamPeerLink link in links.Values) if (link.SteamUserId == steamId && link.Authenticated) return link;
            return null;
        }

        /// <summary>Copies established peer ids into a caller-owned buffer without allocating or exposing mutable connection tables.</summary>
        public int CopyPeers(ulong[] destination)
        {
            if (destination == null || destination.Length < capacity) throw new ArgumentException("Peer buffer smaller than capacity");
            int count = 0; foreach (SteamPeerLink link in links.Values) if (link.Authenticated) destination[count++] = link.SteamUserId; return count;
        }

        /// <summary>Polls at most 64 SDK messages; all native pointers are released even on failure, with gameplay deferred to Core.</summary>
        public void Tick()
        {
            if (disposed || Failure != null) return;
            if (requestPending && clock.Now >= requestDeadline) { Fail("Steam lobby request timed out"); return; }
            if (!lobby.IsValid()) return;
            if (SteamMatchmaking.GetLobbyOwner(lobby) != host) { Fail("Lobby host changed; rehost the saved world explicitly"); return; }
            int count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, messages, messages.Length);
            if (count < 0) { Fail("Steam receive failed"); return; }
            try
            {
                for (int i = 0; i < count; i++)
                {
                    SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(messages[i]); SteamPeerLink link;
                    if (links.TryGetValue(message.m_conn.m_HSteamNetConnection, out link)
                        && !link.Enqueue(message.m_pData, message.m_cbSize)) Fail("Steam receive queue overflow or malformed datagram");
                }
            }
            finally
            {
                for (int i = 0; i < count; i++) if (messages[i] != IntPtr.Zero) { SteamNetworkingMessage_t.Release(messages[i]); messages[i] = IntPtr.Zero; }
            }
        }

        /// <summary>Publishes independent protocol metadata and opens the P2P listener after lobby creation succeeds.</summary>
        void Created(LobbyCreated_t result, bool failed)
        {
            if (disposed) return;
            requestPending = false;
            if (failed || result.m_eResult != EResult.k_EResultOK) { Fail("Steam lobby creation failed"); return; }
            lobby = new CSteamID(result.m_ulSteamIDLobby); host = SteamUser.GetSteamID();
            if (!SteamMatchmaking.SetLobbyData(lobby, ProtocolKey, Protocol)
                || !SteamMatchmaking.SetLobbyData(lobby, HostKey, host.m_SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            { Fail("Steam lobby metadata publication failed"); return; }
            listener = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, null);
            if (listener == HSteamListenSocket.Invalid) Fail("Steam listener creation failed");
        }

        /// <summary>Checks protocol and actual lobby owner instead of trusting an advertised arbitrary host.</summary>
        void Entered(LobbyEnter_t result, bool failed)
        {
            if (disposed) return;
            requestPending = false;
            if (failed || (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            { Fail("Steam lobby admission failed"); return; }
            lobby = new CSteamID(result.m_ulSteamIDLobby); host = SteamMatchmaking.GetLobbyOwner(lobby); ulong advertised;
            if (SteamMatchmaking.GetLobbyData(lobby, ProtocolKey) != Protocol || !host.IsValid()
                || !ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby, HostKey), out advertised) || advertised != host.m_SteamID)
            { Fail("Steam lobby metadata or host mismatch"); return; }
            SteamNetworkingIdentity remote = new SteamNetworkingIdentity(); remote.SetSteamID(host);
            HSteamNetConnection connection = SteamNetworkingSockets.ConnectP2P(ref remote, VirtualPort, 0, null);
            if (connection.m_HSteamNetConnection == 0) { Fail("Steam connection creation failed"); return; }
            links.Add(connection.m_HSteamNetConnection, new SteamPeerLink(connection, host.m_SteamID));
            if (!SteamNetworkingSockets.SetConnectionPollGroup(connection, pollGroup)) Fail("Steam poll group assignment failed");
        }

        /// <summary>Admits only known lobby members on our listener and validates the authenticated identity on connection completion.</summary>
        void ConnectionChanged(SteamNetConnectionStatusChangedCallback_t status)
        {
            if (disposed || Failure != null) return;
            uint id = status.m_hConn.m_HSteamNetConnection; SteamPeerLink link;
            CSteamID user = status.m_info.m_identityRemote.GetSteamID();
            if (status.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting && hosting)
            {
                if (listener == HSteamListenSocket.Invalid || status.m_info.m_hListenSocket != listener) return;
                if (!Member(user) || user == host || links.Count >= capacity - 1 || HasIdentity(user.m_SteamID))
                { SteamNetworkingSockets.CloseConnection(status.m_hConn, 0, "Lobby admission refused", false); return; }
                if (SteamNetworkingSockets.AcceptConnection(status.m_hConn) != EResult.k_EResultOK)
                { SteamNetworkingSockets.CloseConnection(status.m_hConn, 0, "Accept failed", false); return; }
                links[id] = new SteamPeerLink(status.m_hConn, user.m_SteamID);
                if (!SteamNetworkingSockets.SetConnectionPollGroup(status.m_hConn, pollGroup)) Fail("Steam poll group assignment failed");
            }
            else if (links.TryGetValue(id, out link))
            {
                if (status.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    if (link.SteamUserId != user.m_SteamID || !Member(user)) { link.Dispose(); links.Remove(id); return; }
                    link.Connected = true;
                }
                else if (status.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                    || status.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                { link.Dispose(); links.Remove(id); if (!hosting) Fail("Steam host disconnected"); }
            }
        }

        /// <summary>Checks current lobby membership only on connection admission, outside packet hot paths.</summary>
        bool Member(CSteamID user)
        {
            if (!lobby.IsValid() || !user.IsValid()) return false;
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++) if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i) == user) return true;
            return false;
        }

        /// <summary>Includes pending connections when enforcing one connection per Steam identity.</summary>
        bool HasIdentity(ulong user)
        {
            foreach (SteamPeerLink link in links.Values) if (link.SteamUserId == user) return true;
            return false;
        }

        /// <summary>Latches the first failure; logging and user presentation remain Core responsibilities.</summary>
        void Fail(string reason)
        {
            if (Failure == null) Failure = reason;
        }

        /// <summary>Cancels async calls, closes owned handles and leaves the lobby without shutting down the game's Steam runtime.</summary>
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            created.Cancel(); entered.Cancel(); created.Dispose(); entered.Dispose(); changed.Dispose();
            foreach (SteamPeerLink link in links.Values) link.Dispose(); links.Clear();
            if (listener != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listener);
            SteamNetworkingSockets.DestroyPollGroup(pollGroup);
            if (lobby.IsValid()) SteamMatchmaking.LeaveLobby(lobby);
            lobby = CSteamID.Nil; listener = HSteamListenSocket.Invalid;
        }
    }
}
