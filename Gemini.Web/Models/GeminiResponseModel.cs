using Gemini.Web.Enums;
using System.Text.RegularExpressions;

namespace Gemini.Web.Models
{
    public class GeminiResponseModel
    {
        public struct InternalErrors
        {
            public const int GenericError = -1;
            public const int UnknownCertificate = -2;
        }

        /// <summary>
        /// Gets the type of <see cref="Content"/>
        /// </summary>
        public ContentType ContentType
        {
            get
            {
                if (Content == null)
                {
                    return ContentType.Unknown;
                }
                if (Content.GetType() == typeof(string))
                {
                    return ContentType.String;
                }
                if (Content.GetType() == typeof(byte[]))
                {
                    return ContentType.Bytes;
                }
                return ContentType.Unknown;
            }
        }

        /// <summary>
        /// Gets or sets the content of the response
        /// </summary>
        public object? Content { get; set; }

        /// <summary>
        /// Gets the status code
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Gets the meta line
        /// </summary>
        public string Meta { get; }

        /// <summary>
        /// Gets the mime type information
        /// </summary>
        /// <remarks>This is only set on success codes. Check <see cref="IsSuccess"/></remarks>
        public MimeInformation? MimeInformation { get; }

        /// <summary>
        /// Gets if the code is a known gemini code
        /// </summary>
        public bool IsKnownCode => IsInput || IsSuccess || IsRedirect || IsError;

        /// <summary>
        /// Gets if the code demands user input
        /// </summary>
        /// <remarks>
        /// If set, the client should prompt the user for data,
        /// then repeat the request with data
        /// </remarks>
        public bool IsInput => StatusCode >= 10 && StatusCode < 20;

        /// <summary>
        /// Gets if the code indicates success
        /// </summary>
        /// <remarks>
        /// <see cref="Meta"/> contains a mime type,
        /// <see cref="MimeInformation"/> contains parsed mime type information
        /// </remarks>
        public bool IsSuccess => StatusCode >= 20 && StatusCode < 30;

        /// <summary>
        /// Gets if the code indicates a redirect
        /// </summary>
        /// <remarks><see cref="Meta"/> should contain an url</remarks>
        public bool IsRedirect => StatusCode >= 30 && StatusCode < 40;

        /// <summary>
        /// Gets if the error code indicates a temporary error
        /// </summary>
        /// <remarks>Temporary means that repeating the same request in the future may succeed</remarks>
        public bool IsTemporaryError => StatusCode >= 40 && StatusCode < 50;

        /// <summary>
        /// Gets if the error code indicates a permanent error
        /// </summary>
        /// <remarks>
        /// Permanent errors likely reappear when repeating the same request at a later time
        /// </remarks>
        public bool IsPermanentError => StatusCode >= 50 && StatusCode < 60;

        /// <summary>
        /// Gets if the status code indicates any type of gemini error
        /// </summary>
        public bool IsError => IsTemporaryError || IsPermanentError;

        public GeminiResponseModel(ILogger logger, string statusLine)
        {
            var parts = Regex.Match(statusLine, @"^(\d{2})(?:\s+(.*))?$");
            if (!parts.Success)
            {
                logger.LogWarning("[Protocol violation] Invalid status line: {line}", statusLine);
                throw new FormatException($"Invalid status line: '{statusLine}'");
            }
            StatusCode = int.Parse(parts.Groups[1].Value);
            Meta = parts.Groups[2].Value;
            if (Meta.Length > 1024)
            {
                logger.LogWarning("[Protocol violation] Meta data too long");
                throw new FormatException("[Protocol violation] Meta data too long");
            }
            //Only in success conditions does this contain mime type information
            if (IsSuccess)
            {
                MimeInformation = new MimeInformation(logger, Meta);
            }
        }

        public GeminiResponseModel(int statusCode, string meta)
        {
            StatusCode = statusCode;
            Meta = meta;
        }
    }
}
