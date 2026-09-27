// DiagramRequestIteratorTests.cs
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Tests for <see cref="XgDecisionIterator.IterateDiagramRequests"/> over the
/// local corpus and named fixtures: the facts each record states from XG's
/// data. What a record derives from them — its pip counts, after-boards,
/// depth labels and ranks, best play and errors — is BgDataTypes_Lib's and
/// pinned there; how the derivations measure against XG's own numbers is
/// <see cref="XgCorpusAgreementTests"/>'.
/// </summary>
[Collection("FileIO")]
public class DiagramRequestIteratorTests
{
    /// <summary>XG's single-precision rounding around a probability of 0 or 1.</summary>
    private const double RoundingSlack = 1e-5;

    // -----------------------------------------------------------------------
    //  Cube decisions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every cube decision states XG's cube equities, and its stored win
    /// probabilities read as probabilities — to within XG's single-precision
    /// rounding, which leaves a probability of zero a few millionths either
    /// side (the corpus holds one at −4.9e-6). The records store XG's numbers
    /// verbatim and check no range.
    /// </summary>
    [Fact]
    public void IterateDiagramRequests_CubeRequest_EquityFieldsPopulated()
    {
        bool foundCube = false;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path))
                                                   .OfType<CubeDecision>())
            {
                foundCube = true;

                bool hasEquity = req.Decision.NoDoubleEquity != 0.0 || req.Decision.DoubleTakeEquity != 0.0;
                hasEquity.Should().BeTrue(
                    $"cube request in {Path.GetFileName(path)} should have non-zero equity fields");

                req.Decision.WinPctAfterNoDouble.Should().BeInRange(-RoundingSlack, 1.0 + RoundingSlack,
                    "WinPctAfterNoDouble should be a probability");
                req.Decision.WinPctAfterDoubleTake.Should().BeInRange(-RoundingSlack, 1.0 + RoundingSlack,
                    "WinPctAfterDoubleTake should be a probability");
            }
        }

        foundCube.Should().BeTrue("test data should contain at least one cube decision");
    }

    /// <summary>
    /// The taker's half exists only once a double was offered: where the
    /// record was not doubled, a cube decision states no taker action and no
    /// taker error of XG's — the one gate both read.
    /// </summary>
    [Fact]
    public void CubeRequest_TakerHalf_IsAbsentWhenNotDoubled()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            var cubeRecords = EmissionMirror.CubeDecisions(file, sourceFile).ToList();
            var cubeRequests = XgDecisionIterator.IterateDiagramRequests(file, sourceFile)
                .OfType<CubeDecision>()
                .ToList();

            cubeRecords.Count.Should().Be(cubeRequests.Count,
                $"{sourceFile}: cube record count should match cube request count");

            for (int i = 0; i < cubeRecords.Count; i++)
            {
                if (cubeRecords[i].Doubled == 1)
                    continue;
                var decision = cubeRequests[i].Decision;
                decision.UserTakerAction.Should().BeNull($"{sourceFile} cube[{i}]");
                decision.UnstatedTakerActionError.Should().BeNull($"{sourceFile} cube[{i}]");
                decision.UserTakeError.Should().BeNull($"{sourceFile} cube[{i}]");
            }
        }
    }

    // -----------------------------------------------------------------------
    //  The candidates
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every candidate of an analysed checker play carries its play — the one
    /// stored form of a candidate, which submitted-play matching keys off. A
    /// regression that dropped the translation would leave the empty play (a
    /// pass) on every candidate.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_AllCandidates_HavePopulatedPlay()
    {
        int candidatesChecked = 0;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile).OfType<CheckerPlayDecision>())
            {
                foreach (var play in req.Decision.Plays)
                {
                    play.Play.Count.Should().BeGreaterThan(0,
                        $"{sourceFile} {req.Id}: every candidate must carry its play");
                    candidatesChecked++;
                }
            }
        }

        candidatesChecked.Should().BeGreaterThan(0,
            "the .xg corpus must contain at least one analysed move candidate; " +
            "otherwise this test passes vacuously.");
    }

    /// <summary>
    /// Pins the translated plays against a known fixture,
    /// <c>Opening 32 65 64 31 65.xgp</c>: its first analysed decision is a
    /// 6-5 whose candidates, in XG's order, begin with 24/13 — stored as XG
    /// stores it, two sub-moves (24, 18) and (18, 13) — and whose third hits
    /// on point 1 and lands a second checker there, the hit carried in the
    /// sign of its first move's <see cref="Move.ToPt"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_OpeningFixture_FirstDecisionPlayMovesMatchExpected()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "Opening 32 65 64 31 65.xgp");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on the opening-roll fixture in TestData/FixtureFiles/.");

        var file = XgFileReader.ReadFile(path);
        var firstMove = XgDecisionIterator
            .IterateDiagramRequests(file, Path.GetFileName(path))
            .OfType<CheckerPlayDecision>()
            .First();

        firstMove.Decision.Dice.Should().Equal([6, 5], "the first analysed move decision in this fixture is a 6-5 roll");

        var plays = firstMove.Decision.Plays;
        plays.Count.Should().BeGreaterThanOrEqualTo(3,
            "fixture must yield at least three candidates for the spot-check");

        plays[0].Notation.Should().Be("24/13", "the notation joins the one checker's two sub-moves");
        plays[0].Play.Count.Should().Be(2, "XG stores the two sub-moves, and the record keeps them");
        plays[0].Play[0].Should().Be(new Move(24, 18));
        plays[0].Play[1].Should().Be(new Move(18, 13));

        plays[2].Notation.Should().Be("7/1* 6/1");
        plays[2].Play.Count.Should().Be(2);
        plays[2].Play[0].Should().Be(new Move(7, -1), "the landing on the blot is the hit");
        plays[2].Play[1].Should().Be(new Move(6, 1), "the second landing is plain: the blot is already on the bar");
    }

    /// <summary>
    /// <c>match35253054.xg</c> is the fixture where XG's native order is not
    /// equity order: some decision has a candidate past rank 0 rated above
    /// rank 0. The record keeps XG's order, so this is what makes the order
    /// pins elsewhere non-vacuous.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35253054_XgNativeOrderIsNotEquityOrder()
    {
        var file = Match35253054();

        XgDecisionIterator.IterateDiagramRequests(file, "match35253054.xg")
            .OfType<CheckerPlayDecision>()
            .Should().Contain(d => d.Decision.Plays.Skip(1).Any(c => c.Equity > d.Decision.Plays[0].Equity),
                "match35253054.xg should contain a decision whose XG rank 0 is not its highest equity");
    }

    /// <summary>
    /// Each candidate states its own depth, from its own evaluation level in
    /// XG's pane, not one decision-wide value: on <c>match35253054.xg</c> each
    /// candidate's facts are those its own level code states (or its own
    /// rollout's), and some decision holds adjacent candidates at different
    /// levels — XG's analysis depth varies per candidate.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35253054_EachCandidateStatesItsOwnDepth()
    {
        var file = Match35253054();
        var moves = MoveRecordsById(file, "match35253054.xg");
        bool foundVariation = false;

        foreach (var play in XgDecisionIterator.IterateDiagramRequests(file, "match35253054.xg").OfType<CheckerPlayDecision>())
        {
            var move = moves[play.Id];
            for (int i = 0; i < play.Decision.Plays.Count; i++)
            {
                var candidate = play.Decision.Plays[i];
                var expected = XgDepthFacts.Resolve(
                    move.Analysis.EvalLevels[i].Level, move.RolloutIndices[i], file.Rollouts);
                (candidate.AnalysisMode, candidate.AnalysisLevel, candidate.RolloutTrials)
                    .Should().Be((expected.Mode, expected.Level, expected.RolloutTrials), $"{play.Id} candidate {i + 1}");
                if (i > 0 && candidate.AnalysisLevel != play.Decision.Plays[i - 1].AnalysisLevel)
                    foundVariation = true;
            }
        }

        foundVariation.Should().BeTrue(
            "match35253054.xg must contain a decision whose adjacent candidates were analysed at different levels");
    }

    // -----------------------------------------------------------------------
    //  The session and the game
    // -----------------------------------------------------------------------

    /// <summary>
    /// A decision's session is its game's standing turned to the player on
    /// roll: its Crawford flag is the game's, and the standing's kind the
    /// terms'.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_SessionCrawford_MatchesTheGamesStanding()
    {
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            var state = new XgIteratorState();
            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile, state))
            {
                bool gameIsCrawford = state.GameInfo!.Standing is MatchStanding { IsCrawford: true };
                (req.Session is MatchSession { IsCrawford: true }).Should().Be(gameIsCrawford, $"{sourceFile} {req.Id}");
                req.Session.Kind.Should().Be(state.MatchInfo!.Terms.Kind, $"{sourceFile} {req.Id}");
            }
        }
    }

    /// <summary>
    /// <c>match35041658.xg</c> contains a Crawford game (game 4) and games
    /// that are not, so its records state both.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_Match35041658_HasCrawfordAndNonCrawfordRequests()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35041658.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected Crawford fixture not present: {path}.");

        var requests = XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), "match35041658.xg").ToList();

        requests.Should().Contain(r => r.Session is MatchSession && ((MatchSession)r.Session).IsCrawford,
            "match35041658 contains a Crawford game");
        requests.Should().Contain(r => r.Session is MatchSession && !((MatchSession)r.Session).IsCrawford,
            "match35041658 also contains games that are not");
    }

    /// <summary>
    /// A decision in an <c>.xg</c> file is identified by its game and move,
    /// both at least 1: plays number from the context's counter, cubes from
    /// the move their turn goes on to play, so even an opening cube is 1.
    /// </summary>
    [Fact]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_AllRequests_GameAndMoveNumberAtLeastOne()
    {
        int maxGameSeen = 0;
        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string sourceFile = Path.GetFileName(path);

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, sourceFile))
            {
                req.Game.Should().BeGreaterThanOrEqualTo(1, $"{sourceFile} {req.Id}");
                req.MoveNumber.Should().BeGreaterThanOrEqualTo(1, $"{sourceFile} {req.Id}");
                maxGameSeen = Math.Max(maxGameSeen, req.Game!.Value);
            }
        }

        maxGameSeen.Should().BeGreaterThan(1,
            "the corpus must include a record from a game past the first; otherwise a hard-coded game 1 would pass");
    }

    /// <summary>
    /// <c>match35041658.xg</c> and <c>match35253054.xg</c> start every game
    /// from the standard position, so every record states a standard start.
    /// </summary>
    [Theory]
    [InlineData("match35041658.xg")]
    [InlineData("match35253054.xg")]
    [Trait("Category", "FileIO")]
    public void IterateDiagramRequests_StandardStartFixture_AllRequestsIsStandardStartTrue(string fixtureName)
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, fixtureName);
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException($"Expected fixture not present: {path}");

        var requests = XgDecisionIterator.IterateDiagramRequests(XgFileReader.ReadFile(path), fixtureName).ToList();

        requests.Should().NotBeEmpty($"{fixtureName} should yield at least one diagram request");
        requests.Should().OnlyContain(r => r.Descriptive.IsStandardStart == true);
    }

    // -----------------------------------------------------------------------
    //  BgDecisionData sample output
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes a JSON sample of BgDecisionData records to TestData/BgDecisionData/:
    /// - First match: up to 5 records with a non-zero player's error for each
    ///   of a play (under the default ranking), a double and a take (up to 15
    ///   records total)
    /// - Each subsequent match: up to 1 record per error type (up to 3 total)
    /// One file per match, only written when the match contributes at least one record.
    /// </summary>
    [Fact]
    public void BgDecisionData_WriteSampleJson()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Mirrors the product contract in XgJsonOptions: camelCase tokens,
            // names only (halheinrich/backgammon#164). Not a candidate to
            // collapse onto XgJsonOptions.Default — this writes a different
            // document (BgDecisionData, not XgFile) and deliberately omits that
            // document's converters, so the shapes encode independent decisions.
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            },
        };

        Directory.CreateDirectory(TestPaths.BgDecisionDataDir);
        File.WriteAllText(Path.Combine(TestPaths.BgDecisionDataDir, "GotHere.txt"), "ok");

        bool isFirstMatch = true;

        foreach (var path in TestPaths.XgFiles)
        {
            var file = XgFileReader.ReadFile(path);
            string matchId = Path.GetFileNameWithoutExtension(path);

            int quota = isFirstMatch ? 5 : 1;

            var playErrors = new List<BgDecisionData>();
            var doubleErrors = new List<BgDecisionData>();
            var takeErrors = new List<BgDecisionData>();

            foreach (var req in XgDecisionIterator.IterateDiagramRequests(file, Path.GetFileName(path)))
            {
                req.Switch(
                    play =>
                    {
                        if (playErrors.Count < quota
                            && play.Decision.RankedBy(PlayRanking.Equity).PlayerResult.TryGetError(out double error)
                            && error > 0)
                            playErrors.Add(req);
                    },
                    cube =>
                    {
                        if (doubleErrors.Count < quota && cube.Decision.UserDoubleError > 0)
                            doubleErrors.Add(req);
                        if (takeErrors.Count < quota && cube.Decision.UserTakeError > 0)
                            takeErrors.Add(req);
                    });

                if (playErrors.Count >= quota && doubleErrors.Count >= quota && takeErrors.Count >= quota)
                    break;
            }

            var sample = playErrors
                .Concat(doubleErrors)
                .Concat(takeErrors)
                .Distinct()
                .ToList();

            if (sample.Count == 0) continue;

            var output = new
            {
                matchId,
                playErrorSamples = playErrors,
                doubleErrorSamples = doubleErrors,
                takeErrorSamples = takeErrors,
            };

            string outPath = Path.Combine(TestPaths.BgDecisionDataDir, matchId + ".json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(output, opts));

            isFirstMatch = false;
        }
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private static XgFile Match35253054()
    {
        string path = Path.Combine(TestPaths.FixtureFilesDir, "match35253054.xg");
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Expected fixture not present: {path}. " +
                "This test depends on match35253054.xg being in TestData/FixtureFiles/.");
        return XgFileReader.ReadFile(path);
    }

    /// <summary>Each move record of <paramref name="file"/>, by the id a checker play at it is stamped with.</summary>
    private static Dictionary<DecisionId, MoveRecord> MoveRecordsById(XgFile file, string sourceFile)
    {
        var moves = new Dictionary<DecisionId, MoveRecord>();
        var context = new MatchContext(file.Records, file.Comments);
        foreach (var record in file.Records)
        {
            context.Update(record);
            if (record is MoveRecord move)
                moves[XgDecisionIterator.BuildDecisionId(sourceFile, context.GameNumber, context.MoveNumber, isCube: false)] = move;
        }
        return moves;
    }
}
