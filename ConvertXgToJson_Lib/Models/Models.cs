namespace ConvertXgToJson_Lib.Models;

// ---------------------------------------------------------------------------
//  Enumerations
// ---------------------------------------------------------------------------

/// <summary>Type of a save record inside temp.xg.</summary>
public enum RecordType : byte
{
    HeaderMatch  = 0,
    HeaderGame   = 1,
    Cube         = 2,
    Move         = 3,
    FooterGame   = 4,
    FooterMatch  = 5,
    Comment      = 6,   // unused
    Missing      = 7,   // unused
}

public enum ClockType
{
    None       = 0,
    Fischer    = 1,
    Bronstein  = 2,
}

public enum GameMode
{
    Free       = 0,
    Tutor      = 1,
    Teaching   = 2,
    Coaching   = 3,
    Competition = 4,
    IronMan    = 5,
    Custom     = 6,
}

public enum SiteId
{
    GammonSite    = 0,
    FIBS          = 1,
    TrueMoneyGames = 2,
    GridGammon    = 3,
    DailyGammon   = 4,
    NetGammon     = 5,
    VOG           = 6,
    GammonEmpire  = 7,
    ClubGames     = 8,
    PartyGammon   = 9,
    XcitingGames  = 10,
    BGRoom        = 11,
    DiceArena     = 12,
    SafeHarborGames = 13,
    GameAccount   = 14,
    XGMobile      = 15,
}

public enum CurrencyId
{
    Dollar          = 0,
    Euro            = 1,
    SterlingPounds  = 2,
    JapaneseYen     = 3,
    SwissFranc      = 4,
    CanadianDollar  = 5,
}

// ---------------------------------------------------------------------------
//  RichGame outer header  (8232 bytes, packed)
// ---------------------------------------------------------------------------

/// <summary>
/// The outer RichGameFormat wrapper that precedes the compressed XG data.
/// </summary>
public sealed class RichGameHeader
{
    public uint     MagicNumber      { get; init; }   // must be 0x484D4752 ("RGMH")
    public uint     HeaderVersion    { get; init; }
    public uint     HeaderSize       { get; init; }
    public long     ThumbnailOffset  { get; init; }
    public uint     ThumbnailSize    { get; init; }
    public Guid     GameId           { get; init; }
    public string   GameName         { get; init; } = "";
    public string   SaveName         { get; init; } = "";
    public string   LevelName        { get; init; } = "";
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
public sealed class PositionEngine
{
    /// <summary>Raw signed-byte values, indices 0-25.</summary>
    public sbyte[] Points { get; init; } = new sbyte[26];
}

/// <summary>Evaluation result: [loseBG, loseG, loseS, winS, winG, winBG, equity].</summary>
public sealed class EvalResult
{
    public float LoseBackgammon { get; init; }
    public float LoseGammon     { get; init; }
    public float LoseSingle     { get; init; }
    public float WinSingle      { get; init; }
    public float WinGammon      { get; init; }
    public float WinBackgammon  { get; init; }
    public float Equity         { get; init; }
}

/// <summary>Evaluation level descriptor (4 bytes).</summary>
public sealed class EvalLevel
{
    public short Level    { get; init; }
    public bool  IsDouble { get; init; }
}

/// <summary>Clock/time-control settings (32 bytes).</summary>
public sealed class TimeSetting
{
    public ClockType ClockType     { get; init; }
    public bool      PerGame       { get; init; }
    public int       Time1         { get; init; }   // initial time in seconds
    public int       Time2         { get; init; }   // added/reserved time per move
    public int       Penalty       { get; init; }
    public int       TimeLeft1     { get; init; }
    public int       TimeLeft2     { get; init; }
    public int       PenaltyMoney  { get; init; }
}

// ---------------------------------------------------------------------------
//  Engine analysis structures
// ---------------------------------------------------------------------------

/// <summary>Full checker-play analysis result (2184 bytes).</summary>
public sealed class BestMoveAnalysis
{
    public PositionEngine          Position      { get; init; } = new();
    public int[]                   Dice          { get; init; } = new int[2];   // [0]=die1 [1]=die2
    public int                     Level         { get; init; }
    public int[]                   Score         { get; init; } = new int[2];
    public int                     Cube          { get; init; }
    public int                     CubePosition  { get; init; }
    public int                     Crawford      { get; init; }
    public int                     Jacoby        { get; init; }
    public int                     MoveCount     { get; init; }
    public PositionEngine[]        PositionsPlayed { get; init; } = [];
    /// <summary>Move list per candidate: [from1,die1, from2,die2, …, -1].</summary>
    public sbyte[][]               Moves         { get; init; } = [];
    public EvalLevel[]             EvalLevels    { get; init; } = [];
    public EvalResult[]            Evals         { get; init; } = [];
    public bool                    Irrelevant    { get; init; }
    public sbyte                   Choice1Ply    { get; init; }
    public sbyte                   Choice3Ply    { get; init; }
}

/// <summary>Cube-action analysis result (132 bytes).</summary>
public sealed class DoubleActionAnalysis
{
    public PositionEngine Position     { get; init; } = new();
    public int            Level        { get; init; }
    public int[]          Score        { get; init; } = new int[2];
    public int            Cube         { get; init; }
    public int            CubePosition { get; init; }
    public int            Jacoby       { get; init; }
    public short          Crawford     { get; init; }
    public short          FlagDouble   { get; init; }
    public short          IsBeaver     { get; init; }
    public EvalResult     EvalNoDouble { get; init; } = new();
    public float          EquityNoDouble   { get; init; }
    public float          EquityDoubleTake { get; init; }
    public float          EquityDoubleDrop { get; init; }
    public short          LevelRequest    { get; init; }
    public short          DoubleChoice3   { get; init; }
    public EvalResult     EvalDoubleTake  { get; init; } = new();
}

// ---------------------------------------------------------------------------
//  TSaveRec variants
// ---------------------------------------------------------------------------

/// <summary>Base class for all save-record variants.</summary>
public abstract class SaveRecord
{
    public RecordType EntryType { get; init; }
}

/// <summary>tsHeaderMatch (record type 0) – match-level metadata.</summary>
public sealed class MatchHeaderRecord : SaveRecord
{
    // ANSI (XG1 compat)
    public string   Player1Ansi    { get; init; } = "";
    public string   Player2Ansi    { get; init; } = "";
    public int      MatchLength    { get; init; }   // 99999 = unlimited
    public int      Variation      { get; init; }   // 0=BG,1=Nack,2=Hyper,3=Longgammon
    public bool     Crawford       { get; init; }
    public bool     Jacoby         { get; init; }
    public bool     Beaver         { get; init; }
    public bool     AutoDouble     { get; init; }
    public double   Elo1           { get; init; }
    public double   Elo2           { get; init; }
    public int      Experience1    { get; init; }
    public int      Experience2    { get; init; }
    public DateTime Date           { get; init; }
    public string   EventAnsi      { get; init; } = "";
    public int      GameId         { get; init; }
    public int      CompLevel1     { get; init; }
    public int      CompLevel2     { get; init; }
    public bool     CountForElo    { get; init; }
    public bool     AddToProfile1  { get; init; }
    public bool     AddToProfile2  { get; init; }
    public string   LocationAnsi   { get; init; } = "";
    public GameMode GameMode       { get; init; }
    public bool     Imported       { get; init; }
    public string   RoundAnsi      { get; init; } = "";
    public int      Invert         { get; init; }
    public int      Version        { get; init; }
    public int      Magic          { get; init; }
    public int      MoneyInitGames { get; init; }
    public int[]    MoneyInitScore { get; init; } = new int[2];
    public bool     Entered        { get; init; }
    public bool     Counted        { get; init; }
    public bool     UnratedImport  { get; init; }
    public int      CommentHeaderMatchIndex { get; init; }
    public int      CommentFooterMatchIndex { get; init; }
    public bool     IsMoneyMatch   { get; init; }
    public float    WinMoney       { get; init; }
    public float    LoseMoney      { get; init; }
    public CurrencyId Currency     { get; init; }
    public float    FeeMoney       { get; init; }
    public float    TableStake     { get; init; }
    public SiteId   SiteId         { get; init; }
    public int      CubeLimit      { get; init; }
    public int      AutoDoubleMax  { get; init; }
    public bool     Transcribed    { get; init; }
    // Unicode (v24+)
    public string   Event          { get; init; } = "";
    public string   Player1        { get; init; } = "";
    public string   Player2        { get; init; } = "";
    public string   Location       { get; init; } = "";
    public string   Round          { get; init; } = "";
    public TimeSetting TimeSetting { get; init; } = new();
    // v26
    public int      TotalTimeDelayMoves       { get; init; }
    public int      TotalTimeDelayCubes       { get; init; }
    public int      TotalTimeDelayMovesDone   { get; init; }
    public int      TotalTimeDelayCubesDone   { get; init; }
    // v30
    public string   Transcriber    { get; init; } = "";
}

/// <summary>tsHeaderGame (record type 1) – per-game header.</summary>
public sealed class GameHeaderRecord : SaveRecord
{
    public int            Score1              { get; init; }
    public int            Score2              { get; init; }
    public bool           CrawfordApplies     { get; init; }
    public PositionEngine InitialPosition     { get; init; } = new();
    public int            GameNumber          { get; init; }
    public bool           InProgress          { get; init; }
    public int            CommentHeaderGameIndex { get; init; }
    public int            CommentFooterGameIndex { get; init; }
    public int            NumberOfAutoDoubles { get; init; }
}

/// <summary>tsCube (record type 2) – cube action.</summary>
public sealed class CubeRecord : SaveRecord
{
    public int                 ActivePlayer     { get; init; }   // 1=player1, -1=player2
    public int                 Doubled          { get; init; }
    public int                 Taken            { get; init; }   // 0=no, 1=yes, 2=beaver
    public int                 BeaverAccepted   { get; init; }
    public int                 RaccoonAccepted  { get; init; }
    public int                 CubeValue        { get; init; }
    public PositionEngine      Position         { get; init; } = new();
    public DoubleActionAnalysis Analysis        { get; init; } = new();
    public double              ErrorCube        { get; init; }
    public string              DiceRolled       { get; init; } = "";
    public double              ErrorTake        { get; init; }
    public int                 RolloutIndex     { get; init; }
    public int                 ComputerChoice   { get; init; }
    public int                 AnalyzeLevel     { get; init; }
    public double              ErrorBeaver      { get; init; }
    public double              ErrorRaccoon     { get; init; }
    public int                 AnalyzeLevelRequested { get; init; }
    public int                 InvalidDecision  { get; init; }
    public sbyte               TutorCube        { get; init; }
    public sbyte               TutorTake        { get; init; }
    public double              ErrorTutorCube   { get; init; }
    public double              ErrorTutorTake   { get; init; }
    public bool                Flagged          { get; init; }
    public int                 CommentIndex     { get; init; }
    public bool                Edited           { get; init; }
    public bool                TimeDelayed      { get; init; }
    public bool                TimeDelayDone    { get; init; }
    public int                 NumberOfAutoDoubles { get; init; }
    public int                 TimeBotLeft      { get; init; }
    public int                 TimeTopLeft      { get; init; }
}

/// <summary>tsMove (record type 3) – checker play.</summary>
public sealed class MoveRecord : SaveRecord
{
    public PositionEngine   InitialPosition  { get; init; } = new();
    public PositionEngine   FinalPosition    { get; init; } = new();
    public int              ActivePlayer     { get; init; }   // 1=player1, -1=player2
    /// <summary>[from1,die1, from2,die2, …, -1 terminator], 8 ints.</summary>
    public int[]            MoveList         { get; init; } = new int[8];
    public int[]            Dice             { get; init; } = new int[2];
    public int              CubeValue        { get; init; }
    public double           ErrorMove        { get; init; }
    public int              CandidateCount   { get; init; }
    public BestMoveAnalysis Analysis         { get; init; } = new();
    public bool             Played           { get; init; }
    public double           MoveError        { get; init; }
    public double           LuckError        { get; init; }
    public int              ComputerChoice   { get; init; }
    public double           InitialEquity    { get; init; }
    public int[]            RolloutIndices   { get; init; } = new int[32];
    public int              AnalyzeLevel     { get; init; }
    public int              AnalyzeLevelLuck { get; init; }
    public int              InvalidDecision  { get; init; }
    public PositionEngine   TutorPosition    { get; init; } = new();
    public sbyte            TutorMoveIndex   { get; init; }
    public double           ErrorTutorMove   { get; init; }
    public bool             Flagged          { get; init; }
    public int              CommentIndex     { get; init; }
    public bool             Edited           { get; init; }
    public uint             TimeDelayBits    { get; init; }
    public uint             TimeDelayDoneBits { get; init; }
    public int              NumberOfAutoDoubles { get; init; }
}

/// <summary>tsFooterGame (record type 4) – end-of-game summary.</summary>
public sealed class GameFooterRecord : SaveRecord
{
    public int      Score1           { get; init; }
    public int      Score2           { get; init; }
    public bool     CrawfordAppliesNext { get; init; }
    public int      Winner           { get; init; }   // +1=player1, -1=player2
    public int      PointsWon        { get; init; }
    public int      Termination      { get; init; }   // 0=Drop,1=Single,2=Gammon,3=BG,+100=Resign
    public double   ErrorResign      { get; init; }
    public double   ErrorTakeResign  { get; init; }
    public double[] FinalEval        { get; init; } = new double[7];
    public int      EvalLevel        { get; init; }
}

/// <summary>tsFooterMatch (record type 5) – end-of-match summary.</summary>
public sealed class MatchFooterRecord : SaveRecord
{
    public int      Score1    { get; init; }
    public int      Score2    { get; init; }
    public int      Winner    { get; init; }
    public double   Elo1      { get; init; }
    public double   Elo2      { get; init; }
    public int      Exp1      { get; init; }
    public int      Exp2      { get; init; }
    public DateTime Date      { get; init; }
}

// ---------------------------------------------------------------------------
//  Rollout context  (2184 bytes)
// ---------------------------------------------------------------------------

/// <summary>Full rollout context record from temp.xgr.</summary>
public sealed class RolloutContext
{
    // inputs
    public bool   Truncated           { get; init; }
    public bool   ErrorLimited        { get; init; }
    public int    TruncateLevel       { get; init; }
    public int    MinRolls            { get; init; }
    public double ErrorLimit          { get; init; }
    public int    MaxRolls            { get; init; }
    public int    Level1              { get; init; }
    public int    Level2              { get; init; }
    public int    LevelCut            { get; init; }
    public bool   VarianceReduction   { get; init; }
    public bool   Cubeless            { get; init; }
    public bool   TimeLimited         { get; init; }
    public int    Level1Cube          { get; init; }
    public int    Level2Cube          { get; init; }
    public uint   TimeLimit           { get; init; }
    public int    TruncateBO          { get; init; }
    public int    RandomSeed          { get; init; }
    public int    RandomSeedInitial   { get; init; }
    public bool   RollBoth            { get; init; }
    public float  SearchInterval      { get; init; }
    public bool   FirstRoll           { get; init; }
    public bool   DoDouble            { get; init; }
    public bool   Extended            { get; init; }

    // outputs
    public int    GamesRolled         { get; init; }
    public bool   DoubleFirst         { get; init; }

    public double[] Sum1              { get; init; } = new double[37];
    public double[] SumSquare1        { get; init; } = new double[37];
    public double[] Sum2              { get; init; } = new double[37];
    public double[] SumSquare2        { get; init; } = new double[37];
    public double[] Stdev1            { get; init; } = new double[37];
    public double[] Stdev2            { get; init; } = new double[37];
    public int[]    RolledPerDice     { get; init; } = new int[37];

    public float    Error1            { get; init; }
    public float    Error2            { get; init; }
    public float[]  Result1           { get; init; } = new float[7];
    public float[]  Result2           { get; init; } = new float[7];
    public float    Mwc1              { get; init; }
    public float    Mwc2              { get; init; }

    public int      PrevLevel         { get; init; }
    public float[]  PrevEval          { get; init; } = new float[7];
    public float    PrevND            { get; init; }
    public float    PrevD             { get; init; }
    public float    Duration          { get; init; }

    public int      LevelTrunc        { get; init; }
    public int      GamesRolledDouble { get; init; }

    public int      MultipleMin       { get; init; }
    public bool     MultipleStopAll   { get; init; }
    public bool     MultipleStopOne   { get; init; }
    public float    MultipleStopAllValue { get; init; }
    public float    MultipleStopOneValue { get; init; }

    public bool     AsTake            { get; init; }
    public int      Rotation          { get; init; }
    public bool     UserInterrupted   { get; init; }
    public ushort   VersionMajor      { get; init; }
    public ushort   VersionMinor      { get; init; }
}

// ---------------------------------------------------------------------------
//  Top-level parsed file
// ---------------------------------------------------------------------------

/// <summary>Everything extracted from a single .XG file.</summary>
public sealed class XgFile
{
    public RichGameHeader   Header          { get; init; } = new();
    /// <summary>All save records from temp.xg in order.</summary>
    public List<SaveRecord> Records         { get; init; } = [];
    /// <summary>All rollout contexts from temp.xgr in order.</summary>
    public List<RolloutContext> Rollouts    { get; init; } = [];
    /// <summary>Raw comment lines from temp.xgc (RTF, one entry per record).</summary>
    public List<string>     Comments        { get; init; } = [];
}
