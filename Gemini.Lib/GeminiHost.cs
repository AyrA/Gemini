using Gemini.Lib.Network;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Lib
{
    /// <summary>
    /// Interface for a gemini host component
    /// </summary>
    public abstract class GeminiHost
    {
        /// <summary>
        /// Host names this instance is accepting requests for.
        /// An empty list (or null) is equivalent to a single "*" entry.
        /// </summary>
        /// <remarks>
        /// The port specification is optional.
        /// If not supplied, all listening ports forward to the specified host.
        /// A special wildcard host "*" can be specified to listen for all hosts.
        /// The value is checked against the host portion of every client request.
        /// </remarks>
        public virtual string[]? HostNames { get; protected set; }

        /// <summary>
        /// If not empty or null, contains the remote IP ranges permitted to connect
        /// </summary>
        public virtual IpRange[]? RemoteAddressRestrictions { get; protected set; }

        /// <summary>
        /// Called once by the hosting environment
        /// </summary>
        /// <remarks>
        /// The hosting environment guarantees that this method is called exactly once
        /// before the first access to a public property or other method
        /// </remarks>
        public virtual void Start() { }

        /// <summary>
        /// Called once by the hosting environment during shutdown
        /// </summary>
        /// <remarks>
        /// The hosting environment guarantees that during a clean shutdown,
        /// this is called exactly once.
        /// This is done regardless of whether the current gemini host may still be processing requests or not
        /// </remarks>
        public virtual void Stop() { }

        /// <summary>
        /// Rewrites an URL
        /// </summary>
        /// <param name="url">URL</param>
        /// <param name="remoteAddress">IP address of the request</param>
        /// <param name="clientCertificate">Client certificate if provided by the connecting party</param>
        /// <returns>Rewritten URL</returns>
        /// <remarks>
        /// The rewritten URL replaces the passed in URL in the request pipeline for all future hosts.
        /// If no rewrite functionality for a host is desired,
        /// the method can simply return the <paramref name="url"/> argument as-is.
        /// The method is called once before every call to <see cref="Request(Uri, EndPoint)"/>.
        /// By implementing the Rewrite method but hardcoding <see cref="Request(Uri, EndPoint)"/>
        /// to always return false, a gemini host can effectively be turned into a pure URL rewrite mapper.
        /// Returning null will terminate the request early.
        /// </remarks>
        public virtual Uri Rewrite(Uri url, IPAddress remoteAddress, X509Certificate? clientCertificate) => url;

        /// <summary>
        /// Processes a gemini request
        /// </summary>
        /// <param name="url">URL as requested by the client</param>
        /// <param name="clientAddress">remote IP address</param>
        /// <param name="clientCertificate">Client certificate if provided by the connecting party</param>
        /// <returns>
        /// Gemini response.
        /// If a call to this method returns null, the request is passed on to the next host in line
        /// </returns>
        public abstract Task<GeminiResponse?> Request(Uri url, IPEndPoint clientAddress, X509Certificate? clientCertificate);

        /// <summary>
        /// Checks whether the url and remote IP combination is allowed to use this host.
        /// </summary>
        /// <param name="url">URL</param>
        /// <param name="remoteAddress">Remote IP address</param>
        /// <param name="clientCertificate">Client certificate if provided by the connecting party</param>
        /// <returns>true, if allowed, false otherwise</returns>
        /// <remarks>
        /// If this method returns true,
        /// a call to <see cref="Rewrite(Uri)"/>
        /// and <see cref="Request(Uri, EndPoint)"/> follows.
        /// If this returns false, the host is skipped once for this request.
        /// Hosts that accept all requests should override this implementation
        /// with a simple one to gain extra performance by not performing checks.
        /// The default implementation checks against
        /// <see cref="HostNames"/> and <see cref="RemoteAddressRestrictions"/>.
        /// </remarks>
        public virtual bool IsAccepted(Uri url, IPAddress remoteAddress, X509Certificate? clientCertificate)
        {
            if (url == null)
            {
                return false;
            }

            //Check IP restriction first, if any
            var ranges = RemoteAddressRestrictions;
            if (ranges != null && ranges.Length > 0)
            {
                var passed = false;
                foreach (var range in ranges)
                {
                    if (passed = range.InRange(remoteAddress))
                    {
                        break;
                    }
                }
                if (!passed)
                {
                    return false;
                }
            }

            var host = url.Host.ToLower();
            if (url.IsDefaultPort)
            {
                host += ":1965";
            }
            var hosts = HostNames;
            //Empty lists accept all hosts
            if (hosts == null || hosts.Length == 0)
            {
                return true;
            }
            //Wildcard host matching
            if (hosts.Contains("*") || hosts.Contains("*:*") || hosts.Contains($"*:{url.Port}"))
            {
                return true;
            }
            foreach (var hostEntry in hosts)
            {
                var entry = hostEntry?.ToLower();
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }
                if (entry == host || entry + ":1965" == host)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
