namespace ConvertXgToJson_Lib;

/// <summary>
/// One of the two players of an XG match, by header slot — the player named
/// first in the match header is <see cref="Player1"/>. Used by
/// <see cref="XgGameBuilder"/> to say who made a decision.
/// </summary>
/// <remarks>
/// Slots, not roles: the on-roll / opponent roles of a decision are derived
/// from the slot at emission time (the iterator reports the decision-maker's
/// name from the header slot, and the away scores follow). The numeric values
/// are not part of the contract.
/// </remarks>
public enum XgPlayer
{
    /// <summary>The player in the match header's first slot.</summary>
    Player1,

    /// <summary>The player in the match header's second slot.</summary>
    Player2,
}

/// <summary>Slot ↔ sign conversions, internal to the builder.</summary>
internal static class XgPlayerExtensions
{
    /// <summary>
    /// The ecosystem's player-sign convention (<c>≥ 0</c> is player 1), as
    /// stored in <c>ActivePlayer</c> on the XG records.
    /// </summary>
    internal static int ToSign(this XgPlayer player) => player switch
    {
        XgPlayer.Player1 => 1,
        XgPlayer.Player2 => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unknown player slot."),
    };

    /// <summary>The other slot.</summary>
    internal static XgPlayer Opponent(this XgPlayer player) =>
        player == XgPlayer.Player1 ? XgPlayer.Player2 : XgPlayer.Player1;
}
