// XgDecisionIteratorTests.cs
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for XgDecisionIterator row construction.
/// </summary>
[Collection("FileIO")]
public class XgDecisionIteratorTests
{
    // -----------------------------------------------------------------------
    //  Cube rows — XGID turn field
    // -----------------------------------------------------------------------

    /// <summary>
    /// ThisWay.xg and ThatWay.xg are the same match with top and bottom players
    /// reversed. Every XGID produced must be identical between the two files —
    /// XGID is always encoded from the bottom player's perspective regardless of
    /// who is on roll.
    /// </summary>
    [Fact]
    public void ThisWayAndThatWay_ProduceIdenticalXgids()
    {
        var thisWay = XgDecisionIterator
            .Iterate(XgFileReader.ReadFile(TestPaths.ThisWayXg),
                     Path.GetFileNameWithoutExtension(TestPaths.ThisWayXg))
            .Select(r => r.Xgid)
            .ToList();

        var thatWay = XgDecisionIterator
            .Iterate(XgFileReader.ReadFile(TestPaths.ThatWayXg),
                     Path.GetFileNameWithoutExtension(TestPaths.ThatWayXg))
            .Select(r => r.Xgid)
            .ToList();

        thisWay.Should().BeEquivalentTo(thatWay,
            options => options.WithStrictOrdering(),
            "ThisWay.xg and ThatWay.xg are the same match with perspectives " +
            "reversed — XGIDs must be identical");
    }
    // -----------------------------------------------------------------------
    //  XgIteratorState — early-exit tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// With no state passed, behaviour is identical to the stateless overload.
    /// </summary>
    [Fact]
    public void NullState_BehaviourUnchanged()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            var withoutState = XgDecisionIterator.Iterate(file, matchId).ToList();
            var withNull = XgDecisionIterator.Iterate(file, matchId, null).ToList();

            withNull.Count.Should().Be(withoutState.Count,
                $"null state should produce identical rows [{Path.GetFileName(path)}]");
        }
    }

    /// <summary>
    /// Setting AdvanceNextGame after the first row of a game skips remaining
    /// decisions in that game but not in subsequent games.
    /// </summary>
    [Fact]
    public void AdvanceNextGame_SkipsRemainingDecisionsInGame()
    {
        // Use the first xg file that has multiple decisions in at least one game.
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string matchId = Path.GetFileNameWithoutExtension(path);

        var allRows = XgDecisionIterator.Iterate(file, matchId).ToList();

        // Find a game that has more than one decision.
        int targetGame = allRows
            .GroupBy(r => r.Game)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .FirstOrDefault(-1);

        if (targetGame == -1)
            return; // no game with >1 decision — skip test

        int fullCountInGame = allRows.Count(r => r.Game == targetGame);

        // Now iterate with state, advancing after the first row of targetGame.
        var state = new XgIteratorState();
        var collected = new List<DecisionRow>();

        foreach (var row in XgDecisionIterator.Iterate(file, matchId, state))
        {
            collected.Add(row);
            if (row.Game == targetGame && collected.Count(r => r.Game == targetGame) == 1)
                state.AdvanceNextGame = true;
        }

        int skippedCount = collected.Count(r => r.Game == targetGame);
        skippedCount.Should().Be(1,
            $"only the first decision of game {targetGame} should be yielded " +
            $"after AdvanceNextGame is set (full count was {fullCountInGame})");

        // Decisions from other games should still appear.
        collected.Any(r => r.Game != targetGame).Should().BeTrue(
            "decisions from other games should not be skipped");
    }

    /// <summary>
    /// Setting AdvanceNextMatch after the first row skips all remaining
    /// decisions in the match.
    /// </summary>
    [Fact]
    public void AdvanceNextMatch_SkipsRemainingDecisionsInMatch()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string matchId = Path.GetFileNameWithoutExtension(path);

        var allRows = XgDecisionIterator.Iterate(file, matchId).ToList();
        allRows.Count.Should().BeGreaterThan(1,
            "test requires a file with more than one decision");

        var state = new XgIteratorState();
        var collected = new List<DecisionRow>();

        foreach (var row in XgDecisionIterator.Iterate(file, matchId, state))
        {
            collected.Add(row);
            state.AdvanceNextMatch = true; // skip everything after first row
        }

        collected.Count.Should().Be(1,
            "only the first decision should be yielded after AdvanceNextMatch is set");
    }

    /// <summary>
    /// AdvanceNextMatch set during directory iteration does not bleed into
    /// the next file — subsequent files still yield rows.
    /// </summary>
    [Fact]
    public void AdvanceNextMatch_DoesNotBleedIntoNextFile()
    {
        var files = TestPaths.XgFiles.Take(2).ToList();
        if (files.Count < 2)
            return; // need at least two files

        var state = new XgIteratorState();
        var collected = new List<DecisionRow>();

        foreach (var row in XgDecisionIterator.IterateXgDirectory(
            TestPaths.XgDir, state))
        {
            collected.Add(row);
            state.AdvanceNextMatch = true; // skip rest of every match after first row
        }

        // Should get exactly one row per file.
        collected.Count.Should().BeGreaterThanOrEqualTo(2,
            "each file should contribute at least one row despite AdvanceNextMatch");

        var matchIds = collected.Select(r => r.Match).Distinct().ToList();
        matchIds.Count.Should().BeGreaterThanOrEqualTo(2,
            "rows should come from at least two distinct matches");
    }
    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>Extracts the turn field (field index 3, 0-based after "XGID=") from an XGID string.</summary>
    private static int ExtractTurn(string xgid)
    {
        // Format: XGID=<pos>:<cv>:<cp>:<turn>:<dice>:...
        var parts = xgid.Split(':');
        // parts[0] = "XGID=<pos>", parts[1]=cv, parts[2]=cp, parts[3]=turn
        return int.Parse(parts[3]);
    }
}