using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Reads the outer TRichGameHeader from the beginning of a .XG file.
///
/// Layout (8232 bytes, packed):
///   Dword   MagicNumber        4
///   Dword   HeaderVersion      4
///   Dword   HeaderSize         4
///   Int64   ThumbnailOffset    8   (at offset 12 – but TRichGameHeader is packed so no padding here)
///   Dword   ThumbnailSize      4
///   TGUID   GameId            16
///   [1024]WideChar  GameName  2048
///   [1024]WideChar  SaveName  2048
///   [1024]WideChar  LevelName 2048
///   [1024]WideChar  Comments  2048
///                          = 8208 bytes
///
/// Note: TRichGameHeader is declared as a plain record (not with alignment-causing
/// sub-types), so we read it sequentially.  The Int64 at offset 12 is technically
/// misaligned in raw memory, but because it is a *packed* record in Pascal the
/// compiler does NOT pad it.  We therefore skip alignment for this header.
/// </summary>
internal static class RichGameHeaderParser
{
    private const uint MagicNumber = 0x484D4752; // "RGMH"

    public static (RichGameHeader Header, long ContentOffset) Read(Stream stream)
    {
        using var br = new System.IO.BinaryReader(stream, System.Text.Encoding.Latin1, leaveOpen: true);

        uint magic = br.ReadUInt32();
        if (magic != MagicNumber)
            throw new InvalidDataException(
                $"Not a valid XG file: expected magic 0x{MagicNumber:X8} but got 0x{magic:X8}.");

        uint version      = br.ReadUInt32();
        uint headerSize   = br.ReadUInt32();
        long thumbOffset  = br.ReadInt64();   // packed – no padding
        uint thumbSize    = br.ReadUInt32();
        Guid gameId       = ReadGuidPacked(br);

        string gameName   = ReadWideCharArray(br, 1024);
        string saveName   = ReadWideCharArray(br, 1024);
        string levelName  = ReadWideCharArray(br, 1024);
        string comments   = ReadWideCharArray(br, 1024);

        // Content starts right after the header + thumbnail blob
        long contentOffset = (thumbSize > 0) ? headerSize + thumbSize : stream.Position;

        return (new RichGameHeader
        {
            MagicNumber     = magic,
            HeaderVersion   = version,
            HeaderSize      = headerSize,
            ThumbnailOffset = thumbOffset,
            ThumbnailSize   = thumbSize,
            GameId          = gameId,
            GameName        = gameName,
            SaveName        = saveName,
            LevelName       = levelName,
            Comments        = comments,
        }, contentOffset);
    }

    // TGUID is always 16 bytes, packed (no alignment issues inside the packed header)
    private static Guid ReadGuidPacked(System.IO.BinaryReader br)
        => PascalBinaryReader.DecodeGuid(br.ReadBytes(16));

    private static string ReadWideCharArray(System.IO.BinaryReader br, int elementCount)
        => PascalBinaryReader.DecodeWideCharArray(br.ReadBytes(elementCount * 2), elementCount);
}
