using System.Globalization;
using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib;

/// <summary>
/// One thing the decision walk left out of a decoded file, and why: a whole
/// game whose header the session refuses, or one checker play XG marks as
/// illegal or that holds a corrupt candidate (<see cref="XgOmissionKind"/>).
/// The walk raises one per omission, logs it through the caller's logger and
/// records it on the caller's <see cref="XgIteratorState"/>
/// (<see cref="XgIteratorState.Omissions"/>), so a consumer can tell its user
/// what the file held that its decisions do not (Hal's ruling of 2026-11-01,
/// halheinrich/backgammon#514: "provide/extract as many legit decisions as
/// there are"; "any problems are to be brought to the attention of the
/// user").
/// </summary>
/// <remarks>
/// <para>
/// <b>Where in the file.</b> <see cref="Game"/> is the game's 1-based
/// number within the file, the number its decisions carry in their
/// <see cref="XgDecisionId"/>; it counts every game header, an omitted
/// game's included, so the games around an omission keep their numbers. An
/// omitted checker play names its <see cref="MoveNumber"/>, the 1-based
/// number of its move record within the game — again the number a decision
/// there would carry — and its <see cref="Roll"/> where the record states
/// one: an illegal-play marker is reported whatever the record's dice say,
/// and where they are no roll (both 0, one 0, a face above 6) the omission
/// names the move alone, inventing no dice. An omitted game names neither
/// (<see cref="IsWholeGame"/>). A standalone position (an <c>.xgp</c>) is
/// one game of one record, so its omission reads game 1, move 1.
/// </para>
/// <para>
/// <b>The reason is the complete message of the rule that refused</b>
/// (Hal's ruling of 2026-11-01, halheinrich/backgammon#514). For a refused
/// standing it is the exception message BgDataTypes_Lib's constructors or
/// <see cref="GameSession.Create"/> threw, whole: the rule's sentence, the
/// parameter .NET appends and, for an out-of-range value, the offending
/// value on a second line, so the text can hold a line break. For a corrupt
/// candidate it names each candidate found invalid; for an illegal play it
/// states XG's marker. It is text for a reader — a log line, a skip list —
/// and not a stable format: nothing parses it, nothing shortens it, and a
/// consumer that needs the kind reads <see cref="Kind"/>.
/// </para>
/// <para>
/// <b>Built by the walk, read by everyone else.</b> The constructor is
/// internal: an omission states what the walk found, and nothing outside this
/// library has one to state. Value equality, as a record: two omissions
/// naming the same place for the same reason are equal.
/// </para>
/// </remarks>
public sealed record XgOmission
{
    /// <summary>Records an omission the walk found; see the class remarks for each member's meaning.</summary>
    /// <exception cref="ArgumentException"><paramref name="sourceFile"/> or <paramref name="reason"/> is empty, or <paramref name="roll"/> is given without <paramref name="moveNumber"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="game"/> or <paramref name="moveNumber"/> is below 1, or <paramref name="kind"/> is not a defined kind.</exception>
    internal XgOmission(string sourceFile, int game, int? moveNumber, DiceRoll? roll, XgOmissionKind kind, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfLessThan(game, 1);
        if (moveNumber is int move)
            ArgumentOutOfRangeException.ThrowIfLessThan(move, 1, nameof(moveNumber));
        if (roll.HasValue && moveNumber is null)
            throw new ArgumentException("A roll belongs to an omitted checker play, which names its move; an omitted game names neither.", nameof(roll));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is one of XgOmissionKind's.");

        SourceFile = sourceFile;
        Game = game;
        MoveNumber = moveNumber;
        Roll = roll;
        Kind = kind;
        Reason = reason;
    }

    /// <summary>The source file's name with its extension, as the walk was given it — the name its decisions' ids carry.</summary>
    public string SourceFile { get; }

    /// <summary>The 1-based number of the game within the file, counting every game header.</summary>
    public int Game { get; }

    /// <summary>
    /// For an omitted checker play, the 1-based number of its move record
    /// within the game — the number a decision there would carry;
    /// <see langword="null"/> for an omitted game.
    /// </summary>
    public int? MoveNumber { get; }

    /// <summary>
    /// For an omitted checker play, the roll it was made on, where the record
    /// states one; <see langword="null"/> for an omitted game, and for an
    /// illegal-play marker on a record whose dice are no roll (see the class
    /// remarks).
    /// </summary>
    public DiceRoll? Roll { get; }

    /// <summary>Why it was omitted: the kind of problem.</summary>
    public XgOmissionKind Kind { get; }

    /// <summary>
    /// The reason for a reader: the complete message of the rule that
    /// refused, which may hold a line break. Not a stable format; the kind
    /// is <see cref="Kind"/> (see the class remarks).
    /// </summary>
    public string Reason { get; }

    /// <summary>Whether the whole game was omitted (<see cref="XgOmissionKind.InvalidStanding"/>), rather than one checker play of it.</summary>
    public bool IsWholeGame => MoveNumber is null;

    /// <summary>
    /// The omission for a reader — <c>"Illegal play in match.xg, game 3,
    /// move 12, roll 52: …"</c>, the same without <c>", roll 52"</c> where
    /// the record states no roll, or <c>"Invalid standing in match.xg, game
    /// 1: …"</c> — for a log line, a skip list or a test failure; not a wire
    /// form. The reason may carry a line break, so the text may run to two
    /// lines.
    /// </summary>
    public override string ToString() =>
        IsWholeGame
            ? string.Create(CultureInfo.InvariantCulture, $"{Describe(Kind)} in {SourceFile}, game {Game}: {Reason}")
            : Roll is { } roll
                ? string.Create(CultureInfo.InvariantCulture, $"{Describe(Kind)} in {SourceFile}, game {Game}, move {MoveNumber}, roll {roll}: {Reason}")
                : string.Create(CultureInfo.InvariantCulture, $"{Describe(Kind)} in {SourceFile}, game {Game}, move {MoveNumber}: {Reason}");

    /// <summary>A kind as the reader's phrase: the words <see cref="ToString"/> and the walk's log line open with.</summary>
    internal static string Describe(XgOmissionKind kind) => kind switch
    {
        XgOmissionKind.InvalidStanding => "Invalid standing",
        XgOmissionKind.IllegalPlay => "Illegal play",
        XgOmissionKind.CorruptCandidate => "Corrupt candidate",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is one of XgOmissionKind's."),
    };
}
