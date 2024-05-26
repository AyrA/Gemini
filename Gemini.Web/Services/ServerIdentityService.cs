using AyrA.AutoDI;
using Gemini.Web.Models;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Gemini.Web.Services
{
    [AutoDIRegister(AutoDIType.Singleton)]
    public class ServerIdentityService
    {
        private readonly string certSerialFile;

        private readonly Dictionary<string, ServerIdentityModel> keys;

        public ServerIdentityService()
        {
            certSerialFile = Path.Combine(AppContext.BaseDirectory, "Cert", "server.id");
            keys = [];
            Reload();
            if (Cleanup() > 0)
            {
                Save();
            }
        }

        private int Cleanup()
        {
            var now = DateTime.UtcNow;
            int removed = 0;
            lock (keys)
            {
                var indexes = keys.Keys.ToArray();
                foreach (var index in indexes)
                {
                    removed += keys[index].PublicKeys.RemoveAll(m => m.TrustExpires < now);
                    if (keys[index].PublicKeys.Count == 0)
                    {
                        keys.Remove(index);
                    }
                }
            }
            return removed;
        }

        private void Reload()
        {
            lock (keys)
            {
                if (File.Exists(certSerialFile))
                {
                    var content = File.ReadAllText(certSerialFile);
                    var json = JsonSerializer.Deserialize<List<ServerIdentityModel>>(content) ?? [];
                    keys.Clear();
                    foreach (var entry in json)
                    {
                        keys[entry.Host] = entry;
                    }
                }
                else
                {
                    keys.Clear();
                }
            }
        }

        private void Save()
        {
            lock (keys)
            {
                File.WriteAllText(certSerialFile, JsonSerializer.Serialize(keys.Values.ToArray()));
            }
        }

        public ServerIdentityModel[] GetAll()
        {
            return [.. keys.Values];
        }

        public bool RemoveKey(string host, string id)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentException($"'{nameof(host)}' cannot be null or empty.", nameof(host));
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException($"'{nameof(id)}' cannot be null or empty.", nameof(id));
            }
            host = host.ToUpper();
            lock (keys)
            {
                if (keys.TryGetValue(host, out var value))
                {
                    if (0 < value.PublicKeys.RemoveAll(m => m.Id == id.ToUpper()))
                    {
                        Cleanup();
                        Save();
                        return true;
                    }
                }
            }
            return false;
        }

        public bool RemoveKey(string host, byte[] key)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentException($"'{nameof(host)}' cannot be null or empty.", nameof(host));
            }

            ArgumentNullException.ThrowIfNull(key);
            host = host.ToUpper();
            lock (keys)
            {
                if (keys.TryGetValue(host, out var value))
                {
                    if (0 < value.PublicKeys.RemoveAll(m => m.Certificate.SequenceEqual(key)))
                    {
                        Cleanup();
                        Save();
                        return true;
                    }
                }
            }
            return false;
        }

        public void RemoveAll(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentException($"'{nameof(host)}' cannot be null or empty.", nameof(host));
            }
            lock (keys)
            {
                if (keys.Remove(host.ToUpper()))
                {
                    Save();
                }
            }
        }

        public void Clear()
        {
            lock (keys)
            {
                keys.Clear();
            }
        }

        public bool CheckServerTrust(string host, X509Certificate cert)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentException($"'{nameof(host)}' cannot be null or empty.", nameof(host));
            }

            host = host.ToUpper();

            if (!keys.ContainsKey(host))
            {
                return false;
            }
            ArgumentNullException.ThrowIfNull(cert);
            var key = cert.GetPublicKey();
            return keys[host].PublicKeys.Any(m => m.Id == cert.GetCertHashString().ToUpper());
        }

        public ServerIdentityKeyModel AddServerTrust(string host, X509Certificate2 cert)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentException($"'{nameof(host)}' cannot be null or empty.", nameof(host));
            }
            host = host.ToUpper();
            if (!keys.TryGetValue(host, out var entry))
            {
                keys.Add(host, entry = new ServerIdentityModel());
                entry.Host = host;
            }

            if (!CheckServerTrust(host, cert))
            {
                entry.PublicKeys.Add(new ServerIdentityKeyModel()
                {
                    Id = cert.GetCertHashString().ToUpper(),
                    Certificate = cert.GetRawCertData(),
                    TrustedAt = DateTime.UtcNow,
                    TrustExpires = cert.NotAfter.ToUniversalTime()
                });
            }
            Save();
            return entry.PublicKeys.First(m => m.Id == cert.GetCertHashString().ToUpper());
        }

        public ServerIdentityKeyModel AddServerTrust(string host, byte[] cert)
        {
            using var c = new X509Certificate2(cert);
            return AddServerTrust(host, c);
        }
    }
}
