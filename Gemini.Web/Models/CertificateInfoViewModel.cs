using System.Text.RegularExpressions;

namespace Gemini.Web.Models
{
    public class CertificateInfoViewModel(CertificateInfo info)
    {
        public string Name { get; } = Regex.Match(info.FriendlyName, "CN=([^,]+)").Groups?[1]?.Value?.Trim() ?? info.FriendlyName;
        public string Id { get; } = info.Id;
        public DateTime ValidFrom { get; } = info.ValidFrom;
        public DateTime ValidUntil { get; } = info.ValidUntil;
        public bool Encrypted { get; } = info.Encrypted;
    }
}
