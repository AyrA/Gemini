using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Lib
{
    public class GeminiResponse : IDisposable
    {
        /// <summary>
        /// Default status line for success messages
        /// </summary>
        public const string DefaultStatus = "text/gemini; charset=utf-8";

        public StatusCode StatusCode { get; set; } = StatusCode.Success;

        public Stream? Body { get; set; }

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

        public void SetText(string text)
        {
            if (Body != null)
            {
                throw new InvalidOperationException("Body has already been set");
            }
            Body = new MemoryStream(Encoding.UTF8.GetBytes(text), false);
        }

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

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Body?.Dispose();
        }

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

        public static GeminiResponse NotFound(string? status = null)
        {
            return new GeminiResponse()
            {
                StatusCode = StatusCode.NotFound,
                Status = status ?? StatusCode.NotFound.ToString()
            };
        }

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

        public static GeminiResponse Ok(string text, string? contentType = null)
        {
            return new GeminiResponse()
            {
                Body = new MemoryStream(Encoding.UTF8.GetBytes(text), false),
                Status = contentType ?? DefaultStatus
            };
        }

        public static GeminiResponse Ok(byte[] data, string contentType)
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

        public static GeminiResponse Redirect(string location, bool permanent = false)
        {
            return new GeminiResponse()
            {
                StatusCode = permanent ? StatusCode.PermanentRedirect : StatusCode.TemporaryRedirect,
                Status = location
            };
        }

        public static GeminiResponse Redirect(Uri location, bool permanent = false)
        {
            return new GeminiResponse()
            {
                StatusCode = permanent ? StatusCode.PermanentRedirect : StatusCode.TemporaryRedirect,
                Status = location.ToString()
            };
        }

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
