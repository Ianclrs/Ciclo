using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Tests;

/// <summary>
/// A2 — contrato da máquina de estados da matrícula.
///
/// A tabela de transições é a autoridade, e o frontend mantém um **espelho** dela em
/// <c>frontend/src/domain/enrollmentStatus.ts</c> (a tela deriva as ações disponíveis por status; ver
/// o comentário lá sobre por que o espelho existe em vez de a API devolver as transições).
///
/// Este teste trava a tabela por inteiro de propósito: qualquer mudança aqui obriga quem mexe a
/// revisitar o espelho do frontend. Sem isso, o backend poderia deixar de aceitar uma transição que
/// a tela continua oferecendo — o usuário veria um botão que sempre falha.
///
/// O defeito que o A2 corrigiu começa aqui: antes desta rodada, `Rascunho` só ia para `Pendente`, e
/// a tela não chamava `submit` por nenhum caminho — matrícula em `Rascunho` não avançava.
/// </summary>
public sealed class EnrollmentStateMachineTests
{
    private static readonly Dictionary<EnrollmentStatus, string[]> ExpectedTransitions = new()
    {
        [EnrollmentStatus.Rascunho] = ["DocumentacaoPendente", "Pendente"],
        [EnrollmentStatus.Pendente] = ["Aprovado", "Cancelado", "DocumentacaoPendente", "Rejeitado"],
        [EnrollmentStatus.DocumentacaoPendente] = ["Aprovado", "Cancelado", "Pendente", "Rejeitado"],
        // Terminais: sem caminho de volta. A política de reversão é o item M12 do plano de correções.
        [EnrollmentStatus.Aprovado] = [],
        [EnrollmentStatus.Rejeitado] = [],
        [EnrollmentStatus.Cancelado] = [],
    };

    [Fact]
    public void TransitionTable_MatchesThePinnedContract()
    {
        foreach (var (from, expected) in ExpectedTransitions)
        {
            var actual = EnrollmentService.GetAllowedTransitions(from).OrderBy(name => name).ToArray();

            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// O estado que era inalcançável em produção: a tela nunca o criava e o submit nunca o
    /// escolhia. Ele precisa continuar alcançável a partir de <c>Rascunho</c> — Spec 50 FR-007.
    /// </summary>
    [Fact]
    public void DocumentacaoPendente_IsReachableFromRascunho()
    {
        Assert.True(EnrollmentService.CanTransition(
            EnrollmentStatus.Rascunho, EnrollmentStatus.DocumentacaoPendente));
    }

    /// <summary>
    /// Toda transição da tabela tem de ser realmente aceita, e as que não estão nela, recusadas.
    /// Trava a coerência entre o dicionário e <c>CanTransition</c>.
    /// </summary>
    [Fact]
    public void CanTransition_AgreesWithTheTransitionTable()
    {
        foreach (var from in Enum.GetValues<EnrollmentStatus>())
        {
            foreach (var to in Enum.GetValues<EnrollmentStatus>())
            {
                var expected = ExpectedTransitions[from].Contains(to.ToString());

                Assert.Equal(expected, EnrollmentService.CanTransition(from, to));
            }
        }
    }
}
