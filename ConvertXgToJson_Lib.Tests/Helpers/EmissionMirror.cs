using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests.Helpers;

/// <summary>
/// Mirrors the iterator's cube emission filter over a file's raw records,
/// for tests that pair each emitted cube decision with the record it came
/// from. The walk is the iterator's own: a <see cref="MatchContext"/>
/// updated record by record, and <see cref="XgDecisionIterator.IsCubeDecision"/>
/// asked at each cube — so the analysed gate and the Crawford rule
/// (halheinrich/backgammon#201) are consulted where they live, never
/// restated here. A test that interleaves moves and cubes drives the same
/// context inline and asks the same predicate.
/// </summary>
internal static class EmissionMirror
{
    /// <summary>The cube records the iterator emits for <paramref name="file"/>, in record order.</summary>
    public static IEnumerable<CubeRecord> CubeDecisions(XgFile file, string sourceFile)
    {
        var context = new MatchContext(file.Records, sourceFile, file.Comments);
        foreach (var record in file.Records)
        {
            context.Update(record);
            if (record is CubeRecord cube && XgDecisionIterator.IsCubeDecision(cube, context))
                yield return cube;
        }
    }
}
