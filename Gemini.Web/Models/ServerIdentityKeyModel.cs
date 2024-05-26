namespace Gemini.Web.Models
{
    public class ServerIdentityKeyModel
    {
        public string Id { get; set; } = string.Empty;
        public byte[] Certificate { get; set; } = [];
        public DateTime TrustedAt { get; set; } = DateTime.UtcNow;
        public DateTime TrustExpires { get; set; } = DateTime.UtcNow;
    }
}
