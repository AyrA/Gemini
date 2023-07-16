using Gemini.Web.Models;

namespace Gemini.Web.Enums
{
    /// <summary>
    /// Specifies the type of
    /// <see cref="GeminiResponseModel.Content"/>
    /// </summary>
    public enum ContentType
    {
        /// <summary>
        /// Content type is unknown
        /// </summary>
        /// <remarks>Unknown means that the content is null, or not of any other defined type</remarks>
        Unknown = 0,
        /// <summary>
        /// Content is a string
        /// </summary>
        String = 1,
        /// <summary>
        /// Content represents raw binary data
        /// </summary>
        Bytes = 2
    }
}
