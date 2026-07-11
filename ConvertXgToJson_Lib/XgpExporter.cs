using System.Numerics;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Exports a single decision as an XG <c>.xgp</c> position file — the
/// semantic translation from the consumer-level <see cref="BgDecisionData"/>
/// to the XG record shapes, layered over <see cref="XgFileWriter"/>.
/// ("Exporter" per the ecosystem convention: an Exporter translates
/// semantics; a Writer/Reader mirrors byte layout.)
///
/// Usage:
///   XgpExporter.Write(decision, stream);        // Stream / WASM friendly
///   byte[] bytes = XgpExporter.ToBytes(decision);
///   XgpExporter.WriteFile(decision, "pos.xgp"); // path convenience
///
/// <para>
/// The exported file is a <b>clean, unanalyzed</b> position: XG re-analyzes
/// on import, so no analysis panes are carried over. Field values mirror
/// what real XG writes for a position saved from its position editor
/// (verified against the fixture corpus), with the analysis blocks holding
/// XG's own "never analysed" sentinels (<c>Level = -100</c>, error fields
/// <c>-1000</c>).
/// </para>
///
/// <para>
/// <b>XG-import-only, by design.</b> Because the export is unanalyzed, this
/// library's own <see cref="XgDecisionIterator"/> yields <b>zero</b> decisions
/// for it — rule 1 of the <c>.xgp</c> emission policy skips unanalysed
/// decisions. Exported files are for interchange with real XG (and other
/// XG-format consumers), not for re-ingestion by this ecosystem; the
/// ecosystem's native re-ingestible format remains the
/// <see cref="BgDecisionData"/> JSON serialization. Carrying the source
/// decision's analysis through (which would make exports visible to the
/// iterator) is a booked follow-up — see INSTRUCTIONS.md.
/// </para>
///
/// <para>
/// A play decision (dice present) exports a cube record plus a move record
/// carrying the dice; a cube decision exports the cube record only —
/// mirroring which panes XG itself writes.
/// </para>
/// </summary>
public static class XgpExporter
{
    /// <summary>
    /// The GameId GUID real XG stamps into every <c>.xgp</c> RichGameHeader.
    /// Constant across files and XG versions in the fixture corpus (2010
    /// through current), so it is an XG format constant, not a per-file id.
    /// </summary>
    private static readonly Guid XgpGameId = new("2f5af5e1-e021-4832-a423-ef480ec58a0b");

    /// <summary>Sentinel: analysis level "queued / never ran".</summary>
    private const int UnanalysedLevel = -100;

    /// <summary>Sentinel: error field "unanalyzed".</summary>
    private const double UnanalysedError = -1000.0;

    // ------------------------------------------------------------------ //
    //  Public API
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Writes <paramref name="decision"/> to <paramref name="output"/> as a
    /// complete <c>.xgp</c> file.
    /// </summary>
    /// <param name="decision">The decision to export. See <see cref="Validate"/> remarks for requirements.</param>
    /// <param name="output">Destination stream; written sequentially, left open.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the decision's position is not a 26-element board, its
    /// cube size is not a positive power of two, a play decision carries
    /// dice outside 1–6, or match-play needs are outside 1..MatchLength.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown for a centred cube above 1 (an auto-doubled money position):
    /// the XG record encoding carries cube ownership in the sign of a
    /// log2 field, which cannot express "centred, above 1" without XG's
    /// auto-double bookkeeping. Not supported in v1.
    /// </exception>
    public static void Write(BgDecisionData decision, Stream output)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(output);
        XgFileWriter.Write(ToXgFile(decision), output);
    }

    /// <summary>
    /// Serializes <paramref name="decision"/> to <c>.xgp</c> bytes. Preferred
    /// entry point for browser-hosted (WASM) consumers with no filesystem.
    /// </summary>
    public static byte[] ToBytes(BgDecisionData decision)
    {
        using var ms = new MemoryStream();
        Write(decision, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Convenience overload: writes <paramref name="decision"/> to
    /// <paramref name="path"/>, overwriting any existing file.
    /// </summary>
    public static void WriteFile(BgDecisionData decision, string path)
    {
        using var fs = File.Create(path);
        Write(decision, fs);
    }

    // ------------------------------------------------------------------ //
    //  Decision → record set
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Builds the <see cref="XgFile"/> record set for <paramref name="decision"/>.
    /// Internal so tests can assert on records directly; consumers go through
    /// the byte-level entry points and never touch XG record internals.
    /// </summary>
    internal static XgFile ToXgFile(BgDecisionData decision)
    {
        Validate(decision);

        bool isMoney = decision.Descriptive.MatchLength <= 0;
        int matchLength = decision.Descriptive.MatchLength;
        int score1 = isMoney ? 0 : matchLength - decision.Position.OnRollNeeds;
        int score2 = isMoney ? 0 : matchLength - decision.Position.OpponentNeeds;
        var (jacoby, beaver) = isMoney ? MoneyFlags(decision.Xgid) : (false, false);
        bool crawfordApplies = !isMoney && decision.Position.IsCrawford;

        string player1 = NameOrDefault(decision.Descriptive.OnRollName, "Player 1");
        string player2 = NameOrDefault(decision.Descriptive.OpponentName, "Player 2");

        sbyte[] position = ToPositionEngine(decision.Position.Mop);
        int cubeRaw = EncodeCube(decision.Position.CubeSize, decision.Position.CubeOwner);

        var records = new List<SaveRecord>
        {
            BuildMatchHeader(decision, isMoney, matchLength, jacoby, beaver, player1, player2),
            BuildGameHeader(position, score1, score2, crawfordApplies),
            BuildCubeRecord(decision, position, cubeRaw),
        };
        if (!decision.Decision.IsCube)
            records.Add(BuildMoveRecord(decision, position, cubeRaw));

        return new XgFile
        {
            Header = new RichGameHeader
            {
                HeaderVersion = 1,
                GameId = XgpGameId,
                SaveName = BuildSaveName(isMoney, matchLength, score1, score2, jacoby),
            },
            Records = records,
        };
    }

    private static void Validate(BgDecisionData decision)
    {
        if (decision.Position.Mop.Count != 26)
            throw new ArgumentException(
                $"Position.Mop must have 26 elements (got {decision.Position.Mop.Count}).", nameof(decision));

        int cubeSize = decision.Position.CubeSize;
        if (cubeSize < 1 || !BitOperations.IsPow2((uint)cubeSize))
            throw new ArgumentException(
                $"Position.CubeSize must be a positive power of two (got {cubeSize}).", nameof(decision));
        if (cubeSize > 1 && decision.Position.CubeOwner == CubeOwner.Centered)
            throw new NotSupportedException(
                "A centred cube above 1 (auto-doubled money position) is not representable " +
                "in the XG record encoding without auto-double bookkeeping; not supported.");

        if (!decision.Decision.IsCube)
        {
            var dice = decision.Decision.Dice;
            if (dice.Count < 2 || dice[0] is < 1 or > 6 || dice[1] is < 1 or > 6)
                throw new ArgumentException(
                    "A play decision requires two dice in the range 1–6.", nameof(decision));
        }

        int matchLength = decision.Descriptive.MatchLength;
        if (matchLength > 0)
        {
            if (decision.Position.OnRollNeeds < 1 || decision.Position.OnRollNeeds > matchLength
                || decision.Position.OpponentNeeds < 1 || decision.Position.OpponentNeeds > matchLength)
                throw new ArgumentException(
                    $"Match-play needs must be within 1..{matchLength} " +
                    $"(got {decision.Position.OnRollNeeds}/{decision.Position.OpponentNeeds}).", nameof(decision));
        }
    }

    // ------------------------------------------------------------------ //
    //  Record builders — values mirror what real XG writes for a position
    //  saved from its editor (NoAnalysis/PlayAnalysis/DoubleAnalysis fixtures)
    // ------------------------------------------------------------------ //

    private static MatchHeaderRecord BuildMatchHeader(
        BgDecisionData decision, bool isMoney, int matchLength,
        bool jacoby, bool beaver, string player1, string player2)
    {
        string eventName = decision.Descriptive.Event ?? "";
        DateTime date = decision.Descriptive.Date is { } d
            ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : default;

        return new MatchHeaderRecord
        {
            EntryType = RecordType.HeaderMatch,
            Player1Ansi = player1,
            Player2Ansi = player2,
            Player1 = player1,
            Player2 = player2,
            MatchLength = isMoney ? 99999 : matchLength,
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
            GameId = DeterministicGameId(decision),
            CompLevel1 = -1,
            CompLevel2 = -1,
            LocationAnsi = "eXtreme Gammon",
            Location = "eXtreme Gammon",
            GameMode = GameMode.Competition,
            Invert = 1,
            Version = 30,
            Magic = 1229737284,        // constant XG stamps into every save
            CommentHeaderMatchIndex = -1,
            CommentFooterMatchIndex = -1,
            IsMoneyMatch = false,      // XG leaves this false; 99999 is the money signal
            SiteId = (SiteId)(-1),
            CubeLimit = 10,            // XG default: max cube 2^10
        };
    }

    private static GameHeaderRecord BuildGameHeader(
        sbyte[] position, int score1, int score2, bool crawfordApplies) => new()
    {
        EntryType = RecordType.HeaderGame,
        Score1 = score1,
        Score2 = score2,
        CrawfordApplies = crawfordApplies,
        // XG's position-editor pattern: the game "starts" at the saved position.
        InitialPosition = new PositionEngine { Points = position },
        GameNumber = 1,
        InProgress = true,
        CommentHeaderGameIndex = -1,
        CommentFooterGameIndex = -1,
    };

    private static CubeRecord BuildCubeRecord(
        BgDecisionData decision, sbyte[] position, int cubeRaw)
    {
        bool isCube = decision.Decision.IsCube;
        return new CubeRecord
        {
            EntryType = RecordType.Cube,
            ActivePlayer = 1,
            // Mirrors XG's own pane-state values: -1 for a saved cube
            // problem (DoubleAnalysis fixture), -2 when the position is a
            // play decision and the cube pane is incidental (PlayAnalysis).
            Doubled = isCube ? -1 : -2,
            Taken = -1,
            BeaverAccepted = -1,
            RaccoonAccepted = -1,
            CubeValue = cubeRaw,
            Position = new PositionEngine { Points = position },
            Analysis = UnanalysedDoubleAction(),
            ErrorCube = UnanalysedError,
            // XG writes "11" as the cube-pane placeholder of a pre-roll
            // position; a play decision carries its real roll.
            DiceRolled = isCube ? "11" : $"{decision.Decision.Dice[0]}{decision.Decision.Dice[1]}",
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
    }

    private static MoveRecord BuildMoveRecord(
        BgDecisionData decision, sbyte[] position, int cubeRaw) => new()
    {
        EntryType = RecordType.Move,
        InitialPosition = new PositionEngine { Points = position },
        FinalPosition = new PositionEngine(),   // no play made — XG leaves this zeroed
        ActivePlayer = 1,
        Dice = [decision.Decision.Dice[0], decision.Decision.Dice[1]],
        CubeValue = cubeRaw,
        Analysis = new BestMoveAnalysis { Level = UnanalysedLevel },
        MoveError = UnanalysedError,
        RolloutIndices = [.. Enumerable.Repeat(-1, 32)],
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
    private static DoubleActionAnalysis UnanalysedDoubleAction() => new()
    {
        Level = UnanalysedLevel,
        IsBeaver = UnanalysedLevel,
    };

    // ------------------------------------------------------------------ //
    //  Derivation helpers
    // ------------------------------------------------------------------ //

    private static string NameOrDefault(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

    private static sbyte[] ToPositionEngine(IReadOnlyList<int> mop)
    {
        // On-roll player is written as player 1, so the on-roll-relative
        // board maps onto the record position verbatim.
        var points = new sbyte[26];
        for (int i = 0; i < 26; i++)
        {
            if (mop[i] is < -15 or > 15)
                throw new ArgumentException($"Position.Mop[{i}] = {mop[i]} is not a valid checker count.");
            points[i] = (sbyte)mop[i];
        }
        return points;
    }

    /// <summary>
    /// Encodes cube size + owner into XG's signed-log2 record field:
    /// 0 = centred 1-cube; +n = player 1 (the on-roll player here) owns
    /// 2^n; −n = player 2 owns 2^n.
    /// </summary>
    private static int EncodeCube(int cubeSize, CubeOwner owner)
    {
        int log2 = BitOperations.Log2((uint)cubeSize);
        return owner switch
        {
            CubeOwner.OnRoll => log2,
            CubeOwner.Opponent => -log2,
            _ => 0,
        };
    }

    /// <summary>
    /// Recovers the money-game Jacoby/Beaver flags from XGID field 8
    /// (<c>Jacoby + 2×Beaver</c>) — the only place the pipeline carries
    /// them. Falls back to XG's defaults (Jacoby on, Beaver off) when the
    /// decision carries no parseable XGID.
    /// </summary>
    private static (bool Jacoby, bool Beaver) MoneyFlags(string xgid)
    {
        const string prefix = "XGID=";
        string body = xgid.StartsWith(prefix, StringComparison.Ordinal) ? xgid[prefix.Length..] : xgid;
        string[] fields = body.Split(':');
        if (fields.Length >= 8 && int.TryParse(fields[7], out int cj))
            return ((cj & 1) != 0, (cj & 2) != 0);
        return (true, false);
    }

    /// <summary>
    /// Deterministic stand-in for the random per-save id XG writes:
    /// a CRC32 over the position, dice, and cube encoding. Determinism
    /// keeps exports byte-stable for testing; XG treats the value as opaque.
    /// </summary>
    private static int DeterministicGameId(BgDecisionData decision)
    {
        var bytes = new byte[29];
        for (int i = 0; i < 26; i++)
            bytes[i] = unchecked((byte)decision.Position.Mop[i]);
        bytes[26] = decision.Decision.IsCube ? (byte)0 : (byte)decision.Decision.Dice[0];
        bytes[27] = decision.Decision.IsCube ? (byte)0 : (byte)decision.Decision.Dice[1];
        bytes[28] = unchecked((byte)EncodeCube(decision.Position.CubeSize, decision.Position.CubeOwner));
        return unchecked((int)System.IO.Hashing.Crc32.HashToUInt32(bytes));
    }

    private static string BuildSaveName(
        bool isMoney, int matchLength, int score1, int score2, bool jacoby) =>
        isMoney
            ? jacoby ? "Position:  Unlimited Game, Jacoby" : "Position:  Unlimited Game"
            : $"Position: {matchLength} point match {score1}-{score2}";
}
