using System.Text.RegularExpressions;

namespace Gemini.Lib.Data
{
    /// <summary>
    /// Represents an uploaded file
    /// </summary>
    public class FileData : IDisposable
    {
        private const int SizeLimit = 10000;

        private string? tempFileName = null;
        private byte[]? data = null;

        /// <summary>
        /// Gets the file name that should be valid on this system and has all path information removed
        /// </summary>
        public string SanitizedName { get; }
        /// <summary>
        /// Gets the file name as supplied by the client
        /// </summary>
        public string FileName { get; }
        /// <summary>
        /// Gets the offset in the data stream the file data starts
        /// </summary>
        public uint Index { get; }
        /// <summary>
        /// Gets the size of the file
        /// </summary>
        public ulong Size { get; }

        /// <summary>
        /// Creates a file data instance
        /// </summary>
        /// <param name="fileName">file name</param>
        /// <param name="index">Data stream offset</param>
        /// <param name="size">File size</param>
        public FileData(string fileName, uint index, ulong size)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException($"'{nameof(fileName)}' cannot be null or whitespace.", nameof(fileName));
            }

            if (fileName.Trim() != fileName)
            {
                throw new ArgumentException($"'{nameof(fileName)}' cannot begin or end in whitespace.", nameof(fileName));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            SanitizedName = fileName
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Last()
                .Trim();
            //Remove trailing whitespace and dots
            SanitizedName = Regex.Replace(SanitizedName, @"[.\s]*$", "");
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                SanitizedName = SanitizedName.Replace(c, '_');
            }
            FileName = fileName;
            Index = index;
            Size = size;
        }

        /// <summary>
        /// Reads the bytes for the given file
        /// </summary>
        public async Task<byte[]?> GetBytes()
        {
            if (data != null)
            {
                return (byte[])data.Clone();
            }
            else if (!string.IsNullOrEmpty(tempFileName))
            {
                return await File.ReadAllBytesAsync(tempFileName);
            }
            return null;
        }

        /// <summary>
        /// Gets the uploaded data as a stream
        /// </summary>
        public Stream? GetStream()
        {
            if (data != null)
            {
                return new MemoryStream(data, false);
            }
            else if (!string.IsNullOrEmpty(tempFileName))
            {
                return File.OpenRead(tempFileName);
            }
            return null;
        }

        /// <summary>
        /// Gets file data from the stream.
        /// </summary>
        /// <param name="s">Gemini request stream</param>
        /// <param name="dataDirectory">Directory where decoded files from the request stream are cached</param>
        /// <remarks>This assumes that the stream is positioned at the start of the file</remarks>
        public async Task GetFileData(Stream s, string dataDirectory)
        {
            if (Size < SizeLimit)
            {
                data = new byte[Size];
                int offset = 0;
                while (offset < data.Length)
                {
                    var read = await s.ReadAsync(data.AsMemory(offset, data.Length - offset));
                    offset += read;
                    if (read == 0)
                    {
                        throw new IOException("Unexpected end of stream");
                    }
                }
            }
            else
            {
                tempFileName = GetTempName(dataDirectory);
                using var fs = File.Create(tempFileName);
                var data = new byte[SizeLimit];
                ulong total = 0;
                while (total < Size)
                {
                    var read = await s.ReadAsync(data);
                    total += (ulong)read;
                    if (read == 0)
                    {
                        throw new IOException("Unexpected end of stream");
                    }
                    await fs.WriteAsync(data.AsMemory(0, read));
                }
            }
        }

        private static string GetTempName(string dataDir)
        {
            var name = Path.Combine(dataDir, Guid.NewGuid().ToString());
            while (File.Exists(name))
            {
                name = Path.Combine(dataDir, Guid.NewGuid().ToString());
            }
            return name;
        }

        /// <summary>
        /// Disposes this instance and deletes the temporary file, should it exist
        /// </summary>
        public void Dispose()
        {
            if (tempFileName != null)
            {
                try
                {
                    File.Delete(tempFileName);
                    tempFileName = null;
                }
                catch
                {
                    //NOOP
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}