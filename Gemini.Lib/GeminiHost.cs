using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Lib
{
    /// <summary>
    /// Interface for a gemini host component
    /// </summary>
    public abstract class GeminiHost : IDisposable
    {
        /// <summary>
        /// The maximum internal host priority in use
        /// </summary>
        /// <remarks>
        /// This is never the biggest possible value 
        /// to ensure custom hosts can be registered as fallback request handler
        /// </remarks>
        public const ushort MaxInternalHost = 0xFFFE;
        /// <summary>
        /// The minimum priority found on internal hosts
        /// </summary>
        public const ushort MinInternalHost = 0xFF00;
        /// <summary>
        /// The maximum priority that external hosts can use to be processed before internal hosts.
        /// </summary>
        /// <remarks>
        /// This number is guaranteed to be less than <see cref="MinInternalHost"/>,
        /// but it's not guaranteed to be only 1 less.
        /// </remarks>
        public const ushort MaxExternalHost = MinInternalHost - 1;

        /// <summary>
        /// Gets the priority of the host.
        /// Lower indicates higher priority in the request pipeline
        /// </summary>
        public ushort Priority { get; protected set; } = MaxExternalHost;

        /// <summary>
        /// Called once by the hosting environment
        /// </summary>
        /// <returns>
        /// true, if starting was successful.
        /// If false, the host will not be loaded into the request pipeline
        /// </returns>
        /// <remarks>
        /// The hosting environment guarantees that this method is called at least once
        /// before the first access to a public property or other method,
        /// but it may be called multiple times.
        /// </remarks>
        public virtual bool Start() => true;

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
        /// The default implementation is to not rewrite and return the argument as-is.
        /// The method is called once before every call to <see cref="Request(Uri, IPEndPoint, X509Certificate?)"/>.
        /// By implementing the Rewrite method but hardcoding <see cref="Request(Uri, IPEndPoint, X509Certificate?)"/>
        /// to always return null, a gemini host can effectively be turned into a pure URL rewrite mapper.
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
        /// a call to <see cref="Rewrite(Uri, IPAddress, X509Certificate?)"/>
        /// and <see cref="Request(Uri, IPEndPoint, X509Certificate?)"/> follows.
        /// If this returns false, the host is skipped once for this request.
        /// The default implementation accepts all requests
        /// </remarks>
        public virtual bool IsAccepted(Uri url, IPAddress remoteAddress, X509Certificate? clientCertificate) => true;

        /// <summary>
        /// Disposes this instance
        /// </summary>
        public abstract void Dispose();
    }
}
