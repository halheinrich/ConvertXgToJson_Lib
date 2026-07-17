using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Writing;

namespace ConvertXgToJson_Lib;

/// <summary>
/// Serializes an <see cref="XgFile"/> back into the XG binary container —
/// the writing mirror of <see cref="XgFileReader"/>.
///
/// Usage:
///   XgFileWriter.Write(xgFile, stream);      // Stream / WASM friendly
///   byte[] bytes = XgFileWriter.ToBytes(xgFile);
///
/// <para>
/// Output is semantically faithful, not byte-identical: record pointer
/// preambles are zeroed (XG rebuilds them on load), regions the reader
/// never parses are zero-filled, zlib compression level may differ, and
/// the RichGameHeader thumbnail is omitted (the <see cref="XgFile"/> model
/// does not carry it). A written file re-read through
/// <see cref="XgFileReader.ReadStream"/> parses to an equal model.
/// </para>
///
/// <para>
/// The record-level surface is format-generic — it serializes whatever
/// record list the <see cref="XgFile"/> holds, so it handles both
/// <c>.xgp</c> position content and full <c>.xg</c> match content. Only
/// the <c>.xgp</c> shape is validated against real XG imports so far; see
/// <see cref="XgpExporter"/> for the supported decision-level export path.
/// </para>
/// </summary>
public static class XgFileWriter
{
    /// <summary>
    /// Writes <paramref name="file"/> to <paramref name="output"/> as a
    /// complete XG binary container: RichGameHeader, then the concatenated
    /// zlib streams (temp.xg records, temp.xgr rollouts when present,
    /// temp.xgi first+last record index, temp.xgc comments when present),
    /// the file manifest, and the 36-byte end-record XG seeks to from EOF
    /// to locate that manifest.
    /// </summary>
    /// <param name="file">The file model to serialize.</param>
    /// <param name="output">Destination stream; written sequentially, left open.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="file"/> has no records, or its first
    /// record is not a <see cref="MatchHeaderRecord"/> — XG (and this
    /// library's own iterator) require the match header at index 0, so
    /// emitting such a file would produce unreadable output.
    /// </exception>
    public static void Write(XgFile file, Stream output)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(output);
        if (file.Records.Count == 0)
            throw new ArgumentException("XgFile has no records; an XG container requires at least a match header.", nameof(file));
        if (file.Records[0] is not MatchHeaderRecord)
            throw new ArgumentException("XgFile.Records[0] must be a MatchHeaderRecord; XG requires the match header first.", nameof(file));

        RichGameHeaderWriter.Write(output, file.Header);

        var inner = new List<XgContainerWriter.InnerFile>
        {
            new("temp.xg", SaveRecordWriter.WriteAll(file.Records)),
        };

        if (file.Rollouts.Count > 0)
            inner.Add(new("temp.xgr", RolloutContextWriter.WriteAll(file.Rollouts)));

        // temp.xgi always holds exactly two records: first and last of the
        // record stream (identical when the stream has a single record).
        inner.Add(new("temp.xgi", SaveRecordWriter.WriteAll(
            [file.Records[0], file.Records[^1]])));

        if (file.Comments.Count > 0)
            inner.Add(new("temp.xgc", CommentWriter.WriteAll(file.Comments)));

        XgContainerWriter.Write(output, inner);
    }

    /// <summary>
    /// Serializes <paramref name="file"/> to a byte array. Preferred entry
    /// point for browser-hosted (WASM) consumers with no filesystem.
    /// </summary>
    public static byte[] ToBytes(XgFile file)
    {
        using var ms = new MemoryStream();
        Write(file, ms);
        return ms.ToArray();
    }
}
