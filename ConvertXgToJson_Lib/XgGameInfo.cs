namespace ConvertXgToJson_Lib;

/// <summary>
/// Game-level metadata extracted from <see cref="GameHeaderRecord"/>.
/// Populated on <see cref="XgIteratorState.GameInfo"/> before any rows
/// are yielded from the game, allowing the caller to skip the game entirely.
/// </summary>
public sealed class XgGameInfo
{
    /// <summary>Score of player 1 (bottom player) at the start of this game.</summary>
    public int Score1 { get; init; }

    /// <summary>Score of player 2 (top player) at the start of this game.</summary>
    public int Score2 { get; init; }

    /// <summary>True if the Crawford rule applies to this game.</summary>
    public bool CrawfordApplies { get; init; }

    /// <summary>
    /// True if the game starts from the standard backgammon opening position.
    /// False if started from a saved or custom position.
    /// Used to filter for opening move decisions.
    /// </summary>
    public bool IsStandardStart { get; init; }
}