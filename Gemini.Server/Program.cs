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
        }

        builder.ConfigureServices((context, services) =>
        {
            services.AutoRegisterAllAssemblies();
            services.AddHostedService<Service>();
            services.AddTransient((p) => services);
        });

        IHost host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Initialized DI system. Starting application now");
        await host.RunAsync();
        logger.LogInformation("Application has shut down");
    }
}
