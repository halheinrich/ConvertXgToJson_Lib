using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Integration tests that read real .xgp and .xg files from the TestData
/// directories and write JSON output next to the source files.
/// These tests validate that the full pipeline works end-to-end on real data.
/// </summary>
[Collection("FileIO")]
public class RealFileTests
{
    //private static string Peek(string path, long offset)
    //{
    //    if (offset < 0 || offset >= new FileInfo(path).Length) return "(out of range)";
    //    using var f = File.OpenRead(path);
    //    f.Position = offset;
    //    var buf = new byte[4];
    //    f.ReadExactly(buf);
    //    return string.Join(" ", buf.Select(b => b.ToString("X2")));
    //}
    //private static byte[] ReadBytesAt(string path, long offset, int count)
    //{
    //    using var f = File.OpenRead(path);
    //    f.Position = offset;
    //    var buf = new byte[count];
    //    f.ReadExactly(buf);
    //    return buf;
    //}
    // ------------------------------------------------------------------ //
    //  .xgp files
    // ------------------------------------------------------------------ //

    [Fact]
    public void XgpFiles_AllParseWithoutException()
    {
        var files = Directory.Exists(TestPaths.XgpDir)
            ? Directory.GetFiles(TestPaths.XgpDir, "*.xgp")
            : [];

        if (files.Length == 0)
            return;

        foreach (var path in files)
        {
            var act = () => XgFileReader.ReadFile(path);
            act.Should().NotThrow($"parsing {Path.GetFileName(path)} should not throw");
        }
    }

    [Fact]
    public void XgpFiles_WriteJson()
    {
        var files = Directory.Exists(TestPaths.XgpDir)
            ? Directory.GetFiles(TestPaths.XgpDir, "*.xgp")
            : [];

        if (files.Length == 0)
            return;

        Directory.CreateDirectory(TestPaths.OutputDir);

        foreach (var path in files)
        {
            var xgFile = XgFileReader.ReadFile(path);
            string json = XgFileReader.ToJson(xgFile);

            string outPath = Path.Combine(TestPaths.OutputDir, Path.GetFileNameWithoutExtension(path) + ".json");
            File.WriteAllText(outPath, json);

            // Basic sanity checks on the JSON
            json.Should().StartWith("{", $"{Path.GetFileName(path)} JSON should be an object");
            json.Should().Contain("\"header\"", $"{Path.GetFileName(path)} JSON should have a header property");
        }
    }

    [Fact]
    public void XgpFiles_HeaderHasNonEmptyGameName()
    {
        var files = Directory.Exists(TestPaths.XgpDir)
            ? Directory.GetFiles(TestPaths.XgpDir, "*.xgp")
            : [];

        if (files.Length == 0)
            return;

        foreach (var path in files)
        {
            var xgFile = XgFileReader.ReadFile(path);
            xgFile.Header.GameName.Should().NotBeNull(
                $"{Path.GetFileName(path)} should have a GameName");
        }
    }

    [Fact]
    public void XgpFiles_RecordsListIsNotEmpty()
    {
        var files = Directory.Exists(TestPaths.XgpDir)
            ? Directory.GetFiles(TestPaths.XgpDir, "*.xgp")
            : [];

        if (files.Length == 0)
            return;

        foreach (var path in files)
        {
            var xgFile = XgFileReader.ReadFile(path);
            xgFile.Records.Should().NotBeEmpty(
                $"{Path.GetFileName(path)} should contain at least one save record");
        }
    }

    [Fact]
    public void XgpFiles_FirstRecordIsMatchHeader()
    {
        var files = Directory.Exists(TestPaths.XgpDir)
            ? Directory.GetFiles(TestPaths.XgpDir, "*.xgp")
            : [];

        if (files.Length == 0)
            return;

        foreach (var path in files)
        {
            var xgFile = XgFileReader.ReadFile(path);
            xgFile.Records[0].Should().BeOfType<MatchHeaderRecord>(
                $"first record in {Path.GetFileName(path)} should be a MatchHeaderRecord");
        }
    }

    // ------------------------------------------------------------------ //
    //  .xg files
    // ------------------------------------------------------------------ //

    [Fact]
    public void XgFiles_AllParseWithoutException()
    {
        var files = Directory.Exists(TestPaths.XgDir)
            ? Directory.GetFiles(TestPaths.XgDir, "*.xg")
            : [];

        if (files.Length == 0)
            return;

        foreach (var path in files)
        {
            var act = () => XgFileReader.ReadFile(path);
            act.Should().NotThrow($"parsing {Path.GetFileName(path)} should not throw");
        }
    }

    [Fact]
    public void XgFiles_WriteJson()
    {
        var files = Directory.Exists(TestPaths.XgDir)
            ? Directory.GetFiles(TestPaths.XgDir, "*.xg")
            : [];

        if (files.Length == 0)
            return;

        Directory.CreateDirectory(TestPaths.OutputDir);

        foreach (var path in files)
        {
            var xgFile = XgFileReader.ReadFile(path);
            string json = XgFileReader.ToJson(xgFile);

            string outPath = Path.Combine(TestPaths.OutputDir, Path.GetFileNameWithoutExtension(path) + ".json"); 
            File.WriteAllText(outPath, json);

            json.Should().StartWith("{", $"{Path.GetFileName(path)} JSON should be an object");
            json.Should().Contain("\"header\"", $"{Path.GetFileName(path)} JSON should have a header property");
        }
    }

    [Fact]
    public void XgFiles_FirstRecordIsMatchHeader()
    {
        var files = Directory.Exists(TestPaths.XgDir)
            ? Directory.GetFiles(TestPaths.XgDir, "*.xg")
            : [];

        if (files.Length == 0)
            return;

        foreach (var path in files)
        {
            var xgFile = XgFileReader.ReadFile(path);
            xgFile.Records[0].Should().BeOfType<MatchHeaderRecord>(
                $"first record in {Path.GetFileName(path)} should be a MatchHeaderRecord");
        }
    }
}
