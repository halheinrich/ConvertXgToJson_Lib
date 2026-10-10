using System.Buffers.Binary;
using System.Text;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Synthetic-image tests for <see cref="OpeningBook"/> /
/// <see cref="OpeningBookParser"/>: minimal <c>.ob</c> byte images built
/// inline, so header decoding, entry decoding, key normalization, and the
/// selection policy are all pinned without the binary corpus. The real-DB
/// integration pins live in <c>OpeningBookRealDbTests</c>.
/// </summary>
public class OpeningBookTests
{
    // -----------------------------------------------------------------------
    //  Synthetic image builders
    // -----------------------------------------------------------------------

    private const double Day20120401 = 41000.0; // 1899-12-30 + 41000 days

    /// <summary>Standard-opening resulting position used as a default entry
    /// key: after 13/9 6/5, from the new on-roll player's perspective.</summary>
    private static readonly sbyte[] DefaultStoredPosition =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -4, 5, 0, 0, -1, -3, 0, -4, -1, 0, 0, 0, 2, 0];

    /// <summary>The same play's resulting position as XG stores it in a
    /// record: in the mover's frame, whichever seat moved — the mover's
    /// checkers positive, 13/9 6/5 played from the standard start, stated by
    /// hand. The flip of <see cref="DefaultStoredPosition"/>.</summary>
    private static readonly sbyte[] DefaultPositionPlayedInMoversFrame =
        [0, -2, 0, 0, 0, 1, 4, 0, 3, 1, 0, 0, -5, 4, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    private static byte[] HeaderBlock(
        string title = "Test book",
        string versionText = "3.70",
        double createdDays = 40684.75,
        int formatVersion = 1)
    {
        var block = new byte[OpeningBookParser.BlockSize];
        "OBDB"u8.CopyTo(block.AsSpan(4));
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(8), formatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(16), 256);
        BinaryPrimitives.WriteDoubleLittleEndian(block.AsSpan(24), createdDays);
        block[32] = checked((byte)versionText.Length);
        Encoding.Latin1.GetBytes(versionText).CopyTo(block.AsSpan(33));
        block[41] = checked((byte)title.Length);
        Encoding.Unicode.GetBytes(title).CopyTo(block.AsSpan(42));
        return block;
    }

    /// <summary>A kind-1 description-continuation block: 80 UTF-16 chars at
    /// +4 (shorter input is NUL-padded, which is also how the real format
    /// terminates the assembled text).</summary>
    private static byte[] DescriptionBlock(string chars)
    {
        chars.Length.Should().BeLessThanOrEqualTo(80, "a description block carries 80 chars");
        var block = new byte[OpeningBookParser.BlockSize];
        BinaryPrimitives.WriteInt32LittleEndian(block, 1);
        Encoding.Unicode.GetBytes(chars).CopyTo(block.AsSpan(4));
        return block;
    }

    private static byte[] EntryBlock(
        string contributor = "Tester",
        sbyte[]? position = null,
        int cubeValue = 1, int cubeOwnerSign = 0,
        int onRollAway = -1, int opponentAway = -1,
        bool jacoby = false, bool beaver = false, bool crawford = false,
        float[]? evals = null,
        int level = 100, int verMajor = 2, int verMinor = 0,
        int trials = 1296, float sigma = 0.25f,
        int movesLevel = 2, int cubeLevel = 2, int seed = 42,
        float durationSeconds = 60f,
        double addedDays = Day20120401 + 0.5, double analyzedDays = Day20120401 + 0.25)
    {
        position ??= DefaultStoredPosition;
        evals ??= [0.01f, 0.14f, 0.5f, 0.5f, 0.15f, 0.012f, 0.123f];

        var block = new byte[OpeningBookParser.BlockSize];
        var span = block.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, 2);
        Encoding.Unicode.GetBytes(contributor).CopyTo(span[4..]);
        for (int i = 0; i < 26; i++)
            block[68 + i] = unchecked((byte)position[i]);
        BinaryPrimitives.WriteInt32LittleEndian(span[96..], cubeValue);
        BinaryPrimitives.WriteInt32LittleEndian(span[100..], cubeOwnerSign);
        BinaryPrimitives.WriteInt32LittleEndian(span[104..], onRollAway);
        BinaryPrimitives.WriteInt32LittleEndian(span[108..], opponentAway);
        BinaryPrimitives.WriteInt32LittleEndian(span[112..], jacoby ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[116..], beaver ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[120..], crawford ? 1 : 0);
        for (int i = 0; i < 7; i++)
            BinaryPrimitives.WriteSingleLittleEndian(span[(124 + 4 * i)..], evals[i]);
        BinaryPrimitives.WriteInt32LittleEndian(span[152..], level);
        BinaryPrimitives.WriteInt32LittleEndian(span[160..], verMajor);
        BinaryPrimitives.WriteInt32LittleEndian(span[164..], verMinor);
        BinaryPrimitives.WriteInt32LittleEndian(span[168..], trials);
        BinaryPrimitives.WriteSingleLittleEndian(span[172..], sigma);
        BinaryPrimitives.WriteInt32LittleEndian(span[176..], movesLevel);
        BinaryPrimitives.WriteInt32LittleEndian(span[180..], cubeLevel);
        // +184 / +192 hold unidentified values on a small share of real
        // entries; junk here pins that the parser never reads them.
        BinaryPrimitives.WriteInt32LittleEndian(span[184..], 0x11111111);
        BinaryPrimitives.WriteInt32LittleEndian(span[188..], seed);
        BinaryPrimitives.WriteInt32LittleEndian(span[192..], 0x22222222);
        BinaryPrimitives.WriteSingleLittleEndian(span[196..], durationSeconds);
        BinaryPrimitives.WriteDoubleLittleEndian(span[200..], addedDays);
        BinaryPrimitives.WriteDoubleLittleEndian(span[208..], analyzedDays);
        return block;
    }

    private static byte[] Image(params byte[][] blocks)
    {
        var image = new byte[blocks.Sum(b => b.Length)];
        int offset = 0;
        foreach (var block in blocks)
        {
            block.CopyTo(image.AsSpan(offset));
            offset += block.Length;
        }
        return image;
    }

    private static PositionEngine Pos(sbyte[] points) => new() { Points = points };

    // -----------------------------------------------------------------------
    //  Header + description
    // -----------------------------------------------------------------------

    [Fact]
    public void FromImage_HeaderOnly_ParsesMetadataAndZeroEntries()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(
            title: "My little book", versionText: "9.99",
            createdDays: 41000.5, formatVersion: 3)));

        book.Title.Should().Be("My little book");
        book.VersionText.Should().Be("9.99");
        book.FormatVersion.Should().Be(3);
        book.CreatedOn.Should().Be(new DateTime(2012, 4, 1, 12, 0, 0, DateTimeKind.Utc));
        book.EntryCount.Should().Be(0);
        book.Description.Should().BeEmpty();
    }

    [Fact]
    public void FromImage_Description_SpansBlocksAndTruncatesAtNul()
    {
        // The second block carries text, then a NUL, then stale garbage —
        // the real format's shape (blocks are memory dumps).
        string tail = "tail." + "\0GARBAGE";
        var book = OpeningBook.FromImage(Image(
            HeaderBlock(),
            DescriptionBlock(new string('x', 80)),
            DescriptionBlock(tail)));

        book.Description.Should().Be(new string('x', 80) + "tail.");
    }

    [Fact]
    public void FromImage_UnknownBlockKind_IsSkipped()
    {
        var junk = new byte[OpeningBookParser.BlockSize];
        BinaryPrimitives.WriteInt32LittleEndian(junk, 7);

        var book = OpeningBook.FromImage(Image(HeaderBlock(), junk, EntryBlock()));

        book.EntryCount.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    //  Entry field decoding
    // -----------------------------------------------------------------------

    [Fact]
    public void FromImage_Entry_DecodesEveryField()
    {
        float[] evals = [0.031f, 0.171f, 0.501f, 0.499f, 0.148f, 0.013f, 0.377f];
        var book = OpeningBook.FromImage(Image(HeaderBlock(), EntryBlock(
            contributor: "Neil Kazaross",
            position: DefaultStoredPosition,
            cubeValue: 2, cubeOwnerSign: -1,
            onRollAway: 2, opponentAway: 4,
            jacoby: false, beaver: true, crawford: false,
            evals: evals,
            level: 100, verMajor: 2, verMinor: 10,
            trials: 12960, sigma: 0.2921f,
            movesLevel: 3, cubeLevel: 1000, seed: 83467239,
            durationSeconds: 33324.5f,
            addedDays: 41000.5, analyzedDays: 41000.25)));

        book.EntryCount.Should().Be(1);
        var entry = book.Entries[0];
        entry.Contributor.Should().Be("Neil Kazaross");
        entry.Position.Points.Should().Equal(DefaultStoredPosition);
        entry.CubeValue.Should().Be(2);
        entry.CubeOwnerSign.Should().Be(-1);
        entry.OnRollAway.Should().Be(2);
        entry.OpponentAway.Should().Be(4);
        entry.IsMoneySession.Should().BeFalse();
        entry.Jacoby.Should().BeFalse();
        entry.Beaver.Should().BeTrue();
        entry.Crawford.Should().BeFalse();
        entry.Evaluation.LoseBackgammon.Should().Be(evals[0]);
        entry.Evaluation.LoseGammon.Should().Be(evals[1]);
        entry.Evaluation.LoseSingle.Should().Be(evals[2]);
        entry.Evaluation.WinSingle.Should().Be(evals[3]);
        entry.Evaluation.WinGammon.Should().Be(evals[4]);
        entry.Evaluation.WinBackgammon.Should().Be(evals[5]);
        entry.Evaluation.Equity.Should().Be(evals[6]);
        entry.Level.Should().Be(100);
        entry.EngineVersionMajor.Should().Be(2);
        entry.EngineVersionMinor.Should().Be(10);
        entry.Trials.Should().Be(12960);
        entry.EquityStandardDeviation.Should().Be(0.2921f);
        entry.ConfidenceInterval95.Should().BeApproximately(
            1.96 * 0.2921 / Math.Sqrt(12960), 1e-9);
        entry.RolloutMovesLevel.Should().Be(3);
        entry.RolloutCubeLevel.Should().Be(1000);
        entry.Seed.Should().Be(83467239);
        entry.Duration.Should().Be(TimeSpan.FromSeconds(33324.5f));
        entry.AddedOn.Should().Be(new DateTime(2012, 4, 1, 12, 0, 0, DateTimeKind.Utc));
        entry.AnalyzedOn.Should().Be(new DateTime(2012, 4, 1, 6, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void FromImage_MoneyEvaluationEntry_HasNoConfidenceInterval()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(), EntryBlock(
            onRollAway: -1, opponentAway: -1, jacoby: true,
            level: 1002, trials: 0, sigma: 0f,
            movesLevel: 0, cubeLevel: 0, seed: 0, durationSeconds: 0f)));

        var entry = book.Entries[0];
        entry.IsMoneySession.Should().BeTrue();
        entry.Level.Should().Be(1002);
        entry.ConfidenceInterval95.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    //  Key normalization
    // -----------------------------------------------------------------------

    /// <summary>
    /// The key flips the mover's-frame position XG stores into the book's
    /// frame, that of the player on roll after the play. It takes no seat:
    /// XG stores the resulting position in the mover's frame for either
    /// seat, so the flip is the same whoever moved (halheinrich/backgammon#290;
    /// the seats are pinned through the iterator below).
    /// </summary>
    [Fact]
    public void ForMatchPlay_FlipsTheMoversFrameToTheBooksPerspective()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(onRollAway: 2, opponentAway: 4)));

        var key = OpeningBookKey.ForMatchPlay(
            Pos(DefaultPositionPlayedInMoversFrame), moverAway: 4, opponentAway: 2, isCrawford: false);
        var unflipped = OpeningBookKey.ForMatchPlay(
            Pos(DefaultStoredPosition), moverAway: 4, opponentAway: 2, isCrawford: false);

        book.TryGetEntry(key, out var entry).Should().BeTrue(
            "the mover's resulting position must be flipped to the stored new-on-roll perspective");
        entry!.OnRollAway.Should().Be(2);
        book.TryGetEntry(unflipped, out _).Should().BeFalse(
            "a position already in the book's frame is not what XG stores, and is flipped away from it");
    }

    [Fact]
    public void ForMatchPlay_AwayScores_AreOrientedToTheStoredFrame()
    {
        // Stored (c, d) = (2, 4) means: new-on-roll player 2-away, mover
        // 4-away. Swapping the caller's roles must miss.
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(onRollAway: 2, opponentAway: 4)));

        var swapped = OpeningBookKey.ForMatchPlay(
            Pos(DefaultPositionPlayedInMoversFrame), moverAway: 2, opponentAway: 4, isCrawford: false);

        book.TryGetEntry(swapped, out _).Should().BeFalse();
    }

    [Fact]
    public void MatchKey_CrawfordIsAKeyAxis()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(onRollAway: 1, opponentAway: 4, crawford: true, seed: 1),
            EntryBlock(onRollAway: 1, opponentAway: 4, crawford: false, seed: 2)));

        var crawford = OpeningBookKey.ForMatchPlay(
            Pos(DefaultPositionPlayedInMoversFrame), moverAway: 4, opponentAway: 1, isCrawford: true);
        var postCrawford = OpeningBookKey.ForMatchPlay(
            Pos(DefaultPositionPlayedInMoversFrame), moverAway: 4, opponentAway: 1, isCrawford: false);

        book.TryGetEntry(crawford, out var c).Should().BeTrue();
        book.TryGetEntry(postCrawford, out var pc).Should().BeTrue();
        c!.Seed.Should().Be(1);
        pc!.Seed.Should().Be(2);
    }

    [Fact]
    public void MoneyKey_JacobyIsAKeyAxis()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(jacoby: true, seed: 1),
            EntryBlock(jacoby: false, seed: 2)));

        var withJacoby = OpeningBookKey.ForMoneyPlay(Pos(DefaultPositionPlayedInMoversFrame), jacoby: true);
        var withoutJacoby = OpeningBookKey.ForMoneyPlay(Pos(DefaultPositionPlayedInMoversFrame), jacoby: false);

        book.TryGetEntry(withJacoby, out var j).Should().BeTrue();
        book.TryGetEntry(withoutJacoby, out var nj).Should().BeTrue();
        j!.Seed.Should().Be(1);
        nj!.Seed.Should().Be(2);
    }

    [Fact]
    public void MoneyKey_BeaverIsEntryDataNotAKeyAxis()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(jacoby: true, beaver: false, seed: 1),
            EntryBlock(jacoby: true, beaver: true, seed: 2)));

        var key = OpeningBookKey.ForMoneyPlay(Pos(DefaultPositionPlayedInMoversFrame), jacoby: true);

        book.GetEntries(key).Should().HaveCount(2,
            "entries differing only in Beaver share a key");
    }

    [Fact]
    public void ForMatchPlay_InvalidArguments_Throw()
    {
        var pos = Pos(DefaultPositionPlayedInMoversFrame);

        FluentActions.Invoking(() => OpeningBookKey.ForMatchPlay(null!, 4, 2, false))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => OpeningBookKey.ForMatchPlay(pos, 0, 2, false))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => OpeningBookKey.ForMatchPlay(pos, 4, 0, false))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => OpeningBookKey.ForMoneyPlay(null!, jacoby: false))
            .Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    //  The production lookup, for either mover
    // -----------------------------------------------------------------------

    /// <summary>
    /// The iterator's book enrichment hits for a player-2 mover as for a
    /// player-1 mover (halheinrich/backgammon#290: it once missed nearly
    /// every player-2 hit). Every input is stated by hand, none made by
    /// <see cref="XgGameBuilder"/>: the record states 13/9 6/5 from the
    /// standard start as XG stores it, the resulting position in the mover's
    /// frame (<see cref="DefaultPositionPlayedInMoversFrame"/>), and the book
    /// holds one entry per mover, in the book's own frame
    /// (<see cref="DefaultStoredPosition"/>) with the away pair stored for
    /// that mover at 2–4 in a 7-point match — player 1 5-away, player 2
    /// 3-away. A key in the wrong frame, or with the other mover's away pair,
    /// misses, and the candidate states only the bare book facts.
    /// </summary>
    [Theory]
    [InlineData(1, AnalysisLevel.Ply3, 1296)]
    [InlineData(-1, AnalysisLevel.Ply4, 5184)]
    public void Iterator_EnrichesABookHit_ForEitherMover(int activePlayer, AnalysisLevel level, int trials)
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            // Player 1 moved: player 2 is on roll after it, 3-away; player 1, the mover, 5-away.
            EntryBlock(onRollAway: 3, opponentAway: 5, movesLevel: 2, trials: 1296, seed: 1),
            // Player 2 moved: player 1 is on roll after it, 5-away; player 2, the mover, 3-away.
            EntryBlock(onRollAway: 5, opponentAway: 3, movesLevel: 3, trials: 5184, seed: 2)));

        var play = XgDecisionIterator
            .IterateDiagramRequests(BookStampedOpening(activePlayer), "book.xg",
                options: new XgIteratorOptions(OpeningBook: book))
            .Should().ContainSingle().Which.Should().BeOfType<CheckerPlayDecision>().Subject;

        var candidate = play.Decision.Plays.Should().ContainSingle().Subject;
        candidate.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        candidate.BookEdition.Should().Be(BookEdition.V2);
        candidate.AnalysisLevel.Should().Be(level, "the hit states its own entry's rollout moves level");
        candidate.RolloutTrials.Should().Be(trials);
    }

    /// <summary>
    /// A 7-point match at 2–4 whose one decision is the opening 4-1 played
    /// 13/9 6/5 by <paramref name="activePlayer"/>, its one candidate stamped
    /// with XG's V2 book code — records written by hand.
    /// </summary>
    private static XgFile BookStampedOpening(int activePlayer)
    {
        // The standard start, which reads the same in player 1's frame and the mover's.
        var standard = Pos([0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0]);
        var played = Pos(DefaultPositionPlayedInMoversFrame);
        return new XgFile
        {
            Records =
            [
                new MatchHeaderRecord { MatchLength = 7, Player1 = "Alice", Player2 = "Bob" },
                new GameHeaderRecord { Score1 = 2, Score2 = 4, InitialPosition = standard },
                new MoveRecord
                {
                    InitialPosition = standard,
                    FinalPosition = played,
                    ActivePlayer = activePlayer,
                    Dice = [4, 1],
                    Analysis = new BestMoveAnalysis
                    {
                        MoveCount = 1,
                        Evals = [new EvalResult { Equity = 0.01f }],
                        Moves = [[12, 8, 5, 4, -1, -1, -1, -1]],   // 13/9 6/5
                        EvalLevels = [new EvalLevel { Level = XgDepthFacts.BookV2Code }],
                        PositionsPlayed = [played],
                    },
                    RolloutIndices = [.. Enumerable.Repeat(-1, 32)],
                },
            ],
        };
    }

    // -----------------------------------------------------------------------
    //  Selection policy
    // -----------------------------------------------------------------------

    [Fact]
    public void Selection_RolloutBeatsRollerPlusPlus_EvenWhenLaterInFile()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(level: 100, trials: 1296, seed: 1),
            EntryBlock(level: 1002, trials: 0, sigma: 0f, movesLevel: 0, cubeLevel: 0, seed: 2)));

        book.TryGetEntry(MoneyKey(), out var entry).Should().BeTrue();
        entry!.Seed.Should().Be(1);
    }

    [Fact]
    public void Selection_DeeperLevelsBeatMoreTrials()
    {
        // Mirrors the real-DB oracle: XG shows a 12,960-game 4-ply/4-ply
        // rollout over a 20,736-game 3-ply/3-ply one.
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(movesLevel: 2, cubeLevel: 2, trials: 20736, seed: 1),
            EntryBlock(movesLevel: 3, cubeLevel: 3, trials: 12960, seed: 2)));

        book.TryGetEntry(MoneyKey(), out var entry).Should().BeTrue();
        entry!.Seed.Should().Be(2);
    }

    [Fact]
    public void Selection_CubeLevelBreaksEqualMovesLevel()
    {
        // Mirrors the real-DB oracle: at equal checker level, cube
        // XG Roller (1000) outranks cube 3-ply (2).
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(movesLevel: 2, cubeLevel: 2, trials: 20736, seed: 1),
            EntryBlock(movesLevel: 2, cubeLevel: 1000, trials: 15552, seed: 2)));

        book.TryGetEntry(MoneyKey(), out var entry).Should().BeTrue();
        entry!.Seed.Should().Be(2);
    }

    [Fact]
    public void Selection_MovesLevelComparesBeforeCubeLevel()
    {
        // The documented (unverified — no real-DB discriminating case)
        // lexicographic choice: checker level first.
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(movesLevel: 2, cubeLevel: 1000, trials: 20736, seed: 1),
            EntryBlock(movesLevel: 3, cubeLevel: 2, trials: 1296, seed: 2)));

        book.TryGetEntry(MoneyKey(), out var entry).Should().BeTrue();
        entry!.Seed.Should().Be(2);
    }

    [Fact]
    public void Selection_TrialsThenDateThenFileOrderBreakTies()
    {
        var book = OpeningBook.FromImage(Image(HeaderBlock(),
            EntryBlock(trials: 5184, seed: 1),
            EntryBlock(trials: 10368, seed: 2, analyzedDays: 41000.0),
            EntryBlock(trials: 10368, seed: 3, analyzedDays: 41100.0),
            EntryBlock(trials: 10368, seed: 4, analyzedDays: 41100.0)));

        var entries = book.GetEntries(MoneyKey());

        entries.Select(e => e.Seed).Should().Equal(new[] { 4, 3, 2, 1 },
            "more trials first, then later analysis date, then later file position");
    }

    private static OpeningBookKey MoneyKey() =>
        OpeningBookKey.ForMoneyPlay(Pos(DefaultPositionPlayedInMoversFrame), jacoby: false);

    // -----------------------------------------------------------------------
    //  Malformed input + Load / TryLoad transport
    // -----------------------------------------------------------------------

    [Fact]
    public void FromImage_Malformed_Throws()
    {
        FluentActions.Invoking(() => OpeningBook.FromImage([]))
            .Should().Throw<InvalidDataException>("an empty image has no header");
        FluentActions.Invoking(() => OpeningBook.FromImage(new byte[300]))
            .Should().Throw<InvalidDataException>("length must be a whole number of blocks");
        FluentActions.Invoking(() => OpeningBook.FromImage(new byte[256]))
            .Should().Throw<InvalidDataException>("the magic is missing");

        var wrongKind = HeaderBlock();
        BinaryPrimitives.WriteInt32LittleEndian(wrongKind, 2);
        FluentActions.Invoking(() => OpeningBook.FromImage(Image(wrongKind)))
            .Should().Throw<InvalidDataException>("block 0 must be the kind-0 header");
    }

    [Fact]
    public void Load_RoundTripsThroughAFile_AndTryLoadMirrorsIt()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".ob");
        try
        {
            File.WriteAllBytes(path, Image(HeaderBlock(title: "On disk"), EntryBlock()));

            OpeningBook.Load(path).Title.Should().Be("On disk");

            OpeningBook.TryLoad(path, out var book).Should().BeTrue();
            book!.EntryCount.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryLoad_MissingOrCorruptFile_ReturnsFalse()
    {
        string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".ob");
        OpeningBook.TryLoad(missing, out var fromMissing).Should().BeFalse();
        fromMissing.Should().BeNull();

        string corrupt = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".ob");
        try
        {
            File.WriteAllBytes(corrupt, new byte[512]); // right stride, no magic
            OpeningBook.TryLoad(corrupt, out var fromCorrupt).Should().BeFalse();
            fromCorrupt.Should().BeNull();
        }
        finally
        {
            File.Delete(corrupt);
        }
    }
}
