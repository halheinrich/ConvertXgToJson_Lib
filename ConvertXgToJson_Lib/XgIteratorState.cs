namespace ConvertXgToJson_Lib;

/// <summary>
/// Shared state allowing the caller to signal early-exit hints to
/// <see cref="XgDecisionIterator"/> after processing each yielded row.
/// ConvertXgToJson_Lib resets flags at the appropriate boundaries.
/// </summary>
public sealed class XgIteratorState
{
    /// <summary>
    /// Set by the caller after receiving a row to skip all remaining
    /// decisions in the current game. Reset by the iterator at each
    /// new GameHeaderRecord.
    /// </summary>
    public bool AdvanceNextGame { get; set; }

    /// <summary>
    /// Set by the caller after receiving a row to skip all remaining
    /// decisions in the current match (.xg file). Reset by the iterator
    /// at each new .xg file.
    /// </summary>
    public bool AdvanceNextMatch { get; set; }

    /// <summary>
    /// Populated by the iterator from <see cref="MatchHeaderRecord"/> before
    /// any rows are yielded from the match. The caller may read this and set
    /// <see cref="AdvanceNextMatch"/> = true to skip the match entirely.
    /// Reset to null at the start of each new .xg file.
    /// </summary>
    public XgMatchInfo? MatchInfo { get; set; }

    /// <summary>
    /// Populated by the iterator from <see cref="GameHeaderRecord"/> before
    /// any rows are yielded from the game. The caller may read this and set
    /// <see cref="AdvanceNextGame"/> = true to skip the game entirely.
    /// Reset to null at the start of each new game.
    /// </summary>
    public XgGameInfo? GameInfo { get; set; }
}