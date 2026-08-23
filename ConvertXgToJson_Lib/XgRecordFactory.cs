using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The single source of what a record <i>this library synthesizes</i> looks
/// like — the field values real XG writes for a position saved from its
/// editor (decoded from the NoAnalysis / PlayAnalysis / DoubleAnalysis
/// fixtures) plus this library's producer fingerprint. Shared by the two
/// synthesis paths, <see cref="XgpExporter"/> (decision → clean position)
/// and <see cref="XgFileBuilder"/> (intent → match), so the header defaults,
/// the never-analysed sentinels, and the incidental cube pane are encoded
/// once. Anything here is a format fact, not a policy: the callers decide
/// <i>which</i> records to emit; this type decides what a record's
/// XG-conformant defaults are.
/// </summary>
internal static class XgRecordFactory
{
    /// <summary>
    /// The GameId GUID real XG stamps into every RichGameHeader it writes
    /// (<c>.xg</c> and <c>.xgp</c> alike). Constant across files and XG
    /// versions in the fixture corpus (2010 through current), so it is an
    /// XG format constant, not a per-file id.
    /// </summary>
    internal static readonly Guid XgGameId = new("2f5af5e1-e021-4832-a423-ef480ec58a0b");

    /// <summary>
    /// Producer fingerprint written into the match header's Location fields
    /// of every file this library synthesizes. The ecosystem treats Location
    /// as tool provenance — Backgammon Galaxy writes "BackgammonGalaxy" there
    /// (and XG imports those files happily), and
    /// <see cref="Parsing.SaveRecordParser.IsGalaxyMoneyGame"/> keys on it —
    /// so synthesized files self-identify rather than mimicking XG's own
    /// "eXtreme Gammon". Keep the string stable: it is the hook for ever
    /// special-casing our own output the way Galaxy's is special-cased, and
    /// <c>Export_SelfIdentifiesInLocation</c> pins it.
    /// </summary>
    internal const string ProducerLocation = "ConvertXgToJson_Lib";

    /// <summary>Sentinel: analysis level "queued / never ran".</summary>
    internal const int UnanalysedLevel = -100;

    /// <summary>Sentinel: error field "unanalyzed".</summary>
    internal const double UnanalysedError = -1000.0;

    /// <summary>
    /// Constant XG stamps into <see cref="MatchHeaderRecord.Magic"/> of
    /// every save.
    /// </summary>
    private const int MatchHeaderMagic = 1229737284;

    /// <summary>
    /// A match header with XG's editor-save defaults and this library's
    /// provenance. <paramref name="matchLength"/> is the <i>normalized</i>
    /// length (0 = money); the wire sentinel is applied here.
    /// <paramref name="jacoby"/> / <paramref name="beaver"/> apply to money
    /// sessions only and are masked off otherwise. The ANSI twins mirror
    /// the Unicode fields; the writer applies its own truncation.
    /// </summary>
    internal static MatchHeaderRecord MatchHeader(
        int matchLength, bool jacoby, bool beaver,
        string player1, string player2,
        string eventName, DateTime date, int gameId)
    {
        bool isMoney = matchLength <= 0;
        return new MatchHeaderRecord
        {
            EntryType = RecordType.HeaderMatch,
            Player1Ansi = player1,
            Player2Ansi = player2,
            Player1 = player1,
            Player2 = player2,
            MatchLength = isMoney ? MatchHeaderRecord.MoneyMatchLengthSentinel : matchLength,
            Variation = 0,
            Crawford = true,           // XG writes the rule flag on even for money sessions
            Jacoby = isMoney && jacoby,
            Beaver = isMoney && beaver,
            AutoDouble = false,
            Elo1 = 1600,
            Elo2 = 1600,
            Date = date,
            EventAnsi = eventName,
            Event = eventName,
            GameId = gameId,
            CompLevel1 = -1,
            CompLevel2 = -1,
            LocationAnsi = ProducerLocation,
            Location = ProducerLocation,
            GameMode = GameMode.Competition,
            Invert = 1,
            Version = 30,
            Magic = MatchHeaderMagic,
            CommentHeaderMatchIndex = -1,
            CommentFooterMatchIndex = -1,
            IsMoneyMatch = false,      // XG leaves this false; 99999 is the money signal
            SiteId = (SiteId)(-1),
            CubeLimit = 10,            // XG default: max cube 2^10
        };
    }

    /// <summary>
    /// A game header at the given score, starting from
    /// <paramref name="position"/> (player-1-relative). <c>InProgress</c> is
    /// set — no footer follows in either synthesis path.
    /// </summary>
    internal static GameHeaderRecord GameHeader(
        sbyte[] position, int score1, int score2, bool crawfordApplies, int gameNumber) => new()
    {
        EntryType = RecordType.HeaderGame,
        Score1 = score1,
        Score2 = score2,
        CrawfordApplies = crawfordApplies,
        InitialPosition = new PositionEngine { Points = position },
        GameNumber = gameNumber,
        InProgress = true,
        CommentHeaderGameIndex = -1,
        CommentFooterGameIndex = -1,
    };

    /// <summary>
    /// XG's incidental / never-analysed cube pane, exactly as real XG
    /// writes it (NoAnalysis / PlayAnalysis fixtures). Only the pane-state
    /// inputs differ between callers: <paramref name="doubled"/> is XG's
    /// stored double state (1 / 0 / −1 saved cube problem / −2 incidental
    /// pane beside a play), <paramref name="taken"/> the take response
    /// (−1 = none), <paramref name="diceRolled"/> the two-character roll
    /// display ("11" is XG's pre-roll placeholder).
    /// </summary>
    internal static CubeRecord UnanalysedCubeRecord(
        int activePlayer, PositionEngine position, int cubeValueRaw,
        int doubled, int taken, string diceRolled) => new()
    {
        EntryType = RecordType.Cube,
        ActivePlayer = activePlayer,
        Doubled = doubled,
        Taken = taken,
        BeaverAccepted = -1,
        RaccoonAccepted = -1,
        CubeValue = cubeValueRaw,
        Position = position,
        Analysis = UnanalysedDoubleAction(),
        ErrorCube = UnanalysedError,
        DiceRolled = diceRolled,
        ErrorTake = UnanalysedError,
        RolloutIndex = -1,
        AnalyzeLevel = -1,
        ErrorBeaver = UnanalysedError,
        ErrorRaccoon = UnanalysedError,
        AnalyzeLevelRequested = -1,
        TutorCube = -1,
        TutorTake = -1,
        ErrorTutorCube = UnanalysedError,
        ErrorTutorTake = UnanalysedError,
        CommentIndex = -1,
    };

    /// <summary>
    /// A never-analysed move pane carrying the roll: analysis at the
    /// never-analysed level, every error at the sentinel. XG's own shape
    /// for a position saved with dice but no play (PlayAnalysis fixture) has
    /// <paramref name="finalPosition"/> zeroed, an all-zero
    /// <paramref name="moveList"/>, and <paramref name="played"/> false; a
    /// play that was made but not analysed carries its result instead.
    /// </summary>
    internal static MoveRecord UnanalysedMoveRecord(
        int activePlayer, PositionEngine position, PositionEngine finalPosition,
        int[] moveList, bool played, int cubeValueRaw, int die1, int die2) => new()
    {
        EntryType = RecordType.Move,
        InitialPosition = position,
        FinalPosition = finalPosition,
        ActivePlayer = activePlayer,
        MoveList = moveList,
        Dice = [die1, die2],
        CubeValue = cubeValueRaw,
        Analysis = new BestMoveAnalysis { Level = UnanalysedLevel },
        Played = played,
        MoveError = UnanalysedError,
        RolloutIndices = NoRolloutIndices(),
        AnalyzeLevel = -1,
        AnalyzeLevelLuck = -1,
        TutorMoveIndex = -1,
        ErrorTutorMove = UnanalysedError,
        CommentIndex = -1,
    };

    /// <summary>
    /// The "never analysed" cube-analysis block exactly as XG writes it
    /// (NoAnalysis fixture): level −100, IsBeaver −100, all else zero.
    /// </summary>
    internal static DoubleActionAnalysis UnanalysedDoubleAction() => new()
    {
        Level = UnanalysedLevel,
        IsBeaver = UnanalysedLevel,
    };

    /// <summary>
    /// Encodes cube size + owner into XG's signed-log2 record field
    /// (<see cref="CubeRecord.CubeValue"/> / <see cref="MoveRecord.CubeValue"/>):
    /// 0 = centred 1-cube; +n = player 1 owns 2^n; −n = player 2 owns 2^n.
    /// <paramref name="ownerSign"/> follows the ecosystem's player-sign
    /// convention (positive = player 1, negative = player 2, 0 = centred).
    /// The decode direction is <see cref="XgDecisionIterator.CubeValueActual"/>.
    /// </summary>
    internal static int EncodeCube(int cubeSize, int ownerSign)
    {
        int log2 = System.Numerics.BitOperations.Log2((uint)cubeSize);
        return Math.Sign(ownerSign) * log2;
    }

    /// <summary>A fresh 32-slot "no candidate rolled out" index array.</summary>
    internal static int[] NoRolloutIndices() => [.. Enumerable.Repeat(-1, 32)];

    /// <summary>
    /// The outer header of a synthesized file: version 1, XG's constant
    /// GameId, and the caller's display name.
    /// </summary>
    internal static RichGameHeader FileHeader(string saveName) => new()
    {
        HeaderVersion = 1,
        GameId = XgGameId,
        SaveName = saveName,
    };
}
