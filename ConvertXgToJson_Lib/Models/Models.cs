using System.Text.Json.Serialization;

namespace ConvertXgToJson_Lib.Models;

// ---------------------------------------------------------------------------
//  Enumerations
// ---------------------------------------------------------------------------

/// <summary>
/// Type of a save record inside temp.xg. Stored as the EntryType byte at
/// offset 8 of every 2560-byte TSaveRec; the parser dispatches on it and
/// the writer stamps it back.
/// </summary>
internal enum RecordType : byte
{
    /// <summary>tsHeaderMatch — match-level metadata; always the first record.</summary>
    HeaderMatch  = 0,
    /// <summary>tsHeaderGame — per-game header (score, initial position).</summary>
    HeaderGame   = 1,
    /// <summary>tsCube — a cube decision (double / take / drop) with its analysis pane.</summary>
    Cube         = 2,
    /// <summary>tsMove — a checker play with its analysis pane.</summary>
    Move         = 3,
    /// <summary>tsFooterGame — end-of-game summary.</summary>
    FooterGame   = 4,
    /// <summary>tsFooterMatch — end-of-match summary.</summary>
    FooterMatch  = 5,
    /// <summary>tsComment — declared by XG but never observed in files; comments live in temp.xgc.</summary>
    Comment      = 6,
    /// <summary>tsMissing — declared by XG but never observed in files.</summary>
    Missing      = 7,
}

/// <summary>Clock discipline of a <see cref="TimeSetting"/>.</summary>
internal enum ClockType
{
    /// <summary>No clock.</summary>
    None       = 0,
    /// <summary>Fischer increment: <see cref="TimeSetting.Time2"/> added per move.</summary>
    Fischer    = 1,
    /// <summary>Bronstein delay: up to <see cref="TimeSetting.Time2"/> per move not banked.</summary>
    Bronstein  = 2,
}

/// <summary>XG play mode recorded in the match header.</summary>
internal enum GameMode
{
    /// <summary>Free play.</summary>
    Free       = 0,
    /// <summary>Tutor mode (XG warns on errors).</summary>
    Tutor      = 1,
    /// <summary>Teaching mode.</summary>
    Teaching   = 2,
    /// <summary>Coaching mode.</summary>
    Coaching   = 3,
    /// <summary>Competition mode (no assistance); what <see cref="XgpExporter"/> stamps.</summary>
    Competition = 4,
    /// <summary>Iron Man mode.</summary>
    IronMan    = 5,
    /// <summary>Custom assistance settings.</summary>
    Custom     = 6,
}

/// <summary>
/// Online play site of an imported match, per XG's site table. <c>-1</c>
/// (no named member) means "not site-imported" — real XG and
/// <see cref="XgpExporter"/> both write <c>(SiteId)(-1)</c> for local saves,
/// so consumers must tolerate values outside the named range.
/// </summary>
internal enum SiteId
{
    /// <summary>GammonSite.</summary>
    GammonSite    = 0,
    /// <summary>FIBS (First Internet Backgammon Server).</summary>
    FIBS          = 1,
    /// <summary>TrueMoneyGames.</summary>
    TrueMoneyGames = 2,
    /// <summary>GridGammon.</summary>
    GridGammon    = 3,
    /// <summary>DailyGammon.</summary>
    DailyGammon   = 4,
    /// <summary>NetGammon.</summary>
    NetGammon     = 5,
    /// <summary>Vog.ru.</summary>
    VOG           = 6,
    /// <summary>GammonEmpire.</summary>
    GammonEmpire  = 7,
    /// <summary>ClubGames.</summary>
    ClubGames     = 8,
    /// <summary>PartyGammon.</summary>
    PartyGammon   = 9,
    /// <summary>XcitingGames.</summary>
    XcitingGames  = 10,
    /// <summary>BGRoom.</summary>
    BGRoom        = 11,
    /// <summary>DiceArena.</summary>
    DiceArena     = 12,
    /// <summary>SafeHarborGames.</summary>
    SafeHarborGames = 13,
    /// <summary>GameAccount.</summary>
    GameAccount   = 14,
    /// <summary>XG Mobile.</summary>
    XGMobile      = 15,
}

/// <summary>Stake currency of a money session, per XG's currency table.</summary>
internal enum CurrencyId
{
    /// <summary>US dollar.</summary>
    Dollar          = 0,
    /// <summary>Euro.</summary>
    Euro            = 1,
    /// <summary>Pound sterling.</summary>
    SterlingPounds  = 2,
    /// <summary>Japanese yen.</summary>
    JapaneseYen     = 3,
    /// <summary>Swiss franc.</summary>
    SwissFranc      = 4,
    /// <summary>Canadian dollar.</summary>
    CanadianDollar  = 5,
}

// ---------------------------------------------------------------------------
//  RichGame outer header  (8232 bytes, packed)
// ---------------------------------------------------------------------------

/// <summary>
/// The outer RichGameFormat wrapper that precedes the compressed XG data —
/// 8232 bytes, <b>packed</b> (no field alignment; see the Pitfalls entry on
/// <see cref="ThumbnailOffset"/>). The embedded thumbnail is not modeled:
/// the writer always omits it and real XG tolerates that.
/// </summary>
internal sealed class RichGameHeader
{
    /// <summary>File signature; must be 0x484D4752 ("RGMH" little-endian).</summary>
    public uint     MagicNumber      { get; init; }
    /// <summary>Header format version; real XG and <see cref="XgpExporter"/> write 1.</summary>
    public uint     HeaderVersion    { get; init; }
    /// <summary>Total header size in bytes (8232); the compressed payload begins here.</summary>
    public uint     HeaderSize       { get; init; }
    /// <summary>
    /// Absolute file offset of the embedded thumbnail, an Int64 at packed
    /// offset 12 — writing it with 8-byte alignment would insert padding and
    /// corrupt the header. 0 when no thumbnail (always, for written files).
    /// </summary>
    public long     ThumbnailOffset  { get; init; }
    /// <summary>Thumbnail byte length; 0 when no thumbnail.</summary>
    public uint     ThumbnailSize    { get; init; }
    /// <summary>
    /// Save identity GUID. Real XG stamps one constant into every
    /// <c>.xgp</c> (2f5af5e1-e021-4832-a423-ef480ec58a0b, stable 2010→
    /// current — an XG format constant, not a per-file id); the exporter
    /// reproduces it.
    /// </summary>
    public Guid     GameId           { get; init; }
    /// <summary>Display name of the game/match as XG stores it.</summary>
    public string   GameName         { get; init; } = "";
    /// <summary>
    /// Display name of the save, e.g. "Position: 7 point match 2-5" —
    /// <see cref="XgpExporter"/> derives it from match form and score.
    /// </summary>
    public string   SaveName         { get; init; } = "";
    /// <summary>Analysis-level display name as XG stores it.</summary>
    public string   LevelName        { get; init; } = "";
    /// <summary>
    /// Plain-text comment field. XG mirrors the match-header comment here
    /// when saving a commented match to <c>.xgp</c> (the RTF original stays
    /// in the comment table; this is the extraction).
    /// </summary>
    public string   Comments         { get; init; } = "";
}

// ---------------------------------------------------------------------------
//  Shared small types
// ---------------------------------------------------------------------------

/// <summary>
/// The 26-element checker position array.
/// Index 0 = opponent bar, 1-24 = points, 25 = player bar.
/// Positive = player's checkers, negative = opponent's checkers.
/// </summary>
internal sealed class PositionEngine
{
    /// <summary>Raw signed-byte values, indices 0-25.</summary>
    public sbyte[] Points { get; init; } = new sbyte[26];
}

/// <summary>
/// XG's seven-float evaluation vector, in stored order:
/// [loseBG, loseG, loseS, winS, winG, winBG, equity]. The win/lose fields
/// are the on-roll player's outcome probabilities exactly as XG stores
/// them; the iterator surfaces them verbatim onto
/// <c>PlayCandidate</c> / <c>DecisionData</c> (WinSingle → WinPct, etc.).
/// </summary>
internal sealed class EvalResult
{
    /// <summary>Probability the on-roll player loses a backgammon.</summary>
    public float LoseBackgammon { get; init; }
    /// <summary>Probability the on-roll player loses a gammon.</summary>
    public float LoseGammon     { get; init; }
    /// <summary>Loss probability (slot 2 of the vector); surfaced downstream as LosePct.</summary>
    public float LoseSingle     { get; init; }
    /// <summary>Win probability (slot 3 of the vector); surfaced downstream as WinPct.</summary>
    public float WinSingle      { get; init; }
    /// <summary>Probability the on-roll player wins a gammon.</summary>
    public float WinGammon      { get; init; }
    /// <summary>Probability the on-roll player wins a backgammon.</summary>
    public float WinBackgammon  { get; init; }
    /// <summary>
    /// <b>Cubeless</b> equity scalar. The cubeful equities of a cube
    /// decision live on <see cref="DoubleActionAnalysis"/>
    /// (<see cref="DoubleActionAnalysis.EquityNoDouble"/> family) — do not
    /// conflate the two.
    /// </summary>
    public float Equity         { get; init; }
}

/// <summary>
/// Evaluation level descriptor (4 bytes). <see cref="Level"/> uses XG's
/// level taxonomy: 1–7 = N-ply (12 = "3-ply Red"), 1000/1001/1002 =
/// XG Roller / + / ++, 999/998 = Book V1/V2, 100 = rollout sentinel,
/// −100 = never analysed. <c>XgDecisionIterator.ResolveDepthInfo</c> is the
/// single projection of this taxonomy into labels / ranks / depth classes.
/// </summary>
internal sealed class EvalLevel
{
    /// <summary>XG analysis level (taxonomy above).</summary>
    public short Level    { get; init; }
    /// <summary>XG's stored double-eval flag for this candidate.</summary>
    public bool  IsDouble { get; init; }
}

/// <summary>Clock/time-control settings (32 bytes), as stored by XG.</summary>
internal sealed class TimeSetting
{
    /// <summary>Clock discipline; <see cref="ClockType.None"/> when unclocked.</summary>
    public ClockType ClockType     { get; init; }
    /// <summary>True when the clock resets per game rather than per match.</summary>
    public bool      PerGame       { get; init; }
    /// <summary>Initial time in seconds.</summary>
    public int       Time1         { get; init; }
    /// <summary>Added/reserved time per move in seconds (increment or delay).</summary>
    public int       Time2         { get; init; }
    /// <summary>Penalty points applied on flag fall.</summary>
    public int       Penalty       { get; init; }
    /// <summary>Player 1 time remaining, seconds.</summary>
    public int       TimeLeft1     { get; init; }
    /// <summary>Player 2 time remaining, seconds.</summary>
    public int       TimeLeft2     { get; init; }
    /// <summary>Money penalty applied on flag fall in a money session.</summary>
    public int       PenaltyMoney  { get; init; }
}

// ---------------------------------------------------------------------------
//  Engine analysis structures
// ---------------------------------------------------------------------------

/// <summary>
/// Full checker-play analysis pane (2184 bytes): the candidate arrays
/// <see cref="Moves"/>, <see cref="EvalLevels"/>, <see cref="Evals"/>, and
/// <see cref="PositionsPlayed"/> are <b>rank-coupled</b> — index i across
/// all four describes the same candidate, in XG's native ranking order.
/// That order is <b>not</b> strictly equity-descending; use
/// <c>XgDecisionIterator.FindBestByEquityIndex</c> to locate the best
/// candidate rather than assuming index 0.
/// </summary>
internal sealed class BestMoveAnalysis
{
    /// <summary>Position the analysis evaluated (XG's stored snapshot).</summary>
    public PositionEngine          Position      { get; init; } = new();
    /// <summary>Dice of the analysed roll: [0]=die1, [1]=die2.</summary>
    public int[]                   Dice          { get; init; } = new int[2];
    /// <summary>
    /// Analysis level of the pane (see <see cref="EvalLevel"/> for the
    /// taxonomy). −100 = never analysed; the iterator gates emission on
    /// this, not on any error field.
    /// </summary>
    public int                     Level         { get; init; }
    /// <summary>Score snapshot XG stored with the pane: [0]=player 1, [1]=player 2.</summary>
    public int[]                   Score         { get; init; } = new int[2];
    /// <summary>Cube value snapshot as XG stored it.</summary>
    public int                     Cube          { get; init; }
    /// <summary>Cube ownership snapshot as XG stored it.</summary>
    public int                     CubePosition  { get; init; }
    /// <summary>Crawford flag snapshot as XG stored it.</summary>
    public int                     Crawford      { get; init; }
    /// <summary>Jacoby flag snapshot as XG stored it.</summary>
    public int                     Jacoby        { get; init; }
    /// <summary>
    /// Number of candidates XG analysed; the live prefix of the candidate
    /// arrays. 0 means no candidates (the iterator skips such panes).
    /// </summary>
    public int                     MoveCount     { get; init; }
    /// <summary>Resulting position per candidate (rank-coupled).</summary>
    public PositionEngine[]        PositionsPlayed { get; init; } = [];
    /// <summary>
    /// Move list per candidate: adjacent (from, to) pairs in active-player
    /// POV, 0-indexed point values. Sentinels: from == -1 terminates;
    /// from == 24 is bar entry; to == -1 is bear off. Regular points are
    /// value + 1 (so raw 0 = point 1, raw 23 = point 24). Two non-play
    /// sentinel pairs exist — (0, 0) is XG's no-legal-move (dance) encoding
    /// and (-100, -100) its illegal-play workaround — filtered at the
    /// iterator boundary, never fed to leaf decoding.
    /// </summary>
    public sbyte[][]               Moves         { get; init; } = [];
    /// <summary>Analysis depth per candidate (rank-coupled).</summary>
    public EvalLevel[]             EvalLevels    { get; init; } = [];
    /// <summary>Evaluation vector per candidate (rank-coupled).</summary>
    public EvalResult[]            Evals         { get; init; } = [];
    /// <summary>XG's stored "analysis irrelevant" flag (e.g. forced play).</summary>
    public bool                    Irrelevant    { get; init; }
    /// <summary>XG's stored candidate choice at 1-ply.</summary>
    public sbyte                   Choice1Ply    { get; init; }
    /// <summary>XG's stored candidate choice at 3-ply.</summary>
    public sbyte                   Choice3Ply    { get; init; }
}

/// <summary>
/// Cube-action analysis pane (132 bytes). The three <c>Equity*</c> scalars
/// are <b>cubeful</b>; the <see cref="EvalResult.Equity"/> inside
/// <see cref="EvalNoDouble"/> / <see cref="EvalDoubleTake"/> is cubeless.
/// Gate "is analysed" on <see cref="Level"/>, never on
/// <see cref="LevelRequest"/> — XG writes LevelRequest when the user
/// <i>asks</i> and Level when the analysis <i>runs</i>, so a queued-but-
/// unfinished rollout has Level == −100 with a non-zero LevelRequest.
/// </summary>
internal sealed class DoubleActionAnalysis
{
    /// <summary>Position the analysis evaluated (XG's stored snapshot).</summary>
    public PositionEngine Position     { get; init; } = new();
    /// <summary>
    /// Analysis level that actually ran (see <see cref="EvalLevel"/> for
    /// the taxonomy); −100 = never analysed. The IsAnalysed gate.
    /// </summary>
    public int            Level        { get; init; }
    /// <summary>Score snapshot XG stored with the pane: [0]=player 1, [1]=player 2.</summary>
    public int[]          Score        { get; init; } = new int[2];
    /// <summary>Cube value snapshot as XG stored it.</summary>
    public int            Cube         { get; init; }
    /// <summary>Cube ownership snapshot as XG stored it.</summary>
    public int            CubePosition { get; init; }
    /// <summary>Jacoby flag snapshot as XG stored it.</summary>
    public int            Jacoby       { get; init; }
    /// <summary>Crawford flag snapshot as XG stored it.</summary>
    public short          Crawford     { get; init; }
    /// <summary>XG's stored double-recommendation flag.</summary>
    public short          FlagDouble   { get; init; }
    /// <summary>XG's stored beaver flag; −100 in a never-analysed pane.</summary>
    public short          IsBeaver     { get; init; }
    /// <summary>Evaluation vector for "no double" (cubeless equity scalar).</summary>
    public EvalResult     EvalNoDouble { get; init; } = new();
    /// <summary>Cubeful equity of "no double".</summary>
    public float          EquityNoDouble   { get; init; }
    /// <summary>Cubeful equity of "double, take".</summary>
    public float          EquityDoubleTake { get; init; }
    /// <summary>Cubeful equity of "double, drop".</summary>
    public float          EquityDoubleDrop { get; init; }
    /// <summary>
    /// Analysis level the user <i>requested</i> — non-zero on a queued
    /// analysis that never ran. Not the IsAnalysed gate; see the class
    /// summary.
    /// </summary>
    public short          LevelRequest    { get; init; }
    /// <summary>XG's stored cube choice at 3-ply.</summary>
    public short          DoubleChoice3   { get; init; }
    /// <summary>Evaluation vector for "double, take" (cubeless equity scalar).</summary>
    public EvalResult     EvalDoubleTake  { get; init; } = new();
}

// ---------------------------------------------------------------------------
//  TSaveRec variants
// ---------------------------------------------------------------------------

/// <summary>
/// Base class for all save-record variants. Every record occupies a
/// complete zero-padded 2560-byte TSaveRec on disk; the byte layout is
/// deliberately encoded twice — <c>Parsing/SaveRecordParser</c> and
/// <c>Writing/SaveRecordWriter</c> must change together, field-for-field.
/// </summary>
internal abstract class SaveRecord
{
    /// <summary>Record variant tag (byte at offset 8 of the raw record).</summary>
    public RecordType EntryType { get; init; }
}

/// <summary>
/// tsHeaderMatch (record type 0) – match-level metadata. XG requires it as
/// the first record of the stream; both real XG and this library's writer
/// and iterator reject files where it is not.
///
/// <para>String fields come in ANSI/Unicode twins: the ANSI forms are XG1
/// compatibility copies (player names truncate at 40 bytes, Latin1 with
/// <c>?</c> replacement), the Unicode forms (v24+) are authoritative
/// (players truncate at 128 chars). Writers must keep each twin pair in
/// agreement.</para>
/// </summary>
internal sealed class MatchHeaderRecord : SaveRecord
{
    /// <summary>ANSI (XG1 compat) twin of <see cref="Player1"/>; 40-byte Pascal string.</summary>
    public string   Player1Ansi    { get; init; } = "";
    /// <summary>ANSI (XG1 compat) twin of <see cref="Player2"/>; 40-byte Pascal string.</summary>
    public string   Player2Ansi    { get; init; } = "";
    /// <summary>
    /// XG's money-session sentinel for <see cref="MatchLength"/> — the single
    /// spelling of the wire-format magic number. This is the <i>raw</i> rule
    /// (<c>99999</c> = money) and is independent of the <i>normalized</i> rule
    /// (<c>0</c> = money, spelled <c>IsMoneyGame</c>): the two are the same
    /// concept expressed at different layers, so they are single-sourced
    /// separately by design.
    /// </summary>
    internal const int MoneyMatchLengthSentinel = 99999;

    /// <summary>
    /// Match length in points; <b><see cref="MoneyMatchLengthSentinel"/> is XG's
    /// money-session sentinel</b> (normalized to 0 downstream). Backgammon Galaxy
    /// money games — which abuse this field as a cube limit — are detected and
    /// rewritten to the sentinel at parse time, so past the parser one money
    /// representation exists.
    /// </summary>
    public int      MatchLength    { get; init; }
    /// <summary>Game variant: 0=Backgammon, 1=Nackgammon, 2=Hypergammon, 3=Longgammon.</summary>
    public int      Variation      { get; init; }
    /// <summary>
    /// Crawford <b>rule</b> flag for the match — not "this game is the
    /// Crawford game" (that is <see cref="GameHeaderRecord.CrawfordApplies"/>).
    /// XG writes it true even for money sessions.
    /// </summary>
    public bool     Crawford       { get; init; }
    /// <summary>Jacoby rule flag (money sessions).</summary>
    public bool     Jacoby         { get; init; }
    /// <summary>Beaver rule flag (money sessions).</summary>
    public bool     Beaver         { get; init; }
    /// <summary>Auto-double rule flag (money sessions).</summary>
    public bool     AutoDouble     { get; init; }
    /// <summary>Player 1 Elo rating as recorded by XG.</summary>
    public double   Elo1           { get; init; }
    /// <summary>Player 2 Elo rating as recorded by XG.</summary>
    public double   Elo2           { get; init; }
    /// <summary>Player 1 experience (games count) as recorded by XG.</summary>
    public int      Experience1    { get; init; }
    /// <summary>Player 2 experience (games count) as recorded by XG.</summary>
    public int      Experience2    { get; init; }
    /// <summary>
    /// Match date. On disk this is a Delphi TDateTime (a double of days),
    /// so only double-quantized values round-trip tick-for-tick — see the
    /// Pitfalls entry before asserting date equality on synthetic values.
    /// </summary>
    public DateTime Date           { get; init; }
    /// <summary>ANSI (XG1 compat) twin of <see cref="Event"/>.</summary>
    public string   EventAnsi      { get; init; } = "";
    /// <summary>
    /// Per-save id. Real XG writes a random value; <see cref="XgpExporter"/>
    /// writes a deterministic CRC32 over position/dice/cube so exports stay
    /// byte-stable. XG treats it as opaque.
    /// </summary>
    public int      GameId         { get; init; }
    /// <summary>XG engine level of player 1 when computer-played; −1 for a human.</summary>
    public int      CompLevel1     { get; init; }
    /// <summary>XG engine level of player 2 when computer-played; −1 for a human.</summary>
    public int      CompLevel2     { get; init; }
    /// <summary>Whether the session counts toward Elo, as recorded by XG.</summary>
    public bool     CountForElo    { get; init; }
    /// <summary>Whether the session adds to player 1's profile stats.</summary>
    public bool     AddToProfile1  { get; init; }
    /// <summary>Whether the session adds to player 2's profile stats.</summary>
    public bool     AddToProfile2  { get; init; }
    /// <summary>ANSI (XG1 compat) twin of <see cref="Location"/>.</summary>
    public string   LocationAnsi   { get; init; } = "";
    /// <summary>XG play mode of the session.</summary>
    public GameMode GameMode       { get; init; }
    /// <summary>True when XG imported the match rather than playing it.</summary>
    public bool     Imported       { get; init; }
    /// <summary>ANSI (XG1 compat) twin of <see cref="Round"/>.</summary>
    public string   RoundAnsi      { get; init; } = "";
    /// <summary>XG's stored display-inversion value; passed through verbatim.</summary>
    public int      Invert         { get; init; }
    /// <summary>Save-format version; 30 is current (v24+ adds the Unicode twins,
    /// v26 the time-delay totals, v30 <see cref="Transcriber"/>).</summary>
    public int      Version        { get; init; }
    /// <summary>Constant 1229737284, stamped by XG into every save.</summary>
    public int      Magic          { get; init; }
    /// <summary>Money session: games already played when the session started.</summary>
    public int      MoneyInitGames { get; init; }
    /// <summary>Money session: starting score, [0]=player 1, [1]=player 2.</summary>
    public int[]    MoneyInitScore { get; init; } = new int[2];
    /// <summary>XG's stored entered flag; passed through verbatim.</summary>
    public bool     Entered        { get; init; }
    /// <summary>XG's stored counted flag; passed through verbatim.</summary>
    public bool     Counted        { get; init; }
    /// <summary>True when the import is excluded from ratings.</summary>
    public bool     UnratedImport  { get; init; }
    /// <summary>Comment-table index of the match-header comment; −1 = none.</summary>
    public int      CommentHeaderMatchIndex { get; init; }
    /// <summary>Comment-table index of the match-footer comment; −1 = none.</summary>
    public int      CommentFooterMatchIndex { get; init; }
    /// <summary>
    /// Money-match flag. <b>Not the raw XG byte:</b> the parser OR's in
    /// Backgammon Galaxy money-game detection, so a Galaxy money game is
    /// indistinguishable from a native XG money session past the parser.
    /// (XG itself leaves this false and signals money via
    /// <see cref="MatchLength"/> = 99999.)
    /// </summary>
    public bool     IsMoneyMatch   { get; init; }
    /// <summary>Money session: amount won per point, as recorded by XG.</summary>
    public float    WinMoney       { get; init; }
    /// <summary>Money session: amount lost per point, as recorded by XG.</summary>
    public float    LoseMoney      { get; init; }
    /// <summary>Stake currency of a money session.</summary>
    public CurrencyId Currency     { get; init; }
    /// <summary>Money session: table fee, as recorded by XG.</summary>
    public float    FeeMoney       { get; init; }
    /// <summary>Money session: table stake, as recorded by XG.</summary>
    public float    TableStake     { get; init; }
    /// <summary>Import site; −1 (outside the named range) = not site-imported.</summary>
    public SiteId   SiteId         { get; init; }
    /// <summary>Maximum cube as log2 (10 = XG's default 2^10 = 1024).</summary>
    public int      CubeLimit      { get; init; }
    /// <summary>Maximum number of auto-doubles allowed.</summary>
    public int      AutoDoubleMax  { get; init; }
    /// <summary>True when the match was hand-transcribed (see <see cref="Transcriber"/>).</summary>
    public bool     Transcribed    { get; init; }
    /// <summary>Event name (Unicode, authoritative).</summary>
    public string   Event          { get; init; } = "";
    /// <summary>Player 1 name (Unicode, authoritative; 128-char writer truncation).</summary>
    public string   Player1        { get; init; } = "";
    /// <summary>Player 2 name (Unicode, authoritative; 128-char writer truncation).</summary>
    public string   Player2        { get; init; } = "";
    /// <summary>
    /// Location (Unicode, authoritative). The ecosystem treats this as the
    /// <b>producer fingerprint</b>: Backgammon Galaxy writes
    /// "BackgammonGalaxy" (keyed on by the Galaxy money-game repair),
    /// <see cref="XgpExporter"/> writes "ConvertXgToJson_Lib". Treat a
    /// producer's string as a breaking-change surface.
    /// </summary>
    public string   Location       { get; init; } = "";
    /// <summary>Round name (Unicode, authoritative).</summary>
    public string   Round          { get; init; } = "";
    /// <summary>Clock settings of the session.</summary>
    public TimeSetting TimeSetting { get; init; } = new();
    /// <summary>v26: total time-delayed checker plays, as recorded by XG.</summary>
    public int      TotalTimeDelayMoves       { get; init; }
    /// <summary>v26: total time-delayed cube actions, as recorded by XG.</summary>
    public int      TotalTimeDelayCubes       { get; init; }
    /// <summary>v26: completed time-delayed checker plays, as recorded by XG.</summary>
    public int      TotalTimeDelayMovesDone   { get; init; }
    /// <summary>v26: completed time-delayed cube actions, as recorded by XG.</summary>
    public int      TotalTimeDelayCubesDone   { get; init; }
    /// <summary>v30: transcriber credit for a hand-transcribed match.</summary>
    public string   Transcriber    { get; init; } = "";
}

/// <summary>tsHeaderGame (record type 1) – per-game header.</summary>
internal sealed class GameHeaderRecord : SaveRecord
{
    /// <summary>Player 1 score entering the game.</summary>
    public int            Score1              { get; init; }
    /// <summary>Player 2 score entering the game.</summary>
    public int            Score2              { get; init; }
    /// <summary>True when <b>this game</b> is the Crawford game (contrast the
    /// match-level rule flag <see cref="MatchHeaderRecord.Crawford"/>).</summary>
    public bool           CrawfordApplies     { get; init; }
    /// <summary>
    /// Position the game starts from. The standard opening for a played
    /// game; a saved-position export "starts" at the saved position (XG's
    /// position-editor pattern).
    /// </summary>
    public PositionEngine InitialPosition     { get; init; } = new();
    /// <summary>1-based game number within the match.</summary>
    public int            GameNumber          { get; init; }
    /// <summary>True while the game has no footer yet (in progress when saved).</summary>
    public bool           InProgress          { get; init; }
    /// <summary>Comment-table index of the game-header comment; −1 = none.</summary>
    public int            CommentHeaderGameIndex { get; init; }
    /// <summary>Comment-table index of the game-footer comment; −1 = none.</summary>
    public int            CommentFooterGameIndex { get; init; }
    /// <summary>Auto-doubles applied in this game (money sessions).</summary>
    public int            NumberOfAutoDoubles { get; init; }
}

/// <summary>
/// tsCube (record type 2) – cube action. <see cref="ActivePlayer"/> is the
/// <b>doubler</b>, so take/drop decisions are anchored to the doubler with
/// no special-casing. Error fields use the −1000 "unanalyzed" sentinel —
/// treat a value &gt; −999.0 as present; <c>!= 0</c> / <c>HasValue</c>
/// checks silently misread unanalyzed positions as zero-error.
/// </summary>
internal sealed class CubeRecord : SaveRecord
{
    /// <summary>The doubler: 1 = player 1, −1 = player 2 (≥ 0 is player 1 everywhere).</summary>
    public int                 ActivePlayer     { get; init; }
    /// <summary>
    /// Double state as XG stores it: 1 = doubled, 0 = no double (a roll).
    /// XG's <c>.xgp</c> editor saves also use −1 (a saved pre-roll cube
    /// problem) and −2 (the incidental cube pane beside a play decision);
    /// the exporter reproduces both.
    /// </summary>
    public int                 Doubled          { get; init; }
    /// <summary>Take response: 0 = drop, 1 = take, 2 = beaver; −1 = no response.</summary>
    public int                 Taken            { get; init; }
    /// <summary>Beaver response as XG stores it; −1 = none.</summary>
    public int                 BeaverAccepted   { get; init; }
    /// <summary>Raccoon response as XG stores it; −1 = none.</summary>
    public int                 RaccoonAccepted  { get; init; }
    /// <summary>
    /// Cube state as a <b>signed log2</b>: 0 = centred 1-cube; +n =
    /// player 1 owns 2^n; −n = player 2 owns 2^n. "Centred above 1"
    /// (auto-doubled money play) has no representation — the reason
    /// <see cref="XgpExporter"/> throws on it.
    /// </summary>
    public int                 CubeValue        { get; init; }
    /// <summary>Position at the decision, from player 1's perspective.</summary>
    public PositionEngine      Position         { get; init; } = new();
    /// <summary>The cube analysis pane (may be a never-analysed sentinel pane).</summary>
    public DoubleActionAnalysis Analysis        { get; init; } = new();
    /// <summary>Equity error of the cube action; −1000 = unanalyzed.</summary>
    public double              ErrorCube        { get; init; }
    /// <summary>
    /// Two-character roll display string (e.g. "63"). XG writes "11" as
    /// the placeholder in a pre-roll cube pane.
    /// </summary>
    public string              DiceRolled       { get; init; } = "";
    /// <summary>Equity error of the take/drop response; −1000 = unanalyzed.</summary>
    public double              ErrorTake        { get; init; }
    /// <summary>
    /// Index into <see cref="XgFile.Rollouts"/>; −1 = not rolled out.
    /// A cube rollout occupies an <b>adjacent context pair</b> with this
    /// index pointing at the <i>second</i> leg — anything copying or
    /// filtering rollout tables must carry both legs, adjacent and in
    /// order.
    /// </summary>
    public int                 RolloutIndex     { get; init; }
    /// <summary>XG's stored engine choice for the action.</summary>
    public int                 ComputerChoice   { get; init; }
    /// <summary>Analysis level of the cube action as XG recorded it at play time.</summary>
    public int                 AnalyzeLevel     { get; init; }
    /// <summary>Equity error of a beaver response; −1000 = unanalyzed.</summary>
    public double              ErrorBeaver      { get; init; }
    /// <summary>Equity error of a raccoon response; −1000 = unanalyzed.</summary>
    public double              ErrorRaccoon     { get; init; }
    /// <summary>Requested analysis level (parallel to
    /// <see cref="DoubleActionAnalysis.LevelRequest"/> — not the IsAnalysed gate).</summary>
    public int                 AnalyzeLevelRequested { get; init; }
    /// <summary>XG's stored invalid-decision marker (imported-match validation).</summary>
    public int                 InvalidDecision  { get; init; }
    /// <summary>Tutor-mode cube verdict. Runtime-initialized by XG on load —
    /// imported sources carry zeros, so field-level comparisons exclude the
    /// tutor family.</summary>
    public sbyte               TutorCube        { get; init; }
    /// <summary>Tutor-mode take verdict (runtime-initialized; see <see cref="TutorCube"/>).</summary>
    public sbyte               TutorTake        { get; init; }
    /// <summary>Tutor-mode cube error (runtime-initialized; see <see cref="TutorCube"/>).</summary>
    public double              ErrorTutorCube   { get; init; }
    /// <summary>Tutor-mode take error (runtime-initialized; see <see cref="TutorCube"/>).</summary>
    public double              ErrorTutorTake   { get; init; }
    /// <summary>User flag ("marked move") as stored by XG.</summary>
    public bool                Flagged          { get; init; }
    /// <summary>Comment-table index of this decision's comment; −1 = none.
    /// Remapped (not copied raw) by the slice exporter.</summary>
    public int                 CommentIndex     { get; init; }
    /// <summary>True when the record was hand-edited in XG.</summary>
    public bool                Edited           { get; init; }
    /// <summary>Clock: whether this action was time-delayed.</summary>
    public bool                TimeDelayed      { get; init; }
    /// <summary>Clock: whether the time delay completed.</summary>
    public bool                TimeDelayDone    { get; init; }
    /// <summary>Auto-doubles applied so far in the game (money sessions).</summary>
    public int                 NumberOfAutoDoubles { get; init; }
    /// <summary>Clock: bottom player's remaining time, seconds.</summary>
    public int                 TimeBotLeft      { get; init; }
    /// <summary>Clock: top player's remaining time, seconds.</summary>
    public int                 TimeTopLeft      { get; init; }
}

/// <summary>
/// tsMove (record type 3) – checker play. Error fields use the −1000
/// "unanalyzed" sentinel — treat a value &gt; −999.0 as present.
/// </summary>
internal sealed class MoveRecord : SaveRecord
{
    /// <summary>Position before the play, from player 1's perspective.</summary>
    public PositionEngine   InitialPosition  { get; init; } = new();
    /// <summary>Position after the played move; zeroed when no play was made
    /// (e.g. a saved position with dice but no move).</summary>
    public PositionEngine   FinalPosition    { get; init; } = new();
    /// <summary>The player on roll: 1 = player 1, −1 = player 2 (≥ 0 is player 1 everywhere).</summary>
    public int              ActivePlayer     { get; init; }
    /// <summary>
    /// Adjacent (from, to) pairs for the played move, in active-player POV,
    /// 0-indexed point values; same encoding as <see cref="BestMoveAnalysis.Moves"/>.
    /// 8 ints with -1 filler.
    /// </summary>
    public int[]            MoveList         { get; init; } = new int[8];
    /// <summary>Dice of the roll: [0]=die1, [1]=die2.</summary>
    public int[]            Dice             { get; init; } = new int[2];
    /// <summary>Cube state as a signed log2 (same encoding as
    /// <see cref="CubeRecord.CubeValue"/>).</summary>
    public int              CubeValue        { get; init; }
    /// <summary>XG's ErrMove slot preceding the analysis block, as stored;
    /// −1000 = unanalyzed. Downstream consumers read <see cref="MoveError"/>.</summary>
    public double           ErrorMove        { get; init; }
    /// <summary>Number of legal-move candidates XG recorded for the roll.</summary>
    public int              CandidateCount   { get; init; }
    /// <summary>The checker-play analysis pane (may be never-analysed).</summary>
    public BestMoveAnalysis Analysis         { get; init; } = new();
    /// <summary>True when the move was actually played (vs. only analysed).</summary>
    public bool             Played           { get; init; }
    /// <summary>Equity error of the played move — the field the iterator
    /// surfaces as UserPlayError; −1000 = unanalyzed.</summary>
    public double           MoveError        { get; init; }
    /// <summary>Luck value of the roll as XG stores it; −1000 = not computed.</summary>
    public double           LuckError        { get; init; }
    /// <summary>XG's stored engine choice for the play.</summary>
    public int              ComputerChoice   { get; init; }
    /// <summary>Equity before the roll as XG stores it.</summary>
    public double           InitialEquity    { get; init; }
    /// <summary>
    /// Per-candidate indices into <see cref="XgFile.Rollouts"/>,
    /// rank-coupled with the <see cref="Analysis"/> candidate arrays;
    /// −1 = that candidate was not rolled out. Move-candidate rollouts are
    /// individually indexed — no adjacent-pair rule (that is cube-specific,
    /// see <see cref="CubeRecord.RolloutIndex"/>).
    /// </summary>
    public int[]            RolloutIndices   { get; init; } = new int[32];
    /// <summary>Analysis level of the play as XG recorded it at play time.</summary>
    public int              AnalyzeLevel     { get; init; }
    /// <summary>Analysis level of the luck computation.</summary>
    public int              AnalyzeLevelLuck { get; init; }
    /// <summary>XG's stored invalid-decision marker (imported-match validation).</summary>
    public int              InvalidDecision  { get; init; }
    /// <summary>Tutor-mode position snapshot. Runtime-initialized by XG on
    /// load — imported sources carry zeros, so field-level comparisons
    /// exclude the tutor family.</summary>
    public PositionEngine   TutorPosition    { get; init; } = new();
    /// <summary>Tutor-mode move index (runtime-initialized; see <see cref="TutorPosition"/>).</summary>
    public sbyte            TutorMoveIndex   { get; init; }
    /// <summary>Tutor-mode move error (runtime-initialized; see <see cref="TutorPosition"/>).</summary>
    public double           ErrorTutorMove   { get; init; }
    /// <summary>User flag ("marked move") as stored by XG.</summary>
    public bool             Flagged          { get; init; }
    /// <summary>Comment-table index of this decision's comment; −1 = none.
    /// Remapped (not copied raw) by the slice exporter.</summary>
    public int              CommentIndex     { get; init; }
    /// <summary>True when the record was hand-edited in XG.</summary>
    public bool             Edited           { get; init; }
    /// <summary>Clock: per-move time-delay bitfield as XG stores it.</summary>
    public uint             TimeDelayBits    { get; init; }
    /// <summary>Clock: completed time-delay bitfield as XG stores it.</summary>
    public uint             TimeDelayDoneBits { get; init; }
    /// <summary>Auto-doubles applied so far in the game (money sessions).</summary>
    public int              NumberOfAutoDoubles { get; init; }
}

/// <summary>tsFooterGame (record type 4) – end-of-game summary.</summary>
internal sealed class GameFooterRecord : SaveRecord
{
    /// <summary>Player 1 score after the game.</summary>
    public int      Score1           { get; init; }
    /// <summary>Player 2 score after the game.</summary>
    public int      Score2           { get; init; }
    /// <summary>True when the <b>next</b> game is the Crawford game.</summary>
    public bool     CrawfordAppliesNext { get; init; }
    /// <summary>Game winner: +1 = player 1, −1 = player 2.</summary>
    public int      Winner           { get; init; }
    /// <summary>Points the winner scored (cube × gammon multiplier).</summary>
    public int      PointsWon        { get; init; }
    /// <summary>How the game ended: 0=Drop, 1=Single, 2=Gammon, 3=Backgammon; +100 = by resignation.</summary>
    public int      Termination      { get; init; }
    /// <summary>Equity error of the resignation; −1000 = not applicable/unanalyzed.</summary>
    public double   ErrorResign      { get; init; }
    /// <summary>Equity error of accepting the resignation; −1000 = not applicable/unanalyzed.</summary>
    public double   ErrorTakeResign  { get; init; }
    /// <summary>Final seven-double evaluation vector as XG stores it
    /// (same slot order as <see cref="EvalResult"/>).</summary>
    public double[] FinalEval        { get; init; } = new double[7];
    /// <summary>Analysis level of <see cref="FinalEval"/>.</summary>
    public int      EvalLevel        { get; init; }
}

/// <summary>tsFooterMatch (record type 5) – end-of-match summary.</summary>
internal sealed class MatchFooterRecord : SaveRecord
{
    /// <summary>Player 1 final score.</summary>
    public int      Score1    { get; init; }
    /// <summary>Player 2 final score.</summary>
    public int      Score2    { get; init; }
    /// <summary>Match winner: +1 = player 1, −1 = player 2.</summary>
    public int      Winner    { get; init; }
    /// <summary>Player 1 Elo after the match.</summary>
    public double   Elo1      { get; init; }
    /// <summary>Player 2 Elo after the match.</summary>
    public double   Elo2      { get; init; }
    /// <summary>Player 1 experience after the match.</summary>
    public int      Exp1      { get; init; }
    /// <summary>Player 2 experience after the match.</summary>
    public int      Exp2      { get; init; }
    /// <summary>Match end date (Delphi TDateTime on disk; see
    /// <see cref="MatchHeaderRecord.Date"/> for the round-trip caveat).</summary>
    public DateTime Date      { get; init; }
}

// ---------------------------------------------------------------------------
//  Rollout context  (2184 bytes)
// ---------------------------------------------------------------------------

/// <summary>
/// Full rollout context record from temp.xgr — the settings and
/// accumulated results of one rollout leg, referenced by index from
/// <see cref="CubeRecord.RolloutIndex"/> / <see cref="MoveRecord.RolloutIndices"/>.
/// Carried byte-faithfully for round-trip; the producer consumes only the
/// depth-label inputs (<see cref="Level2"/>, <see cref="Level1"/>,
/// <see cref="LevelTrunc"/>, <see cref="GamesRolled"/>). The 37-slot
/// accumulator arrays are XG-internal (per first-roll bucketing) and are
/// not interpreted by this library.
/// </summary>
internal sealed class RolloutContext
{
    /// <summary>Input: whether the rollout truncates after a fixed depth.</summary>
    public bool   Truncated           { get; init; }
    /// <summary>Input: whether the rollout stops on reaching an error bound.</summary>
    public bool   ErrorLimited        { get; init; }
    /// <summary>Input: truncation depth in plies when <see cref="Truncated"/>.</summary>
    public int    TruncateLevel       { get; init; }
    /// <summary>Input: minimum games to roll.</summary>
    public int    MinRolls            { get; init; }
    /// <summary>Input: stop error bound when <see cref="ErrorLimited"/>.</summary>
    public double ErrorLimit          { get; init; }
    /// <summary>Input: maximum games to roll.</summary>
    public int    MaxRolls            { get; init; }
    /// <summary>
    /// Input: checker-play analysis level of the first leg phase. Fallback
    /// source (after <see cref="Level2"/>) of the rollout's "inner ply"
    /// depth label.
    /// </summary>
    public int    Level1              { get; init; }
    /// <summary>
    /// Input: checker-play analysis level of the second leg phase. Primary
    /// source of the rollout's "inner ply" depth label (the stored value is
    /// ply − 1: raw 2 labels a 3-ply rollout).
    /// </summary>
    public int    Level2              { get; init; }
    /// <summary>Input: analysis level after the cut point, as stored by XG.</summary>
    public int    LevelCut            { get; init; }
    /// <summary>Input: variance-reduction flag.</summary>
    public bool   VarianceReduction   { get; init; }
    /// <summary>Input: cubeless-rollout flag.</summary>
    public bool   Cubeless            { get; init; }
    /// <summary>Input: whether the rollout stops on a wall-clock budget.</summary>
    public bool   TimeLimited         { get; init; }
    /// <summary>Input: cube-decision analysis level of the first leg phase.</summary>
    public int    Level1Cube          { get; init; }
    /// <summary>Input: cube-decision analysis level of the second leg phase.</summary>
    public int    Level2Cube          { get; init; }
    /// <summary>Input: wall-clock budget when <see cref="TimeLimited"/>.</summary>
    public uint   TimeLimit           { get; init; }
    /// <summary>Input: bear-off truncation setting as stored by XG.</summary>
    public int    TruncateBO          { get; init; }
    /// <summary>Input: random seed in use.</summary>
    public int    RandomSeed          { get; init; }
    /// <summary>Input: random seed as originally configured.</summary>
    public int    RandomSeedInitial   { get; init; }
    /// <summary>Input: whether both first-roll orders are rolled.</summary>
    public bool   RollBoth            { get; init; }
    /// <summary>Input: search-interval setting as stored by XG.</summary>
    public float  SearchInterval      { get; init; }
    /// <summary>Input: whether the first roll is fixed.</summary>
    public bool   FirstRoll           { get; init; }
    /// <summary>Input: whether cube actions are played inside the rollout.</summary>
    public bool   DoDouble            { get; init; }
    /// <summary>Input: XG's extended-rollout flag.</summary>
    public bool   Extended            { get; init; }

    /// <summary>Output: games actually rolled — the trial count shown in
    /// depth labels ("Rollout: 1296 trials …").</summary>
    public int    GamesRolled         { get; init; }
    /// <summary>Output: whether the double branch was rolled first.</summary>
    public bool   DoubleFirst         { get; init; }

    /// <summary>Accumulator: per-bucket equity sums, branch 1 (XG-internal).</summary>
    public double[] Sum1              { get; init; } = new double[37];
    /// <summary>Accumulator: per-bucket squared sums, branch 1 (XG-internal).</summary>
    public double[] SumSquare1        { get; init; } = new double[37];
    /// <summary>Accumulator: per-bucket equity sums, branch 2 (XG-internal).</summary>
    public double[] Sum2              { get; init; } = new double[37];
    /// <summary>Accumulator: per-bucket squared sums, branch 2 (XG-internal).</summary>
    public double[] SumSquare2        { get; init; } = new double[37];
    /// <summary>Accumulator: per-bucket standard deviations, branch 1 (XG-internal).</summary>
    public double[] Stdev1            { get; init; } = new double[37];
    /// <summary>Accumulator: per-bucket standard deviations, branch 2 (XG-internal).</summary>
    public double[] Stdev2            { get; init; } = new double[37];
    /// <summary>Accumulator: games rolled per bucket (XG-internal).</summary>
    public int[]    RolledPerDice     { get; init; } = new int[37];

    /// <summary>Output: standard error, branch 1.</summary>
    public float    Error1            { get; init; }
    /// <summary>Output: standard error, branch 2.</summary>
    public float    Error2            { get; init; }
    /// <summary>Output: seven-float result vector, branch 1 (same slot order
    /// as <see cref="EvalResult"/>).</summary>
    public float[]  Result1           { get; init; } = new float[7];
    /// <summary>Output: seven-float result vector, branch 2.</summary>
    public float[]  Result2           { get; init; } = new float[7];
    /// <summary>Output: match-winning chance, branch 1.</summary>
    public float    Mwc1              { get; init; }
    /// <summary>Output: match-winning chance, branch 2.</summary>
    public float    Mwc2              { get; init; }

    /// <summary>Prior static evaluation's level, as stored by XG.</summary>
    public int      PrevLevel         { get; init; }
    /// <summary>Prior static evaluation's seven-float vector.</summary>
    public float[]  PrevEval          { get; init; } = new float[7];
    /// <summary>Prior static no-double equity.</summary>
    public float    PrevND            { get; init; }
    /// <summary>Prior static double equity.</summary>
    public float    PrevD             { get; init; }
    /// <summary>Output: rollout duration in seconds.</summary>
    public float    Duration          { get; init; }

    /// <summary>Input: analysis level at the truncation point; last fallback
    /// source of the rollout's "inner ply" depth label.</summary>
    public int      LevelTrunc        { get; init; }
    /// <summary>Output: games rolled on the double branch.</summary>
    public int      GamesRolledDouble { get; init; }

    /// <summary>Input: minimum games before a multiple-rollout stop rule fires.</summary>
    public int      MultipleMin       { get; init; }
    /// <summary>Input: multiple-rollout "stop all" rule flag.</summary>
    public bool     MultipleStopAll   { get; init; }
    /// <summary>Input: multiple-rollout "stop one" rule flag.</summary>
    public bool     MultipleStopOne   { get; init; }
    /// <summary>Input: threshold for the "stop all" rule.</summary>
    public float    MultipleStopAllValue { get; init; }
    /// <summary>Input: threshold for the "stop one" rule.</summary>
    public float    MultipleStopOneValue { get; init; }

    /// <summary>Input: whether the rollout evaluates the position as a take.</summary>
    public bool     AsTake            { get; init; }
    /// <summary>Input: dice-rotation setting as stored by XG.</summary>
    public int      Rotation          { get; init; }
    /// <summary>Output: true when the user interrupted the rollout.</summary>
    public bool     UserInterrupted   { get; init; }
    /// <summary>XG major version that produced the rollout.</summary>
    public ushort   VersionMajor      { get; init; }
    /// <summary>XG minor version that produced the rollout.</summary>
    public ushort   VersionMinor      { get; init; }
}

// ---------------------------------------------------------------------------
//  Top-level parsed file
// ---------------------------------------------------------------------------

/// <summary>
/// A parsed or synthesized XG file — the opaque handle every public surface
/// of this library exchanges: <see cref="XgFileReader"/> produces one,
/// <see cref="XgFileWriter"/> / <see cref="XgpExporter"/> /
/// <see cref="XgDecisionIterator"/> consume one, and
/// <see cref="XgFileBuilder"/> is the one public way to make one in memory.
///
/// <para>
/// The XG record model behind it is internal by design: consumers say what
/// a match <i>is</i> through the builder and never see the on-disk record
/// structure. The collections below carry <see cref="JsonIncludeAttribute"/>
/// so <see cref="XgFileReader.ToJson"/> / <see cref="XgFileReader.ReadJson"/>
/// round-trip the same JSON document they always did — the document shape
/// is a published output contract and is pinned by the test suite.
/// </para>
/// </summary>
public sealed class XgFile
{
    /// <summary>
    /// In-assembly construction only: the reader, the exporter's record
    /// assembly, the JSON deserializer, and <see cref="XgFileBuilder"/>.
    /// </summary>
    [JsonConstructor]
    internal XgFile() { }

    /// <summary>The outer RichGameFormat header (thumbnail bytes not carried).</summary>
    [JsonInclude]
    internal RichGameHeader   Header          { get; init; } = new();
    /// <summary>All save records from temp.xg in order.</summary>
    [JsonInclude]
    internal List<SaveRecord> Records         { get; init; } = [];
    /// <summary>All rollout contexts from temp.xgr in order.</summary>
    [JsonInclude]
    internal List<RolloutContext> Rollouts    { get; init; } = [];
    /// <summary>
    /// Raw comment lines from temp.xgc (RTF, one entry per record), joined
    /// by the records' comment indices. May carry unreferenced leftovers —
    /// XG bundles its working temp files wholesale, so orphaned entries are
    /// format reality, not a parse failure. An interior empty entry is a
    /// real (empty) comment; skipping it desyncs every later index.
    /// </summary>
    [JsonInclude]
    internal List<string>     Comments        { get; init; } = [];
}
