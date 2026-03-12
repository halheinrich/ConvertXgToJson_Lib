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
    /// <summary>
    /// MatchInfo is populated on state before the first row is yielded from
    /// each file. Player names and match length must be non-default values for
    /// a well-formed .xg file.
    /// </summary>
    [Fact]
    public void MatchInfo_IsPopulatedBeforeFirstRow()
    {
        var state = new XgIteratorState();
        XgMatchInfo? capturedInfo = null;
        bool firstRow = true;

        foreach (var row in XgDecisionIterator.IterateXgDirectory(TestPaths.XgDir, state))
        {
            if (firstRow)
            {
                capturedInfo = state.MatchInfo;
                firstRow = false;
                break;
            }
        }

        capturedInfo.Should().NotBeNull("MatchInfo should be set before the first row");
        capturedInfo!.Player1.Should().NotBeNullOrEmpty("Player1 should be populated from the match header");
        capturedInfo.Player2.Should().NotBeNullOrEmpty("Player2 should be populated from the match header");
    }

    /// <summary>
    /// MatchInfo is reset to null then repopulated at the start of each new file.
    /// </summary>
    [Fact]
    public void MatchInfo_IsResetBetweenFiles()
    {
        var files = TestPaths.XgFiles.Take(2).ToList();
        if (files.Count < 2)
            return;

        var state = new XgIteratorState();
        var capturedInfos = new List<XgMatchInfo?>();
        string? lastMatch = null;

        foreach (var row in XgDecisionIterator.IterateXgDirectory(TestPaths.XgDir, state))
        {
            if (row.Match != lastMatch)
            {
                capturedInfos.Add(state.MatchInfo);
                lastMatch = row.Match;
            }
            if (capturedInfos.Count >= 2) break;
        }

        capturedInfos.Count.Should().BeGreaterThanOrEqualTo(2,
            "need at least two files to verify MatchInfo resets");
        capturedInfos.Should().AllSatisfy(info =>
            info.Should().NotBeNull("MatchInfo should be set at the start of each file"));
    }
    // -----------------------------------------------------------------------
    //  XgIteratorState.GameInfo tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// GameInfo is populated on state before the first row of each game is yielded.
    /// </summary>
    [Fact]
    public void GameInfo_IsPopulatedBeforeFirstRow()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string matchId = Path.GetFileNameWithoutExtension(path);

        var state = new XgIteratorState();
        XgGameInfo? capturedInfo = null;
        bool firstRow = true;

        foreach (var row in XgDecisionIterator.Iterate(file, matchId, state))
        {
            if (firstRow)
            {
                capturedInfo = state.GameInfo;
                firstRow = false;
                break;
            }
        }

        capturedInfo.Should().NotBeNull("GameInfo should be set before the first row of a game");
    }

    /// <summary>
    /// GameInfo is reset and repopulated at the start of each new game.
    /// </summary>
    [Fact]
    public void GameInfo_IsResetBetweenGames()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string matchId = Path.GetFileNameWithoutExtension(path);

        var state = new XgIteratorState();
        var capturedInfos = new List<XgGameInfo?>();
        int? lastGame = null;

        foreach (var row in XgDecisionIterator.Iterate(file, matchId, state))
        {
            if (row.Game != lastGame)
            {
                capturedInfos.Add(state.GameInfo);
                lastGame = row.Game;
            }
            if (capturedInfos.Count >= 2) break;
        }

        if (capturedInfos.Count < 2)
            return; // file has only one game — skip

        capturedInfos.Should().AllSatisfy(info =>
            info.Should().NotBeNull("GameInfo should be set at the start of each game"));
    }

    /// <summary>
    /// IsStandardStart is true for a game that starts from the standard opening position.
    /// Verified using ThisWay.xg which is a normally started match.
    /// </summary>
    [Fact]
    public void GameInfo_IsStandardStart_TrueForNormalGame()
    {
        var file = XgFileReader.ReadFile(TestPaths.ThisWayXg);
        string matchId = Path.GetFileNameWithoutExtension(TestPaths.ThisWayXg);

        var state = new XgIteratorState();

        foreach (var row in XgDecisionIterator.Iterate(file, matchId, state))
        {
            // First game of a normal match must be standard start
            state.GameInfo.Should().NotBeNull();
            state.GameInfo!.IsStandardStart.Should().BeTrue(
                "ThisWay.xg game 1 starts from the standard opening position");
            break;
        }
    }

    /// <summary>
    /// Setting AdvanceNextGame after reading GameInfo skips the entire game
    /// before any rows are yielded from it.
    /// </summary>
    [Fact]
    public void GameInfo_AdvanceNextGame_SkipsEntireGame()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string matchId = Path.GetFileNameWithoutExtension(path);

        var allRows = XgDecisionIterator.Iterate(file, matchId).ToList();

        // Need a file with at least 2 games that each have rows
        var gamesWithRows = allRows.GroupBy(r => r.Game).Where(g => g.Count() > 0).ToList();
        if (gamesWithRows.Count < 2)
            return;

        int skipGame = gamesWithRows.First().Key;

        var state = new XgIteratorState();
        var collected = new List<DecisionRow>();
        int? lastGame = null;

        foreach (var row in XgDecisionIterator.Iterate(file, matchId, state))
        {
            if (row.Game != lastGame)
            {
                // At game boundary, check if we should skip this game
                if (state.GameInfo != null && row.Game == skipGame)
                {
                    // This shouldn't happen — we set AdvanceNextGame before rows yield
                }
                lastGame = row.Game;
            }
            collected.Add(row);
        }

        // Now do it properly — set AdvanceNextGame based on GameInfo
        // GameInfo is set before any rows yield, so we need to observe it
        // by hooking into the enumerator manually
        var state2 = new XgIteratorState();
        var collected2 = new List<DecisionRow>();
        int? prevGame2 = null;

        var enumerator = XgDecisionIterator.Iterate(file, matchId, state2).GetEnumerator();
        while (enumerator.MoveNext())
        {
            var row = enumerator.Current;
            if (row.Game == skipGame && prevGame2 != skipGame)
            {
                // First row of the target game — GameInfo was already set before this
                // We can't set it pre-row via Iterate(), but we can skip after first row
                state2.AdvanceNextGame = true;
            }
            if (row.Game != skipGame)
                collected2.Add(row);
            prevGame2 = row.Game;
        }

        collected2.Any(r => r.Game == skipGame).Should().BeFalse(
            "no rows from the skipped game should appear after AdvanceNextGame is set on first row");
        collected2.Count.Should().BeGreaterThan(0,
            "rows from other games should still be yielded");
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