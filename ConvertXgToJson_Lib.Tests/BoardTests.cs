using ConvertXgToJson_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests that DecisionRow.Board is correctly populated by XgDecisionIterator.
/// </summary>
[Collection("FileIO")]
public class BoardTests
{
    // -----------------------------------------------------------------------
    //  Structural invariants — all files
    // -----------------------------------------------------------------------

    [Fact]
    public void XgFiles_AllDecisions_BoardHas26Elements()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                row.Board.Should().HaveCount(26,
                    $"Board must always have 26 elements [{Path.GetFileName(path)}]");
            }
        }
    }

    [Fact]
    public void XgFiles_AllDecisions_EachSideHasBetween1And15Checkers()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                string loc = $"[{Path.GetFileName(path)} Game {row.Game} Move {row.MoveNum}]";
                int onRoll = row.Board.Where(v => v > 0).Sum();
                int opponent = row.Board.Where(v => v < 0).Sum(Math.Abs);
                onRoll.Should().BeInRange(1, 15, $"player on roll checker count {loc}");
                opponent.Should().BeInRange(1, 15, $"opponent checker count {loc}");
            }
        }
    }

    [Fact]
    public void XgFiles_AllDecisions_Board0NeverPositive()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                row.Board[0].Should().BeLessOrEqualTo(0,
                    $"board[0] is opponent bar — never positive [{Path.GetFileName(path)} " +
                    $"Game {row.Game} Move {row.MoveNum}]");
            }
        }
    }

    [Fact]
    public void XgFiles_AllDecisions_Board25NeverNegative()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                row.Board[25].Should().BeGreaterOrEqualTo(0,
                    $"board[25] is player on roll bar — never negative [{Path.GetFileName(path)} " +
                    $"Game {row.Game} Move {row.MoveNum}]");
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Structural invariants — .xgp files
    // -----------------------------------------------------------------------

    [Fact]
    public void XgpFiles_AllDecisions_EachSideHasBetween1And15Checkers()
    {
        foreach (var path in TestPaths.XgpFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                string loc = $"[{Path.GetFileName(path)} Game {row.Game} Move {row.MoveNum}]";
                int onRoll = row.Board.Where(v => v > 0).Sum();
                int opponent = row.Board.Where(v => v < 0).Sum(Math.Abs);
                onRoll.Should().BeInRange(1, 15, $"player on roll checker count {loc}");
                opponent.Should().BeInRange(1, 15, $"opponent checker count {loc}");
            }
        }
    }

    [Fact]
    public void XgpFiles_AllDecisions_Board0NeverPositive()
    {
        foreach (var path in TestPaths.XgpFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                row.Board[0].Should().BeLessOrEqualTo(0,
                    $"board[0] is opponent bar — never positive [{Path.GetFileName(path)} " +
                    $"Game {row.Game} Move {row.MoveNum}]");
            }
        }
    }

    [Fact]
    public void XgpFiles_AllDecisions_Board25NeverNegative()
    {
        foreach (var path in TestPaths.XgpFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                row.Board[25].Should().BeGreaterOrEqualTo(0,
                    $"board[25] is player on roll bar — never negative [{Path.GetFileName(path)} " +
                    $"Game {row.Game} Move {row.MoveNum}]");
            }
        }
    }
}