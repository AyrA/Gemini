using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace Gemini.Server.Network
{
    public class TcpServer : IDisposable
    {
        public const int DefaultPort = 1965;

        public delegate void ConnectionHandler(object sender, Socket client, IPEndPoint remoteAddress);

        public event ConnectionHandler Connection = delegate { };

        private readonly ILogger logger = Tools.GetLogger<TcpServer>();
        private readonly object _lock = new();
        private readonly IPEndPoint _bind;
        private readonly TcpListener _listener;
        private bool _disposed = false;
        private bool _listening = false;
        private Thread? _thread;

        public bool IsListening => _listening;
        public bool IsDisposed => _disposed;
        public IPEndPoint LocalEndpoint => new(_bind.Address, _bind.Port);

        public TcpServer() : this(IPAddress.Any) { }

        public TcpServer(IPEndPoint bind)
        {
            _bind = bind;
            _listener = new TcpListener(bind);
            logger.LogInformation("Created TCP listener for {address}", bind);
        }

        public TcpServer(IPAddress bindAddr, int bindPort = DefaultPort) : this(new IPEndPoint(bindAddr, bindPort)) { }

        public TcpServer(string bindAddr, int bindPort = DefaultPort) : this(IPAddress.Parse(bindAddr), bindPort) { }

        public void Start()
        {
            if (_disposed)
            {
                logger.LogError("Called .Start() on disposed instance");
                throw new ObjectDisposedException(nameof(TcpServer));
            }
            lock (_lock)
            {
                if (_listening)
                {
                    logger.LogError("Called .Start() on already listening instance");
                    throw new InvalidOperationException("Already listening");
                }
                logger.LogInformation("Begin listening for TCP connections");
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
                logger.LogError("Called .Start() on disposed instance");
                throw new ObjectDisposedException(nameof(TcpServer));
            }
            lock (_lock)
            {
                if (!_listening)
                {
                    logger.LogError("Called .Start() on already listening instance");
                    throw new InvalidOperationException("Already stopped");
                }
                logger.LogInformation("Stopping TCP listener");
                _listening = false;
                _listener.Stop();
                _thread?.Join();
                logger.LogInformation("TCP listener stopped");
            }
        }

        public void Dispose()
        {
            logger.LogDebug("Disposing TCP listener instance");
            lock (_lock)
            {
                if (_listening)
                {
                    Stop();
                }
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        private void ListenLoop()
        {
            logger.LogDebug("Calling _listener.Start()");
            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to begin listening on {address}", _bind);
                throw;
            }
            logger.LogInformation("Listener ready to accept connections");
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
                        logger.LogError("Failed to obtain remote IP info from accepted socket");
                        throw null!;
                    }
                    logger.LogInformation("New connection {remoteAddress}", ep);
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
                    logger.LogDebug("Begin event processing for {remoteAddress}", ep);
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
