using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Integration tests that iterate decisions from .xg files (direct) and
/// from pre-exported JSON files, writing CSV output to TestData/Csv.
/// </summary>
[Collection("FileIO")]
public class DecisionCsvTests
{
    // -----------------------------------------------------------------------
    //  Direct .xg iteration
    // -----------------------------------------------------------------------

    [Fact]
    public void XgDirect_WriteDecisionCsv()
    {
        var xgFiles = Directory.GetFiles(TestPaths.XgDir, "*.xg");
        xgFiles.Should().NotBeEmpty("TestData/xg should contain at least one .xg file");

        Directory.CreateDirectory(TestPaths.CsvDir);

        foreach (var xgPath in xgFiles)
        {
            string matchId = Path.GetFileNameWithoutExtension(xgPath);
            string csvPath = Path.Combine(TestPaths.CsvDir, matchId + ".csv");

            var file = XgFileReader.ReadFile(xgPath);
            var rows = XgDecisionIterator.Iterate(file, matchId).ToList();

            using var writer = new StreamWriter(csvPath);
            writer.WriteLine(DecisionRow.CsvHeader);
            foreach (var row in rows)
                writer.WriteLine(row.ToCsvLine());

            rows.Should().NotBeEmpty($"{matchId} should contain at least one analysed decision");
            rows.Should().OnlyContain(r => r.Xgid.StartsWith("XGID="),
                "every row should have a valid XGID");
            rows.Should().OnlyContain(r => r.Error >= 0,
                "error values should be non-negative");
        }
    }

    [Fact]
    public void XgDirect_AllDecisions_HaveNonEmptyPlayer()
    {
        foreach (var path in Directory.GetFiles(TestPaths.XgDir, "*.xg"))
        {
            string matchId = Path.GetFileNameWithoutExtension(path);
            var file = XgFileReader.ReadFile(path);
            var rows = XgDecisionIterator.Iterate(file, matchId).ToList();

            rows.Should().NotBeEmpty($"{matchId} should have decisions");
            rows.Should().OnlyContain(r => !string.IsNullOrEmpty(r.Player),
                $"all decisions in {matchId} should have a player name");
        }
    }

    [Fact]
    public void XgDirect_MoveDecisions_HaveNonZeroRoll()
    {
        foreach (var path in Directory.GetFiles(TestPaths.XgDir, "*.xg"))
        {
            string matchId = Path.GetFileNameWithoutExtension(path);
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId)
                                                   .Where(r => !r.IsCube))
            {
                row.Roll.Should().NotBe(0,
                    $"checker play in {matchId} game {row.Game} move {row.MoveNum} should have dice");
            }
        }
    }

    [Fact]
    public void XgDirect_AllDecisions_XgidPositionIs26Chars()
    {
        foreach (var path in Directory.GetFiles(TestPaths.XgDir, "*.xg"))
        {
            string matchId = Path.GetFileNameWithoutExtension(path);
            var file = XgFileReader.ReadFile(path);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                // XGID=<26chars>:... — colon at index 31
                int colonIndex = row.Xgid.IndexOf(':', 5);
                colonIndex.Should().Be(31, $"position string should be 26 chars in {row.Xgid}");
            }
        }
    }

    // -----------------------------------------------------------------------
    //  JSON-derived iteration
    // -----------------------------------------------------------------------

    [Fact]
    public void JsonDerived_WriteDecisionCsv()
    {
        EnsureJsonFilesExist();

        var jsonFiles = Directory.GetFiles(TestPaths.OutputDir, "*.json")
            .Where(p => File.Exists(Path.Combine(TestPaths.XgDir,
                Path.GetFileNameWithoutExtension(p) + ".xg")))
            .ToArray();

        jsonFiles.Should().NotBeEmpty("Output dir should have JSON from .xg files");

        Directory.CreateDirectory(TestPaths.CsvDir);

        foreach (var jsonPath in jsonFiles)
        {
            string matchId = Path.GetFileNameWithoutExtension(jsonPath);
            string csvPath = Path.Combine(TestPaths.CsvDir, matchId + "-fromjson.csv");

            var file = XgFileReader.ReadJson(jsonPath);
            var rows = XgDecisionIterator.Iterate(file, matchId).ToList();

            using var writer = new StreamWriter(csvPath);
            writer.WriteLine(DecisionRow.CsvHeader);
            foreach (var row in rows)
                writer.WriteLine(row.ToCsvLine());

            rows.Should().NotBeEmpty($"{matchId} JSON should contain at least one decision");
        }
    }

    [Fact]
    public void BothSources_ProduceSameRowCount()
    {
        EnsureJsonFilesExist();

        var jsonFiles = Directory.GetFiles(TestPaths.OutputDir, "*.json")
            .Where(p => File.Exists(Path.Combine(TestPaths.XgDir,
                Path.GetFileNameWithoutExtension(p) + ".xg")))
            .ToArray();

        jsonFiles.Should().NotBeEmpty();

        foreach (var jsonPath in jsonFiles)
        {
            string matchId = Path.GetFileNameWithoutExtension(jsonPath);
            string xgPath = Path.Combine(TestPaths.XgDir, matchId + ".xg");

            var fromXg = XgDecisionIterator.Iterate(XgFileReader.ReadFile(xgPath), matchId).ToList();
            var fromJson = XgDecisionIterator.Iterate(XgFileReader.ReadJson(jsonPath), matchId).ToList();

            fromJson.Count.Should().Be(fromXg.Count,
                $"{matchId}: JSON and .xg sources should produce the same number of decisions");
        }
    }

    /// <summary>
    /// Same perspective invariant verified over JSON-derived rows:
    /// away1 = MatchLength - onRollScore, away2 = MatchLength - opponentScore,
    /// cross-checked against XGID score fields.
    /// </summary>
    [Fact]
    public void JsonDerived_MatchScore_Away1_IsOnRollPlayersAwayScore()
    {
        EnsureJsonFilesExist();

        var jsonFiles = Directory.GetFiles(TestPaths.OutputDir, "*.json")
            .Where(p => File.Exists(Path.Combine(TestPaths.XgDir,
                Path.GetFileNameWithoutExtension(p) + ".xg")))
            .ToArray();

        jsonFiles.Should().NotBeEmpty();

        foreach (var jsonPath in jsonFiles)
        {
            string matchId = Path.GetFileNameWithoutExtension(jsonPath);
            var file = XgFileReader.ReadJson(jsonPath);

            foreach (var row in XgDecisionIterator.Iterate(file, matchId))
            {
                if (row.MatchLength == 0) continue; // money — no away scores

                var parts = row.Xgid.Split(':');
                int xgidScore1 = int.Parse(parts[5]);
                int xgidScore2 = int.Parse(parts[6]);

                var scoreParts = row.MatchScore.TrimEnd('C').Split('a', StringSplitOptions.RemoveEmptyEntries);
                int msAway1 = int.Parse(scoreParts[0]);
                int msAway2 = int.Parse(scoreParts[1]);

                msAway1.Should().Be(row.MatchLength - xgidScore1,
                    $"away1 should be on-roll player's away score in {matchId} " +
                    $"game {row.Game} move {row.MoveNum} (XGID score1={xgidScore1})");
                msAway2.Should().Be(row.MatchLength - xgidScore2,
                    $"away2 should be opponent's away score in {matchId} " +
                    $"game {row.Game} move {row.MoveNum} (XGID score2={xgidScore2})");
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ensures JSON files exist in the Output directory for every .xg file.
    /// Writes them if missing so JSON-derived tests do not depend on
    /// RealFileTests having run first.
    /// </summary>
    private static void EnsureJsonFilesExist()
    {
        Directory.CreateDirectory(TestPaths.OutputDir);
        foreach (var xgPath in Directory.GetFiles(TestPaths.XgDir, "*.xg"))
        {
            string outPath = Path.Combine(TestPaths.OutputDir,
                Path.GetFileNameWithoutExtension(xgPath) + ".json");
            if (!File.Exists(outPath))
            {
                var xgFile = XgFileReader.ReadFile(xgPath);
                File.WriteAllText(outPath, XgFileReader.ToJson(xgFile));
            }
        }
    }
}