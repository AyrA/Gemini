using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Gemini.Web.Models
{
    /// <summary>
    /// Represents mime type information on successful gemini requests
    /// </summary>
    public class MimeInformation
    {
        /// <summary>
        /// Gets the mime type in "type/subtype" format
        /// </summary>
        public string MimeType { get; }

        /// <summary>
        /// Gets the encoding component that can be used to decode the data into a string
        /// </summary>
        /// <remarks>
        /// This is determined by examining the "charset" mime attribute.
        /// If the attribute is not defined,
        /// this defaults to UTF8 if the mime type is of text/*,
        /// otherwise it's set to null
        /// </remarks>
        [JsonIgnore]
        public Encoding? Encoding { get; }

        /// <summary>
        /// Gets the charset name of <see cref="Encoding"/>
        /// </summary>
        /// <remarks>
        /// This has integrated null check
        /// and returns null if <see cref="Encoding"/> is null
        /// </remarks>
        public string? Charset => Encoding?.WebName;

        /// <summary>
        /// Gets all extra mime attributes supplied by the mime type declaration
        /// </summary>
        public Dictionary<string, string> ExtraInfo { get; } = [];

        public MimeInformation(ILogger logger, string mimeLine)
        {
            if (string.IsNullOrWhiteSpace(mimeLine))
            {
                logger.LogInformation("No extra info provided. Using defaults");
                MimeType = "text/gemini";
                Encoding = Encoding.UTF8;
            }
            else
            {
                var parts = mimeLine.Split(';').Select(m => m.Trim()).ToArray();
                if (!Regex.IsMatch(parts[0], @"^[^/\s]+/\S+$"))
                {
                    logger.LogWarning("[Protocol violation] Invalid mime type declaration: {mime}", parts[0]);
                    throw new FormatException($"Invalid mime type declaration: '{parts[0]}'");
                }

                MimeType = parts[0];
                foreach (var part in parts.Skip(1).Where(m => !string.IsNullOrWhiteSpace(m)))
                {
                    var m = Regex.Match(part, @"^([^=]+)=(.*)$");
                    if (m.Success)
                    {
                        var name = m.Groups[1].Value;
                        var value = m.Groups[2].Value;
                        logger.LogInformation("Parsing mime info '{name}' = '{value}'", name, value);
                        if (name == "charset")
                        {
                            try
                            {
                                Encoding = Encoding.GetEncoding(value);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Unknown encoding: {encoding}; Falling back to UTF-8", value);
                                Encoding = Encoding.UTF8;
                            }
                        }
                        if (ExtraInfo.ContainsKey(name))
                        {
                            logger.LogWarning("[Protocol violation] Mime key {key} already defined", name);
                        }
                        else
                        {
                            ExtraInfo[name] = value;
                        }
                    }
                    else
                    {
                        logger.LogWarning("[Protocol violation] Invalid mime type extra info: {part}", part);
                        throw new FormatException($"Invalid mime type extra info: {part}");
                    }
                }
                //Fallback to UTF-8 for all text if nothing was defined
                if (Encoding == null && MimeType.ToLower().StartsWith("text/"))
                {
                    Encoding = Encoding.UTF8;
                }
            }
        }
    }
}
