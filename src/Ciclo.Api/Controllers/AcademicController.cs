using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Security;
using Ciclo.Infrastructure.Services;

namespace Ciclo.Api.Controllers;

/// <summary>
/// Spec 150 — superfície administrativa da estrutura acadêmica e do lançamento de notas.
///
/// O controlador é só transporte: converte rota e corpo em chamada ao serviço e devolve o
/// resultado. Nenhum <c>try/catch</c> aqui — a exceção de domínio sobe e o
/// <c>ExceptionMiddleware</c> a traduz no formato canônico (400, 404 ou 409 com o código
/// estável no corpo), como já acontece com os demais domínios.
///
/// Autorização por action, com as constantes de <see cref="AccessPolicy.Roles"/>: leitura para
/// Admin/Staff, cadastro só para Admin e lançamento de nota para Admin/Staff.
/// </summary>
[ApiController]
[Route("api/academic")]
public class AcademicController : ControllerBase
{
    private readonly IAcademicService _academicService;

    public AcademicController(IAcademicService academicService)
    {
        _academicService = academicService;
    }

    // ── Anos letivos ────────────────────────────────────────────────

    /// <summary>FR-004: lista os anos letivos do tenant.</summary>
    [HttpGet("years")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureRead)]
    public async Task<IActionResult> GetYears(CancellationToken cancellationToken)
    {
        var result = await _academicService.ListYearsAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: cadastra um ano letivo.</summary>
    [HttpPost("years")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> CreateYear(
        [FromBody] CreateAcademicYearRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.CreateYearAsync(request, cancellationToken);
        return Created(string.Empty, result);
    }

    /// <summary>FR-004: atualiza um ano letivo.</summary>
    [HttpPut("years/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> UpdateYear(
        Guid id, [FromBody] UpdateAcademicYearRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.UpdateYearAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: exclui um ano letivo sem turmas dependentes.</summary>
    [HttpDelete("years/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> DeleteYear(Guid id, CancellationToken cancellationToken)
    {
        await _academicService.DeleteYearAsync(id, cancellationToken);
        return NoContent();
    }

    // ── Turmas ──────────────────────────────────────────────────────

    /// <summary>FR-004: lista as turmas do tenant, opcionalmente de um ano letivo.</summary>
    [HttpGet("classes")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureRead)]
    public async Task<IActionResult> GetClasses(
        [FromQuery] Guid? academicYearId, CancellationToken cancellationToken)
    {
        var result = await _academicService.ListClassesAsync(academicYearId, cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: cadastra uma turma.</summary>
    [HttpPost("classes")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> CreateClass(
        [FromBody] CreateSchoolClassRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.CreateClassAsync(request, cancellationToken);
        return Created(string.Empty, result);
    }

    /// <summary>FR-004: atualiza uma turma.</summary>
    [HttpPut("classes/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> UpdateClass(
        Guid id, [FromBody] UpdateSchoolClassRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.UpdateClassAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: exclui uma turma sem alunos nem avaliações.</summary>
    [HttpDelete("classes/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> DeleteClass(Guid id, CancellationToken cancellationToken)
    {
        await _academicService.DeleteClassAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>FR-005: roster da turma para a tela de lançamento de notas.</summary>
    [HttpGet("classes/{id:guid}/students")]
    [Authorize(Roles = AccessPolicy.Roles.GradeManage)]
    public async Task<IActionResult> GetClassStudents(Guid id, CancellationToken cancellationToken)
    {
        var result = await _academicService.ListClassStudentsAsync(id, cancellationToken);
        return Ok(result);
    }

    // ── Componentes curriculares ────────────────────────────────────

    /// <summary>FR-004: lista os componentes curriculares do tenant.</summary>
    [HttpGet("components")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureRead)]
    public async Task<IActionResult> GetComponents(CancellationToken cancellationToken)
    {
        var result = await _academicService.ListComponentsAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: cadastra um componente curricular.</summary>
    [HttpPost("components")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> CreateComponent(
        [FromBody] CreateCurriculumComponentRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.CreateComponentAsync(request, cancellationToken);
        return Created(string.Empty, result);
    }

    /// <summary>FR-004: atualiza um componente curricular.</summary>
    [HttpPut("components/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> UpdateComponent(
        Guid id, [FromBody] UpdateCurriculumComponentRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.UpdateComponentAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: exclui um componente sem avaliações.</summary>
    [HttpDelete("components/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> DeleteComponent(Guid id, CancellationToken cancellationToken)
    {
        await _academicService.DeleteComponentAsync(id, cancellationToken);
        return NoContent();
    }

    // ── Avaliações ──────────────────────────────────────────────────

    /// <summary>FR-004: lista as avaliações do tenant, filtráveis por turma e componente.</summary>
    [HttpGet("assessments")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureRead)]
    public async Task<IActionResult> GetAssessments(
        [FromQuery] Guid? schoolClassId,
        [FromQuery] Guid? curriculumComponentId,
        CancellationToken cancellationToken)
    {
        var result = await _academicService.ListAssessmentsAsync(
            schoolClassId, curriculumComponentId, cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: cadastra uma avaliação.</summary>
    [HttpPost("assessments")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> CreateAssessment(
        [FromBody] CreateAssessmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.CreateAssessmentAsync(request, cancellationToken);
        return Created(string.Empty, result);
    }

    /// <summary>FR-004: atualiza uma avaliação.</summary>
    [HttpPut("assessments/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> UpdateAssessment(
        Guid id, [FromBody] UpdateAssessmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.UpdateAssessmentAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>FR-004: exclui uma avaliação sem notas lançadas.</summary>
    [HttpDelete("assessments/{id:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.AcademicStructureManage)]
    public async Task<IActionResult> DeleteAssessment(Guid id, CancellationToken cancellationToken)
    {
        await _academicService.DeleteAssessmentAsync(id, cancellationToken);
        return NoContent();
    }

    // ── Lançamento de nota ──────────────────────────────────────────

    /// <summary>FR-005: lançamento idempotente da nota de um aluno em uma avaliação.</summary>
    [HttpPut("assessments/{assessmentId:guid}/grades/{studentId:guid}")]
    [Authorize(Roles = AccessPolicy.Roles.GradeManage)]
    public async Task<IActionResult> UpsertGrade(
        Guid assessmentId, Guid studentId, [FromBody] GradeRequest request, CancellationToken cancellationToken)
    {
        var result = await _academicService.UpsertGradeAsync(
            assessmentId, studentId, request.Value, cancellationToken);
        return Ok(result);
    }
}
