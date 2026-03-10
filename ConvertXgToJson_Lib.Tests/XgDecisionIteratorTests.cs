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
    /// When a cube decision produces two rows (doubler + taker), the take/drop
    /// row's XGID must encode the RESPONDER's turn, not the doubler's.
    ///
    /// Bug: both rows currently share the same XGID (doubler's turn), so the
    /// take/drop row has the wrong turn field.
    /// </summary>
    [Fact]
    public void CubeTakeRow_Xgid_EncodedFromResponderPerspective()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            var rows = XgDecisionIterator.Iterate(file, matchId).ToList();

            // Find consecutive cube row pairs: same Game+MoveNum, Roll==0, different Player
            for (int i = 0; i < rows.Count - 1; i++)
            {
                var r1 = rows[i];
                var r2 = rows[i + 1];

                bool isPair = r1.IsCube && r2.IsCube
                    && r1.Game == r2.Game
                    && r1.MoveNum == r2.MoveNum
                    && r1.Player != r2.Player;

                if (!isPair) continue;

                // r1 = doubler's row, r2 = take/drop row
                // Extract turn field from each XGID: "XGID=<pos>:cv:cp:TURN:..."
                int turn1 = ExtractTurn(r1.Xgid);
                int turn2 = ExtractTurn(r2.Xgid);

                turn2.Should().NotBe(turn1,
                    $"take/drop row XGID must flip turn relative to doubler row " +
                    $"[{Path.GetFileName(path)} Game {r1.Game} Move {r1.MoveNum}]");
            }
        }
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