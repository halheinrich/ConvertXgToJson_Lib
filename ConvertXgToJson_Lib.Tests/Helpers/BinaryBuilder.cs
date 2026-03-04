using System.IO.Compression;
using System.Text;

namespace ConvertXgToJson_Lib.Tests.Helpers;

/// <summary>
/// Fluent builder for raw little-endian Pascal binary buffers.
/// Tracks the current write offset so AlignTo() can insert correct padding.
/// </summary>
internal sealed class BinaryBuilder
{
    private readonly List<byte> _bytes = [];

    /// <summary>Current number of bytes written so far.</summary>
    public int CurrentOffset => _bytes.Count;

    // ------------------------------------------------------------------ //
    //  Alignment
    // ------------------------------------------------------------------ //

    public BinaryBuilder AlignTo(int boundary)
    {
        while (_bytes.Count % boundary != 0)
            _bytes.Add(0x00);
        return this;
    }

    // ------------------------------------------------------------------ //
    //  Unaligned primitives
    // ------------------------------------------------------------------ //

    public BinaryBuilder Bool(bool v) { _bytes.Add(v ? (byte)1 : (byte)0); return this; }
    public BinaryBuilder Byte(byte v) { _bytes.Add(v); return this; }
    /// <summary>Int64 with NO alignment – for use in packed Pascal records.</summary>
    public BinaryBuilder Int64Packed(long v)
    {
        _bytes.AddRange(BitConverter.GetBytes(v));
        return this;
    }
    public BinaryBuilder SByte(sbyte v) { _bytes.Add(unchecked((byte)v)); return this; }
    public BinaryBuilder Pad(int count) { for (int i = 0; i < count; i++) _bytes.Add(0); return this; }

    // ------------------------------------------------------------------ //
    //  2-byte aligned
    // ------------------------------------------------------------------ //

    public BinaryBuilder Int16(short v) { AlignTo(2); _bytes.AddRange(BitConverter.GetBytes(v)); return this; }
    public BinaryBuilder UInt16(ushort v) { AlignTo(2); _bytes.AddRange(BitConverter.GetBytes(v)); return this; }

    // ------------------------------------------------------------------ //
    //  4-byte aligned
    // ------------------------------------------------------------------ //

    public BinaryBuilder Int32(int v) { AlignTo(4); _bytes.AddRange(BitConverter.GetBytes(v)); return this; }
    public BinaryBuilder UInt32(uint v) { AlignTo(4); _bytes.AddRange(BitConverter.GetBytes(v)); return this; }
    public BinaryBuilder Float(float v) { AlignTo(4); _bytes.AddRange(BitConverter.GetBytes(v)); return this; }

    // ------------------------------------------------------------------ //
    //  8-byte aligned
    // ------------------------------------------------------------------ //

    public BinaryBuilder Double(double v) { AlignTo(8); _bytes.AddRange(BitConverter.GetBytes(v)); return this; }
    public BinaryBuilder Int64(long v) { AlignTo(8); _bytes.AddRange(BitConverter.GetBytes(v)); return this; }

    // ------------------------------------------------------------------ //
    //  Pascal string[N]  – 1-byte length, N body bytes (ANSI), no align
    // ------------------------------------------------------------------ //

    public BinaryBuilder PascalAnsiString(string s, int maxLen)
    {
        byte[] ansi = Encoding.Latin1.GetBytes(s);
        int len = Math.Min(ansi.Length, maxLen);
        _bytes.Add((byte)len);
        byte[] body = new byte[maxLen];
        Array.Copy(ansi, body, len);
        _bytes.AddRange(body);
        return this;
    }

    // ------------------------------------------------------------------ //
    //  array[0..N] of WideChar  – (N+1)*2 bytes, 2-byte align, #0-terminated
    // ------------------------------------------------------------------ //

    public BinaryBuilder WideCharArray(string s, int elementCount)
    {
        AlignTo(2);
        byte[] utf16 = Encoding.Unicode.GetBytes(s);
        byte[] buf = new byte[elementCount * 2]; // zero-filled → null-terminated
        int copyLen = Math.Min(utf16.Length, (elementCount - 1) * 2); // leave room for #0
        Array.Copy(utf16, buf, copyLen);
        _bytes.AddRange(buf);
        return this;
    }

    // ------------------------------------------------------------------ //
    //  TShortUnicodeString  – array[0..128] of Char = 258 bytes, 2-byte align
    // ------------------------------------------------------------------ //

    public BinaryBuilder ShortUnicodeString(string s) => WideCharArray(s, 129);

    // ------------------------------------------------------------------ //
    //  TDateTime  – double, 8-byte align. December 30 1899 = day 0.
    // ------------------------------------------------------------------ //

    private static readonly DateTime PascalEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);

    public BinaryBuilder TDateTime(DateTime dt)
    {
        double d = (dt - PascalEpoch).TotalDays;
        return Double(d);
    }

    // ------------------------------------------------------------------ //
    //  TGUID  – 16 bytes packed
    // ------------------------------------------------------------------ //

    public BinaryBuilder Guid(Guid g)
    {
        _bytes.AddRange(g.ToByteArray());
        return this;
    }

    // ------------------------------------------------------------------ //
    //  PositionEngine  – array[0..25] of ShortInt = 26 bytes, no align
    // ------------------------------------------------------------------ //

    public BinaryBuilder WritePosition(sbyte[] points)
    {
        if (points.Length != 26) throw new ArgumentException("PositionEngine must have 26 elements.");
        foreach (sbyte b in points) _bytes.Add(unchecked((byte)b));
        return this;
    }

    public BinaryBuilder ZeroPosition() => WritePosition(new sbyte[26]);

    // ------------------------------------------------------------------ //
    //  Pad to exact size
    // ------------------------------------------------------------------ //

    public BinaryBuilder PadTo(int totalSize)
    {
        int needed = totalSize - CurrentOffset;
        if (needed < 0) throw new InvalidOperationException(
            $"Already written {CurrentOffset} bytes, cannot pad to {totalSize}.");
        return Pad(needed);
    }

    // ------------------------------------------------------------------ //
    //  Output
    // ------------------------------------------------------------------ //

    public byte[] ToArray() => [.. _bytes];

    public MemoryStream ToStream()
    {
        var ms = new MemoryStream(_bytes.Count);
        ms.Write([.. _bytes]);
        ms.Position = 0;
        return ms;
    }
}

/// <summary>
/// Builds complete .XG file byte payloads for integration-level tests,
/// assembling: RichGameHeader + thumbnail + compressed(xg+xgi+xgr+xgc).
/// </summary>
internal static class XgFileBuilder
{
    private const uint RmMagicNumber = 0x484D4752u;   // "RGMH"
    private const uint HeaderSize = 8232u;

    /// <summary>
    /// Builds a minimal but valid .XG file stream containing a single
    /// MatchHeader record and a MatchFooter record.
    /// </summary>
    public static MemoryStream BuildMinimalXgFile(
        string player1 = "Alice",
        string player2 = "Bob",
        int matchLength = 7,
        DateTime? date = null)
    {
        var matchDate = date ?? new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc);

        byte[] xgBytes = BuildXgStream(player1, player2, matchLength, matchDate);
        byte[] xgiBytes = BuildXgiStream(xgBytes);
        byte[] xgrBytes = [];
        byte[] xgcBytes = Encoding.Latin1.GetBytes("Match comment\r\n");

        byte[] compressed = CompressAll(xgBytes, xgrBytes, xgiBytes, xgcBytes);

        var guid = System.Guid.NewGuid();
        var header = new BinaryBuilder()
            .UInt32(RmMagicNumber)
            .UInt32(1)
            .UInt32(HeaderSize)
            .Int64Packed(0)   // ThumbnailOffset (packed, no align)
            .UInt32(0)
            .Guid(guid)
            .WideCharArray("Test Game", 1024)
            .WideCharArray("Test Save", 1024)
            .WideCharArray("", 1024)
            .WideCharArray("", 1024)
            .ToArray();

        if (header.Length < (int)HeaderSize)
        {
            byte[] padded = new byte[HeaderSize];
            Array.Copy(header, padded, header.Length);
            header = padded;
        }

        var file = new MemoryStream();
        file.Write(header);
        file.Write(compressed);
        file.Position = 0;
        return file;
    }

    // ------------------------------------------------------------------

    private static byte[] BuildXgStream(string p1, string p2, int matchLen, DateTime date)
    {
        byte[] rec0 = BuildMatchHeaderRecord(p1, p2, matchLen, date);
        byte[] rec1 = BuildMatchFooterRecord(matchLen);
        return [.. rec0, .. rec1];
    }

    private static byte[] BuildXgiStream(byte[] xgBytes)
    {
        const int recSize = 2560;
        byte[] first = xgBytes[..recSize];
        byte[] last = xgBytes[(xgBytes.Length - recSize)..];
        return [.. first, .. last];
    }

    private static byte[] CompressAll(byte[] xg, byte[] xgr, byte[] xgi, byte[] xgc)
    {
        // Each non-empty section gets its own zlib stream, in order: xg, xgr, xgi, xgc.
        // This matches the ZlibArchive multi-stream format used by XG.
        var ms = new MemoryStream();
        foreach (var section in new[] { xg, xgr, xgi, xgc })
        {
            if (section.Length == 0) continue;
            using var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true);
            z.Write(section);
        }
        return ms.ToArray();
    }    // ------------------------------------------------------------------
    //  Record builders
    // ------------------------------------------------------------------

    internal static byte[] BuildMatchHeaderRecord(
        string p1, string p2, int matchLen, DateTime date)
    {
        const int MagicNumber = 0x494C4D44;

        var b = new BinaryBuilder()
            .UInt32(0)          // Previous (ignored)
            .UInt32(0)          // Next     (ignored)
            .Byte(0);           // EntryType = tsHeaderMatch = 0
        // Now at offset 9

        b.PascalAnsiString(p1, 40)      // offset 9  → 50
         .PascalAnsiString(p2, 40);     // offset 50 → 91
        // MatchLength: AlignTo(4) → offset 92
        b.Int32(matchLen)               // offset 96
         .Int32(0)                      // Variation
         .Bool(true)                    // Crawford
         .Bool(false)                   // Jacoby
         .Bool(false)                   // Beaver
         .Bool(false);                  // AutoDouble
        // Elo1: AlignTo(8) → offset 104
        b.Double(1500.0)                // Elo1
         .Double(1450.0)                // Elo2
         .Int32(100)                    // exp1
         .Int32(200)                    // exp2
         .TDateTime(date)               // Date
         .PascalAnsiString("World Championship", 128)
         .Int32(42)                     // GameId  AlignTo4 → 268
         .Int32(3)                      // CompLevel1
         .Int32(3)                      // CompLevel2
         .Bool(true)                    // CountForElo
         .Bool(true)                    // AddToProfile1
         .Bool(true)                    // AddToProfile2
         .PascalAnsiString("Monaco", 128)
         .Int32((int)ConvertXgToJson_Lib.Models.GameMode.Competition)
         .Bool(false)                   // Imported
         .PascalAnsiString("Final", 128)
         .Int32(0)                      // Invert
         .Int32(30)                     // version
         .Int32(MagicNumber)
         .Int32(0)                      // MoneyInitG
         .Int32(0)                      // MoneyInitScore[1]
         .Int32(0)                      // MoneyInitScore[2]
         .Bool(false)                   // Entered
         .Bool(false)                   // Counted
         .Bool(false)                   // UnratedImp
         .Int32(-1)                     // CommentHeaderMatch
         .Int32(-1)                     // CommentFooterMatch
         .Bool(false)                   // IsMoneyMatch
         .Float(0f)                     // WinMoney
         .Float(0f)                     // LoseMoney
         .Int32(0)                      // Currency
         .Float(0f)                     // FeeMoney
         .Float(0f)                     // TableStake
         .Int32(0)                      // SiteId
         .Int32(0)                      // CubeLimit
         .Int32(0)                      // AutoDoubleMax
         .Bool(false);                  // Transcribed

        b.ShortUnicodeString("World Championship")
         .ShortUnicodeString(p1)
         .ShortUnicodeString(p2)
         .ShortUnicodeString("Monaco")
         .ShortUnicodeString("Final");

        b.Int32(0)    // ClockType = None
         .Bool(false) // PerGame
         .Int32(0)    // Time1
         .Int32(0)    // Time2
         .Int32(0)    // Penalty
         .Int32(0)    // TimeLeft1
         .Int32(0)    // TimeLeft2
         .Int32(0);   // PenaltyMoney

        b.Int32(0).Int32(0).Int32(0).Int32(0); // v26 delay counters

        b.ShortUnicodeString("Transcriber Name");

        b.PadTo(2560);
        return b.ToArray();
    }

    internal static byte[] BuildMatchFooterRecord(int matchLen)
    {
        var b = new BinaryBuilder()
            .UInt32(0).UInt32(0)
            .Byte(5);           // tsFooterMatch = 5
        b.Int32(matchLen)
         .Int32(0)
         .Int32(1)              // WinnerM (+1 = player1)
         .Double(1516.0)        // Elo1m
         .Double(1434.0)        // Elo2m
         .Int32(101)            // exp1m
         .Int32(199)            // exp2m
         .TDateTime(new DateTime(2024, 1, 15, 16, 0, 0, DateTimeKind.Utc));
        b.PadTo(2560);
        return b.ToArray();
    }

    internal static byte[] BuildGameHeaderRecord(int score1 = 0, int score2 = 0, int gameNum = 1)
    {
        var b = new BinaryBuilder()
            .UInt32(0).UInt32(0)
            .Byte(1);           // tsHeaderGame
        b.Int32(score1)
         .Int32(score2)
         .Bool(false)           // CrawfordApplies
         .ZeroPosition()        // PosInit (26 bytes)
         .Int32(gameNum)        // GameNumber
         .Bool(true)            // InProgress
         .Int32(-1)             // CommentHeaderGame
         .Int32(-1)             // CommentFooterGame
         .Int32(0);             // NumberOfAutoDoubles
        b.PadTo(2560);
        return b.ToArray();
    }

    internal static byte[] BuildGameFooterRecord()
    {
        var b = new BinaryBuilder()
            .UInt32(0).UInt32(0)
            .Byte(4);           // tsFooterGame
        b.Int32(7)              // Score1g
         .Int32(3)              // Score2g
         .Bool(false)           // CrawfordApplyg
         .Int32(1)              // Winner (+1 = player1)
         .Int32(3)              // Pointswon
         .Int32(1)              // Termination (single)
         .Double(-1000.0)       // ErrResign
         .Double(-1000.0);      // ErrTakeResign
        for (int i = 0; i < 7; i++) b.Double(0.0);  // Eval[0..6]
        b.Int32(3);             // EvalLevel
        b.PadTo(2560);
        return b.ToArray();
    }

    internal static byte[] BuildCubeRecord()
    {
        var b = new BinaryBuilder()
            .UInt32(0).UInt32(0)
            .Byte(2);           // tsCube
        b.Int32(1)              // Actif (player1)
         .Int32(1)              // double = yes
         .Int32(1)              // Take = yes
         .Int32(0)              // BeaverR
         .Int32(0)              // RaccoonR
         .Int32(1)              // CubeB
         .ZeroPosition();       // Position

        // DoubleD: EngineStructDoubleAction
        b.ZeroPosition()        // Pos
         .Int32(3)              // Level
         .Int32(3).Int32(2)     // Score
         .Int32(2)              // Cube
         .Int32(0)              // CubePos
         .Int32(0)              // Jacoby
         .Int16(0)              // Crawford
         .Int16(0)              // met
         .Int16(1)              // FlagDouble
         .Int16(0);             // isBeaver
        for (int i = 0; i < 7; i++) b.Float(0f);  // Eval
        b.Float(0.12f)          // equB
         .Float(0.08f)          // equDouble
         .Float(-1.0f)          // equDrop
         .Int16(3)              // LevelRequest
         .Int16(1);             // DoubleChoice3
        for (int i = 0; i < 7; i++) b.Float(0f);  // EvalDouble

        b.Double(-1000.0)       // ErrCube
         .PascalAnsiString("31", 2)
         .Double(-1000.0)       // ErrTake
         .Int32(-1)             // RolloutindexD
         .Int32(0)              // CompChoiceD
         .Int32(3)              // AnalyzeC
         .Double(-1000.0)       // ErrBeaver
         .Double(-1000.0)       // ErrRaccoon
         .Int32(3)              // AnalyzeCR
         .Int32(0)              // inValid
         .SByte(0)              // TutorCube
         .SByte(0)              // TutorTake
         .Double(-1000.0)       // ErrTutorCube
         .Double(-1000.0)       // ErrTutorTake
         .Bool(false)           // FlaggedDouble
         .Int32(-1)             // CommentCube
         .Bool(false)           // EditedCube
         .Bool(false)           // TimeDelayCube
         .Bool(false)           // TimeDelayCubeDone
         .Int32(0)              // NumberOfAutoDoubleCube
         .Int32(300)            // TimeBot
         .Int32(300);           // TimeTop
        b.PadTo(2560);
        return b.ToArray();
    }

    internal static byte[] BuildMoveRecord()
    {
        var b = new BinaryBuilder()
            .UInt32(0).UInt32(0)
            .Byte(3);           // tsMove
        b.ZeroPosition()        // PositionI
         .ZeroPosition()        // PositionEnd
         .Int32(1);             // ActifP

        b.Int32(24).Int32(6).Int32(23).Int32(5)
         .Int32(-1).Int32(0).Int32(0).Int32(0);  // Moves[1..8]

        b.Int32(6).Int32(5);    // Dice

        b.Int32(1)              // CubeA
         .Double(0.0)           // ErrorM
         .Int32(5);             // NMoveEval

        b.Pad(2184);            // DataMoves (zeroed)

        b.Bool(true)            // Played
         .Double(-0.042)        // ErrMove
         .Double(0.015)         // ErrLuck
         .Int32(0)              // CompChoice
         .Double(0.312);        // InitEq

        for (int i = 0; i < 32; i++) b.Int32(-1);  // RolloutindexM

        b.Int32(3)              // AnalyzeM
         .Int32(3)              // AnalyzeL
         .Int32(0)              // InvalidM
         .ZeroPosition()        // PositionTutor
         .SByte(0)              // Tutor
         .Double(-1000.0)       // ErrTutorMove
         .Bool(false)           // Flagged
         .Int32(-1)             // CommentMove
         .Bool(false)           // Editedmove
         .UInt32(0)             // TimeDelayMove
         .UInt32(0)             // TimeDelayMoveDone
         .Int32(0)              // NumberOfAutoDoubleMove
         .Int32(0).Int32(0).Int32(0).Int32(0);  // Filler[1..4]

        b.PadTo(2560);
        return b.ToArray();
    }
}
