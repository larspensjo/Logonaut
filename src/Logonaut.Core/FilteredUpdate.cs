using Logonaut.Common;
using System.Collections.Generic;

namespace Logonaut.Core
{
    /// <summary>
    /// Represents an update to the collection of filtered log lines.
    /// </summary>
    /// <param name="Type">Indicates whether to Replace or Append.</param>
    /// <param name="Lines">The list of filtered log lines for this update.</param>
    public record FilteredUpdate(IReadOnlyList<FilteredLogLine> Lines);
}