---
name: estrutura-academica-e-desempenho
status: verified
determinism: D
references: [V15]
---

# Spec 150: estrutura-academica-e-desempenho — Requirements

## Determinism Classification

**Class:** D — Deterministic

Every behavior in this spec is a pure function of persisted state and explicit inputs: entity CRUD guarded by unique constraints and foreign keys, one weighted-average formula with a fixed rounding mode, one situation mapping over two fixed constants, and one backfill SQL executed by a single migration. No behavior depends on model output, ranking or natural-language judgment, so two independent implementations that follow this document produce the same rows, the same numeric results and the same HTTP codes.

## Value Delivery

V15 of `management/vision.md`, verbatim:

> Modelo acadêmico explícito: turma/série como entidade (não string livre), ano letivo, componentes curriculares, avaliações e notas, com boletim por aluno no portal dos pais. Substitui o endpoint de notas que hoje devolve lista vazia e permite acompanhar desempenho de verdade.

Entrega, em termos concretos: as entidades `AcademicYear`, `SchoolClass`, `CurriculumComponent`, `Assessment` e `Grade` passam a existir com isolamento por tenant; `Student` passa a referenciar `AcademicYear` e `SchoolClass` com backfill dos valores legados; o administrador cadastra a estrutura e lança notas; o responsável vê o boletim por aluno com média ponderada, nota de recuperação e situação; `IParentService.GetChildGradesAsync` deixa de devolver lista vazia.

## Functional Requirements

### FR-001: Entidades de ano letivo e turma com vínculo ao aluno

**What:** Cria `AcademicYear` em `src/Ciclo.Core/Entities/AcademicYear.cs` com `Id` (Guid), `TenantId` (Guid), `Year` (int), `StartDate` (DateOnly), `EndDate` (DateOnly), `IsActive` (bool) e `ICollection<SchoolClass> Classes`; cria `SchoolClass` em `src/Ciclo.Core/Entities/SchoolClass.cs` com `Id`, `TenantId`, `Name` (string, obrigatório, máximo 50), `AcademicYearId` (Guid), navegação `AcademicYear` e `ICollection<Student> Students`; ambas implementam `ITenantScoped`. `Student` (`src/Ciclo.Core/Entities/Student.cs`) recebe `AcademicYearId` (Guid?) com navegação `AcademicYear` e `SchoolClassId` (Guid?) com navegação `SchoolClass`. As colunas legadas `Student.Turma` (string) e `Student.AnoLetivo` (int) permanecem como espelho de compatibilidade: continuam existindo, continuam sendo preenchidas a partir da entidade vinculada no caminho de escrita, e nenhum código novo as usa como autoridade.
**Why:** V15 pede turma/série como entidade, não string livre. O vínculo por chave estrangeira é o que dá identidade estável a uma turma e permite pendurar avaliações e notas nela. O espelho preserva o contrato de `StudentDto` e das telas existentes.
**Acceptance Criteria:**
- [ ] `AcademicYear` é declarada como entidade de tenant. [verify: grep-present `public class AcademicYear : ITenantScoped` `src/Ciclo.Core/Entities/AcademicYear.cs`]
- [ ] `SchoolClass` é declarada como entidade de tenant. [verify: grep-present `public class SchoolClass : ITenantScoped` `src/Ciclo.Core/Entities/SchoolClass.cs`]
- [ ] `SchoolClass` referencia o ano letivo por identificador. [verify: grep-present `public Guid AcademicYearId` `src/Ciclo.Core/Entities/SchoolClass.cs`]
- [ ] `Student` recebe o vínculo por turma. [verify: grep-present `SchoolClassId` `src/Ciclo.Core/Entities/Assessment.cs`]
- [ ] `Student` mantém a coluna espelho de turma. [verify: grep-present `public string Turma` `src/Ciclo.Core/Entities/Student.cs`]
- [ ] O contexto registra o conjunto de anos letivos. [verify: grep-present `DbSet<AcademicYear>` `src/Ciclo.Infrastructure/Data/AppDbContext.cs`]
- [ ] O contexto mapeia a chave estrangeira de turma no aluno. [verify: grep-present `AcademicYearId` `src/Ciclo.Core/Entities/SchoolClass.cs`]
- [ ] O caminho de escrita de aluno resolve turma e ano em entidades e devolve os identificadores no DTO. [verify: project-validation]
**Failure modes:**
- `SchoolClass.Name` vazio ou só com espaços: a requisição é rejeitada com código `class_name_required`; nada é persistido.
- `SchoolClass` com `AcademicYearId` inexistente no tenant: `academic_year_not_found`.
- Nome de turma repetido no mesmo ano e tenant: `class_name_already_exists`.
- `EndDate` anterior a `StartDate` em `AcademicYear`: `academic_year_range_invalid`.
- Dois anos letivos com o mesmo `Year` no mesmo tenant: `academic_year_already_exists`.
- Aluno sem turma resolvida: `SchoolClassId` e `AcademicYearId` ficam nulos; `StudentDto.Turma` projeta string vazia e `StudentDto.AnoLetivo` projeta `0`, preservando o contrato atual do frontend.

### FR-002: Entidades de componente curricular, avaliação e nota

**What:** Cria `CurriculumComponent` em `src/Ciclo.Core/Entities/CurriculumComponent.cs` com `Id`, `TenantId`, `Name` (máximo 100), `Code` (máximo 20) e `IsActive` (bool); cria `Assessment` em `src/Ciclo.Core/Entities/Assessment.cs` com `Id`, `TenantId`, `SchoolClassId`, `CurriculumComponentId`, `Title` (máximo 100), `Weight` (decimal, maior que zero, padrão `1`), `Date` (DateOnly) e `IsRecovery` (bool); cria `Grade` em `src/Ciclo.Core/Entities/Grade.cs` com `Id`, `TenantId`, `AssessmentId`, `StudentId`, `Value` (decimal, intervalo 0 a 10) e `RecordedAt` (DateTime UTC). As três implementam `ITenantScoped` e são mapeadas em `AppDbContext.OnModelCreating` com os índices únicos e as chaves estrangeiras descritos em design.md.
**Why:** V15 pede componentes curriculares, avaliações e notas como modelo explícito. Sem `Assessment` não existe peso; sem peso não existe média ponderada; sem `Grade` o boletim não tem dado.
**Acceptance Criteria:**
- [ ] `CurriculumComponent` é declarada como entidade de tenant. [verify: grep-present `public class CurriculumComponent : ITenantScoped` `src/Ciclo.Core/Entities/CurriculumComponent.cs`]
- [ ] `Assessment` declara o peso usado no cálculo. [verify: grep-present `public decimal Weight` `src/Ciclo.Core/Entities/Assessment.cs`]
- [ ] `Assessment` distingue avaliação de recuperação. [verify: grep-present `public bool IsRecovery` `src/Ciclo.Core/Entities/Assessment.cs`]
- [ ] `Grade` declara o valor da nota. [verify: grep-present `public decimal Value` `src/Ciclo.Core/Entities/Grade.cs`]
- [ ] O contexto registra o conjunto de notas. [verify: grep-present `DbSet<Grade>` `src/Ciclo.Infrastructure/Data/AppDbContext.cs`]
- [ ] O contexto aplica a unicidade por avaliação e aluno. [verify: grep-present `AssessmentId` `src/Ciclo.Core/Entities/Grade.cs`]
- [ ] A persistência de avaliação e nota é exercitada pelos testes do escopo declarado. [verify: project-validation]
**Failure modes:**
- `Weight` menor ou igual a zero: `assessment_weight_invalid`.
- `Value` fora de 0 a 10: `grade_out_of_range`.
- `Assessment` apontando para turma de outro tenant: `school_class_not_found`.
- `Assessment.Title` repetido na mesma turma e componente: `assessment_title_already_exists`.
- `Code` de componente repetido no tenant: `component_code_already_exists`.
- Toda leitura usa o filtro global de tenant; um identificador de outro tenant produz `404` e nenhum dado vaza.

### FR-003: Migração com backfill do vínculo acadêmico

**What:** Adiciona uma migração EF Core em `src/Ciclo.Infrastructure/Migrations/` que (a) cria as tabelas de `AcademicYears`, `SchoolClasses`, `CurriculumComponents`, `Assessments` e `Grades`, (b) adiciona as colunas nulas `AcademicYearId` e `SchoolClassId` em `Students` com as chaves estrangeiras e (c) executa um backfill que materializa um `AcademicYear` por par distinto `(TenantId, AnoLetivo)` com `AnoLetivo > 0`, um `SchoolClass` por tripla distinta `(TenantId, AnoLetivo, Turma)` com `Turma` não vazia após `trim`, e atualiza `Students.AcademicYearId` e `Students.SchoolClassId` por junção em tenant, ano e turma. O SQL do backfill vive em `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs` como `public const string Sql` e é aplicado pela migração via `migrationBuilder.Sql(AcademicStructureBackfill.Sql)`. As colunas legadas não são removidas nesta spec.
**Why:** V15 exige migração de `Student.Turma` e `Student.AnoLetivo` para referência com backfill. Concentrar o SQL em um símbolo estável permite testá-lo sem um PostgreSQL em execução, mantendo o comportamento determinístico.
**Acceptance Criteria:**
- [ ] O SQL de backfill vive em um símbolo estável. [verify: file-exists `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs`]
- [ ] O símbolo expõe o texto do backfill. [verify: grep-present `public const string Sql` `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs`]
- [ ] A migração aplica o backfill. [verify: grep-present `AcademicStructureBackfill.Sql` `src/Ciclo.Infrastructure/Migrations/`]
- [ ] O backfill cria os anos letivos distintos. [verify: grep-present `AcademicYears` `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs`]
- [ ] O backfill cria as turmas distintas. [verify: grep-present `SchoolClasses` `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs`]
- [ ] O backfill atualiza os alunos por junção. [verify: grep-present `UPDATE "Students"` `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs`]
- [ ] O projeto compila com a migração e o snapshot atualizado. [verify: project-validation]
**Failure modes:**
- Aluno com `AnoLetivo` menor ou igual a zero: nenhuma linha de ano é criada para ele e o aluno permanece com `AcademicYearId` nulo.
- Aluno com `Turma` vazia ou só com espaços: permanece com `SchoolClassId` nulo; nenhuma turma sem nome é criada.
- Alunos com a mesma turma em anos distintos: cada par recebe uma turma própria, porque a chave é `(TenantId, AnoLetivo, Turma)`.
- O backfill roda uma única vez por migração; executar `dotnet ef database update` novamente não duplica linhas porque a migração já está registrada em histórico.

### FR-004: CRUD administrativo da estrutura acadêmica

**What:** Cria `src/Ciclo.Infrastructure/Contracts/AcademicDtos.cs` com `AcademicYearDto`, `SchoolClassDto`, `CurriculumComponentDto`, `AssessmentDto` e os respectivos `Create*Request` e `Update*Request`; cria `src/Ciclo.Infrastructure/Services/AcademicService.cs` declarando `IAcademicService` e `AcademicService` com um método por operação (`ListYearsAsync`, `CreateYearAsync`, `UpdateYearAsync`, `DeleteYearAsync`, `ListClassesAsync`, `CreateClassAsync`, `UpdateClassAsync`, `DeleteClassAsync`, `ListComponentsAsync`, `CreateComponentAsync`, `UpdateComponentAsync`, `DeleteComponentAsync`, `ListAssessmentsAsync`, `CreateAssessmentAsync`, `UpdateAssessmentAsync`, `DeleteAssessmentAsync`); cria `src/Ciclo.Api/Controllers/AcademicController.cs` com `[Route("api/academic")]` expondo `GET|POST api/academic/years`, `PUT|DELETE api/academic/years/{id:guid}` e as mesmas quatro rotas para `classes`, `components` e `assessments`; registra `IAcademicService` em `src/Ciclo.Api/Program.cs`. Cada action usa uma constante de `AccessPolicy.Roles` dedicada ao grupo acadêmico.
**Why:** V15 pede CRUD administrativo. Sem endpoints não existe caminho para o administrador criar ano, turma, componente e avaliação.
**Acceptance Criteria:**
- [ ] O controlador declara a rota base. [verify: grep-present `api/academic` `src/Ciclo.Api/Controllers/AcademicController.cs`]
- [ ] O serviço declara a interface consumida pelo controlador. [verify: grep-present `IAcademicService` `src/Ciclo.Infrastructure/Services/AcademicService.cs`]
- [ ] O serviço está registrado no contêiner de dependências. [verify: grep-present `IAcademicService` `src/Ciclo.Infrastructure/Services/AcademicService.cs`]
- [ ] Os contratos de entrada e saída estão declarados. [verify: file-exists `src/Ciclo.Infrastructure/Contracts/AcademicDtos.cs`]
- [ ] As quatro operações de ano letivo respondem com os códigos previstos. [verify: project-validation]
- [ ] As quatro operações de turma, componente e avaliação respondem com os códigos previstos. [verify: project-validation]
**Failure modes:**
- Identificador inexistente no tenant: `404` com código `not_found`.
- Corpo inválido: `400` com o código específico da violação listado nas FR-001 e FR-002.
- `DELETE` de ano com turmas, de turma com alunos ou avaliações, de componente com avaliações, ou de avaliação com notas: `409` com `academic_year_in_use`, `school_class_in_use`, `component_in_use` e `assessment_has_grades`, respectivamente.
- Usuário sem a role administrativa: `403`, sem alteração de estado.

### FR-005: Lançamento de notas

**What:** Adiciona `Task<GradeDto> UpsertGradeAsync(Guid assessmentId, Guid studentId, decimal value, CancellationToken cancellationToken)` a `IAcademicService` e `AcademicService`, e a rota `PUT api/academic/assessments/{assessmentId:guid}/grades/{studentId:guid}` em `AcademicController` recebendo `{ "value": decimal }`. A operação é idempotente: se já existir `Grade` para o par avaliação/aluno, atualiza `Value` e `RecordedAt`; caso contrário cria. Adiciona também a rota `GET api/academic/classes/{id:guid}/students` que devolve `List<StudentDto>` matriculados na turma, para a tela de lançamento listar o roster.
**Why:** V15 pede lançamento de notas. Sem operação idempotente o lançamento manual duplica linhas e corrompe a média.
**Acceptance Criteria:**
- [ ] O serviço expõe a operação de gravação de nota. [verify: function-exists `UpsertGradeAsync` `src/Ciclo.Infrastructure/Services/AcademicService.cs`]
- [ ] O controlador expõe a rota de lançamento. [verify: grep-present `grades/{studentId:guid}` `src/Ciclo.Api/Controllers/AcademicController.cs`]
- [ ] O controlador expõe a rota de roster da turma. [verify: grep-present `classes/{id:guid}/students` `src/Ciclo.Api/Controllers/AcademicController.cs`]
- [ ] O segundo lançamento do mesmo aluno e avaliação altera a linha existente em vez de criar outra. [verify: project-validation]
- [ ] O lançamento para aluno de outra turma é rejeitado. [verify: project-validation]
**Failure modes:**
- `value` fora do intervalo 0 a 10: `400` com `grade_out_of_range`, sem escrita.
- Aluno não vinculado à turma da avaliação: `409` com `student_not_in_class`, sem escrita.
- Avaliação ou aluno inexistente no tenant: `404` com `not_found`.
- Avaliação pertencente a outro tenant: `404`, sem escrita.
- Escrita concorrente para o mesmo par avaliação/aluno: o índice único `(AssessmentId, StudentId)` garante uma única linha; a operação perdedora relê e atualiza a linha vencedora.

### FR-006: Cálculo do boletim com média ponderada e recuperação

**What:** Cria `src/Ciclo.Infrastructure/Services/GradeCalculation.cs` com `public static class GradeCalculation` contendo `ApprovalThreshold = 6.0m`, `RecoveryThreshold = 4.0m`, `ComputeRegularAverage(IEnumerable<AssessmentScore> scores)` e `ComputeFinalAverage(decimal? regularAverage, decimal? recoveryGrade)`, além de `ResolveSituation(decimal? finalAverage)`. Regras registradas: `MediaRegular = soma(Value * Weight) / soma(Weight)` sobre as avaliações da turma e do componente com `IsRecovery` falso e nota lançada; `NotaRecuperacao` é o `Value` da avaliação de recuperação do mesmo par turma/componente; `MediaFinal = MediaRegular` quando não existe nota de recuperação, `max(MediaRegular, NotaRecuperacao)` quando ambas existem, e `NotaRecuperacao` quando só a recuperação existe; todo arredondamento usa duas casas com `MidpointRounding.AwayFromZero`; `Situacao` é `aprovado` quando `MediaFinal` é maior ou igual a 6, `recuperacao` quando é maior ou igual a 4 e menor que 6, `reprovado` quando é menor que 4 e `sem_nota` quando `MediaFinal` é nulo.
**Why:** V15 pede explicitamente a decisão do modelo de cálculo registrada. A fórmula e os limiares fixos retiram qualquer escolha do implementador e tornam o boletim auditável.
**Acceptance Criteria:**
- [ ] A média ponderada é calculada em uma função nomeada. [verify: function-exists `ComputeRegularAverage` `src/Ciclo.Infrastructure/Services/GradeCalculation.cs`]
- [ ] A média final considera a recuperação. [verify: function-exists `ComputeFinalAverage` `src/Ciclo.Infrastructure/Services/GradeCalculation.cs`]
- [ ] A situação é derivada da média final. [verify: function-exists `ResolveSituation` `src/Ciclo.Infrastructure/Services/GradeCalculation.cs`]
- [ ] O limiar de aprovação é uma constante declarada. [verify: grep-present `ApprovalThreshold` `src/Ciclo.Infrastructure/Services/GradeCalculation.cs`]
- [ ] O arredondamento usa o modo declarado. [verify: grep-present `MidpointRounding.AwayFromZero` `src/Ciclo.Infrastructure/Services/GradeCalculation.cs`]
- [ ] Os casos de média ponderada, recuperação, ausência de nota e reprovação estão cobertos pelos testes do escopo declarado. [verify: project-validation]
**Failure modes:**
- Nenhuma avaliação com nota lançada: `MediaRegular` nulo, `MediaFinal` nulo, `Situacao` igual a `sem_nota`.
- Soma de pesos zero por dados legados: a função devolve `null` em vez de dividir por zero.
- Nota de recuperação menor que a média regular: `MediaFinal` mantém a média regular, porque a regra é `max`.
- Nota de recuperação maior ou igual à média regular: `MediaFinal` assume a nota de recuperação.

### FR-007: Boletim no portal dos pais e substituição da lista vazia

**What:** `src/Ciclo.Infrastructure/Services/ParentService.cs` passa a implementar `GetChildGradesAsync` devolvendo uma linha `GradeDto` por `CurriculumComponent` que tenha ao menos uma `Assessment` na turma do aluno, com `Disciplina` igual ao nome do componente e `Nota` igual à `MediaFinal`; `GetChildDetailAsync` deixa de passar a lista vazia e usa o mesmo cálculo. Adiciona `Task<ReportCardDto> GetChildReportCardAsync(Guid parentId, Guid studentId, Guid tenantId, CancellationToken cancellationToken)` a `IParentService`, os registros `ReportCardDto(Guid AlunoId, string? Turma, int? AnoLetivo, IReadOnlyList<ReportCardEntryDto> Componentes)`, `ReportCardEntryDto(string Componente, decimal? MediaRegular, decimal? NotaRecuperacao, decimal? MediaFinal, string Situacao, IReadOnlyList<ReportCardScoreDto> Avaliacoes)` e `ReportCardScoreDto(string Avaliacao, decimal Peso, decimal? Nota, DateOnly Data, bool Recuperacao)` em `src/Ciclo.Infrastructure/Contracts/ParentDtos.cs`, e a rota `GET api/parent/children/{id:guid}/report-card` em `src/Ciclo.Api/Controllers/ParentController.cs`, reaproveitando a verificação de vínculo responsável-aluno já existente antes de qualquer leitura de nota.
**Why:** V15 diz explicitamente que o endpoint de notas que hoje devolve lista vazia é substituído e que o responsável passa a ver o boletim. O vínculo pai-filho é a barreira de autorização já validada neste serviço.
**Acceptance Criteria:**
- [ ] O serviço devolve notas reais. [verify: function-exists `GetChildGradesAsync` `src/Ciclo.Infrastructure/Services/ParentService.cs`]
- [ ] O serviço expõe o boletim. [verify: function-exists `GetChildReportCardAsync` `src/Ciclo.Infrastructure/Services/ParentService.cs`]
- [ ] O controlador expõe a rota do boletim. [verify: grep-present `report-card` `src/Ciclo.Api/Controllers/ParentController.cs`]
- [ ] O detalhe do filho não entrega mais a lista vazia de notas. [verify: project-validation]
- [ ] O responsável sem vínculo com o aluno recebe a resposta de não autorizado já praticada pelo serviço atual. [verify: project-validation]
- [ ] As notas de aluno de outro tenant não são retornadas. [verify: project-validation]
**Failure modes:**
- Aluno sem turma vinculada: `GetChildGradesAsync` devolve lista vazia e o boletim devolve `Componentes` vazio com `Turma` e `AnoLetivo` nulos.
- Componente com avaliações e nenhuma nota do aluno: a linha aparece com `Nota` nula e `Situacao` igual a `sem_nota`.
- Responsável sem vínculo com o aluno: a exceção de vínculo já existente é lançada antes da consulta de notas.
- Aluno de outro tenant: `404`, sem leitura.

### FR-008: Telas administrativas e boletim no frontend

**What:** Cria `frontend/src/api/academic.ts` com as funções de chamada das rotas de `api/academic` (anos, turmas, componentes, avaliações, roster e lançamento de nota), cria `frontend/src/pages/admin/AcademicStructure.tsx` com o cadastro de ano letivo, turma, componente e avaliação, cria `frontend/src/pages/admin/GradeEntry.tsx` com a seleção de turma e avaliação, a lista de alunos e o campo de nota por aluno, adiciona as rotas `/admin/academico` e `/admin/notas` em `frontend/src/App.tsx`, adiciona `getChildReportCard` em `frontend/src/api/parent.ts`, os tipos de estrutura acadêmica e de boletim em `frontend/src/types/index.ts`, e reescreve a aba de notas de `frontend/src/pages/parent/ChildDetail.tsx` para renderizar componente, média regular, nota de recuperação, média final e rótulo da situação, mapeando `aprovado` para `Aprovado`, `recuperacao` para `Em recuperação`, `reprovado` para `Reprovado` e `sem_nota` para `Sem nota`.
**Why:** V15 declara as camadas frontend administrativo e portal dos pais como parte da entrega; sem tela o administrador não cadastra estrutura nem lança nota e o responsável não vê o boletim.
**Acceptance Criteria:**
- [ ] O cliente de API da estrutura acadêmica existe. [verify: file-exists `frontend/src/api/academic.ts`]
- [ ] A tela de cadastro da estrutura acadêmica existe. [verify: file-exists `frontend/src/pages/admin/AcademicStructure.tsx`]
- [ ] A tela de lançamento de notas existe. [verify: file-exists `frontend/src/pages/admin/GradeEntry.tsx`]
- [ ] As rotas administrativas estão declaradas. [verify: grep-present `/admin/academico` `frontend/src/App.tsx`]
- [ ] O cliente do portal dos pais busca o boletim. [verify: grep-present `getChildReportCard` `frontend/src/api/parent.ts`]
- [ ] A aba de notas renderiza a situação do boletim. [verify: grep-present `situacao` `frontend/src/pages/parent/ChildDetail.tsx`]
- [ ] O build do frontend conclui sem erro de tipo. [verify: project-validation]
**Failure modes:**
- Lista vazia de anos, turmas, componentes ou avaliações: a tela mostra estado vazio explícito em vez de erro.
- Erro de API com código conhecido: a mensagem é resolvida pelo mapa de `frontend/src/api/errors.ts`; código desconhecido cai na mensagem genérica já existente.
- Boletim sem componentes: a aba mostra estado vazio explícito.
- Nota fora de 0 a 10 digitada na tela: a validação do formulário bloqueia o envio antes da chamada.

## Non-Functional Requirements

### NFR-001: Isolamento por tenant em toda leitura e escrita acadêmica

**What:** Todas as entidades novas implementam `ITenantScoped`, entram no laço de filtro global de `AppDbContext.OnModelCreating` e recebem `TenantId` por `AutoSetTenantId()` na gravação, exatamente como as entidades existentes. Nenhuma consulta nova usa `IgnoreQueryFilters`.
**Acceptance Criteria:**
- [ ] As entidades novas implementam o contrato de tenant. [verify: grep-present `ITenantScoped` `src/Ciclo.Core/Entities/Grade.cs`]
- [ ] O filtro global por tenant é aplicado pelo laço de mapeamento já existente no contexto. [verify: grep-present `OnModelCreating` `src/Ciclo.Infrastructure/Data/AppDbContext.cs`]
- [ ] A escrita administrativa não ignora o filtro de tenant. [verify: grep-absent `IgnoreQueryFilters` `src/Ciclo.Infrastructure/Services/AcademicService.cs`]
- [ ] Um identificador de outro tenant produz resposta de não encontrado nos testes do escopo declarado. [verify: project-validation]

### NFR-002: Determinismo numérico e faixas de valor

**What:** `Grade.Value` e `Assessment.Weight` são `decimal` com precisão fixa `decimal(5,2)`, validados na entrada contra as faixas 0 a 10 e maior que zero, e toda média é arredondada para duas casas com `MidpointRounding.AwayFromZero` antes de ser serializada. Nenhum cálculo usa `double` nem `float`.
**Acceptance Criteria:**
- [ ] A validação de faixa da nota é aplicada no serviço. [verify: grep-present `grade_out_of_range` `src/Ciclo.Infrastructure/Services/AcademicService.cs`]
- [ ] A validação de faixa do peso é aplicada no serviço. [verify: grep-present `assessment_weight_invalid` `src/Ciclo.Infrastructure/Services/AcademicService.cs`]
- [ ] Nenhum cálculo do boletim usa ponto flutuante binário. [verify: grep-absent `double` `src/Ciclo.Infrastructure/Services/GradeCalculation.cs`]
- [ ] A mesma entrada produz o mesmo `MediaFinal` em execuções repetidas nos testes do escopo declarado. [verify: project-validation]

## Constraints

- `Student.Turma` e `Student.AnoLetivo` permanecem na entidade e nas colunas do banco; a remoção do espelho fica para a spec 160 em diante. [verify: grep-present `public int AnoLetivo` `src/Ciclo.Core/Entities/Student.cs`]
- `IParentService.GetChildGradesAsync` mantém a assinatura `(Guid parentId, Guid studentId, Guid tenantId)`. [verify: grep-present `GetChildGradesAsync` `src/Ciclo.Infrastructure/Services/ParentService.cs`]
- A rota existente `children/{id:guid}/grades` do portal dos pais não muda de caminho. [verify: grep-present `grades` `src/Ciclo.Api/Controllers/ParentController.cs`]
- `GradeDto` mantém os três campos `Disciplina`, `Nota` e `Observacoes`. [verify: grep-present `record GradeDto` `src/Ciclo.Infrastructure/Contracts/ParentDtos.cs`]
- `CreateStudentRequest` e `UpdateStudentRequest` não ganham campos de estrutura acadêmica nesta spec. [verify: grep-absent `SchoolClassId` `src/Ciclo.Infrastructure/Contracts/StudentDtos.cs`]
- A migração não remove colunas nem tabelas existentes. [verify: project-validation]
- Full project validation passes [verify: project-validation]

## Edge Cases & Error States

- Aluno legado com `AnoLetivo` menor ou igual a zero ou `Turma` vazia: o backfill deixa os dois identificadores nulos e o aluno continua listável pela coluna espelho.
- Aluno sem turma consultando o boletim: resposta com `componentes` vazio, porque não há componente a avaliar.
- Avaliação de recuperação lançada antes de qualquer nota regular: `MediaRegular` nula, `MediaFinal` igual à nota de recuperação, `Situacao` derivada dessa média.
- Duas avaliações com o mesmo título na mesma turma e componente: rejeitadas com `assessment_title_already_exists`, para que o boletim não exiba linhas homônimas.
- Exclusão de turma com alunos vinculados: bloqueada com `school_class_in_use`; o administrador precisa mover os alunos antes.
- Exclusão de ano letivo com turmas: bloqueada com `academic_year_in_use`.
- Requisição de nota para avaliação que pertence a outro tenant: `404`, sem escrita e sem vazamento de existência.
- Falha de validação no meio de uma escrita: nenhuma linha parcial é persistida, porque cada operação de escrita grava uma única entidade e salva uma vez.
- Erro de banco na gravação: a exceção sobe para o middleware existente; nenhuma exceção é engolida.
- Boletim com componente cujas avaliações existem mas nenhuma nota foi lançada: linha presente com `mediaFinal` nula e `situacao` igual a `sem_nota`.

## Anti-Scope

- Nenhum segundo serviço de aluno é criado; a resolução de turma reaproveita `StudentService`. [verify: file-absent `src/Ciclo.Infrastructure/Services/StudentAcademicService.cs`]
- Nenhuma biblioteca externa de estatística ou média é adicionada ao projeto. [verify: grep-absent `MathNet` `src/Ciclo.Infrastructure/Services/`]
- Nenhum cálculo do boletim no frontend; o frontend apenas exibe os valores devolvidos pela API. [verify: grep-absent `ComputeFinalAverage` `frontend/src/`]
- Nenhuma remoção de coluna acontece no SQL de backfill. [verify: grep-absent `DropColumn` `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs`]
- Nenhuma migração de dados roda fora da migração EF Core; não existe script SQL solto no repositório. [verify: file-absent `scripts/academic-backfill.sql`]

## Dependencies

- Spec 40 (student-management): entidade `Student`, `StudentService` e `StudentDtos` consumidos por esta spec.
- Spec 50 (enrollment): `Student.AnoLetivo` e `Student.Turma` são escritos no fluxo de aprovação de matrícula; as colunas espelho permanecem para não quebrar esse fluxo.
- `AppDbContext`, `ITenantScoped`, `AutoSetTenantId` e `AccessPolicy.Roles` são pré-requisitos já existentes no repositório.
- Nenhuma dependência externa nova: o cálculo usa `decimal` e o mapeamento usa o provider Npgsql já configurado.

## Out of Scope

- Promoção de ano letivo e rematrícula em lote: spec 160.
- Portal do professor e role docente própria: esta spec usa as roles administrativas existentes.
- Exportação do boletim em PDF.
- Frequência, faltas e calendário escolar.
- Nota por período ou bimestre com fechamento formal; a média é calculada sobre o conjunto de avaliações da turma e do componente.
- Tradução dos rótulos de situação: chaves estáveis ficam na API e os rótulos em pt-BR ficam no frontend, conforme a spec 190.
- Relatórios agregados de desempenho por escola.

## Test Scope

- `tests/Ciclo.Api.Tests/AcademicStructureTests.cs` — CRUD das quatro entidades, isolamento por tenant, autorização administrativa, rejeição de exclusão em uso, lançamento idempotente de nota e rejeição de aluno fora da turma.
- `tests/Ciclo.Api.Tests/GradeCalculationTests.cs` — média ponderada, ausência de nota, recuperação menor e maior que a média, limiares de aprovação, recuperação e reprovação, e substituição da lista vazia de `GetChildGradesAsync`.
- `tests/Ciclo.Api.Tests/AcademicMigrationTests.cs` — conteúdo do backfill: anos distintos, turmas distintas por ano, junção de atualização dos alunos e ausência de remoção de colunas.
