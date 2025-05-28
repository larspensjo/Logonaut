using System;

namespace Logonaut.Core;

/*
 * Represents an error indicating that a log source (e.g., a file)
 * has been reset or truncated, requiring re-initialization.
 */
public class SourceResetException : Exception
{
    public SourceResetException(string message) : base(message) { }
    public SourceResetException(string message, Exception innerException) : base(message, innerException) { }
}
