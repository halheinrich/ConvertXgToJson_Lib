using AwesomeAssertions;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for the outer RichGameFormat header (TRichGameHeader).
/// </summary>
public class RichGameHeaderTests
{
    [Fact]
    public void ReadFile_ParsesGameName()
    {
        var stream = XgBytesBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.GameName.Should().Be("Test Game");
    }

    [Fact]
    public void ReadFile_ParsesSaveName()
    {
        var stream = XgBytesBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.SaveName.Should().Be("Test Save");
    }

    [Fact]
    public void ReadFile_MagicNumberIsCorrect()
    {
        var stream = XgBytesBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.MagicNumber.Should().Be(0x484D4752u);
    }

    [Fact]
    public void ReadStream_ThrowsOnInvalidMagicNumber()
    {
        // Corrupt the first 4 bytes
        var stream = XgBytesBuilder.BuildMinimalXgFile();
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        bytes[0] = 0xDE; bytes[1] = 0xAD; bytes[2] = 0xBE; bytes[3] = 0xEF;

        var act = () => XgFileReader.ReadStream(new MemoryStream(bytes));
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*magic*");
    }

    // ------------------------------------------------------------------ //
    //  A magic number that is not XG's: refused as a value by the Try forms
    //  (halheinrich/backgammon#523), thrown by the others
    // ------------------------------------------------------------------ //

    private const string BadMagicMessage = "Not a valid XG file: expected magic 0x484D4752 but got 0xEFBEADDE.";

    private static byte[] ValidBytes() => XgBytesBuilder.BuildMinimalXgFile().ToArray();

    private static byte[] BadMagicBytes()
    {
        byte[] bytes = ValidBytes();
        bytes[0] = 0xDE; bytes[1] = 0xAD; bytes[2] = 0xBE; bytes[3] = 0xEF;
        return bytes;
    }

    [Fact]
    public void TryRead_ReadsAValidHeader_AsReadDoes()
    {
        byte[] bytes = ValidBytes();
        var (expected, expectedOffset) = RichGameHeaderParser.Read(new MemoryStream(bytes));

        RichGameHeaderParser.TryRead(new MemoryStream(bytes), out var header, out long contentOffset, out var refusal)
            .Should().BeTrue();

        header.Should().BeEquivalentTo(expected);
        contentOffset.Should().Be(expectedOffset);
        refusal.Should().BeNull();
    }

    [Fact]
    public void TryRead_ReturnsTheRefusal_Unthrown_ForABadMagicNumber()
    {
        RichGameHeaderParser.TryRead(new MemoryStream(BadMagicBytes()), out var header, out long contentOffset, out var refusal)
            .Should().BeFalse();

        header.Should().BeNull();
        contentOffset.Should().Be(0);
        refusal!.Message.Should().Be(BadMagicMessage);
        refusal.StackTrace.Should().BeNull("the refusal is returned, never thrown");
    }

    /// <summary>Only the bad magic number is a value: a stream too short to hold one throws.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TryRead_Throws_ForFewerThanFourBytes(int length)
    {
        FluentActions.Invoking(() => RichGameHeaderParser.TryRead(new MemoryStream(ValidBytes()[..length]), out _, out _, out _))
            .Should().Throw<EndOfStreamException>();
    }

    /// <summary>A stream that ends after XG's magic number, inside the rest of the header, throws as before.</summary>
    [Fact]
    public void TryRead_Throws_ForAHeaderCutShortAfterTheMagicNumber()
    {
        FluentActions.Invoking(() => RichGameHeaderParser.TryRead(new MemoryStream(ValidBytes()[..10]), out _, out _, out _))
            .Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Read_ThrowsTheRefusal_ForABadMagicNumber()
    {
        FluentActions.Invoking(() => RichGameHeaderParser.Read(new MemoryStream(BadMagicBytes())))
            .Should().Throw<InvalidDataException>().WithMessage(BadMagicMessage);
    }

    [Fact]
    public void TryReadStream_DecodesAValidFile_AsReadStreamDoes()
    {
        byte[] bytes = ValidBytes();   // each build has its own game id: decode one set of bytes twice

        XgFileReader.TryReadStream(new MemoryStream(bytes), out var file, out var refusal).Should().BeTrue();

        refusal.Should().BeNull();
        XgFileReader.ToJson(file!).Should().Be(XgFileReader.ToJson(XgFileReader.ReadStream(new MemoryStream(bytes))));
    }

    [Fact]
    public void TryReadStream_ReturnsTheRefusal_Unthrown_ForABadMagicNumber()
    {
        XgFileReader.TryReadStream(new MemoryStream(BadMagicBytes()), out var file, out var refusal).Should().BeFalse();

        file.Should().BeNull();
        refusal!.Message.Should().Be(BadMagicMessage);
        refusal.StackTrace.Should().BeNull("the refusal is returned, never thrown");
    }

    [Fact]
    public void ReadStream_ThrowsTheRefusal_WithItsExactMessage()
    {
        FluentActions.Invoking(() => XgFileReader.ReadStream(new MemoryStream(BadMagicBytes())))
            .Should().Throw<InvalidDataException>().WithMessage(BadMagicMessage);
    }

    [Fact]
    public void ReadFile_HeaderVersionIsNonZero()
    {
        var stream = XgBytesBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.HeaderVersion.Should().Be(1);
    }

    [Fact]
    public void ReadFile_GameIdIsValidGuid()
    {
        var stream = XgBytesBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.GameId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ReadFile_EmptyLevelNameAndComments()
    {
        var stream = XgBytesBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.LevelName.Should().BeEmpty();
        file.Header.Comments.Should().BeEmpty();
    }
}
