using AyrA.AutoDI;
using Gemini.Web.Exceptions;
using Gemini.Web.Extensions;
using Gemini.Web.Models;
using System.Collections;

namespace Gemini.Web.Services
{
    [AutoDIRegister(AutoDIType.Transient)]
    public class BookmarkService : IEnumerable<BookmarkModel>
    {
        private readonly Dictionary<Guid, BookmarkModel> _bookmarks = [];
        private readonly ILogger<BookmarkService> _logger;
        private readonly string _bookmarkFile;

        public Guid[] Ids => [.. _bookmarks.Keys];

        public BookmarkModel[] Bookmarks => [.. _bookmarks.Values];

        public BookmarkModel this[Guid id] => _bookmarks[id];

        public BookmarkService(ILogger<BookmarkService> logger)
        {
            _logger = logger;
            _bookmarkFile = Path.Combine(AppContext.BaseDirectory, "bookmarks.json");
            Reload();
        }

        public bool Delete(Guid id)
        {
            return _bookmarks.Remove(id);
        }

        public Guid Add(BookmarkModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            model.Validate();
            var id = Guid.NewGuid();
            var copy = model.Clone();
            _bookmarks.Add(id, copy);
            try
            {
                Validate();
            }
            catch
            {
                _bookmarks.Remove(id);
                throw;
            }
            return id;
        }

        public void Update(Guid id, BookmarkModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            model.Validate();

            lock (_bookmarks)
            {
                var old = _bookmarks[id];
                try
                {
                    _bookmarks[id] = model;
                    Validate();
                }
                catch
                {
                    _bookmarks[id] = old;
                    throw;
                }
            }
        }

        public void Reload()
        {
            string data;
            try
            {
                data = File.ReadAllText(_bookmarkFile);
            }
            catch (FileNotFoundException)
            {
                _logger.LogInformation("No bookmarks file found");
                _bookmarks.Clear();
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read {file}", _bookmarkFile);
                throw;
            }
            _logger.LogInformation("Reloading bookmarks from file");
            var items = data.FromJson<IDictionary<Guid, BookmarkModel>>();
            if (items == null)
            {
                _logger.LogError("Failed to deserialize bookmarks");
                return;
            }

            //Store current entries
            lock (_bookmarks)
            {
                var currentEntries = _bookmarks.ToArray();
                try
                {
                    _bookmarks.Clear();
                    foreach (var item in items)
                    {
                        _bookmarks.Add(item.Key, item.Value);
                    }
                    Validate();

                }
                catch
                {
                    //Restore entries previously stored
                    _bookmarks.Clear();
                    foreach (var e in currentEntries)
                    {
                        _bookmarks.Add(e.Key, e.Value);
                    }

                    throw;
                }
            }
        }

        public void Save()
        {
            Validate();
            _logger.LogInformation("Saving bookmarks to file");
            File.WriteAllText(_bookmarkFile, _bookmarks.ToJson());
        }

        private void Validate()
        {
            foreach (var bm in _bookmarks)
            {
                if (bm.Value == null)
                {
                    _logger.LogInformation("Validation failed. Bookmark list contains null value");
                    throw new ValidationException("Bookmark list contains null entries");
                }
                bm.Value.Validate();
            }
            var ids = _bookmarks.Keys.ToArray();
            if (ids.Contains(Guid.Empty))
            {
                _logger.LogInformation("Validation failed. Bookmark list contains null guid");
                throw new ValidationException("Empty bookmark id");
            }
        }

        public IEnumerator<BookmarkModel> GetEnumerator()
        {
            return ((IEnumerable<BookmarkModel>)_bookmarks.Values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _bookmarks.Values.GetEnumerator();
        }
    }
}
