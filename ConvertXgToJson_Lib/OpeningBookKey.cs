using System.Runtime.InteropServices;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Lookup key for <see cref="OpeningBook"/>: a candidate play's resulting
/// position plus the game context (money rules or match score) the book
/// entry was analysed under.
///
/// <para>
/// The key owns the book's two normalization conventions so callers cannot
/// get them wrong:
/// </para>
/// <list type="bullet">
///   <item><b>Perspective.</b> The book stores the resulting position from
///   the perspective of the player on roll <i>after</i> the play — the
///   mover's opponent. XG record positions
///   (<see cref="BestMoveAnalysis.PositionsPlayed"/>,
///   <see cref="MoveRecord.FinalPosition"/>) are player-1-relative, so the
///   factories flip when player 1 made the play
///   (<c>activePlayer &gt;= 0</c>) and pass through when player 2 did.</item>
///   <item><b>Away-score orientation.</b> The stored score pair is
///   (on-roll player's away, mover's away) in the flipped frame. The
///   factories take decision-time roles — <c>moverAway</c> /
///   <c>opponentAway</c> — and reorder internally.</item>
/// </list>
///
/// <para>
/// The factories cover the cube-centred-at-1 context only. 22 of the
/// shipped database's 53,210 entries carry a turned cube; the player
/// mapping of their stored owner sign has no tooltip oracle and is
/// unverified, so no public factory exposes it (an unverifiable parameter
/// would be API noise). Those entries are indexed but unreachable until
/// the convention is proven.
/// </para>
/// </summary>
internal readonly struct OpeningBookKey : IEquatable<OpeningBookKey>
{
    // The 26 position sbytes packed into four little-endian ulongs
    // (zero-padded to 32 bytes) so the struct is equatable and hashable
    // without per-comparison array walks.
    private readonly ulong _p0, _p1, _p2, _p3;
    private readonly int _cubeValue;
    private readonly int _cubeOwnerSign;
    private readonly int _onRollAway;    // stored slot c; -1 = money
    private readonly int _opponentAway;  // stored slot d; -1 = money
    private readonly bool _jacoby;       // money contexts only
    private readonly bool _crawford;     // match contexts only

    private OpeningBookKey(
        ReadOnlySpan<sbyte> positionOnRollPov, int cubeValue, int cubeOwnerSign,
        int onRollAway, int opponentAway, bool jacoby, bool crawford)
    {
        Span<byte> packed = stackalloc byte[32];
        MemoryMarshal.AsBytes(positionOnRollPov).CopyTo(packed);
        _p0 = BitConverter.ToUInt64(packed[..8]);
        _p1 = BitConverter.ToUInt64(packed[8..16]);
        _p2 = BitConverter.ToUInt64(packed[16..24]);
        _p3 = BitConverter.ToUInt64(packed[24..32]);
        _cubeValue = cubeValue;
        _cubeOwnerSign = cubeOwnerSign;
        _onRollAway = onRollAway;
        _opponentAway = opponentAway;
        _jacoby = jacoby;
        _crawford = crawford;
    }

    /// <summary>
    /// Builds the key for a candidate play made during a match game.
    /// </summary>
    /// <param name="positionPlayed">The candidate's resulting position in
    /// the XG record convention (player-1-relative) — e.g. an element of
    /// <see cref="BestMoveAnalysis.PositionsPlayed"/>.</param>
    /// <param name="activePlayer">The player who made the play, in the
    /// ecosystem's sign convention (≥ 0 = player 1).</param>
    /// <param name="moverAway">Away score of the player who made the play.</param>
    /// <param name="opponentAway">Away score of their opponent.</param>
    /// <param name="isCrawford">Whether the game is the Crawford game.</param>
    public static OpeningBookKey ForMatchPlay(
        PositionEngine positionPlayed, int activePlayer,
        int moverAway, int opponentAway, bool isCrawford)
    {
        ArgumentNullException.ThrowIfNull(positionPlayed);
        ArgumentOutOfRangeException.ThrowIfLessThan(moverAway, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(opponentAway, 1);

        return new OpeningBookKey(
            NormalizeToNewOnRoll(positionPlayed, activePlayer),
            cubeValue: 1, cubeOwnerSign: 0,
            onRollAway: opponentAway, opponentAway: moverAway,
            jacoby: false, crawford: isCrawford);
    }

    /// <summary>
    /// Builds the key for a candidate play made during a money session.
    /// </summary>
    /// <param name="positionPlayed">The candidate's resulting position in
    /// the XG record convention (player-1-relative) — e.g. an element of
    /// <see cref="BestMoveAnalysis.PositionsPlayed"/>.</param>
    /// <param name="activePlayer">The player who made the play, in the
    /// ecosystem's sign convention (≥ 0 = player 1).</param>
    /// <param name="jacoby">Whether the Jacoby rule is in effect — part of
    /// the book's money key (the shipped database stores separate money
    /// rollouts per Jacoby setting).</param>
    public static OpeningBookKey ForMoneyPlay(
        PositionEngine positionPlayed, int activePlayer, bool jacoby)
    {
        ArgumentNullException.ThrowIfNull(positionPlayed);

        return new OpeningBookKey(
            NormalizeToNewOnRoll(positionPlayed, activePlayer),
            cubeValue: 1, cubeOwnerSign: 0,
            onRollAway: -1, opponentAway: -1,
            jacoby: jacoby, crawford: false);
    }

    /// <summary>
    /// Builds the key a parsed entry is indexed under — the stored position
    /// is already in the book's on-roll-after-the-play perspective, so no
    /// flip. Normalizes the rule flags the same way the public factories do
    /// (Jacoby is a money-key axis, Crawford a match-key axis), so noisy
    /// stored flags cannot make a lookup miss.
    /// </summary>
    internal static OpeningBookKey ForStoredEntry(OpeningBookEntry entry)
    {
        bool money = entry.IsMoneySession;
        return new OpeningBookKey(
            entry.Position.Points,
            entry.CubeValue, entry.CubeOwnerSign,
            entry.OnRollAway, entry.OpponentAway,
            jacoby: money && entry.Jacoby,
            crawford: !money && entry.Crawford);
    }

    /// <summary>
    /// Re-expresses a player-1-relative resulting position from the
    /// perspective of the player on roll after the play: flip when player 1
    /// was the mover, pass through when player 2 was (the new on-roll
    /// player 1 already reads positive).
    /// </summary>
    private static sbyte[] NormalizeToNewOnRoll(PositionEngine positionPlayed, int activePlayer)
        => activePlayer >= 0
            ? BackgammonConstants.Flip(positionPlayed.Points)
            : positionPlayed.Points;

    /// <inheritdoc/>
    public bool Equals(OpeningBookKey other) =>
        _p0 == other._p0 && _p1 == other._p1 && _p2 == other._p2 && _p3 == other._p3 &&
        _cubeValue == other._cubeValue && _cubeOwnerSign == other._cubeOwnerSign &&
        _onRollAway == other._onRollAway && _opponentAway == other._opponentAway &&
        _jacoby == other._jacoby && _crawford == other._crawford;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpeningBookKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        _p0, _p1, _p2, _p3,
        _cubeValue * 4 + _cubeOwnerSign,
        _onRollAway * 65536 + _opponentAway,
        _jacoby, _crawford);

    /// <summary>Value equality over position and full context.</summary>
    public static bool operator ==(OpeningBookKey left, OpeningBookKey right) => left.Equals(right);

    /// <summary>Negation of <see cref="op_Equality"/>.</summary>
    public static bool operator !=(OpeningBookKey left, OpeningBookKey right) => !left.Equals(right);
}
