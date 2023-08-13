using Gemini.Lib.Data;
using System.Collections;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Lib
{
    /// <summary>
    /// Object that holds the entire request state
    /// </summary>
    public class RequestState : IDisposable
    {
        private bool loaded = false;
        private readonly string tempFilePath;
        /// <summary>
        /// Unique request id
        /// </summary>
        public Guid Id { get; }
        /// <summary>
        /// Request url
        /// </summary>
        public Uri Url { get; }
        /// <summary>
        /// Request data stream
        /// </summary>
        public Stream? DataStream { get; }
        /// <summary>
        /// Client endpoint
        /// </summary>
        public IPEndPoint ClientAddress { get; }
        /// <summary>
        /// Client certificate
        /// </summary>
        public X509Certificate? ClientCertificate { get; }
        /// <summary>
        /// Form data
        /// </summary>
        public FormData Form { get; }
        /// <summary>
        /// Files decoded from the form
        /// </summary>
        public FileDataCollection Files { get; }

        /// <summary>
        /// Creates a new instance
        /// </summary>
        /// <param name="url">Request url</param>
        /// <param name="clientAddress">Client endpoint</param>
        /// <param name="clientCertificate">Client certificate</param>
        /// <param name="dataStream">Connection data stream</param>
        public RequestState(Uri url, IPEndPoint clientAddress, X509Certificate? clientCertificate, Stream? dataStream)
        {
            Id = Guid.NewGuid();
            Url = url;
            ClientAddress = clientAddress;
            ClientCertificate = clientCertificate;
            DataStream = dataStream;
            if (!string.IsNullOrWhiteSpace(url.Query) && url.Query.Length > 1)
            {
                Form = new FormData(url.Query, dataStream);
            }
            else
            {
                Form = new FormData(null, dataStream);
            }
            tempFilePath = Path.Combine(Path.GetTempPath(), Id.ToString());
            Files = new FileDataCollection();
        }

        /// <summary>
        /// Loads files from the form collection
        /// </summary>
        public async Task LoadFiles()
        {
            if (DataStream == null)
            {
                throw new InvalidOperationException("Stream has not been set");
            }
            if (loaded)
            {
                throw new InvalidOperationException("Data already loaded");
            }
            loaded = true;
            var files = Form.Files.Select(m => Form.GetAsFile(m)).OrderBy(m => m.Index).ToArray();

            var indexList = files.Select(m => (int)m.Index).ToArray();
            var expectedList = Enumerable.Range(1, files.Length).ToArray();

            if (!indexList.SequenceEqual(expectedList))
            {
                var strIndexList = string.Join(", ", indexList);
                var strExpectedList = string.Join(", ", expectedList);
                throw new Exception($"File list not in order. Expected indexes {strExpectedList} but got {strIndexList}");
            }

            foreach (var f in files)
            {
                await f.GetFileData(DataStream, tempFilePath);
            }
            Files.AddRange(files);
        }

        /// <summary>
        /// Disposes this instance and all associated data
        /// </summary>
        public void Dispose()
        {
            ClientCertificate?.Dispose();
            try
            {
                Directory.Delete(tempFilePath);
            }
            catch
            {
                //NOOP
            }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents a collection of <see cref="FileData"/> instances
    /// </summary>
    public class FileDataCollection : IEnumerable<FileData>, IDisposable
    {
        private readonly List<FileData> data;

        /// <summary>
        /// Gets the number of items contained in this instance
        /// </summary>
        public int Count => data.Count;

        /// <summary>
        /// Creates an empty file data collection
        /// </summary>
        public FileDataCollection()
        {
            data = new();
        }

        /// <summary>
        /// Creates a file data collection from existing file data
        /// </summary>
        /// <param name="files">file data</param>
        public FileDataCollection(IEnumerable<FileData> files)
        {
            data = files
                .Where(m => m != null)
                .OrderBy(m => m.Index)
                .ToList();
        }

        /// <summary>
        /// Adds files to the collection
        /// </summary>
        /// <param name="files">Files</param>
        internal void AddRange(IEnumerable<FileData> files)
        {
            data.AddRange(files.OrderBy(m => m.Index));
        }

        /// <summary>
        /// Disposes this instance as well as all filedata instances
        /// </summary>
        public void Dispose()
        {
            foreach (var entry in data)
            {
                entry?.Dispose();
            }
            data.Clear();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Returns an enumerator for all file data instances contained in this collection
        /// </summary>
        public IEnumerator<FileData> GetEnumerator()
        {
            return data.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return data.GetEnumerator();
        }
    }
}
