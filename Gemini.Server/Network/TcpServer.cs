using AyrA.AutoDI;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace Gemini.Server.Network
{
    [AutoDIRegister(AutoDIType.Transient)]
    public class TcpServer : IDisposable
    {
        public const int DefaultPort = 1965;

        public delegate void ConnectionHandler(object sender, Socket client, IPEndPoint remoteAddress);

        public event ConnectionHandler Connection = delegate { };

        private readonly ILogger _logger;
        private readonly object _lock = new();
        private IPEndPoint? _bind;
        private TcpListener? _listener;
        private bool _disposed = false;
        private bool _listening = false;
        private Thread? _thread;

        public bool IsListening => _listening;
        public bool IsDisposed => _disposed;
        public IPEndPoint? LocalEndpoint => _bind == null ? null : new(_bind.Address, _bind.Port);

        public TcpServer(ILogger<TcpServer> logger)
        {
            _logger = logger;
        }

        public void Bind(IPEndPoint bind)
        {
            if (_listener != null)
            {
                throw new InvalidOperationException("Socket already listening. Call Stop() before rebinding the socket");
            }
            _logger.LogInformation("Binding listener to {address}", bind);
            _bind = bind;
        }

        public void Bind(IPAddress bindAddr, int bindPort = DefaultPort)
            => Bind(new IPEndPoint(bindAddr, bindPort));

        public void Bind(string bindAddr, int bindPort = DefaultPort)
            => Bind(IPAddress.Parse(bindAddr), bindPort);

        public void Start()
        {
            if (_disposed)
            {
                _logger.LogError("Called .Start() on disposed instance");
                throw new ObjectDisposedException(nameof(TcpServer));
            }
            if (_bind == null)
            {
                _logger.LogError("Attempted to listen without calling .Bind() first");
                throw new InvalidOperationException("Socket has not been bound yet");
            }
            lock (_lock)
            {
                if (_listening)
                {
                    _logger.LogError("Called .Start() on already listening instance");
                    throw new InvalidOperationException("Already listening");
                }
                _logger.LogInformation("Begin listening for TCP connections");
                _listener = new TcpListener(_bind);
                if (_bind.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                }
                _listening = true;
                _thread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = $"TCP listener on {_bind}"
                };
                _thread.Start();
            }
        }

        public void Stop()
        {
            if (_disposed)
            {
                _logger.LogError("Called .Start() on disposed instance");
                throw new ObjectDisposedException(nameof(TcpServer));
            }
            lock (_lock)
            {
                if (!_listening)
                {
                    _logger.LogError("Called .Start() on already listening instance");
                    throw new InvalidOperationException("Already stopped");
                }
                _logger.LogInformation("Stopping TCP listener");
                _listening = false;
                _listener?.Stop();
                _thread?.Join();
                _listener = null;
                _logger.LogInformation("TCP listener stopped");
            }
        }

        public void Dispose()
        {
            _logger.LogDebug("Disposing TCP listener instance");
            GC.SuppressFinalize(this);
            lock (_lock)
            {
                if (_listening)
                {
                    Stop();
                }
                _disposed = true;
            }
        }

        private void ListenLoop()
        {
            _logger.LogDebug("Calling _listener.Start()");
            if (_listener == null)
            {
                _logger.LogCritical("Likely developer error: Thread started but no listener has been set");
                throw new Exception("Likely developer error: Thread started but no listener has been set");
            }
            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to begin listening on {address}", _bind);
                throw;
            }
            _logger.LogInformation("Listener ready to accept connections");
            while (_listening)
            {
                Socket? s = null;
                EndPoint? ep = null;
                try
                {
                    s = _listener.AcceptSocket();
                    ep = s.RemoteEndPoint;
                    if (ep == null)
                    {
                        _logger.LogError("Failed to obtain remote IP info from accepted socket");
                        throw null!;
                    }
                    _logger.LogInformation("New connection {remoteAddress}", ep);
                }
                catch
                {
                    s?.Dispose();
                    s = null;
                    ep = null;
                    //NOOP
                }
                if (s != null && ep != null)
                {
                    _logger.LogDebug("Begin event processing for {remoteAddress}", ep);
                    //Send event in a new thread to not block the listener loop
                    new Thread(delegate ()
                    {
                        Connection(this, s, (IPEndPoint)ep);
                    }).Start();
                }
            }
        }
    }
}
