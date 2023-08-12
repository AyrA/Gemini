using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Lib
{
    /// <summary>
    /// Changes the name of a controller away from the default
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ControllerNameAttribute : Attribute
    {
        /// <summary>
        /// Name of the controller
        /// </summary>
        public string ControllerName { get; }

        /// <summary>
        /// Renames a controller
        /// </summary>
        public ControllerNameAttribute(string controllerName)
        {
            ControllerName = controllerName;
        }
    }

    /// <summary>
    /// Wrapper for a GeminiHost that acts as a controller with 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class GeminiController<T> : GeminiHost where T : GeminiHost
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
        /// Calls the same method of the <typeparamref name="T"/> instance
        /// </summary>
        public override Task<GeminiResponse?> Request(Uri url, IPEndPoint clientAddress, X509Certificate? clientCertificate)
        {
            if (running)
            {
                var funcName = url.PathAndQuery[(url.PathAndQuery.IndexOf('/', 1) + 1)..];
                funcName = funcName[..funcName.IndexOfAny("/?#".ToCharArray())];
                var m = typeof(T).GetMethod(funcName, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
                if (m != null)
                {
                    if (m.ReturnType == typeof(Task<GeminiResponse?>))
                    {
                        var result = m.Invoke(_instance, ConstructArguments(m, url, clientAddress, clientCertificate));
                        if (result != null)
                        {
                            return (Task<GeminiResponse?>)result;
                        }
                        return Task.FromResult<GeminiResponse?>(null);
                    }
                }
                else
                {
                    return _instance.Request(url, clientAddress, clientCertificate);
                }
            }
            return Task.FromResult<GeminiResponse?>(null);
        }

        private object?[] ConstructArguments(MethodInfo method, Uri url, IPEndPoint clientAddress, X509Certificate? clientCertificate)
        {
            //Add all known static arguments
            var known = new Dictionary<Type, object?>
            {
                [typeof(GeminiController<T>)] = this,
                [typeof(T)] = _instance,
                [typeof(Uri)] = url,
                [typeof(IPEndPoint)] = clientAddress,
                [typeof(X509Certificate)] = clientCertificate
            };

            //Add all dynamic arguments
            var query = string.IsNullOrWhiteSpace(url.Query) ? null : Uri.UnescapeDataString(url.Query[1..]);
            known[typeof(string)] = query;

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
