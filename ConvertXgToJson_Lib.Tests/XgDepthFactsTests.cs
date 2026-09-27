using AwesomeAssertions.Execution;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgDepthFacts"/> — the one decoding of XG's analysis
/// depth into the typed facts a record stores: the
/// <see cref="AnalysisMode"/> × <see cref="AnalysisLevel"/> pair, the rollout
/// trial count, the book edition and an unrecognized level's raw code. The
/// label, abbreviation and rank those facts determine are BgDataTypes_Lib's
/// derivations, pinned there, so nothing here asserts one: this class pins
/// what the producer states. The level table is covered code by code, the
/// rollout branch through synthesized <see cref="RolloutContext"/>s and the
/// book branch through synthesized <see cref="OpeningBookEntry"/>s, so none of
/// it depends on the binary corpus; fixture-pinned regressions exercise both
/// against real files and the real book database, which is why the class joins
/// the file-IO collection.
/// </summary>
[Collection("FileIO")]
public class XgDepthFactsTests
{
    private static readonly List<RolloutContext> NoRollouts = [];

    // -----------------------------------------------------------------------
    //  The level table
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0,    AnalysisLevel.Ply1)]
    [InlineData(1,    AnalysisLevel.Ply2)]
    [InlineData(11,   AnalysisLevel.Ply2)]
    [InlineData(12,   AnalysisLevel.Ply3Red)]
    [InlineData(2,    AnalysisLevel.Ply3)]
    [InlineData(1000, AnalysisLevel.XgRoller)]
    [InlineData(3,    AnalysisLevel.Ply4)]
    [InlineData(1001, AnalysisLevel.XgRollerPlus)]
    [InlineData(4,    AnalysisLevel.Ply5)]
    [InlineData(5,    AnalysisLevel.Ply6)]
    [InlineData(6,    AnalysisLevel.Ply7)]
    [InlineData(1002, AnalysisLevel.XgRollerPlusPlus)]
    public void OfLevel_EvaluationCode_IsAnEvaluationAtItsLevel(int code, AnalysisLevel level)
    {
        XgDepthFacts.OfLevel(code).Should().Be(new XgDepthFacts(AnalysisMode.Evaluation, level));
    }

    /// <summary>
    /// The book codes read "backwards" — 998 is the newer V2 book, 999 the V1
    /// — and a book hit states its edition and no level: the file records none.
    /// </summary>
    [Theory]
    [InlineData(998, BookEdition.V2)]
    [InlineData(999, BookEdition.V1)]
    public void OfLevel_BookCode_IsABookHitOfItsEdition(int code, BookEdition edition)
    {
        XgDepthFacts.OfLevel(code).Should().Be(
            new XgDepthFacts(AnalysisMode.BookRollout, AnalysisLevel.Unknown, BookEdition: edition));
    }

    /// <summary>The rollout sentinel on its own states a rollout and nothing more: its parameters are a rollout context's.</summary>
    [Fact]
    public void OfLevel_RolloutSentinel_IsARolloutWithNoParameters()
    {
        XgDepthFacts.OfLevel(100).Should().Be(new XgDepthFacts(AnalysisMode.Rollout, AnalysisLevel.Unknown));
    }

    /// <summary>
    /// A code the table does not recognize states its raw code, so the depth
    /// still reads as itself, and nothing else — never a guessed level.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(7777)]
    [InlineData(-100)]
    public void OfLevel_UnrecognizedCode_StatesItsRawCode(int code)
    {
        XgDepthFacts.OfLevel(code).Should().Be(
            new XgDepthFacts(AnalysisMode.Unknown, AnalysisLevel.Unknown, UnrecognizedLevelCode: code));
    }

    /// <summary>
    /// Every recognized <see cref="AnalysisLevel"/> is reached by an evaluation
    /// code, so a member added to the enum fails here until the table decodes
    /// something to it. Codes 1 and 11 share one arm (XG's display draws no
    /// distinction, halheinrich/backgammon#160), so they decode identically.
    /// </summary>
    [Fact]
    public void OfLevel_Table_ReachesEveryRecognizedLevel()
    {
        int[] evaluationCodes = [0, 1, 11, 12, 2, 1000, 3, 1001, 4, 5, 6, 1002];

        evaluationCodes.Select(c => XgDepthFacts.OfLevel(c).Level).Distinct().Should().BeEquivalentTo(
            Enum.GetValues<AnalysisLevel>().Where(l => l != AnalysisLevel.Unknown));
        XgDepthFacts.OfLevel(11).Should().Be(XgDepthFacts.OfLevel(1));
    }

    /// <summary>
    /// A rollout index outside the file's contexts — negative, or past the end —
    /// is no rollout: the analysis states what its level code does.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(42)]
    public void Resolve_RolloutIndexOutsideTheContexts_StatesTheLevelCode(int index)
    {
        XgDepthFacts.Resolve(2, index, NoRollouts).Should().Be(XgDepthFacts.OfLevel(2));
    }

    // -----------------------------------------------------------------------
    //  A rollout
    // -----------------------------------------------------------------------

    /// <summary>
    /// A rollout states its inner evaluation level, decoded through the same
    /// table as an evaluation, and its trial count; the analysis's own level
    /// code is not consulted.
    /// </summary>
    [Theory]
    [InlineData(2,    1296, AnalysisLevel.Ply3)]
    [InlineData(3,     648, AnalysisLevel.Ply4)]
    [InlineData(0,     500, AnalysisLevel.Ply1)]
    [InlineData(6,     100, AnalysisLevel.Ply7)]
    [InlineData(1000, 1296, AnalysisLevel.XgRoller)]
    [InlineData(1001, 1296, AnalysisLevel.XgRollerPlus)]
    [InlineData(1002, 1296, AnalysisLevel.XgRollerPlusPlus)]
    public void Resolve_Rollout_StatesItsInnerLevelAndTrials(int innerCode, int trials, AnalysisLevel level)
    {
        List<RolloutContext> rollouts = [new() { Level1 = innerCode, Level2 = innerCode, GamesRolled = trials }];

        XgDepthFacts.Resolve(7777, 0, rollouts).Should().Be(
            new XgDepthFacts(AnalysisMode.Rollout, level, RolloutTrials: trials));
    }

    /// <summary>
    /// The first phase names the rollout when one exists — <c>LevelCut</c>
    /// above 0 — whatever the levels hold (the user's ruling on
    /// halheinrich/backgammon#251): XG's "First 2 moves: 4-ply … Remaining
    /// moves: XG Roller" is a 4-ply rollout, and its mirror an XG Roller one.
    /// Level 0 is 1-ply, not "unset", and <c>LevelTrunc</c> is not a depth
    /// input.
    /// </summary>
    [Fact]
    public void Resolve_Rollout_ThePhaseNamingItFollowsLevelCut()
    {
        static AnalysisLevel Named(RolloutContext rollout) => XgDepthFacts.Resolve(100, 0, [rollout]).Level;

        using var scope = new AssertionScope();
        Named(new() { LevelCut = 2, Level1 = 3, Level2 = 1000, GamesRolled = 1296 }).Should().Be(AnalysisLevel.Ply4);
        Named(new() { LevelCut = 2, Level1 = 1000, Level2 = 3, GamesRolled = 1296 }).Should().Be(AnalysisLevel.XgRoller);
        Named(new() { LevelCut = 0, Level1 = 0, Level2 = 2, GamesRolled = 100 }).Should().Be(AnalysisLevel.Ply3);
        Named(new() { LevelCut = 0, Level1 = 3, Level2 = 2, GamesRolled = 100 }).Should().Be(AnalysisLevel.Ply3);
        Named(new() { LevelCut = 2, Level1 = 0, Level2 = 2, GamesRolled = 100 }).Should().Be(AnalysisLevel.Ply1);
        Named(new() { LevelCut = 0, Level1 = 0, Level2 = 0, LevelTrunc = 3, GamesRolled = 100 }).Should().Be(AnalysisLevel.Ply1);
    }

    /// <summary>
    /// An inner code that is not an evaluation level — unrecognized, or a code
    /// of another kind — states its raw code with the level unknown; the mode
    /// stays a rollout, because the rollout itself is recorded.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(998)]
    [InlineData(100)]
    public void Resolve_Rollout_InnerCodeNotAnEvaluationLevel_StatesTheRawCode(int innerCode)
    {
        XgDepthFacts.Resolve(0, 0, [new() { Level1 = innerCode, Level2 = innerCode, GamesRolled = 200 }]).Should().Be(
            new XgDepthFacts(AnalysisMode.Rollout, AnalysisLevel.Unknown, RolloutTrials: 200, UnrecognizedLevelCode: innerCode));
    }

    /// <summary>A trial count of 0 is none recorded: a record's count is a number of games rolled, at least 1.</summary>
    [Fact]
    public void Resolve_Rollout_ZeroGamesRolled_StatesNoTrialCount()
    {
        XgDepthFacts.Resolve(100, 0, [new() { Level2 = 2, GamesRolled = 0 }]).RolloutTrials.Should().BeNull();
    }

    /// <summary>Each candidate resolves its own rollout: nothing carries over between indices.</summary>
    [Fact]
    public void Resolve_Rollout_EachIndexResolvesItsOwnContext()
    {
        List<RolloutContext> rollouts =
        [
            new() { Level2 = 2, GamesRolled = 1296 },
            new() { Level2 = 3, GamesRolled = 5000 },
        ];

        XgDepthFacts.Resolve(0, 0, rollouts).Should().Be(new XgDepthFacts(AnalysisMode.Rollout, AnalysisLevel.Ply3, RolloutTrials: 1296));
        XgDepthFacts.Resolve(0, 1, rollouts).Should().Be(new XgDepthFacts(AnalysisMode.Rollout, AnalysisLevel.Ply4, RolloutTrials: 5000));
    }

    // -----------------------------------------------------------------------
    //  A book hit
    // -----------------------------------------------------------------------

    /// <summary>
    /// A book hit enriched by a rollout entry states the entry's rollout moves
    /// level and trial count beside its edition.
    /// </summary>
    [Theory]
    [InlineData(3,    12960, AnalysisLevel.Ply4)]
    [InlineData(2,    20736, AnalysisLevel.Ply3)]
    [InlineData(12,     648, AnalysisLevel.Ply3Red)]
    [InlineData(1000, 20736, AnalysisLevel.XgRoller)]
    public void Resolve_BookHitWithARolloutEntry_StatesTheEntrysLevelAndTrials(int movesLevel, int trials, AnalysisLevel level)
    {
        var entry = new OpeningBookEntry { Level = 100, Trials = trials, RolloutMovesLevel = movesLevel };

        XgDepthFacts.Resolve(998, -1, NoRollouts, entry).Should().Be(
            new XgDepthFacts(AnalysisMode.BookRollout, level, RolloutTrials: trials, BookEdition: BookEdition.V2));
    }

    /// <summary>
    /// The book's Roller++ evaluation baselines store zeroed rollout
    /// parameters, so an evaluation entry does not enrich: the hit states its
    /// bare book facts, never a bogus 1-ply from the zeroed moves level.
    /// </summary>
    [Fact]
    public void Resolve_BookHitWithAnEvaluationEntry_StatesTheBareBookFacts()
    {
        var entry = new OpeningBookEntry { Level = 1002, Trials = 0, RolloutMovesLevel = 0 };

        XgDepthFacts.Resolve(998, -1, NoRollouts, entry).Should().Be(XgDepthFacts.OfLevel(998));
    }

    /// <summary>An entry beside an analysis that is not a book hit enriches nothing.</summary>
    [Fact]
    public void Resolve_EntryBesideAnEvaluation_IsIgnored()
    {
        var entry = new OpeningBookEntry { Level = 100, Trials = 12960, RolloutMovesLevel = 3 };

        XgDepthFacts.Resolve(2, -1, NoRollouts, entry).Should().Be(XgDepthFacts.OfLevel(2));
    }

    /// <summary>An explicit rollout the file carries outranks a cached book rollout: the branch order is a contract.</summary>
    [Fact]
    public void Resolve_RolloutIndexAndBookEntry_TheRolloutWins()
    {
        var entry = new OpeningBookEntry { Level = 100, Trials = 12960, RolloutMovesLevel = 3 };

        XgDepthFacts.Resolve(998, 0, [new() { Level2 = 2, GamesRolled = 1296 }], entry).Should().Be(
            new XgDepthFacts(AnalysisMode.Rollout, AnalysisLevel.Ply3, RolloutTrials: 1296));
    }

    // -----------------------------------------------------------------------
    //  End to end: the facts a record states
    // -----------------------------------------------------------------------

    private static string BookPath => Path.Combine(TestPaths.FixtureFilesDir, "OpeningBookV2.ob");

    private static OpeningBook LoadBook()
    {
        if (!File.Exists(BookPath))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {BookPath}. Copy OpeningBookV2.ob " +
                "from the eXtreme Gammon 2 install directory into TestData/FixtureFiles/.");
        return OpeningBook.Load(BookPath);
    }

    private static CheckerPlayDecision FixtureAPlay(XgIteratorOptions? options = null)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "ajhhBG0407.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");
        return XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), Path.GetFileName(path), options: options)
            .OfType<CheckerPlayDecision>()
            .Single(r => r.Game == 9 && r.MoveNumber == 1);
    }

    /// <summary>
    /// XG stamps an opening-book hit as a bare level code with no rollout
    /// context. In <c>ajhhBG0024.xg</c>, game 6's opening play is a V2 hit;
    /// with no book database supplied its candidates state the book mode, the
    /// V2 edition and no level.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_BookOpening_StatesTheBareBookFacts()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "ajhhBG0024.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");

        var play = XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), Path.GetFileName(path))
            .OfType<CheckerPlayDecision>()
            .Single(r => r.Game == 6 && r.MoveNumber == 1);

        var best = play.Decision.RankedBy(PlayRanking.Equity).Best.Candidate;
        best.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        best.AnalysisLevel.Should().Be(AnalysisLevel.Unknown, "no book database was supplied, so no level is recovered");
        best.BookEdition.Should().Be(BookEdition.V2);
        best.RolloutTrials.Should().BeNull();
    }

    /// <summary>
    /// Fixture (a) (<c>ajhhBG0407.xg</c> game 9 move 1) with the real book
    /// database: each of the decision's five V2 hits resolves its own entry.
    /// Three rollout-backed hits state their rollout's moves level and trials
    /// — two of Neil Kazaross's 12,960-game 4-ply rollouts (the entry XG's
    /// tooltip shows for 13/9 6/5) and Steven Carey's 15,552-game 3-ply one;
    /// two resolve to the book's Roller++ evaluation baselines and stay bare.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_FixtureA_WithBook_EachHitStatesItsOwnEntry()
    {
        var play = FixtureAPlay(new XgIteratorOptions(OpeningBook: LoadBook()));

        var hits = play.Decision.Plays.Where(p => p.AnalysisMode == AnalysisMode.BookRollout).ToList();
        hits.Should().HaveCount(5, "five of the decision's ten candidates are book-stamped");
        hits.Should().OnlyContain(p => p.BookEdition == BookEdition.V2);

        hits.Where(p => p.RolloutTrials is not null)
            .Select(p => (p.AnalysisLevel, p.RolloutTrials))
            .Should().BeEquivalentTo(new (AnalysisLevel, int?)[]
            {
                (AnalysisLevel.Ply4, 12960),
                (AnalysisLevel.Ply4, 12960),
                (AnalysisLevel.Ply3, 15552),
            });
        hits.Where(p => p.RolloutTrials is null).Should().HaveCount(2)
            .And.OnlyContain(p => p.AnalysisLevel == AnalysisLevel.Unknown);
    }

    /// <summary>
    /// The same decision without a book: every hit states the bare book facts.
    /// Enrichment is strictly additive — the same candidates, in the same
    /// order, with the same plays and equities either way.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_FixtureA_WithoutBook_OnlyTheFactsDiffer()
    {
        var bare = FixtureAPlay();
        var enriched = FixtureAPlay(new XgIteratorOptions(OpeningBook: LoadBook()));

        bare.Decision.Plays.Where(p => p.AnalysisMode == AnalysisMode.BookRollout)
            .Should().OnlyContain(p => p.AnalysisLevel == AnalysisLevel.Unknown && p.RolloutTrials == null);

        bare.Decision.Plays.Should().HaveSameCount(enriched.Decision.Plays);
        for (int i = 0; i < bare.Decision.Plays.Count; i++)
        {
            bare.Decision.Plays[i].Play.IsSameEncoding(enriched.Decision.Plays[i].Play).Should().BeTrue();
            bare.Decision.Plays[i].Equity.Should().Be(enriched.Decision.Plays[i].Equity);
        }
    }

    /// <summary>
    /// A real XG file analysed at "3-ply Red": its two strongest candidates
    /// state <see cref="AnalysisLevel.Ply3Red"/>, three more level code 11
    /// read as plain 2-ply, and the tail 1-ply — evidence that XG stamps
    /// level 12 for the setting. Local-only by the TestData rule.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void IterateDiagramRequests_ThreePlyRedFixture_StatesPly3Red()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "3-ply Red.xgp");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");

        var plays = XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), Path.GetFileName(path))
            .OfType<CheckerPlayDecision>().Single().Decision.Plays;

        plays.Should().OnlyContain(p => p.AnalysisMode == AnalysisMode.Evaluation);
        plays.GroupBy(p => p.AnalysisLevel).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<AnalysisLevel, int>
            {
                [AnalysisLevel.Ply3Red] = 2,
                [AnalysisLevel.Ply2] = 3,
                [AnalysisLevel.Ply1] = 9,
            });
    }

    /// <summary>
    /// The user's file from halheinrich/backgammon#251, whose rollouts XG
    /// shows as "First 2 moves: 4-ply … Remaining moves: XG Roller": every
    /// rolled-out candidate states a 4-ply rollout of 1296 trials. Local-only
    /// by the TestData rule.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void IterateDiagramRequests_UserFourPlyThenRollerFixture_StatesFourPly()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "Opening 41 44 51 62 MEDIUM.xgp");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");

        var rolledOut = XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), Path.GetFileName(path))
            .OfType<CheckerPlayDecision>()
            .SelectMany(r => r.Decision.Plays)
            .Where(p => p.AnalysisMode == AnalysisMode.Rollout)
            .ToList();

        rolledOut.Should().NotBeEmpty("the fixture's plays were rolled out");
        rolledOut.Should().OnlyContain(p => p.AnalysisLevel == AnalysisLevel.Ply4 && p.RolloutTrials == 1296);
    }
}
