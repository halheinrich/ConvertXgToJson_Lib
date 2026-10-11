using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The one read of one source: acquires its bytes, decodes them into an
/// <see cref="XgFile"/>, and returns the outcome — <see cref="XgSourceDecoded"/>
/// or <see cref="XgSourceFailed"/> — instead of throwing for a source that
/// cannot be read. A caller walking many sources reads each through it and
/// decides for itself what a failed source means: skip it, report it, stop.
/// The read decodes and nothing more; converting the file (the iterator, its
/// opening book and ranking) and filtering stay with the caller.
/// </summary>
/// <remarks>
/// <para>
/// <b>Entry points, one per kind of source.</b> A file path, read
/// synchronously (<see cref="ReadFile"/>) or asynchronously
/// (<see cref="ReadFileAsync"/>); an open stream the caller owns
/// (<see cref="ReadStream"/>); and an asynchronous acquisition the caller
/// supplies — a browser upload's stream, say — which the read awaits with
/// the caller's token and buffers (<see cref="ReadAsync"/>). Each takes the
/// <see cref="XgSourceFormat"/> to decode as, a required argument with no
/// default (Hal's ruling of 2026-10-11): the caller states which decoder it
/// means, and nothing is inferred from a path or a name. The enum's zero
/// value, <see cref="XgSourceFormat.Unspecified"/>, is refused like a value
/// that is no member at all (Hal's ruling of 2026-10-11), so an unset format
/// never chooses a decoder; that refuses a format nobody stated, and cannot
/// show that a stated one fits the bytes. XG's binary file decodes through
/// <see cref="XgFileReader.ReadStream"/>, this library's JSON document
/// through the decoder behind <see cref="XgFileReader.ReadJson"/>. There is
/// no second decoder:
/// <see cref="XgFileReader"/>'s entry points are the decode primitives this
/// read calls, and they keep throwing.
/// </para>
/// <para>
/// <b>One failure policy.</b> Every exception raised while acquiring the
/// source's bytes or decoding them fails that source, whatever its type,
/// and is returned as <see cref="XgSourceFailed"/>, never thrown — save a
/// cancellation (below). A malformed file can raise an
/// <see cref="ArgumentException"/> while it is decoded (a header whose
/// payload offset no stream can hold raises an
/// <see cref="ArgumentOutOfRangeException"/>), and that is the source's
/// failure, not the caller's misuse: an exception's type alone never
/// classifies it.
/// </para>
/// <para>
/// <b>Usage errors are thrown, never an outcome.</b> Each entry point
/// validates its own arguments before it acquires anything, and throws for
/// a mistake in how it was called: a null or blank path or name, a null
/// stream or acquisition, an unset (<see cref="XgSourceFormat.Unspecified"/>)
/// or undefined <see cref="XgSourceFormat"/>, and for
/// <see cref="ReadStream"/> a stream it cannot decode (below). The
/// asynchronous entry points throw these synchronously, from the call,
/// before any task exists. Everything after validation — opening the file,
/// running the acquisition, reading the stream, decoding — is inside the
/// failure policy.
/// </para>
/// <para>
/// <b>Cancellation stays a cancellation.</b> An
/// <see cref="OperationCanceledException"/> raised while acquiring or
/// decoding propagates to the caller, whichever token it carries; it is
/// never a failed source. The entry points that take a token observe it
/// before the acquisition starts, after the awaited acquisition (whether it
/// produced bytes or failed) and before returning an outcome, so an
/// acquisition that ignores the token cannot turn a cancelled read into a
/// decoded or a failed source. Decoding is synchronous and is not
/// interrupted. An acquisition that raises a cancellation of its own — a
/// timeout, for example — therefore ends the caller's operation rather than
/// failing only that source; an acquisition that means "this source timed
/// out" raises a different exception.
/// </para>
/// <para>
/// <b>Resources.</b> <see cref="ReadFile"/> and <see cref="ReadFileAsync"/>
/// open the file themselves and close it before they return, whatever the
/// outcome, and when the read is cancelled. <see cref="ReadStream"/>
/// borrows its stream: it never closes or disposes it, on any outcome or on
/// cancellation; it reads from position 0 (XG's header states its payload's
/// offset from the file's first byte, and the binary decoder seeks there),
/// so the stream must be readable, seekable and at position 0, and its
/// position afterwards is wherever decoding left it. <see cref="ReadAsync"/>
/// owns the stream its acquisition returns: it copies the stream into a
/// buffer the decoder can seek, and disposes the stream once the copy ends
/// — when it completes, when it fails, and when it is cancelled — before
/// decoding; a forward-only stream, such as a browser upload's, is
/// therefore fine there.
/// </para>
/// <para>
/// <b>One precedence between the read's exception and the cleanup's.</b>
/// Every resource the read disposes is disposed, whatever the read did.
/// When the read and the disposal both raise, a cancellation the read raised
/// propagates, the disposal's exception dropped; an ordinary read failure
/// fails the source with the read's own exception, the disposal's ordinary
/// failure dropped, unless the disposal raised a cancellation, which
/// propagates. After a clean read, a cancellation the disposal raises
/// propagates, and an ordinary failure disposing fails the source with the
/// disposal's exception. So a cancellation always stays a cancellation, and
/// a failed source carries the exception that failed it. Where the read
/// could break the rule, it enforces it: the stream <see cref="ReadAsync"/>'s
/// acquisition returns is the caller's code, whose read may fail or cancel
/// and whose disposal may too. Its other resources cannot: the
/// <see cref="MemoryStream"/>s it makes over its own buffers do not fail to
/// dispose, the file <see cref="ReadFile"/> opens is read by a synchronous
/// decode that observes no token, and <see cref="ReadFileAsync"/>'s file is
/// opened and closed within <see cref="File.ReadAllBytesAsync(string, CancellationToken)"/>.
/// </para>
/// <para>
/// <b>What a decoded source keeps.</b> Its <see cref="XgFile"/>, complete in
/// memory and independent of every resource the read used. Its bytes, exactly
/// as acquired, only when the caller asks (<c>retainBytes: true</c>, on the
/// entry points that acquire) — for verbatim output without acquiring the
/// source again; a caller that does not ask keeps no copy.
/// </para>
/// <para>
/// <b>Attribution.</b> Every outcome carries <see cref="XgSourceRead.SourceName"/>:
/// the path as passed to the file entry points, the name as passed to the
/// others. The read does not interpret it — a caller that will hand the
/// file to <see cref="XgDecisionIterator"/> passes the name the iterator
/// needs (a file name with its extension) or derives it from the path.
/// </para>
/// </remarks>
public static class XgSourceReader
{
    // ------------------------------------------------------------------ //
    //  File paths
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Reads the file at <paramref name="path"/>: opens it, decodes it as
    /// <paramref name="format"/>, closes it, and returns the outcome.
    /// </summary>
    /// <param name="path">The file's path; it becomes the outcome's <see cref="XgSourceRead.SourceName"/> as passed.</param>
    /// <param name="format">How the file's bytes are decoded: <see cref="XgSourceFormat.Xg"/> or <see cref="XgSourceFormat.Json"/>, stated by the caller.</param>
    /// <param name="retainBytes">
    /// Whether a decoded source keeps the file's bytes
    /// (<see cref="XgSourceDecoded.Bytes"/>). When true the file is read
    /// whole and decoded from memory.
    /// </param>
    /// <returns>
    /// <see cref="XgSourceDecoded"/>, or <see cref="XgSourceFailed"/> with the
    /// exception that opening, reading or decoding the file raised.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="XgSourceFormat.Unspecified"/>, the unset value, or no member of <see cref="XgSourceFormat"/>.</exception>
    /// <exception cref="OperationCanceledException">Raised while reading or decoding the file; propagated, never an outcome.</exception>
    public static XgSourceRead ReadFile(
        string path, XgSourceFormat format, bool retainBytes = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var decode = RequireDecoder(format);

        try
        {
            if (retainBytes)
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var buffered = new MemoryStream(bytes, writable: false);
                return new XgSourceDecoded(path, decode(buffered, path), bytes);
            }

            using var stream = File.OpenRead(path);
            return new XgSourceDecoded(path, decode(stream, path), bytes: null);
        }
        catch (Exception exception) when (FailsTheSource(exception))
        {
            return new XgSourceFailed(path, exception);
        }
    }

    /// <summary>
    /// Reads the file at <paramref name="path"/> asynchronously: reads its
    /// bytes with <paramref name="cancellationToken"/>, decodes them as
    /// <paramref name="format"/>, and returns the outcome. The file is closed
    /// before the task completes, whatever the outcome.
    /// </summary>
    /// <param name="path">The file's path; it becomes the outcome's <see cref="XgSourceRead.SourceName"/> as passed.</param>
    /// <param name="format">How the file's bytes are decoded: <see cref="XgSourceFormat.Xg"/> or <see cref="XgSourceFormat.Json"/>, stated by the caller.</param>
    /// <param name="retainBytes">Whether a decoded source keeps the file's bytes (<see cref="XgSourceDecoded.Bytes"/>).</param>
    /// <param name="cancellationToken">Observed before the file is read, by the read itself, after it, and before an outcome is returned.</param>
    /// <returns>
    /// A task for <see cref="XgSourceDecoded"/>, or for
    /// <see cref="XgSourceFailed"/> with the exception that reading or
    /// decoding the file raised; a cancelled task when the read is cancelled.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null; thrown by the call itself.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or white space; thrown by the call itself.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="XgSourceFormat.Unspecified"/>, the unset value, or no member of <see cref="XgSourceFormat"/>; thrown by the call itself.</exception>
    public static Task<XgSourceRead> ReadFileAsync(
        string path, XgSourceFormat format, bool retainBytes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var decode = RequireDecoder(format);

        return ReadAcquiredAsync(
            path,
            token => File.ReadAllBytesAsync(path, token),
            input => decode(input, path),
            retainBytes,
            cancellationToken);
    }

    // ------------------------------------------------------------------ //
    //  A stream the caller holds
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Reads the source <paramref name="stream"/> holds, which the caller
    /// owns: decodes it as <paramref name="format"/> and returns the outcome,
    /// leaving the stream open.
    /// </summary>
    /// <param name="sourceName">The source's name; it becomes the outcome's <see cref="XgSourceRead.SourceName"/> as passed.</param>
    /// <param name="stream">
    /// The source, borrowed: readable, seekable, and at position 0, the
    /// source's first byte. Never closed or disposed by the read; its
    /// position afterwards is unspecified. A forward-only stream is read
    /// through <see cref="ReadAsync"/>, which buffers it.
    /// </param>
    /// <param name="format">How the stream's bytes are decoded: <see cref="XgSourceFormat.Xg"/> or <see cref="XgSourceFormat.Json"/>, stated by the caller.</param>
    /// <returns>
    /// <see cref="XgSourceDecoded"/> (whose <see cref="XgSourceDecoded.Bytes"/>
    /// is null: the caller holds the source), or <see cref="XgSourceFailed"/>
    /// with the exception that reading or decoding the stream raised.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceName"/> or <paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="sourceName"/> is empty or white space, or
    /// <paramref name="stream"/> is not readable (a disposed stream is not),
    /// not seekable, or not at position 0.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="XgSourceFormat.Unspecified"/>, the unset value, or no member of <see cref="XgSourceFormat"/>.</exception>
    /// <exception cref="OperationCanceledException">Raised while reading or decoding the stream; propagated, never an outcome.</exception>
    public static XgSourceRead ReadStream(
        string sourceName, Stream stream, XgSourceFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(stream);
        var decode = RequireDecoder(format);
        if (!stream.CanRead)
            throw new ArgumentException(
                "The stream must be readable; a closed or disposed stream is not.", nameof(stream));
        if (!stream.CanSeek)
            throw new ArgumentException(
                "The stream must be seekable: the decoder seeks to the payload XG's header locates. " +
                "Read a forward-only stream through XgSourceReader.ReadAsync, which buffers it.", nameof(stream));
        if (stream.Position != 0)
            throw new ArgumentException(
                $"The stream must be at position 0, the source's first byte (it is at {stream.Position}): " +
                "XG's header states its payload's offset from the start of the file.", nameof(stream));

        try
        {
            return new XgSourceDecoded(sourceName, decode(stream, sourceName), bytes: null);
        }
        catch (Exception exception) when (FailsTheSource(exception))
        {
            return new XgSourceFailed(sourceName, exception);
        }
    }

    // ------------------------------------------------------------------ //
    //  An asynchronous acquisition the caller supplies
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Reads the source <paramref name="acquire"/> opens: awaits the stream
    /// it returns, copies it into a buffer with
    /// <paramref name="cancellationToken"/> and disposes it, decodes the
    /// buffer as <paramref name="format"/>, and returns the outcome.
    /// </summary>
    /// <param name="sourceName">The source's name; it becomes the outcome's <see cref="XgSourceRead.SourceName"/> as passed.</param>
    /// <param name="acquire">
    /// Opens the source, given the caller's token — for a browser upload,
    /// <c>token =&gt; Task.FromResult(file.OpenReadStream(limit, token))</c>.
    /// Called at most once, after the token is checked. The read owns the
    /// stream it returns and disposes it; the stream may be forward-only.
    /// Whatever the acquisition throws, or a null task or stream it returns,
    /// fails the source — except a cancellation, which propagates.
    /// </param>
    /// <param name="format">How the source's bytes are decoded: <see cref="XgSourceFormat.Xg"/> or <see cref="XgSourceFormat.Json"/>, stated by the caller.</param>
    /// <param name="retainBytes">Whether a decoded source keeps the acquired bytes (<see cref="XgSourceDecoded.Bytes"/>).</param>
    /// <param name="cancellationToken">
    /// Passed to <paramref name="acquire"/> and to the copy; observed before
    /// the acquisition starts, after it, and before an outcome is returned.
    /// </param>
    /// <returns>
    /// A task for <see cref="XgSourceDecoded"/>, or for
    /// <see cref="XgSourceFailed"/> with the exception that acquiring,
    /// copying or decoding the source raised; a cancelled task when the read
    /// is cancelled.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceName"/> or <paramref name="acquire"/> is null; thrown by the call itself.</exception>
    /// <exception cref="ArgumentException"><paramref name="sourceName"/> is empty or white space; thrown by the call itself.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="XgSourceFormat.Unspecified"/>, the unset value, or no member of <see cref="XgSourceFormat"/>; thrown by the call itself.</exception>
    public static Task<XgSourceRead> ReadAsync(
        string sourceName, Func<CancellationToken, Task<Stream>> acquire,
        XgSourceFormat format, bool retainBytes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(acquire);
        var decode = RequireDecoder(format);

        return ReadAcquiredAsync(
            sourceName,
            token => BufferAsync(acquire, token),
            input => decode(input, sourceName),
            retainBytes,
            cancellationToken);
    }

    // ------------------------------------------------------------------ //
    //  The asynchronous read: acquire, then decode
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The asynchronous read both asynchronous entry points share, after
    /// their arguments are validated: the caller's token is observed before
    /// <paramref name="acquire"/> starts, after it has been awaited (so an
    /// acquisition that ignored the token neither reaches the decoder nor
    /// becomes a failed source) and before the outcome is returned.
    /// <paramref name="decode"/> is the format's decoder; it is a parameter so
    /// the ordering can be tested against a decoder that observes the token.
    /// </summary>
    internal static async Task<XgSourceRead> ReadAcquiredAsync(
        string sourceName,
        Func<CancellationToken, Task<byte[]>> acquire,
        Func<Stream, XgFile> decode,
        bool retainBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] bytes = [];
        Exception? acquisitionFailure = null;
        try
        {
            bytes = await acquire(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (FailsTheSource(exception))
        {
            acquisitionFailure = exception;
        }

        cancellationToken.ThrowIfCancellationRequested();

        XgSourceRead read = acquisitionFailure is null
            ? DecodeAcquired(sourceName, bytes, decode, retainBytes)
            : new XgSourceFailed(sourceName, acquisitionFailure);

        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    /// <summary>
    /// Opens the caller's acquisition and copies its stream into memory the
    /// decoder can seek, disposing the stream whether the copy completes,
    /// fails or is cancelled. When the copy and the disposal both raise, one
    /// precedence decides which exception leaves (<see cref="ReadExceptionStands"/>).
    /// </summary>
    /// <remarks>
    /// Not <c>await using</c>: that is a <c>finally</c>, and an exception thrown
    /// in a <c>finally</c> replaces the one already propagating — a stream that
    /// failed or cancelled its own read and then failed to close would have
    /// lost the read's exception to the disposal's.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The acquisition returned a null task or a null stream.</exception>
    private static async Task<byte[]> BufferAsync(
        Func<CancellationToken, Task<Stream>> acquire, CancellationToken cancellationToken)
    {
        Task<Stream>? opening = acquire(cancellationToken)
            ?? throw new InvalidOperationException("The source's acquisition returned no task.");
        Stream? stream = await opening.ConfigureAwait(false)
            ?? throw new InvalidOperationException("The source's acquisition returned no stream.");

        Exception? readException = null;
        try
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
        catch (Exception exception)
        {
            readException = exception;
            throw;
        }
        finally
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposalException) when (ReadExceptionStands(readException, disposalException))
            {
                // The read's exception, already propagating, is the outcome.
            }
        }
    }

    /// <summary>
    /// The precedence between the exception a read raised and the one its
    /// disposal raised, stated once: the read's stands — dropping the
    /// disposal's — when the read was cancelled, or when it failed and the
    /// disposal did not cancel. So a cancellation always propagates, the
    /// read's own first; an ordinary read failure fails the source with its
    /// own exception; and after a clean read (<paramref name="readException"/>
    /// null) the disposal's exception propagates, a cancellation to the
    /// caller and an ordinary failure to fail the source.
    /// </summary>
    private static bool ReadExceptionStands(Exception? readException, Exception disposalException) =>
        readException is OperationCanceledException
        || (readException is not null && disposalException is not OperationCanceledException);

    /// <summary>Decodes acquired bytes under the failure policy, keeping them when asked.</summary>
    private static XgSourceRead DecodeAcquired(
        string sourceName, byte[] bytes, Func<Stream, XgFile> decode, bool retainBytes)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            // The null is typed: an untyped null converts through
            // ReadOnlyMemory's implicit conversion from an array, to empty
            // memory, which would read as zero retained bytes.
            return new XgSourceDecoded(
                sourceName, decode(input),
                retainBytes ? new ReadOnlyMemory<byte>(bytes) : default(ReadOnlyMemory<byte>?));
        }
        catch (Exception exception) when (FailsTheSource(exception))
        {
            return new XgSourceFailed(sourceName, exception);
        }
    }

    // ------------------------------------------------------------------ //
    //  The policy and the decoders
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The failure policy, stated once: an exception raised while acquiring
    /// or decoding a source fails the source unless it is a cancellation,
    /// which is the caller's and propagates.
    /// </summary>
    private static bool FailsTheSource(Exception exception) => exception is not OperationCanceledException;

    /// <summary>
    /// The decoder <paramref name="format"/> states — one of
    /// <see cref="XgFileReader"/>'s, the library's only ones, given the input
    /// and the source's name — or null when it states none:
    /// <see cref="XgSourceFormat.Unspecified"/>, the unset value, or no member
    /// at all. The one statement of which formats the read decodes, and how.
    /// </summary>
    private static Func<Stream, string, XgFile>? DecoderFor(XgSourceFormat format) => format switch
    {
        XgSourceFormat.Xg => static (input, _) => XgFileReader.ReadStream(input),
        XgSourceFormat.Json => static (input, sourceName) => XgFileReader.DecodeJson(input, sourceName),
        _ => null,
    };

    /// <summary>The decoder <paramref name="format"/> states, validated as the entry point's argument.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="XgSourceFormat.Unspecified"/>, the unset value, or no member of <see cref="XgSourceFormat"/>.</exception>
    private static Func<Stream, string, XgFile> RequireDecoder(XgSourceFormat format) =>
        DecoderFor(format) ?? throw new ArgumentOutOfRangeException(nameof(format), format,
            format == XgSourceFormat.Unspecified
                ? "The format is unset: state XgSourceFormat.Xg or XgSourceFormat.Json."
                : "Not an XgSourceFormat.");
}
