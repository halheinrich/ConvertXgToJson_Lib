using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace ConvertXgToJson_Lib.Writing;

/// <summary>
/// Writes the compressed XG payload: the concatenated zlib streams that
/// follow the RichGameHeader, plus the trailing file manifest. Mirror of
/// <see cref="Parsing.XgDecompressor"/>'s container knowledge, extended
/// with the manifest layout that the reader never needed (it finds stream
/// boundaries by scanning for zlib headers; real XG writes — and this
/// writer reproduces — an explicit directory).
///
/// Stream order matches real XG files: temp.xg, temp.xgr (when rollouts
/// exist), temp.xgi, temp.xgc (when comments exist), then the manifest.
///
/// Manifest layout (decoded from the fixture corpus; one 532-byte entry
/// per inner file, in stream order, the manifest itself unlisted):
///   [0..511]   Pascal ANSI filename (1-byte length + body, zero padded)
///   [512..515] uncompressed size
///   [516..519] compressed size
///   [520..523] offset of the compressed stream, relative to content start
///   [524..527] CRC32 (IEEE) of the uncompressed bytes
///   [528..531] constant 0x200
/// </summary>
internal static class XgContainerWriter
{
    private const int ManifestEntrySize = 532;
    private const int ManifestNameField = 512;
    private const uint ManifestConstant = 0x200;

    /// <summary>
    /// One inner file of the container: its temp-file name and uncompressed
    /// payload. Order of appearance = physical stream order.
    /// </summary>
    internal readonly record struct InnerFile(string Name, byte[] Data);

    /// <summary>
    /// Compresses each inner file into a zlib stream, appends the compressed
    /// manifest, and writes the whole payload to <paramref name="output"/>.
    /// </summary>
    internal static void Write(Stream output, IReadOnlyList<InnerFile> files)
    {
        var compressed = new byte[files.Count][];
        for (int i = 0; i < files.Count; i++)
            compressed[i] = Compress(files[i].Data);

        byte[] manifest = BuildManifest(files, compressed);

        foreach (byte[] stream in compressed)
            output.Write(stream);
        output.Write(Compress(manifest));
    }

    private static byte[] BuildManifest(IReadOnlyList<InnerFile> files, byte[][] compressed)
    {
        byte[] manifest = new byte[files.Count * ManifestEntrySize];
        uint offset = 0;

        for (int i = 0; i < files.Count; i++)
        {
            Span<byte> entry = manifest.AsSpan(i * ManifestEntrySize, ManifestEntrySize);

            byte[] name = Encoding.Latin1.GetBytes(files[i].Name);
            entry[0] = (byte)Math.Min(name.Length, ManifestNameField - 1);
            name.AsSpan(0, entry[0]).CopyTo(entry[1..]);

            BitConverter.TryWriteBytes(entry[512..], (uint)files[i].Data.Length);
            BitConverter.TryWriteBytes(entry[516..], (uint)compressed[i].Length);
            BitConverter.TryWriteBytes(entry[520..], offset);
            BitConverter.TryWriteBytes(entry[524..], Crc32.HashToUInt32(files[i].Data));
            BitConverter.TryWriteBytes(entry[528..], ManifestConstant);

            offset += (uint)compressed[i].Length;
        }

        return manifest;
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return ms.ToArray();
    }
}
