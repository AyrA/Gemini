using AyrA.AutoDI;
using Gemini.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using Plugin;

internal class Program
{
    private static async Task Main(string[] args)
    {
#if DEBUG
        AutoDIExtensions.Logger = Console.Error;
        AutoDIExtensions.DebugLogging = true;
#endif

        ArgumentParser parser;
        try
        {
            parser = ArgumentParser.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to parse command line arguments. {0}", ex.Message);
            return;
        }

        if (parser.IsHelp)
        {
            Console.WriteLine(@"Gemini.Server [/service | /install <file> | /uninstall <id>]
Runs the extendable gemini server

/service     Runs the server as a windows service
/install     Installs or updates the specified plugin
/uninstall   Uninstalls the plugin with the given id
<file>       Path to zip file to install
<id>         Id to uninstall

The server runs as normal console application without any arguments
");
            return;
        }

        //Register Gemini URI scheme with the HTTP handler because it's similar.
        //Gemini lacks the URI fragment but we don't care.
        UriParser.Register(new HttpStyleUriParser(), "gemini", 1965);


        if (parser.IsInstall)
        {
            try
            {
                GeminiHostInstaller.Install(parser.InstallFile!);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to install the plugin. {0}", ex.Message);
            }
            return;
        }
        else if (parser.IsUninstall)
        {
            try
            {
                GeminiHostInstaller.Uninstall(parser.UninstallId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to delete the plugin. {0}", ex.Message);
            }
            return;
        }
        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        if (parser.IsService)
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
            GeminiHostInstaller.LoadPlugins(services);
            services.AutoRegisterAllAssemblies();
#if DEBUG
            services.AutoRegisterFromAssembly(typeof(MathsHost).Assembly);
#endif
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
