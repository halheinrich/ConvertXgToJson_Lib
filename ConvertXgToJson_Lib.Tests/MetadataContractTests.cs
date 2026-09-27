using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Json;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins this repo's side of the shared consumer contracts:
/// <see cref="XgMatchInfo"/> satisfies <see cref="IMatchInfo"/> and
/// <see cref="XgGameInfo"/> satisfies <see cref="IGameInfo"/>, stating money
/// versus match as the kind of their <see cref="XgMatchInfo.Terms"/> and
/// <see cref="XgGameInfo.Standing"/> (halheinrich/backgammon#273) — read from
/// XG's headers at the parse boundary, XG's money sentinel as money terms and
/// never as a length, and the cube limit as money's alone. Both types are
/// serialized through this library's options, so the wire half pins
/// BgDataTypes_Lib's absence rule for every member on both paths.
/// </summary>
public class MetadataContractTests
{
    private static MatchHeaderRecord Header(int matchLength, int cubeLimit = XgMatchInfo.DefaultCubeLimitExponent,
        bool jacoby = false, bool beaver = false) => new()
    {
        Player1 = "Alice",
        Player2 = "Bob",
        MatchLength = matchLength,
        CubeLimit = cubeLimit,
        Jacoby = jacoby,
        Beaver = beaver,
    };

    private static GameHeaderRecord GameHeader(int score1, int score2, bool crawford = false, sbyte[]? initial = null) => new()
    {
        Score1 = score1,
        Score2 = score2,
        CrawfordApplies = crawford,
        InitialPosition = new PositionEngine { Points = initial ?? XgGameBuilder.PointsOf(BoardPosition.Standard) },
    };

    // -----------------------------------------------------------------------
    //  The match's terms, read at the parse boundary
    // -----------------------------------------------------------------------

    [Fact]
    public void From_MatchHeader_StatesTheMatchsLength()
    {
        IMatchInfo info = XgMatchInfo.From(Header(matchLength: 7));

        info.Player1.Should().Be("Alice");
        info.Player2.Should().Be("Bob");
        info.Terms.Should().Be(new MatchTerms { Length = 7 });
    }

    /// <summary>
    /// XG's 99999 sentinel is read as money terms, never surfaced as a length
    /// (<see cref="IMatchInfo"/>'s producer contract), stating the header's
    /// Jacoby and beaver rules and the cube limit its exponent spells.
    /// </summary>
    [Theory]
    [InlineData(true, false, 10, 1024)]
    [InlineData(false, true, 3, 8)]
    [InlineData(true, true, 4, 16)]
    [InlineData(false, false, 0, 1)]
    public void From_MoneySentinel_StatesMoneyTermsWithTheLimitTheExponentSpells(
        bool jacoby, bool beaver, int exponent, int cubeLimit)
    {
        var info = XgMatchInfo.From(Header(MatchHeaderRecord.MoneyMatchLengthSentinel, exponent, jacoby, beaver));

        info.Terms.Should().Be(new MoneyTerms { IsJacoby = jacoby, IsBeaver = beaver, CubeLimit = cubeLimit });
    }

    /// <summary>
    /// A match header reads whatever its Max Cube field states, and its terms
    /// carry no limit (Hal's ruling on halheinrich/backgammon#273,
    /// 2026-09-27). XG typically writes 10 for a match, but its own files hold
    /// others — 3 for a 5-point match, 4 for a 9-point one — so no value is
    /// refused and none is related to the length. The files are synthesized
    /// through the binary builder and read through both header paths, the full
    /// parse and the fast one; the field itself is read, onto the parsed record.
    /// </summary>
    [Theory]
    [InlineData(5, 3)]
    [InlineData(9, 4)]
    [InlineData(7, 10)]
    [InlineData(7, 0)]
    public void MatchHeader_ReadsWhateverItsMaxCube_WithNoLimitOnItsTerms(int length, int maxCubeExponent)
    {
        using var stream = XgBytesBuilder.BuildMinimalXgFile(matchLength: length, maxCubeExponent: maxCubeExponent);
        byte[] bytes = stream.ToArray();
        var file = XgFileReader.ReadStream(new MemoryStream(bytes));

        file.Records.OfType<MatchHeaderRecord>().Single().CubeLimit.Should().Be(maxCubeExponent,
            "the field is read with the header");
        XgDecisionIterator.ExtractMatchInfo(file)!.Terms.Should().Be(new MatchTerms { Length = length },
            "a match's terms are its length alone, whatever its Max Cube");

        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xg");
        File.WriteAllBytes(path, bytes);
        try
        {
            XgFileReader.ReadMatchInfo(path)!.Terms.Should().Be(new MatchTerms { Length = length });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public void From_MoneyHeaderWhoseExponentSpellsNoLimit_IsRefused(int exponent)
    {
        FluentActions.Invoking(() => XgMatchInfo.From(Header(MatchHeaderRecord.MoneyMatchLengthSentinel, exponent)))
            .Should().Throw<InvalidDataException>();
    }

    /// <summary>A match's own rules are the terms' to refuse: a length of 0 is no match, and no longer money.</summary>
    [Fact]
    public void From_MatchLengthZero_IsRefusedByTheTerms()
    {
        FluentActions.Invoking(() => XgMatchInfo.From(Header(matchLength: 0)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    // -----------------------------------------------------------------------
    //  The game's standing, against the terms
    // -----------------------------------------------------------------------

    [Fact]
    public void GameFrom_MatchTerms_StatesEachPlayersAwayScoreAndTheCrawfordFlag()
    {
        IGameInfo info = XgGameInfo.From(GameHeader(score1: 4, score2: 6, crawford: true), new MatchTerms { Length = 7 });

        info.Standing.Should().Be(new MatchStanding { Away1 = 3, Away2 = 1, IsCrawford = true });
        info.IsStandardStart.Should().BeTrue();
    }

    [Fact]
    public void GameFrom_MoneyTerms_StatesEachPlayersScore()
    {
        var terms = new MoneyTerms { IsJacoby = true, IsBeaver = false, CubeLimit = 1024 };

        XgGameInfo.From(GameHeader(score1: 2, score2: 5), terms).Standing
            .Should().Be(new MoneyStanding { Score1 = 2, Score2 = 5 });
    }

    /// <summary>
    /// The standing's own rules are its to refuse — here the Crawford game
    /// with neither player 1-away — and no money stand-in is left to hide one.
    /// </summary>
    [Fact]
    public void GameFrom_AStandingItsRulesRefuse_IsRefused()
    {
        FluentActions.Invoking(() => XgGameInfo.From(GameHeader(0, 0, crawford: true), new MatchTerms { Length = 7 }))
            .Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The standard start is <see cref="BoardPosition.Standard"/>'s; any other
    /// position, or one that is not even a position, is not it.
    /// </summary>
    [Fact]
    public void GameFrom_IsStandardStart_IsTheStandardPositionsQuestion()
    {
        var terms = new MatchTerms { Length = 7 };
        var saved = XgGameBuilder.PointsOf(BoardPosition.Nackgammon);
        var malformed = new sbyte[26];
        malformed[5] = 16;

        XgGameInfo.From(GameHeader(0, 0), terms).IsStandardStart.Should().BeTrue();
        XgGameInfo.From(GameHeader(0, 0, initial: saved), terms).IsStandardStart.Should().BeFalse();
        XgGameInfo.From(GameHeader(0, 0, initial: malformed), terms).IsStandardStart.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    //  The wire
    // -----------------------------------------------------------------------

    private static XgMatchInfo MoneyInfo() =>
        XgMatchInfo.From(Header(MatchHeaderRecord.MoneyMatchLengthSentinel, 10, jacoby: true));

    private static XgGameInfo MatchGameInfo() =>
        XgGameInfo.From(GameHeader(4, 6, crawford: true), new MatchTerms { Length = 7 });

    /// <summary>
    /// Through the library's options, each type writes its members in the
    /// document's camelCase, the terms and the standing as kinded documents —
    /// their kind stated first, as BgDataTypes_Lib's dispatch writes it.
    /// </summary>
    [Fact]
    public void Wire_ThroughTheLibrarysOptions_IsPinned()
    {
        JsonSerializer.Serialize(MoneyInfo(), XgJsonOptions.Default).Should().Be(
            """
            {
              "player1": "Alice",
              "player2": "Bob",
              "terms": {
                "kind": "money",
                "isJacoby": true,
                "isBeaver": false,
                "cubeLimit": 1024
              }
            }
            """.ReplaceLineEndings());

        JsonSerializer.Serialize(MatchGameInfo(), XgJsonOptions.Default).Should().Be(
            """
            {
              "isStandardStart": true,
              "standing": {
                "kind": "match",
                "away1": 3,
                "away2": 1,
                "isCrawford": true
              }
            }
            """.ReplaceLineEndings());
    }

    /// <summary>Each type reads back as it was written, on the library's options and the reflection defaults alike.</summary>
    [Fact]
    public void Wire_RoundTripsOnBothPaths()
    {
        foreach (var options in new[] { XgJsonOptions.Default, JsonSerializerOptions.Default })
        {
            var match = JsonSerializer.Deserialize<XgMatchInfo>(JsonSerializer.Serialize(MoneyInfo(), options), options)!;
            match.Player1.Should().Be("Alice");
            match.Player2.Should().Be("Bob");
            match.Terms.Should().Be(MoneyInfo().Terms);

            var game = JsonSerializer.Deserialize<XgGameInfo>(JsonSerializer.Serialize(MatchGameInfo(), options), options)!;
            game.IsStandardStart.Should().BeTrue();
            game.Standing.Should().Be(MatchGameInfo().Standing);
        }
    }

    public static TheoryData<string, string> Members => new()
    {
        { "match", "player1" }, { "match", "player2" }, { "match", "terms" },
        { "game", "isStandardStart" }, { "game", "standing" },
    };

    private static string Written(string type, JsonSerializerOptions options) => type == "match"
        ? JsonSerializer.Serialize(MoneyInfo(), options)
        : JsonSerializer.Serialize(MatchGameInfo(), options);

    private static void Read(string type, string json, JsonSerializerOptions options)
    {
        if (type == "match")
            JsonSerializer.Deserialize<XgMatchInfo>(json, options);
        else
            JsonSerializer.Deserialize<XgGameInfo>(json, options);
    }

    private static string Named(string member, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(member) ?? char.ToUpperInvariant(member[0]) + member[1..];

    /// <summary>
    /// The absence rule, first half: every member is required, so a document
    /// missing one is a <see cref="JsonException"/> on both paths — no member
    /// arrives silently as a default.
    /// </summary>
    [Theory]
    [MemberData(nameof(Members))]
    public void Wire_AMissingMember_IsRefusedOnBothPaths(string type, string member)
    {
        foreach (var options in new[] { XgJsonOptions.Default, JsonSerializerOptions.Default })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Written(type, options))!.AsObject();
            node.Remove(Named(member, options)).Should().BeTrue();

            FluentActions.Invoking(() => Read(type, node.ToJsonString(), options)).Should().Throw<JsonException>();
        }
    }

    /// <summary>
    /// The absence rule, second half: no member is nullable, so a document
    /// stating <see langword="null"/> for one is a <see cref="JsonException"/>
    /// on both paths.
    /// </summary>
    [Theory]
    [MemberData(nameof(Members))]
    public void Wire_AnExplicitNull_IsRefusedOnBothPaths(string type, string member)
    {
        foreach (var options in new[] { XgJsonOptions.Default, JsonSerializerOptions.Default })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Written(type, options))!.AsObject();
            node[Named(member, options)] = null;

            FluentActions.Invoking(() => Read(type, node.ToJsonString(), options)).Should().Throw<JsonException>();
        }
    }

    /// <summary>
    /// The terms and the standing are read through BgDataTypes_Lib's one
    /// dispatch: a document of the other kind's members is refused, not read
    /// with the members dropped.
    /// </summary>
    [Fact]
    public void Wire_TheKindedMembers_AreReadThroughTheOneDispatch()
    {
        var options = XgJsonOptions.Default;
        var match = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(MoneyInfo(), options))!.AsObject();
        match["terms"]!["kind"] = "match";
        var game = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(MatchGameInfo(), options))!.AsObject();
        game["standing"]!["kind"] = "money";

        FluentActions.Invoking(() => JsonSerializer.Deserialize<XgMatchInfo>(match.ToJsonString(), options))
            .Should().Throw<JsonException>();
        FluentActions.Invoking(() => JsonSerializer.Deserialize<XgGameInfo>(game.ToJsonString(), options))
            .Should().Throw<JsonException>();
    }
}
