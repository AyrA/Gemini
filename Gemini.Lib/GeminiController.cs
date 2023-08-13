using Gemini.Lib.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Lib
{
    /// <summary>
    /// Holds public methods that do not depend on the generic type
    /// </summary>
    public interface IGeminiController
    {
        /// <summary>
        /// Calls the mapped controller method of the generic type instance
        /// </summary>
        Task<GeminiResponse?> Request(RequestState state);
    }

    /// <summary>
    /// Wrapper for a GeminiHost that acts as a controller with 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class GeminiController<T> : GeminiHost, IGeminiController where T : GeminiHost
    {
        private bool running;
        private readonly string _controllerName;
        private readonly T _instance;

        /// <summary>
        /// DI
        /// </summary>
        public GeminiController(IServiceProvider provider)
        {
            var attr = typeof(T).GetCustomAttribute<ControllerNameAttribute>();
            if (attr != null)
            {
                _controllerName = attr.ControllerName;
            }
            else
            {
                _controllerName = typeof(T).Name.ToLower();
            }
            _instance = provider.GetRequiredService<T>();
        }

        /// <summary>
        /// Disposes this instance as well as the instance of <typeparamref name="T"/>
        /// </summary>
        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            _instance.Dispose();
            running = false;
        }

        /// <summary>
        /// Starts the instance of <typeparamref name="T"/>
        /// </summary>
        public override bool Start()
        {
            try
            {
                return running = _instance.Start();
            }
            catch
            {
                running = false;
                throw;
            }
        }

        /// <summary>
        /// Stops the instance of <typeparamref name="T"/>
        /// </summary>
        public override void Stop()
        {
            _instance.Stop();
            running = false;
        }

        /// <summary>
        /// Checks if the URL matches the controller name,
        /// then calls the same method of the <typeparamref name="T"/> instance
        /// </summary>
        public override bool IsAccepted(Uri url, IPAddress remoteAddress, X509Certificate? clientCertificate)
        {
            return
                running &&
                url.PathAndQuery.ToLower().StartsWith($"/{_controllerName}/".ToLower()) &&
                _instance.IsAccepted(url, remoteAddress, clientCertificate);
        }

        /// <summary>
        /// Calls the same method of the <typeparamref name="T"/> instance
        /// </summary>
        public override Uri Rewrite(Uri url, IPAddress remoteAddress, X509Certificate? clientCertificate)
        {
            return running ? _instance.Rewrite(url, remoteAddress, clientCertificate) : url;
        }

        /// <summary>
        /// Calls the mapped controller method of the <typeparamref name="T"/> instance
        /// </summary>
        public override Task<GeminiResponse?> Request(Uri url, IPEndPoint clientAddress, X509Certificate? clientCertificate)
        {
            using var state = new RequestState(url, clientAddress, clientCertificate, null);
            return Request(state);
        }

        /// <summary>
        /// Calls the mapped controller method of the <typeparamref name="T"/> instance
        /// </summary>
        public async Task<GeminiResponse?> Request(RequestState state)
        {
            if (running)
            {
                var funcName = state.Url.PathAndQuery[(state.Url.PathAndQuery.IndexOf('/', 1) + 1)..];
                var endIndex = funcName.IndexOfAny("/?#".ToCharArray());
                if (endIndex > 0)
                {
                    funcName = funcName[..endIndex];
                }
                var m = typeof(T).GetMethod(funcName, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
                if (m != null)
                {
                    if (m.ReturnType == typeof(Task<GeminiResponse?>))
                    {
                        if (m.Invoke(_instance, await ConstructArgumentsAsync(m, state)) is Task<GeminiResponse?> result)
                        {
                            return await result;
                        }
                        return null;
                    }
                }
                else
                {
                    return await _instance.Request(state.Url, state.ClientAddress, state.ClientCertificate);
                }
            }
            return null;
        }

        private async Task<object?[]> ConstructArgumentsAsync(MethodInfo method, RequestState state)
        {
            //Add all known static arguments
            var known = new Dictionary<Type, object?>
            {
                [typeof(GeminiController<T>)] = this,
                [typeof(IGeminiController)] = this,
                [typeof(T)] = _instance,
                [typeof(Uri)] = state.Url,
                [typeof(IPEndPoint)] = state.ClientAddress,
                [typeof(IPAddress)] = state.ClientAddress.Address,
                [typeof(X509Certificate)] = state.ClientCertificate,
                [typeof(Stream)] = state.DataStream
            };

            //Add all dynamic arguments
            var query = string.IsNullOrWhiteSpace(state.Url.Query) ? null : state.Url.Query[1..];
            known[typeof(string)] = query;
            known[typeof(FormData)] = state.Form;
            known[typeof(FileDataCollection)] = state.Files;

            //Extract files from stream
            await state.LoadFiles();

            var args = method.GetParameters();
            var ret = new object?[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i].ParameterType;
                if (known.TryGetValue(arg, out var value))
                {
                    ret[i] = value;
                }
                else
                {
                    throw new Exception($"Don't know how to map argument of type {arg.FullName}");
                }
            }
            return ret;
        }
    }
}
