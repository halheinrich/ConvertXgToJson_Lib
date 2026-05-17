using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Match-level metadata extracted from <see cref="MatchHeaderRecord"/>.
/// Populated on <see cref="XgIteratorState.MatchInfo"/> before any rows
/// are yielded from the match, allowing the caller to skip the match entirely.
/// </summary>
public sealed class XgMatchInfo
{
    /// <summary>Name of player 1 (bottom player in XG).</summary>
    public string Player1 { get; init; } = string.Empty;

    /// <summary>Name of player 2 (top player in XG).</summary>
    public string Player2 { get; init; } = string.Empty;

    /// <summary>
    /// Match length (points to win). 0 = unlimited / money session.
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
        MatchLength = hm.MatchLength >= 99999 ? 0 : hm.MatchLength,
    };
}