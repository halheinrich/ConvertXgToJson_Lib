namespace ConvertXgToJson_Lib;

/// <summary>
/// Read-only observer of producer-internal iteration state. Populated by
/// <see cref="XgDecisionIterator"/> as iteration progresses; the caller may
/// inspect <see cref="MatchInfo"/> and <see cref="GameInfo"/> for per-row
/// context.
///
/// <para>
/// Iteration control — skipping matches, games, or stopping early — is
/// supplied separately as predicates on <see cref="XgIteratorCallbacks"/>.
/// This type carries no caller-mutable surface.
/// </para>
/// </summary>
public sealed class XgIteratorState
{
    /// <summary>
    /// Populated by the iterator from <see cref="Models.MatchHeaderRecord"/> before
    /// any rows are yielded from the match. Reset (repopulated) at the start
    /// of each new .xg file.
    /// </summary>
    public XgMatchInfo? MatchInfo { get; internal set; }

    /// <summary>
    /// Populated by the iterator from <see cref="Models.GameHeaderRecord"/> before
    /// any rows are yielded from the game. Reset to null at the start of each
    /// new .xg file and repopulated at each new game.
    /// </summary>
    public XgGameInfo? GameInfo { get; internal set; }
}
