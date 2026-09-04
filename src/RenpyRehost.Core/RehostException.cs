namespace RenpyRehost.Core;

/// <summary>
/// A conversion failure with a message meant for the user. The pipeline runner
/// catches this and reports it against the stage that threw; anything else
/// propagates as an unexpected error.
/// </summary>
public sealed class RehostException : Exception
{
    public RehostException(string message) : base(message) { }
    public RehostException(string message, Exception inner) : base(message, inner) { }
}
