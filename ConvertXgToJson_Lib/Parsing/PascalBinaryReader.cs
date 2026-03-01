using System.Text;

namespace ConvertXgToJson_Lib.Parsing;

/// <summary>
/// Wraps a BinaryReader and tracks the current byte offset so that Pascal
/// record alignment rules can be enforced manually.
///
/// Pascal alignment rules (all little-endian):
///   Boolean, byte, ShortInt, string[N]  – no alignment
///   SmallInt, Word, array-of-Char/WideChar – 2-byte boundary
///   Integer, Dword, Longword, Single     – 4-byte boundary
///   Double, Int64, UInt64, TDateTime     – 8-byte boundary
/// </summary>
internal sealed class PascalBinaryReader(Stream stream) : IDisposable
{
    private readonly BinaryReader _reader = new(stream, Encoding.Latin1, leaveOpen: true);

    /// <summary>Current byte position relative to the stream origin.</summary>
    public long Position => stream.Position;

    // ------------------------------------------------------------------ //
    //  Alignment helpers
    // ------------------------------------------------------------------ //

    /// <summary>Advance the stream to the next multiple of <paramref name="boundary"/>.</summary>
    public void AlignTo(int boundary)
    {
        long rem = Position % boundary;
        if (rem != 0)
            Skip((int)(boundary - rem));
    }

    /// <summary>Skip <paramref name="count"/> bytes.</summary>
    public void Skip(int count) => _reader.BaseStream.Seek(count, SeekOrigin.Current);

    // ------------------------------------------------------------------ //
    //  Unaligned primitives
    // ------------------------------------------------------------------ //

    public bool   ReadBoolean()  => _reader.ReadByte() != 0;
    public byte   ReadByte()     => _reader.ReadByte();
    public sbyte  ReadShortInt() => _reader.ReadSByte();

    // ------------------------------------------------------------------ //
    //  2-byte-aligned
    // ------------------------------------------------------------------ //

    public short  ReadSmallInt() { AlignTo(2); return _reader.ReadInt16(); }
    public ushort ReadWord()     { AlignTo(2); return _reader.ReadUInt16(); }

    // ------------------------------------------------------------------ //
    //  4-byte-aligned
    // ------------------------------------------------------------------ //

    public int    ReadInteger()  { AlignTo(4); return _reader.ReadInt32(); }
    public uint   ReadDword()    { AlignTo(4); return _reader.ReadUInt32(); }
    public float  ReadSingle()   { AlignTo(4); return _reader.ReadSingle(); }

    // ------------------------------------------------------------------ //
    //  8-byte-aligned
    // ------------------------------------------------------------------ //

    public double ReadDouble()   { AlignTo(8); return _reader.ReadDouble(); }
    public long   ReadInt64()    { AlignTo(8); return _reader.ReadInt64(); }
    public ulong  ReadUInt64()   { AlignTo(8); return _reader.ReadUInt64(); }

    // ------------------------------------------------------------------ //
    //  Pascal TDateTime  (Double, days since 1899-12-30)
    // ------------------------------------------------------------------ //

    private static readonly DateTime PascalEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);

    public DateTime ReadTDateTime()
    {
        double d = ReadDouble(); // already 8-byte aligned
        if (d == 0) return PascalEpoch;
        return PascalEpoch.AddDays(d);
    }

    // ------------------------------------------------------------------ //
    //  Pascal string[N]  – 1-byte length prefix, ANSI body, NOT #0-terminated
    //  Total on-disk size = N+1 bytes.  No alignment.
    // ------------------------------------------------------------------ //

    public string ReadPascalAnsiString(int maxLen)
    {
        byte len = _reader.ReadByte();
        byte[] buf = _reader.ReadBytes(maxLen);        // always read maxLen bytes
        if (len > maxLen) len = (byte)maxLen;
        return Encoding.Latin1.GetString(buf, 0, len);
    }

    // ------------------------------------------------------------------ //
    //  array [0..N] of WideChar / Char  – UTF-16LE, #0-terminated
    //  Total on-disk size = (N+1)*2 bytes.  Aligns to 2-byte boundary first.
    // ------------------------------------------------------------------ //

    public string ReadWideCharArray(int elementCount)
    {
        AlignTo(2);
        byte[] raw = _reader.ReadBytes(elementCount * 2);
        // Find first #0 terminator
        int terminator = 0;
        while (terminator < elementCount && (raw[terminator * 2] != 0 || raw[terminator * 2 + 1] != 0))
            terminator++;
        return Encoding.Unicode.GetString(raw, 0, terminator * 2);
    }

    // ------------------------------------------------------------------ //
    //  TShortUnicodeString  – array[0..128] of Char = 129 WideChars = 258 bytes
    //  Aligns to 2-byte boundary.
    // ------------------------------------------------------------------ //

    public string ReadShortUnicodeString() => ReadWideCharArray(129);

    // ------------------------------------------------------------------ //
    //  TGUID  – 16 bytes, packed (D1=uint32, D2=uint16, D3=uint16, D4=8 bytes)
    // ------------------------------------------------------------------ //

    public Guid ReadGuid()
    {
        uint  d1 = _reader.ReadUInt32();
        ushort d2 = _reader.ReadUInt16();
        ushort d3 = _reader.ReadUInt16();
        byte[] d4 = _reader.ReadBytes(8);
        return new Guid((int)d1, (short)d2, (short)d3, d4);
    }

    // ------------------------------------------------------------------ //
    //  Raw byte block (e.g. for reading a whole fixed record at once)
    // ------------------------------------------------------------------ //

    public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

    public void Dispose() => _reader.Dispose();
}
