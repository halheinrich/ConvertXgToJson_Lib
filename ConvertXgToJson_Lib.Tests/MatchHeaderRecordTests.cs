using FluentAssertions;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for tsHeaderMatch (MatchHeaderRecord) parsing.
/// Verifies every significant field including alignment-sensitive ones.
/// </summary>
public class MatchHeaderRecordTests
{
    private static XgFile BuildFile(
        string p1 = "Alice", string p2 = "Bob",
        int matchLen = 7, DateTime? date = null)
    {
        var stream = XgFileBuilder.BuildMinimalXgFile(p1, p2, matchLen, date);
        return XgFileReader.ReadStream(stream);
    }

    private static MatchHeaderRecord GetHeader(XgFile file)
        => file.Records.OfType<MatchHeaderRecord>().First();

    // ------------------------------------------------------------------ //
    //  Basic fields
    // ------------------------------------------------------------------ //

    [Fact]
    public void MatchHeader_RecordTypeIsHeaderMatch()
    {
        var h = GetHeader(BuildFile());
        h.EntryType.Should().Be(RecordType.HeaderMatch);
    }

    [Fact]
    public void MatchHeader_Player1AnsiNameParsed()
    {
        var h = GetHeader(BuildFile(p1: "Alice"));
        h.Player1Ansi.Should().Be("Alice");
    }

    [Fact]
    public void MatchHeader_Player2AnsiNameParsed()
    {
        var h = GetHeader(BuildFile(p2: "Bob"));
        h.Player2Ansi.Should().Be("Bob");
    }

    [Fact]
    public void MatchHeader_MatchLengthParsed()
    {
        var h = GetHeader(BuildFile(matchLen: 11));
        h.MatchLength.Should().Be(11);
    }

    [Fact]
    public void MatchHeader_CrawfordIsTrue()
    {
        var h = GetHeader(BuildFile());
        h.Crawford.Should().BeTrue();
    }

    [Fact]
    public void MatchHeader_JacobyIsFalse()
    {
        var h = GetHeader(BuildFile());
        h.Jacoby.Should().BeFalse();
    }

    // ------------------------------------------------------------------ //
    //  Alignment-sensitive fields
    // ------------------------------------------------------------------ //

    [Fact]
    public void MatchHeader_Elo1ParsedCorrectly()
    {
        // Elo1 sits at offset 104 in the record variant (8-byte aligned Double)
        var h = GetHeader(BuildFile());
        h.Elo1.Should().BeApproximately(1500.0, 1e-9);
    }

    [Fact]
    public void MatchHeader_Elo2ParsedCorrectly()
    {
        var h = GetHeader(BuildFile());
        h.Elo2.Should().BeApproximately(1450.0, 1e-9);
    }

    [Fact]
    public void MatchHeader_DateParsedCorrectly()
    {
        var date = new DateTime(2024, 6, 15, 9, 0, 0, DateTimeKind.Utc);
        var h = GetHeader(BuildFile(date: date));
        h.Date.Year.Should().Be(2024);
        h.Date.Month.Should().Be(6);
        h.Date.Day.Should().Be(15);
        h.Date.Hour.Should().Be(9);
    }

    [Fact]
    public void MatchHeader_EventAnsiParsed()
    {
        var h = GetHeader(BuildFile());
        h.EventAnsi.Should().Be("World Championship");
    }

    [Fact]
    public void MatchHeader_GameIdParsed()
    {
        // GameId sits at offset 268 (after 3-byte pad following SEvent)
        var h = GetHeader(BuildFile());
        h.GameId.Should().Be(42);
    }

    // ------------------------------------------------------------------ //
    //  Unicode fields (v24+)
    // ------------------------------------------------------------------ //

    [Fact]
    public void MatchHeader_Player1UnicodeParsed()
    {
        var h = GetHeader(BuildFile(p1: "Alice"));
        h.Player1.Should().Be("Alice");
    }

    [Fact]
    public void MatchHeader_Player2UnicodeParsed()
    {
        var h = GetHeader(BuildFile(p2: "Bob"));
        h.Player2.Should().Be("Bob");
    }

    [Fact]
    public void MatchHeader_EventUnicodeParsed()
    {
        var h = GetHeader(BuildFile());
        h.Event.Should().Be("World Championship");
    }

    [Fact]
    public void MatchHeader_LocationUnicodeParsed()
    {
        var h = GetHeader(BuildFile());
        h.Location.Should().Be("Monaco");
    }

    [Fact]
    public void MatchHeader_RoundUnicodeParsed()
    {
        var h = GetHeader(BuildFile());
        h.Round.Should().Be("Final");
    }

    [Fact]
    public void MatchHeader_TranscriberParsed()
    {
        var h = GetHeader(BuildFile());
        h.Transcriber.Should().Be("Transcriber Name");
    }

    // ------------------------------------------------------------------ //
    //  TimeSetting (v25)
    // ------------------------------------------------------------------ //

    [Fact]
    public void MatchHeader_TimeSettingClockTypeIsNone()
    {
        var h = GetHeader(BuildFile());
        h.TimeSetting.ClockType.Should().Be(ClockType.None);
    }

    [Fact]
    public void MatchHeader_TimeSettingPerGameIsFalse()
    {
        var h = GetHeader(BuildFile());
        h.TimeSetting.PerGame.Should().BeFalse();
    }

    // ------------------------------------------------------------------ //
    //  Version and magic
    // ------------------------------------------------------------------ //

    [Fact]
    public void MatchHeader_VersionIs30()
    {
        var h = GetHeader(BuildFile());
        h.Version.Should().Be(30);
    }

    [Fact]
    public void MatchHeader_MagicNumberIsCorrect()
    {
        var h = GetHeader(BuildFile());
        h.Magic.Should().Be(unchecked((int)0x494C4D44));
    }

    // ------------------------------------------------------------------ //
    //  Long player names (up to 40 ANSI chars)
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData("A")]
    [InlineData("Maximilian von Hapsburg III")]
    [InlineData("12345678901234567890123456789012345678901")] // 41 chars → truncated at 40
    public void MatchHeader_PlayerNameHandlesVariousLengths(string name)
    {
        var h = GetHeader(BuildFile(p1: name));
        h.Player1Ansi.Should().Be(name.Length > 40 ? name[..40] : name);
    }

    // ------------------------------------------------------------------ //
    //  Comment indices
    // ------------------------------------------------------------------ //

    [Fact]
    public void MatchHeader_CommentIndicesAreMinusOne()
    {
        var h = GetHeader(BuildFile());
        h.CommentHeaderMatchIndex.Should().Be(-1);
        h.CommentFooterMatchIndex.Should().Be(-1);
    }

    // ------------------------------------------------------------------ //
    //  Record count
    // ------------------------------------------------------------------ //

    [Fact]
    public void ParsedFile_ContainsExactlyTwoRecords()
    {
        // Minimal file: MatchHeader + MatchFooter
        var file = BuildFile();
        file.Records.Should().HaveCount(2);
    }

    [Fact]
    public void ParsedFile_FirstRecordIsMatchHeader()
    {
        var file = BuildFile();
        file.Records[0].Should().BeOfType<MatchHeaderRecord>();
    }

    [Fact]
    public void ParsedFile_LastRecordIsMatchFooter()
    {
        var file = BuildFile();
        file.Records[^1].Should().BeOfType<MatchFooterRecord>();
    }
}
