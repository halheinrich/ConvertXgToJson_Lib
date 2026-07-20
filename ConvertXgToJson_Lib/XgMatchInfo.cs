using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Match-level metadata extracted from <see cref="MatchHeaderRecord"/>.
/// Populated on <see cref="XgIteratorState.MatchInfo"/> before any rows
/// are yielded from the match, allowing the caller to skip the match entirely.
/// Satisfies <see cref="IMatchInfo"/> so filter layers can consume it without
/// referencing this type; <see cref="IMatchInfo.IsMoneyGame"/> is inherited
/// from the contract rather than restated here.
/// </summary>
public sealed class XgMatchInfo : IMatchInfo
{
    /// <summary>Name of player 1 (bottom player in XG).</summary>
    public string Player1 { get; init; } = string.Empty;

    /// <summary>Name of player 2 (top player in XG).</summary>
    public string Player2 { get; init; } = string.Empty;

    /// <summary>
    /// Match length (points to win). 0 = unlimited / money session.
    /// XG's 99999 sentinel is normalized to 0 by <see cref="From"/>, honoring
    /// the producer contract documented on <see cref="IMatchInfo.MatchLength"/>.
    /// </summary>
    public int MatchLength { get; init; }

    /// <summary>
    /// Projects a parsed <see cref="MatchHeaderRecord"/> to match-level
    /// metadata. The single header-to-<see cref="XgMatchInfo"/> projection,
    /// shared by the file reader and the decision iterator.
    /// </summary>
    internal static XgMatchInfo From(MatchHeaderRecord hm) => new()
    {
        Player1 = hm.Player1,
        Player2 = hm.Player2,
        MatchLength = hm.MatchLength >= MatchHeaderRecord.MoneyMatchLengthSentinel ? 0 : hm.MatchLength,
    };
}