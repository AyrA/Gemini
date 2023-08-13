namespace Gemini.Lib
{
    /// <summary>
    /// Extensions for <see cref="Uri"/>
    /// </summary>
    public static class UrlExtensions
    {
        /// <summary>
        /// Registers the gemini URL scheme
        /// </summary>
        /// <remarks>
        /// The gemini server already calls this.
        /// Only call this yourself if you use this library in a custom server
        /// </remarks>
        public static void RegisterGeminiScheme()
        {
            UriParser.Register(new HttpStyleUriParser(), "gemini", 1965);
            UriParser.Register(new HttpStyleUriParser(), "gemini+", 1965);
        }

        /// <summary>
        /// Checks if the URL is either a classic gemini url or a gemini+ url
        /// </summary>
        /// <param name="instance">URL</param>
        /// <returns>true, if gemini(+) url</returns>
        /// <remarks>null argument yields false</remarks>
        public static bool IsAnyGeminiUrl(this Uri? instance)
        {
            return IsGeminiUrl(instance) || IsGeminiPlusUrl(instance);
        }

        /// <summary>
        /// Checks if the URL is a classic gemini url
        /// </summary>
        /// <param name="instance">URL instance</param>
        /// <returns>true, if classic gemini URL</returns>
        public static bool IsGeminiUrl(this Uri? instance)
        {
            return instance?.Scheme?.ToLower() == "gemini";
        }

        /// <summary>
        /// Checks if the URL is a gemini+ url
        /// </summary>
        /// <param name="instance">URL instance</param>
        /// <returns>true, if gemini+ URL</returns>
        public static bool IsGeminiPlusUrl(this Uri? instance)
        {
            return instance?.Scheme?.ToLower() == "gemini+";
        }
    }
}
