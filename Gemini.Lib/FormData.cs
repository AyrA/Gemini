using System.Text.RegularExpressions;

namespace Gemini.Lib
{
    /// <summary>
    /// Holds form data supplied by the client
    /// </summary>
    public class FormData
    {
        private readonly Dictionary<string, StringValues> data = new();
        /// <summary>
        /// Gets all form keys
        /// </summary>
        public string[] Keys => data.Keys.ToArray();

        /// <summary>
        /// Gets all indexes that are considered files
        /// </summary>
        public string[] Files => data.Keys.Where(IsFile).ToArray();

        /// <summary>
        /// Gets the value of a form key
        /// </summary>
        /// <param name="key">Form key</param>
        /// <returns></returns>
        public StringValues this[string key]
        {
            get
            {
                return data[key];
            }
        }

        /// <summary>
        /// Deserializes Data into a formdata instance
        /// </summary>
        /// <param name="query">Data. Usually from <see cref="Uri.Query"/></param>
        public FormData(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }
            var r = new Regex(@"^([^=]*)=(.*)$");
            foreach (var part in query[1..].Split('&'))
            {
                var m = r.Match(part);
                if (m.Success)
                {
                    var key = Uri.UnescapeDataString(m.Groups[1].Value);
                    var value = Uri.UnescapeDataString(m.Groups[2].Value);
                    if (!data.TryGetValue(key, out var existing))
                    {
                        existing = new StringValues();
                        data.Add(part, existing);
                    }
                    existing.Add(value);
                }
                else //No "=" in query string. Assume entire string is the key
                {
                    //Add a key with empty string values if it doesn't exists.
                    if (!data.TryGetValue(part, out var empty))
                    {
                        empty = new StringValues();
                    }
                    data.Add(part, empty);
                }
            }
        }

        /// <summary>
        /// Decodes a form key as a file
        /// </summary>
        /// <param name="key">Form key</param>
        /// <returns>File data</returns>
        /// <exception cref="InvalidOperationException">Not a file</exception>
        public FileData GetAsFile(string key)
        {
            if (
                data.TryGetValue(key, out var fileName) &&
                data.TryGetValue(key + ".index", out var streamIndex) &&
                data.TryGetValue(key + ".size", out var size) &&
                uint.TryParse(streamIndex, out var lIndex) &&
                ulong.TryParse(size, out var lSize))
            {
                return new FileData(fileName, lIndex, lSize);
            }

            throw new InvalidOperationException("Not a file");
        }

        /// <summary>
        /// Checks if the given form key is a file
        /// </summary>
        /// <param name="key">Form key</param>
        /// <returns>true, if a file entry</returns>
        public bool IsFile(string key)
        {
            return
                data.TryGetValue(key, out _) &&
                data.TryGetValue(key + ".index", out var index) &&
                data.TryGetValue(key + ".size", out var size) &&
                uint.TryParse(index, out _) &&
                ulong.TryParse(size, out _);
        }
    }
}