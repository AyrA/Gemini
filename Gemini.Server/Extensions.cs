using System.Text.Json;

namespace Gemini.Server
{
    public static class Extensions
    {
        private static readonly JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        public static T? FromJson<T>(this string json)
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }

        public static string ToJson(this object o)
        {
            if (o == null)
            {
                return "null";
            }
            return JsonSerializer.Serialize(o, options);
        }
    }
}
