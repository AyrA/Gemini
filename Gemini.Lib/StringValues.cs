using System.Collections;

namespace Gemini.Lib
{
    /// <summary>
    /// Represents a collection of string values that can also behave like a single string
    /// </summary>
    public class StringValues : IEnumerable<string>
    {
        private readonly List<string> strings = new();

        /// <summary>
        /// Gets the number of strings stored in this instance
        /// </summary>
        public int Count => strings.Count;

        /// <summary>
        /// Gets the string at the given index
        /// </summary>
        /// <param name="index">Index</param>
        public string this[int index]
        {
            get
            {
                return strings[index];
            }
        }

        /// <summary>
        /// Adds a string to the collection
        /// </summary>
        /// <param name="value">value</param>
        /// <returns>New item count</returns>
        public int Add(string value)
        {
            strings.Add(value);
            return strings.Count;
        }

        /// <summary>
        /// Converts the strings from this instance into a single string
        /// </summary>
        /// <returns>Single string</returns>
        /// <remarks>The joiner will be a comma followed by a space</remarks>
        public override string ToString()
        {
            return string.Join(", ", strings);
        }

        /// <summary>
        /// Gets an enumerator to iterate over the strings in this collection
        /// </summary>
        public IEnumerator<string> GetEnumerator()
        {
            return strings.GetEnumerator();
        }

        /// <summary>
        /// Gets an enumerator to iterate over the strings in this collection
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Implicitly casts this instance to a string
        /// </summary>
        /// <param name="sv">instance</param>
        public static implicit operator string(StringValues sv) => sv.ToString();
    }
}