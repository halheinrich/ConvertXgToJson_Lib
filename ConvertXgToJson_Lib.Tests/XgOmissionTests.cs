using BgDataTypes_Lib;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The omission record's own rules (<see cref="XgOmission"/>): a game
/// omission names no move and no roll, a decision omission names both, and
/// its reader's form opens with the kind.
/// </summary>
public class XgOmissionTests
{
    [Fact]
    public void AGameOmission_NamesNoMove_AndReadsAsItsKindFileGameAndReason()
    {
        var omission = new XgOmission("match.xg", 3, null, null, XgOmissionKind.InvalidStanding, "the rule's sentence");

        omission.IsWholeGame.Should().BeTrue();
        omission.ToString().Should().Be("Invalid standing in match.xg, game 3: the rule's sentence");
    }

    [Fact]
    public void ADecisionOmission_NamesItsMoveAndRoll()
    {
        var omission = new XgOmission("match.xg", 3, 12, new DiceRoll(2, 5), XgOmissionKind.IllegalPlay, "the marker");

        omission.IsWholeGame.Should().BeFalse();
        omission.ToString().Should().Be("Illegal play in match.xg, game 3, move 12, roll 52: the marker");
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, 1)]
    public void AMoveWithoutARoll_OrARollWithoutAMove_IsRefused(int? move, int? die)
    {
        var act = () => new XgOmission("match.xg", 1, move, die is int d ? new DiceRoll(d, d) : null, XgOmissionKind.CorruptCandidate, "reason");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnEmptyReasonOrFile_AGameBelowOne_OrAnUndefinedKind_IsRefused()
    {
        FluentActions.Invoking(() => new XgOmission(" ", 1, null, null, XgOmissionKind.InvalidStanding, "r")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new XgOmission("m.xg", 1, null, null, XgOmissionKind.InvalidStanding, "")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new XgOmission("m.xg", 0, null, null, XgOmissionKind.InvalidStanding, "r")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new XgOmission("m.xg", 1, null, null, (XgOmissionKind)99, "r")).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TwoOmissionsOfTheSamePlaceAndReason_AreEqual()
    {
        var one = new XgOmission("match.xg", 2, 5, new DiceRoll(3, 1), XgOmissionKind.CorruptCandidate, "why");
        var other = new XgOmission("match.xg", 2, 5, new DiceRoll(3, 1), XgOmissionKind.CorruptCandidate, "why");

        one.Should().Be(other);
    }
}
