using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EliteWhisper.Services
{
    /// <summary>
    /// Builds an <see cref="SocketsHttpHandler"/> that connects with RFC 8305
    /// "Happy Eyeballs" semantics: the address family returned first by the resolver
    /// is tried immediately, and the other family is started shortly afterwards.
    /// <para>
    /// Without this, .NET tries the resolver's addresses strictly sequentially. When a
    /// machine advertises IPv6 but the route to the host silently drops IPv6 traffic,
    /// the TCP handshake for the first IPv6 address blocks for the operating system
    /// connect timeout and the request fails on the HttpClient timeout instead of
    /// falling back to a healthy IPv4 address.
    /// </para>
    /// </summary>
    public static class HttpConnectivity
    {
        /// <summary>RFC 8305 "Connection Attempt Delay" between the two address families.</summary>
        private const int FamilyFallbackDelayMs = 250;

        /// <summary>How long to wait for the losing attempt to release its socket.</summary>
        private static readonly TimeSpan LoserCleanupTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Creates a handler that races IPv6 and IPv4 instead of blocking on the first
        /// address family returned by DNS.
        /// </summary>
        public static SocketsHttpHandler CreateHandler()
        {
            return new SocketsHttpHandler
            {
                ConnectCallback = ConnectAsync
            };
        }

        private static async ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            // DnsEndPoint is the address the handler wants to connect to (it already
            // accounts for a configured proxy, unlike the original request URI).
            DnsEndPoint target = context.DnsEndPoint;

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(
                target.Host, target.AddressFamily, cancellationToken).ConfigureAwait(false);

            if (addresses.Length == 0)
                throw new SocketException((int)SocketError.HostUnreachable);

            List<IPAddress> primary = new();
            List<IPAddress> secondary = new();
            bool primaryIsV6 = addresses[0].AddressFamily == AddressFamily.InterNetworkV6;

            foreach (IPAddress address in addresses)
            {
                bool isV6 = address.AddressFamily == AddressFamily.InterNetworkV6;
                if (isV6 == primaryIsV6)
                    primary.Add(address);
                else
                    secondary.Add(address);
            }

            using var attemptsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            List<Task<Stream>> attempts = new(2);

            if (primary.Count > 0)
                attempts.Add(ConnectFamilyAsync(primary, target.Port, attemptsCts.Token, TimeSpan.Zero));

            if (secondary.Count > 0)
                attempts.Add(ConnectFamilyAsync(secondary, target.Port, attemptsCts.Token, TimeSpan.FromMilliseconds(FamilyFallbackDelayMs)));

            if (attempts.Count == 0)
                throw new SocketException((int)SocketError.HostUnreachable);

            Exception? lastError = null;
            try
            {
                while (attempts.Count > 0)
                {
                    Task<Stream> completed = await Task.WhenAny(attempts).ConfigureAwait(false);
                    try
                    {
                        // Winner: ownership of the stream (and its socket) passes to the caller.
                        return await completed.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        attempts.Remove(completed);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }
            finally
            {
                attemptsCts.Cancel();
                // Give the losing attempt a short window to unwind so its socket is released
                // before the linked token source is disposed.
                try
                {
                    await Task.WhenAll(attempts).WaitAsync(LoserCleanupTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
            }

            throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
        }

        private static async Task<Stream> ConnectFamilyAsync(
            List<IPAddress> addresses,
            int port,
            CancellationToken cancellationToken,
            TimeSpan initialDelay)
        {
            if (initialDelay > TimeSpan.Zero)
                await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false);

            Exception? lastError = null;

            foreach (IPAddress address in addresses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                bool handedOff = false;
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                    handedOff = true;
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                finally
                {
                    if (!handedOff)
                        socket.Dispose();
                }
            }

            throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
        }
    }
}
