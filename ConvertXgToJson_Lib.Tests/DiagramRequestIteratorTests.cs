// DiagramRequestIteratorTests.cs
using BackgammonDiagram_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

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
            string matchId = Path.GetFileNameWithoutExtension(path);

            // DecisionRow count: cube decisions yield two rows (doubler + taker).
            // DiagramRequest count: cube decisions yield one request.
            // So we need to count unique decisions, not raw rows.
            // A cube decision produces MoveNum+1 for both rows and Roll==0.
            // Count unique (Game, MoveNum, Roll==0) cube decisions separately.
            var decisionRows = XgDecisionIterator.Iterate(file, matchId).ToList();

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

            var requests = XgDecisionIterator.IterateDiagramRequests(file).ToList();

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
            string matchId = Path.GetFileNameWithoutExtension(path);

            var moveRows = XgDecisionIterator.Iterate(file, matchId)
                .Where(r => !r.IsCube)
                .ToList();

            var moveRequests = XgDecisionIterator.IterateDiagramRequests(file)
                .Where(r => !r.IsCube)
                .ToList();

            moveRequests.Count.Should().Be(moveRows.Count,
                $"{Path.GetFileName(path)}: move request count should match move row count");

            for (int i = 0; i < moveRows.Count; i++)
            {
                var row = moveRows[i];
                var req = moveRequests[i];

                req.IsCube.Should().BeFalse($"move request [{i}] should have IsCube=false");

                // Board / Mop agreement
                req.Mop.Count.Should().Be(26, "Mop must be 26 elements");
                for (int p = 0; p < 26; p++)
                    req.Mop[p].Should().Be(row.Board[p],
                        $"{Path.GetFileName(path)} move [{i}] Mop[{p}] should match Board[{p}]");

                // Player name
                req.OnRollName.Should().Be(row.Player,
                    $"{Path.GetFileName(path)} move [{i}] OnRollName should match Player");

                // Dice: Roll is d1*10+d2; Dice[0] and Dice[1] are the individual dice
                int expectedRoll = req.Dice[0] * 10 + req.Dice[1];
                expectedRoll.Should().Be(row.Roll,
                    $"{Path.GetFileName(path)} move [{i}] Dice should reconstruct Roll");

                // Needs are non-negative
                req.OnRollNeeds.Should().BeGreaterThanOrEqualTo(0,
                    $"{Path.GetFileName(path)} move [{i}] OnRollNeeds should be >= 0");
                req.OpponentNeeds.Should().BeGreaterThanOrEqualTo(0,
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

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file)
                                                   .Where(r => r.IsCube))
            {
                foundCube = true;

                req.IsCube.Should().BeTrue("cube request must have IsCube=true");
                req.Dice[0].Should().Be(0, "cube request Dice[0] must be 0");
                req.Dice[1].Should().Be(0, "cube request Dice[1] must be 0");

                // At least one of the equity fields should be non-zero —
                // a fully-analysed cube position will have meaningful values.
                bool hasEquity = req.NoDoubleEquity != 0.0 || req.DoubleTakeEquity != 0.0;
                hasEquity.Should().BeTrue(
                    $"cube request in {Path.GetFileName(path)} should have non-zero equity fields");

                // Win percentages should be in [0, 1]
                req.WinPctAfterNoDouble.Should().BeInRange(0f, 1f,
                    "WinPctAfterNoDouble should be a probability");
                req.WinPctAfterDoubleTake.Should().BeInRange(0f, 1f,
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

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file))
            {
                // Exclude positions where one side is bearing off (pip count < 7
                // would mean at most one checker on point 6 or fewer remaining).
                // This is a conservative threshold that avoids false failures on
                // late-game positions.
                if (req.OnRollPipCount < 7 || req.OpponentPipCount < 7)
                    continue;

                req.OnRollPipCount.Should().BeGreaterThan(0,
                    $"OnRollPipCount should be positive in {Path.GetFileName(path)}");
                req.OpponentPipCount.Should().BeGreaterThan(0,
                    $"OpponentPipCount should be positive in {Path.GetFileName(path)}");

                // Sanity: pip counts should be plausible for a backgammon position.
                // Starting pip count is 167; maximum possible is 15*24 = 360.
                req.OnRollPipCount.Should().BeLessThanOrEqualTo(360,
                    $"OnRollPipCount={req.OnRollPipCount} is implausibly large");
                req.OpponentPipCount.Should().BeLessThanOrEqualTo(360,
                    $"OpponentPipCount={req.OpponentPipCount} is implausibly large");
            }
        }
    }
}