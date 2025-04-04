using Logonaut.Common;
using System.Collections.Generic;

namespace Logonaut.Core
{
    /// <summary>
    /// Represents the type of update for the filtered log view.
    /// </summary>
    public enum UpdateType
    {
        /// <summary>
        /// Replace the entire content with the new lines.
        /// </summary>
        Replace,
        /// <summary>
        /// Append the new lines to the existing content.
        /// </summary>
        Append
    }

    /// <summary>
    /// Represents an update to the collection of filtered log lines.
    /// </summary>
    /// <param name="Type">Indicates whether to Replace or Append.</param>
    /// <param name="Lines">The list of filtered log lines for this update.</param>
    public record FilteredUpdate(UpdateType Type, IReadOnlyList<FilteredLogLine> Lines);
}