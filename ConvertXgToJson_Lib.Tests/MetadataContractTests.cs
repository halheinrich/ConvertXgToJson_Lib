using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Json;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins this repo's side of the contract-layer arc: <see cref="XgMatchInfo"/>
/// satisfies <see cref="IMatchInfo"/> and <see cref="XgGameInfo"/> satisfies
/// <see cref="IGameInfo"/>, so filter layers can consume them without ever
/// naming a producer type. The implements are additive — these tests also pin
/// that they left the JSON wire shape of the metadata types untouched.
/// </summary>
public class MetadataContractTests
{
    // -----------------------------------------------------------------------
    //  Contract satisfaction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Consumption through the interface must see exactly what the concrete
    /// type carries — the point of the contract is that a filter reading
    /// <see cref="IMatchInfo"/> needs no producer-specific knowledge.
    /// </summary>
    [Fact]
    public void XgMatchInfo_SatisfiesIMatchInfo_MembersRoundTripThroughTheInterface()
    {
        IMatchInfo info = new XgMatchInfo
        {
            Player1 = "Alice",
            Player2 = "Bob",
            MatchLength = 7,
        };

        info.Player1.Should().Be("Alice");
        info.Player2.Should().Be("Bob");
        info.MatchLength.Should().Be(7);
    }

    /// <summary>
    /// <see cref="IMatchInfo.IsMoneyGame"/> is a default interface member:
    /// <see cref="XgMatchInfo"/> inherits the rule rather than restating it,
    /// which is why it is only reachable through the interface. Pinning both
    /// polarities guards against a future redeclaration on the concrete type
    /// silently taking over with a different derivation.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(7, false)]
    public void XgMatchInfo_InheritsIsMoneyGameFromTheContract(int matchLength, bool expected)
    {
        IMatchInfo info = new XgMatchInfo { MatchLength = matchLength };

        info.IsMoneyGame.Should().Be(expected);
    }

    /// <summary>
    /// The producer contract documented on <see cref="IMatchInfo.MatchLength"/>:
    /// XG's 99999 money sentinel is normalized at the parse boundary, so the
    /// sentinel never reaches a consumer — and <c>IsMoneyGame</c>, which derives
    /// from the normalized value, is correct as a result.
    /// </summary>
    [Fact]
    public void XgMatchInfo_From_NormalizesMoneySentinel_SoIsMoneyGameHolds()
    {
        IMatchInfo info = XgMatchInfo.From(new MatchHeaderRecord
        {
            Player1 = "Alice",
            Player2 = "Bob",
            MatchLength = 99999,
        });

        info.MatchLength.Should().Be(0, "the sentinel is normalized away at the parse boundary");
        info.IsMoneyGame.Should().BeTrue();
    }

    [Fact]
    public void XgGameInfo_SatisfiesIGameInfo_MembersRoundTripThroughTheInterface()
    {
        IGameInfo info = new XgGameInfo
        {
            Away1 = 3,
            Away2 = 5,
            IsCrawfordGame = true,
            IsStandardStart = true,
        };

        info.Away1.Should().Be(3);
        info.Away2.Should().Be(5);
        info.IsCrawfordGame.Should().BeTrue();
        info.IsStandardStart.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    //  Wire-shape regression — adding an interface must not alter JSON
    // -----------------------------------------------------------------------

    /// <summary>
    /// The <see cref="IMatchInfo"/> implement is declaration-only, and
    /// <see cref="IMatchInfo.IsMoneyGame"/> is a default interface member — not
    /// a property of <see cref="XgMatchInfo"/> — so it must not appear in the
    /// serialized output. Pins the exact wire shape against both the library's
    /// options and plain defaults.
    /// </summary>
    [Fact]
    public void XgMatchInfo_JsonWireShape_IsUnchangedByTheImplement()
    {
        var info = new XgMatchInfo { Player1 = "Alice", Player2 = "Bob", MatchLength = 7 };

        string libraryJson = JsonSerializer.Serialize(info, XgJsonOptions.Default);
        libraryJson.Should().Be(
            """
            {
              "player1": "Alice",
              "player2": "Bob",
              "matchLength": 7
            }
            """.ReplaceLineEndings(),
            "the interface adds no serialized member — IsMoneyGame is a DIM, not a property");

        string defaultJson = JsonSerializer.Serialize(info);
        defaultJson.Should().Be("""{"Player1":"Alice","Player2":"Bob","MatchLength":7}""");
    }

    [Fact]
    public void XgGameInfo_JsonWireShape_IsUnchangedByTheImplement()
    {
        var info = new XgGameInfo
        {
            Away1 = 3,
            Away2 = 5,
            IsCrawfordGame = true,
            IsStandardStart = false,
        };

        JsonSerializer.Serialize(info).Should().Be(
            """{"Away1":3,"Away2":5,"IsCrawfordGame":true,"IsStandardStart":false}""");
    }

    // -----------------------------------------------------------------------
    //  MatchContext — the internal spelling of the same rule
    // -----------------------------------------------------------------------

    /// <summary>
    /// <see cref="MatchContext"/> deliberately does not implement
    /// <see cref="IMatchInfo"/> (it keeps its player names private behind
    /// <c>PlayerName</c>), but its local <c>IsMoneyGame</c> must agree with the
    /// contract's derivation — including for the raw 99999 sentinel, which its
    /// constructor normalizes.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(99999, true)]
    [InlineData(1, false)]
    [InlineData(7, false)]
    public void MatchContext_IsMoneyGame_AgreesWithTheContractDerivation(int rawMatchLength, bool expected)
    {
        var ctx = new MatchContext(
            [new MatchHeaderRecord { MatchLength = rawMatchLength }],
            sourceFile: null,
            comments: []);

        ctx.IsMoneyGame.Should().Be(expected);
        ctx.IsMoneyGame.Should().Be(((IMatchInfo)new XgMatchInfo { MatchLength = ctx.MatchLength }).IsMoneyGame);
    }
}
