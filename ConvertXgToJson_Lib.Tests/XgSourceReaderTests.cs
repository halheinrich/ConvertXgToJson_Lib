using System.Buffers.Binary;
using System.Text;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The one read of one source, <see cref="XgSourceReader"/>: every entry
/// point decodes both formats; every exception raised while acquiring or
/// decoding fails the source and is returned, whatever its type; usage
/// errors are thrown before anything is acquired, the asynchronous entry
/// points' from the call itself; a cancellation propagates, and the token
/// is observed before the acquisition, after it and before the outcome;
/// the read closes what it opens and never what it borrows; and a decoded
/// source keeps its bytes only when asked. Gating: every source is
/// synthesized here, through the builder and the writer.
/// </summary>
[Collection("FileIO")]
public sealed class XgSourceReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("XgSourceReaderTests_").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // ------------------------------------------------------------------ //
    //  Sources
    // ------------------------------------------------------------------ //

    /// <summary>A two-decision match: one analysed play each side.</summary>
    private static XgFile Match()
    {
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, new DiceRoll(3, 1), Play.Create(new Move(8, 5), new Move(6, 5)))
            .Play(XgPlayer.Player2, new DiceRoll(6, 1), Play.Create(new Move(13, 7), new Move(8, 7)));
        return builder.Build();
    }

    private static readonly byte[] XgBytes = XgFileWriter.ToBytes(Match());

    private static readonly byte[] JsonBytes = Encoding.UTF8.GetBytes(XgFileReader.ToJson(Match()));

    private static byte[] Valid(XgSourceFormat format) => format == XgSourceFormat.Json ? JsonBytes : XgBytes;

    /// <summary>
    /// What a decoded source must serialize to: the decode primitive's own
    /// reading of the bytes for XG, the document itself for JSON.
    /// </summary>
    private static string ExpectedJson(XgSourceFormat format) =>
        format == XgSourceFormat.Json
            ? Encoding.UTF8.GetString(JsonBytes)
            : XgFileReader.ToJson(XgFileReader.ReadStream(new MemoryStream(XgBytes)));

    /// <summary>A source the decoder refuses: XG's body CRC no longer holds, or JSON that does not parse.</summary>
    private static byte[] Corrupt(XgSourceFormat format)
    {
        if (format == XgSourceFormat.Json)
            return "{ \"records\": "u8.ToArray();
        byte[] bytes = XgBytes.ToArray();
        bytes[^60] ^= 0xFF;   // inside the compressed manifest
        return bytes;
    }

    /// <summary>
    /// An XG header whose header and thumbnail sizes put the payload past
    /// any stream a <see cref="MemoryStream"/> can hold: decoding it from
    /// memory raises an <see cref="ArgumentOutOfRangeException"/> — an
    /// argument-type exception arising from the content, not the caller.
    /// </summary>
    private static byte[] HugeOffsetHeader()
    {
        var bytes = new byte[8232];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x484D4752);              // "RGMH"
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), uint.MaxValue);  // header size
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), uint.MaxValue); // thumbnail size
        return bytes;
    }

    private string WriteTemp(string name, byte[] bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string NameFor(XgSourceFormat format) => format == XgSourceFormat.Json ? "source.json" : "source.xg";

    /// <summary>The entry points, for the tests every one of them must pass.</summary>
    public enum Entry { ReadFile, ReadFileRetaining, ReadFileAsync, ReadStream, ReadAsync }

    public static TheoryData<Entry, XgSourceFormat> EveryEntryAndFormat()
    {
        var data = new TheoryData<Entry, XgSourceFormat>();
        foreach (var entry in Enum.GetValues<Entry>())
            foreach (var format in Enum.GetValues<XgSourceFormat>())
                data.Add(entry, format);
        return data;
    }

    /// <summary>
    /// Reads <paramref name="bytes"/> through <paramref name="entry"/>: from a
    /// file for the file entries, from a seekable memory stream for
    /// <see cref="XgSourceReader.ReadStream"/>, and from a forward-only stream
    /// — a browser upload's shape — for <see cref="XgSourceReader.ReadAsync"/>.
    /// Returns the outcome and the name it must carry.
    /// </summary>
    private async Task<(XgSourceRead Read, string Name)> ReadThrough(Entry entry, byte[] bytes, XgSourceFormat format)
    {
        string name = NameFor(format);
        switch (entry)
        {
            case Entry.ReadFile:
            {
                string path = WriteTemp(name, bytes);
                return (XgSourceReader.ReadFile(path, format), path);
            }
            case Entry.ReadFileRetaining:
            {
                string path = WriteTemp(name, bytes);
                return (XgSourceReader.ReadFile(path, format, retainBytes: true), path);
            }
            case Entry.ReadFileAsync:
            {
                string path = WriteTemp(name, bytes);
                return (await XgSourceReader.ReadFileAsync(path, format), path);
            }
            case Entry.ReadStream:
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return (XgSourceReader.ReadStream(name, stream, format), name);
            }
            default:
            {
                var probe = new ProbeStream(bytes, seekable: false);
                var read = await XgSourceReader.ReadAsync(name, _ => Task.FromResult<Stream>(probe), format);
                probe.Disposed.Should().BeTrue("the read owns the stream its acquisition returns");
                return (read, name);
            }
        }
    }

    // ------------------------------------------------------------------ //
    //  A source of each kind decodes; the outcome names it
    // ------------------------------------------------------------------ //

    [Theory]
    [MemberData(nameof(EveryEntryAndFormat))]
    public async Task EveryEntry_DecodesBothFormats_AndNamesTheSource(Entry entry, XgSourceFormat format)
    {
        var (read, name) = await ReadThrough(entry, Valid(format), format);

        var decoded = read.Should().BeOfType<XgSourceDecoded>().Subject;
        decoded.SourceName.Should().Be(name, "an outcome names the source exactly as the caller did");
        XgFileReader.ToJson(decoded.File).Should().Be(ExpectedJson(format),
            "the read decodes through the library's one decoder for the format");
        XgDecisionIterator.Iterate(decoded.File, NameFor(format)).Should().HaveCount(2);
    }

    // ------------------------------------------------------------------ //
    //  The failure policy: every exception from acquiring or decoding
    // ------------------------------------------------------------------ //

    [Theory]
    [MemberData(nameof(EveryEntryAndFormat))]
    public async Task ZeroByteSource_IsAFailedSource(Entry entry, XgSourceFormat format)
    {
        var (read, name) = await ReadThrough(entry, [], format);

        var failed = read.Should().BeOfType<XgSourceFailed>().Subject;
        failed.SourceName.Should().Be(name);
        failed.Exception.Should().BeAssignableTo(format == XgSourceFormat.Json ? typeof(JsonException) : typeof(EndOfStreamException));
    }

    [Theory]
    [MemberData(nameof(EveryEntryAndFormat))]
    public async Task ADecodeThatThrows_IsAFailedSource_CarryingTheDecodersException(Entry entry, XgSourceFormat format)
    {
        var (read, name) = await ReadThrough(entry, Corrupt(format), format);

        var failed = read.Should().BeOfType<XgSourceFailed>().Subject;
        failed.SourceName.Should().Be(name);
        failed.Exception.Should().BeAssignableTo(format == XgSourceFormat.Json ? typeof(JsonException) : typeof(InvalidDataException));
    }

    [Theory]
    [InlineData(Entry.ReadFile)]
    [InlineData(Entry.ReadFileRetaining)]
    [InlineData(Entry.ReadFileAsync)]
    [InlineData(Entry.ReadStream)]
    [InlineData(Entry.ReadAsync)]
    public async Task AJsonSourceThatIsNull_IsAFailedSource_NamingIt(Entry entry)
    {
        var (read, name) = await ReadThrough(entry, "null"u8.ToArray(), XgSourceFormat.Json);

        var failed = read.Should().BeOfType<XgSourceFailed>().Subject;
        failed.Exception.Should().BeOfType<InvalidDataException>()
            .Which.Message.Should().Contain(name, "the refusal names the source it read");
    }

    /// <summary>
    /// Malformed content can raise an argument-type exception while it is
    /// decoded; it is still the source's failure — an exception's type never
    /// classifies it as the caller's misuse.
    /// </summary>
    [Theory]
    [InlineData(Entry.ReadFileRetaining)]
    [InlineData(Entry.ReadFileAsync)]
    [InlineData(Entry.ReadStream)]
    [InlineData(Entry.ReadAsync)]
    public async Task MalformedContent_RaisingAnArgumentException_IsAFailedSource(Entry entry)
    {
        var (read, _) = await ReadThrough(entry, HugeOffsetHeader(), XgSourceFormat.Xg);

        read.Should().BeOfType<XgSourceFailed>()
            .Which.Exception.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AMissingFile_IsAFailedSource()
    {
        string missing = Path.Combine(_dir, "missing.xg");

        XgSourceReader.ReadFile(missing).Should().BeOfType<XgSourceFailed>()
            .Which.Exception.Should().BeOfType<FileNotFoundException>();
        (await XgSourceReader.ReadFileAsync(missing)).Should().BeOfType<XgSourceFailed>()
            .Which.Exception.Should().BeOfType<FileNotFoundException>();
    }

    /// <summary>
    /// Only a null or blank path is the caller's mistake; a path the file
    /// system refuses to open, whatever it raises, is that source's failure.
    /// </summary>
    [Fact]
    public void APathTheFileSystemRefuses_IsAFailedSource()
    {
        XgSourceReader.ReadFile("bad\0path.xg").Should().BeOfType<XgSourceFailed>()
            .Which.SourceName.Should().Be("bad\0path.xg");
    }

    public static TheoryData<string, Func<CancellationToken, Task<Stream>>> FailingAcquisitions() => new()
    {
        { "throws", _ => throw new IOException("the upload broke") },
        { "faults", _ => Task.FromException<Stream>(new IOException("the upload broke")) },
        { "throws an argument exception", _ => throw new ArgumentException("the acquisition's own argument") },
        { "returns no task", _ => null! },
        { "returns no stream", _ => Task.FromResult<Stream>(null!) },
    };

    [Theory]
    [MemberData(nameof(FailingAcquisitions))]
    public async Task AnAcquisitionThatFails_IsAFailedSource(string how, Func<CancellationToken, Task<Stream>> acquire)
    {
        var read = await XgSourceReader.ReadAsync("upload.xg", acquire);

        var failed = read.Should().BeOfType<XgSourceFailed>($"an acquisition that {how} fails the source").Subject;
        failed.SourceName.Should().Be("upload.xg");
        failed.Exception.Should().NotBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task AnAcquisitionThatThrows_IsFailedByThatVeryException()
    {
        var thrown = new IOException("the upload broke");

        var read = await XgSourceReader.ReadAsync("upload.xg", _ => throw thrown);

        read.Should().BeOfType<XgSourceFailed>().Which.Exception.Should().BeSameAs(thrown);
    }

    [Fact]
    public async Task AStreamThatFailsWhileBuffered_IsAFailedSource_AndIsDisposed()
    {
        var thrown = new IOException("the connection dropped");
        var probe = new ProbeStream(XgBytes, seekable: false) { OnRead = () => throw thrown };

        var read = await XgSourceReader.ReadAsync("upload.xg", _ => Task.FromResult<Stream>(probe));

        read.Should().BeOfType<XgSourceFailed>().Which.Exception.Should().BeSameAs(thrown);
        probe.Disposed.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    //  Usage errors: thrown, before anything is acquired
    // ------------------------------------------------------------------ //

    private const XgSourceFormat Undefined = (XgSourceFormat)7;

    [Fact]
    public void ReadFile_UsageErrors_AreThrown()
    {
        FluentActions.Invoking(() => XgSourceReader.ReadFile(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => XgSourceReader.ReadFile("")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => XgSourceReader.ReadFile("  ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => XgSourceReader.ReadFile(WriteTemp("a.xg", XgBytes), Undefined))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadFileAsync_UsageErrors_AreThrownByTheCallItself()
    {
        // Not awaited: an argument error is the call's, before any task exists.
        Action nullPath = () => _ = XgSourceReader.ReadFileAsync(null!);
        Action blankPath = () => _ = XgSourceReader.ReadFileAsync(" ");
        Action undefined = () => _ = XgSourceReader.ReadFileAsync(WriteTemp("a.xg", XgBytes), Undefined);

        nullPath.Should().Throw<ArgumentNullException>();
        blankPath.Should().Throw<ArgumentException>();
        undefined.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadStream_UsageErrors_AreThrown_AndTheStreamIsUntouched()
    {
        using var stream = new MemoryStream(XgBytes);
        FluentActions.Invoking(() => XgSourceReader.ReadStream(null!, stream)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => XgSourceReader.ReadStream(" ", stream)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => XgSourceReader.ReadStream("a.xg", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => XgSourceReader.ReadStream("a.xg", stream, Undefined))
            .Should().Throw<ArgumentOutOfRangeException>();

        var disposed = new MemoryStream(XgBytes);
        disposed.Dispose();
        FluentActions.Invoking(() => XgSourceReader.ReadStream("a.xg", disposed))
            .Should().Throw<ArgumentException>().WithParameterName("stream");

        using var forwardOnly = new ProbeStream(XgBytes, seekable: false);
        FluentActions.Invoking(() => XgSourceReader.ReadStream("a.xg", forwardOnly))
            .Should().Throw<ArgumentException>().WithParameterName("stream");
        forwardOnly.Reads.Should().Be(0, "a usage error is thrown before the stream is read");

        stream.Position = 3;
        FluentActions.Invoking(() => XgSourceReader.ReadStream("a.xg", stream))
            .Should().Throw<ArgumentException>().WithParameterName("stream");
        stream.Position.Should().Be(3, "a usage error is thrown before the stream is read");
    }

    [Fact]
    public void ReadAsync_UsageErrors_AreThrownByTheCallItself()
    {
        int acquisitions = 0;
        Func<CancellationToken, Task<Stream>> acquire = _ =>
        {
            acquisitions++;
            return Task.FromResult<Stream>(new MemoryStream(XgBytes));
        };

        Action nullName = () => _ = XgSourceReader.ReadAsync(null!, acquire);
        Action blankName = () => _ = XgSourceReader.ReadAsync("", acquire);
        Action nullAcquire = () => _ = XgSourceReader.ReadAsync("a.xg", null!);
        Action undefined = () => _ = XgSourceReader.ReadAsync("a.xg", acquire, Undefined);

        nullName.Should().Throw<ArgumentNullException>();
        blankName.Should().Throw<ArgumentException>();
        nullAcquire.Should().Throw<ArgumentNullException>();
        undefined.Should().Throw<ArgumentOutOfRangeException>();
        acquisitions.Should().Be(0, "a usage error is thrown before anything is acquired");
    }

    // ------------------------------------------------------------------ //
    //  Cancellation stays a cancellation
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task ReadAsync_CancelledBeforeAcquisition_ThrowsWithoutAcquiring()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int acquisitions = 0;

        var reading = XgSourceReader.ReadAsync("upload.xg", _ =>
        {
            acquisitions++;
            return Task.FromResult<Stream>(new MemoryStream(XgBytes));
        }, cancellationToken: cts.Token);

        (await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>())
            .Which.CancellationToken.Should().Be(cts.Token);
        acquisitions.Should().Be(0);
    }

    [Fact]
    public async Task ReadFileAsync_CancelledBeforeAcquisition_Throws_AndLeavesTheFileClosed()
    {
        string path = WriteTemp("a.xg", XgBytes);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Awaiting(() => XgSourceReader.ReadFileAsync(path, cancellationToken: cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        AssertClosed(path);
    }

    [Fact]
    public async Task ReadAsync_CancelledDuringTheAcquisition_Throws()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();

        var reading = XgSourceReader.ReadAsync("upload.xg", async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new MemoryStream(XgBytes);
        }, cancellationToken: cts.Token);

        await started.Task;
        cts.Cancel();

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadAsync_CancelledWhileBuffering_Throws_AndDisposesTheStream()
    {
        using var cts = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        var probe = new ProbeStream(XgBytes, seekable: false)
        {
            BeforeReadAsync = async token =>
            {
                reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
        };

        var reading = XgSourceReader.ReadAsync("upload.xg", _ => Task.FromResult<Stream>(probe), cancellationToken: cts.Token);
        await reached.Task;
        cts.Cancel();

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
        probe.Disposed.Should().BeTrue("the read disposes the stream it owns when it is cancelled");
    }

    [Fact]
    public async Task AnAcquisitionIgnoringTheToken_CannotTurnACancelledReadIntoADecodedSource()
    {
        using var cts = new CancellationTokenSource();
        var probe = new ProbeStream(XgBytes, seekable: false);

        var reading = XgSourceReader.ReadAsync("upload.xg", _ =>
        {
            cts.Cancel();                                   // the caller cancels …
            return Task.FromResult<Stream>(probe);          // … and the acquisition completes regardless
        }, cancellationToken: cts.Token);

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
        probe.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task AnAcquisitionIgnoringTheToken_CannotTurnACancelledReadIntoAFailedSource()
    {
        using var cts = new CancellationTokenSource();

        var reading = XgSourceReader.ReadAsync("upload.xg", _ =>
        {
            cts.Cancel();
            throw new IOException("the handle closed under the cancellation");
        }, cancellationToken: cts.Token);

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A cancellation the acquisition raises on its own — a timeout — is not
    /// the caller's, and still propagates: it ends the caller's operation
    /// rather than failing only this source.
    /// </summary>
    [Fact]
    public async Task ACancellationTheCallersTokenDidNotCause_Propagates()
    {
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        var timedOut = new TaskCanceledException("the upload timed out", null, timeout.Token);

        var reading = XgSourceReader.ReadAsync("upload.xg", _ => throw timedOut, cancellationToken: CancellationToken.None);

        (await FluentActions.Awaiting(() => reading).Should().ThrowAsync<TaskCanceledException>())
            .Which.Should().BeSameAs(timedOut);
    }

    [Fact]
    public void ACancellationRaisedWhileDecoding_Propagates_AndTheBorrowedStreamStaysOpen()
    {
        var probe = new ProbeStream(XgBytes, seekable: true) { OnRead = () => throw new OperationCanceledException() };

        FluentActions.Invoking(() => XgSourceReader.ReadStream("a.xg", probe))
            .Should().Throw<OperationCanceledException>();
        probe.Disposed.Should().BeFalse("a borrowed stream is never disposed by the read");
    }

    /// <summary>
    /// The token is observed after the awaited acquisition: when it was
    /// cancelled meanwhile, the acquired bytes never reach the decoder.
    /// </summary>
    [Fact]
    public async Task TheToken_IsObservedAfterTheAcquisition_BeforeDecoding()
    {
        using var cts = new CancellationTokenSource();
        int decodes = 0;

        var reading = XgSourceReader.ReadAcquiredAsync(
            "upload.xg",
            _ =>
            {
                cts.Cancel();
                return Task.FromResult(XgBytes);
            },
            input =>
            {
                decodes++;
                return XgFileReader.ReadStream(input);
            },
            retainBytes: false,
            cts.Token);

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
        decodes.Should().Be(0);
    }

    /// <summary>
    /// The token is observed before an outcome is returned: a read cancelled
    /// while it decoded (decoding itself is not interrupted) is neither a
    /// decoded nor a failed source.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheToken_IsObservedBeforeReturningAnOutcome(bool decodeSucceeds)
    {
        using var cts = new CancellationTokenSource();

        var reading = XgSourceReader.ReadAcquiredAsync(
            "upload.xg",
            _ => Task.FromResult(XgBytes),
            input =>
            {
                cts.Cancel();
                return decodeSucceeds ? XgFileReader.ReadStream(input) : throw new InvalidDataException("refused");
            },
            retainBytes: false,
            cts.Token);

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ACancellationRaisedByTheDecoder_Propagates()
    {
        var reading = XgSourceReader.ReadAcquiredAsync(
            "upload.xg",
            _ => Task.FromResult(XgBytes),
            _ => throw new OperationCanceledException(),
            retainBytes: false,
            CancellationToken.None);

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------ //
    //  Resources: closed when opened here, never when borrowed
    // ------------------------------------------------------------------ //

    /// <summary>Opening the file exclusively succeeds only if no handle to it is left open.</summary>
    private static void AssertClosed(string path)
    {
        FluentActions.Invoking(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose())
            .Should().NotThrow("the read closes every file it opens");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TheFileEntries_CloseTheFile_WhateverTheOutcome(bool asynchronous, bool corrupt)
    {
        string path = WriteTemp("a.xg", corrupt ? Corrupt(XgSourceFormat.Xg) : XgBytes);

        var read = asynchronous ? await XgSourceReader.ReadFileAsync(path) : XgSourceReader.ReadFile(path);
        read.Should().BeAssignableTo(corrupt ? typeof(XgSourceFailed) : typeof(XgSourceDecoded));
        AssertClosed(path);

        var retained = asynchronous
            ? await XgSourceReader.ReadFileAsync(path, retainBytes: true)
            : XgSourceReader.ReadFile(path, retainBytes: true);
        retained.Should().BeAssignableTo(corrupt ? typeof(XgSourceFailed) : typeof(XgSourceDecoded));
        AssertClosed(path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadStream_LeavesTheBorrowedStreamOpen_WhateverTheOutcome(bool corrupt)
    {
        var probe = new ProbeStream(corrupt ? Corrupt(XgSourceFormat.Xg) : XgBytes, seekable: true);

        var read = XgSourceReader.ReadStream("a.xg", probe);

        read.Should().BeAssignableTo(corrupt ? typeof(XgSourceFailed) : typeof(XgSourceDecoded));
        probe.Disposed.Should().BeFalse("the caller owns the stream");
        probe.CanRead.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_DisposesTheAcquiredStream_WhenItDecodesAndWhenItFails(bool corrupt)
    {
        var probe = new ProbeStream(corrupt ? Corrupt(XgSourceFormat.Xg) : XgBytes, seekable: false);

        var read = await XgSourceReader.ReadAsync("upload.xg", _ => Task.FromResult<Stream>(probe));

        read.Should().BeAssignableTo(corrupt ? typeof(XgSourceFailed) : typeof(XgSourceDecoded));
        probe.Disposed.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    //  What a decoded source keeps
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(XgSourceFormat.Xg)]
    [InlineData(XgSourceFormat.Json)]
    public async Task RetainBytes_KeepsTheExactAcquiredBytes(XgSourceFormat format)
    {
        byte[] source = Valid(format);
        string path = WriteTemp(NameFor(format), source);

        XgSourceRead[] reads =
        [
            XgSourceReader.ReadFile(path, format, retainBytes: true),
            await XgSourceReader.ReadFileAsync(path, format, retainBytes: true),
            await XgSourceReader.ReadAsync(NameFor(format),
                _ => Task.FromResult<Stream>(new ProbeStream(source, seekable: false)), format, retainBytes: true),
        ];

        foreach (var read in reads)
            read.Should().BeOfType<XgSourceDecoded>().Which.Bytes!.Value.ToArray().Should().Equal(source);
    }

    [Theory]
    [MemberData(nameof(EveryEntryAndFormat))]
    public async Task WithoutRetainBytes_NoBytesAreKept(Entry entry, XgSourceFormat format)
    {
        if (entry == Entry.ReadFileRetaining)
            return;

        var (read, _) = await ReadThrough(entry, Valid(format), format);

        read.Should().BeOfType<XgSourceDecoded>().Which.Bytes.Should().BeNull();
    }

    [Fact]
    public async Task ADecodedFile_OutlivesEveryResourceTheReadUsed()
    {
        string path = WriteTemp("a.xg", XgBytes);
        var fromFile = (XgSourceDecoded)XgSourceReader.ReadFile(path);
        File.Delete(path);

        var probe = new ProbeStream(XgBytes, seekable: false);
        var fromUpload = (XgSourceDecoded)await XgSourceReader.ReadAsync("upload.xg", _ => Task.FromResult<Stream>(probe));
        probe.Disposed.Should().BeTrue();

        foreach (var decoded in new[] { fromFile, fromUpload })
        {
            XgDecisionIterator.Iterate(decoded.File, "a.xg").Should().HaveCount(2);
            XgFileReader.ToJson(XgFileReader.ReadStream(new MemoryStream(XgFileWriter.ToBytes(decoded.File))))
                .Should().Be(ExpectedJson(XgSourceFormat.Xg), "the file still writes, whole");
        }
    }

    // ------------------------------------------------------------------ //
    //  The closed pair
    // ------------------------------------------------------------------ //

    [Fact]
    public void MatchAndSwitch_TakeTheOutcomesBranch_AndRefuseANullBranch()
    {
        var decoded = XgSourceReader.ReadStream("a.xg", new MemoryStream(XgBytes));
        var failed = XgSourceReader.ReadStream("b.xg", new MemoryStream(Corrupt(XgSourceFormat.Xg)));

        decoded.Match(d => "decoded", f => "failed").Should().Be("decoded");
        failed.Match(d => "decoded", f => "failed").Should().Be("failed");

        string? taken = null;
        decoded.Switch(_ => taken = "decoded", _ => taken = "failed");
        taken.Should().Be("decoded");
        failed.Switch(_ => taken = "decoded", _ => taken = "failed");
        taken.Should().Be("failed");

        FluentActions.Invoking(() => decoded.Match<int>(null!, _ => 0)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => failed.Match<int>(_ => 0, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => decoded.Switch(_ => { }, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => failed.Switch(null!, _ => { })).Should().Throw<ArgumentNullException>();

        decoded.ToString().Should().Be("a.xg: decoded");
        failed.ToString().Should().StartWith("b.xg: failed: InvalidDataException: ");
    }

    // ------------------------------------------------------------------ //
    //  A stream that records what was done to it
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A read-only stream over <c>bytes</c>, seekable or forward-only, that
    /// records its disposal and its reads, can run a hook on each read, and
    /// ignores the token its asynchronous reads are given unless its
    /// <see cref="BeforeReadAsync"/> hook observes it.
    /// </summary>
    private sealed class ProbeStream(byte[] bytes, bool seekable) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        public Action? OnRead { get; init; }
        public Func<CancellationToken, Task>? BeforeReadAsync { get; init; }

        public override bool CanRead => !Disposed;
        public override bool CanSeek => seekable && !Disposed;
        public override bool CanWrite => false;
        public override long Length => seekable ? _inner.Length : throw new NotSupportedException();

        public override long Position
        {
            get => seekable ? _inner.Position : throw new NotSupportedException();
            set
            {
                if (!seekable) throw new NotSupportedException();
                _inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            OnRead?.Invoke();
            return _inner.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (BeforeReadAsync is not null)
                await BeforeReadAsync(cancellationToken);
            Reads++;
            OnRead?.Invoke();
            return _inner.Read(buffer.Span);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) =>
            seekable ? _inner.Seek(offset, origin) : throw new NotSupportedException();

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
