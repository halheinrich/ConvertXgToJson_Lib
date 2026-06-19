using ConvertXgToJson_Lib;
namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for the public XG-format file-discovery helpers on
/// <see cref="XgFileReader"/> — <see cref="XgFileReader.IsXgFormatFile"/> and
/// <see cref="XgFileReader.EnumerateXgFormatFiles"/>. These single-source the
/// <c>.xg</c> / <c>.xgp</c> discovery rule that was previously duplicated
/// privately across the producer and its consumers.
/// </summary>
[Collection("FileIO")]
public class XgFileReaderDiscoveryTests
{
    // -----------------------------------------------------------------------
    //  XgFormatExtensions — the single source
    // -----------------------------------------------------------------------

    /// <summary>
    /// The published extension set is exactly <c>.xg</c> then <c>.xgp</c>, in
    /// that order — the order both discovery members derive from.
    /// </summary>
    [Fact]
    public void XgFormatExtensions_IsXgThenXgp()
    {
        XgFileReader.XgFormatExtensions.Should().Equal(".xg", ".xgp");
    }

    // -----------------------------------------------------------------------
    //  IsXgFormatFile
    // -----------------------------------------------------------------------

    /// <summary>
    /// XG-format extensions are recognized, including mixed case — extension
    /// matching is case-insensitive.
    /// </summary>
    [Theory]
    [InlineData("match.xg")]
    [InlineData("position.xgp")]
    [InlineData("MATCH.XG")]
    [InlineData("Position.XgP")]
    [InlineData(@"C:\some\dir\match35041658.xg")]
    public void IsXgFormatFile_TrueForXgAndXgp(string path)
    {
        XgFileReader.IsXgFormatFile(path).Should().BeTrue(
            $"'{path}' has an XG-format extension");
    }

    /// <summary>
    /// Non-XG extensions, missing extensions, and near-misses are rejected.
    /// </summary>
    [Theory]
    [InlineData("export.json")]
    [InlineData("noextension")]
    [InlineData("archive.zip")]
    [InlineData("data.xgz")]   // not a .xg* glob match — only .xg / .xgp count
    [InlineData("notes.txt")]
    public void IsXgFormatFile_FalseForOthers(string path)
    {
        XgFileReader.IsXgFormatFile(path).Should().BeFalse(
            $"'{path}' is not an XG-format file");
    }

    // -----------------------------------------------------------------------
    //  EnumerateXgFormatFiles
    // -----------------------------------------------------------------------

    /// <summary>
    /// In a directory of mixed files, only the <c>.xg</c> and <c>.xgp</c>
    /// files are returned, with every <c>.xg</c> ordered before every
    /// <c>.xgp</c>; non-XG files are ignored. Synthetic temp directory — no
    /// dependency on the shared <c>TestData/xg</c> corpus.
    /// </summary>
    [Fact]
    public void EnumerateXgFormatFiles_ReturnsOnlyXgAndXgp_XgBeforeXgp()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "ConvertXgToJson_Lib.Tests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create files of each extension. Names are chosen so that a
            // naive single filesystem-order pass would interleave .xg and
            // .xgp (b.xgp sorts before c.xg), proving the .xg-first contract
            // is enforced by the two-pass enumeration, not by name order.
            File.WriteAllText(Path.Combine(tempDir, "a.xg"), "");
            File.WriteAllText(Path.Combine(tempDir, "c.xg"), "");
            File.WriteAllText(Path.Combine(tempDir, "b.xgp"), "");
            File.WriteAllText(Path.Combine(tempDir, "d.xgp"), "");
            File.WriteAllText(Path.Combine(tempDir, "ignore.json"), "");
            File.WriteAllText(Path.Combine(tempDir, "ignore.txt"), "");
            File.WriteAllText(Path.Combine(tempDir, "noext"), "");

            var names = XgFileReader.EnumerateXgFormatFiles(tempDir)
                .Select(Path.GetFileName)
                .ToList();

            names.Should().BeEquivalentTo(
                new[] { "a.xg", "c.xg", "b.xgp", "d.xgp" },
                "only .xg and .xgp files are XG-format inputs");

            // .xg group entirely precedes the .xgp group.
            int lastXg = names.FindLastIndex(n => n!.EndsWith(".xg", StringComparison.OrdinalIgnoreCase));
            int firstXgp = names.FindIndex(n => n!.EndsWith(".xgp", StringComparison.OrdinalIgnoreCase));
            lastXg.Should().BeLessThan(firstXgp,
                "every .xg file must be enumerated before any .xgp file");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// An empty directory yields no files.
    /// </summary>
    [Fact]
    public void EnumerateXgFormatFiles_EmptyDirectory_ReturnsEmpty()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "ConvertXgToJson_Lib.Tests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            XgFileReader.EnumerateXgFormatFiles(tempDir).Should().BeEmpty(
                "an empty directory contains no XG-format files");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// A directory containing only non-XG files yields nothing.
    /// </summary>
    [Fact]
    public void EnumerateXgFormatFiles_NoXgFiles_ReturnsEmpty()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "ConvertXgToJson_Lib.Tests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "a.json"), "");
            File.WriteAllText(Path.Combine(tempDir, "b.txt"), "");

            XgFileReader.EnumerateXgFormatFiles(tempDir).Should().BeEmpty(
                "no .xg or .xgp files are present");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
