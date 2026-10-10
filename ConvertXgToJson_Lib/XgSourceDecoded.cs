using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// A source <see cref="XgSourceReader"/> acquired and decoded: its
/// <see cref="Models.XgFile"/>, and its bytes when the read was asked to
/// retain them.
/// </summary>
/// <remarks>
/// The decoded file is complete in memory: it holds no stream, file handle
/// or buffer of the read, so it stays usable — for conversion, iteration and
/// export — after every resource the read used has been closed.
/// </remarks>
public sealed class XgSourceDecoded : XgSourceRead
{
    internal XgSourceDecoded(string sourceName, XgFile file, ReadOnlyMemory<byte>? bytes)
        : base(sourceName)
    {
        File = file;
        Bytes = bytes;
    }

    /// <summary>The decoded file.</summary>
    public XgFile File { get; }

    /// <summary>
    /// The source's bytes exactly as acquired — for verbatim output without
    /// acquiring the source again — when the read was asked to retain them
    /// (<c>retainBytes: true</c>); <see langword="null"/> otherwise. Never
    /// set for <see cref="XgSourceReader.ReadStream"/>, whose stream the
    /// caller already holds.
    /// </summary>
    public ReadOnlyMemory<byte>? Bytes { get; }

    /// <inheritdoc/>
    public override TResult Match<TResult>(Func<XgSourceDecoded, TResult> decoded, Func<XgSourceFailed, TResult> failed)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(failed);
        return decoded(this);
    }

    /// <inheritdoc/>
    public override void Switch(Action<XgSourceDecoded> decoded, Action<XgSourceFailed> failed)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(failed);
        decoded(this);
    }

    /// <summary>The source and its case, e.g. <c>"match.xg: decoded"</c>.</summary>
    public override string ToString() => $"{SourceName}: decoded";
}
