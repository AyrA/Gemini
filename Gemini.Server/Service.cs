using AyrA.AutoDI;
using Gemini.Lib.Services;
using Gemini.Server.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gemini.Server
{
    [AutoDIRegister(AutoDIType.Singleton)]
    internal class Service : BackgroundService
    {
        public static readonly string ConfigFile = Path.Combine(AppContext.BaseDirectory, "TCP.json");

        private readonly ILogger<Service> _logger;
        private readonly SemaphoreSlim _semaphore;
        private readonly IServiceProvider _provider;
        private readonly CertificateService _certService;
        private readonly Tools _tools;
        private readonly List<ListenerConfig> _servers = new();

        private class ListenerConfig : IDisposable
        {
            public string? Listen { get; set; }

            public string? Certificate { get; set; }

            [JsonIgnore]
            public TcpServer? Server { get; private set; }

            [JsonIgnore]
            public GeminiRequestHandler? Handler { get; set; }

            [MemberNotNull(nameof(Server))]
            public void CreateServer(IServiceProvider provider)
            {
                var logger = provider.GetRequiredService<ILogger<ListenerConfig>>();
                if (Server != null)
                {
                    throw new InvalidOperationException();
                }
                if (string.IsNullOrEmpty(Listen))
                {
                    throw new InvalidOperationException("Listener string is empty or null");
                }

                if (!IPEndPoint.TryParse(Listen, out var ep))
                {
                    throw new Exception($"'{Listen}' is not a valid IP endpoint value");
                }
                Server = provider.GetRequiredService<TcpServer>();
                try
                {
                    Server.Bind(ep);
                    logger.LogInformation("TCP endpoint bound to {endpoint}", ep);
                }
                catch (Exception ex)
                {
                    Server.Dispose();
                    logger.LogError(ex, "Unable to bind to {endpoint}", ep);
                }
            }

            [MemberNotNull(nameof(Server))]
            public void Start()
            {
                if (Server == null)
                {
                    throw new InvalidOperationException("Server has not been created yet");
                }
                if (Server.IsListening)
                {
                    throw new InvalidOperationException("Server is already listening");
                }
                if (Handler == null)
                {
                    throw new InvalidOperationException("Request handler has not been assigned");
                }
                Server.Start();
            }

            public void Stop()
            {
                if (Server == null)
                {
                    throw new InvalidOperationException("Server has not been created yet");
                }
                if (!Server.IsListening)
                {
                    throw new InvalidOperationException("Server is not running");
                }
                Server.Stop();
            }

            public void Validate()
            {
                if (string.IsNullOrEmpty(Listen))
                {
                    throw new Exception("Listener endpoint not specified");
                }
                if (!IPEndPoint.TryParse(Listen, out _))
                {
                    throw new Exception($"Listener endpoint '{Listen}' is invalid");
                }
            }

            public void Dispose()
            {
                Server?.Dispose();
                Server = null;
                Handler = null;
                GC.SuppressFinalize(this);
            }
        }

        public Service(ILogger<Service> logger, IServiceProvider provider, CertificateService certService, Tools tools)
        {
            _logger = logger;
            _semaphore = new SemaphoreSlim(1);
            _provider = provider;
            _certService = certService;
            _tools = tools;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting service");

            var configs = JsonSerializer.Deserialize<ListenerConfig[]>(File.ReadAllText(ConfigFile)) ??
                throw new IOException($"Unable to deserialize {ConfigFile}");
            if (configs.Length == 0)
            {
                throw new Exception("TCP configuration is empty");
            }
            _logger.LogInformation("Found {count} listener configurations", configs.Length);
            _servers.ForEach(server => server.Stop());
            _servers.Clear();
            foreach (var config in configs)
            {
                try
                {
                    config.Validate();
                    _logger.LogInformation("Starting new listener on {ep}", config.Listen);
                    config.CreateServer(_provider);
                    config.Handler = _provider.GetRequiredService<GeminiRequestHandler>();
                    if (string.IsNullOrWhiteSpace(config.Certificate))
                    {
                        _logger.LogWarning("No certificate file name or thumbprint specified. Using temporary certificate");
                        config.Handler.UseDeveloperCertificate();
                    }
                    else
                    {
                        config.Handler.ServerCertificate = LoadCertificate(config);
                    }
                    config.Server.Connection += config.Handler.TcpServerEventHandler;
                    config.Start();
                    _servers.Add(config);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start listener. Skipping this entry.");
                    config.Dispose();
                }
            }
            if (_servers.Count == 0)
            {
                throw new Exception("No active listeners");
            }
            return _semaphore.WaitAsync(stoppingToken);
        }

        private X509Certificate2 LoadCertificate(ListenerConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.Certificate))
            {
                _logger.LogWarning("No certificate file name or thumbprint specified in listener for {endpoint}. Using temporary certificate", config.Listen);
                return _certService.CreateOrLoadDevCert();
            }
            if (Path.IsPathFullyQualified(config.Certificate))
            {
                return _certService.ReadFromFile(config.Certificate, null);
            }
            var relPath = Path.Combine(AppContext.BaseDirectory, config.Certificate);
            if (File.Exists(relPath))
            {
                return _certService.ReadFromFile(config.Certificate, null);
            }
            if (CertificateService.IsValidThumbprint(config.Certificate))
            {
                return _certService.ReadFromStore(config.Certificate);
            }
            throw new ArgumentException($"'{config.Certificate}' cannot be loaded");
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting hosts");
            //Consume the only available semaphore handle
            await _semaphore.WaitAsync(cancellationToken);
            await _tools.StartHosts();
            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Service is shutting down");
            //Stop all listeners first
            foreach (var server in _servers)
            {
                try
                {
                    server.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Server {listen} refused to stop", server.Listen);
                }
                finally
                {
                    server.Dispose();
                }
            }
            //Stop all hosts
            await _tools.StopHosts();
            //Permit restart
            _semaphore.Release();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _semaphore.Dispose();
            GC.SuppressFinalize(this);
            base.Dispose();
        }
    }
}
