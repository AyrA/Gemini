using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Gemini.Web.Extensions
{
    public static class JsonExtensions
    {
        public static string ToJson(this object obj, bool pretty = false)
        {
            if (obj == null)
            {
                return "null";
            }
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions() { WriteIndented = pretty });
        }

        [return: MaybeNull]
        public static T FromJson<T>(this string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                throw new ArgumentException($"'{nameof(s)}' cannot be null or whitespace.", nameof(s));
            }

            if (s == "null")
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(s);
        }
    }
}
