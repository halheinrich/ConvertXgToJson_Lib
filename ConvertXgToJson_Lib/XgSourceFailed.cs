namespace ConvertXgToJson_Lib;

/// <summary>
/// A source <see cref="XgSourceReader"/> could not read: the exception that
/// failed it, raised while acquiring the source's bytes or while decoding
/// them — the read's one failure policy covers both stages, so the case does
/// not say which.
/// </summary>
/// <remarks>
/// <see cref="Exception"/> is the failure's own exception, carried as the
/// value it is: its type says what kind of failure it was (an
/// <see cref="IOException"/> for a source that could not be opened or read,
/// the decoder's <see cref="InvalidDataException"/> for bytes it refuses, a
/// <see cref="System.Text.Json.JsonException"/> for JSON that does not
/// parse, or whatever else the acquisition or the decoder raised), and its
/// message names what failed. A caller can show it, log it or branch on it
/// without parsing text.
/// </remarks>
public sealed class XgSourceFailed : XgSourceRead
{
    internal XgSourceFailed(string sourceName, Exception exception)
        : base(sourceName) => Exception = exception;

    /// <summary>The exception that failed the source; never null, never an <see cref="OperationCanceledException"/>.</summary>
    public Exception Exception { get; }

    /// <inheritdoc/>
    public override TResult Match<TResult>(Func<XgSourceDecoded, TResult> decoded, Func<XgSourceFailed, TResult> failed)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(failed);
        return failed(this);
    }

    /// <inheritdoc/>
    public override void Switch(Action<XgSourceDecoded> decoded, Action<XgSourceFailed> failed)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(failed);
        failed(this);
    }

    /// <summary>
    /// The source and the failure, e.g.
    /// <c>"match.xg: failed: InvalidDataException: …"</c>.
    /// </summary>
    public override string ToString() => $"{SourceName}: failed: {Exception.GetType().Name}: {Exception.Message}";
}
