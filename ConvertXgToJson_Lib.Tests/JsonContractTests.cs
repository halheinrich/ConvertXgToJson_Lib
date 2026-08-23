using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the JSON <i>document</i> <see cref="XgFileReader.ToJson"/> emits —
/// XgToJson's output contract — against goldens captured from XG-authored
/// fixtures <b>before</b> the record model went internal
/// (halheinrich/backgammon#131). Internal types with public properties and
/// <c>[JsonInclude]</c> on <see cref="XgFile"/>'s collections must serialize
/// byte-for-byte as the public model did; any drift here is a contract
/// change, not a refactor. The two goldens cover every collection:
/// <c>DoubleAnalysis.xgp</c> carries a comment table, <c>TooGoodAndTake.xgp</c>
/// a rollout context.
/// </summary>
[Collection("FileIO")]
public class JsonContractTests
{
    public static TheoryData<string> GoldenFixtures => new()
    {
        "DoubleAnalysis.xgp",
        "TooGoodAndTake.xgp",
    };

    [Theory]
    [MemberData(nameof(GoldenFixtures))]
    public void ToJson_MatchesPreChangeGolden(string fixture)
    {
        var file = XgFileReader.ReadFile(Path.Combine(TestPaths.FixtureFilesDir, fixture));

        string json = Normalize(XgFileReader.ToJson(file));

        json.Should().Be(Normalize(Golden(fixture)),
            "the JSON document shape is XgToJson's output contract and must survive the " +
            "record model going internal unchanged");
    }

    [Theory]
    [MemberData(nameof(GoldenFixtures))]
    public void ReadJson_ReadsPreChangeGolden_AndReserializesIdentically(string fixture)
    {
        string goldenPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{fixture}.json");
        File.WriteAllText(goldenPath, Golden(fixture));
        try
        {
            var file = XgFileReader.ReadJson(goldenPath);

            file.Records.Should().NotBeEmpty("the internal constructor and [JsonInclude] setters must still deserialize");
            Normalize(XgFileReader.ToJson(file)).Should().Be(Normalize(Golden(fixture)),
                "a document written by the public-model era must read and re-emit identically");
        }
        finally
        {
            File.Delete(goldenPath);
        }
    }

    [Theory]
    [MemberData(nameof(GoldenFixtures))]
    public void Golden_ParsesToTheSameModel_AsTheBinaryFixture(string fixture)
    {
        // The golden is not just a string: it must describe the fixture. A
        // regenerated golden from a drifted serializer would pass the string
        // pins above trivially; this ties it back to the binary.
        var fromBinary = XgFileReader.ReadFile(Path.Combine(TestPaths.FixtureFilesDir, fixture));
        using var golden = JsonDocument.Parse(Golden(fixture));

        golden.RootElement.GetProperty("records").GetArrayLength().Should().Be(fromBinary.Records.Count);
        golden.RootElement.GetProperty("rollouts").GetArrayLength().Should().Be(fromBinary.Rollouts.Count);
        golden.RootElement.GetProperty("comments").GetArrayLength().Should().Be(fromBinary.Comments.Count);
        golden.RootElement.GetProperty("records")[0].GetProperty("$type").GetString().Should().Be("HeaderMatch");
    }

    private static string Golden(string fixture)
    {
        var assembly = typeof(JsonContractTests).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Golden.{fixture}.json")
            ?? throw new InvalidOperationException($"Golden resource for {fixture} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Line endings follow the writer's <see cref="Environment.NewLine"/> and
    /// git's text normalization; neither is part of the contract.
    /// </summary>
    private static string Normalize(string json) => json.Replace("\r\n", "\n").TrimEnd();
}
