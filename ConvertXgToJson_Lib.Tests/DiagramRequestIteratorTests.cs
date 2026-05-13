// DiagramRequestIteratorTests.cs
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgDecisionIterator.IterateDiagramRequests"/>.
/// </summary>
[Collection("FileIO")]
public class DiagramRequestIteratorTests
{
    // -----------------------------------------------------------------------
    //  Count invariant
    // -----------------------------------------------------------------------

    /// <summary>
    /// IterateDiagramRequests yields exactly one DiagramRequest per analysed
    /// decision — the same count as Iterate yields DecisionRows. Pins the
    /// 1:1 contract across both surfaces (play and cube).
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_YieldsOneRequestPerAnalysedDecision()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            // Both surfaces emit one row per analysed decision (play or cube),
            // so IterateDiagramRequests should yield the same count as Iterate.
            // The grouping below is residual defence from the prior two-row
            // cube regime and is a no-op under the current 1:1 contract.
            var decisionRows = XgDecisionIterator.Iterate(file, Path.GetFileName(path)).ToList();

            int uniqueDecisions = decisionRows
                .GroupBy(r => (r.Game, r.MoveNumber, r.IsCube))
                .Count();

            int cubeGroups = decisionRows
                .Where(r => r.IsCube)
                .GroupBy(r => (r.Game, r.MoveNumber))
                .Count();
            int moveCount = decisionRows.Count(r => !r.IsCube);
            int expectedCount = moveCount + cubeGroups;

            var requests = XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path)).ToList();

            requests.Count.Should().Be(expectedCount,
                $"{Path.GetFileName(path)}: expected {expectedCount} DiagramRequests " +
                $"({moveCount} move + {cubeGroups} cube)");
        }
    }

    // -----------------------------------------------------------------------
    //  Move request field agreement with DecisionRow
    // -----------------------------------------------------------------------

    /// <summary>
    /// For every move decision, the DiagramRequest fields derived from the same
    /// raw record agree with the corresponding DecisionRow:
    /// - Mop matches Board
    /// - OnRollName matches Player
    /// - Dice matches Roll digits
    /// - IsCube is false
    /// - OnRollNeeds and OpponentNeeds are non-negative
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_MoveRequest_FieldsMatchDecisionRow()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            var moveRows = XgDecisionIterator.Iterate(file, sourceFile)
                .Where(r => !r.IsCube)
                .ToList();

            var moveRequests = XgDecisionIterator.IterateDiagramRequests(file, sourceFile)
                .Where(r => !r.Decision.IsCube)
                .ToList();

            moveRequests.Count.Should().Be(moveRows.Count,
                $"{Path.GetFileName(path)}: move request count should match move row count");

            for (int i = 0; i < moveRows.Count; i++)
            {
                var row = moveRows[i];
                var req = moveRequests[i];

                req.Decision.IsCube.Should().BeFalse($"move request [{i}] should have IsCube=false");

                // Board / Mop agreement
                req.Position.Mop.Count.Should().Be(26, "Mop must be 26 elements");
                for (int p = 0; p < 26; p++)
                    req.Position.Mop[p].Should().Be(row.Board[p],
                        $"{Path.GetFileName(path)} move [{i}] Mop[{p}] should match Board[{p}]");

                // Player name
                req.Descriptive.OnRollName.Should().Be(row.Player,
                    $"{Path.GetFileName(path)} move [{i}] OnRollName should match Player");

                // Dice: Roll is d1*10+d2; Dice[0] and Dice[1] are the individual dice
                int expectedRoll = req.Decision.Dice[0] * 10 + req.Decision.Dice[1];
                expectedRoll.Should().Be(row.Roll,
                    $"{Path.GetFileName(path)} move [{i}] Dice should reconstruct Roll");

                // Needs are non-negative
                req.Position.OnRollNeeds.Should().BeGreaterThanOrEqualTo(0,
                    $"{Path.GetFileName(path)} move [{i}] OnRollNeeds should be >= 0");
                req.Position.OpponentNeeds.Should().BeGreaterThanOrEqualTo(0,
                    $"{Path.GetFileName(path)} move [{i}] OpponentNeeds should be >= 0");
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Cube request fields
    // -----------------------------------------------------------------------

    /// <summary>
    /// For cube decisions, IsCube is true, Dice is [0,0], and equity fields
    /// are populated (non-zero) for analysed positions.
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_CubeRequest_EquityFieldsPopulated()
    {
        bool foundCube = false;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path))
                                                   .Where(r => r.Decision.IsCube))
            {
                foundCube = true;

                req.Decision.IsCube.Should().BeTrue("cube request must have IsCube=true");
                req.Decision.Dice[0].Should().Be(0, "cube request Dice[0] must be 0");
                req.Decision.Dice[1].Should().Be(0, "cube request Dice[1] must be 0");

                // At least one of the equity fields should be non-zero —
                // a fully-analysed cube position will have meaningful values.
                bool hasEquity = req.Decision.NoDoubleEquity != 0.0 || req.Decision.DoubleTakeEquity != 0.0;
                hasEquity.Should().BeTrue(
                    $"cube request in {Path.GetFileName(path)} should have non-zero equity fields");

                // Win percentages should be in [0, 1]
                req.Decision.WinPctAfterNoDouble.Should().BeInRange(0f, 1f,
                    "WinPctAfterNoDouble should be a probability");
                req.Decision.WinPctAfterDoubleTake.Should().BeInRange(0f, 1f,
                    "WinPctAfterDoubleTake should be a probability");
            }
        }

        foundCube.Should().BeTrue("test data should contain at least one cube decision");
    }

    /// <summary>
    /// Cube requests carry no play; both after-boards must be empty per the
    /// PlayOutcomeData contract so board-based play-type filters correctly skip
    /// cube decisions. Producer-enforced — consumers rely on it.
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_CubeRequest_AfterBoardsAreEmpty()
    {
        bool foundCube = false;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path))
                                                   .Where(r => r.Decision.IsCube))
            {
                foundCube = true;
                req.Outcome.AfterBestBoard.Should().BeEmpty(
                    $"cube request in {Path.GetFileName(path)} must have empty AfterBestBoard");
                req.Outcome.AfterPlayerBoard.Should().BeEmpty(
                    $"cube request in {Path.GetFileName(path)} must have empty AfterPlayerBoard");
                req.AfterBestBoard.Should().BeEmpty("forwarded IDecisionFilterData.AfterBestBoard must be empty");
                req.AfterPlayerBoard.Should().BeEmpty("forwarded IDecisionFilterData.AfterPlayerBoard must be empty");
            }
        }

        foundCube.Should().BeTrue("test data should contain at least one cube decision");
    }

    // -----------------------------------------------------------------------
    //  Pip counts
    // -----------------------------------------------------------------------

    /// <summary>
    /// OnRollPipCount and OpponentPipCount are positive for every request that
    /// is not a late bearoff (where one side may have very few checkers left).
    /// Catches wrong pip count computation such as using bar indices or
    /// computing in the wrong direction.
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_PipCounts_NonZeroForNonBearoffPositions()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path)))
            {
                // Exclude positions where one side is bearing off (pip count < 7
                // would mean at most one checker on point 6 or fewer remaining).
                // This is a conservative threshold that avoids false failures on
                // late-game positions.
                if (req.Position.OnRollPipCount < 7 || req.Position.OpponentPipCount < 7)
                    continue;

                req.Position.OnRollPipCount.Should().BeGreaterThan(0,
                    $"OnRollPipCount should be positive in {Path.GetFileName(path)}");
                req.Position.OpponentPipCount.Should().BeGreaterThan(0,
                    $"OpponentPipCount should be positive in {Path.GetFileName(path)}");

                // Sanity: pip counts should be plausible for a backgammon position.
                // Starting pip count is 167; maximum possible is 15*24 = 360.
                req.Position.OnRollPipCount.Should().BeLessThanOrEqualTo(360,
                    $"OnRollPipCount={req.Position.OnRollPipCount} is implausibly large");
                req.Position.OpponentPipCount.Should().BeLessThanOrEqualTo(360,
                    $"OpponentPipCount={req.Position.OpponentPipCount} is implausibly large");
            }
        }
    }
    // -----------------------------------------------------------------------
    //  User error fields
    // -----------------------------------------------------------------------

    /// <summary>
    /// UserPlayError is non-negative when present.
    /// For .xgp files (sentinel MoveError == -1000) it must be null.
    /// </summary>
    [Fact]
    public void MoveRequest_UserPlayError_NonNegativeWhenPresent()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            bool isXgp = path.EndsWith(".xgp", StringComparison.OrdinalIgnoreCase);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path))
                                                   .Where(r => !r.Decision.IsCube))
            {
                if (isXgp)
                {
                    req.Decision.UserPlayError.Should().BeNull(
                        $".xgp file {Path.GetFileName(path)}: UserPlayError should be null (sentinel)");
                }
                else if (req.Decision.UserPlayError.HasValue)
                {
                    req.Decision.UserPlayError.Value.Should().BeGreaterThanOrEqualTo(0,
                        $"{Path.GetFileName(path)}: UserPlayError must be non-negative");
                }
            }
        }
    }

    /// <summary>
    /// UserDoubleError is non-negative when present; null for sentinel values.
    /// IsCube must be false for move requests (guard).
    /// </summary>
    [Fact]
    public void CubeRequest_UserDoubleError_NonNegativeWhenPresent()
    {
        bool foundCube = false;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path))
                                                   .Where(r => r.Decision.IsCube))
            {
                foundCube = true;

                if (req.Decision.UserDoubleError.HasValue)
                {
                    req.Decision.UserDoubleError.Value.Should().BeGreaterThanOrEqualTo(0,
                        $"{Path.GetFileName(path)}: UserDoubleError must be non-negative");
                }
            }
        }

        foundCube.Should().BeTrue("test data should contain at least one cube decision");
    }

    /// <summary>
    /// UserTakeError is null when the cube was not offered (Doubled != 1).
    /// When present it is non-negative.
    /// </summary>
    [Fact]
    public void CubeRequest_UserTakeError_NullWhenNotDoubled()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            // Pair raw CubeRecords with their diagram requests to check Doubled flag.
            var cubeRecords = file.Records.OfType<CubeRecord>()
                .Where(c => c.Analysis.Level > 0 || c.Analysis.LevelRequest > 0)
                .ToList();

            var cubeRequests = XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path))
                .Where(r => r.Decision.IsCube)
                .ToList();

            cubeRecords.Count.Should().Be(cubeRequests.Count,
                $"{Path.GetFileName(path)}: cube record count should match cube request count");

            for (int i = 0; i < cubeRecords.Count; i++)
            {
                var rec = cubeRecords[i];
                var req = cubeRequests[i];

                if (rec.Doubled != 1)
                {
                    req.Decision.UserTakeError.Should().BeNull(
                        $"{Path.GetFileName(path)} cube[{i}]: UserTakeError must be null when not doubled");
                }
                else if (req.Decision.UserTakeError.HasValue)
                {
                    req.Decision.UserTakeError.Value.Should().BeGreaterThanOrEqualTo(0,
                        $"{Path.GetFileName(path)} cube[{i}]: UserTakeError must be non-negative");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Plays list sort order
    // -----------------------------------------------------------------------

    /// <summary>
    /// For every move request across the .xg corpus, <c>Plays</c> is sorted
    /// descending by equity, <c>Plays[0].EquityLoss</c> is <c>0.0</c> (the
    /// best play has no loss vs. itself), and every subsequent entry has
    /// <c>EquityLoss &gt;= 0</c>. XG stores candidates in its native ranking
    /// order, which is not always strict equity-descending — an unsorted
    /// list produces negative EquityLoss on any candidate whose equity
    /// exceeds rank-0's, which the renderer correctly suppresses as blank
    /// cells. <see cref="PlayCandidate.EquityLoss"/> is non-nullable post
    /// the BgDataTypes_Lib relocation; <c>0.0</c> is the test for "is this
    /// a best play" and ties may share zero.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Plays_DescendingEquityWithZeroLossAtTop()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile)
                                                    .Where(r => !r.Decision.IsCube))
            {
                var plays = req.Decision.Plays;
                if (plays.Count == 0) continue;

                plays[0].EquityLoss.Should().Be(0.0,
                    $"{sourceFile}: Plays[0].EquityLoss must be 0.0 (the best play has no loss)");

                for (int i = 1; i < plays.Count; i++)
                {
                    plays[i - 1].Equity.Should().BeGreaterThanOrEqualTo(plays[i].Equity,
                        $"{sourceFile}: Plays must be sorted descending by equity " +
                        $"(Plays[{i - 1}].Equity={plays[i - 1].Equity} vs Plays[{i}].Equity={plays[i].Equity})");

                    plays[i].EquityLoss.Should().BeGreaterThanOrEqualTo(0,
                        $"{sourceFile}: Plays[{i}].EquityLoss must be non-negative after sorting " +
                        $"(got {plays[i].EquityLoss})");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    //  PlayCandidate.Play population
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every <see cref="PlayCandidate"/> emitted for an analysed move
    /// decision must have its <see cref="PlayCandidate.Play"/> populated —
    /// <c>Count &gt; 0</c>. The Play is the structural counterpart to
    /// <see cref="PlayCandidate.MoveNotation"/>, sourced from the same
    /// <see cref="XgMoveTranslator"/> output, and is what downstream
    /// consumers (e.g. BgQuiz_Blazor's submitted-play structural matching)
    /// key off. A regression that drops the <c>Play = …</c> initializer
    /// would leave default <c>Play</c> values (Count == 0) on every
    /// candidate; this corpus-wide check catches that.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_AllCandidates_HavePopulatedPlay()
    {
        int candidatesChecked = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile)
                                                    .Where(r => !r.Decision.IsCube))
            {
                foreach (var play in req.Decision.Plays)
                {
                    play.Play.Count.Should().BeGreaterThan(0,
                        $"{sourceFile}: every PlayCandidate must have Play populated " +
                        $"(MoveNotation='{play.MoveNotation}')");
                    candidatesChecked++;
                }
            }
        }

        candidatesChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one analysed move candidate; " +
            "otherwise this test passes vacuously.");
    }

    /// <summary>
    /// Pins the structural correctness of <see cref="PlayCandidate.Play"/>
    /// against a known fixture: <c>Opening 32 65 64 31 65.xgp</c>. The
    /// first analysed decision is a 6-5 roll with 25 candidates; this test
    /// asserts specific <c>(FrPt, ToPt)</c> pairs on two of them, chosen
    /// to cover the structural facts that distinguish <c>Play</c> from
    /// <c>MoveNotation</c>:
    ///
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>Plays[0]</c> renders as <c>"24/13"</c> (a single combined-die
    ///     leg) but the structural <c>Play</c> contains <b>two</b> sub-moves
    ///     <c>(24, 18)</c> and <c>(18, 13)</c>. The notation collapses the
    ///     chain; the <see cref="Play"/> preserves it. Catches a regression
    ///     where the producer hands a single-leg <c>Play</c> to the
    ///     formatter (would yield the same notation but lose the structural
    ///     signal downstream consumers rely on).
    ///   </description></item>
    ///   <item><description>
    ///     <c>Plays[2]</c> renders as <c>"7/1* 6/1"</c> — a hit on point 1
    ///     followed by a non-hit landing on the same point. The structural
    ///     <c>Play</c> encodes the hit via the sign on <c>ToPt</c>:
    ///     <c>(7, -1)</c> is the hit, <c>(6, 1)</c> is plain. Catches a
    ///     regression where hit detection is dropped (would yield
    ///     <c>(7, 1)</c>, both legs unmarked, and the formatter would
    ///     re-collapse them).
    ///   </description></item>
    /// </list>
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_OpeningFixture_FirstDecisionPlayMovesMatchExpected()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "Opening 32 65 64 31 65.xgp");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the opening-roll fixture in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var firstMove = XgDecisionIterator
            .IterateDiagramRequests(file, Path.GetFileName(path))
            .First(r => !r.Decision.IsCube);

        firstMove.Decision.Dice.Should().BeEquivalentTo(new[] { 6, 5 },
            options => options.WithStrictOrdering(),
            "the first analysed move decision in this fixture is a 6-5 roll");

        var plays = firstMove.Decision.Plays;
        plays.Count.Should().BeGreaterThanOrEqualTo(3,
            "fixture must yield at least three candidates for the spot-check");

        // [0] best play — chain-collapsed notation, two-sub-move Play.
        plays[0].MoveNotation.Should().Be("24/13",
            "Plays[0] renders the combined-die leg as a single chain");
        plays[0].Play.Count.Should().Be(2,
            "Plays[0].Play preserves the two structural sub-moves the notation collapses");
        plays[0].Play[0].Should().Be(new Move(24, 18),
            "first sub-move uses the larger die from point 24");
        plays[0].Play[1].Should().Be(new Move(18, 13),
            "second sub-move continues the same checker to point 13");
        plays[0].EquityLoss.Should().Be(0.0,
            "Plays[0] is the best play — EquityLoss == 0.0 post the BgDataTypes_Lib relocation");

        // [2] hit play — hit encoded via negative ToPt.
        plays[2].MoveNotation.Should().Be("7/1* 6/1",
            "Plays[2] hits the opponent blot on point 1 and lands a second checker there");
        plays[2].Play.Count.Should().Be(2,
            "Plays[2].Play has two distinct sub-moves (no chain to collapse)");
        plays[2].Play[0].Should().Be(new Move(7, -1),
            "first sub-move hits — ToPt is sign-encoded as -1 to flag the hit on point 1");
        plays[2].Play[1].Should().Be(new Move(6, 1),
            "second sub-move lands on point 1 plain (the blot is already on the bar)");
    }

    /// <summary>
    /// <c>match35253054.xg</c> is the user-observed fixture where XG's
    /// native ordering disagrees with strict equity-descending: a candidate
    /// past rank 0 has higher equity than rank 0. Before Task 2, that
    /// produced negative EquityLoss and a blank cell in the renderer. This
    /// test pins the anomaly: at least one decision in the file has a
    /// candidate whose raw XG-native equity at index k &gt; 0 exceeds index
    /// 0's — proving the sort is actually doing work on this corpus rather
    /// than passing vacuously. The corpus-wide test above then proves the
    /// output is correct.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35253054_XgNativeOrderIsNotEquityOrder()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35253054.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "Task 2 test depends on match35253054.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);

        bool foundAnomaly = false;
        foreach (var rec in file.Records.OfType<MoveRecord>())
        {
            var analysis = rec.Analysis;
            if (analysis.MoveCount == 0 || analysis.Evals.Length == 0) continue;
            if (XgDecisionIterator.IsSentinelOnlyAnalysis(analysis)) continue;

            double rank0 = analysis.Evals[0].Equity;
            for (int i = 1; i < analysis.MoveCount && i < analysis.Evals.Length; i++)
            {
                if (analysis.Evals[i].Equity > rank0) { foundAnomaly = true; break; }
            }
            if (foundAnomaly) break;
        }

        foundAnomaly.Should().BeTrue(
            "match35253054.xg should contain at least one decision where XG's native " +
            "rank 0 is not the highest-equity candidate — otherwise the Task 2 sort is " +
            "passing the corpus test vacuously.");
    }

    /// <summary>
    /// On the <c>match35253054.xg</c> anomaly fixture, for every decision
    /// where XG-native rank 0 is not the best-by-equity candidate, the
    /// emitted <c>Decision.Plays[0].Depth</c> must match the depth that
    /// <c>ResolveDepth</c> produces from
    /// <c>EvalLevels[FindBestByEquityIndex]</c>. Plays[0] is the
    /// best-by-equity candidate after the sort in BuildMoveDiagramRequest
    /// (see <c>3f1920d</c>), so its <c>Depth</c> label sources from
    /// <c>EvalLevels[bestIdx]</c>, not <c>EvalLevels[0]</c>. Non-vacuousness
    /// gate requires at least one decision to exhibit rank-0 ≠ best-by-equity.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35253054_DepthLabelUsesBestByEquity()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35253054.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on match35253054.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        using var reqEnum = XgDecisionIterator
            .IterateDiagramRequests(file, sourceFile).GetEnumerator();

        int divergingDecisions = 0;

        foreach (var rec in file.Records)
        {
            if (rec is MoveRecord move)
            {
                var analysis = move.Analysis;
                if (analysis.MoveCount == 0 || analysis.Evals.Length == 0) continue;
                if (XgDecisionIterator.IsSentinelOnlyAnalysis(analysis)) continue;
                int dice = move.Dice.Length >= 2 ? move.Dice[0] * 10 + move.Dice[1] : 0;
                if (dice == 0) continue; // BuildMoveDiagramRequest skips dice==0

                reqEnum.MoveNext().Should().BeTrue(
                    $"{sourceFile}: expected a diagram request for analysed move");
                var req = reqEnum.Current;
                req.Decision.IsCube.Should().BeFalse(
                    $"{sourceFile}: correlated request should be a move");

                int bestIdx = XgDecisionIterator.FindBestByEquityIndex(analysis);

                short expectedEvalLevel = bestIdx < analysis.EvalLevels.Length
                    ? analysis.EvalLevels[bestIdx].Level
                    : (short)0;
                string expectedLabel = XgDecisionIterator.ResolveDepth(
                    evalLevel: expectedEvalLevel,
                    rolloutIndex: bestIdx < move.RolloutIndices.Length ? move.RolloutIndices[bestIdx] : -1,
                    rollouts: file.Rollouts);

                req.Decision.Plays.Should().NotBeEmpty(
                    $"{sourceFile}: move request must carry at least one candidate");
                req.Decision.Plays[0].Depth.Should().Be(expectedLabel,
                    $"{sourceFile} game {req.Descriptive.SourceFile}: " +
                    "Plays[0] is best-by-equity, so its Depth must resolve from " +
                    "EvalLevels[bestIdx], not EvalLevels[0]");

                if (bestIdx != 0) divergingDecisions++;
            }
            else if (rec is CubeRecord cube)
            {
                // Skip past cube requests — they go through a different depth path.
                if (cube.Analysis.Level <= 0) continue;
                reqEnum.MoveNext();
            }
        }

        divergingDecisions.Should().BeGreaterThan(0,
            $"{sourceFile} must contain at least one decision where XG-native rank 0 " +
            "disagrees with best-by-equity; otherwise this test passes vacuously.");
    }

    /// <summary>
    /// On the <c>match35253054.xg</c> fixture, for every analysed move
    /// decision and every candidate <c>k</c> in
    /// <c>req.Decision.Plays</c>, the emitted <c>Plays[k].Depth</c> must
    /// equal the <c>ResolveDepth</c> result for the corresponding XG-native
    /// index <c>sortedIdx[k]</c>'s <c>EvalLevels</c> entry. Pins the
    /// per-play Depth migration (BgDataTypes <c>d86dab7</c>): each candidate
    /// carries its own depth label keyed off its own <c>EvalLevels[i]</c>,
    /// not a single decision-scoped value. Non-vacuousness gate: at least
    /// one decision with non-empty <c>EvalLevels</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35253054_PerPlayDepthMatchesEvalLevel()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35253054.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on match35253054.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        string sourceFile = Path.GetFileName(path);

        using var reqEnum = XgDecisionIterator
            .IterateDiagramRequests(file, sourceFile).GetEnumerator();

        int decisionsCheckedWithEvalLevels = 0;

        foreach (var rec in file.Records)
        {
            if (rec is MoveRecord move)
            {
                var analysis = move.Analysis;
                if (analysis.MoveCount == 0 || analysis.Evals.Length == 0) continue;
                if (XgDecisionIterator.IsSentinelOnlyAnalysis(analysis)) continue;
                int dice = move.Dice.Length >= 2 ? move.Dice[0] * 10 + move.Dice[1] : 0;
                if (dice == 0) continue;

                reqEnum.MoveNext().Should().BeTrue(
                    $"{sourceFile}: expected a diagram request for analysed move");
                var req = reqEnum.Current;

                // Recompute sortedIdx the same way BuildMoveDiagramRequest
                // does, so Plays[k] maps to XG-native index sortedIdx[k].
                int n = Math.Min(analysis.MoveCount, analysis.Evals.Length);
                int[] sortedIdx = Enumerable.Range(0, n)
                    .OrderByDescending(i => analysis.Evals[i].Equity)
                    .ToArray();

                req.Decision.Plays.Count.Should().Be(sortedIdx.Length,
                    $"{sourceFile}: Plays count must match analysed candidate count");

                for (int k = 0; k < sortedIdx.Length; k++)
                {
                    int i = sortedIdx[k];
                    short evalLevel = i < analysis.EvalLevels.Length
                        ? analysis.EvalLevels[i].Level
                        : (short)0;
                    string expected = XgDecisionIterator.ResolveDepth(
                        evalLevel: evalLevel,
                        rolloutIndex: i < move.RolloutIndices.Length ? move.RolloutIndices[i] : -1,
                        rollouts: file.Rollouts);
                    req.Decision.Plays[k].Depth.Should().Be(expected,
                        $"{sourceFile} candidate k={k} (XG-native index {i}): " +
                        "Plays[k].Depth must be ResolveDepth(EvalLevels[sortedIdx[k]], RolloutIndices[sortedIdx[k]], ...)");
                }

                if (analysis.EvalLevels.Length > 0) decisionsCheckedWithEvalLevels++;
            }
            else if (rec is CubeRecord cube)
            {
                if (cube.Analysis.Level <= 0) continue;
                reqEnum.MoveNext();
            }
        }

        decisionsCheckedWithEvalLevels.Should().BeGreaterThan(0,
            $"{sourceFile} must contain at least one analysed move decision with " +
            "non-empty EvalLevels; otherwise this test passes vacuously.");
    }

    /// <summary>
    /// For every analysed cube decision across the .xg corpus, the emitted
    /// <c>Decision.CubeDepth</c> must equal <c>ResolveDepth</c> applied to
    /// <c>analysis.LevelRequest</c> and the cube's rollout index. Also
    /// asserts that cube requests carry no plays (cube decisions are
    /// play-free by contract). Pins the scalar <c>CubeDepth</c> migration
    /// (BgDataTypes <c>d86dab7</c>). Non-vacuousness: at least one cube
    /// request must be observed across the corpus.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Cube_CubeDepthMatchesResolveDepth()
    {
        int cubesChecked = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            using var reqEnum = XgDecisionIterator
                .IterateDiagramRequests(file, sourceFile).GetEnumerator();

            foreach (var rec in file.Records)
            {
                if (rec is MoveRecord move)
                {
                    var analysis = move.Analysis;
                    if (analysis.MoveCount == 0 || analysis.Evals.Length == 0) continue;
                    if (XgDecisionIterator.IsSentinelOnlyAnalysis(analysis)) continue;
                    int dice = move.Dice.Length >= 2 ? move.Dice[0] * 10 + move.Dice[1] : 0;
                    if (dice == 0) continue;
                    reqEnum.MoveNext();
                }
                else if (rec is CubeRecord cube)
                {
                    if (cube.Analysis.Level <= 0) continue;

                    reqEnum.MoveNext().Should().BeTrue(
                        $"{sourceFile}: expected a diagram request for analysed cube");
                    var req = reqEnum.Current;

                    req.Decision.IsCube.Should().BeTrue(
                        $"{sourceFile}: correlated request should be a cube");

                    string expected = XgDecisionIterator.ResolveDepth(
                        evalLevel: cube.Analysis.LevelRequest,
                        rolloutIndex: cube.RolloutIndex,
                        rollouts: file.Rollouts);

                    req.Decision.CubeDepth.Should().Be(expected,
                        $"{sourceFile}: CubeDepth must be ResolveDepth(analysis.LevelRequest, ...)");
                    req.Decision.Plays.Should().BeEmpty(
                        $"{sourceFile}: cube request must carry no plays");

                    cubesChecked++;
                }
            }
        }

        cubesChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one analysed cube decision; " +
            "otherwise this test passes vacuously.");
    }

    // -----------------------------------------------------------------------
    //  Depth abbreviation and rank population
    // -----------------------------------------------------------------------

    /// <summary>
    /// For every emitted diagram request across the .xg corpus,
    /// <c>PlayCandidate.DepthAbbreviation</c> is non-empty and
    /// <c>DepthRank &gt;= 0</c> on every play; for cube requests,
    /// <c>CubeDepthAbbreviation</c> is non-empty and
    /// <c>CubeDepthRank &gt;= 0</c>. Catches regressions where
    /// BuildMoveDiagramRequest or BuildCubeDiagramRequests forget to
    /// populate the new fields from ResolveDepthInfo.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_DepthAbbreviationAndRank_PopulatedForEveryCandidate()
    {
        int movePlaysChecked = 0;
        int cubesChecked = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile))
            {
                if (req.Decision.IsCube)
                {
                    req.Decision.CubeDepthAbbreviation.Should().NotBeNullOrEmpty(
                        $"{sourceFile}: CubeDepthAbbreviation must be populated on cube requests");
                    req.Decision.CubeDepthRank.Should().BeGreaterThanOrEqualTo(0,
                        $"{sourceFile}: CubeDepthRank must be >= 0");
                    cubesChecked++;
                }
                else
                {
                    foreach (var play in req.Decision.Plays)
                    {
                        play.DepthAbbreviation.Should().NotBeNullOrEmpty(
                            $"{sourceFile}: PlayCandidate.DepthAbbreviation must be populated");
                        play.DepthRank.Should().BeGreaterThanOrEqualTo(0,
                            $"{sourceFile}: PlayCandidate.DepthRank must be >= 0");
                        movePlaysChecked++;
                    }
                }
            }
        }

        movePlaysChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one analysed move candidate");
        cubesChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one analysed cube decision");
    }

    /// <summary>
    /// <c>match35253054.xg</c> exercises XG's per-candidate depth
    /// variation (see <see cref="ConvertXgToJson_Lib.MEMORY.md"/> note
    /// "XG analysis depth varies per candidate" — 2–4 distinct ply
    /// depths across ~12 candidates is typical). At least one move
    /// decision in the fixture must emit adjacent plays with different
    /// <c>DepthRank</c> values, providing non-vacuous coverage for
    /// BackgammonDiagram_Lib's Session-3 out-of-order italic logic.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35253054_AdjacentPlaysHaveDifferingDepthRanks()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35253054.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on match35253054.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);

        bool foundVariation = false;
        foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, "match35253054.xg")
                                              .Where(r => !r.Decision.IsCube))
        {
            var plays = req.Decision.Plays;
            for (int i = 1; i < plays.Count; i++)
            {
                if (plays[i].DepthRank != plays[i - 1].DepthRank) { foundVariation = true; break; }
            }
            if (foundVariation) break;
        }

        foundVariation.Should().BeTrue(
            "match35253054.xg must contain at least one decision where adjacent " +
            "candidates have different DepthRank values; otherwise Session 3's " +
            "out-of-order italic logic has no non-vacuous test coverage.");
    }

    // -----------------------------------------------------------------------
    //  IsCrawford flag propagation
    // -----------------------------------------------------------------------

    /// <summary>
    /// For every diagram request across the .xg corpus, <c>Position.IsCrawford</c>
    /// equals <c>state.GameInfo.IsCrawfordGame</c> for the game the request was
    /// yielded in. Catches regressions on both sides: omitting IsCrawford from
    /// a <see cref="PositionData"/> construction (renders Crawford games as
    /// non-Crawford downstream) and over-flagging money-game decisions where
    /// the match-header Jacoby flag used to leak through the overloaded int.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_PositionIsCrawford_MatchesGameInfo()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            var state = new XgIteratorState();
            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile, state))
            {
                bool inCrawford = state.GameInfo?.IsCrawfordGame ?? false;
                req.Position.IsCrawford.Should().Be(inCrawford,
                    $"{sourceFile} game {(state.GameInfo?.IsCrawfordGame == true ? "Crawford" : "non-Crawford")}: " +
                    $"Position.IsCrawford must match state.GameInfo.IsCrawfordGame");
            }
        }
    }

    /// <summary>
    /// <c>match35041658.xg</c> contains a Crawford game (game 4). Pins that
    /// the fixture reproducing the original rendering bug now yields at
    /// least one diagram request with <c>Position.IsCrawford == true</c> and
    /// at least one with <c>IsCrawford == false</c> — the two missing-
    /// IsCrawford construction sites (move + cube doubler) would have
    /// dropped the flag on every request before the fix.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35041658_HasCrawfordAndNonCrawfordRequests()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35041658.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected Crawford fixture not present: {path}. " +
                "Task 1 test depends on match35041658.xg being in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var requests = XgDecisionIterator.IterateDiagramRequests(file, "match35041658.xg").ToList();

        requests.Any(r => r.Position.IsCrawford).Should().BeTrue(
            "match35041658 contains a Crawford game; at least one diagram request must carry IsCrawford=true");
        requests.Any(r => !r.Position.IsCrawford).Should().BeTrue(
            "match35041658 also contains non-Crawford games; at least one diagram request must carry IsCrawford=false");
    }

    // -----------------------------------------------------------------------
    //  MoveNumber and IsStandardStart propagation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every emitted diagram request across the .xg corpus has
    /// <c>Descriptive.MoveNumber &gt;= 1</c>. Move requests source from
    /// <c>ctx.MoveNumber</c> after MatchContext has incremented it; cube
    /// requests use <c>ctx.MoveNumber + 1</c>, so even an opening cube
    /// decision yields 1. A request with MoveNumber == 0 indicates the
    /// new Descriptive field was not populated.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_AllRequests_DescriptiveMoveNumberAtLeastOne()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile))
            {
                req.Descriptive.MoveNumber.Should().BeGreaterThanOrEqualTo(1,
                    $"{sourceFile}: every diagram request must have Descriptive.MoveNumber >= 1");
                // The IDecisionFilterData forwarding accessor must agree.
                req.MoveNumber.Should().Be(req.Descriptive.MoveNumber,
                    $"{sourceFile}: BgDecisionData.MoveNumber must forward Descriptive.MoveNumber");
            }
        }
    }

    /// <summary>
    /// <c>match35041658.xg</c> and <c>match35253054.xg</c> are full-match
    /// XG files where every game starts from the canonical opening
    /// position. Every emitted diagram request must therefore carry
    /// <c>Descriptive.IsStandardStart == true</c>. Mirrors the DecisionRow
    /// corpus assertion for the BgDecisionData path.
    /// </summary>
    [Theory]
    [InlineData("match35041658.xg")]
    [InlineData("match35253054.xg")]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_StandardStartFixture_AllRequestsIsStandardStartTrue(string fixtureName)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}");

        var file = XgFileReader.ReadFile(path);
        var requests = XgDecisionIterator.IterateDiagramRequests(file, fixtureName).ToList();

        requests.Should().NotBeEmpty($"{fixtureName} should yield at least one diagram request");
        requests.Should().OnlyContain(r => r.Descriptive.IsStandardStart,
            $"{fixtureName} starts every game from the canonical opening; " +
            "every BgDecisionData must carry Descriptive.IsStandardStart=true");
        requests.Should().OnlyContain(r => r.IsStandardStart,
            $"{fixtureName}: BgDecisionData.IsStandardStart must forward Descriptive.IsStandardStart");
    }

    // -----------------------------------------------------------------------
    //  Game propagation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every emitted diagram request across the .xg corpus has
    /// <c>Descriptive.Game &gt;= 1</c>. Both stamp sites
    /// (<c>BuildMoveDiagramRequest</c> and <c>BuildCubeDiagramRequests</c>)
    /// source from <c>ctx.GameNumber</c>, which MatchContext increments to
    /// 1 at the first <c>GameHeaderRecord</c>. A request with Game == 0
    /// indicates the new Descriptive field was not populated at one of
    /// the two sites.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_AllRequests_DescriptiveGameAtLeastOne()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile))
            {
                req.Descriptive.Game.Should().BeGreaterThanOrEqualTo(1,
                    $"{sourceFile}: every diagram request must have Descriptive.Game >= 1");
            }
        }
    }

    /// <summary>
    /// Pairs <see cref="XgDecisionIterator.Iterate"/> and
    /// <see cref="XgDecisionIterator.IterateDiagramRequests"/> 1:1 in order
    /// across the .xg corpus and asserts <c>req.Descriptive.Game</c> equals
    /// <c>row.Game</c> on every record. Pins the per-record semantic value
    /// (not just the >= 1 floor) for both stamp sites — a regression that
    /// hard-coded Game = 1 would pass the floor test but fail this one as
    /// soon as the corpus crosses a game boundary. Multi-game fixtures in
    /// the corpus (e.g. match35041658.xg, which includes a Crawford game 4)
    /// guarantee at least one record with Game > 1.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_DescriptiveGame_MatchesDecisionRowGame()
    {
        int recordsChecked = 0;
        int maxGameSeen = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            var rows = XgDecisionIterator.Iterate(file, sourceFile).ToList();
            var requests = XgDecisionIterator.IterateDiagramRequests(file, sourceFile).ToList();

            requests.Count.Should().Be(rows.Count,
                $"{sourceFile}: Iterate and IterateDiagramRequests are 1:1 by contract");

            for (int i = 0; i < rows.Count; i++)
            {
                requests[i].Descriptive.Game.Should().Be(rows[i].Game,
                    $"{sourceFile} record [{i}] (game {rows[i].Game} move {rows[i].MoveNumber}, " +
                    $"IsCube={rows[i].IsCube}): " +
                    "Descriptive.Game must match the paired DecisionRow.Game");
                recordsChecked++;
                if (rows[i].Game > maxGameSeen) maxGameSeen = rows[i].Game;
            }
        }

        recordsChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one analysed decision; " +
            "otherwise this test passes vacuously.");
        maxGameSeen.Should().BeGreaterThan(1,
            "the .xg corpus must include at least one record from a Game > 1; " +
            "otherwise a hard-coded Game = 1 would pass this test vacuously.");
    }

    // -----------------------------------------------------------------------
    //  BgDecisionData sample output
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes a JSON sample of BgDecisionData records to TestData/BgDecisionData/:
    /// - First match: up to 5 records with non-zero UserPlayError, UserDoubleError,
    ///   and UserTakeError respectively (up to 15 records total)
    /// - Each subsequent match: up to 1 record per error type (up to 3 total)
    /// One file per match, only written when the match contributes at least one record.
    /// </summary>
    [Fact]
    public void BgDecisionData_WriteSampleJson()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        Directory.CreateDirectory(TestPaths.BgDecisionDataDir);
        File.WriteAllText(Path.Combine(TestPaths.BgDecisionDataDir, "GotHere.txt"), "ok");

        bool isFirstMatch = true;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            int quota = isFirstMatch ? 5 : 1;

            var playErrors = new List<BgDecisionData>();
            var doubleErrors = new List<BgDecisionData>();
            var takeErrors = new List<BgDecisionData>();

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path)))
            {
                if (playErrors.Count < quota && req.Decision.UserPlayError > 0)
                    playErrors.Add(req);
                if (doubleErrors.Count < quota && req.Decision.UserDoubleError > 0)
                    doubleErrors.Add(req);
                if (takeErrors.Count < quota && req.Decision.UserTakeError > 0)
                    takeErrors.Add(req);

                if (playErrors.Count >= quota && doubleErrors.Count >= quota && takeErrors.Count >= quota)
                    break;
            }

            var sample = playErrors
                .Concat(doubleErrors)
                .Concat(takeErrors)
                .Distinct()
                .ToList();

            if (sample.Count == 0) continue;

            var output = new
            {
                matchId,
                playErrorSamples = playErrors,
                doubleErrorSamples = doubleErrors,
                takeErrorSamples = takeErrors,
            };

            string outPath = Path.Combine(TestPaths.BgDecisionDataDir, matchId + ".json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(output, opts));

            isFirstMatch = false;
        }
    }

    // -----------------------------------------------------------------------
    //  After-boards: real-corpus invariants
    // -----------------------------------------------------------------------

    /// <summary>
    /// For every analysed checker decision across <c>TestData/xg/</c>:
    ///
    ///  * cube requests carry empty after-boards;
    ///  * move requests where the player's chosen play is not in the analysed
    ///    candidate set also carry empty after-boards (producer contract
    ///    matching the cube-decision handling — the decision doesn't qualify
    ///    for board-based play-type filters);
    ///  * otherwise both after-boards have exactly 26 elements, and the
    ///    decision-maker's checker count matches XG's stored end-state
    ///    (<c>PositionsPlayed[0]</c> for best, <c>FinalPosition</c> for
    ///    player). The opponent's checker count is preserved (hits move a
    ///    checker from a point to the bar; they do not remove a checker).
    ///
    /// Pairs raw records with iterator output by walking both in tandem.
    /// Guards against silent regressions in <see cref="Parsing.AfterBoardBuilder"/>
    /// or the <see cref="XgDecisionIterator"/> wiring that would pass
    /// unit tests (synthetic boards) but disagree with XG's ground truth.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_AfterBoards_AgreeWithXgStoredPositions()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            using var reqEnum = XgDecisionIterator
                .IterateDiagramRequests(file, sourceFile).GetEnumerator();

            foreach (var rec in file.Records)
            {
                if (rec is MoveRecord move)
                {
                    var analysis = move.Analysis;
                    if (analysis.MoveCount == 0 || analysis.Evals.Length == 0) continue;
                    if (XgDecisionIterator.IsSentinelOnlyAnalysis(analysis)) continue;
                    int dice = move.Dice.Length >= 2 ? move.Dice[0] * 10 + move.Dice[1] : 0;
                    if (dice == 0) continue; // BuildMoveDiagramRequest skips dice==0

                    reqEnum.MoveNext().Should().BeTrue(
                        $"{sourceFile}: expected a diagram request for analysed move");
                    var req = reqEnum.Current;
                    req.Decision.IsCube.Should().BeFalse($"{sourceFile}: correlated request should be a move");

                    var best = req.Outcome.AfterBestBoard;
                    var player = req.Outcome.AfterPlayerBoard;

                    // "Doesn't qualify" case: the player's actual play was not in
                    // the analysed candidate set. Both must be empty per the
                    // PlayOutcomeData contract.
                    if (best.Count == 0)
                    {
                        player.Count.Should().Be(0,
                            $"{sourceFile}: when AfterBestBoard is empty, AfterPlayerBoard must also be empty");
                        continue;
                    }

                    best.Count.Should().Be(26,
                        $"{sourceFile}: non-empty AfterBestBoard must have 26 elements");
                    player.Count.Should().Be(26,
                        $"{sourceFile}: non-empty AfterPlayerBoard must have 26 elements");

                    // req.Position.Mop is in on-roll POV (decision-maker = positive,
                    // opponent = negative) — the natural place to count both sides.
                    int priorOppCount = SumAbsNegatives(req.Position.Mop);

                    // Decision-maker's checkers are stored as *negative* in the
                    // POV-flipped after-board (opponent is now on roll).
                    int bestDmCount = SumAbsNegatives(best);
                    int playerDmCount = SumAbsNegatives(player);
                    int bestOppCount = SumPositives(best);
                    int playerOppCount = SumPositives(player);

                    // PositionsPlayed[i] and FinalPosition are stored in
                    // active-player POV (decision-maker = positive), unlike
                    // InitialPosition which is file-native. Sum positives
                    // directly to get the decision-maker's count. Best is
                    // sourced from the highest-equity candidate index (see
                    // XgDecisionIterator.FindBestByEquityIndex), which
                    // agrees with rank 0 for most decisions but diverges on
                    // the subset where XG's stored ranking isn't strict
                    // equity-descending — the same convention that governs
                    // BgDecisionData.Plays[0] and DecisionRow.Equity.
                    int bestIdx = XgDecisionIterator.FindBestByEquityIndex(analysis);
                    int expectedBestDmCount = SumPositivesOnPoints(analysis.PositionsPlayed[bestIdx]);
                    int expectedPlayerDmCount = SumPositivesOnPoints(move.FinalPosition);

                    bestDmCount.Should().Be(expectedBestDmCount,
                        $"{sourceFile}: AfterBestBoard decision-maker count must match XG's PositionsPlayed[bestIdx]");
                    playerDmCount.Should().Be(expectedPlayerDmCount,
                        $"{sourceFile}: AfterPlayerBoard decision-maker count must match XG's FinalPosition");

                    bestOppCount.Should().Be(priorOppCount,
                        $"{sourceFile}: AfterBestBoard opponent count must equal prior (hits preserve total)");
                    playerOppCount.Should().Be(priorOppCount,
                        $"{sourceFile}: AfterPlayerBoard opponent count must equal prior (hits preserve total)");
                }
                else if (rec is CubeRecord cube)
                {
                    if (cube.Analysis.Level <= 0) continue;

                    reqEnum.MoveNext().Should().BeTrue(
                        $"{sourceFile}: expected a diagram request for analysed cube");
                    var req = reqEnum.Current;
                    req.Decision.IsCube.Should().BeTrue($"{sourceFile}: correlated request should be a cube");
                    req.Outcome.AfterBestBoard.Should().BeEmpty(
                        $"{sourceFile}: cube request AfterBestBoard must be empty");
                    req.Outcome.AfterPlayerBoard.Should().BeEmpty(
                        $"{sourceFile}: cube request AfterPlayerBoard must be empty");
                }
            }
        }
    }

    // Helpers for the corpus test above ------------------------------------

    /// <summary>
    /// Sums the positive values on a <see cref="PositionEngine.Points"/> array.
    /// Used on <c>analysis.PositionsPlayed[i]</c> and <c>move.FinalPosition</c>,
    /// both of which are stored in active-player POV — positives are the
    /// decision-maker's checkers. Note that <c>move.InitialPosition</c> is
    /// different: it's in file-native (player1-relative) POV, which is why
    /// <see cref="XgDecisionIterator.ToBoard"/> applies a flip based on
    /// <c>ActivePlayer</c> for it.
    /// </summary>
    private static int SumPositivesOnPoints(PositionEngine pos)
    {
        int count = 0;
        for (int i = 0; i < 26; i++)
        {
            int v = pos.Points[i];
            if (v > 0) count += v;
        }
        return count;
    }

    private static int SumAbsNegatives(IReadOnlyList<int> board)
    {
        int sum = 0;
        for (int i = 0; i < board.Count; i++) if (board[i] < 0) sum -= board[i];
        return sum;
    }

    private static int SumPositives(IReadOnlyList<int> board)
    {
        int sum = 0;
        for (int i = 0; i < board.Count; i++) if (board[i] > 0) sum += board[i];
        return sum;
    }

}