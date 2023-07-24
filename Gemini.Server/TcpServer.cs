using System.Net;
using System.Net.Sockets;

namespace Gemini.Server
{
    public class TcpServer : IDisposable
    {
        public const int DefaultPort = 1965;

        public delegate void ConnectionHandler(object sender, Socket client, IPEndPoint remoteAddress);

        public event ConnectionHandler Connection = delegate { };

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
        }

        public TcpServer(IPAddress bindAddr, int bindPort = DefaultPort) : this(new IPEndPoint(bindAddr, bindPort)) { }

        public TcpServer(string bindAddr, int bindPort = DefaultPort) : this(IPAddress.Parse(bindAddr), bindPort) { }

        public void Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TcpServer));
            }
            lock (_lock)
            {
                if (_listening)
                {
                    throw new InvalidOperationException("Already listening");
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
                throw new ObjectDisposedException(nameof(TcpServer));
            }
            lock (_lock)
            {
                if (!_listening)
                {
                    throw new InvalidOperationException("Already stopped");
                }
                _listening = false;
                _listener.Stop();
                _thread?.Join();
            }
        }

        public void Dispose()
        {
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
            _listener.Start();
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
                        throw null!;
                    }
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
