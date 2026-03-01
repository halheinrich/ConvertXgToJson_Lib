using FluentAssertions;
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
        var stream = XgFileBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.GameName.Should().Be("Test Game");
    }

    [Fact]
    public void ReadFile_ParsesSaveName()
    {
        var stream = XgFileBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.SaveName.Should().Be("Test Save");
    }

    [Fact]
    public void ReadFile_MagicNumberIsCorrect()
    {
        var stream = XgFileBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.MagicNumber.Should().Be(0x484D4752u);
    }

    [Fact]
    public void ReadStream_ThrowsOnInvalidMagicNumber()
    {
        // Corrupt the first 4 bytes
        var stream = XgFileBuilder.BuildMinimalXgFile();
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        bytes[0] = 0xDE; bytes[1] = 0xAD; bytes[2] = 0xBE; bytes[3] = 0xEF;

        var act = () => XgFileReader.ReadStream(new MemoryStream(bytes));
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*magic*");
    }

    [Fact]
    public void ReadFile_HeaderVersionIsNonZero()
    {
        var stream = XgFileBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.HeaderVersion.Should().Be(1);
    }

    [Fact]
    public void ReadFile_GameIdIsValidGuid()
    {
        var stream = XgFileBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.GameId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ReadFile_EmptyLevelNameAndComments()
    {
        var stream = XgFileBuilder.BuildMinimalXgFile();
        var file = XgFileReader.ReadStream(stream);
        file.Header.LevelName.Should().BeEmpty();
        file.Header.Comments.Should().BeEmpty();
    }
}
