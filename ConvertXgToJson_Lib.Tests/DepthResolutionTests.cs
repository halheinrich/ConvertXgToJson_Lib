using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgDecisionIterator.ResolveDepthInfo"/> — the
/// canonical producer of per-candidate analysis depth
/// (Label / Abbreviation / Rank). Covers every case of the underlying
/// ply-level switch so the abbreviation and rank tables can't silently
/// drift. Rollout branch is covered via a synthesized RolloutContext
/// so it doesn't depend on the binary corpus.
/// </summary>
public class DepthResolutionTests
{
    // Empty rollout list shared by every non-rollout-branch test.
    private static readonly List<RolloutContext> NoRollouts = [];

    // -----------------------------------------------------------------------
    //  Non-rollout branch — exhaustive short-level coverage
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData((short)0,    "1-ply",       "1-ply",     1)]
    [InlineData((short)1,    "2-ply",       "2-ply",     2)]
    [InlineData((short)2,    "3-ply",       "3-ply",     3)]
    [InlineData((short)12,   "3-ply red",   "3-ply red", 3)]
    [InlineData((short)3,    "4-ply",       "4-ply",     4)]
    [InlineData((short)4,    "5-ply",       "5-ply",     5)]
    [InlineData((short)5,    "6-ply",       "6-ply",     6)]
    [InlineData((short)6,    "7-ply",       "7-ply",     7)]
    [InlineData((short)100,  "Rollout",     "Ro",        100)]
    [InlineData((short)1000, "XG Roller",   "R",         20)]
    [InlineData((short)1001, "XG Roller+",  "R+",        21)]
    [InlineData((short)1002, "XG Roller++", "R++",       22)]
    [InlineData((short)998,  "Book V1",     "Book",      0)]
    [InlineData((short)999,  "Book V2",     "Book",      0)]
    public void ResolveDepthInfo_NonRollout_KnownLevels(
        short level, string expectedLabel, string expectedAbbrev, int expectedRank)
    {
        var (label, abbrev, rank) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: level,
            rolloutIndex: -1,
            rollouts: NoRollouts);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(expectedAbbrev);
        rank.Should().Be(expectedRank);
    }

    /// <summary>
    /// Unknown levels fall through to the synthesized "level-{N}" label
    /// on both Label and Abbreviation; rank defaults to 0 (lowest slot).
    /// Picked a value that hasn't been adopted by any XG version we've
    /// seen so this test doesn't quietly break if the switch gains a new
    /// case later.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_NonRollout_UnknownLevel_FallsThrough()
    {
        var (label, abbrev, rank) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 7777,
            rolloutIndex: -1,
            rollouts: NoRollouts);

        label.Should().Be("level-7777");
        abbrev.Should().Be("level-7777");
        rank.Should().Be(0);
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
        var (label, abbrev, rank) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 2, // 3-ply
            rolloutIndex: idx,
            rollouts: NoRollouts);

        label.Should().Be("3-ply");
        abbrev.Should().Be("3-ply");
        rank.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    //  Rollout branch — synthesized RolloutContext
    // -----------------------------------------------------------------------

    /// <summary>
    /// With a valid rollout index and Level2 set, ResolveDepthInfo takes
    /// the rollout branch: inner ply is Level2+1 (because the short
    /// encoding shifts by 1 — Level2=2 → 3-ply), abbreviation is
    /// "{innerPly}p{trials}", rank is 100+innerPly. evalLevel is
    /// ignored in this branch.
    /// </summary>
    [Theory]
    [InlineData(2, 1296, "Rollout: 1296 trials. 3-ply", "3p1296", 103)]
    [InlineData(3,  648, "Rollout: 648 trials. 4-ply",  "4p648",  104)]
    [InlineData(0,  500, "Rollout: 500 trials. 1-ply",  "1p500",  101)]
    public void ResolveDepthInfo_Rollout_Level2_PopulatesTriple(
        int level2, int trials, string expectedLabel, string expectedAbbrev, int expectedRank)
    {
        var rollouts = new List<RolloutContext>
        {
            new() { Level2 = level2, GamesRolled = trials },
        };

        // evalLevel here is 7777 (unknown non-rollout) to prove it's ignored.
        var (label, abbrev, rank) = XgDecisionIterator.ResolveDepthInfo(
            evalLevel: 7777,
            rolloutIndex: 0,
            rollouts: rollouts);

        label.Should().Be(expectedLabel);
        abbrev.Should().Be(expectedAbbrev);
        rank.Should().Be(expectedRank);
    }

    /// <summary>
    /// ResolveDepthInfo prefers Level2, then Level1, then LevelTrunc
    /// when computing the inner ply level. This test pins the fallback
    /// order so a refactor of the selection logic can't silently change
    /// which field wins.
    /// </summary>
    [Fact]
    public void ResolveDepthInfo_Rollout_LevelFallback_PrefersLevel2ThenLevel1ThenTrunc()
    {
        // Level2 dominates.
        var r1 = new List<RolloutContext>
        {
            new() { Level2 = 3, Level1 = 2, LevelTrunc = 1, GamesRolled = 100 },
        };
        XgDecisionIterator.ResolveDepthInfo(0, 0, r1).Rank.Should().Be(104,
            "Level2=3 → innerPly=4 → rank 104");

        // Level2 absent → Level1 wins.
        var r2 = new List<RolloutContext>
        {
            new() { Level2 = 0, Level1 = 2, LevelTrunc = 1, GamesRolled = 100 },
        };
        XgDecisionIterator.ResolveDepthInfo(0, 0, r2).Rank.Should().Be(103,
            "Level1=2 → innerPly=3 → rank 103");

        // Both absent → LevelTrunc wins.
        var r3 = new List<RolloutContext>
        {
            new() { Level2 = 0, Level1 = 0, LevelTrunc = 1, GamesRolled = 100 },
        };
        XgDecisionIterator.ResolveDepthInfo(0, 0, r3).Rank.Should().Be(102,
            "LevelTrunc=1 → innerPly=2 → rank 102");
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

        var c1 = XgDecisionIterator.ResolveDepthInfo(0, 1, rollouts);
        c1.Abbreviation.Should().Be("4p5000");
        c1.Rank.Should().Be(104);
    }
}
