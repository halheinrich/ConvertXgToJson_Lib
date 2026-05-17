using ConvertXgToJson_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the single-parser unification: the fast path (ReadMatchInfo) and the
/// full parse (ReadFile + ExtractMatchInfo) must yield identical match
/// metadata for every corpus file.
/// </summary>
[Collection("FileIO")]
public class ReadMatchInfoTests
{
    [Fact]
    public void ReadMatchInfo_MatchesFullParse()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string name = Path.GetFileName(path);

            var fast = XgFileReader.ReadMatchInfo(path);
            var full = XgDecisionIterator.ExtractMatchInfo(XgFileReader.ReadFile(path));

            fast.Should().NotBeNull($"ReadMatchInfo should read {name}");
            full.Should().NotBeNull($"ExtractMatchInfo should read {name}");
            fast!.Player1.Should().Be(full!.Player1, $"Player1 mismatch in {name}");
            fast.Player2.Should().Be(full.Player2, $"Player2 mismatch in {name}");
            fast.MatchLength.Should().Be(full.MatchLength, $"MatchLength mismatch in {name}");
        }
    }
}
