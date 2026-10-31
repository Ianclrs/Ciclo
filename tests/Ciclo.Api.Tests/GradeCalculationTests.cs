using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Tests;

/// <summary>
/// Spec 150, FR-006 — a fórmula do boletim é uma função pura (ADR-150-1), então as regras
/// registradas são provadas aqui sem banco: média ponderada, a recuperação nunca reduz a média,
/// limiares fixos de 6 e 4, as quatro situações e arredondamento em duas casas com meio para cima.
///
/// A leitura escopada ao responsável, que consome esta mesma classe, está coberta em
/// <see cref="ParentReportCardTests"/> — inclusive a substituição da lista vazia de
/// <c>GetChildGradesAsync</c>.
/// </summary>
public sealed class GradeCalculationTests
{
    [Fact]
    public void ComputeRegularAverage_WeighsEachScoreByItsOwnWeight()
    {
        // (8 * 1 + 6 * 3) / (1 + 3) = 26 / 4 = 6,5.
        var average = GradeCalculation.ComputeRegularAverage(
        [
            new AssessmentScore(8m, 1m, IsRecovery: false),
            new AssessmentScore(6m, 3m, IsRecovery: false)
        ]);

        Assert.Equal(6.5m, average);
    }

    [Fact]
    public void ComputeRegularAverage_IgnoresRecoveryScores()
    {
        // A nota de recuperação entra só na média final; somá-la aqui a contaria duas vezes.
        var average = GradeCalculation.ComputeRegularAverage(
        [
            new AssessmentScore(7m, 1m, IsRecovery: false),
            new AssessmentScore(10m, 5m, IsRecovery: true)
        ]);

        Assert.Equal(7m, average);
    }

    [Fact]
    public void ComputeRegularAverage_WithoutScoresOrWithZeroWeightReturnsNull()
    {
        Assert.Null(GradeCalculation.ComputeRegularAverage([]));

        // Soma de pesos zero por dado legado não pode virar divisão por zero.
        Assert.Null(GradeCalculation.ComputeRegularAverage([new AssessmentScore(9m, 0m, IsRecovery: false)]));
    }

    [Fact]
    public void ComputeRegularAverage_RoundsToTwoDecimalsAwayFromZero()
    {
        // (7 + 8 + 8) / 3 = 7,666… → 7,67.
        var repeating = GradeCalculation.ComputeRegularAverage(
        [
            new AssessmentScore(7m, 1m, IsRecovery: false),
            new AssessmentScore(8m, 1m, IsRecovery: false),
            new AssessmentScore(8m, 1m, IsRecovery: false)
        ]);
        Assert.Equal(7.67m, repeating);

        // (6,67 + 6,68) / 2 = 6,675 → 6,68: meio para cima, não para o par.
        var midpoint = GradeCalculation.ComputeRegularAverage(
        [
            new AssessmentScore(6.67m, 1m, IsRecovery: false),
            new AssessmentScore(6.68m, 1m, IsRecovery: false)
        ]);
        Assert.Equal(6.68m, midpoint);
    }

    [Fact]
    public void ComputeFinalAverage_KeepsTheRegularAverageAndNeverLowersIt()
    {
        Assert.Null(GradeCalculation.ComputeFinalAverage(null, null));

        // Sem recuperação lançada, a média final é a regular.
        Assert.Equal(7m, GradeCalculation.ComputeFinalAverage(7m, null));

        // Só a recuperação existe: ela é a média final.
        Assert.Equal(5m, GradeCalculation.ComputeFinalAverage(null, 5m));

        // A regra é max: recuperação menor mantém a média regular, maior assume a final.
        Assert.Equal(7m, GradeCalculation.ComputeFinalAverage(7m, 5m));
        Assert.Equal(8.5m, GradeCalculation.ComputeFinalAverage(5m, 8.5m));
    }

    [Fact]
    public void ResolveSituation_FollowsTheFixedThresholds()
    {
        Assert.Equal("sem_nota", GradeCalculation.ResolveSituation(null));

        // Limiar exato de aprovação.
        Assert.Equal("aprovado", GradeCalculation.ResolveSituation(GradeCalculation.ApprovalThreshold));
        Assert.Equal("aprovado", GradeCalculation.ResolveSituation(10m));

        // Faixa de recuperação: maior ou igual a 4 e menor que 6.
        Assert.Equal("recuperacao", GradeCalculation.ResolveSituation(5.99m));
        Assert.Equal("recuperacao", GradeCalculation.ResolveSituation(GradeCalculation.RecoveryThreshold));

        // Abaixo de 4 é reprovação.
        Assert.Equal("reprovado", GradeCalculation.ResolveSituation(3.99m));
        Assert.Equal("reprovado", GradeCalculation.ResolveSituation(0m));
    }

    [Fact]
    public void ThresholdsAreTheRegisteredConstants()
    {
        Assert.Equal(6.0m, GradeCalculation.ApprovalThreshold);
        Assert.Equal(4.0m, GradeCalculation.RecoveryThreshold);
    }

    [Fact]
    public void TheSameInputProducesTheSameFinalAverageOnEveryRun()
    {
        AssessmentScore[] scores =
        [
            new AssessmentScore(6.67m, 1m, IsRecovery: false),
            new AssessmentScore(6.68m, 2m, IsRecovery: false)
        ];

        var expected = GradeCalculation.ComputeFinalAverage(
            GradeCalculation.ComputeRegularAverage(scores), 6.5m);

        for (var run = 0; run < 5; run++)
        {
            var repeated = GradeCalculation.ComputeFinalAverage(
                GradeCalculation.ComputeRegularAverage(scores), 6.5m);

            Assert.Equal(expected, repeated);
        }
    }

    [Fact]
    public void Round_PropagatesNullAndKeepsTwoDecimals()
    {
        Assert.Null(GradeCalculation.Round(null));
        Assert.Equal(6.68m, GradeCalculation.Round(6.675m));
        Assert.Equal(7m, GradeCalculation.Round(7m));
    }
}
