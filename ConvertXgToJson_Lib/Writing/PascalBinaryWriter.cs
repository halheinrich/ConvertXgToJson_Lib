using System.Text;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Wraps a BinaryWriter and tracks the current byte offset so that Pascal
/// record alignment rules can be enforced manually. Byte-layout mirror of
/// <see cref="Parsing.PascalBinaryReader"/>: each <c>WriteX</c> aligns
/// exactly like its <c>ReadX</c> counterpart, so a record written with the
/// same call sequence lands every field at the same offset the reader
/// expects.
///
/// Pascal alignment rules (all little-endian):
///   Boolean, byte, ShortInt, string[N]  – no alignment
///   SmallInt, Word, array-of-Char/WideChar – 2-byte boundary
///   Integer, Dword, Longword, Single     – 4-byte boundary
///   Double, Int64, UInt64, TDateTime     – 8-byte boundary
///
/// Padding bytes are written as zeros — real XG files zero-fill the regions
/// between fields (verified across the fixture corpus), so explicit zero
/// padding reproduces the on-disk shape regardless of the target stream's
/// prior contents.
/// </summary>
internal sealed class PascalBinaryWriter(Stream stream) : IDisposable
{
    private readonly BinaryWriter _writer = new(stream, Encoding.Latin1, leaveOpen: true);

    /// <summary>Current byte position relative to the stream origin.</summary>
    public long Position => stream.Position;

    // ------------------------------------------------------------------ //
    //  Alignment helpers
    // ------------------------------------------------------------------ //

    /// <summary>Zero-pad the stream to the next multiple of <paramref name="boundary"/>.</summary>
    public void AlignTo(int boundary)
    {
        long rem = Position % boundary;
        if (rem != 0)
            WriteZeros((int)(boundary - rem));
    }

    /// <summary>Write <paramref name="count"/> zero bytes.</summary>
    public void WriteZeros(int count)
    {
        for (int i = 0; i < count; i++)
            _writer.Write((byte)0);
    }

    // ------------------------------------------------------------------ //
    //  Unaligned primitives
    // ------------------------------------------------------------------ //

    public void WriteBoolean(bool value)   => _writer.Write((byte)(value ? 1 : 0));
    public void WriteByte(byte value)      => _writer.Write(value);
    public void WriteShortInt(sbyte value) => _writer.Write(value);

    // ------------------------------------------------------------------ //
    //  2-byte-aligned
    // ------------------------------------------------------------------ //

    public void WriteSmallInt(short value) { AlignTo(2); _writer.Write(value); }
    public void WriteWord(ushort value)    { AlignTo(2); _writer.Write(value); }

    // ------------------------------------------------------------------ //
    //  4-byte-aligned
    // ------------------------------------------------------------------ //

    public void WriteInteger(int value)  { AlignTo(4); _writer.Write(value); }
    public void WriteDword(uint value)   { AlignTo(4); _writer.Write(value); }
    public void WriteSingle(float value) { AlignTo(4); _writer.Write(value); }

    // ------------------------------------------------------------------ //
    //  8-byte-aligned
    // ------------------------------------------------------------------ //

    public void WriteDouble(double value) { AlignTo(8); _writer.Write(value); }
    public void WriteInt64(long value)    { AlignTo(8); _writer.Write(value); }
    public void WriteUInt64(ulong value)  { AlignTo(8); _writer.Write(value); }

    // ------------------------------------------------------------------ //
    //  Pascal TDateTime  (Double, days since 1899-12-30)
    // ------------------------------------------------------------------ //

    private static readonly DateTime PascalEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);

    public void WriteTDateTime(DateTime value)
    {
        double days = value <= PascalEpoch ? 0 : (value - PascalEpoch).TotalDays;
        WriteDouble(days);
    }

    // ------------------------------------------------------------------ //
    //  Pascal string[N]  – 1-byte length prefix, ANSI body, NOT #0-terminated
    //  Total on-disk size = N+1 bytes.  No alignment.
    // ------------------------------------------------------------------ //

    public void WritePascalAnsiString(string value, int maxLen)
    {
        byte[] body = Encoding.Latin1.GetBytes(value);
        int len = Math.Min(body.Length, maxLen);
        _writer.Write((byte)len);
        _writer.Write(body, 0, len);
        WriteZeros(maxLen - len);
    }

    // ------------------------------------------------------------------ //
    //  array [0..N] of WideChar / Char  – UTF-16LE, #0-terminated
    //  Total on-disk size = (N+1)*2 bytes.  Aligns to 2-byte boundary first.
    //  Content is truncated to elementCount-1 chars so at least one #0
    //  terminator always survives (the reader stops at the first #0).
    // ------------------------------------------------------------------ //

    public void WriteWideCharArray(string value, int elementCount)
    {
        AlignTo(2);
        int chars = Math.Min(value.Length, elementCount - 1);
        byte[] body = Encoding.Unicode.GetBytes(value, 0, chars);
        _writer.Write(body);
        WriteZeros(elementCount * 2 - body.Length);
    }

    // ------------------------------------------------------------------ //
    //  TShortUnicodeString  – array[0..128] of Char = 129 WideChars = 258 bytes
    // ------------------------------------------------------------------ //

    public void WriteShortUnicodeString(string value) => WriteWideCharArray(value, 129);

    // ------------------------------------------------------------------ //
    //  TGUID  – 16 bytes, packed (D1=uint32, D2=uint16, D3=uint16, D4=8 bytes)
    //  Guid.ToByteArray() produces exactly this layout.
    // ------------------------------------------------------------------ //

    public void WriteGuid(Guid value) => _writer.Write(value.ToByteArray());

    // ------------------------------------------------------------------ //
    //  Raw byte block
    // ------------------------------------------------------------------ //

    public void WriteBytes(byte[] data) => _writer.Write(data);

    public void Dispose() => _writer.Dispose();
}
