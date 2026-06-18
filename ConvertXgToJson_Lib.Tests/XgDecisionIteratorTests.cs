using BgDataTypes_Lib;
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
                     Path.GetFileName(TestPaths.ThisWayXg))
            .Select(r => r.Xgid)
            .ToList();

        var thatWay = XgDecisionIterator
            .Iterate(XgFileReader.ReadFile(TestPaths.ThatWayXg),
                     Path.GetFileName(TestPaths.ThatWayXg))
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
            string sourceFile = Path.GetFileName(path);

            var withoutState = XgDecisionIterator.Iterate(file, sourceFile).ToList();
            var withNull = XgDecisionIterator.Iterate(file, sourceFile, null).ToList();

            withNull.Count.Should().Be(withoutState.Count,
                $"null state should produce identical rows [{Path.GetFileName(path)}]");
        }
    }

    /// <summary>
    /// A <c>StopGameAfter</c> predicate returning true after the first row of
    /// a game skips remaining decisions in that game but not in subsequent
    /// games.
    /// </summary>
    [Fact]
    public void StopGameAfter_SkipsRemainingDecisionsInGame()
    {
        // Use the first xg file that has multiple decisions in at least one game.
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        var allRows = XgDecisionIterator.Iterate(file, sourceFile).ToList();

        // Find a game that has more than one decision.
        int targetGame = allRows
            .GroupBy(r => r.Game)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .FirstOrDefault(-1);

        if (targetGame == -1)
            return; // no game with >1 decision — skip test

        int fullCountInGame = allRows.Count(r => r.Game == targetGame);

        // StopGameAfter receives IDecisionFilterData (the cross-surface
        // contract); cast back to DecisionRow to read the .Game ordinal.
        var callbacks = new XgIteratorCallbacks(
            StopGameAfter: row => ((DecisionRow)row).Game == targetGame);
        var collected = XgDecisionIterator
            .Iterate(file, sourceFile, callbacks: callbacks)
            .ToList();

        int skippedCount = collected.Count(r => r.Game == targetGame);
        skippedCount.Should().Be(1,
            $"only the first decision of game {targetGame} should be yielded " +
            $"after StopGameAfter returns true (full count was {fullCountInGame})");

        // Decisions from other games should still appear.
        collected.Any(r => r.Game != targetGame).Should().BeTrue(
            "decisions from other games should not be skipped");
    }

    /// <summary>
    /// A <c>StopMatchAfter</c> predicate returning true after the first
    /// yielded row skips all remaining decisions in the match.
    /// </summary>
    [Fact]
    public void StopMatchAfter_SkipsRemainingDecisionsInMatch()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        var allRows = XgDecisionIterator.Iterate(file, sourceFile).ToList();
        allRows.Count.Should().BeGreaterThan(1,
            "test requires a file with more than one decision");

        var callbacks = new XgIteratorCallbacks(
            StopMatchAfter: _ => true);
        var collected = XgDecisionIterator
            .Iterate(file, sourceFile, callbacks: callbacks)
            .ToList();

        collected.Count.Should().Be(1,
            "only the first decision should be yielded when StopMatchAfter returns true");
    }

    // -----------------------------------------------------------------------
    //  IterateXgDirectory file selection
    // -----------------------------------------------------------------------

    /// <summary>
    /// IterateXgDirectory must enumerate both <c>*.xg</c> match files and
    /// <c>*.xgp</c> position files — both formats are valid XG-native
    /// inputs and downstream consumers (e.g. LocalFolderProcessor) treat
    /// them as a single supported set. Regression guard: prior to this
    /// test, the iterator filtered to <c>*.xg</c> only and silently
    /// skipped every <c>.xgp</c>.
    ///
    /// <para>
    /// Self-contained: copies one fixture of each extension into a fresh
    /// temp directory and iterates it. Avoids touching <c>TestData/xg/</c>
    /// (which is partitioned by extension by convention).
    /// </para>
    /// </summary>
    [Fact]
    public void IterateXgDirectory_IncludesBothXgAndXgpFiles()
    {
        const string xgFixture = "match35041658.xg";
        const string xgpFixture = "PlayAnalysis.xgp";
        string xgSource = Path.Combine(TestPaths.FixtureFilesDir, xgFixture);
        string xgpSource = Path.Combine(TestPaths.FixtureFilesDir, xgpFixture);

        if (!File.Exists(xgSource) || !File.Exists(xgpSource))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixtures missing: {xgSource} and/or {xgpSource}.");

        string tempDir = Path.Combine(Path.GetTempPath(), "ConvertXgToJson_Lib.Tests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(xgSource, Path.Combine(tempDir, xgFixture));
            File.Copy(xgpSource, Path.Combine(tempDir, xgpFixture));

            var rows = XgDecisionIterator.IterateXgDirectory(tempDir).ToList();

            var sourceFiles = rows.Select(r => r.SourceFile).Distinct().ToList();
            sourceFiles.Should().Contain(xgFixture,
                "IterateXgDirectory must yield rows from .xg match files");
            sourceFiles.Should().Contain(xgpFixture,
                "IterateXgDirectory must yield rows from .xgp position files — " +
                "the prior *.xg-only filter silently skipped these");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// A <c>StopMatchAfter</c> predicate fires independently per file during
    /// directory iteration: skipping the rest of one match has no effect on
    /// subsequent files. The predicate is stateless from the producer's
    /// perspective, so bleed across files is impossible by construction.
    /// </summary>
    [Fact]
    public void StopMatchAfter_DoesNotBleedIntoNextFile()
    {
        var files = TestPaths.XgFiles.Take(2).ToList();
        if (files.Count < 2)
            return; // need at least two files

        var callbacks = new XgIteratorCallbacks(
            StopMatchAfter: _ => true);
        var collected = XgDecisionIterator
            .IterateXgDirectory(TestPaths.XgDir, callbacks: callbacks)
            .ToList();

        // Should get exactly one row per file that has any rows.
        collected.Count.Should().BeGreaterThanOrEqualTo(2,
            "each file should contribute at least one row despite StopMatchAfter");

        var sourceFiles = collected.Select(r => r.SourceFile).Distinct().ToList();
        sourceFiles.Count.Should().BeGreaterThanOrEqualTo(2,
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
        string? lastSourceFile = null;

        foreach (var row in XgDecisionIterator.IterateXgDirectory(TestPaths.XgDir, state))
        {
            if (row.SourceFile != lastSourceFile)
            {
                capturedInfos.Add(state.MatchInfo);
                lastSourceFile = row.SourceFile;
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
    /// Away scores are non-negative and sum to less than or equal to match length.
    /// </summary>
    [Fact]
    public void GameInfo_IsPopulatedBeforeFirstRow()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        var state = new XgIteratorState();

        foreach (var row in XgDecisionIterator.Iterate(file, sourceFile, state))
        {
            state.GameInfo.Should().NotBeNull("GameInfo should be set before the first row");
            state.GameInfo!.Away1.Should().BeGreaterThanOrEqualTo(0);
            state.GameInfo.Away2.Should().BeGreaterThanOrEqualTo(0);
            break;
        }
    }

    /// <summary>
    /// GameInfo is reset and repopulated at the start of each new game.
    /// </summary>
    [Fact]
    public void GameInfo_IsResetBetweenGames()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        var state = new XgIteratorState();
        var capturedInfos = new List<XgGameInfo?>();
        int? lastGame = null;

        foreach (var row in XgDecisionIterator.Iterate(file, sourceFile, state))
        {
            if (row.Game != lastGame)
            {
                capturedInfos.Add(state.GameInfo);
                lastGame = row.Game;
            }
            if (capturedInfos.Count >= 2) break;
        }

        if (capturedInfos.Count < 2)
            return; // single-game file — skip

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
        string sourceFile = Path.GetFileName(TestPaths.ThisWayXg);

        var state = new XgIteratorState();

        foreach (var row in XgDecisionIterator.Iterate(file, sourceFile, state))
        {
            // First game of a normal match must be standard start
            state.GameInfo.Should().NotBeNull();
            state.GameInfo!.IsStandardStart.Should().BeTrue(
                "ThisWay.xg game 1 starts from the standard opening position");
            break;
        }
    }

    /// <summary>
    /// A <c>SkipGameAt</c> predicate evaluated at the game header skips the
    /// entire game before any rows are yielded from it. This is the precise
    /// "skip before yield" semantic the prior flag-based mechanism could not
    /// express — under the old API the first row of the target game still
    /// yielded before the flag could be set.
    /// </summary>
    [Fact]
    public void SkipGameAt_SkipsEntireGameBeforeAnyYield()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        var allRows = XgDecisionIterator.Iterate(file, sourceFile).ToList();

        // Need a file with at least 2 games that each have rows
        var gamesWithRows = allRows.GroupBy(r => r.Game).Where(g => g.Count() > 0).ToList();
        if (gamesWithRows.Count < 2)
            return;

        int skipGame = gamesWithRows.First().Key;

        // SkipGameAt fires once per game header, ordered by file position.
        // Skip the Nth invocation where N matches the target game number
        // (MatchContext.GameNumber is 1-based, incremented on each
        // GameHeaderRecord, so the callback-fire ordinal lines up).
        int currentGameOrdinal = 0;
        var callbacks = new XgIteratorCallbacks(
            SkipGameAt: _ => ++currentGameOrdinal == skipGame);
        var collected = XgDecisionIterator
            .Iterate(file, sourceFile, callbacks: callbacks)
            .ToList();

        collected.Any(r => r.Game == skipGame).Should().BeFalse(
            "no rows from the skipped game should appear when SkipGameAt returns true");
        collected.Count.Should().BeGreaterThan(0,
            "rows from other games should still be yielded");
    }
    /// <summary>
    /// GameInfo.Away scores are correct: MatchLength - Score at game start.
    /// Verified against the first game of a match file where scores start at 0.
    /// </summary>
    [Fact]
    public void GameInfo_AwayScores_CorrectForFirstGame()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        // Get match length from the file
        int matchLength = 0;
        foreach (var r in file.Records)
        {
            if (r is MatchHeaderRecord hm)
            {
                matchLength = hm.MatchLength >= 99999 ? 0 : hm.MatchLength;
                break;
            }
        }

        if (matchLength == 0)
            return; // money session — skip

        var state = new XgIteratorState();

        foreach (var row in XgDecisionIterator.Iterate(file, sourceFile, state))
        {
            // First game of a match always starts 0-0
            state.GameInfo!.Away1.Should().Be(matchLength,
                "first game starts at score 0, so away = matchLength");
            state.GameInfo.Away2.Should().Be(matchLength,
                "first game starts at score 0, so away = matchLength");
            break;
        }
    }
    /// <summary>
    /// For every non-money decision, MatchScore is expressed from the on-roll
    /// player's perspective: away1 = MatchLength - onRollScore, away2 = MatchLength - opponentScore.
    /// Scores are cross-checked against the XGID score fields (field indices 5 and 6).
    /// </summary>
    [Fact]
    public void MatchScore_Away1_IsOnRollPlayersAwayScore()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, sourceFile))
            {
                if (row.MatchLength == 0) continue; // money — no away scores

                // XGID format: XGID=<pos>:<cv>:<cp>:<turn>:<dice>:<score1>:<score2>:<cj>:<ml>:<maxcube>
                var parts = row.Xgid.Split(':');
                int xgidScore1 = int.Parse(parts[5]); // on-roll player's score
                int xgidScore2 = int.Parse(parts[6]); // opponent's score

                // MatchScore format: "{away1}a{away2}a[C]"
                var scoreParts = row.MatchScore.TrimEnd('C').Split('a', StringSplitOptions.RemoveEmptyEntries);
                int msAway1 = int.Parse(scoreParts[0]);
                int msAway2 = int.Parse(scoreParts[1]);

                int expectedAway1 = row.MatchLength - xgidScore1;
                int expectedAway2 = row.MatchLength - xgidScore2;

                msAway1.Should().Be(expectedAway1,
                    $"away1 should be on-roll player's away score in {Path.GetFileName(path)} " +
                    $"game {row.Game} move {row.MoveNumber} (XGID score1={xgidScore1})");
                msAway2.Should().Be(expectedAway2,
                    $"away2 should be opponent's away score in {Path.GetFileName(path)} " +
                    $"game {row.Game} move {row.MoveNumber} (XGID score2={xgidScore2})");
            }
        }
    }

    // -----------------------------------------------------------------------
    //  SourceFile plumbing — corpus check
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every <see cref="DecisionRow"/> and <see cref="BgDecisionData"/> produced
    /// from a corpus file carries that file's name (with extension) in its
    /// <c>SourceFile</c> field. Guards against regressions in the parser
    /// plumbing where the filename is dropped before reaching a row or request.
    /// </summary>
    [Fact]
    public void SourceFile_PopulatedFromFixtureFilename_ForEveryRowAndRequest()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string expected = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, expected))
            {
                row.SourceFile.Should().Be(expected,
                    $"every DecisionRow from {expected} must carry that filename");
            }

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, expected))
            {
                req.Descriptive.SourceFile.Should().Be(expected,
                    $"every BgDecisionData from {expected} must carry that filename");
            }
        }
    }

    /// <summary>
    /// Cube DecisionRows (both doubler and taker) carry no play, so the
    /// PlayOutcomeData contract requires both after-boards empty. Producer-
    /// enforced — board-based play-type filters rely on this to skip cube rows.
    /// </summary>
    [Fact]
    public void CubeDecisionRows_AfterBoardsAreEmpty()
    {
        bool foundCube = false;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, Path.GetFileName(path))
                                                   .Where(r => r.IsCube))
            {
                foundCube = true;
                row.AfterBestBoard.Should().BeEmpty(
                    $"cube DecisionRow in {Path.GetFileName(path)} must have empty AfterBestBoard");
                row.AfterPlayerBoard.Should().BeEmpty(
                    $"cube DecisionRow in {Path.GetFileName(path)} must have empty AfterPlayerBoard");
            }
        }

        foundCube.Should().BeTrue("test data should contain at least one cube decision");
    }

    // -----------------------------------------------------------------------
    //  DecisionRow.Equity — best-by-equity convention
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>match35253054.xg</c> contains at least one decision where XG's
    /// native rank 0 is not the highest-equity candidate (the same anomaly
    /// fixture used by the diagram-request sort test). For every such
    /// decision, <see cref="DecisionRow.Equity"/> must report the
    /// <em>highest</em> equity in the analysed candidate set, not
    /// <c>Evals[0].Equity</c>. Pairs raw MoveRecords with DecisionRows
    /// emitted by <see cref="XgDecisionIterator.Iterate"/> and compares
    /// against <see cref="XgDecisionIterator.FindBestByEquityIndex"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void MoveRow_Equity_UsesHighestEquityCandidate_NotXgNativeRank0()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35253054.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on match35253054.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        using var rowEnum = XgDecisionIterator
            .Iterate(file, sourceFile)
            .Where(r => !r.IsCube)
            .GetEnumerator();

        int divergingDecisions = 0;

        foreach (var rec in file.Records.OfType<MoveRecord>())
        {
            var analysis = rec.Analysis;
            if (analysis.MoveCount == 0 || analysis.Evals.Length == 0) continue;
            if (XgDecisionIterator.IsSentinelOnlyAnalysis(analysis)) continue;

            rowEnum.MoveNext().Should().BeTrue(
                $"{sourceFile}: expected a DecisionRow for analysed move");
            var row = rowEnum.Current;

            int bestIdx = XgDecisionIterator.FindBestByEquityIndex(analysis);
            double maxEquity = analysis.Evals.Take(analysis.MoveCount).Max(e => e.Equity);
            double rank0Equity = analysis.Evals[0].Equity;

            row.Equity.Should().BeApproximately(maxEquity, 1e-9,
                $"{sourceFile} game {row.Game} move {row.MoveNumber}: " +
                $"DecisionRow.Equity must be the highest equity in the candidate set");

            if (bestIdx != 0 || maxEquity > rank0Equity)
                divergingDecisions++;
        }

        divergingDecisions.Should().BeGreaterThan(0,
            $"{sourceFile} must contain at least one decision where XG-native rank 0 " +
            "disagrees with best-by-equity; otherwise this test passes vacuously.");
    }

    // -----------------------------------------------------------------------
    //  MoveNumber and IsStandardStart propagation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every emitted DecisionRow across the .xg corpus has
    /// <c>MoveNumber &gt;= 1</c>. Move rows pick up MoveNumber after
    /// <c>MatchContext.Update</c> has incremented it; cube rows use
    /// <c>ctx.MoveNumber + 1</c>, so even an opening cube decision (no
    /// preceding move) yields 1. A row with MoveNumber == 0 indicates the
    /// new field was not populated from the context.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void Iterate_AllRows_MoveNumberAtLeastOne()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var row in XgDecisionIterator.Iterate(file, sourceFile))
            {
                row.MoveNumber.Should().BeGreaterThanOrEqualTo(1,
                    $"{sourceFile} game {row.Game}: every decision row must have MoveNumber >= 1");
            }
        }
    }

    /// <summary>
    /// <c>match35041658.xg</c> and <c>match35253054.xg</c> are full-match
    /// XG files where every game starts from the canonical opening
    /// position. Every emitted DecisionRow must therefore carry
    /// <c>IsStandardStart == true</c>. Pins MatchContext-&gt;DecisionRow
    /// flow for the new field; a regression here would manifest as
    /// downstream MoveNumberFilter (Session 3) silently rejecting all
    /// match decisions.
    /// </summary>
    [Theory]
    [InlineData("match35041658.xg")]
    [InlineData("match35253054.xg")]
    [Trait("Category", "FileIO")]
    public void Iterate_StandardStartFixture_AllRowsIsStandardStartTrue(string fixtureName)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}");

        var file = XgFileReader.ReadFile(path);
        var rows = XgDecisionIterator.Iterate(file, fixtureName).ToList();

        rows.Should().NotBeEmpty($"{fixtureName} should yield at least one row");
        rows.Should().OnlyContain(r => r.IsStandardStart,
            $"{fixtureName} starts every game from the canonical opening; " +
            "every DecisionRow must carry IsStandardStart=true");
    }

    // -----------------------------------------------------------------------
    //  Xgid — cross-surface agreement
    // -----------------------------------------------------------------------

    /// <summary>
    /// The diagram surface (<see cref="XgDecisionIterator.IterateDiagramRequests"/>)
    /// and the CSV surface (<see cref="XgDecisionIterator.Iterate"/>) must stamp
    /// the same XGID on the same decision. Pairs the two by their shared
    /// <see cref="DecisionId"/> across the whole .xg corpus and asserts
    /// <c>BgDecisionData.Xgid == DecisionRow.Xgid</c>. Fixture-agnostic — no
    /// hardcoded XGID — and would fail outright if the diagram builders left
    /// <c>Xgid</c> at its default empty string.
    ///
    /// <para>
    /// Direction: the diagram set is a subset of the CSV set (the checker
    /// diagram builder additionally drops <c>dice == 0</c> rows), so every
    /// <c>BgDecisionData.Id</c> is expected to resolve against the CSV-row
    /// dictionary, not vice versa.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void DiagramRequests_Xgid_MatchesDecisionRowXgid()
    {
        bool anyPaired = false;

        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            var rowXgidById = XgDecisionIterator
                .Iterate(file, sourceFile)
                .ToDictionary(r => r.Id, r => r.Xgid);

            foreach (var data in XgDecisionIterator.IterateDiagramRequests(file, sourceFile))
            {
                rowXgidById.TryGetValue(data.Id, out var rowXgid).Should().BeTrue(
                    $"{sourceFile}: BgDecisionData {data.Id} must pair with a DecisionRow of the same Id");
                data.Xgid.Should().NotBeNullOrEmpty(
                    $"{sourceFile} {data.Id}: BgDecisionData.Xgid must be populated");
                data.Xgid.Should().Be(rowXgid,
                    $"{sourceFile} {data.Id}: diagram and CSV surfaces must agree on XGID");
                anyPaired = true;
            }
        }

        anyPaired.Should().BeTrue("the .xg corpus should yield at least one decision");
    }

    // -----------------------------------------------------------------------
    //  Comment / Flagged — DescriptiveData population
    // -----------------------------------------------------------------------

    /// <summary>
    /// <see cref="DescriptiveData.Comment"/> and <see cref="DescriptiveData.Flagged"/>
    /// are populated from the source record's <c>CommentIndex</c> /
    /// <c>Flagged</c> and the file's comment table. Synthetic — a minimal
    /// match-header + analysed-cube file with a known <c>Comments</c> list —
    /// so it does not depend on any corpus file happening to carry a comment
    /// or a flag. Exercises the in-range join, the <c>-1</c> no-comment
    /// sentinel, and the out-of-range bounds guard.
    /// </summary>
    [Fact]
    public void DiagramRequests_CommentAndFlagged_PopulatedFromRecordAndCommentTable()
    {
        const string sourceFile = "synthetic.xg";
        var comments = new List<string> { "comment zero", "comment one", "the flagged note" };

        BgDecisionData Build(bool flagged, int commentIndex)
        {
            var header = new MatchHeaderRecord
            {
                MatchLength = 7,
                Player1 = "Alice",
                Player2 = "Bob",
            };
            var cube = new CubeRecord
            {
                ActivePlayer = 1,
                CubeValue = 0,
                Position = new PositionEngine
                {
                    Points = (sbyte[])BackgammonConstants.StandardOpeningPosition.Clone(),
                },
                Analysis = new DoubleActionAnalysis { Level = 1 },
                Flagged = flagged,
                CommentIndex = commentIndex,
            };
            var file = new XgFile
            {
                Records = new List<SaveRecord> { header, cube },
                Comments = comments,
            };
            return XgDecisionIterator.IterateDiagramRequests(file, sourceFile).Single();
        }

        // Flagged + an in-range comment index → both fields populated.
        var flaggedWithComment = Build(flagged: true, commentIndex: 2);
        flaggedWithComment.Descriptive.Flagged.Should().BeTrue(
            "Flagged must pass through from the source record");
        flaggedWithComment.Descriptive.Comment.Should().Be("the flagged note",
            "Comment must join CommentIndex against the file's comment table");

        // Not flagged + a different in-range index → joins the correct entry.
        var plain = Build(flagged: false, commentIndex: 0);
        plain.Descriptive.Flagged.Should().BeFalse();
        plain.Descriptive.Comment.Should().Be("comment zero");

        // XG's "no comment" sentinel (-1) must map to empty, NOT Comments[0].
        Build(flagged: false, commentIndex: -1).Descriptive.Comment.Should().BeEmpty(
            "CommentIndex -1 is XG's no-comment sentinel and must not alias Comments[0]");

        // An out-of-range index is bounds-guarded to empty.
        Build(flagged: false, commentIndex: 99).Descriptive.Comment.Should().BeEmpty(
            "an out-of-range CommentIndex must be bounds-guarded to empty");
    }

    // -----------------------------------------------------------------------
    //  Cubeless equities — cube DecisionData population
    // -----------------------------------------------------------------------

    /// <summary>
    /// On cube decisions, <see cref="DecisionData.CubelessNoDoubleEquity"/> and
    /// <see cref="DecisionData.CubelessDoubleTakeEquity"/> are sourced from the
    /// analysis's cubeless evals (<c>EvalNoDouble.Equity</c> /
    /// <c>EvalDoubleTake.Equity</c>), under the same <c>IsUsable</c> guard the
    /// cubeful pair uses. Pairs raw cube records with diagram requests
    /// sequentially (both surfaces emit one cube decision per
    /// <c>Analysis.Level &gt; 0</c> cube record, in record order) and checks the
    /// wired value against the source field.
    ///
    /// <para>
    /// Gating: requires at least one cube decision whose cubeless equity is
    /// non-zero and whose cubeless N/D differs from the cubeful N/D
    /// (<c>EquityNoDouble</c>) — otherwise the test would pass even if the
    /// fields were left at default <c>0.0</c> or mis-wired to the cubeful
    /// source.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void DiagramRequests_CubeDecisions_CubelessEquitiesMatchAnalysis()
    {
        // Mirror of XgDecisionIterator.IsUsable (private): NaN / infinity /
        // -999 sentinel → 0.0, matching the producer's guard exactly.
        static double Usable(float v) =>
            !float.IsNaN(v) && !float.IsInfinity(v) && v > -999f ? v : 0.0;

        int nonZero = 0;
        int divergesFromCubeful = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            using var cubeData = XgDecisionIterator
                .IterateDiagramRequests(file, sourceFile)
                .Where(d => d.Decision.IsCube)
                .GetEnumerator();

            foreach (var cube in file.Records.OfType<CubeRecord>().Where(c => c.Analysis.Level > 0))
            {
                cubeData.MoveNext().Should().BeTrue(
                    $"{sourceFile}: expected a cube BgDecisionData for each analysed cube record");
                var analysis = cube.Analysis;
                var decision = cubeData.Current.Decision;

                double expectedNd = Usable(analysis.EvalNoDouble.Equity);
                double expectedDt = Usable(analysis.EvalDoubleTake.Equity);

                decision.CubelessNoDoubleEquity.Should().Be(expectedNd,
                    $"{sourceFile}: CubelessNoDoubleEquity must come from EvalNoDouble.Equity");
                decision.CubelessDoubleTakeEquity.Should().Be(expectedDt,
                    $"{sourceFile}: CubelessDoubleTakeEquity must come from EvalDoubleTake.Equity");

                if (expectedNd != 0.0) nonZero++;
                if (expectedNd != Usable(analysis.EquityNoDouble)) divergesFromCubeful++;
            }
        }

        nonZero.Should().BeGreaterThan(0,
            "corpus must contain a cube decision with non-zero cubeless equity, " +
            "else this test passes even with the fields left at default 0.0");
        divergesFromCubeful.Should().BeGreaterThan(0,
            "cubeless N/D must differ from cubeful N/D on at least one decision, " +
            "else a mis-wire to the cubeful source would pass undetected");
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