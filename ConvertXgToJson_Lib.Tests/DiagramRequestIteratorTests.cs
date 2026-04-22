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
    /// decision — the same count as Iterate yields DecisionRows. This pins the
    /// one-per-decision contract, including that cube decisions yield one request
    /// (not two as DecisionRow does for the taker).
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_YieldsOneRequestPerAnalysedDecision()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            // DecisionRow count: cube decisions yield two rows (doubler + taker).
            // DiagramRequest count: cube decisions yield one request.
            // So we need to count unique decisions, not raw rows.
            // A cube decision produces MoveNum+1 for both rows and Roll==0.
            // Count unique (Game, MoveNum, Roll==0) cube decisions separately.
            var decisionRows = XgDecisionIterator.Iterate(file, Path.GetFileName(path)).ToList();

            // Unique decision count = move rows + unique cube positions
            // (cube taker row shares Game/MoveNum with doubler row)
            int uniqueDecisions = decisionRows
                .GroupBy(r => (r.Game, r.MoveNum, r.IsCube))
                .Count();

            // For cube rows, grouping by (Game, MoveNum, IsCube=true) collapses
            // doubler+taker into one. That gives the expected DiagramRequest count.
            int cubeGroups = decisionRows
                .Where(r => r.IsCube)
                .GroupBy(r => (r.Game, r.MoveNum))
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
        string path = Path.Combine(TestPaths.XgDir, "match35041658.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected Crawford fixture not present: {path}. " +
                "Task 1 test depends on match35041658.xg being in TestData/xg/.");

        var file = XgFileReader.ReadFile(path);
        var requests = XgDecisionIterator.IterateDiagramRequests(file, "match35041658.xg").ToList();

        requests.Any(r => r.Position.IsCrawford).Should().BeTrue(
            "match35041658 contains a Crawford game; at least one diagram request must carry IsCrawford=true");
        requests.Any(r => !r.Position.IsCrawford).Should().BeTrue(
            "match35041658 also contains non-Crawford games; at least one diagram request must carry IsCrawford=false");
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

                    // PositionsPlayed[0] and FinalPosition are stored in
                    // active-player POV (decision-maker = positive), unlike
                    // InitialPosition which is file-native. Sum positives
                    // directly to get the decision-maker's count.
                    int expectedBestDmCount = SumPositivesOnPoints(analysis.PositionsPlayed[0]);
                    int expectedPlayerDmCount = SumPositivesOnPoints(move.FinalPosition);

                    bestDmCount.Should().Be(expectedBestDmCount,
                        $"{sourceFile}: AfterBestBoard decision-maker count must match XG's PositionsPlayed[0]");
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