using System.Text.RegularExpressions;

namespace Gemini.Web.Models
{
    public class CertificateInfoViewModel
    {
        public string Name { get; }
        public string Id { get; }
        public DateTime ValidFrom { get; }
        public DateTime ValidUntil { get; }
        public bool Encrypted { get; }

        public CertificateInfoViewModel(CertificateInfo info)
        {
            //Try to extract thecommon name, but if that fails (the field is actually optional) use the full name.
            Name = Regex.Match(info.FriendlyName, "CN=([^,]+)").Groups?[1]?.Value?.Trim() ?? info.FriendlyName;
            Id = info.Id;
            ValidFrom = info.ValidFrom;
            ValidUntil = info.ValidUntil;
            Encrypted = info.Encrypted;
        }
    }
}
