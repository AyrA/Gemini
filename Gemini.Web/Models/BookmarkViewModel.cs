namespace Gemini.Web.Models
{
    public class BookmarkViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Uri Url { get; set; }

        public BookmarkViewModel(Guid id, BookmarkModel model)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("Cannot use Nullguid");
            }
            ArgumentNullException.ThrowIfNull(model);

            Id = id;
            Name = model.Name;
            Url = model.Url;
        }
    }
}
