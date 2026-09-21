using AwesomeAssertions.Execution;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgDecisionIterator.ResolveDepthInfo"/> — the
/// canonical producer of per-candidate analysis depth (Label / Abbreviation /
/// Rank / the <see cref="AnalysisMode"/> × <see cref="AnalysisLevel"/> pair).
/// Covers every case of the underlying ply-level switch so the abbreviation,
/// rank, and taxonomy tables can't silently drift. The rollout branch is
/// covered via a synthesized RolloutContext, the book branch via synthesized
/// <see cref="OpeningBookEntry"/> instances, so neither depends on the binary
/// corpus. Fixture-pinned regressions (the ajhhBG0024 book opening and the
/// ajhhBG0407 book-enrichment set) exercise the book tier end-to-end against
/// real <c>.xg</c> files and the real book database, which is why the class
/// joins the file-IO collection.
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
    [InlineData((short)0,    "1-ply",       "1-ply",     10,  AnalysisMode.Evaluation,  AnalysisLevel.Ply1)]
    [InlineData((short)1,    "2-ply",       "2-ply",     20,  AnalysisMode.Evaluation,  AnalysisLevel.Ply2)]
    [InlineData((short)11,   "2-ply",       "2-ply",     20,  AnalysisMode.Evaluation,  AnalysisLevel.Ply2)]
    [InlineData((short)12,   "3-ply Red",   "3-ply Red", 25,  AnalysisMode.Evaluation,  AnalysisLevel.Ply3Red)]
    [InlineData((short)2,    "3-ply",       "3-ply",     30,  AnalysisMode.Evaluation,  AnalysisLevel.Ply3)]
    [InlineData((short)1000, "XG Roller",   "R",         35,  AnalysisMode.Evaluation,  AnalysisLevel.XgRoller)]
    [InlineData((short)3,    "4-ply",       "4-ply",     40,  AnalysisMode.Evaluation,  AnalysisLevel.Ply4)]
    [InlineData((short)1001, "XG Roller+",  "R+",        45,  AnalysisMode.Evaluation,  AnalysisLevel.XgRollerPlus)]
    [InlineData((short)4,    "5-ply",       "5-ply",     50,  AnalysisMode.Evaluation,  AnalysisLevel.Ply5)]
    [InlineData((short)5,    "6-ply",       "6-ply",     60,  AnalysisMode.Evaluation,  AnalysisLevel.Ply6)]
    [InlineData((short)6,    "7-ply",       "7-ply",     70,  AnalysisMode.Evaluation,  AnalysisLevel.Ply7)]
    [InlineData((short)1002, "XG Roller++", "R++",       75,  AnalysisMode.Evaluation,  AnalysisLevel.XgRollerPlusPlus)]
    [InlineData((short)998,  "Book V2",     "Book",      99,  AnalysisMode.BookRollout, AnalysisLevel.Unknown)]
    [InlineData((short)999,  "Book V1",     "Book",      99,  AnalysisMode.BookRollout, AnalysisLevel.Unknown)]
    [InlineData((short)100,  "Rollout",     "Ro",        100, AnalysisMode.Rollout,     AnalysisLevel.Unknown)]
    public void ResolveDepthInfo_NonRollout_KnownLevels(
        short level, string expectedLabel, string expectedAbbrev, int expectedRank,
        AnalysisMode expectedMode, AnalysisLevel expectedLevel)
    {
        var (label, abbrev, rank, mode, analysisLevel) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: level,
            rolloutIndex: -1,
            rollouts: NoRollouts);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(expectedAbbrev);
        rank.Should().Be(expectedRank);
        mode.Should().Be(expectedMode);
        analysisLevel.Should().Be(expectedLevel);
    }

    /// <summary>
    /// Unknown levels fall through to the synthesized "level-{N}" label
    /// on both Label and Abbreviation; rank defaults to 0 (lowest slot)
    /// and the pair to <see cref="AnalysisMode.Unknown"/> +
    /// <see cref="AnalysisLevel.Unknown"/>. Picked a value that hasn't been
    /// adopted by any XG version we've seen so this test doesn't quietly
    /// break if the switch gains a new case later.
    ///
    /// <para>
    /// The mode names the semantic tier a rank only orders: an unrecognised
    /// level is <see cref="AnalysisMode.Unknown"/> (rank 0), a book hit is
    /// <see cref="AnalysisMode.BookRollout"/> (rank 99, the rollout-derived
    /// opening book). The pair carries that distinction independently of the
    /// rank values.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_NonRollout_UnknownLevel_FallsThrough()
    {
        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 7777,
            rolloutIndex: -1,
            rollouts: NoRollouts);

        label.Should().Be("level-7777");
        abbrev.Should().Be("level-7777");
        rank.Should().Be(0);
        mode.Should().Be(AnalysisMode.Unknown);
        level.Should().Be(AnalysisLevel.Unknown);
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
        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 2, // 3-ply
            rolloutIndex: idx,
            rollouts: NoRollouts);

        label.Should().Be("3-ply");
        abbrev.Should().Be("3-ply");
        rank.Should().Be(30);
        mode.Should().Be(AnalysisMode.Evaluation);
        level.Should().Be(AnalysisLevel.Ply3);
    }

    // -----------------------------------------------------------------------
    //  Rank ordering — the ruled interleaved sequence
    // -----------------------------------------------------------------------

    /// <summary>
    /// XG's analysis levels in the contractual order of its own menu, lowest
    /// rigor first, given as the XG level codes the producer decodes. This is
    /// the user's ruling of 2026-08-28, mirroring
    /// <see cref="AnalysisLevel"/>'s contractual declaration order: the ply
    /// family and the XG Roller family <i>interleave</i> rather than forming
    /// two blocks, and "3-ply Red" sits below a full 3-ply.
    /// </summary>
    private static readonly (short Code, AnalysisLevel Level)[] RuledRigorOrder =
    [
        (0,    AnalysisLevel.Ply1),
        (1,    AnalysisLevel.Ply2),
        (12,   AnalysisLevel.Ply3Red),
        (2,    AnalysisLevel.Ply3),
        (1000, AnalysisLevel.XgRoller),
        (3,    AnalysisLevel.Ply4),
        (1001, AnalysisLevel.XgRollerPlus),
        (4,    AnalysisLevel.Ply5),
        (5,    AnalysisLevel.Ply6),
        (6,    AnalysisLevel.Ply7),
        (1002, AnalysisLevel.XgRollerPlusPlus),
    ];

    private static int RankOf(short code) =>
        XgDecisionIterator.ResolveDepthInfo(code, rolloutIndex: -1, rollouts: NoRollouts).Rank;

    /// <summary>
    /// The rank scale increases strictly along the whole ruled sequence — the
    /// <i>relationship</i> pin that the individual row values above cannot
    /// give. A future rank edit that leaves every value individually
    /// plausible but breaks the interleave (re-blocking the Roller family
    /// above the plies, or letting "3-ply Red" tie with a full 3-ply as it
    /// did before 2026-08-28) fails here, naming the adjacent pair that
    /// regressed. The cube side reads the same table, so this pins cube depth
    /// ordering too.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rank_StrictlyIncreasesAlongRuledRigorOrder()
    {
        for (int i = 1; i < RuledRigorOrder.Length; i++)
        {
            var lower = RuledRigorOrder[i - 1];
            var higher = RuledRigorOrder[i];

            RankOf(higher.Code).Should().BeGreaterThan(RankOf(lower.Code),
                $"{higher.Level} outranks {lower.Level} in XG's own menu order");
        }
    }

    /// <summary>
    /// Every level in the ruled sequence resolves to its own
    /// <see cref="AnalysisLevel"/> member, and the sequence covers every
    /// member of the enum except <see cref="AnalysisLevel.Unknown"/> — which
    /// sits outside the rigor scale ("level not recorded"), not at the bottom
    /// of it. The coverage half is what makes a future enum member fail here
    /// rather than slip in unranked: adding one without giving it a rigor
    /// position breaks this test.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_RuledRigorOrder_CoversEveryRankedAnalysisLevel()
    {
        foreach (var (code, expected) in RuledRigorOrder)
        {
            XgDecisionIterator
                .ResolveDepthInfo(code, rolloutIndex: -1, rollouts: NoRollouts)
                .Level.Should().Be(expected, $"XG level code {code} is {expected}");
        }

        RuledRigorOrder.Select(e => e.Level).Should().BeEquivalentTo(
            Enum.GetValues<AnalysisLevel>().Where(l => l != AnalysisLevel.Unknown),
            "every ranked AnalysisLevel member must hold a position in the ruled order");
    }

    /// <summary>
    /// XG level code 11 is plain 2-ply: it resolves to code 1's <i>exact</i>
    /// tuple — label, abbreviation, rank and pair alike — because XG's own
    /// display, the designated authority, draws no distinction between them
    /// (user-ruled 2026-08-28, halheinrich/backgammon#160). Pinning whole-tuple
    /// equality rather than the individual values is the point: the two codes
    /// share one switch arm, and this fails if they are ever split into arms
    /// that could drift.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Level11_ResolvesIdenticallyToLevel1()
    {
        var eleven = XgDecisionIterator.ResolveDepthInfo(11, rolloutIndex: -1, rollouts: NoRollouts);
        var one = XgDecisionIterator.ResolveDepthInfo(1, rolloutIndex: -1, rollouts: NoRollouts);

        eleven.Should().Be(one, "XG displays level code 11 as plain 2-ply");
    }

    /// <summary>
    /// The floors and ceilings bracketing the evaluation scale: the opening
    /// book (99) sits above every evaluation and below the explicit-rollout
    /// floor, and an unrecognised level (0) sits below everything meaningful.
    /// Book hits are rollout-derived, so under depth-first ordering they sort
    /// below an explicit rollout the file actually carries and above every
    /// evaluation — the observed consequence the stance was re-affirmed on
    /// (2026-08-28).
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rank_BookSitsAboveEveryEvaluationAndBelowRollout()
    {
        int deepestEvaluation = RuledRigorOrder.Max(e => RankOf(e.Code));
        int shallowestEvaluation = RuledRigorOrder.Min(e => RankOf(e.Code));

        RankOf(998).Should().Be(RankOf(999), "both book versions rank alike");
        RankOf(998).Should().BeGreaterThan(deepestEvaluation,
            "XG's opening book is rollout-derived, so it outranks every evaluation");
        RankOf(998).Should().BeLessThan(RankOf(100),
            "a cached rollout the file no longer describes ranks under one it carries");
        RankOf(7777).Should().BeLessThan(shallowestEvaluation,
            "the fallback rank is the floor, below everything meaningful");
    }

    // -----------------------------------------------------------------------
    //  Abbreviation grammar — the one pin
    // -----------------------------------------------------------------------

    /// <summary>
    /// The depth-abbreviation grammar's one pin. <see cref="DepthAbbreviationFormat"/>
    /// owns the spelling of both trial-bearing forms, and this is the only
    /// test that writes them out: every other abbreviation assertion in the
    /// suite composes its expectation through the owner. The division is the
    /// point — the rollout and book rows pin what
    /// <see cref="XgDecisionIterator.ResolveDepthInfo"/> feeds the grammar
    /// (inner-level token, trial count), this pins how the grammar
    /// writes it, so a grammar change edits the owner and this test alone.
    /// The two forms carry different separators — only the book form takes
    /// the underscore, the user's ruling of 2026-09-16
    /// (halheinrich/backgammon#240) — so the rows below pin four spellings,
    /// not one pattern applied four times. Each form covers both token kinds
    /// <c>InnerLevelToken</c> yields: a ply digit and a Roller abbreviation.
    /// </summary>
    [Fact]
    public void DepthAbbreviationFormat_SpellsBothTrialBearingForms()
    {
        using var scope = new AssertionScope();

        DepthAbbreviationFormat.Rollout(levelToken: "3", trials: 1296).Should().Be("3p1296");
        DepthAbbreviationFormat.Rollout(levelToken: "R", trials: 1296).Should().Be("Rp1296");
        DepthAbbreviationFormat.Book(levelToken: "4", trials: 12960).Should().Be("B4_12960");
        DepthAbbreviationFormat.Book(levelToken: "R", trials: 20736).Should().Be("BR_20736");
    }

    // -----------------------------------------------------------------------
    //  Rollout branch — synthesized RolloutContext
    // -----------------------------------------------------------------------

    /// <summary>
    /// With a valid rollout index, ResolveDepthInfo takes the rollout branch
    /// and names the rollout by its inner evaluation level, decoded through
    /// the same level table as an evaluation (a ply code stores ply − 1:
    /// 2 → 3-ply). The label carries the inner level's label, the
    /// abbreviation is the grammar's rollout form over the inner level's
    /// token and the trial count (composed through
    /// <see cref="DepthAbbreviationFormat.Rollout"/>; the spelling itself is
    /// pinned once, in
    /// <see cref="DepthAbbreviationFormat_SpellsBothTrialBearingForms"/>),
    /// the rank is 100 plus the inner level's evaluation rank, and the pair
    /// is <see cref="AnalysisMode.Rollout"/> + the inner level's member.
    /// Both phases carry the same level (a single-level rollout); the phase
    /// precedence is pinned separately. evalLevel is ignored in this branch.
    /// </summary>
    [Theory]
    [InlineData(2, 1296, "Rollout: 1296 trials. 3-ply", "3", 130, AnalysisLevel.Ply3)]
    [InlineData(3,  648, "Rollout: 648 trials. 4-ply",  "4", 140, AnalysisLevel.Ply4)]
    [InlineData(0,  500, "Rollout: 500 trials. 1-ply",  "1", 110, AnalysisLevel.Ply1)]
    [InlineData(6,  100, "Rollout: 100 trials. 7-ply",  "7", 170, AnalysisLevel.Ply7)]
    public void ResolveDepthInfo_Rollout_PlyInnerLevel_PopulatesQuintuple(
        int innerLevel, int trials, string expectedLabel, string expectedToken, int expectedRank,
        AnalysisLevel expectedLevel)
    {
        var rollouts = new List<RolloutContext>
        {
            new() { Level1 = innerLevel, Level2 = innerLevel, GamesRolled = trials },
        };

        // evalLevel here is 7777 (unknown non-rollout) to prove it's ignored.
        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 7777,
            rolloutIndex: 0,
            rollouts: rollouts);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(DepthAbbreviationFormat.Rollout(expectedToken, trials));
        rank.Should().Be(expectedRank);
        mode.Should().Be(AnalysisMode.Rollout);
        level.Should().Be(expectedLevel);
    }

    /// <summary>
    /// A rollout whose inner level is an XG Roller family code (1000 / 1001 /
    /// 1002) is named as that Roller level in every form: the Roller token
    /// ("R" / "R+" / "R++"), never the number that ply arithmetic over the
    /// code produced (it read <c>1001p1296</c>, halheinrich/backgammon#251);
    /// the Roller member, not <see cref="AnalysisLevel.Unknown"/>; and a rank
    /// that rides the interleaved grid (135 / 145 / 175), not 1101.
    /// </summary>
    [Theory]
    [InlineData(1000, "Rollout: 1296 trials. XG Roller",   "R",   135, AnalysisLevel.XgRoller)]
    [InlineData(1001, "Rollout: 1296 trials. XG Roller+",  "R+",  145, AnalysisLevel.XgRollerPlus)]
    [InlineData(1002, "Rollout: 1296 trials. XG Roller++", "R++", 175, AnalysisLevel.XgRollerPlusPlus)]
    public void ResolveDepthInfo_Rollout_RollerInnerLevel_NamedAsRollerNeverANumber(
        int rollerCode, string expectedLabel, string expectedToken, int expectedRank,
        AnalysisLevel expectedLevel)
    {
        var rollouts = new List<RolloutContext>
        {
            new() { Level1 = rollerCode, Level2 = rollerCode, GamesRolled = 1296 },
        };

        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 0,
            rolloutIndex: 0,
            rollouts: rollouts);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(DepthAbbreviationFormat.Rollout(expectedToken, 1296));
        rank.Should().Be(expectedRank);
        mode.Should().Be(AnalysisMode.Rollout);
        level.Should().Be(expectedLevel);
    }

    /// <summary>
    /// The user's shape (halheinrich/backgammon#251): XG's "First 2 moves:
    /// 4-ply … Remaining moves: XG Roller" — a first phase of 2 moves
    /// (<c>LevelCut</c> 2) at 4-ply (code 3), then XG Roller (code 1000).
    /// The first phase names the rollout, the user's ruling: it is a 4-ply
    /// rollout in label, abbreviation, rank, mode and level. The real file is
    /// pinned end-to-end in
    /// <see cref="IterateDiagramRequests_UserFourPlyThenRollerFixture_ReadsFourPly"/>.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rollout_FourPlyThenRoller_ClassifiedAsFourPly()
    {
        var rollouts = new List<RolloutContext>
        {
            new() { LevelCut = 2, Level1 = 3, Level2 = 1000, GamesRolled = 1296 },
        };

        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 100,
            rolloutIndex: 0,
            rollouts: rollouts);

        using var scope = new AssertionScope();
        label.Should().Be("Rollout: 1296 trials. 4-ply");
        abbrev.Should().Be(DepthAbbreviationFormat.Rollout("4", 1296));
        rank.Should().Be(140);
        mode.Should().Be(AnalysisMode.Rollout);
        level.Should().Be(AnalysisLevel.Ply4);
    }

    /// <summary>
    /// The mirror of the user's shape: a 2-move first phase at XG Roller,
    /// then 4-ply. The first phase still names the rollout — an XG Roller
    /// rollout in every form — so the precedence is by phase, not by
    /// whichever level is deeper.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rollout_RollerThenFourPly_ClassifiedAsRoller()
    {
        var rollouts = new List<RolloutContext>
        {
            new() { LevelCut = 2, Level1 = 1000, Level2 = 3, GamesRolled = 1296 },
        };

        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 100,
            rolloutIndex: 0,
            rollouts: rollouts);

        using var scope = new AssertionScope();
        label.Should().Be("Rollout: 1296 trials. XG Roller");
        abbrev.Should().Be(DepthAbbreviationFormat.Rollout("R", 1296));
        rank.Should().Be(135);
        mode.Should().Be(AnalysisMode.Rollout);
        level.Should().Be(AnalysisLevel.XgRoller);
    }

    /// <summary>
    /// The two trial-bearing forms spell the same inner level with the same
    /// token: a rollout played at level <c>code</c> and a book
    /// entry whose moves level is <c>code</c> compose their abbreviations
    /// over one token — one rule for naming an inner level, one owner
    /// (<c>InnerLevelToken</c>). The forms may differ only in prefix and
    /// separator, which <see cref="DepthAbbreviationFormat"/> owns.
    /// </summary>
    [Theory]
    [InlineData(0,    "1")]
    [InlineData(1,    "2")]
    [InlineData(12,   "3")]
    [InlineData(2,    "3")]
    [InlineData(1000, "R")]
    [InlineData(3,    "4")]
    [InlineData(1001, "R+")]
    [InlineData(6,    "7")]
    [InlineData(1002, "R++")]
    public void ResolveDepthInfo_RolloutAndBookForms_SpellTheSameInnerLevelWithTheSameToken(
        int code, string expectedToken)
    {
        const int trials = 1296;
        var rollouts = new List<RolloutContext>
        {
            new() { Level1 = code, Level2 = code, GamesRolled = trials },
        };
        var entry = new OpeningBookEntry { Level = 100, Trials = trials, RolloutMovesLevel = code };

        var rollout = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 100, rolloutIndex: 0, rollouts: rollouts);
        var book = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 998, rolloutIndex: -1, rollouts: NoRollouts, bookEntry: entry);

        using var scope = new AssertionScope();
        rollout.Abbreviation.Should().Be(DepthAbbreviationFormat.Rollout(expectedToken, trials));
        book.Abbreviation.Should().Be(DepthAbbreviationFormat.Book(expectedToken, trials));
        rollout.Level.Should().Be(book.Level, "both forms decode the inner level through one table");
    }

    /// <summary>
    /// Rollout ranks ride the evaluation grid: a rollout whose inner level is
    /// deeper in XG's ruled rigor order outranks one whose inner level is
    /// shallower, interleave included (an XG Roller rollout sits between the
    /// 3-ply and 4-ply rollouts), and the shallowest rollout still outranks
    /// Book (99).
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_RolloutRank_FollowsRuledRigorOrderAboveBook()
    {
        static int RolloutRankOf(short code) => XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 100,
            rolloutIndex: 0,
            rollouts: [new() { Level1 = code, Level2 = code, GamesRolled = 100 }]).Rank;

        for (int i = 1; i < RuledRigorOrder.Length; i++)
        {
            var lower = RuledRigorOrder[i - 1];
            var higher = RuledRigorOrder[i];

            RolloutRankOf(higher.Code).Should().BeGreaterThan(RolloutRankOf(lower.Code),
                $"a {higher.Level} rollout outranks a {lower.Level} rollout");
        }

        RolloutRankOf(RuledRigorOrder[0].Code).Should().BeGreaterThan(RankOf(998),
            "every rollout outranks a book hit");
    }

    /// <summary>
    /// An unrecognised inner level code degrades exactly as it does anywhere
    /// else in the level table — <see cref="AnalysisLevel.Unknown"/>, inner
    /// rank 0 and the raw <c>level-{code}</c> spelling — rather than
    /// producing an out-of-taxonomy value. Lifted into the rollout branch
    /// that is rank 100, the rollout floor (the rank of a rollout sentinel
    /// with no context), and the mode stays
    /// <see cref="AnalysisMode.Rollout"/> — only the level degrades.
    /// Defensive: a rolled-out candidate always carries a recognised inner
    /// level in practice.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rollout_UnrecognisedInnerLevel_DegradesLevelToUnknown()
    {
        // Code 7 is not in the level table (7-ply is code 6).
        var rollouts = new List<RolloutContext>
        {
            new() { Level1 = 7, Level2 = 7, GamesRolled = 200 },
        };

        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 0,
            rolloutIndex: 0,
            rollouts: rollouts);

        label.Should().Be("Rollout: 200 trials. level-7");
        abbrev.Should().Be(DepthAbbreviationFormat.Rollout("level-7", 200));
        rank.Should().Be(100);
        mode.Should().Be(AnalysisMode.Rollout);
        level.Should().Be(AnalysisLevel.Unknown,
            "code 7 is outside the level table and degrades to Unknown");
    }

    /// <summary>
    /// Which leg phase names the rollout is decided by whether a first phase
    /// exists — <c>LevelCut</c>, the number of moves it covers, above 0 —
    /// never by testing a level for "set" (halheinrich/backgammon#251). Each
    /// case gives the losing field a different level, so the field that
    /// wins is the one the assertion names — asserting on the rank and the
    /// pair.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rollout_PhaseSelection_FollowsLevelCutNotLevelValues()
    {
        // No first phase — the fixtures' shape (Level1 0 beside a 3-ply Level2,
        // LevelCut 0): the second phase plays throughout and names the rollout.
        var noFirst = XgDecisionIterator.ResolveDepthInfo(0, 0,
            [new() { LevelCut = 0, Level1 = 0, Level2 = 2, GamesRolled = 100 }]);
        noFirst.Rank.Should().Be(130, "no first phase → Level2=2 → 3-ply (30) → rank 130");
        noFirst.Mode.Should().Be(AnalysisMode.Rollout);
        noFirst.Level.Should().Be(AnalysisLevel.Ply3);

        // No first phase, even with a deeper Level1 stored: a first-phase
        // setting that covers no moves does not name the rollout.
        var unusedFirst = XgDecisionIterator.ResolveDepthInfo(0, 0,
            [new() { LevelCut = 0, Level1 = 3, Level2 = 2, GamesRolled = 100 }]);
        unusedFirst.Level.Should().Be(AnalysisLevel.Ply3, "LevelCut 0 → Level2 names the rollout");

        // A genuine 1-ply first phase (Level1 0 is 1-ply, not "unset") names
        // the rollout 1-ply.
        var onePlyFirst = XgDecisionIterator.ResolveDepthInfo(0, 0,
            [new() { LevelCut = 2, Level1 = 0, Level2 = 2, GamesRolled = 100 }]);
        onePlyFirst.Rank.Should().Be(110, "first phase exists → Level1=0 → 1-ply (10) → rank 110");
        onePlyFirst.Mode.Should().Be(AnalysisMode.Rollout);
        onePlyFirst.Level.Should().Be(AnalysisLevel.Ply1);

        // LevelTrunc is not a depth input: a 1-ply second phase played
        // throughout is 1-ply whatever the truncation level holds.
        var truncIgnored = XgDecisionIterator.ResolveDepthInfo(0, 0,
            [new() { LevelCut = 0, Level1 = 0, Level2 = 0, LevelTrunc = 3, GamesRolled = 100 }]);
        truncIgnored.Level.Should().Be(AnalysisLevel.Ply1, "LevelTrunc never names the rollout");
    }

    /// <summary>
    /// Two candidates pointing into the same rollouts list each resolve
    /// independently to their own rollout's inner level / trial count.
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
        c0.Abbreviation.Should().Be(DepthAbbreviationFormat.Rollout("3", 1296));
        c0.Rank.Should().Be(130);
        c0.Level.Should().Be(AnalysisLevel.Ply3);

        var c1 = XgDecisionIterator.ResolveDepthInfo(0, 1, rollouts);
        c1.Abbreviation.Should().Be(DepthAbbreviationFormat.Rollout("4", 5000));
        c1.Rank.Should().Be(140);
        c1.Level.Should().Be(AnalysisLevel.Ply4);
    }

    // -----------------------------------------------------------------------
    //  Book branch — synthesized OpeningBookEntry
    // -----------------------------------------------------------------------

    /// <summary>
    /// A rollout book entry enriches a V2 book stamp, following the rollout
    /// sibling forms: label "Book V2: {trials} trials. {moves-level label}",
    /// and an abbreviation in the grammar's book form over the moves-level
    /// token and the trial count — composed through
    /// <see cref="DepthAbbreviationFormat.Book"/>, so each row pins the token
    /// its moves level yields, while the spelling is pinned once, in
    /// <see cref="DepthAbbreviationFormat_SpellsBothTrialBearingForms"/>. The
    /// pair is <see cref="AnalysisMode.BookRollout"/> plus the entry's moves
    /// level mapped through the level taxonomy; and the rank deliberately
    /// stays at the unenriched book rank 99 — enrichment recovers the cached
    /// rollout's parameters, but <c>DepthRank</c> semantics hold stable.
    /// </summary>
    [Theory]
    [InlineData(3,    12960, "Book V2: 12960 trials. 4-ply",     "4", AnalysisLevel.Ply4)]
    [InlineData(2,    20736, "Book V2: 20736 trials. 3-ply",     "3", AnalysisLevel.Ply3)]
    [InlineData(12,     648, "Book V2: 648 trials. 3-ply Red",   "3", AnalysisLevel.Ply3Red)]
    [InlineData(1000, 20736, "Book V2: 20736 trials. XG Roller", "R", AnalysisLevel.XgRoller)]
    public void ResolveDepthInfo_BookEntry_Rollout_EnrichesLabelAbbreviationAndLevel(
        int movesLevel, int trials, string expectedLabel, string expectedToken,
        AnalysisLevel expectedLevel)
    {
        var entry = new OpeningBookEntry
        {
            Level = 100, // rollout entry
            Trials = trials,
            RolloutMovesLevel = movesLevel,
        };

        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 998,
            rolloutIndex: -1,
            rollouts: NoRollouts,
            bookEntry: entry);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(DepthAbbreviationFormat.Book(expectedToken, trials));
        rank.Should().Be(99, "enrichment must not move the book rank");
        mode.Should().Be(AnalysisMode.BookRollout);
        level.Should().Be(expectedLevel);
    }

    /// <summary>
    /// A non-rollout book entry (the book also stores Roller++ evaluation
    /// baselines, whose rollout-parameter fields are zeroed) must not
    /// enrich: the projection falls through to the plain unenriched
    /// "Book V2" forms.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_BookEntry_EvaluationEntry_FallsThroughUnenriched()
    {
        var entry = new OpeningBookEntry
        {
            Level = 1002, // Roller++ evaluation baseline
            Trials = 0,
            RolloutMovesLevel = 0, // zeroed — would decode as a bogus "1-ply"
        };

        var (label, abbrev, rank, mode, level) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 998,
            rolloutIndex: -1,
            rollouts: NoRollouts,
            bookEntry: entry);

        label.Should().Be("Book V2");
        abbrev.Should().Be("Book");
        rank.Should().Be(99);
        mode.Should().Be(AnalysisMode.BookRollout);
        level.Should().Be(AnalysisLevel.Unknown);
    }

    /// <summary>
    /// A valid rollout index outranks a book entry — the two cannot co-occur
    /// on real data (a book stamp carries no rollout index), but the branch
    /// order is a contract worth pinning: an explicit rollout the file
    /// carries beats a cached book rollout.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_RolloutIndexAndBookEntry_RolloutWins()
    {
        var rollouts = new List<RolloutContext>
        {
            new() { Level2 = 2, GamesRolled = 1296 },
        };
        var entry = new OpeningBookEntry { Level = 100, Trials = 12960, RolloutMovesLevel = 3 };

        var (label, _, rank, mode, _) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 998,
            rolloutIndex: 0,
            rollouts: rollouts,
            bookEntry: entry);

        label.Should().Be("Rollout: 1296 trials. 3-ply");
        rank.Should().Be(130);
        mode.Should().Be(AnalysisMode.Rollout);
    }

    // -----------------------------------------------------------------------
    //  Book tier — fixture-pinned end-to-end regression (no book database)
    // -----------------------------------------------------------------------

    /// <summary>
    /// XG stamps opening-book hits as bare level 999/998 (Book V1/V2) with no
    /// rollout context. The book is rollout-derived, so a hit ranks 99 — above
    /// XG Roller++ (rank 75) and below the explicit-rollout floor (rank 100):
    /// a cached rollout whose parameters the file no longer records ranks under
    /// a rollout the file actually carries. In <c>ajhhBG0024.xg</c>, game 6's
    /// opening play (the 52 roll, <c>MoveNumber</c> 1) is such a book hit
    /// (level 998); with no book database supplied it must resolve to label
    /// "Book V2", <see cref="AnalysisMode.BookRollout"/> +
    /// <see cref="AnalysisLevel.Unknown"/> (the graceful-degradation stamp),
    /// rank 99 all the way through the diagram surface. Pins the rank
    /// promotion (0 → 99) end-to-end so rollout-depth filtering stops
    /// dropping booked openings, and guards the pair the
    /// <c>IDecisionFilterData</c> members expose for filtering.
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
        // decision's IDecisionFilterData pair derives from it (BestPlayIndex 0).
        var best = req.Decision.Plays[0];
        best.Depth.Should().Be("Book V2");
        best.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        best.AnalysisLevel.Should().Be(AnalysisLevel.Unknown,
            "no book database was supplied, so the level cannot be recovered");
        best.DepthRank.Should().Be(99);

        req.AnalysisMode.Should().Be(AnalysisMode.BookRollout,
            "the decision's filter-facing mode must report the book tier");
        req.AnalysisLevel.Should().Be(AnalysisLevel.Unknown);
    }

    // -----------------------------------------------------------------------
    //  Book enrichment — fixture (a) against the real book database
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

    private static XgFile LoadFixtureA(out string sourceFile)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "ajhhBG0407.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}.");
        sourceFile = Path.GetFileName(path);
        return XgFileReader.ReadFile(path);
    }

    /// <summary>
    /// Fixture (a) (<c>ajhhBG0407.xg</c> game 9 move 1, roll 41) end-to-end
    /// with the real book database supplied via
    /// <see cref="XgIteratorOptions.OpeningBook"/>: the best candidate
    /// (13/9 6/5, a V2 book stamp) enriches from the entry XG's tooltip
    /// shows — Neil Kazaross's 12,960-game 4-ply/4-ply rollout — so the
    /// diagram surface stamps <see cref="AnalysisMode.BookRollout"/> +
    /// <see cref="AnalysisLevel.Ply4"/> with the trials-bearing label and
    /// abbreviation, while the rank stays 99.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_FixtureA_WithBook_EnrichesBestCandidate()
    {
        var file = LoadFixtureA(out string sourceFile);
        var options = new XgIteratorOptions(OpeningBook: LoadBook());

        var req = XgDecisionIterator.IterateDiagramRequests(file, sourceFile, options: options)
            .Single(r => !r.Decision.IsCube
                      && r.Descriptive.Game == 9
                      && r.Descriptive.MoveNumber == 1);

        var best = req.Decision.Plays[0];
        best.Depth.Should().Be("Book V2: 12960 trials. 4-ply");
        best.DepthAbbreviation.Should().Be(DepthAbbreviationFormat.Book("4", 12960));
        best.DepthRank.Should().Be(99, "enrichment must not move the book rank");
        best.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        best.AnalysisLevel.Should().Be(AnalysisLevel.Ply4, "the entry's rollout used 4-ply checker play");

        req.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        req.AnalysisLevel.Should().Be(AnalysisLevel.Ply4);
    }

    /// <summary>
    /// Same decision, no book database: every book-stamped candidate
    /// degrades to the bare "Book V2" label and
    /// <see cref="AnalysisMode.BookRollout"/> +
    /// <see cref="AnalysisLevel.Unknown"/>. The with/without pair pins that
    /// enrichment is strictly additive — the book changes labels and levels,
    /// never which decisions or candidates are emitted.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_FixtureA_WithoutBook_DegradesToUnknownLevel()
    {
        var file = LoadFixtureA(out string sourceFile);

        var req = XgDecisionIterator.IterateDiagramRequests(file, sourceFile)
            .Single(r => !r.Decision.IsCube
                      && r.Descriptive.Game == 9
                      && r.Descriptive.MoveNumber == 1);

        var best = req.Decision.Plays[0];
        best.Depth.Should().Be("Book V2");
        best.DepthAbbreviation.Should().Be("Book");
        best.DepthRank.Should().Be(99);
        best.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        best.AnalysisLevel.Should().Be(AnalysisLevel.Unknown);

        req.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        req.AnalysisLevel.Should().Be(AnalysisLevel.Unknown);
    }

    /// <summary>
    /// Per-candidate enrichment: each book-mode candidate of the fixture (a)
    /// decision resolves its own entry, keyed by its own resulting position.
    /// The decision's ten candidates pin both book populations at once
    /// (probed candidate-by-candidate against the real database, pane eval
    /// vectors bitwise-identical to the resolved entries):
    ///
    /// <para>
    /// Three candidates are backed by <b>rollout</b> entries and enrich —
    /// two from Neil Kazaross's 12,960-game 4-ply rollouts, one from Steven
    /// Carey's 15,552-game 3-ply rollout, so the enriched labels differ
    /// across candidates and each carries its recovered moves level. Two
    /// more candidates are 998-stamped but backed by <b>Roller++
    /// evaluation baseline</b> entries (Level 1002, zero trials): XG stamps
    /// 998 because the book supplied the numbers, yet there is no cached
    /// rollout to recover, so they deliberately stay at the unenriched
    /// "Book V2" label with <see cref="AnalysisLevel.Unknown"/>. The
    /// remaining five candidates are ordinary Roller++ evaluations from the
    /// file itself.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_FixtureA_WithBook_EnrichesEachBookCandidateFromItsOwnEntry()
    {
        var file = LoadFixtureA(out string sourceFile);
        var options = new XgIteratorOptions(OpeningBook: LoadBook());

        var req = XgDecisionIterator.IterateDiagramRequests(file, sourceFile, options: options)
            .Single(r => !r.Decision.IsCube
                      && r.Descriptive.Game == 9
                      && r.Descriptive.MoveNumber == 1);

        var bookPlays = req.Decision.Plays
            .Where(p => p.AnalysisMode == AnalysisMode.BookRollout)
            .ToList();
        bookPlays.Should().HaveCount(5, "five of the decision's ten candidates are book-stamped");
        bookPlays.Should().OnlyContain(p => p.DepthRank == 99,
            "enrichment must not move the book rank");

        // Rollout-backed hits enrich, each from its own entry.
        var enriched = bookPlays.Where(p => p.AnalysisLevel != AnalysisLevel.Unknown).ToList();
        enriched.Select(p => p.Depth).Should().BeEquivalentTo(
            [
                "Book V2: 12960 trials. 4-ply",
                "Book V2: 12960 trials. 4-ply",
                "Book V2: 15552 trials. 3-ply",
            ],
            "each rollout-backed candidate must carry its own entry's trials and moves level");
        enriched.Select(p => p.AnalysisLevel).Should().BeEquivalentTo(
            [AnalysisLevel.Ply4, AnalysisLevel.Ply4, AnalysisLevel.Ply3]);

        // Evaluation-backed hits (the book's Roller++ baselines) stay bare.
        var degraded = bookPlays.Where(p => p.AnalysisLevel == AnalysisLevel.Unknown).ToList();
        degraded.Should().HaveCount(2,
            "two 998 stamps resolve to Roller++ evaluation baseline entries with no rollout to recover");
        degraded.Should().OnlyContain(p => p.Depth == "Book V2" && p.DepthAbbreviation == "Book");
    }

    /// <summary>
    /// The CSV surface converges with the diagram surface on the enriched
    /// depth: <c>DecisionRow.AnalysisDepth</c> carries the enriched label
    /// (the CSV depth column keeps carrying whatever the label resolves to)
    /// and the row's <see cref="AnalysisMode"/> /
    /// <see cref="AnalysisLevel"/> match the diagram surface's
    /// best-by-equity candidate.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void Iterate_FixtureA_WithBook_CsvRowCarriesEnrichedDepth()
    {
        var file = LoadFixtureA(out string sourceFile);
        var options = new XgIteratorOptions(OpeningBook: LoadBook());

        var row = XgDecisionIterator.Iterate(file, sourceFile, options: options)
            .Single(r => !r.IsCube && r.Game == 9 && r.MoveNumber == 1);

        row.AnalysisDepth.Should().Be("Book V2: 12960 trials. 4-ply");
        row.AnalysisMode.Should().Be(AnalysisMode.BookRollout);
        row.AnalysisLevel.Should().Be(AnalysisLevel.Ply4);
    }

    // -----------------------------------------------------------------------
    //  3-ply Red — fixture-pinned against a real file (local-only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// A real XG file analysed at "3-ply Red" resolves to
    /// <see cref="AnalysisLevel.Ply3Red"/> with XG's own label casing and the
    /// interleaved rank — the end-to-end counterpart to the synthesized level
    /// table above, evidence that XG really does stamp level 12 for this
    /// setting rather than a variant of the plain 3-ply code.
    ///
    /// <para>
    /// Local-only by the TestData rule: the fixture lives in the gitignored
    /// <c>TestData/FixtureFiles/</c>, so this test carries the CI-excluded
    /// <c>RequiresFixtureFiles</c> trait and nothing gating depends on it.
    /// The gating coverage for level 12 is the synthesized level table.
    /// </para>
    ///
    /// <para>
    /// The fixture also covers the whole depth spread XG writes for a single
    /// decision: its two strongest candidates carry XG level 12 (3-ply Red),
    /// three more carry level code 11 — identified as plain 2-ply by XG's own
    /// display over these very rows (halheinrich/backgammon#160) — and the
    /// nine-candidate tail carries level 0 (1-ply). Three distinct levels in
    /// one analysis, which is also why per-candidate depth is not redundant
    /// with a decision-level depth.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void IterateDiagramRequests_ThreePlyRedFixture_ResolvesToPly3Red()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "3-ply Red.xgp");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the 3-ply Red fixture in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var plays = XgDecisionIterator
            .IterateDiagramRequests(file, Path.GetFileName(path))
            .Single(r => !r.Decision.IsCube)
            .Decision.Plays;

        plays.GroupBy(p => p.AnalysisLevel)
             .ToDictionary(g => g.Key, g => g.Count())
             .Should().BeEquivalentTo(new Dictionary<AnalysisLevel, int>
             {
                 [AnalysisLevel.Ply3Red] = 2,
                 [AnalysisLevel.Ply2]    = 3,
                 [AnalysisLevel.Ply1]    = 9,
             },
             "XG analysed the two strongest candidates at 3-ply Red, three more "
             + "at 2-ply (level code 11), and filled the tail at 1-ply");

        plays.Where(p => p.AnalysisLevel == AnalysisLevel.Ply3Red).Should().OnlyContain(
            p => p.Depth == "3-ply Red"
              && p.DepthAbbreviation == "3-ply Red"
              && p.DepthRank == 25
              && p.AnalysisMode == AnalysisMode.Evaluation,
            "level 12 carries XG's own casing and the interleaved rank");

        plays.Where(p => p.AnalysisLevel == AnalysisLevel.Ply2).Should().OnlyContain(
            p => p.Depth == "2-ply"
              && p.DepthAbbreviation == "2-ply"
              && p.DepthRank == 20
              && p.AnalysisMode == AnalysisMode.Evaluation,
            "level code 11 is plain 2-ply — XG's display draws no distinction");
    }

    /// <summary>
    /// The user's file from halheinrich/backgammon#251, whose rollouts XG
    /// shows as "First 2 moves: 4-ply … Remaining moves: XG Roller" and which
    /// read <c>1001p1296</c> before the fix: every rolled-out candidate reads
    /// as a 4-ply rollout of 1296 trials. The end-to-end counterpart to the
    /// synthesized user's-shape test — evidence that XG stores the phase
    /// split in <c>LevelCut</c> (2 here) and the first phase's level in
    /// <c>Level1</c>.
    ///
    /// <para>
    /// Local-only by the TestData rule: the fixture lives in the gitignored
    /// <c>TestData/FixtureFiles/</c>, so this test carries the CI-excluded
    /// <c>RequiresFixtureFiles</c> trait and nothing gating depends on it.
    /// The gating coverage is the synthesized rollout tests above.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public void IterateDiagramRequests_UserFourPlyThenRollerFixture_ReadsFourPly()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "Opening 41 44 51 62 MEDIUM.xgp");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the halheinrich/backgammon#251 fixture in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var rolledOut = XgDecisionIterator
            .IterateDiagramRequests(file, Path.GetFileName(path))
            .Where(r => !r.Decision.IsCube)
            .SelectMany(r => r.Decision.Plays)
            .Where(p => p.AnalysisMode == AnalysisMode.Rollout)
            .ToList();

        rolledOut.Should().NotBeEmpty("the fixture's plays were rolled out");
        rolledOut.Should().OnlyContain(
            p => p.Depth == "Rollout: 1296 trials. 4-ply"
              && p.DepthAbbreviation == DepthAbbreviationFormat.Rollout("4", 1296)
              && p.DepthRank == 140
              && p.AnalysisLevel == AnalysisLevel.Ply4,
            "the first phase (2 moves at 4-ply) names the rollout, not the XG Roller remainder");
    }
}
