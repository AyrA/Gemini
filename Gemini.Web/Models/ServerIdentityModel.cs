namespace Gemini.Web.Models
{
    public class ServerIdentityModel
    {
        public string Host { get; set; } = string.Empty;
        public List<ServerIdentityKeyModel> PublicKeys { get; set; } = [];
    }
}
