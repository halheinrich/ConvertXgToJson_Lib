using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The one public way to make an in-memory <see cref="XgFile"/>: describes
/// a match in intent-level terms — match length and players (or a money
/// session), then per game the score, the starting position, and the
/// checker plays and cube actions that were made — and synthesizes the XG
/// record stream behind it. Consumers never see the record model: the
/// result is the same opaque handle <see cref="XgFileReader"/> produces,
/// ready for <see cref="XgDecisionIterator"/>, <see cref="XgFileWriter"/>,
/// <see cref="XgpExporter"/>, or <see cref="XgFileReader.ToJson"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Positions</b> are given as 26-element boards in XG's own frame —
/// player 1's perspective regardless of who is on roll: <c>[0]</c> player
/// 2's bar (≤ 0), <c>[1..24]</c> the points with player 1 bearing off past
/// point 1, <c>[25]</c> player 1's bar (≥ 0); positive counts are player
/// 1's checkers. Plays, by contrast, are in the mover's own numbering
/// (the <see cref="BgDataTypes_Lib.Play"/> contract); the builder flips
/// between the two.
/// </para>
/// <para>
/// <b>State is tracked</b> within a game: each play advances the position
/// and each taken double moves the cube, so a sequence of decisions reads
/// like the game it describes. <see cref="XgGameBuilder.AtPosition"/>
/// resets the position mid-game for problem positions.
/// </para>
/// <para>
/// <b>Validation fails loud</b> at the call that makes the match
/// unrepresentable — an out-of-range score, a play from an empty point, a
/// decision after the game ended on a pass — with
/// <see cref="ArgumentException"/> / <see cref="InvalidOperationException"/>,
/// never a silently malformed file. Dice legality of plays is not checked.
/// </para>
/// <para>
/// Synthesized files self-identify through the match header's Location
/// (the same producer fingerprint <see cref="XgpExporter"/> writes) and
/// are byte-deterministic: no timestamps, no random ids.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var builder = XgFileBuilder.ForMatch(7, "Alice", "Bob");
/// var game = builder.AddGame(score1: 0, score2: 1);
/// game.CubeDecision(XgPlayer.Player2, new XgCubeEquities(0.2, 0.1, 1.0), doublerAction: CubeAction.NoDouble);
/// game.Play(XgPlayer.Player1, new DiceRoll(3, 1), play);   // play: 8/5 6/5 as a Play
/// XgFile file = builder.Build();
/// </code>
/// </example>
public sealed class XgFileBuilder
{
    private readonly List<XgGameBuilder> _games = [];

    private XgFileBuilder(int matchLength, string player1, string player2, bool jacoby, bool beaver)
    {
        MatchLength = matchLength;
        Player1 = player1;
        Player2 = player2;
        IsJacoby = jacoby;
        IsBeaver = beaver;
    }

    /// <summary>
    /// Starts a match of <paramref name="matchLength"/> points between the
    /// two named players (header slots 1 and 2).
    /// </summary>
    /// <param name="matchLength">Match length in points, at least 1.</param>
    /// <param name="player1">Name of the player in header slot 1; non-blank.</param>
    /// <param name="player2">Name of the player in header slot 2; non-blank.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="matchLength"/> is below 1.</exception>
    /// <exception cref="ArgumentException">A player name is null or whitespace.</exception>
    public static XgFileBuilder ForMatch(int matchLength, string player1, string player2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(matchLength, 1);
        ValidateName(player1);
        ValidateName(player2);
        return new XgFileBuilder(matchLength, player1, player2, jacoby: false, beaver: false);
    }

    /// <summary>
    /// Starts an unlimited (money) session between the two named players.
    /// Defaults to XG's money defaults: Jacoby on, Beaver off.
    /// </summary>
    /// <param name="player1">Name of the player in header slot 1; non-blank.</param>
    /// <param name="player2">Name of the player in header slot 2; non-blank.</param>
    /// <param name="jacoby">Whether the Jacoby rule is in force.</param>
    /// <param name="beaver">Whether beavers are allowed.</param>
    /// <exception cref="ArgumentException">A player name is null or whitespace.</exception>
    public static XgFileBuilder ForMoneySession(
        string player1, string player2, bool jacoby = true, bool beaver = false)
    {
        ValidateName(player1);
        ValidateName(player2);
        return new XgFileBuilder(matchLength: 0, player1, player2, jacoby, beaver);
    }

    /// <summary>Match length in points; 0 for a money session.</summary>
    public int MatchLength { get; }

    /// <summary>True for an unlimited (money) session.</summary>
    public bool IsMoneySession => MatchLength == 0;

    /// <summary>Name of the player in header slot 1.</summary>
    public string Player1 { get; }

    /// <summary>Name of the player in header slot 2.</summary>
    public string Player2 { get; }

    /// <summary>Whether the Jacoby rule is in force (money sessions only).</summary>
    public bool IsJacoby { get; }

    /// <summary>Whether beavers are allowed (money sessions only).</summary>
    public bool IsBeaver { get; }

    /// <summary>
    /// Adds the next game of the match and returns its builder, on which
    /// the game's decisions are recorded in play order. Games are numbered
    /// in the order added.
    /// </summary>
    /// <param name="score1">Player 1's score entering the game.</param>
    /// <param name="score2">Player 2's score entering the game.</param>
    /// <param name="isCrawford">
    /// Whether this is the Crawford game. Match play only, and only when
    /// exactly one player is one point from winning.
    /// </param>
    /// <param name="initialPosition">
    /// The position the game starts from, in the player-1 frame described
    /// on the class; null (the default) for the standard opening position.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A score is negative, or (match play) not below the match length.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="isCrawford"/> in a money session or at a score where
    /// neither or both players are one away; or
    /// <paramref name="initialPosition"/> is not a valid 26-element board.
    /// </exception>
    public XgGameBuilder AddGame(
        int score1 = 0, int score2 = 0, bool isCrawford = false,
        IReadOnlyList<int>? initialPosition = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(score1);
        ArgumentOutOfRangeException.ThrowIfNegative(score2);
        if (!IsMoneySession)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(score1, MatchLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(score2, MatchLength);
        }
        if (isCrawford)
        {
            if (IsMoneySession)
                throw new ArgumentException(
                    "The Crawford rule applies to match play only.", nameof(isCrawford));
            bool oneAway1 = MatchLength - score1 == 1;
            bool oneAway2 = MatchLength - score2 == 1;
            if (oneAway1 == oneAway2)
                throw new ArgumentException(
                    $"The Crawford game is played when exactly one player is one point from " +
                    $"winning; at {score1}-{score2} in a {MatchLength}-point match " +
                    (oneAway1 ? "both players are." : "neither player is."),
                    nameof(isCrawford));
        }

        sbyte[] position = initialPosition is null
            ? (sbyte[])BackgammonConstants.StandardOpeningPosition.Clone()
            : XgGameBuilder.ValidatePosition(initialPosition, nameof(initialPosition));

        var game = new XgGameBuilder(this, _games.Count + 1, score1, score2, isCrawford, position);
        _games.Add(game);
        return game;
    }

    /// <summary>
    /// Synthesizes the <see cref="XgFile"/> for everything described so far.
    /// A match with no games is a valid (header-only) file; the builder may
    /// be extended and built again.
    /// </summary>
    public XgFile Build()
    {
        var records = new List<SaveRecord>(1 + _games.Sum(g => 1 + g.RecordCount))
        {
            XgRecordFactory.MatchHeader(
                MatchLength, IsJacoby, IsBeaver, Player1, Player2,
                eventName: "", date: default, gameId: 0),
        };
        foreach (var game in _games)
            game.AppendRecords(records);

        return new XgFile
        {
            Header = XgRecordFactory.FileHeader(SaveName()),
            Records = records,
        };
    }

    /// <summary>
    /// Display name in XG's own pattern for a match save
    /// ("Alice vs. Bob, 7 point match").
    /// </summary>
    private string SaveName() =>
        IsMoneySession
            ? $"{Player1} vs. {Player2}, Unlimited Game"
            : $"{Player1} vs. {Player2}, {MatchLength} point match";

    private static void ValidateName(string name, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(name))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A player name must be non-blank.", paramName);
    }
}
