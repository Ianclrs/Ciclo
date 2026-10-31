namespace Ciclo.Infrastructure.Services;

/// <summary>
/// Nota de uma avaliação como o cálculo do boletim a enxerga: valor, peso e se é recuperação.
/// </summary>
public readonly record struct AssessmentScore(decimal Value, decimal Weight, bool IsRecovery);

/// <summary>
/// Fórmula única do boletim. Não conhece banco: a mesma entrada produz sempre a mesma saída,
/// inclusive nas casas decimais, porque todo arredondamento usa
/// <see cref="MidpointRounding.AwayFromZero"/> e nenhum valor passa por ponto flutuante binário.
///
/// Regras registradas (ADR-150-1): a média regular é ponderada apenas pelas avaliações não
/// recuperativas que têm nota lançada; a recuperação nunca reduz a média; os limiares de
/// aprovação e de recuperação são constantes fixas.
/// </summary>
public static class GradeCalculation
{
    public const decimal ApprovalThreshold = 6.0m;
    public const decimal RecoveryThreshold = 4.0m;

    /// <summary>
    /// <c>soma(Value * Weight) / soma(Weight)</c> sobre as avaliações de recuperação ignoradas.
    /// Devolve nulo quando não há nenhuma nota ou quando a soma dos pesos é zero, em vez de dividir por zero.
    /// </summary>
    public static decimal? ComputeRegularAverage(IEnumerable<AssessmentScore> scores)
    {
        var weightedSum = 0m;
        var weightSum = 0m;

        foreach (var score in scores)
        {
            if (score.IsRecovery)
                continue;

            weightedSum += score.Value * score.Weight;
            weightSum += score.Weight;
        }

        return weightSum == 0m ? null : Round(weightedSum / weightSum);
    }

    /// <summary>
    /// Sem nota de recuperação, a média final é a regular; só com recuperação, é a recuperação;
    /// com as duas, é a maior delas — a recuperação nunca reduz a média do aluno.
    /// </summary>
    public static decimal? ComputeFinalAverage(decimal? regularAverage, decimal? recoveryGrade)
    {
        if (recoveryGrade is null)
            return regularAverage;

        if (regularAverage is null)
            return recoveryGrade;

        return Round(Math.Max(regularAverage.Value, recoveryGrade.Value));
    }

    /// <summary>
    /// Situação do componente: <c>aprovado</c> (maior ou igual a 6), <c>recuperacao</c>
    /// (maior ou igual a 4 e menor que 6), <c>reprovado</c> (menor que 4) e <c>sem_nota</c> (média nula).
    /// </summary>
    public static string ResolveSituation(decimal? finalAverage)
    {
        if (finalAverage is null)
            return "sem_nota";

        if (finalAverage >= ApprovalThreshold)
            return "aprovado";

        return finalAverage >= RecoveryThreshold ? "recuperacao" : "reprovado";
    }

    /// <summary>Duas casas decimais, meio para cima; nulo propaga como nulo.</summary>
    public static decimal? Round(decimal? value) =>
        value is null ? null : decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);
}
