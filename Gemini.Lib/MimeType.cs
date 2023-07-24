using System.Reflection;
using System.Text.RegularExpressions;

namespace Gemini.Lib
{
    public class MimeType
    {
        private const string ResName = "Gemini.Lib.mime.txt";
        public const string DefaultType = "application/octet-stream";

        private static readonly Dictionary<string, string> mimeMap = new();

        static MimeType()
        {
            var a = Assembly.GetExecutingAssembly();
            using var s = a.GetManifestResourceStream(ResName)
                ?? throw new NotImplementedException($"Resource {ResName} was not found in assembly {a.FullName}");
            using var sr = new StreamReader(s);
            var lines = sr.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var r = new Regex(@"^(\S+)\s*(.+)$");
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
        }

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
    }
}
