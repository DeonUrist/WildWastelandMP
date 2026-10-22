using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WastelandMPNew
{
    /// <summary>One provisioned UDP endpoint with a unique per-peer session key; no anonymous discovery or key exchange.</summary>
    public sealed class AuthenticatedUdpLink : IPacketLink
    {
        readonly Socket socket;
        readonly HMACSHA256 sendMac, receiveMac;
        readonly byte[] send = new byte[FrameCodec.MaxBytes + 32];
        readonly byte[] receive = new byte[FrameCodec.MaxBytes + 33];
        bool disposed;

        /// <summary>Opens one nonblocking connected UDP socket after an external trusted setup exchanged a random 256-bit key.</summary>
        public AuthenticatedUdpLink(IPEndPoint local, IPEndPoint remote, byte[] sessionKey, bool hostEndpoint)
        {
            if (local == null || remote == null || local.AddressFamily != remote.AddressFamily || sessionKey == null || sessionKey.Length != 32)
                throw new ArgumentException("Endpoints and a per-peer 256-bit key are required");
            byte[] outbound, inbound;
            using (HMACSHA256 derive = new HMACSHA256(sessionKey))
            {
                outbound = derive.ComputeHash(Encoding.ASCII.GetBytes(hostEndpoint ? "WW2 host to guest v1" : "WW2 guest to host v1"));
                inbound = derive.ComputeHash(Encoding.ASCII.GetBytes(hostEndpoint ? "WW2 guest to host v1" : "WW2 host to guest v1"));
            }
            sendMac = new HMACSHA256(outbound); receiveMac = new HMACSHA256(inbound);
            Array.Clear(outbound, 0, outbound.Length); Array.Clear(inbound, 0, inbound.Length);
            socket = new Socket(local.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try { socket.Bind(local); socket.Connect(remote); socket.Blocking = false; }
            catch { socket.Dispose(); sendMac.Dispose(); receiveMac.Dispose(); throw; }
        }

        /// <summary>The link authenticates messages with its provisioned key; it does not encrypt them.</summary>
        public bool Authenticated { get { return !disposed; } }

        /// <summary>Appends a message authentication code and reports a full datagram send only.</summary>
        public bool TrySend(byte[] bytes, int offset, int count)
        {
            if (disposed || bytes == null || offset < 0 || count < 1 || count > FrameCodec.MaxBytes || offset > bytes.Length - count) return false;
            Buffer.BlockCopy(bytes, offset, send, 0, count);
            byte[] tag = sendMac.ComputeHash(send, 0, count); Buffer.BlockCopy(tag, 0, send, count, tag.Length);
            try { return socket.Send(send, 0, count + 32, SocketFlags.None) == count + 32; }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }

        /// <summary>Examines at most eight datagrams per call; rejects oversize or invalid MACs before copying.</summary>
        public bool TryReceive(byte[] destination, out int count)
        {
            count = 0;
            if (disposed || destination == null || destination.Length < FrameCodec.MaxBytes) return false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int size;
                try { size = socket.Receive(receive, 0, receive.Length, SocketFlags.None); }
                catch (SocketException) { return false; }
                catch (ObjectDisposedException) { return false; }
                if (size <= 32 || size > FrameCodec.MaxBytes + 32) continue;
                int body = size - 32, diff = 0; byte[] tag = receiveMac.ComputeHash(receive, 0, body);
                for (int i = 0; i < 32; i++) diff |= tag[i] ^ receive[body + i];
                if (diff != 0) continue;
                Buffer.BlockCopy(receive, 0, destination, 0, body); count = body; return true;
            }
            return false;
        }

        /// <summary>Idempotently closes the socket and clears key material held by the MAC.</summary>
        public void Dispose()
        {
            if (disposed) return; disposed = true; socket.Dispose(); sendMac.Dispose(); receiveMac.Dispose();
            Array.Clear(send, 0, send.Length); Array.Clear(receive, 0, receive.Length);
        }
    }
}
