using AyrA.AutoDI;
using Gemini.Lib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gemini.Server
{
    [AutoDIRegister(AutoDIType.Singleton)]
    public class Tools(IServiceProvider provider, IServiceCollection collection, ILogger<Tools> logger)
    {
        public static bool IsGeminiHost(ServiceDescriptor descriptor)
        {
            return descriptor.ServiceType.IsSubclassOf(typeof(GeminiHost));
        }

        public static Type[] GetHosts(IServiceCollection collection)
        {
            return collection
                .Where(IsGeminiHost)
                .Select(m => m.ServiceType)
                .Distinct()
                .ToArray();
        }

        public async Task StartHosts()
        {
            using var scope = provider.CreateAsyncScope();
            var hosts = GetHosts(collection).Select(m => (GeminiHost)provider.GetRequiredService(m));
            await Parallel.ForEachAsync(hosts, (host, token) =>
            {
                try
                {
                    host.Start();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Host {type} failed to start", host.GetType());
                }
                return ValueTask.CompletedTask;
            });
        }

        public async Task StopHosts()
        {
            using var scope = provider.CreateAsyncScope();
            var hosts = GetHosts(collection).Select(m => (GeminiHost)provider.GetRequiredService(m));
            await Parallel.ForEachAsync(hosts, (host, token) =>
            {
                try
                {
                    host.Stop();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Host {type} failed to stop", host.GetType());
                }
                return ValueTask.CompletedTask;
            });
        }
    }
}
