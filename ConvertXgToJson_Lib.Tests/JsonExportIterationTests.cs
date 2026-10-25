using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins that a corpus exported to JSON files iterates to the decisions its
/// XG sources do: every <c>.xg</c> file written through
/// <see cref="XgFileReader.ToJson(XgFile)"/>, read back from disk as JSON.
/// </summary>
[Collection("FileIO")]
public class JsonExportIterationTests
{
    [Fact]
    public void JsonExport_IteratesToTheXgDecisionCount()
    {
        var xgFiles = TestPaths.XgFiles.ToList();
        if (xgFiles.Count < 2)
            return;

        // A fresh directory, so stale files from a prior run can't inflate the count.
        string tempJsonDir = Path.Combine(Path.GetTempPath(), $"xg_json_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempJsonDir);
        try
        {
            foreach (var path in xgFiles)
            {
                string jsonPath = Path.Combine(tempJsonDir,
                    Path.GetFileNameWithoutExtension(path) + ".json");
                File.WriteAllText(jsonPath, XgFileReader.ToJson(XgFileReader.ReadFile(path)));
            }

            int xgDecisions = DirectoryWalk.XgRows(TestPaths.XgDir).Count();
            int jsonDecisions = DirectoryWalk.JsonRows(tempJsonDir).Count();

            jsonDecisions.Should().Be(xgDecisions,
                "JSON and XG iteration should yield identical decision counts");
        }
        finally
        {
            Directory.Delete(tempJsonDir, recursive: true);
        }
    }
}
