namespace ConvertXgToJson_Lib;

/// <summary>
/// Options for <see cref="XgpExporter"/>'s slice surface. The slice copies
/// the source match header verbatim; these options are the opt-in
/// exceptions. The default instance (all properties <see langword="null"/>)
/// changes nothing.
///
/// <para><b>Two naming axes.</b> The slot-based pair
/// (<see cref="Player1Name"/>/<see cref="Player2Name"/>) renames by header
/// slot. The role-based pair (<see cref="OnRollName"/>/
/// <see cref="OpponentName"/>) renames by decision role: the exporter
/// resolves which slot is the decision-maker's from the source records'
/// <c>ActivePlayer</c> sign (<c>&gt;= 0</c> is player 1; a cube record's
/// <c>ActivePlayer</c> is the doubler, so a take decision is anchored to
/// the doubler). Roles are determinable iff the exported records contain
/// at least one move/cube record and all of them share one
/// <c>ActivePlayer</c> sign — true by construction for every slice
/// (a single located decision) and every single-decision copy source
/// (a parsed <c>.xgp</c>); a multi-decision copy (a whole <c>.xg</c>
/// match) has no file-wide roles. When roles are determinable a role name
/// takes precedence over the same slot's slot name; when they are not,
/// slot names apply and role names are deliberately unused — unless no
/// slot name is set at all, in which case the exporter throws
/// <see cref="NotSupportedException"/> rather than silently guess.
/// <see cref="Anonymized"/> carries both pairs, so it never throws.</para>
///
/// <para><b>Field mechanics.</b> A resolved name rewrites that player's
/// two match-header fields — the Unicode field
/// (<c>Player1</c>/<c>Player2</c>, truncated at 128 characters by the
/// writer) and its ANSI twin (<c>Player1Ansi</c>/<c>Player2Ansi</c>,
/// truncated at 40 bytes). The Unicode field carries the override verbatim;
/// the ANSI twin degrades any character outside Latin1 to <c>'?'</c> (the
/// writer's replacement fallback), mirroring how the XG1-compat twin
/// relates to the Unicode field in real XG saves. No other header field is
/// touched — in particular the Location provenance fingerprint passes
/// through untouched.</para>
/// </summary>
public sealed record XgpSliceOptions
{
    private readonly string? _player1Name;
    private readonly string? _player2Name;
    private readonly string? _onRollName;
    private readonly string? _opponentName;

    /// <summary>
    /// Replacement name for player 1; <see langword="null"/> (the default)
    /// keeps the source name. Must be non-empty when set.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown on init when the value is empty or whitespace.
    /// </exception>
    public string? Player1Name
    {
        get => _player1Name;
        init => _player1Name = ValidateName(value);
    }

    /// <summary>
    /// Replacement name for player 2; <see langword="null"/> (the default)
    /// keeps the source name. Must be non-empty when set.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown on init when the value is empty or whitespace.
    /// </exception>
    public string? Player2Name
    {
        get => _player2Name;
        init => _player2Name = ValidateName(value);
    }

    /// <summary>
    /// Role-based replacement name for the decision-maker's slot — the
    /// player on roll (for a cube decision, the doubler);
    /// <see langword="null"/> (the default) means no role-based override.
    /// Must be non-empty when set. Takes precedence over that slot's
    /// slot-based name when roles are determinable; see the type remarks
    /// for the resolution rule.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown on init when the value is empty or whitespace.
    /// </exception>
    public string? OnRollName
    {
        get => _onRollName;
        init => _onRollName = ValidateName(value);
    }

    /// <summary>
    /// Role-based replacement name for the other slot — the opponent of
    /// the decision-maker (for a cube decision, the taker);
    /// <see langword="null"/> (the default) means no role-based override.
    /// Must be non-empty when set. Takes precedence over that slot's
    /// slot-based name when roles are determinable; see the type remarks
    /// for the resolution rule.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown on init when the value is empty or whitespace.
    /// </exception>
    public string? OpponentName
    {
        get => _opponentName;
        init => _opponentName = ValidateName(value);
    }

    /// <summary>
    /// Preset for anonymized export, carrying both naming axes. Wherever a
    /// single decision defines roles (every slice; a copy of a
    /// single-decision <c>.xgp</c>) the players are renamed by role,
    /// "On-roll" / "Opponent"; where roles are undefined (a copy of a whole
    /// <c>.xg</c> match) the slot fallback applies, "Player 1" /
    /// "Player 2" — so this preset never throws. This instance is the
    /// single source of what "anonymized" means — consumers wire an
    /// anonymize toggle to it rather than encoding their own replacement
    /// names.
    /// </summary>
    public static XgpSliceOptions Anonymized { get; } = new()
    {
        OnRollName = "On-roll",
        OpponentName = "Opponent",
        Player1Name = "Player 1",
        Player2Name = "Player 2",
    };

    private static string? ValidateName(string? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "A player-name override must be non-empty; use null for no override.",
                nameof(value));
        return value;
    }
}
