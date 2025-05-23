using System;

namespace Logonaut.Common
{
    /// <summary>
    /// Represents a log line after filtering, retaining its original line number.
    /// </summary>
    public class FilteredLogLine
    {
        /// <summary>
        /// Gets the 1-based line number from the original, unfiltered log document.
        /// </summary>
        public int OriginalLineNumber { get; }

        /// <summary>
        /// Gets the text content of the log line.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets whether this line was included as a context line rather than a direct match.
        /// </summary>
        public bool IsContextLine { get; }

        public FilteredLogLine(int originalLineNumber, string text, bool isContextLine = false)
        {
            if (originalLineNumber < 1)
            {
                // Defensive check, though FilterEngine should provide 1-based
                originalLineNumber = 1; // Default to 1 if invalid
            }
            OriginalLineNumber = originalLineNumber;
            Text = text ?? string.Empty; // Ensure text is not null
            IsContextLine = isContextLine;
        }

        // Optional: Override Equals and GetHashCode if needed for specific collection logic
        public override bool Equals(object? obj)
        {
            return obj is FilteredLogLine line &&
                   OriginalLineNumber == line.OriginalLineNumber &&
                   Text == line.Text &&
                   IsContextLine == line.IsContextLine;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(OriginalLineNumber, Text, IsContextLine);
        }
    }
}
