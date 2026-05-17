using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Game-level metadata extracted from <see cref="GameHeaderRecord"/> and
/// <see cref="MatchHeaderRecord"/>. Populated on
/// <see cref="XgIteratorState.GameInfo"/> before any rows are yielded from
/// the game, allowing the caller to skip the game entirely.
/// Money sessions: Away1 = 0, Away2 = 0, IsCrawfordGame = false.
/// </summary>
public sealed class XgGameInfo
{
    /// <summary>
    /// Points still needed by player 1 to win the match.
    /// 0 for money sessions.
    /// </summary>
    public int Away1 { get; init; }

    /// <summary>
    /// Points still needed by player 2 to win the match.
    /// 0 for money sessions.
    /// </summary>
    public int Away2 { get; init; }

    /// <summary>True if the Crawford rule applies to this game.</summary>
    public bool IsCrawfordGame { get; init; }

    /// <summary>
    /// True if the game starts from the standard backgammon opening position.
    /// False if started from a saved or custom position.
    /// Used to filter for opening move decisions.
    /// </summary>
    public bool IsStandardStart { get; init; }

    /// <summary>
    /// Projects a parsed <see cref="GameHeaderRecord"/> to game-level metadata.
    /// The single header-to-<see cref="XgGameInfo"/> projection, shared by the
    /// file reader and the decision iterator. <paramref name="matchLength"/> is
    /// the normalized match length (0 for money sessions).
    /// </summary>
    internal static XgGameInfo From(GameHeaderRecord gh, int matchLength) => new()
    {
        Away1 = BackgammonConstants.AwayScore(matchLength, gh.Score1),
        Away2 = BackgammonConstants.AwayScore(matchLength, gh.Score2),
        IsCrawfordGame = gh.CrawfordApplies,
        IsStandardStart = BackgammonConstants.IsStandardOpeningPosition(gh.InitialPosition),
    };
}