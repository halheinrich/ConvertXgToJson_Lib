namespace ConvertXgToJson_Lib;

/// <summary>
/// Represents a single checker-play or cube decision extracted from an XG file,
/// ready for CSV export or analysis.
/// </summary>
public sealed class DecisionRow
{
    /// <summary>Full XGID string including position and game state.</summary>
    public string Xgid            { get; init; } = "";

    /// <summary>Equity error (cost of the played move/cube action vs best). 0 = best play.</summary>
    public double Error           { get; init; }

    /// <summary>Match score encoded as away-scores, e.g. "5a3a" = 5-away 3-away. "money" for money games.</summary>
    public string MatchScore      { get; init; } = "";

    /// <summary>Match length (0 for money game).</summary>
    public int MatchLength        { get; init; }

    /// <summary>Name of the player who made this decision.</summary>
    public string Player          { get; init; } = "";

    /// <summary>Match identifier (derived from file name).</summary>
    public string Match           { get; init; } = "";

    /// <summary>Game number within the match.</summary>
    public int Game               { get; init; }

    /// <summary>Sequential move number within the game.</summary>
    public int MoveNum            { get; init; }

    /// <summary>Dice roll as two-digit int (e.g. 62 for 6-2). 0 for cube decisions.</summary>
    public int Roll               { get; init; }

    /// <summary>Analysis depth label (e.g. "XG Roller++", "3-ply", "1-ply").</summary>
    public string AnalysisDepth   { get; init; } = "";

    /// <summary>Best-play equity at this decision point.</summary>
    public double Equity          { get; init; }

    /// <summary>Whether this is a cube decision (true) or checker play (false).</summary>
    public bool IsCube            { get; init; }

    /// <summary>Formats this row as a CSV line (no header).</summary>
    public string ToCsvLine() =>
        $"{Xgid},{Error:G4},{MatchScore},{MatchLength},{Player},{Match},{Game},{MoveNum},{Roll},{AnalysisDepth},{Equity:G4}";

    /// <summary>CSV header line.</summary>
    public static string CsvHeader =>
        "XGID,Error,Match Score,Length,Player,Match,Game,Move Num,Roll,Analysis Depth,Equity";
}
