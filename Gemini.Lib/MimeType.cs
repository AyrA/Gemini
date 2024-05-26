using System.Reflection;
using System.Text.RegularExpressions;

namespace Gemini.Lib
{
    /// <summary>
    /// Provides Mime type translation
    /// </summary>
    public partial class MimeType
    {
        /// <summary>
        /// Name of the resource file
        /// </summary>
        private const string ResName = "Gemini.Lib.mime.txt";

        /// <summary>
        /// Default type if none can be determined
        /// </summary>
        public const string DefaultType = "application/octet-stream";

        /// <summary>
        /// Mime type map
        /// </summary>
        private static readonly Dictionary<string, string> mimeMap = [];

        /// <summary>
        /// Initializes the mime type map
        /// </summary>
        static MimeType()
        {
            var a = Assembly.GetExecutingAssembly();
            using var s = a.GetManifestResourceStream(ResName)
                ?? throw new NotImplementedException($"Resource {ResName} was not found in assembly {a.FullName}");
            using var sr = new StreamReader(s);
            var lines = sr.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var r = MimeTypeMapper();
            foreach (var line in lines.Where(m => m[0] != '#'))
            {
                var m = r.Match(line);
                if (m.Success)
                {
                    foreach (var ext in m.Groups[2].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        mimeMap[ext.Trim()] = m.Groups[1].Value;
                    }
                }
            }
            mimeMap["gmi"] = "text/gemini";
        }

        /// <summary>
        /// Build a mime type line from the given arguments
        /// </summary>
        /// <param name="mimeType">Mime type</param>
        /// <param name="properties">Extra attributes</param>
        /// <returns>Formatted mime type line</returns>
        public static string BuildMimeLine(string mimeType, IDictionary<string, string>? properties = null)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                throw new ArgumentException($"'{nameof(mimeType)}' cannot be null or whitespace.", nameof(mimeType));
            }

            if (properties == null || properties.Count == 0)
            {
                return mimeType;
            }

            return mimeType + "; " + string.Join("; ", properties.Select(m => $"{EscapeKey(m.Key)}={EscapeValue(m.Value)}"));
        }

        /// <summary>
        /// Gets a matching mime type for the given file name
        /// </summary>
        /// <param name="fileNameOrExtension">File name, or just the extension (leading dot optional)</param>
        /// <returns>Mime type, or <see cref="DefaultType"/> if none can be found</returns>
        public static string GetMimeType(string fileNameOrExtension)
        {
            if (string.IsNullOrEmpty(fileNameOrExtension))
            {
                return DefaultType;
            }
            var ext = (fileNameOrExtension.Contains('.') ? Path.GetExtension(fileNameOrExtension)[1..] : fileNameOrExtension).ToLower();
            if (string.IsNullOrEmpty(ext))
            {
                return DefaultType;
            }
            return mimeMap.TryGetValue(ext, out var mime) ? mime : DefaultType;
        }

        /// <summary>
        /// Escapes a mime type key
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Escaped key</returns>
        private static string EscapeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }

            return Uri.EscapeDataString(key);
        }

        /// <summary>
        /// Escapes a mime type value
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Escaped value</returns>
        private static string EscapeValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (ControlCharacters().IsMatch(value))
            {
                value = Uri.EscapeDataString(value);
            }

            if (MimeValueEscapeFinder().IsMatch(value))
            {
                return $"\"{value}\"";
            }
            return value;
        }

        [GeneratedRegex(@"[\x00-\x1F]")]
        private static partial Regex ControlCharacters();
        [GeneratedRegex(@"[\s;]")]
        private static partial Regex MimeValueEscapeFinder();
        [GeneratedRegex(@"^(\S+)\s*(.+)$")]
        private static partial Regex MimeTypeMapper();
    }
}
