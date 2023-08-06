using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Lib
{
    /// <summary>
    /// Represents the response to a gemini request
    /// </summary>
    public class GeminiResponse : IDisposable
    {
        /// <summary>
        /// Default status line for success messages
        /// </summary>
        public const string DefaultStatus = "text/gemini; charset=utf-8";

        /// <summary>
        /// Gets or sets the gemini status code
        /// </summary>
        public StatusCode StatusCode { get; set; } = StatusCode.Success;

        /// <summary>
        /// Gets or sets the body.
        /// Consider using <see cref="SetBytes(byte[])"/>
        /// or <see cref="SetStream(Stream)"/>
        /// or <see cref="SetText(string)"/> instead
        /// </summary>
        /// <remarks>
        /// This is only valid if <see cref="StatusCode"/> is <see cref="StatusCode.Success"/>
        /// </remarks>
        public Stream? Body { get; set; }

        /// <summary>
        /// Gets or sets the status line that's appended to <see cref="StatusCode"/>
        /// </summary>
        public string Status { get; set; } = DefaultStatus;

        /// <summary>
        /// Creates an empty gemini response
        /// </summary>
        /// <remarks>
        /// The default status code is <see cref="StatusCode.Success"/>,
        /// the body is empty, and the content type is <see cref="DefaultStatus"/>
        /// </remarks>
        public GeminiResponse() { }

        /// <summary>
        /// Creates a new gemini response
        /// </summary>
        /// <param name="statusCode">Response status code</param>
        /// <param name="body">Response body</param>
        /// <param name="status">Status line</param>
        /// <remarks><paramref name="body"/> is disposed automatically after processing</remarks>
        public GeminiResponse(StatusCode statusCode, Stream? body, string status)
        {
            StatusCode = statusCode;
            Body = body;
            Status = status;
        }

        /// <summary>
        /// Sets the given text as body data
        /// </summary>
        /// <param name="text">Text</param>
        /// <remarks>Text will be UTF-8 encoded</remarks>
        public void SetText(string text)
        {
            if (Body != null)
            {
                throw new InvalidOperationException("Body has already been set");
            }
            Body = new MemoryStream(Encoding.UTF8.GetBytes(text), false);
        }

        /// <summary>
        /// Sets the given bytes as body data
        /// </summary>
        /// <param name="data">Data</param>
        public void SetBytes(byte[] data)
        {
            if (Body != null)
            {
                throw new InvalidOperationException("Body has already been set");
            }
            Body = new MemoryStream(data, false);
        }

        /// <summary>
        /// Sets the stream used for the body content
        /// </summary>
        /// <param name="data">Stream</param>
        /// <exception cref="InvalidOperationException">Body already set</exception>
        /// <remarks><paramref name="data"/> is disposed automatically after processing</remarks>
        public void SetStream(Stream data)
        {
            if (Body != null)
            {
                throw new InvalidOperationException("Body has already been set");
            }
            Body = data;
        }

        /// <summary>
        /// Sends a full gemini response to the given stream
        /// </summary>
        /// <param name="destination">Output stream</param>
        public void SendTo(Stream destination)
        {
            var code = (int)StatusCode;
            if (code < 10 || code > 99)
            {
                Status = $"A backend application generated an invalid status code of {code}";
                StatusCode = StatusCode.CgiError;
                Body?.Dispose();
                Body = null;
                code = (int)StatusCode;
            }
            if (Status != null)
            {
                //Replace all control characters with spaces
                Status = Regex.Replace(Status, @"[\x00-\x1F]+", " ");
            }

            if (code >= 20 && code <= 30)
            {
                if (string.IsNullOrEmpty(Status))
                {
                    Status = DefaultStatus;
                }
            }
            var line = Encoding.UTF8.GetBytes($"{code} {Status}\r\n");
            destination.Write(line);
            Body?.CopyTo(destination);
            destination.Flush();
        }

        /// <summary>
        /// Disposes this instance as well as <see cref="Body"/>
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Body?.Dispose();
        }

        /// <summary>
        /// Creates a gemini response that sends a file
        /// </summary>
        /// <param name="filePath">File path</param>
        /// <param name="contentType">
        /// Content type. If not supplied, it is guessed based on the file extension
        /// </param>
        /// <returns>Gemini response</returns>
        public static GeminiResponse File(string filePath, string? contentType = null)
        {
            contentType ??= MimeType.GetMimeType(filePath);
            var fs = System.IO.File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var props = new Dictionary<string, string>()
            {
                { "Size", fs.Length.ToString() },
                { "Filename", Path.GetFileName(filePath) },
                { "LastModified", System.IO.File.GetLastWriteTimeUtc(filePath).ToString("s") }
            };

            return new GeminiResponse()
            {
                Body = fs,
                Status = MimeType.BuildMimeLine(contentType, props)
            };
        }

        /// <summary>
        /// Creates a "not found" gemini response
        /// </summary>
        /// <param name="status">Optional status message</param>
        /// <returns>Gemini response</returns>
        public static GeminiResponse NotFound(string? status = null)
        {
            return new GeminiResponse()
            {
                StatusCode = StatusCode.NotFound,
                Status = status ?? StatusCode.NotFound.ToString()
            };
        }

        /// <summary>
        /// Creates a "bad request" gemini response
        /// </summary>
        /// <param name="status">Optional status message</param>
        /// <returns>Gemini response</returns>
        public static GeminiResponse BadRequest(string? status = null)
        {
            return new GeminiResponse()
            {
                StatusCode = StatusCode.BadRequest,
                Status = status ?? StatusCode.BadRequest.ToString()
            };
        }

        /// <summary>
        /// Creates an empty success response
        /// </summary>
        public static GeminiResponse Ok()
        {
            return new GeminiResponse()
            {
                Status = "application/x-empty"
            };
        }

        /// <summary>
        /// Creates a success response using the given text and content type
        /// </summary>
        /// <param name="text">Text</param>
        /// <param name="contentType">Content type. If not supplied, <see cref="DefaultStatus"/> is used</param>
        /// <returns>Gemini response</returns>
        /// <remarks>Text is always UTF-8 encoded, regardless of the supplied <paramref name="contentType"/></remarks>
        public static GeminiResponse Ok(string text, string? contentType = null)
        {
            return new GeminiResponse()
            {
                Body = new MemoryStream(Encoding.UTF8.GetBytes(text), false),
                Status = contentType ?? DefaultStatus
            };
        }

        /// <summary>
        /// Creates a success response using the given bytes and content type
        /// </summary>
        /// <param name="data">Bytes</param>
        /// <param name="contentType">Content type. If not supplied, <see cref="DefaultStatus"/> is used</param>
        /// <returns>Gemini response</returns>
        public static GeminiResponse Ok(byte[] data, string? contentType = null)
        {
            return new GeminiResponse()
            {
                Body = new MemoryStream(data, false),
                Status = contentType ?? DefaultStatus
            };
        }

        /// <summary>
        /// Creates a success response with the given stream data
        /// </summary>
        /// <param name="data">Body content</param>
        /// <param name="contentType">Content type of <paramref name="data"/></param>
        /// <returns>Gemini response</returns>
        /// <remarks><paramref name="data"/> is disposed automatically after processing</remarks>
        public static GeminiResponse Ok(Stream? data, string contentType)
        {
            return new GeminiResponse()
            {
                Body = data,
                Status = contentType
            };
        }

        /// <summary>
        /// Creates a redirection result
        /// </summary>
        /// <param name="location">Redirection target</param>
        /// <param name="permanent">Use permanent redirect code</param>
        /// <returns>Gemini response</returns>
        public static GeminiResponse Redirect(string location, bool permanent = false)
        {
            return new GeminiResponse()
            {
                StatusCode = permanent ? StatusCode.PermanentRedirect : StatusCode.TemporaryRedirect,
                Status = location
            };
        }

        /// <summary>
        /// Creates a redirection result
        /// </summary>
        /// <param name="location">Redirection target</param>
        /// <param name="permanent">Use permanent redirect code</param>
        /// <returns>Gemini response</returns>
        public static GeminiResponse Redirect(Uri location, bool permanent = false)
        {
            return new GeminiResponse()
            {
                StatusCode = permanent ? StatusCode.PermanentRedirect : StatusCode.TemporaryRedirect,
                Status = location.ToString()
            };
        }

        /// <summary>
        /// Creates a result that requests client authentication
        /// </summary>
        /// <param name="status">
        /// Optional status message. If not supplied,
        /// the string "ClientCertificateRequired" will be used
        /// </param>
        /// <returns>Gemini response</returns>
        public static GeminiResponse CertificateRequired(string? status = null)
        {
            return new GeminiResponse()
            {
                StatusCode = StatusCode.ClientCertificateRequired,
                Status = status ?? StatusCode.ClientCertificateRequired.ToString()
            };
        }
    }
}
