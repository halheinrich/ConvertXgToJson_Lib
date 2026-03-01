using System.Text.Json;
using FluentAssertions;
using ConvertXgToJson_Lib.Json;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for JSON serialization output shape.
/// Verifies camelCase naming, enum string values, $type discriminators,
/// PositionEngine as array, and TDateTime as ISO-8601.
/// </summary>
public class JsonOutputTests
{
    private static XgFile BuildFile() =>
        XgFileReader.ReadStream(XgFileBuilder.BuildMinimalXgFile("Alice", "Bob", 7));

    private static JsonDocument Serialize(XgFile file) =>
        JsonDocument.Parse(XgFileReader.ToJson(file));

    // ------------------------------------------------------------------ //
    //  Top-level structure
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_TopLevelHasHeaderProperty()
    {
        using var doc = Serialize(BuildFile());
        doc.RootElement.TryGetProperty("header", out _).Should().BeTrue();
    }

    [Fact]
    public void Json_TopLevelHasRecordsArray()
    {
        using var doc = Serialize(BuildFile());
        doc.RootElement.TryGetProperty("records", out var records).Should().BeTrue();
        records.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void Json_TopLevelHasRolloutsArray()
    {
        using var doc = Serialize(BuildFile());
        doc.RootElement.TryGetProperty("rollouts", out var rollouts).Should().BeTrue();
        rollouts.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void Json_TopLevelHasCommentsArray()
    {
        using var doc = Serialize(BuildFile());
        doc.RootElement.TryGetProperty("comments", out _).Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    //  Header shape
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_HeaderGameNameIsCamelCase()
    {
        using var doc = Serialize(BuildFile());
        var header = doc.RootElement.GetProperty("header");
        header.TryGetProperty("gameName", out var name).Should().BeTrue();
        name.GetString().Should().Be("Test Game");
    }

    [Fact]
    public void Json_HeaderMagicNumberIsInteger()
    {
        using var doc = Serialize(BuildFile());
        var header = doc.RootElement.GetProperty("header");
        header.TryGetProperty("magicNumber", out var magic).Should().BeTrue();
        magic.ValueKind.Should().Be(JsonValueKind.Number);
    }

    // ------------------------------------------------------------------ //
    //  Record discriminator
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_FirstRecordHasTypeDiscriminator()
    {
        using var doc = Serialize(BuildFile());
        var record = doc.RootElement.GetProperty("records")[0];
        record.TryGetProperty("$type", out var type).Should().BeTrue();
        type.GetString().Should().Be("HeaderMatch");
    }

    [Fact]
    public void Json_LastRecordHasMatchFooterDiscriminator()
    {
        using var doc = Serialize(BuildFile());
        var records = doc.RootElement.GetProperty("records");
        var last = records[records.GetArrayLength() - 1];
        last.GetProperty("$type").GetString().Should().Be("FooterMatch");
    }

    // ------------------------------------------------------------------ //
    //  Enum serialization
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_GameModeIsStringNotNumber()
    {
        using var doc = Serialize(BuildFile());
        var record = doc.RootElement.GetProperty("records")[0];
        record.TryGetProperty("gameMode", out var mode).Should().BeTrue();
        mode.ValueKind.Should().Be(JsonValueKind.String);
        mode.GetString().Should().Be("competition");
    }

    [Fact]
    public void Json_SiteIdIsStringNotNumber()
    {
        using var doc = Serialize(BuildFile());
        var record = doc.RootElement.GetProperty("records")[0];
        record.TryGetProperty("siteId", out var site).Should().BeTrue();
        site.ValueKind.Should().Be(JsonValueKind.String);
    }

    // ------------------------------------------------------------------ //
    //  Player names
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_Player1AnsiNameInRecord()
    {
        using var doc = Serialize(BuildFile());
        var record = doc.RootElement.GetProperty("records")[0];
        record.GetProperty("player1Ansi").GetString().Should().Be("Alice");
    }

    [Fact]
    public void Json_Player1UnicodeNameInRecord()
    {
        using var doc = Serialize(BuildFile());
        var record = doc.RootElement.GetProperty("records")[0];
        record.GetProperty("player1").GetString().Should().Be("Alice");
    }

    // ------------------------------------------------------------------ //
    //  Date serialization
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_DateFieldIsIso8601String()
    {
        using var doc = Serialize(BuildFile());
        var record = doc.RootElement.GetProperty("records")[0];
        record.TryGetProperty("date", out var date).Should().BeTrue();
        date.ValueKind.Should().Be(JsonValueKind.String);
        // Should be parseable as a DateTime
        DateTime.TryParse(date.GetString(), out _).Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    //  PositionEngine as compact array
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_PositionEngineIsArray()
    {
        // Build a file with a game header (which contains a PositionEngine)
        var date = new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc);
        using var doc = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new GameHeaderRecord { EntryType = RecordType.HeaderGame, InitialPosition = new PositionEngine() },
                XgJsonOptions.Default));

        doc.RootElement.TryGetProperty("initialPosition", out var pos).Should().BeTrue();
        pos.ValueKind.Should().Be(JsonValueKind.Array);
        pos.GetArrayLength().Should().Be(26);
    }

    [Fact]
    public void Json_PositionEngineValuesAreIntegers()
    {
        var pos = new PositionEngine { Points = new sbyte[26] };
        pos.Points[1]  = 2;   // 2 checkers on the 1-point
        pos.Points[24] = -2;  // opponent has 2 on the 24-point

        var json = JsonSerializer.Serialize(pos, XgJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement[1].GetSByte().Should().Be(2);
        doc.RootElement[24].GetSByte().Should().Be(-2);
    }

    // ------------------------------------------------------------------ //
    //  Round-trip: ToJson → parse → verify key values
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_MatchLengthRoundTrips()
    {
        using var doc = Serialize(BuildFile());
        doc.RootElement.GetProperty("records")[0]
            .GetProperty("matchLength").GetInt32().Should().Be(7);
    }

    [Fact]
    public void Json_CrawfordRoundTrips()
    {
        using var doc = Serialize(BuildFile());
        doc.RootElement.GetProperty("records")[0]
            .GetProperty("crawford").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Json_Elo1RoundTrips()
    {
        using var doc = Serialize(BuildFile());
        doc.RootElement.GetProperty("records")[0]
            .GetProperty("elo1").GetDouble().Should().BeApproximately(1500.0, 1e-6);
    }

    // ------------------------------------------------------------------ //
    //  Custom options
    // ------------------------------------------------------------------ //

    [Fact]
    public void Json_CustomOptionsAreRespected()
    {
        var compact = new JsonSerializerOptions
        {
            WriteIndented = false,
            Converters    = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var file = BuildFile();
        var json = XgFileReader.ToJson(file, compact);
        // Compact output should not contain newlines
        json.Should().NotContain("\n");
    }
}
