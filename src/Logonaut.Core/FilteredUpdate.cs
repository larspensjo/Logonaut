using Logonaut.Common;

namespace Logonaut.Core;

/// <summary>
/// Base record for updates to the filtered log lines.
/// </summary>
public abstract record FilteredUpdateBase(IReadOnlyList<FilteredLogLine> Lines);

/// <summary>
/// Represents an update that should completely replace the current filtered view.
/// Typically used after filter settings change or initial load.
/// </summary>
public record ReplaceFilteredUpdate(IReadOnlyList<FilteredLogLine> Lines, bool IsInitialLoadProcessingComplete) : FilteredUpdateBase(Lines);

/// <summary>
/// Represents an update containing only newly filtered lines (including context)
/// that should be appended to the current filtered view.
/// </summary>
public record AppendFilteredUpdate(IReadOnlyList<FilteredLogLine> Lines) : FilteredUpdateBase(Lines);
