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

}