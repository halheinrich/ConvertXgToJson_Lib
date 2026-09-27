using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the single-parser unification for game headers: the fast path
/// (ReadGameHeaders) and the full parse (ReadFile + XgGameInfo.From) must
/// yield identical game metadata for every corpus file.
/// </summary>
[Collection("FileIO")]
public class ReadGameHeadersTests
{
    [Fact]
    public void ReadGameHeaders_MatchesFullParse()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            string name = Path.GetFileName(path);

            var state = new XgIteratorState();
            var fast = XgFileReader.ReadGameHeaders(path, state).ToList();

            var file = XgFileReader.ReadFile(path);
            var terms = XgDecisionIterator.ExtractMatchInfo(file)!.Terms;
            var full = file.Records.OfType<GameHeaderRecord>()
                .Select(gh => XgGameInfo.From(gh, terms))
                .ToList();

            fast.Should().HaveCount(full.Count, $"game-header count in {name}");
            for (int i = 0; i < full.Count; i++)
            {
                fast[i].Standing.Should().Be(full[i].Standing, $"standing game#{i} in {name}");
                fast[i].IsStandardStart.Should().Be(full[i].IsStandardStart, $"StdStart game#{i} in {name}");
            }
        }
    }
}
