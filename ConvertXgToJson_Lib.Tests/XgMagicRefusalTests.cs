using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// halheinrich/backgammon#523 (experimental, not for main): the refusal of a
/// file whose magic number is not XG's, at the check
/// (<see cref="RichGameHeaderParser.RefusalOf"/>) and through both readers
/// that reach it — <see cref="XgFileReader.ReadStream"/> still throws it,
/// <see cref="XgSourceReader.ReadStream"/> returns it as a failed source with
/// the same message and leaves the borrowed stream open.
/// </summary>
public sealed class XgMagicRefusalTests
{
    private const string BadMagicMessage = "Not a valid XG file: expected magic 0x484D4752 but got 0xEFBEADDE.";

    private static byte[] ValidBytes() => XgBytesBuilder.BuildMinimalXgFile().ToArray();

    private static byte[] BadMagicBytes()
    {
        byte[] bytes = ValidBytes();
        bytes[0] = 0xDE; bytes[1] = 0xAD; bytes[2] = 0xBE; bytes[3] = 0xEF;
        return bytes;
    }

    [Fact]
    public void TheCheck_AcceptsTheValidMagicNumber()
    {
        RichGameHeaderParser.RefusalOf(0x484D4752u).Should().BeNull();
    }

    [Fact]
    public void TheCheck_ReturnsAnUnthrownRefusal_ForABadMagicNumber()
    {
        var refusal = RichGameHeaderParser.RefusalOf(0xEFBEADDEu);

        refusal.Should().BeOfType<InvalidDataException>().Which.Message.Should().Be(BadMagicMessage);
        refusal!.StackTrace.Should().BeNull("the check returns the refusal; it does not throw it");
    }

    [Fact]
    public void XgFileReader_ReadStream_StillThrows_ForABadMagicNumber()
    {
        var act = () => XgFileReader.ReadStream(new MemoryStream(BadMagicBytes()));

        act.Should().Throw<InvalidDataException>().WithMessage(BadMagicMessage);
    }

    [Fact]
    public void XgSourceReader_ReadStream_FailsTheSource_WithTheRefusal_AndLeavesTheStreamOpen()
    {
        using var stream = new MemoryStream(BadMagicBytes());

        var read = XgSourceReader.ReadStream("bad.xg", stream, XgSourceFormat.Xg);

        var failed = read.Should().BeOfType<XgSourceFailed>().Subject;
        failed.SourceName.Should().Be("bad.xg");
        failed.Exception.Should().BeOfType<InvalidDataException>().Which.Message.Should().Be(BadMagicMessage);
        stream.CanRead.Should().BeTrue("the stream is borrowed");
    }

    [Fact]
    public void XgSourceReader_ReadStream_StillDecodesValidInput_FromPositionZero_AndLeavesTheStreamOpen()
    {
        byte[] bytes = ValidBytes();   // each build has its own game id: decode one set of bytes twice
        using var stream = new MemoryStream(bytes);

        var read = XgSourceReader.ReadStream("ok.xg", stream, XgSourceFormat.Xg);

        var decoded = read.Should().BeOfType<XgSourceDecoded>().Subject;
        XgFileReader.ToJson(decoded.File).Should().Be(
            XgFileReader.ToJson(XgFileReader.ReadStream(new MemoryStream(bytes))));
        stream.CanRead.Should().BeTrue("the stream is borrowed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void XgSourceReader_ReadStream_FailsTheSource_WithEndOfStream_WhenFewerThanFourBytes(int length)
    {
        using var stream = new MemoryStream(ValidBytes()[..length]);

        var read = XgSourceReader.ReadStream("short.xg", stream, XgSourceFormat.Xg);

        read.Should().BeOfType<XgSourceFailed>().Which.Exception.Should().BeOfType<EndOfStreamException>();
        stream.CanRead.Should().BeTrue("the stream is borrowed");
    }
}
