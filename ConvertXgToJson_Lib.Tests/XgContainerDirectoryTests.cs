using System.Buffers.Binary;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;
using ConvertXgToJson_Lib.Writing;
using Entry = ConvertXgToJson_Lib.Tests.Helpers.RawXgContainer.Entry;
using Trailer = ConvertXgToJson_Lib.Tests.Helpers.RawXgContainer.Trailer;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The container reader's contract (halheinrich/backgammon#343;
/// <see cref="XgDecompressor"/>'s class doc states it), against payloads
/// <see cref="RawXgContainer"/> synthesizes byte by byte:
/// <list type="bullet">
/// <item>that a payload is read through its directory when the directory
/// validates, inflating no inner file before it is consulted; that only a
/// payload whose directory fails is tested as the legacy single stream; and
/// that every other payload is refused — never assigned by record
/// size;</item>
/// <item>one refusal control per directory check, each refused with the
/// same <see cref="InvalidDataException"/> by all three read surfaces, the
/// game-header enumeration's deferred;</item>
/// <item>the one extraction rule — stored entries read raw, compressed ones
/// within their extent — and that a metadata read extracts temp.xg alone,
/// so it neither extracts nor certifies the other inner files.</item>
/// </list>
/// The evidence for the layout is the real corpus; the two real files that
/// first showed stored entries are pinned, local-only, in
/// <see cref="ManifestStreamAssignmentTests"/>.
/// </summary>
public class XgContainerDirectoryTests
{
    private const string DirectoryRefusal = "its container directory cannot be used";

    // -----------------------------------------------------------------------
    //  Inner content: a 7-point match, Alice v Bob, of two games
    // -----------------------------------------------------------------------

    private static readonly byte[] GameRecords =
    [
        .. XgBytesBuilder.BuildMatchHeaderRecord("Alice", "Bob", 7, new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc)),
        .. XgBytesBuilder.BuildGameHeaderRecord(score1: 0, score2: 0, gameNum: 1),
        .. XgBytesBuilder.BuildGameHeaderRecord(score1: 2, score2: 1, gameNum: 2),
        .. XgBytesBuilder.BuildMatchFooterRecord(7),
    ];

    private static readonly byte[] IndexRecords =
        [.. GameRecords[..SaveRecordParser.RecordSize], .. GameRecords[^SaveRecordParser.RecordSize..]];

    private static readonly byte[] CommentTable = "first\r\nsecond\r\n"u8.ToArray();

    private static Entry Xg => new("temp.xg", GameRecords);
    private static Entry Xgi => new("temp.xgi", IndexRecords);
    private static Entry Xgc => new("temp.xgc", CommentTable);

    /// <summary>A valid three-file container: temp.xg, temp.xgi, temp.xgc.</summary>
    private static byte[] ValidPayload() => RawXgContainer.Payload([Xg, Xgi, Xgc]);

    /// <summary>The end-record's archivesize field: where the manifest begins.</summary>
    private static uint ManifestOffsetOf(byte[] payload) =>
        BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(payload.Length - RawXgContainer.EndRecordSize + 16));

    // -----------------------------------------------------------------------
    //  The three read surfaces
    // -----------------------------------------------------------------------

    private static XgFile ReadFull(byte[] payload) =>
        XgFileReader.ReadStream(new MemoryStream(RawXgContainer.File(payload)));

    /// <summary>
    /// Reads <paramref name="payload"/> through all three surfaces and
    /// asserts each sees the match <see cref="GameRecords"/> holds.
    /// </summary>
    private static XgFile AssertEverySurfaceReadsTheMatch(byte[] payload)
    {
        var file = ReadFull(payload);
        file.Records.Should().HaveCount(4, "temp.xg holds four records");
        file.Records[0].Should().BeOfType<MatchHeaderRecord>();

        using var temp = new TempXgFile(RawXgContainer.File(payload));
        var info = XgFileReader.ReadMatchInfo(temp.Path);
        info.Should().NotBeNull();
        info!.Player1.Should().Be("Alice");
        info.Player2.Should().Be("Bob");
        info.Terms.Should().Be(new MatchTerms { Length = 7 });

        var state = new XgIteratorState();
        XgFileReader.ReadGameHeaders(temp.Path, state).Select(g => g.Standing).Should().Equal(
            new MatchStanding { Away1 = 7, Away2 = 7, IsCrawford = false },
            new MatchStanding { Away1 = 5, Away2 = 6, IsCrawford = false });
        state.MatchInfo.Should().NotBeNull();
        return file;
    }

    /// <summary>
    /// All three surfaces refuse <paramref name="payload"/> with the same
    /// <see cref="InvalidDataException"/>, whose message names the failed
    /// check by <paramref name="phrase"/>; the game-header enumeration
    /// defers it to its first <c>MoveNext</c>.
    /// </summary>
    private static void AssertEverySurfaceRefuses(byte[] payload, string phrase)
    {
        byte[] bytes = RawXgContainer.File(payload);
        string message = FluentActions.Invoking(() => XgFileReader.ReadStream(new MemoryStream(bytes)))
            .Should().Throw<InvalidDataException>().Which.Message;
        message.Should().Contain(phrase);

        using var temp = new TempXgFile(bytes);
        FluentActions.Invoking(() => XgFileReader.ReadMatchInfo(temp.Path))
            .Should().Throw<InvalidDataException>().Which.Message.Should().Be(message);

        IEnumerable<XgGameInfo> games = [];
        FluentActions.Invoking(() => games = XgFileReader.ReadGameHeaders(temp.Path, new XgIteratorState()))
            .Should().NotThrow("the refusal is deferred to enumeration");
        FluentActions.Invoking(() => games.ToList())
            .Should().Throw<InvalidDataException>().Which.Message.Should().Be(message);
    }

    /// <summary>
    /// The full read refuses <paramref name="payload"/> naming
    /// <paramref name="phrase"/>, while both metadata reads — which extract
    /// temp.xg alone — still read the match.
    /// </summary>
    private static void AssertOnlyTheFullReadRefuses(byte[] payload, string phrase)
    {
        FluentActions.Invoking(() => ReadFull(payload))
            .Should().Throw<InvalidDataException>().WithMessage($"*{phrase}*");

        using var temp = new TempXgFile(RawXgContainer.File(payload));
        XgFileReader.ReadMatchInfo(temp.Path)!.Player1.Should().Be("Alice",
            "a metadata read extracts temp.xg alone and does not certify the other inner files");
        XgFileReader.ReadGameHeaders(temp.Path, new XgIteratorState()).Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    //  Which format: a directory that validates, else exactly one zlib stream
    // -----------------------------------------------------------------------

    [Fact]
    public void ExactlyOneZlibStream_ReadsAsTheLegacyFormat_OnEverySurface()
    {
        byte[] payload = RawXgContainer.Zlib([.. GameRecords, .. IndexRecords]);

        using (var streams = XgDecompressor.Decompress(new MemoryStream(payload)))
        {
            streams.GameRecords.ToArray().Should().Equal(GameRecords, "the index's two records are split off the tail");
            streams.IndexRecords.ToArray().Should().Equal(IndexRecords);
            streams.RolloutContexts.Length.Should().Be(0);
            streams.Comments.Length.Should().Be(0);
        }
        AssertEverySurfaceReadsTheMatch(payload);
    }

    [Fact]
    public void OneZlibStream_FollowedByStoredBytes_IsRefused()
    {
        byte[] payload = [.. RawXgContainer.Zlib([.. GameRecords, .. IndexRecords]), 0x0D, 0x0A];

        AssertEverySurfaceRefuses(payload, "are not an end-record");
    }

    [Fact]
    public void OneGoodStream_FollowedByAStreamThatFailsToInflate_IsRefused()
    {
        byte[] broken = RawXgContainer.Zlib(CommentTable);
        broken[^5] ^= 0xFF;
        byte[] payload = [.. RawXgContainer.Zlib([.. GameRecords, .. IndexRecords]), .. broken];

        AssertEverySurfaceRefuses(payload, "are not an end-record");
    }

    [Fact]
    public void ConcatenatedStreams_WithNoEndRecord_AreRefused_NeverAssignedByRecordSize()
    {
        // The shape the record-size heuristics used to read: temp.xg, then
        // temp.xgi, then a comment-sized stream that was taken for temp.xgc.
        byte[] payload =
        [
            .. RawXgContainer.Zlib(GameRecords),
            .. RawXgContainer.Zlib(IndexRecords),
            .. RawXgContainer.Zlib(CommentTable),
        ];

        AssertEverySurfaceRefuses(payload, "are not an end-record");
    }

    [Fact]
    public void Container_WithATruncatedEndRecord_IsRefused()
    {
        byte[] payload = ValidPayload()[..^10];

        AssertEverySurfaceRefuses(payload, DirectoryRefusal);
    }

    [Fact]
    public void Container_CutAtTheEndOfItsFirstStream_ReadsAsLegacy_TheStatedLimit()
    {
        // The rule's one stated limit: what remains is one complete zlib
        // stream, which is the legacy format by definition.
        byte[] first = RawXgContainer.Zlib([.. GameRecords, .. IndexRecords]);
        byte[] payload = RawXgContainer.Payload([new("temp.xg", [.. GameRecords, .. IndexRecords]), Xgc]);
        payload[..first.Length].Should().Equal(first);

        AssertEverySurfaceReadsTheMatch(payload[..first.Length]);
    }

    /// <summary>
    /// The directory is consulted before any inner file is inflated. This
    /// container's first stream is temp.xgc, declared as the comment table's
    /// few bytes but a zip bomb of 64 MiB of zeros. Deciding the format by
    /// inflating the stream at byte 0 first — the order before this test —
    /// made every surface inflate it in full and discard it; read through the
    /// directory first, the metadata reads never touch it and the full read
    /// stops at its declared size. The observation is the managed bytes each
    /// read allocates on this thread, far below the bomb's size.
    /// </summary>
    [Fact]
    public void ValidContainer_IsReadThroughItsDirectory_BeforeAnyInnerFileIsInflated()
    {
        const long budget = 16 * 1024 * 1024;
        byte[] bomb = RawXgContainer.Zlib(new byte[64 * 1024 * 1024]);
        byte[] file = RawXgContainer.File(RawXgContainer.Payload([Xgc with { Body = bomb }, Xg, Xgi]));
        using var temp = new TempXgFile(file);

        static long Allocated(Action read)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            read();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Allocated(() => XgFileReader.ReadMatchInfo(temp.Path)!.Player1.Should().Be("Alice"))
            .Should().BeLessThan(budget, "the match info read inflates temp.xg alone");
        Allocated(() => XgFileReader.ReadGameHeaders(temp.Path, new XgIteratorState()).Should().HaveCount(2))
            .Should().BeLessThan(budget, "the game-header read inflates temp.xg alone");
        Allocated(() => FluentActions.Invoking(() => XgFileReader.ReadStream(new MemoryStream(file)))
                .Should().Throw<InvalidDataException>()
                .WithMessage($"*inner file 'temp.xgc' cannot be read: it inflates to more than the {CommentTable.Length} bytes*"))
            .Should().BeLessThan(budget, "the full read stops temp.xgc at its declared size");
    }

    /// <summary>
    /// A legacy stream with no match footer, ending in a game header: its
    /// index records — the match header and that last game header again —
    /// sit at the stream's tail and are split off before the game headers
    /// are read, so every game is yielded exactly once. At 0d7282b the
    /// game-header read took the whole stream and yielded the last game a
    /// second time from the index's copy.
    /// </summary>
    [Fact]
    public void LegacyStream_WithNoMatchFooter_EndingInAGameHeader_YieldsEveryGameOnce()
    {
        const int record = SaveRecordParser.RecordSize;
        byte[] records = GameRecords[..^record];
        byte[] index = [.. records[..record], .. records[^record..]];
        using var temp = new TempXgFile(RawXgContainer.File(RawXgContainer.Zlib([.. records, .. index])));

        XgFileReader.ReadGameHeaders(temp.Path, new XgIteratorState()).Select(g => g.Standing).Should().Equal(
            new MatchStanding { Away1 = 7, Away2 = 7, IsCrawford = false },
            new MatchStanding { Away1 = 5, Away2 = 6, IsCrawford = false });
    }

    [Fact]
    public void PayloadAskingForAPresetDictionary_IsRefusedAsInvalidData()
    {
        // .NET raises a non-public IOException for this header; the reader
        // must still refuse with its own InvalidDataException.
        byte[] payload = [0x78, 0xBB, 0x00, 0x00, 0x00, 0x01, 0x03, 0x00];

        AssertEverySurfaceRefuses(payload, "too few for the 36-byte end-record");
    }

    // -----------------------------------------------------------------------
    //  The directory — one refusal control per check, on every surface
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidContainer_ReadsOnEverySurface()
    {
        var file = AssertEverySurfaceReadsTheMatch(ValidPayload());
        file.Comments.Should().Equal("first", "second");
    }

    [Fact]
    public void Payload_ShorterThanAnEndRecord_IsRefused() =>
        AssertEverySurfaceRefuses([0x01, 0x02, 0x03], "the payload holds 3 bytes, too few for the 36-byte end-record");

    [Theory]
    [InlineData("version 2")]
    [InlineData("compressed registry 0")]
    [InlineData("a non-zero reserved byte")]
    public void EndRecord_WithAnotherShape_IsRefused(string shape)
    {
        Trailer trailer = shape switch
        {
            "version 2" => new() { Version = 2 },
            "compressed registry 0" => new() { CompressedRegistry = 0 },
            "a non-zero reserved byte" => new() { LastReservedByte = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        AssertEverySurfaceRefuses(RawXgContainer.Payload([Xg, Xgi, Xgc], trailer), "are not an end-record");
    }

    [Fact]
    public void EndRecord_CountingNoInnerFiles_IsRefused() =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc], new Trailer { FileCount = 0 }),
            "the end-record counts no inner files");

    [Fact]
    public void Manifest_NotEndingWhereTheEndRecordBegins_IsRefused()
    {
        uint manifestOffset = ManifestOffsetOf(ValidPayload());

        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc], new Trailer { ArchiveSize = manifestOffset - 1 }),
            "does not end where the end-record begins");
    }

    [Fact]
    public void BodyCrc_Mismatch_IsRefused()
    {
        byte[] payload = ValidPayload();
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(payload.Length - RawXgContainer.EndRecordSize));

        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc], new Trailer { Crc = crc ^ 1 }),
            "the body's CRC32 is");
    }

    [Theory]
    [InlineData("stored raw", "are not a valid zlib stream")]
    [InlineData("a trailing byte", "are a zlib stream that ends before")]
    [InlineData("its last byte missing", "are a zlib stream that runs past")]
    public void Manifest_ThatIsNotExactlyOneZlibStream_IsRefused(string shape, string phrase)
    {
        Func<byte[], byte[]> encode = shape switch
        {
            "stored raw" => m => m,
            "a trailing byte" => m => [.. RawXgContainer.Zlib(m), 0x00],
            "its last byte missing" => m => RawXgContainer.Zlib(m)[..^1],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc], encodeManifest: encode),
            $"the manifest's compressed bytes {phrase}");
    }

    [Fact]
    public void Manifest_ShorterThanTheEndRecordCounts_IsRefused() =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi], new Trailer { FileCount = 3 }),
            "the manifest inflates to 1064 bytes, but the end-record's 3 entries need 1596");

    [Fact]
    public void Manifest_LongerThanTheEndRecordCounts_IsRefused() =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi], new Trailer { FileCount = 1 }),
            "the manifest inflates to more bytes than the end-record's 1 entries need 532");

    [Fact]
    public void Entry_NamingNoXgInnerFile_IsRefused() =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, new Entry("temp.xgz", CommentTable)]),
            "manifest entry 'temp.xgz' does not name an XG inner file");

    [Fact]
    public void Entry_NamingAnInnerFileTwice_IsRefused_CaseInsensitively() =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, new Entry("TEMP.XG", GameRecords)]),
            "the manifest names temp.xg more than once");

    [Fact]
    public void Manifest_NamingNoTempXg_IsRefused() =>
        AssertEverySurfaceRefuses(RawXgContainer.Payload([Xgi, Xgc]), "the manifest names no temp.xg");

    /// <summary>
    /// Each entry is shaped so that reading it as stored would succeed —
    /// raw bytes, equal sizes, their CRC — so only refusing the flag can
    /// pass: no value but 0 and 1 is read as either.
    /// </summary>
    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)0x80)]
    [InlineData((byte)0xFF)]
    public void Entry_WithAStorageFlagOtherThanTheTwoEvidenced_IsRefused_NeverReadAsStored(byte flag) =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Stored = true, Flag = flag }]),
            $"manifest entry 'temp.xgc' has storage flag {flag}, where only 0 (compressed) and 1 (stored) are known");

    [Fact]
    public void StoredEntry_WithUnequalSizes_IsRefused() =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Stored = true, UncompressedSize = (uint)CommentTable.Length + 1 }]),
            $"manifest entry 'temp.xgc' is stored, but its stored size ({CommentTable.Length}) differs from its uncompressed size ({CommentTable.Length + 1})");

    [Fact]
    public void Entry_EndingOneBytePastTheManifest_IsRefused()
    {
        uint manifestOffset = ManifestOffsetOf(RawXgContainer.Payload([Xg, Xgi]));

        // Nothing is written for it, so the manifest begins where this
        // one-byte entry claims to: its last byte would be the manifest's first.
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload(
            [
                Xg, Xgi,
                new Entry("temp.xgc", [0x41]) { Stored = true, Body = [], CompressedSize = 1, Offset = manifestOffset },
            ]),
            $"manifest entry 'temp.xgc' spans bytes {manifestOffset} to {manifestOffset + 1L} of the body, " +
            $"past the manifest at byte {manifestOffset}");
    }

    [Fact]
    public void Entry_WhoseExtentWrapsInThirtyTwoBits_IsRefused_ComputedWithoutOverflow()
    {
        // uint.MaxValue - 1 + 4 wraps to 2, which a 32-bit sum would place
        // well before the manifest.
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload(
            [
                Xg, Xgi,
                new Entry("temp.xgc", [1, 2, 3, 4]) { Stored = true, Body = [], CompressedSize = 4, Offset = uint.MaxValue - 1 },
            ]),
            $"spans bytes {uint.MaxValue - 1} to {(long)uint.MaxValue + 3} of the body, past the manifest");
    }

    [Fact]
    public void ZeroLengthStoredEntries_TheLastExactlyAtTheManifestOffset_AreValid()
    {
        // The real .xg's shape: an empty stored temp.xgr between temp.xg and
        // temp.xgi, and an empty stored temp.xgc lying exactly where the
        // manifest begins.
        byte[] payload = RawXgContainer.Payload(
        [
            Xg,
            new Entry("temp.xgr", []) { Stored = true },
            Xgi,
            new Entry("temp.xgc", []) { Stored = true },
        ]);

        var file = AssertEverySurfaceReadsTheMatch(payload);
        file.Rollouts.Should().BeEmpty();
        file.Comments.Should().BeEmpty("an empty temp.xgc holds no comment");
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)3)]
    [InlineData((byte)9)]
    [InlineData((byte)0xFF)]
    public void CompressedEntries_AtAnyCompressionLevel_AreRead_TheLevelIsNoGate(byte level)
    {
        var file = AssertEverySurfaceReadsTheMatch(
            RawXgContainer.Payload([Xg with { Level = level }, Xgi with { Level = level }, Xgc with { Level = level }]));
        file.Comments.Should().Equal("first", "second");
    }

    [Fact]
    public void Entry_PathFieldAndPadding_AreNeverRead()
    {
        var file = AssertEverySurfaceReadsTheMatch(
            RawXgContainer.Payload([Xg with { Path = "C:/XG/temp", Padding = 0xBEEF }, Xgi, Xgc]));
        file.Comments.Should().Equal("first", "second");
    }

    // -----------------------------------------------------------------------
    //  Selection by name
    // -----------------------------------------------------------------------

    [Fact]
    public void ManifestListingAnotherFileAheadOfTempXg_IsReadByNameOnEverySurface()
    {
        // temp.xgi's stream comes first in the body, so "the first stream"
        // is not temp.xg here; every surface must select it by name.
        var file = AssertEverySurfaceReadsTheMatch(RawXgContainer.Payload([Xgi, Xgc, Xg]));
        file.Comments.Should().Equal("first", "second");
    }

    [Fact]
    public void StoredTempXg_IsReadRawOnEverySurface() =>
        AssertEverySurfaceReadsTheMatch(RawXgContainer.Payload([Xg with { Stored = true }, Xgi, Xgc]));

    /// <summary>
    /// A stored, non-empty temp.xgc holding two comments, and a play whose
    /// record references the second: the table's exact bytes arrive, and
    /// each decision resolves its own comment.
    /// </summary>
    [Fact]
    public void StoredCommentTable_ArrivesByteForByte_AndItsIndicesHold()
    {
        var threeOne = new DiceRoll(3, 1);
        var makeFivePoint = Play.Create(new Move(8, 5), new Move(6, 5));
        var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
        builder.AddGame()
            .Play(XgPlayer.Player1, threeOne, makeFivePoint, comment: "the first comment")
            .Play(XgPlayer.Player2, threeOne, makeFivePoint, comment: "the second comment");
        XgFile model = builder.Build();

        byte[] xgc = CommentWriter.WriteAll(model.Comments);
        byte[] payload = RawXgContainer.Payload(
        [
            new("temp.xg", SaveRecordWriter.WriteAll(model.Records)),
            new("temp.xgi", SaveRecordWriter.WriteAll([model.Records[0], model.Records[^1]])),
            new("temp.xgc", xgc) { Stored = true },
        ]);

        using (var streams = XgDecompressor.Decompress(new MemoryStream(payload)))
            streams.Comments.ToArray().Should().Equal(xgc, "a stored entry is its raw bytes");

        var file = ReadFull(payload);
        file.Comments.Should().Equal("the first comment", "the second comment");
        file.Records.OfType<MoveRecord>().Select(m => m.CommentIndex).Should().Equal(0, 1);
        XgDecisionIterator.IterateDiagramRequests(file, "stored.xg")
            .Select(d => d.Descriptive.Comment)
            .Should().Equal("the first comment", "the second comment");
    }

    // -----------------------------------------------------------------------
    //  Extraction — the one rule, applied only to what a surface asks for
    // -----------------------------------------------------------------------

    [Fact]
    public void InnerFile_InflatingShortOfItsDeclaredSize_FailsTheFullRead_NotTheMetadataReads() =>
        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { UncompressedSize = (uint)CommentTable.Length + 1 }]),
            $"inner file 'temp.xgc' cannot be read: it inflates to {CommentTable.Length} bytes, " +
            $"but its manifest entry declares {CommentTable.Length + 1}");

    [Fact]
    public void InnerFile_InflatingPastItsDeclaredSize_IsRefused() =>
        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { UncompressedSize = (uint)CommentTable.Length - 1 }]),
            $"it inflates to more than the {CommentTable.Length - 1} bytes its manifest entry declares");

    [Fact]
    public void CompressedEntry_WhoseStreamEndsBeforeItsExtent_IsRefused() =>
        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Body = [.. RawXgContainer.Zlib(CommentTable), 0x00] }]),
            "inner file 'temp.xgc' cannot be read: its compressed bytes are a zlib stream that ends before");

    [Fact]
    public void CompressedEntry_WhoseStreamRunsPastItsExtent_IsRefused()
    {
        // The stream's last byte lies outside the declared extent, though
        // still before the manifest: it is read within its extent, not to
        // wherever the stream happens to end.
        byte[] stream = RawXgContainer.Zlib(CommentTable);

        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Body = stream, CompressedSize = (uint)stream.Length - 1 }]),
            "inner file 'temp.xgc' cannot be read: its compressed bytes are a zlib stream that runs past");
    }

    [Fact]
    public void CompressedEntry_ThatIsNotZlib_IsRefused() =>
        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Body = CommentTable }]),
            "inner file 'temp.xgc' cannot be read: its compressed bytes are not a valid zlib stream");

    [Fact]
    public void CompressedEntry_AskingForAPresetDictionary_IsRefusedAsInvalidData() =>
        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Body = [0x78, 0xBB, 0x00, 0x00, 0x00, 0x01, 0x03, 0x00] }]),
            "inner file 'temp.xgc' cannot be read: its compressed bytes are not a valid zlib stream");

    [Fact]
    public void CompressedEntry_WithAnEmptyExtent_IsRefused() =>
        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Body = [] }]),
            "inner file 'temp.xgc' cannot be read: its compressed bytes are a zlib stream that runs past its 0-byte extent");

    /// <summary>
    /// The per-entry CRC check, added by halheinrich/backgammon#343: the
    /// bytes are intact (the body CRC holds), but they are not the bytes the
    /// entry describes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InnerFile_WhoseCrcContradictsItsEntry_IsRefused(bool stored) =>
        AssertOnlyTheFullReadRefuses(
            RawXgContainer.Payload([Xg, Xgi, Xgc with { Stored = stored, Crc32 = System.IO.Hashing.Crc32.HashToUInt32(CommentTable) ^ 1 }]),
            "inner file 'temp.xgc' cannot be read: its CRC32 is 0x");

    [Fact]
    public void TempXg_FailingExtraction_IsRefusedAlikeOnEverySurface() =>
        AssertEverySurfaceRefuses(
            RawXgContainer.Payload([Xg with { Crc32 = System.IO.Hashing.Crc32.HashToUInt32(GameRecords) ^ 1 }, Xgi, Xgc]),
            "inner file 'temp.xg' cannot be read: its CRC32 is 0x");
}
