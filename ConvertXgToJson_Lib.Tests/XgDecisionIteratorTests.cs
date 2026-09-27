using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;
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
        // The corpus churns and single-game matches are valid corpus members,
        // so scan for the first file whose shape can exercise both assertions:
        // a game with more than one decision, plus decisions in at least one
        // other game. Tolerate a corpus with no such file (or no files at all).
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            var allRows = XgDecisionIterator.Iterate(file, sourceFile).ToList();
            var byGame = allRows.GroupBy(r => r.Game).ToList();

            // Need >1 decision in some game AND decisions in another game.
            int targetGame = byGame
                .Where(g => g.Count() > 1)
                .Select(g => g.Key!.Value)
                .FirstOrDefault(-1);
            if (targetGame == -1 || byGame.Count < 2)
                continue;

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
            return;
        }
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
    /// GameInfo is populated on state before the first row of each game is
    /// yielded, its standing of the match's kind.
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
            state.GameInfo!.Standing.Kind.Should().Be(state.MatchInfo!.Terms.Kind,
                "a game's standing is of its match's kind");
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

        int skipGame = gamesWithRows.First().Key!.Value;

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
    /// A match game's standing states each player's away score — the match's
    /// length less the score at the game's start. Verified against the first
    /// game of a match file, where the scores start at 0.
    /// </summary>
    [Fact]
    public void GameInfo_AwayScores_CorrectForFirstGame()
    {
        var path = TestPaths.XgFiles.First();
        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        // Get match length from the file
        if (XgDecisionIterator.ExtractMatchInfo(file)!.Terms is not MatchTerms terms)
            return; // money session — skip

        var state = new XgIteratorState();

        foreach (var row in XgDecisionIterator.Iterate(file, sourceFile, state))
        {
            // First game of a match always starts 0-0
            var standing = state.GameInfo!.Standing.Should().BeOfType<MatchStanding>().Subject;
            standing.Away1.Should().Be(terms.Length, "first game starts at score 0, so away = the length");
            standing.Away2.Should().Be(terms.Length, "first game starts at score 0, so away = the length");
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
                if (row.MatchLength is not int matchLength) continue; // money — no away scores

                // XGID format: XGID=<pos>:<cv>:<cp>:<turn>:<dice>:<score1>:<score2>:<cj>:<ml>:<maxcube>
                var parts = row.Xgid.Split(':');
                int xgidScore1 = int.Parse(parts[5]); // on-roll player's score
                int xgidScore2 = int.Parse(parts[6]); // opponent's score

                // MatchScore format: "{away1}a{away2}a[C]"
                var scoreParts = row.MatchScore.TrimEnd('C').Split('a', StringSplitOptions.RemoveEmptyEntries);
                int msAway1 = int.Parse(scoreParts[0]);
                int msAway2 = int.Parse(scoreParts[1]);

                int expectedAway1 = matchLength - xgidScore1;
                int expectedAway2 = matchLength - xgidScore2;

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
                req.SourceFile.Should().Be(expected,
                    $"every BgDecisionData from {expected} must carry that filename");
            }
        }
    }

    /// <summary>
    /// A cube row carries no play, so it states no after-board: both are
    /// <see langword="null"/>, the other kind's columns empty — board-based
    /// play-type filters rely on this to skip cube rows.
    /// </summary>
    [Fact]
    public void CubeDecisionRows_AfterBoardsAreEmpty()
    {
        bool foundCube = false;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, Path.GetFileName(path))
                                                   .Where(r => r.Kind == DecisionKind.Cube))
            {
                foundCube = true;
                row.AfterBestBoard.Should().BeNull(
                    $"cube DecisionRow in {Path.GetFileName(path)} states no AfterBestBoard");
                row.AfterPlayerBoard.Should().BeNull(
                    $"cube DecisionRow in {Path.GetFileName(path)} states no AfterPlayerBoard");
            }
        }

        foundCube.Should().BeTrue("test data should contain at least one cube decision");
    }

    // -----------------------------------------------------------------------
    //  Candidates in XG's order; the row's equity is its ranking's best
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>match35253054.xg</c> contains decisions where XG's native rank 0 is
    /// not the highest-equity candidate. A record keeps XG's order — its first
    /// candidate is XG's rank 0, never re-sorted by equity — and which play is
    /// best is a ranking's: the row built for the default ranking reports the
    /// highest equity in the candidate set. Pairs each checker-play record
    /// with its XG move record and its row, by id.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void CheckerPlay_CandidatesKeepXgsOrder_AndTheRowsEquityIsTheRankingsBest()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35253054.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on match35253054.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);
        var rowsById = XgDecisionIterator.Iterate(file, sourceFile).ToDictionary(r => r.Id);
        var moves = new Dictionary<(int, int), MoveRecord>();
        var context = new MatchContext(file.Records, file.Comments);
        foreach (var record in file.Records)
        {
            context.Update(record);
            if (record is MoveRecord move)
                moves[(context.GameNumber, context.MoveNumber)] = move;
        }

        int divergingDecisions = 0;
        foreach (var play in XgDecisionIterator.IterateDiagramRequests(file, sourceFile).OfType<CheckerPlayDecision>())
        {
            var evals = moves[(play.Game!.Value, play.MoveNumber!.Value)].Analysis.Evals;
            play.Decision.Plays.Select(c => c.Equity).Should().Equal(
                evals.Take(play.Decision.Plays.Count).Select(e => (double)e.Equity),
                $"{play.Id}: the candidates keep XG's order");

            double maxEquity = play.Decision.Plays.Max(c => c.Equity);
            rowsById[play.Id].Equity.Should().Be(maxEquity,
                $"{play.Id}: the default ranking's best is the highest equity");
            if (play.Decision.Plays[0].Equity < maxEquity)
                divergingDecisions++;
        }

        divergingDecisions.Should().BeGreaterThan(0,
            $"{sourceFile} must contain at least one decision where XG-native rank 0 " +
            "is not the highest equity; otherwise this test passes vacuously.");
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
        rows.Should().OnlyContain(r => r.IsStandardStart == true,
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
            var standard = new PositionEngine { Points = XgGameBuilder.PointsOf(BoardPosition.Standard) };
            var game = new GameHeaderRecord { InitialPosition = standard };
            var cube = new CubeRecord
            {
                ActivePlayer = 1,
                CubeValue = 0,
                Position = standard,
                Analysis = new DoubleActionAnalysis { Level = 1 },
                Flagged = flagged,
                CommentIndex = commentIndex,
            };
            var file = new XgFile
            {
                Records = new List<SaveRecord> { header, game, cube },
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

        // XG's "no comment" sentinel (-1) is none, NOT Comments[0].
        Build(flagged: false, commentIndex: -1).Descriptive.Comment.Should().BeNull(
            "CommentIndex -1 is XG's no-comment sentinel and must not alias Comments[0]");

        // An out-of-range index is bounds-guarded to none.
        Build(flagged: false, commentIndex: 99).Descriptive.Comment.Should().BeNull(
            "an out-of-range CommentIndex must be bounds-guarded to none");
    }

    // -----------------------------------------------------------------------
    //  XG's errors: stored only where the record states no move to derive one
    // -----------------------------------------------------------------------

    /// <summary>
    /// A match header, a game header at the standard start, and
    /// <paramref name="decision"/> — records written by hand, since the
    /// builder records no error for the shapes these tests need.
    /// </summary>
    private static XgFile OneDecision(SaveRecord decision)
    {
        var standard = new PositionEngine { Points = XgGameBuilder.PointsOf(BoardPosition.Standard) };
        return new XgFile
        {
            Records =
            [
                new MatchHeaderRecord { MatchLength = 7, Player1 = "Alice", Player2 = "Bob" },
                new GameHeaderRecord { InitialPosition = standard },
                decision,
            ],
        };
    }

    /// <summary>
    /// A played move XG did not list is stated by its error alone: the record
    /// has no played candidate to derive one from, so XG's error is stored as
    /// the unlisted play's (its magnitude), and the player's result is that
    /// error under any ranking.
    /// </summary>
    [Fact]
    public void CheckerPlay_UnlistedPlayedMove_StoresXgsErrorForIt()
    {
        var standard = new PositionEngine { Points = XgGameBuilder.PointsOf(BoardPosition.Standard) };
        var elsewhere = new PositionEngine { Points = XgGameBuilder.PointsOf(BoardPosition.Nackgammon) };
        var move = new MoveRecord
        {
            InitialPosition = standard,
            FinalPosition = elsewhere,                  // matches no candidate's resulting position
            ActivePlayer = 1,
            Dice = [3, 1],
            MoveError = -0.125,
            Analysis = new BestMoveAnalysis
            {
                MoveCount = 1,
                Evals = [new EvalResult { Equity = 0.16f }],
                Moves = [[7, 4, 5, 4, -1, -1, -1, -1]],   // 8/5 6/5
                EvalLevels = [new EvalLevel { Level = 2 }],
                PositionsPlayed = [standard],
            },
            RolloutIndices = [.. Enumerable.Repeat(-1, 32)],
        };

        var play = XgDecisionIterator.IterateDiagramRequests(OneDecision(move), "synthetic.xg")
            .Should().ContainSingle().Which.Should().BeOfType<CheckerPlayDecision>().Subject;

        play.Decision.UserPlayIndex.Should().BeNull();
        play.Decision.UnlistedPlayError.Should().Be(0.125);
        play.Decision.RankedBy(PlayRanking.DepthFirst).PlayerResult.Should().Be(PlayerResult.Unstated(0.125));
    }

    /// <summary>
    /// A cube half whose played action the record does not state keeps XG's
    /// error for it: XG's resignation pane records no doubler action, so its
    /// doubling error, where XG scored one, is stored as the unstated half's.
    /// </summary>
    [Fact]
    public void CubeDecision_UnstatedHalf_StoresXgsErrorForIt()
    {
        var standard = new PositionEngine { Points = XgGameBuilder.PointsOf(BoardPosition.Standard) };
        var cube = new CubeRecord
        {
            ActivePlayer = 1,
            Doubled = -1,                               // no cube action recorded
            Taken = -1,
            Position = standard,
            Analysis = new DoubleActionAnalysis { Level = 1, EquityNoDouble = 0.2f, EquityDoubleTake = 0.3f },
            ErrorCube = -0.0625,
            ErrorTake = -1000.0,
            RolloutIndex = -1,
            CommentIndex = -1,
        };

        var decision = XgDecisionIterator.IterateDiagramRequests(OneDecision(cube), "synthetic.xg")
            .Should().ContainSingle().Which.Should().BeOfType<CubeDecision>().Subject.Decision;

        decision.UserDoublerAction.Should().BeNull();
        decision.UnstatedDoublerActionError.Should().Be(0.0625);
        decision.UserDoubleError.Should().Be(0.0625);
        decision.UnstatedTakerActionError.Should().BeNull("no double was offered, so no taker decision exists");
    }

    // -----------------------------------------------------------------------
    //  Cubeless equities — cube DecisionData population
    // -----------------------------------------------------------------------

    /// <summary>
    /// On cube decisions, <see cref="CubeDecisionData.CubelessNoDoubleEquity"/>
    /// and <see cref="CubeDecisionData.CubelessDoubleTakeEquity"/> are XG's
    /// cubeless evals (<c>EvalNoDouble.Equity</c> / <c>EvalDoubleTake.Equity</c>),
    /// verbatim — no stand-in replaces a value. Pairs raw cube records with diagram requests
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
        int nonZero = 0;
        int divergesFromCubeful = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            string sourceFile = Path.GetFileName(path);
            var file = XgFileReader.ReadFile(path);

            using var cubeData = XgDecisionIterator
                .IterateDiagramRequests(file, sourceFile)
                .OfType<CubeDecision>()
                .GetEnumerator();

            foreach (var cube in EmissionMirror.CubeDecisions(file, sourceFile))
            {
                cubeData.MoveNext().Should().BeTrue(
                    $"{sourceFile}: expected a cube BgDecisionData for each analysed cube record");
                var analysis = cube.Analysis;
                var decision = cubeData.Current.Decision;

                double expectedNd = analysis.EvalNoDouble.Equity;
                double expectedDt = analysis.EvalDoubleTake.Equity;

                decision.CubelessNoDoubleEquity.Should().Be(expectedNd,
                    $"{sourceFile}: CubelessNoDoubleEquity must come from EvalNoDouble.Equity");
                decision.CubelessDoubleTakeEquity.Should().Be(expectedDt,
                    $"{sourceFile}: CubelessDoubleTakeEquity must come from EvalDoubleTake.Equity");

                if (expectedNd != 0.0) nonZero++;
                if (expectedNd != analysis.EquityNoDouble) divergesFromCubeful++;
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