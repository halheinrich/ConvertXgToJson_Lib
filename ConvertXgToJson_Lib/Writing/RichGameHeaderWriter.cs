using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Writes the outer TRichGameHeader (8232 bytes, packed) — the mirror of
/// <see cref="Parsing.RichGameHeaderParser"/>.
///
/// The thumbnail is always written as absent (<c>ThumbnailSize = 0</c>):
/// the <see cref="XgFile"/> model does not carry the thumbnail bytes (the
/// reader skips them), so a faithful re-emission is impossible and the
/// compressed payload must start directly after the header. XG's own files
/// leave <c>ThumbnailOffset</c> at 0 even when a thumbnail is present, so
/// both thumbnail fields are simply zeroed.
/// </summary>
internal static class RichGameHeaderWriter
{
    private const uint MagicNumber = 0x484D4752; // "RGMH"
    internal const uint HeaderSize = 8232;

    public static void Write(Stream stream, RichGameHeader header)
    {
        using var w = new PascalBinaryWriter(stream);

        w.WriteDword(MagicNumber);
        w.WriteDword(header.HeaderVersion != 0 ? header.HeaderVersion : 1);
        w.WriteDword(HeaderSize);
        // ThumbnailOffset (Int64) sits at offset 12 — misaligned, but the
        // record is *packed*, so it must be written raw. WriteInt64 would
        // insert 4 alignment bytes here and corrupt the layout.
        w.WriteZeros(8);  // ThumbnailOffset — unused by XG (0 even when a thumbnail exists)
        w.WriteDword(0);  // ThumbnailSize  — no thumbnail (see class remarks)
        w.WriteGuid(header.GameId);

        // 4 × 1024 WideChars = 8192 bytes; header totals exactly 8232.
        w.WriteWideCharArray(header.GameName, 1024);
        w.WriteWideCharArray(header.SaveName, 1024);
        w.WriteWideCharArray(header.LevelName, 1024);
        w.WriteWideCharArray(header.Comments, 1024);
    }
}
