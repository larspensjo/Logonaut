using System;
using System.Collections.Generic;

namespace Logonaut.Common
{
    /// <summary>
    /// Represents a log document as a collection of lines.
    /// Supports thread-safe appending and retrieving of log lines.
    /// </summary>
    public class LogDocument
    {
        private readonly List<string> _lines = new();
        private readonly object _lock = new object();

        /// <summary>
        /// Gets the total number of lines in the document.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _lines.Count;
                }
            }
        }

        /// <summary>
        /// Appends a new line to the log document.
        /// </summary>
        /// <param name="line">The line to append.</param>
        public void AppendLine(string line)
        {
            lock (_lock)
            {
                _lines.Add(line);
            }
        }

        public void AddInitialLines(string text)
        {
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);

            lock (_lock)
            {
                _lines.AddRange(lines);
                // If the last line ends with "\r\n", there will be an empty line at the end.
                if (_lines.Count > 0 && string.IsNullOrEmpty(_lines[^1]))
                {
                    _lines.RemoveAt(_lines.Count - 1);
                }
            }
        }

        /// <summary>
        /// Retrieves a subset of lines from the document. To facilitate unit testing.
        /// </summary>
        /// <param name="start">Zero-based start index.</param>
        /// <param name="count">Number of lines to retrieve.</param>
        /// <returns>A read-only list of log lines.</returns>
        public IReadOnlyList<string> GetLines(int start, int count)
        {
            lock (_lock)
            {
                if (start < 0)
                    start = 0;
                if (start >= _lines.Count)
                    return Array.Empty<string>();
                if (start + count > _lines.Count)
                    count = _lines.Count - start;
                return _lines.GetRange(start, count).AsReadOnly();
            }
        }

        /// <summary>
        /// Returns a read-only list of all lines in the document.
        /// </summary>
        public IReadOnlyList<string> ToList()
        {
            lock (_lock)
            {
                return _lines.AsReadOnly();
            }
        }

        /// <summary>
        /// Provides indexed access to log lines.
        /// </summary>
        public string this[int index]
        {
            get
            {
                lock (_lock)
                {
                    return _lines[index];
                }
            }
        }

        /// <summary>
        /// Clears the log document.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _lines.Clear();
            }
        }
    }
}
