using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Lib.Data
{
    /// <summary>
    /// Holds form data supplied by the client
    /// </summary>
    public class FormData
    {
        private readonly Dictionary<string, StringValues> data = new(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// Gets all form keys
        /// </summary>
        public string[] Keys { get; private set; }

        /// <summary>
        /// Gets all indexes that are considered files
        /// </summary>
        public string[] Files => data.Keys.Where(IsFile).ToArray();

        /// <summary>
        /// Gets if this instance is empty
        /// </summary>
        public bool IsEmpty => Keys.Length == 0;

        /// <summary>
        /// Gets if the form data is contained in the body
        /// </summary>
        public bool IsFormDataInBody { get; private set; }

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
        /// <param name="s">Stream to read body data from if needed</param>
        public FormData(string? query, Stream? s)
        {
            ParseQuery(query);
            if (IsFormDataInBody)
            {
                ParseFormDataFromBody(s ?? throw new ArgumentNullException(nameof(s)));
            }
        }

        /// <summary>
        /// Tries to get a value from the form
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="v">Form value</param>
        /// <returns>true, if value found, false otherwise</returns>
        public bool TryGetValue(string key, [NotNullWhen(true)] out StringValues? v)
        {
            return data.TryGetValue(key, out v);
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

        /// <summary>
        /// Validates the file data in the form
        /// </summary>
        /// <returns>true, if valid or empty</returns>
        public bool ValidateFiles()
        {
            var keys = Files;
            if (keys == null || keys.Length == 0)
            {
                return true;
            }
            return keys
                .Select(GetAsFile)
                .OrderBy(m => m.Index)
                .Select(m => (int)m.Index)
                .SequenceEqual(Enumerable.Range(1, keys.Length));
        }

        /// <summary>
        /// Parses form data from an URL encoded body segment
        /// </summary>
        /// <param name="s">Request stream</param>
        private void ParseFormDataFromBody(Stream s)
        {
            var file = GetAsFile(Keys[0]);
            var size = file.Size;
            var data = new byte[size];
            int offset = 0;
            while (offset < data.Length)
            {
                var read = s.Read(data, offset, data.Length - offset);
                offset += read;
                if (read == 0)
                {
                    throw new IOException("Unexpected end of stream when parsing form data from request body");
                }
            }
            ParseQuery("?" + Encoding.UTF8.GetString(data));
        }

        /// <summary>
        /// Parses query data into form data
        /// </summary>
        /// <param name="query">Query data</param>
        [MemberNotNull(nameof(Keys))]
        private void ParseQuery(string? query)
        {
            data.Clear();
            if (string.IsNullOrWhiteSpace(query))
            {
                Keys = Array.Empty<string>();
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
                        data.Add(key, existing);
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
            var allKeys = data.Keys.Select(m => m.ToLower()).OrderBy(m => m).ToList();
            //Remove file based subkeys
            for (var i = 0; i < allKeys.Count; i++)
            {
                var k = allKeys[i];
                allKeys.Remove($"{k}.index");
                allKeys.Remove($"{k}.size");
            }
            Keys = allKeys.ToArray();
            IsFormDataInBody = Keys.Length == 1 && IsFile(Keys[0]) && GetAsFile(Keys[0]).Index == 0;
        }
    }
}