using System.Globalization;
using System.Text;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Tests.Helpers;
using Xunit.Abstractions;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The converter measured against XG's own numbers over the local corpus
/// (<c>TestData/xg/</c> and <c>TestData/xgp/</c>; halheinrich/backgammon#273,
/// halheinrich/backgammon#282). A record states only what XG stores and
/// BgDataTypes_Lib derives the rest; XG stores much of the rest too, so each
/// derivation has an oracle:
/// <list type="bullet">
///   <item><description>
///     <b>Every analysed decision has one of three outcomes</b> — it builds a
///     record, it is passed by as not a decision (a side borne off), or it is
///     skipped for a candidate invalid from its position, with a warning
///     naming it. Nothing else fails.
///   </description></item>
///   <item><description>
///     <b>The after-boards are XG's.</b> Each candidate's derived after-board
///     is XG's stored resulting position for it, and the player's is XG's
///     position after the played move.
///   </description></item>
///   <item><description>
///     <b>XG's error for the player's move is the depth-first error</b> of the
///     played candidate (SPEC-scoring §2a) — except where the depth-first best
///     is an opening-book candidate, which XG's recorded analysis evidently
///     did not rank (the umbrella's measurement on halheinrich/backgammon#282,
///     2026-09-26: 23 such opening moves in 36,101).
///   </description></item>
///   <item><description>
///     <b>XG's cube errors are the scoring policy's</b> errors of the stated
///     actions (BgDataTypes_Lib, "Cube-decision scoring on CubeDecisionData").
///   </description></item>
/// </list>
/// The corpus is gitignored and churns, so the invariants hold for any corpus
/// and no count is pinned; on an empty corpus every fact passes vacuously, by
/// design (AGENTS.md, "TestData convention"). This gates nothing. The measured
/// counts are in <see cref="Summary"/>'s output.
/// </summary>
/// <remarks>
/// <para>
/// <b>How the outcomes are observed without restating a rule.</b> The corpus
/// is walked twice. The mirror walk drives a <see cref="MatchContext"/> record
/// by record, as the iterator does, and asks the iterator's own emission
/// predicates which records are analysed decisions — and
/// <see cref="PositionData.IsDecisionPosition"/> which of those are not
/// decisions (its answer does not depend on the frame, so it is asked of the
/// stored position). The iterator walk runs the real iterator; each file is
/// named as an <c>.xg</c> there, so an <c>.xgp</c>'s decisions are seen
/// before its single-decision policy, which is checked on its own.
/// </para>
/// <para>
/// <b>Frames.</b> XG stores a move's starting position in player 1's frame and
/// every resulting position — each candidate's
/// (<see cref="BestMoveAnalysis.PositionsPlayed"/>) and the played move's
/// (<see cref="MoveRecord.FinalPosition"/>) — in the mover's. A record's
/// after-boards are in the next mover's frame, the mover's turned
/// (<see cref="BoardPosition.Flipped"/>), so XG's resulting position is
/// compared flipped, for either seat.
/// </para>
/// </remarks>
[Collection("FileIO")]
public class XgCorpusAgreementTests(XgCorpusAgreementTests.Measurement corpus, ITestOutputHelper output)
    : IClassFixture<XgCorpusAgreementTests.Measurement>
{
    /// <summary>
    /// How far an error XG stores may sit from the derivation it is compared
    /// with: XG computes its errors from single-precision equities, and a few
    /// of its cube errors are rounded to the fourth decimal (the umbrella's
    /// measurement, BgDataTypes_Lib's "Stored or derived").
    /// </summary>
    internal const double ErrorTolerance = 1e-4 + 1e-9;

    [Fact]
    public void EveryAnalysedDecision_BuildsARecord_IsPassedBy_OrIsSkippedForACorruptCandidate()
    {
        corpus.Unclassified.Should().BeEmpty("every analysed decision has exactly one of the three outcomes");
        corpus.UnexpectedRecords.Should().BeEmpty("a record comes only from an analysed decision");
        corpus.CorruptWarningsNamingNoDecision.Should().BeEmpty(
            "each corrupt-candidate warning names an analysed decision the iterator built no record for");
        corpus.Failures.Should().BeEmpty("nothing else fails");
        (corpus.Built + corpus.NotADecision + corpus.CorruptCandidate).Should().Be(corpus.AnalysedDecisions);
    }

    [Fact]
    public void AfterBoards_AreXgsOwnResultingPositions()
    {
        corpus.CandidateAfterBoardMismatches.Should().BeEmpty(
            "each candidate's derived after-board is XG's stored resulting position for it");
        corpus.PlayerAfterBoardMismatches.Should().BeEmpty(
            "the player's derived after-board is XG's position after the played move");
    }

    /// <summary>
    /// XG's recorded error for the player's move is the played candidate's
    /// error under depth first; where it is not, the depth-first best is an
    /// opening-book candidate — the one exception, stated as a rule.
    /// </summary>
    [Fact]
    public void PlayersError_IsTheDepthFirstError_SaveWhereTheDepthFirstBestIsABookCandidate()
    {
        corpus.PlayerErrorExceptions.Should().OnlyContain(e => e.BestIsBookCandidate,
            "XG's recorded analysis departs from depth first only where it did not rank the book candidate the record carries");
    }

    [Fact]
    public void CubeErrors_AreTheScoringPolicysErrorsOfTheStatedActions()
    {
        corpus.DoublerErrorMismatches.Should().BeEmpty("XG's doubling error is the stated doubler action's derived error");
        corpus.TakerErrorMismatches.Should().BeEmpty("XG's take error is the stated taker action's derived error");
    }

    /// <summary>
    /// Read as the <c>.xgp</c> it is, a position file emits the play its walk
    /// built if there is one, else its cube: the filters act upstream of the
    /// single-decision policy, so a play they pass by never suppresses the cube.
    /// </summary>
    [Fact]
    public void Xgp_EmitsThePlayItsWalkBuilt_ElseItsCube()
    {
        corpus.XgpPolicyMismatches.Should().BeEmpty();
    }

    [Fact]
    public void Summary()
    {
        output.WriteLine(corpus.Report());
    }

    // -----------------------------------------------------------------------
    //  The measurement: one walk of the corpus, shared by every fact
    // -----------------------------------------------------------------------

    /// <summary>
    /// Walks the local corpus once and records every outcome and every
    /// disagreement the facts assert on, with the counts the summary reports.
    /// </summary>
    public sealed class Measurement
    {
        public int Files { get; private set; }
        public int ParseFailures { get; private set; }
        public int AnalysedDecisions { get; private set; }
        public int Built { get; private set; }
        public int BuiltPlays { get; private set; }
        public int BuiltCubes { get; private set; }
        public int NotADecision { get; private set; }
        public int CorruptCandidate { get; private set; }
        public List<string> CorruptSkips { get; } = [];
        public List<string> Unclassified { get; } = [];
        public List<string> UnexpectedRecords { get; } = [];
        public List<string> CorruptWarningsNamingNoDecision { get; } = [];
        public List<string> Failures { get; } = [];

        public int CandidateAfterBoards { get; private set; }
        public List<string> CandidateAfterBoardMismatches { get; } = [];
        public int PlayerAfterBoards { get; private set; }
        public List<string> PlayerAfterBoardMismatches { get; } = [];

        public int PlayerErrors { get; private set; }
        public int PlayerErrorsAgreeingExactly { get; private set; }
        public int PlayerErrorsAgreeingUnderEquity { get; private set; }
        public int UnlistedPlayErrors { get; private set; }
        public List<(string Where, bool BestIsBookCandidate, string Detail)> PlayerErrorExceptions { get; } = [];

        public int DoublerErrors { get; private set; }
        public int DoublerErrorsExact { get; private set; }
        public double DoublerErrorMaxDifference { get; private set; }
        public List<string> DoublerErrorMismatches { get; } = [];
        public int TakerErrors { get; private set; }
        public int TakerErrorsExact { get; private set; }
        public double TakerErrorMaxDifference { get; private set; }
        public List<string> TakerErrorMismatches { get; } = [];
        public int UnstatedDoublerErrors { get; private set; }
        public int UnstatedTakerErrors { get; private set; }

        public int XgpFiles { get; private set; }
        public List<string> XgpPolicyMismatches { get; } = [];

        /// <summary>A threshold for counting an agreement exact: XG's single precision, not its fourth-decimal rounding.</summary>
        private const double Exact = 1e-6;

        public Measurement()
        {
            var paths = new List<string>();
            if (Directory.Exists(TestPaths.XgDir))
                paths.AddRange(TestPaths.XgFiles.OrderBy(p => p, StringComparer.Ordinal));
            if (Directory.Exists(TestPaths.XgpDir))
                paths.AddRange(TestPaths.XgpFiles.OrderBy(p => p, StringComparer.Ordinal));

            foreach (var path in paths)
            {
                Files++;
                XgFile file;
                try
                {
                    file = XgFileReader.ReadFile(path);
                }
                catch (Exception e) when (e is IOException or InvalidDataException)
                {
                    ParseFailures++;   // not this leg's question: a file the reader cannot parse has no decisions to classify
                    continue;
                }

                try
                {
                    Measure(file, Path.GetFileName(path));
                }
                catch (Exception e)
                {
                    Failures.Add($"{Path.GetFileName(path)}: {e.GetType().Name}: {e.Message}");
                }
            }
        }

        private void Measure(XgFile file, string name)
        {
            bool isXgp = name.EndsWith(".xgp", StringComparison.OrdinalIgnoreCase);
            string walkName = isXgp ? Path.GetFileNameWithoutExtension(name) + ".xg" : name;

            // The mirror: which records are analysed decisions, and which of
            // those are not decisions, asked of the iterator's own predicates.
            var sources = new Dictionary<DecisionId, SaveRecord>();
            var notDecisions = new HashSet<DecisionId>();
            var context = new MatchContext(file.Records, file.Comments);
            foreach (var record in file.Records)
            {
                context.Update(record);
                PositionEngine position;
                DecisionId id;
                switch (record)
                {
                    case MoveRecord move when XgDecisionIterator.IsAnalysed(move)
                                          && !XgDecisionIterator.IsSentinelOnlyAnalysis(move.Analysis)
                                          && XgDecisionIterator.StatesRoll(move):
                        position = move.InitialPosition;
                        id = XgDecisionIterator.BuildDecisionId(walkName, context.GameNumber, context.MoveNumber, isCube: false);
                        break;
                    case CubeRecord cube when XgDecisionIterator.IsAnalysedCubePane(cube, context):
                        position = cube.Position;
                        id = XgDecisionIterator.BuildDecisionId(walkName, context.GameNumber, context.MoveNumber + 1, isCube: true);
                        break;
                    default:
                        continue;
                }
                sources[id] = record;
                if (!PositionData.IsDecisionPosition(position.ToBoardPosition()))
                    notDecisions.Add(id);
            }
            AnalysedDecisions += sources.Count;
            NotADecision += notDecisions.Count;

            // The iterator, and the decisions its warnings name.
            var logger = new CapturingLogger();
            var records = XgDecisionIterator.IterateDiagramRequests(file, walkName, logger: logger).ToList();
            var corrupt = new HashSet<DecisionId>();
            for (int i = 0; i < logger.Entries.Count; i++)
            {
                if (!logger.Entries[i].Message.StartsWith("Corrupt candidate", StringComparison.Ordinal))
                    continue;
                var values = logger.Values[i];
                var id = XgDecisionIterator.BuildDecisionId(walkName,
                    Convert.ToInt32(values["Game"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(values["MoveNumber"], CultureInfo.InvariantCulture), isCube: false);
                if (!sources.ContainsKey(id) || notDecisions.Contains(id) || !corrupt.Add(id))
                    CorruptWarningsNamingNoDecision.Add($"{name}: {logger.Entries[i].Message}");
                else
                    CorruptSkips.Add($"{name}: {logger.Entries[i].Message}");
            }
            CorruptCandidate += corrupt.Count;

            var built = new Dictionary<DecisionId, BgDecisionData>();
            foreach (var record in records)
            {
                if (!sources.ContainsKey(record.Id) || notDecisions.Contains(record.Id) || corrupt.Contains(record.Id))
                    UnexpectedRecords.Add($"{name}: {record.Id}");
                built[record.Id] = record;
            }
            Built += records.Count;
            foreach (var id in sources.Keys)
            {
                int outcomes = (built.ContainsKey(id) ? 1 : 0) + (notDecisions.Contains(id) ? 1 : 0) + (corrupt.Contains(id) ? 1 : 0);
                if (outcomes != 1)
                    Unclassified.Add($"{name}: {id} ({outcomes} outcomes)");
            }

            foreach (var record in records)
            {
                record.Switch(
                    play => MeasurePlay(play, (MoveRecord)sources[play.Id], name),
                    cube => MeasureCube(cube, (CubeRecord)sources[cube.Id], name));
            }

            if (isXgp)
                MeasureXgpPolicy(file, name, records);
        }

        private void MeasurePlay(CheckerPlayDecision play, MoveRecord move, string name)
        {
            BuiltPlays++;
            string where = $"{name} {play.Id}";

            for (int i = 0; i < play.Decision.Plays.Count; i++)
            {
                CandidateAfterBoards++;
                var xg = move.Analysis.PositionsPlayed[i].ToBoardPosition().Flipped();
                if (play.AfterBoardOf(i) != xg)
                    CandidateAfterBoardMismatches.Add($"{where} candidate {i + 1} ({play.Decision.Plays[i].Notation})");
            }
            if (play.AfterPlayerBoard is { } after)
            {
                PlayerAfterBoards++;
                if (after != move.FinalPosition.ToBoardPosition().Flipped())
                    PlayerAfterBoardMismatches.Add(where);
            }

            if (move.MoveError <= -999.0)
                return;
            if (play.Decision.UserPlayIndex is null)
            {
                UnlistedPlayErrors++;
                return;
            }

            PlayerErrors++;
            double xgError = Math.Abs(move.MoveError);
            var depthFirst = play.Decision.RankedBy(PlayRanking.DepthFirst);
            if (play.Decision.RankedBy(PlayRanking.Equity).PlayerResult.TryGetError(out double equityError)
                && Math.Abs(equityError - xgError) <= ErrorTolerance)
                PlayerErrorsAgreeingUnderEquity++;
            if (depthFirst.PlayerResult.TryGetError(out double error) && Math.Abs(error - xgError) <= ErrorTolerance)
            {
                if (Math.Abs(error - xgError) <= Exact)
                    PlayerErrorsAgreeingExactly++;
                return;
            }

            string ours = depthFirst.PlayerResult.Match(
                () => "not recorded", () => "not scored", e => e.ToString("F5", CultureInfo.InvariantCulture), e => $"unstated {e:F5}");
            PlayerErrorExceptions.Add((
                where,
                depthFirst.Best.Candidate.AnalysisMode == AnalysisMode.BookRollout,
                $"move {play.MoveNumber}, standard start {play.IsStandardStart}, depth-first {ours}, XG {xgError.ToString("F5", CultureInfo.InvariantCulture)}"));
        }

        private void MeasureCube(CubeDecision cube, CubeRecord record, string name)
        {
            BuiltCubes++;
            var d = cube.Decision;
            if (d.UnstatedDoublerActionError is not null) UnstatedDoublerErrors++;
            if (d.UnstatedTakerActionError is not null) UnstatedTakerErrors++;

            if (d.UserDoublerAction is { } doubler && record.ErrorCube > -999.0)
            {
                DoublerErrors++;
                double difference = Math.Abs(d.DoublerActionError(doubler) - Math.Abs(record.ErrorCube));
                DoublerErrorMaxDifference = Math.Max(DoublerErrorMaxDifference, difference);
                if (difference <= Exact) DoublerErrorsExact++;
                if (difference > ErrorTolerance) DoublerErrorMismatches.Add($"{name} {cube.Id}: {difference:E3}");
            }
            if (d.UserTakerAction is { } taker && record.ErrorTake > -999.0)
            {
                TakerErrors++;
                double difference = Math.Abs(d.TakerActionError(taker) - Math.Abs(record.ErrorTake));
                TakerErrorMaxDifference = Math.Max(TakerErrorMaxDifference, difference);
                if (difference <= Exact) TakerErrorsExact++;
                if (difference > ErrorTolerance) TakerErrorMismatches.Add($"{name} {cube.Id}: {difference:E3}");
            }
        }

        private void MeasureXgpPolicy(XgFile file, string name, List<BgDecisionData> walked)
        {
            XgpFiles++;
            var expected = walked.FirstOrDefault(r => r.Kind == DecisionKind.CheckerPlay)
                ?? walked.FirstOrDefault(r => r.Kind == DecisionKind.Cube);
            var emitted = XgDecisionIterator.IterateDiagramRequests(file, name).ToList();

            if (emitted.Count > 1)
                XgpPolicyMismatches.Add($"{name}: {emitted.Count} decisions");
            else if ((expected is null) != (emitted.Count == 0))
                XgpPolicyMismatches.Add($"{name}: expected {(expected is null ? "none" : expected.Kind)}, emitted {emitted.Count}");
            else if (expected is not null && (emitted[0].Kind != expected.Kind || emitted[0].Board != expected.Board))
                XgpPolicyMismatches.Add($"{name}: expected the walk's {expected.Kind}, emitted a {emitted[0].Kind}");
        }

        /// <summary>The measured counts, for the leg report.</summary>
        public string Report()
        {
            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Corpus: {Files} files ({XgpFiles} .xgp), {ParseFailures} unparseable, {Failures.Count} failed.");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Analysed decisions: {AnalysedDecisions}: {Built} built ({BuiltPlays} plays, {BuiltCubes} cubes), {NotADecision} passed by as not decisions, {CorruptCandidate} skipped for a corrupt candidate.");
            foreach (var skip in CorruptSkips)
                text.AppendLine(CultureInfo.InvariantCulture, $"  skipped: {skip}");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"After-boards: candidates {CandidateAfterBoards - CandidateAfterBoardMismatches.Count}/{CandidateAfterBoards} agree; players {PlayerAfterBoards - PlayerAfterBoardMismatches.Count}/{PlayerAfterBoards} agree.");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Player's error: depth first agrees {PlayerErrors - PlayerErrorExceptions.Count}/{PlayerErrors} ({PlayerErrorsAgreeingExactly} within {Exact:E0}); equity agrees {PlayerErrorsAgreeingUnderEquity}/{PlayerErrors}; {UnlistedPlayErrors} unlisted plays carry XG's error as stored.");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  exceptions: {PlayerErrorExceptions.Count}, of which {PlayerErrorExceptions.Count(e => e.BestIsBookCandidate)} with a book candidate as the depth-first best.");
            foreach (var (where, book, detail) in PlayerErrorExceptions)
                text.AppendLine(CultureInfo.InvariantCulture, $"  {(book ? "book" : "NOT BOOK")}: {where}: {detail}");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Cube errors: doubler {DoublerErrors - DoublerErrorMismatches.Count}/{DoublerErrors} agree ({DoublerErrorsExact} within {Exact:E0}, max difference {DoublerErrorMaxDifference:E3}); taker {TakerErrors - TakerErrorMismatches.Count}/{TakerErrors} agree ({TakerErrorsExact} within {Exact:E0}, max difference {TakerErrorMaxDifference:E3}); stored as unstated: doubler {UnstatedDoublerErrors}, taker {UnstatedTakerErrors}.");
            text.AppendLine(CultureInfo.InvariantCulture,
                $".xgp policy: {XgpFiles - XgpPolicyMismatches.Count}/{XgpFiles} agree.");
            return text.ToString();
        }
    }
}
