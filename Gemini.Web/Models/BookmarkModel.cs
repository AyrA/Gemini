using Gemini.Web.Exceptions;
using Gemini.Web.Interfaces;
using System.Text.Json.Serialization;

namespace Gemini.Web.Models
{
    public class BookmarkModel : ICloneable<BookmarkModel>
    {
        public string Name { get; private set; }

        public Uri Url { get; private set; }

        public BookmarkModel()
        {
            Name = string.Empty;
            Url = new Uri("about:blank");
        }

        [JsonConstructor]
        public BookmarkModel(string name, Uri url)
        {
            Name = name;
            Url = url;
            Validate();
        }

        public void Validate()
        {
            if (Url == null)
            {
                throw new ValidationException($"{nameof(Url)} is null");
            }
            if (Url.Scheme != "gemini")
            {
                throw new ValidationException("Not a gemini URL");
            }
            if (!Url.IsAbsoluteUri)
            {
                throw new ValidationException("Not a full URL");
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new ValidationException($"{nameof(Name)} is null or whitespace");
            }
        }

        public BookmarkModel Clone()
        {
            return new BookmarkModel(Name, Url);
        }
    }
}
