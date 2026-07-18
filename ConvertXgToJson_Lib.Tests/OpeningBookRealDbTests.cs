using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Integration pins against the real XG opening-book database. The fixture
/// (<c>TestData/FixtureFiles/OpeningBookV2.ob</c>, copied from the XG 2
/// install; contents gitignored like all TestData) is required — these tests
/// fail loudly when it is absent rather than silently skipping.
///
/// <para>
/// The ground truth is XG's own tooltip rendering of book entries on this
/// machine (the display oracle): fixture (a) is <c>ajhhBG0407.xg</c> game 9
/// move 1 (roll 41, best candidate 13/9 6/5 — Neil Kazaross, 12,960 games,
/// seed 83467239, 4-ply/4-ply, ±0.0050, 2011-06-18, equity +0.3770);
/// fixture (b) is the 13/10 13/9 candidate at 9-away/9-away (Steven Carey,
/// 20,736 games, seed 13, 3-ply moves / XG Roller cube, ±0.0039,
/// 2012-08-07, equity +0.0103, cubeless +0.0119).
/// </para>
/// </summary>
[Collection("FileIO")]
public class OpeningBookRealDbTests
{
    private static string BookPath => Path.Combine(TestPaths.FixtureFilesDir, "OpeningBookV2.ob");

    private static OpeningBook LoadBook()
    {
        if (!File.Exists(BookPath))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {BookPath}. Copy OpeningBookV2.ob " +
                "from the eXtreme Gammon 2 install directory into TestData/FixtureFiles/.");
        return OpeningBook.Load(BookPath);
    }

    [Fact]
    [Trait("Category", "FileIO")]
    public void RealDb_HeaderAndEntryCount()
    {
        var book = LoadBook();

        book.EntryCount.Should().Be(53210);
        book.Title.Should().Be("Based on rollout performed by the Bgonline.org community");
        book.VersionText.Should().Be("3.70");
        book.FormatVersion.Should().Be(1);
        book.CreatedOn.Date.Should().Be(new DateTime(2011, 5, 21));
        book.Description.Should().StartWith("These Rollouts were performed and posted by the");
        book.Description.Should().EndWith("Grant Hoffman and Steven Carey.",
            "the assembled description must stop at the NUL, not leak block garbage");
    }

    /// <summary>
    /// Fixture (a) end-to-end through the producer's own data path: the
    /// <c>.xg</c> decision's away scores and candidate resulting position
    /// (<see cref="BestMoveAnalysis.PositionsPlayed"/>) build the key; the
    /// book returns the exact entry XG's tooltip shows, and its stored eval
    /// vector is bit-identical to the one XG copied into the <c>.xg</c>
    /// analysis pane (level 998 book stamp). This is the keying convention
    /// session 3's depth stamping will rely on.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void RealDb_FixtureA_KeyedFromXgCandidate_ReturnsTooltipEntry()
    {
        var book = LoadBook();

        string xgPath = Path.Combine(TestPaths.FixtureFilesDir, "ajhhBG0407.xg");
        if (!File.Exists(xgPath))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {xgPath}.");

        var file = XgFileReader.ReadFile(xgPath);
        int matchLength = XgDecisionIterator.ExtractMatchInfo(file)!.MatchLength;
        matchLength.Should().Be(9, "the fixture is a 9-point match");

        // Game 9's first move record; the game header supplies the score.
        int game = 0;
        GameHeaderRecord? header = null;
        MoveRecord? move = null;
        foreach (var record in file.Records)
        {
            if (record is GameHeaderRecord gh) { game++; header = gh; continue; }
            if (game == 9 && record is MoveRecord mv) { move = mv; break; }
        }
        move.Should().NotBeNull();
        header.Should().NotBeNull();

        move!.ActivePlayer.Should().Be(1, "player 1 is on roll at game 9 move 1");
        move.Analysis.EvalLevels[0].Level.Should().Be(998, "the best candidate is a book hit");
        int moverAway = matchLength - header!.Score1;
        int opponentAway = matchLength - header.Score2;
        moverAway.Should().Be(4);
        opponentAway.Should().Be(2);

        var key = OpeningBookKey.ForMatchPlay(
            move.Analysis.PositionsPlayed[0], move.ActivePlayer,
            moverAway, opponentAway, isCrawford: false);

        book.TryGetEntry(key, out var entry).Should().BeTrue();
        entry!.Contributor.Should().Be("Neil Kazaross");
        entry.Level.Should().Be(100, "the tooltip shows a rollout entry");
        entry.Trials.Should().Be(12960);
        entry.Seed.Should().Be(83467239);
        entry.RolloutMovesLevel.Should().Be(3, "4-ply checker play");
        entry.RolloutCubeLevel.Should().Be(3, "4-ply cube");
        entry.EngineVersionMajor.Should().Be(2, "tooltip says XG 2.00");
        entry.EngineVersionMinor.Should().Be(0);
        entry.AnalyzedOn.Date.Should().Be(new DateTime(2011, 6, 18));
        entry.ConfidenceInterval95.Should().BeApproximately(0.0050, 0.0001);
        entry.OnRollAway.Should().Be(2);
        entry.OpponentAway.Should().Be(4);
        entry.Crawford.Should().BeFalse();
        entry.IsMoneySession.Should().BeFalse();

        // XG copied the book vector into the .xg pane verbatim — bitwise.
        var paneEval = move.Analysis.Evals[0];
        entry.Evaluation.LoseBackgammon.Should().Be(paneEval.LoseBackgammon);
        entry.Evaluation.LoseGammon.Should().Be(paneEval.LoseGammon);
        entry.Evaluation.LoseSingle.Should().Be(paneEval.LoseSingle);
        entry.Evaluation.WinSingle.Should().Be(paneEval.WinSingle);
        entry.Evaluation.WinGammon.Should().Be(paneEval.WinGammon);
        entry.Evaluation.WinBackgammon.Should().Be(paneEval.WinBackgammon);
        entry.Evaluation.Equity.Should().Be(paneEval.Equity);
        entry.Evaluation.Equity.Should().BeApproximately(0.3770f, 0.0001f,
            "the tooltip equity is the stored cubeful equity");
    }

    /// <summary>
    /// Fixture (a)'s key resolves against multiple competing entries — the
    /// selection-policy pin on real data: XG shows the 12,960-game
    /// 4-ply/4-ply rollout over a 20,736-game 3-ply/3-ply one (deeper level
    /// beats more trials) and over XG's own Roller++ evaluation.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void RealDb_FixtureA_SelectionPicksDeeperRolloutOverMoreTrials()
    {
        var book = LoadBook();

        var key = OpeningBookKey.ForMatchPlay(
            PositionAfter1394And65PlayedByPlayer1(), activePlayer: 1,
            moverAway: 4, opponentAway: 2, isCrawford: false);

        var entries = book.GetEntries(key);
        entries.Should().HaveCountGreaterThanOrEqualTo(4,
            "the shipped book holds several competing entries for this key");

        entries[0].Seed.Should().Be(83467239, "the tooltip entry must rank first");
        entries.Should().Contain(e => e.Trials == 20736 && e.RolloutMovesLevel == 2,
            "a more-trials-but-shallower rollout exists and must rank below");
        entries.Should().Contain(e => e.Level == 1002,
            "XG's Roller++ evaluation baseline exists and must rank below");
    }

    /// <summary>
    /// Fixture (b): a hand-built resulting position (13/10 13/9 from the
    /// standard start) at 9-away/9-away. Pins the second tooltip oracle,
    /// including the derived cubeless equity — the probability-slot
    /// combination XG displays, distinct from the stored cubeful equity.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void RealDb_FixtureB_NineAwayNineAway_ReturnsTooltipEntry()
    {
        var book = LoadBook();

        // Player 1 plays 13/10 13/9 from the standard start (player-1-relative).
        var positionPlayed = new PositionEngine
        {
            Points = [0, -2, 0, 0, 0, 0, 5, 0, 3, 1, 1, 0, -5, 3, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0],
        };

        var key = OpeningBookKey.ForMatchPlay(
            positionPlayed, activePlayer: 1,
            moverAway: 9, opponentAway: 9, isCrawford: false);

        book.TryGetEntry(key, out var entry).Should().BeTrue();
        entry!.Contributor.Should().Be("Steven Carey");
        entry.Trials.Should().Be(20736);
        entry.Seed.Should().Be(13);
        entry.RolloutMovesLevel.Should().Be(2, "3-ply checker play");
        entry.RolloutCubeLevel.Should().Be(1000, "XG Roller cube");
        entry.AnalyzedOn.Date.Should().Be(new DateTime(2012, 8, 7));
        entry.ConfidenceInterval95.Should().BeApproximately(0.0039, 0.0001);
        entry.Evaluation.Equity.Should().BeApproximately(0.0103f, 0.0001f);

        var e = entry.Evaluation;
        double cubeless = (e.WinSingle - e.LoseSingle)
                        + (e.WinGammon - e.LoseGammon)
                        + (e.WinBackgammon - e.LoseBackgammon);
        cubeless.Should().BeApproximately(0.0119, 0.0001,
            "the tooltip's cubeless equity derives from the probability slots");
    }

    /// <summary>
    /// Money entries key on the Jacoby setting: the same resulting position
    /// resolves to distinct entries with and without Jacoby.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void RealDb_MoneyLookup_KeysOnJacoby()
    {
        var book = LoadBook();
        var positionPlayed = PositionAfter1394And65PlayedByPlayer1();

        var jacobyKey = OpeningBookKey.ForMoneyPlay(positionPlayed, 1, jacoby: true);
        var plainKey = OpeningBookKey.ForMoneyPlay(positionPlayed, 1, jacoby: false);

        book.TryGetEntry(jacobyKey, out var jacobyEntry).Should().BeTrue();
        book.TryGetEntry(plainKey, out var plainEntry).Should().BeTrue();

        jacobyEntry!.IsMoneySession.Should().BeTrue();
        jacobyEntry.Jacoby.Should().BeTrue();
        plainEntry!.IsMoneySession.Should().BeTrue();
        plainEntry.Jacoby.Should().BeFalse();
    }

    /// <summary>Player 1's resulting position after 13/9 6/5 from the
    /// standard start, in the XG record convention (player-1-relative) —
    /// fixture (a)'s best candidate.</summary>
    private static PositionEngine PositionAfter1394And65PlayedByPlayer1() => new()
    {
        Points = [0, -2, 0, 0, 0, 1, 4, 0, 3, 1, 0, 0, -5, 4, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0],
    };
}
