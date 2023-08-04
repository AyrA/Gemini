using AyrA.AutoDI;
using Gemini.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

internal class Program
{
    private static async Task Main(string[] args)
    {
        //Register Gemini URI scheme with the HTTP handler because it's similar.
        //Gemini lacks the URI fragment but we don't care.
        UriParser.Register(new HttpStyleUriParser(), "gemini", 1965);

        var isService = args.Any(m => m.ToLower() == "/service");
        IHostBuilder builder = Host.CreateDefaultBuilder(args);

        if (isService)
        {
            //Run as windows service
            builder.UseWindowsService(options => { options.ServiceName = ".NET Gemini host"; });
            builder.ConfigureServices((context, services) =>
            {
                if (OperatingSystem.IsWindows())
                {
                    LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(services);
                }
                // See: https://github.com/dotnet/runtime/issues/47303
                services.AddLogging(builder =>
                {
                    builder.AddConfiguration(context.Configuration.GetSection("Logging"));
                });
            });
        }
        else
        {
            builder.UseConsoleLifetime();
            /*
            //Run as plain console application
            var server = new TcpServer(IPAddress.Loopback);
            server.Connection += GeminiRequestHandler.Tcp_Handler;
            server.Start();
            Debugging.DumbClient(new IPEndPoint(IPAddress.Loopback, TcpServer.DefaultPort));
            Thread.CurrentThread.Join();
            //*/
        }

        builder.ConfigureServices((context, services) =>
        {
            services.AutoRegisterAllAssemblies();
            services.AddHostedService<Service>();
            services.AddTransient((p) => services);
        });

        IHost host = builder.Build();
        //var task1 = host.StartAsync();
        //var task2 = host.RunAsync();
        //await Task.WhenAll(task1, task2);
        host.Run();
    }
}
