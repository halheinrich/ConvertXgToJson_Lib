using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgDecisionIterator.ResolveDepthInfo"/> — the
/// canonical producer of per-candidate analysis depth
/// (Label / Abbreviation / Rank / Class). Covers every case of the underlying
/// ply-level switch so the abbreviation, rank, and depth-class tables can't
/// silently drift. Rollout branch is covered via a synthesized RolloutContext
/// so it doesn't depend on the binary corpus. A single fixture-pinned
/// regression (<see cref="IterateDiagramRequests_BookOpening_ResolvesToBookRank99"/>)
/// exercises the book tier end-to-end against a real <c>.xg</c>, which is why
/// the class joins the file-IO collection.
/// </summary>
[Collection("FileIO")]
public class DepthResolutionTests
{
    // Empty rollout list shared by every non-rollout-branch test.
    private static readonly List<RolloutContext> NoRollouts = [];

    // -----------------------------------------------------------------------
    //  Non-rollout branch — exhaustive short-level coverage
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData((short)0,    "1-ply",       "1-ply",     1,   AnalysisDepthClass.Ply1)]
    [InlineData((short)1,    "2-ply",       "2-ply",     2,   AnalysisDepthClass.Ply2)]
    [InlineData((short)2,    "3-ply",       "3-ply",     3,   AnalysisDepthClass.Ply3)]
    [InlineData((short)12,   "3-ply red",   "3-ply red", 3,   AnalysisDepthClass.Ply3)]
    [InlineData((short)3,    "4-ply",       "4-ply",     4,   AnalysisDepthClass.Ply4)]
    [InlineData((short)4,    "5-ply",       "5-ply",     5,   AnalysisDepthClass.Ply5)]
    [InlineData((short)5,    "6-ply",       "6-ply",     6,   AnalysisDepthClass.Ply6)]
    [InlineData((short)6,    "7-ply",       "7-ply",     7,   AnalysisDepthClass.Ply7)]
    [InlineData((short)100,  "Rollout",     "Ro",        100, AnalysisDepthClass.Rollout)]
    [InlineData((short)1000, "XG Roller",   "R",         20,  AnalysisDepthClass.XgRoller)]
    [InlineData((short)1001, "XG Roller+",  "R+",        21,  AnalysisDepthClass.XgRollerPlus)]
    [InlineData((short)1002, "XG Roller++", "R++",       22,  AnalysisDepthClass.XgRollerPlusPlus)]
    [InlineData((short)998,  "Book V1",     "Book",      99,  AnalysisDepthClass.Book)]
    [InlineData((short)999,  "Book V2",     "Book",      99,  AnalysisDepthClass.Book)]
    public void ResolveDepthInfo_NonRollout_KnownLevels(
        short level, string expectedLabel, string expectedAbbrev, int expectedRank,
        AnalysisDepthClass expectedClass)
    {
        var (label, abbrev, rank, depthClass) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: level,
            rolloutIndex: -1,
            rollouts: NoRollouts);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(expectedAbbrev);
        rank.Should().Be(expectedRank);
        depthClass.Should().Be(expectedClass);
    }

    /// <summary>
    /// Unknown levels fall through to the synthesized "level-{N}" label
    /// on both Label and Abbreviation; rank defaults to 0 (lowest slot)
    /// and the class to <see cref="AnalysisDepthClass.Unknown"/>. Picked a
    /// value that hasn't been adopted by any XG version we've seen so this
    /// test doesn't quietly break if the switch gains a new case later.
    ///
    /// <para>
    /// The class names the semantic tier a rank only orders: an unrecognised
    /// level is <see cref="AnalysisDepthClass.Unknown"/> (rank 0), a book hit
    /// is <see cref="AnalysisDepthClass.Book"/> (rank 99, the rollout-derived
    /// opening book). The class carries that distinction independently — it no
    /// longer leans on a shared rank-0 slot to separate the two.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_NonRollout_UnknownLevel_FallsThrough()
    {
        var (label, abbrev, rank, depthClass) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 7777,
            rolloutIndex: -1,
            rollouts: NoRollouts);

        label.Should().Be("level-7777");
        abbrev.Should().Be("level-7777");
        rank.Should().Be(0);
        depthClass.Should().Be(AnalysisDepthClass.Unknown);
    }

    /// <summary>
    /// RolloutIndex out of bounds (negative, or past the end of the
    /// rollouts list) falls through to the non-rollout branch. Guards
    /// against a regression where an empty rollout list still triggers
    /// the rollout path.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]  // rollouts is empty so index 0 is out of bounds
    [InlineData(42)]
    public void ResolveDepthInfo_InvalidRolloutIndex_FallsThroughToNonRollout(int idx)
    {
        var (label, abbrev, rank, depthClass) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 2, // 3-ply
            rolloutIndex: idx,
            rollouts: NoRollouts);

        label.Should().Be("3-ply");
        abbrev.Should().Be("3-ply");
        rank.Should().Be(3);
        depthClass.Should().Be(AnalysisDepthClass.Ply3);
    }

    // -----------------------------------------------------------------------
    //  Rollout branch — synthesized RolloutContext
    // -----------------------------------------------------------------------

    /// <summary>
    /// With a valid rollout index and Level2 set, ResolveDepthInfo takes
    /// the rollout branch: inner ply is Level2+1 (because the short
    /// encoding shifts by 1 — Level2=2 → 3-ply), abbreviation is
    /// "{innerPly}p{trials}", rank is 100+innerPly, and the class is
    /// RolloutPly{innerPly}. evalLevel is ignored in this branch.
    /// </summary>
    [Theory]
    [InlineData(2, 1296, "Rollout: 1296 trials. 3-ply", "3p1296", 103, AnalysisDepthClass.RolloutPly3)]
    [InlineData(3,  648, "Rollout: 648 trials. 4-ply",  "4p648",  104, AnalysisDepthClass.RolloutPly4)]
    [InlineData(0,  500, "Rollout: 500 trials. 1-ply",  "1p500",  101, AnalysisDepthClass.RolloutPly1)]
    [InlineData(6,  100, "Rollout: 100 trials. 7-ply",  "7p100",  107, AnalysisDepthClass.RolloutPly7)]
    public void ResolveDepthInfo_Rollout_Level2_PopulatesQuad(
        int level2, int trials, string expectedLabel, string expectedAbbrev, int expectedRank,
        AnalysisDepthClass expectedClass)
    {
        var rollouts = new List<RolloutContext>
        {
            new() { Level2 = level2, GamesRolled = trials },
        };

        // evalLevel here is 7777 (unknown non-rollout) to prove it's ignored.
        var (label, abbrev, rank, depthClass) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 7777,
            rolloutIndex: 0,
            rollouts: rollouts);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(expectedAbbrev);
        rank.Should().Be(expectedRank);
        depthClass.Should().Be(expectedClass);
    }

    /// <summary>
    /// An inner ply outside 1–7 falls back to the
    /// <see cref="AnalysisDepthClass.Rollout"/> floor rather than
    /// producing an out-of-taxonomy class. Defensive: a rolled-out
    /// candidate always carries an in-range inner ply in practice, so
    /// this pins the guard against silently mapping to a bogus enum value.
    /// Rank and abbreviation still reflect the raw inner ply (rank 108,
    /// "8p…") — only the class clamps to the floor.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rollout_InnerPlyOutOfRange_ClampsToRolloutFloor()
    {
        // Level2 = 7 → innerPly = 8, past the RolloutPly7 ceiling.
        var rollouts = new List<RolloutContext>
        {
            new() { Level2 = 7, GamesRolled = 200 },
        };

        var (_, abbrev, rank, depthClass) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 0,
            rolloutIndex: 0,
            rollouts: rollouts);

        abbrev.Should().Be("8p200");
        rank.Should().Be(108);
        depthClass.Should().Be(AnalysisDepthClass.Rollout,
            "innerPly 8 is outside the RolloutPly1–7 range and clamps to the floor");
    }

    /// <summary>
    /// ResolveDepthInfo prefers Level2, then Level1, then LevelTrunc
    /// when computing the inner ply level. This test pins the fallback
    /// order so a refactor of the selection logic can't silently change
    /// which field wins — asserting on both the rank and the class.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rollout_LevelFallback_PrefersLevel2ThenLevel1ThenTrunc()
    {
        // Level2 dominates.
        var r1 = new List<RolloutContext>
        {
            new() { Level2 = 3, Level1 = 2, LevelTrunc = 1, GamesRolled = 100 },
        };
        var c1 = XgDecisionIterator.ResolveDepthInfo(0, 0, r1);
        c1.Rank.Should().Be(104, "Level2=3 → innerPly=4 → rank 104");
        c1.Class.Should().Be(AnalysisDepthClass.RolloutPly4);

        // Level2 absent → Level1 wins.
        var r2 = new List<RolloutContext>
        {
            new() { Level2 = 0, Level1 = 2, LevelTrunc = 1, GamesRolled = 100 },
        };
        var c2 = XgDecisionIterator.ResolveDepthInfo(0, 0, r2);
        c2.Rank.Should().Be(103, "Level1=2 → innerPly=3 → rank 103");
        c2.Class.Should().Be(AnalysisDepthClass.RolloutPly3);

        // Both absent → LevelTrunc wins.
        var r3 = new List<RolloutContext>
        {
            new() { Level2 = 0, Level1 = 0, LevelTrunc = 1, GamesRolled = 100 },
        };
        var c3 = XgDecisionIterator.ResolveDepthInfo(0, 0, r3);
        c3.Rank.Should().Be(102, "LevelTrunc=1 → innerPly=2 → rank 102");
        c3.Class.Should().Be(AnalysisDepthClass.RolloutPly2);
    }

    /// <summary>
    /// Two candidates pointing into the same rollouts list each resolve
    /// independently to their own rollout's inner ply / trial count.
    /// Pins the per-candidate scalar contract — a regression to "first
    /// valid hit wins across all candidates" would surface here.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_PerCandidate_IndependentResolution()
    {
        var rollouts = new List<RolloutContext>
        {
            new() { Level2 = 2, GamesRolled = 1296 }, // idx 0
            new() { Level2 = 3, GamesRolled = 5000 }, // idx 1
        };

        var c0 = XgDecisionIterator.ResolveDepthInfo(0, 0, rollouts);
        c0.Abbreviation.Should().Be("3p1296");
        c0.Rank.Should().Be(103);
        c0.Class.Should().Be(AnalysisDepthClass.RolloutPly3);

        var c1 = XgDecisionIterator.ResolveDepthInfo(0, 1, rollouts);
        c1.Abbreviation.Should().Be("4p5000");
        c1.Rank.Should().Be(104);
        c1.Class.Should().Be(AnalysisDepthClass.RolloutPly4);
    }

    // -----------------------------------------------------------------------
    //  Book tier — fixture-pinned end-to-end regression
    // -----------------------------------------------------------------------

    /// <summary>
    /// XG stamps opening-book hits as bare level 998/999 (Book V1/V2) with no
    /// rollout context. The book is rollout-derived, so a hit ranks 99 — above
    /// XG Roller++ (rank 22) and below the explicit-rollout floor (rank 100):
    /// a cached rollout whose parameters the file no longer records ranks under
    /// a rollout the file actually carries. In <c>ajhhBG0024.xg</c>, game 6's
    /// opening play (the 52 roll, <c>MoveNumber</c> 1) is such a book hit; it
    /// must resolve to label "Book V1", class <see cref="AnalysisDepthClass.Book"/>,
    /// rank 99 all the way through the diagram surface. Pins the rank promotion
    /// (0 → 99) end-to-end so rollout-depth filtering stops dropping booked
    /// openings, and guards the <see cref="AnalysisDepthClass.Book"/> class the
    /// <c>IDecisionFilterData</c> member exposes for filtering.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_BookOpening_ResolvesToBookRank99()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "ajhhBG0024.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on ajhhBG0024.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        // Game = 6th GameHeaderRecord; move = its first play (MoveNumber 1).
        var req = XgDecisionIterator.IterateDiagramRequests(file, sourceFile)
            .Single(r => !r.Decision.IsCube
                      && r.Descriptive.Game == 6
                      && r.Descriptive.MoveNumber == 1);

        // Plays[0] is the best-by-equity candidate after the sort; the
        // decision's IDecisionFilterData class derives from it (BestPlayIndex 0).
        var best = req.Decision.Plays[0];
        best.Depth.Should().Be("Book V1");
        best.DepthClass.Should().Be(AnalysisDepthClass.Book);
        best.DepthRank.Should().Be(99);

        req.AnalysisDepthClass.Should().Be(AnalysisDepthClass.Book,
            "the decision's filter-facing class must report the book tier");
    }
}
