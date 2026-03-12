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
}