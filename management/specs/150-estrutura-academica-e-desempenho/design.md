# Spec 150: estrutura-academica-e-desempenho — Design

## Design Approach

Cinco entidades explícitas (`AcademicYear`, `SchoolClass`, `CurriculumComponent`, `Assessment`, `Grade`) formam o modelo acadêmico e são as autoridades de leitura a partir desta spec. O vínculo do aluno passa a ser por chave estrangeira, mantendo as colunas legadas `Turma` e `AnoLetivo` como espelho de compatibilidade escrito exclusivamente pelo resolvedor do caminho de escrita — nenhum código novo as lê como autoridade, e nenhum contrato público de aluno muda.

A gravação de estrutura e de notas fica em um único serviço novo (`AcademicService`), porque o domínio não tem dono existente. As leituras escopadas pelo responsável continuam em `ParentService`, que já é o dono da verificação de vínculo responsável-aluno.

O cálculo do boletim é uma classe estática pura (`GradeCalculation`) sem `DbContext`, para que a fórmula e os limiares sejam provados sem banco. O backfill de dados vive em um símbolo estável (`AcademicStructureBackfill.Sql`), aplicado por `migrationBuilder.Sql`, para que a migração seja verificável sem um PostgreSQL em execução.

Princípios aplicados: KISS (uma entidade por arquivo, um serviço novo, um controlador novo), DRY (uma única fórmula de média, um único resolvedor de vínculo), menor mudança sistêmica (referência nova como autoridade, espelho legado preservado) e nenhum caminho de escrita duplicado.

## Architecture Decisions

### ADR-150-1: Modelo de cálculo registrado

Decisão exigida por V15. `MediaRegular = soma(Value * Weight) / soma(Weight)` sobre as avaliações da turma e do componente com `IsRecovery` falso que tenham nota do aluno. `MediaFinal = MediaRegular` quando não há nota de recuperação; `max(MediaRegular, NotaRecuperacao)` quando ambas existem; `NotaRecuperacao` quando só a recuperação existe. Arredondamento em duas casas com `MidpointRounding.AwayFromZero`. Limiares fixos: `ApprovalThreshold = 6.0m` e `RecoveryThreshold = 4.0m`. Situação: `aprovado` para média maior ou igual a 6, `recuperacao` para média maior ou igual a 4 e menor que 6, `reprovado` para média menor que 4 e `sem_nota` quando a média final é nula.

Consequência: nenhuma escolha de fórmula sobra para o implementador, e a recuperação nunca reduz a média do aluno.

### ADR-150-2: Referência como autoridade, coluna legada como espelho

`Student.Turma` e `Student.AnoLetivo` continuam existindo como colunas e propriedades porque são lidas por `StudentDto`, pela listagem com filtro e pelos testes existentes. O caminho de escrita de aluno resolve a estrutura e grava as duas representações na mesma operação: `SchoolClassId` e `AcademicYearId` como referência, `Turma` e `AnoLetivo` como espelho do valor canônico da entidade resolvida. Código novo lê apenas a referência.

Consequência: zero quebra de contrato público; uma única fonte de verdade para código novo; a remoção do espelho fica para a spec 160.

### ADR-150-3: SQL de backfill em símbolo estável

A migração tem nome com carimbo de tempo, logo não é citável por caminho determinístico nem executável em teste sem PostgreSQL. O SQL do backfill fica em `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs` como `public const string Sql`, e a migração gerada o aplica com `migrationBuilder.Sql(AcademicStructureBackfill.Sql)`.

Consequência: o backfill é verificado por asserção sobre o texto aplicado, sem infraestrutura de banco adicional.

### ADR-150-4: Nota identificada pelo par avaliação/aluno

O índice único `(AssessmentId, StudentId)` transforma o lançamento em upsert determinístico: a segunda escrita atualiza a linha existente e `RecordedAt`. Não existe tabela de histórico de nota nesta spec.

### ADR-150-5: Dois modelos de leitura de nota

`GradeDto` permanece intacto porque é o contrato do endpoint atual e do portal. O boletim completo usa `ReportCardDto`, que expõe média regular, nota de recuperação, média final, situação e as notas por avaliação. Um modelo não substitui o outro: `GradeDto` é resumo por componente e `ReportCardDto` é detalhe auditável.

### ADR-150-6: Roles dedicadas no AccessPolicy existente

`src/Ciclo.Infrastructure/Security/AccessPolicy.cs` ganha três constantes no mesmo formato das existentes: `AcademicStructureRead = "Admin,Staff"`, `AcademicStructureManage = "Admin"` e `GradeManage = "Admin,Staff"`. Nenhuma role nova é criada; o formato CSV já é o praticado.

## Component / Module Breakdown

### Component: AcademicYear (entidade)

**Purpose:** Representa o ano letivo de um tenant e é o pai das turmas.
**Interface:** `public class AcademicYear : ITenantScoped` com `Guid Id`, `Guid TenantId`, `int Year`, `DateOnly StartDate`, `DateOnly EndDate`, `bool IsActive`, `ICollection<SchoolClass> Classes`.
**Internal Logic:** apenas estado; unicidade e faixa de datas são garantidas pelo `AppDbContext` e pelo serviço.
**Dependencies:** `ITenantScoped`.
**Error Handling:** não lança; violação de unicidade chega como `DbUpdateException` e é traduzida pelo serviço.

### Component: SchoolClass (entidade)

**Purpose:** Representa a turma/série como entidade de primeira classe, substituindo a string livre como autoridade.
**Interface:** `public class SchoolClass : ITenantScoped` com `Guid Id`, `Guid TenantId`, `string Name`, `Guid AcademicYearId`, `AcademicYear AcademicYear`, `ICollection<Student> Students`.
**Internal Logic:** apenas estado; `Name` é obrigatório e limitado a 50 caracteres pelo mapeamento.
**Dependencies:** `AcademicYear`, `Student`.
**Error Handling:** não lança; exclusão com alunos dependentes é bloqueada por `OnDelete(DeleteBehavior.Restrict)` e traduzida em `school_class_in_use`.

### Component: CurriculumComponent (entidade)

**Purpose:** Representa o componente curricular do tenant.
**Interface:** `public class CurriculumComponent : ITenantScoped` com `Guid Id`, `Guid TenantId`, `string Name`, `string Code`, `bool IsActive`.
**Internal Logic:** apenas estado; `Code` único por tenant.
**Dependencies:** `ITenantScoped`.
**Error Handling:** não lança; unicidade de `Code` é traduzida em `component_code_already_exists`.

### Component: Assessment (entidade)

**Purpose:** Representa uma avaliação de uma turma em um componente, com o peso que entra na média.
**Interface:** `public class Assessment : ITenantScoped` com `Guid Id`, `Guid TenantId`, `Guid SchoolClassId`, `SchoolClass SchoolClass`, `Guid CurriculumComponentId`, `CurriculumComponent CurriculumComponent`, `string Title`, `decimal Weight`, `DateOnly Date`, `bool IsRecovery`.
**Internal Logic:** apenas estado; `Weight` mapeado como `decimal(5,2)` com padrão `1`.
**Dependencies:** `SchoolClass`, `CurriculumComponent`, `Grade`.
**Error Handling:** não lança; exclusão com notas é bloqueada por `Restrict` e traduzida em `assessment_has_grades`.

### Component: Grade (entidade)

**Purpose:** Representa a nota de um aluno em uma avaliação.
**Interface:** `public class Grade : ITenantScoped` com `Guid Id`, `Guid TenantId`, `Guid AssessmentId`, `Assessment Assessment`, `Guid StudentId`, `Student Student`, `decimal Value`, `DateTime RecordedAt`.
**Internal Logic:** apenas estado; `Value` mapeado como `decimal(5,2)`; índice único `(AssessmentId, StudentId)`.
**Dependencies:** `Assessment`, `Student`.
**Error Handling:** não lança; a violação do índice único é o mecanismo que garante o upsert e é tratada no serviço.

### Component: Student (entidade alterada)

**Purpose:** Ganha o vínculo por referência com ano letivo e turma.
**Interface:** acrescenta `Guid? AcademicYearId`, `AcademicYear? AcademicYear`, `Guid? SchoolClassId`, `SchoolClass? SchoolClass`. Mantém `string Turma` e `int AnoLetivo`.
**Internal Logic:** apenas estado.
**Dependencies:** `AcademicYear`, `SchoolClass`.
**Error Handling:** não lança; as chaves estrangeiras nulas são aceitas para alunos legados sem valor válido.

### Component: AppDbContext (alterado)

**Purpose:** Mapear as cinco entidades novas e o vínculo do aluno.
**Interface:** acrescenta `DbSet<AcademicYear>`, `DbSet<SchoolClass>`, `DbSet<CurriculumComponent>`, `DbSet<Assessment>` e `DbSet<Grade>`; em `OnModelCreating` acrescenta os índices únicos `(TenantId, Year)`, `(TenantId, AcademicYearId, Name)`, `(TenantId, Code)`, `(TenantId, SchoolClassId, CurriculumComponentId, Title)` e `(AssessmentId, StudentId)`, os `HasMaxLength`, os `HasPrecision` e as chaves estrangeiras com `DeleteBehavior.Restrict`.
**Internal Logic:** o laço já existente de filtro global por tenant passa a cobrir as entidades novas porque elas implementam `ITenantScoped`; `AutoSetTenantId()` preenche `TenantId` na gravação.
**Dependencies:** `ITenantScoped`.
**Error Handling:** nenhum `catch` novo; exceções de banco sobem ao middleware existente.

### Component: Resolvedor de vínculo acadêmico em StudentService

**Purpose:** Garantir que a turma e o ano informados como texto no contrato atual sejam persistidos como referência, sem mudar o contrato de aluno.
**Interface:** `private async Task ResolveAcademicStructureAsync(Student student, CancellationToken cancellationToken)` em `src/Ciclo.Infrastructure/Services/StudentService.cs`, chamado em `CreateAsync` e em `UpdateAsync` antes do `SaveChangesAsync`.
**Internal Logic:** (1) normaliza `student.Turma` com `Trim()` e lê `student.AnoLetivo`; (2) se `AnoLetivo` for menor ou igual a zero ou a turma normalizada for vazia, deixa `AcademicYearId` e `SchoolClassId` nulos e retorna; (3) localiza `AcademicYear` por `(TenantId, Year == AnoLetivo)` ou cria um com `StartDate` em 1 de janeiro do ano e `EndDate` em 31 de dezembro do ano; (4) localiza `SchoolClass` por `(TenantId, AcademicYearId, Name == turma normalizada)` ou cria um; (5) grava `student.AcademicYearId` e `student.SchoolClassId` e reescreve o espelho `student.Turma` e `student.AnoLetivo` com os valores canônicos.
**Dependencies:** `AppDbContext`, entidades `AcademicYear` e `SchoolClass`.
**Error Handling:** erro de banco sobe; nenhuma exceção é engolida; como o resolvedor roda antes do `SaveChangesAsync` da operação, falha nele não deixa linha parcial de aluno.

### Component: AcademicService

**Purpose:** Dono único da escrita e da leitura administrativa da estrutura acadêmica e do lançamento de notas.
**Interface:** `public interface IAcademicService` em `src/Ciclo.Infrastructure/Services/AcademicService.cs`:

```csharp
Task<List<AcademicYearDto>> ListYearsAsync(CancellationToken cancellationToken = default);
Task<AcademicYearDto> CreateYearAsync(CreateAcademicYearRequest request, CancellationToken cancellationToken = default);
Task<AcademicYearDto> UpdateYearAsync(Guid id, UpdateAcademicYearRequest request, CancellationToken cancellationToken = default);
Task DeleteYearAsync(Guid id, CancellationToken cancellationToken = default);
Task<List<SchoolClassDto>> ListClassesAsync(Guid? academicYearId, CancellationToken cancellationToken = default);
Task<SchoolClassDto> CreateClassAsync(CreateSchoolClassRequest request, CancellationToken cancellationToken = default);
Task<SchoolClassDto> UpdateClassAsync(Guid id, UpdateSchoolClassRequest request, CancellationToken cancellationToken = default);
Task DeleteClassAsync(Guid id, CancellationToken cancellationToken = default);
Task<List<CurriculumComponentDto>> ListComponentsAsync(CancellationToken cancellationToken = default);
Task<CurriculumComponentDto> CreateComponentAsync(CreateCurriculumComponentRequest request, CancellationToken cancellationToken = default);
Task<CurriculumComponentDto> UpdateComponentAsync(Guid id, UpdateCurriculumComponentRequest request, CancellationToken cancellationToken = default);
Task DeleteComponentAsync(Guid id, CancellationToken cancellationToken = default);
Task<List<AssessmentDto>> ListAssessmentsAsync(Guid? schoolClassId, Guid? curriculumComponentId, CancellationToken cancellationToken = default);
Task<AssessmentDto> CreateAssessmentAsync(CreateAssessmentRequest request, CancellationToken cancellationToken = default);
Task<AssessmentDto> UpdateAssessmentAsync(Guid id, UpdateAssessmentRequest request, CancellationToken cancellationToken = default);
Task DeleteAssessmentAsync(Guid id, CancellationToken cancellationToken = default);
Task<List<StudentDto>> ListClassStudentsAsync(Guid classId, CancellationToken cancellationToken = default);
Task<GradeDto> UpsertGradeAsync(Guid assessmentId, Guid studentId, decimal value, CancellationToken cancellationToken = default);
```

`public class AcademicService : IAcademicService` com construtor `AcademicService(AppDbContext db)`.

**Internal Logic:** cada operação valida o pedido, resolve a entidade no tenant pelo filtro global, aplica a regra e chama `SaveChangesAsync` uma única vez. `UpsertGradeAsync` valida `value` na faixa 0 a 10, carrega a avaliação, valida que o aluno pertence à turma da avaliação, localiza a nota por `(AssessmentId, StudentId)`, cria ou atualiza `Value` e `RecordedAt` com `DateTime.UtcNow`, salva e devolve `GradeDto` com `Disciplina` igual ao nome do componente, `Nota` igual ao valor e `Observacoes` nulo. `ListClassStudentsAsync` devolve o roster da turma como `StudentDto`.
**Dependencies:** `AppDbContext`, `AcademicDtos`, entidades acadêmicas e o tipo de exceção de domínio já existente em `src/Ciclo.Core/Exceptions/`, o mesmo usado por `StudentService` para `turma_required`.
**Error Handling:** entidade ausente lança a exceção de domínio com `not_found`, que o middleware existente responde como 404; entrada inválida lança com o código específico e resposta 400; conflito de dependência ou de unicidade lança com o código específico e resposta 409. `DbUpdateException` de índice único é capturada e convertida no código correspondente; qualquer outra exceção sobe sem tradução.

### Component: GradeCalculation

**Purpose:** Isolar a fórmula do boletim, sem dependência de banco.
**Interface:** em `src/Ciclo.Infrastructure/Services/GradeCalculation.cs`:

```csharp
public readonly record struct AssessmentScore(decimal Value, decimal Weight, bool IsRecovery);

public static class GradeCalculation
{
    public const decimal ApprovalThreshold = 6.0m;
    public const decimal RecoveryThreshold = 4.0m;

    public static decimal? ComputeRegularAverage(IEnumerable<AssessmentScore> scores);
    public static decimal? ComputeFinalAverage(decimal? regularAverage, decimal? recoveryGrade);
    public static string ResolveSituation(decimal? finalAverage);
    public static decimal? Round(decimal? value);
}
```

**Internal Logic:** `ComputeRegularAverage` ignora entradas com `IsRecovery` verdadeiro, soma `Value * Weight` e o total de pesos, e devolve `null` quando o total de pesos é zero; caso contrário devolve `Round(soma / pesos)`. `ComputeFinalAverage` devolve `regularAverage` quando `recoveryGrade` é nulo, devolve `recoveryGrade` quando `regularAverage` é nulo e devolve `Round(max(regularAverage, recoveryGrade))` quando ambos existem. `ResolveSituation` devolve `sem_nota` para nulo, `aprovado` para média maior ou igual a `ApprovalThreshold`, `recuperacao` para média maior ou igual a `RecoveryThreshold` e `reprovado` abaixo dela. `Round` aplica `decimal.Round(valor, 2, MidpointRounding.AwayFromZero)` e propaga nulo.
**Dependencies:** nenhuma além de `System`.
**Error Handling:** todas as funções são totais: entrada vazia produz média nula, divisão por zero é evitada pela checagem do total de pesos e nulo propaga como nulo; nenhuma exceção é lançada.

### Component: ParentService (alterado)

**Purpose:** Servir notas e boletim ao responsável, mantendo a autorização existente como única porta.
**Interface:** `GetChildGradesAsync(Guid parentId, Guid studentId, Guid tenantId)` passa a devolver dados reais; acrescenta `Task<ReportCardDto> GetChildReportCardAsync(Guid parentId, Guid studentId, Guid tenantId, CancellationToken cancellationToken = default)` a `IParentService` e `ParentService`.
**Internal Logic:** ambas chamam `VerifyParentChildLinkAsync` primeiro. Em seguida carregam o aluno, sua turma, os componentes que têm avaliação na turma e as notas do aluno. `GetChildGradesAsync` devolve uma linha `GradeDto(Disciplina = nome do componente, Nota = MediaFinal, Observacoes = null)` por componente com pelo menos uma avaliação, ordenada por nome do componente. `GetChildReportCardAsync` monta uma entrada por componente com `MediaRegular`, `NotaRecuperacao`, `MediaFinal`, `Situacao` e a lista de `ReportCardScoreDto` por avaliação, mais `Turma` e `AnoLetivo` do aluno. `GetChildDetailAsync` passa a delegar a `GetChildGradesAsync` em vez de usar lista vazia.
**Dependencies:** `AppDbContext`, `GradeCalculation`, `ParentDtos`.
**Error Handling:** vínculo inexistente mantém a exceção já praticada; aluno sem turma devolve lista vazia e boletim com `Componentes` vazio; nenhuma exceção nova é introduzida.

### Component: AcademicController

**Purpose:** Expor a estrutura acadêmica e o lançamento de notas.
**Interface:** `[ApiController]`, `[Route("api/academic")]`, herda `ControllerBase`, construtor `AcademicController(IAcademicService academicService)`. Rotas:

| Método | Rota | Autorização | Corpo | Resposta |
|---|---|---|---|---|
| GET | `api/academic/years` | `AccessPolicy.Roles.AcademicStructureRead` | nenhum | `List<AcademicYearDto>` |
| POST | `api/academic/years` | `AccessPolicy.Roles.AcademicStructureManage` | `CreateAcademicYearRequest` | 201 `AcademicYearDto` |
| PUT | `api/academic/years/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | `UpdateAcademicYearRequest` | `AcademicYearDto` |
| DELETE | `api/academic/years/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | nenhum | 204 |
| GET | `api/academic/classes` | `AccessPolicy.Roles.AcademicStructureRead` | nenhum | `List<SchoolClassDto>` |
| POST | `api/academic/classes` | `AccessPolicy.Roles.AcademicStructureManage` | `CreateSchoolClassRequest` | 201 `SchoolClassDto` |
| PUT | `api/academic/classes/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | `UpdateSchoolClassRequest` | `SchoolClassDto` |
| DELETE | `api/academic/classes/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | nenhum | 204 |
| GET | `api/academic/classes/{id:guid}/students` | `AccessPolicy.Roles.AcademicStructureRead` | nenhum | `List<StudentDto>` |
| GET | `api/academic/components` | `AccessPolicy.Roles.AcademicStructureRead` | nenhum | `List<CurriculumComponentDto>` |
| POST | `api/academic/components` | `AccessPolicy.Roles.AcademicStructureManage` | `CreateCurriculumComponentRequest` | 201 `CurriculumComponentDto` |
| PUT | `api/academic/components/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | `UpdateCurriculumComponentRequest` | `CurriculumComponentDto` |
| DELETE | `api/academic/components/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | nenhum | 204 |
| GET | `api/academic/assessments` | `AccessPolicy.Roles.AcademicStructureRead` | nenhum | `List<AssessmentDto>` |
| POST | `api/academic/assessments` | `AccessPolicy.Roles.AcademicStructureManage` | `CreateAssessmentRequest` | 201 `AssessmentDto` |
| PUT | `api/academic/assessments/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | `UpdateAssessmentRequest` | `AssessmentDto` |
| DELETE | `api/academic/assessments/{id:guid}` | `AccessPolicy.Roles.AcademicStructureManage` | nenhum | 204 |
| PUT | `api/academic/assessments/{assessmentId:guid}/grades/{studentId:guid}` | `AccessPolicy.Roles.GradeManage` | `GradeRequest` | `GradeDto` |

**Internal Logic:** cada action converte rota e corpo em chamada ao serviço e devolve o resultado; `GET api/academic/classes` e `GET api/academic/assessments` aceitam filtros opcionais por query string.
**Dependencies:** `IAcademicService`, `AccessPolicy.Roles`.
**Error Handling:** nenhum `try/catch` no controlador; a exceção de domínio sobe e o middleware existente converte em 400, 404 ou 409 com o código no corpo, no mesmo formato já usado pelos controladores atuais.

### Component: AcademicDtos

**Purpose:** Contratos de entrada e saída da estrutura acadêmica.
**Interface:** `src/Ciclo.Infrastructure/Contracts/AcademicDtos.cs` declara `AcademicYearDto(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, bool IsActive)`, `CreateAcademicYearRequest(int Year, DateOnly StartDate, DateOnly EndDate, bool IsActive)`, `UpdateAcademicYearRequest(int Year, DateOnly StartDate, DateOnly EndDate, bool IsActive)`, `SchoolClassDto(Guid Id, string Name, Guid AcademicYearId, int Year)`, `CreateSchoolClassRequest(string Name, Guid AcademicYearId)`, `UpdateSchoolClassRequest(string Name, Guid AcademicYearId)`, `CurriculumComponentDto(Guid Id, string Name, string Code, bool IsActive)`, `CreateCurriculumComponentRequest(string Name, string Code, bool IsActive)`, `UpdateCurriculumComponentRequest(string Name, string Code, bool IsActive)`, `AssessmentDto(Guid Id, Guid SchoolClassId, Guid CurriculumComponentId, string Title, decimal Weight, DateOnly Date, bool IsRecovery)`, `CreateAssessmentRequest(Guid SchoolClassId, Guid CurriculumComponentId, string Title, decimal Weight, DateOnly Date, bool IsRecovery)`, `UpdateAssessmentRequest(Guid SchoolClassId, Guid CurriculumComponentId, string Title, decimal Weight, DateOnly Date, bool IsRecovery)` e `GradeRequest(decimal Value)`.
**Internal Logic:** apenas transporte, sem lógica.
**Dependencies:** nenhuma.
**Error Handling:** desserialização inválida é rejeitada pelo `[ApiController]` com o 400 padrão do framework.

### Component: Frontend academic.ts

**Purpose:** Cliente tipado das rotas administrativas de estrutura e de nota.
**Interface:** `frontend/src/api/academic.ts` exporta `listAcademicYears`, `createAcademicYear`, `updateAcademicYear`, `deleteAcademicYear`, `listSchoolClasses`, `createSchoolClass`, `updateSchoolClass`, `deleteSchoolClass`, `listComponents`, `createComponent`, `updateComponent`, `deleteComponent`, `listAssessments`, `createAssessment`, `updateAssessment`, `deleteAssessment`, `listClassStudents` e `upsertGrade`, todas sobre o cliente HTTP já configurado em `frontend/src/api`.
**Internal Logic:** cada função monta a rota e delega ao cliente axios existente, com o mesmo tratamento de erro das funções atuais.
**Dependencies:** cliente HTTP existente, tipos de `frontend/src/types/index.ts`.
**Error Handling:** erro de API propaga para a tela, que resolve a mensagem pelo mapa de `frontend/src/api/errors.ts`.

### Component: Frontend AcademicStructure e GradeEntry

**Purpose:** Cadastrar a estrutura acadêmica e lançar notas.
**Interface:** `frontend/src/pages/admin/AcademicStructure.tsx` e `frontend/src/pages/admin/GradeEntry.tsx`, ambos componentes React exportados por padrão e registrados em `frontend/src/App.tsx` nas rotas `/admin/academico` e `/admin/notas`.
**Internal Logic:** `AcademicStructure` tem quatro seções (anos, turmas, componentes, avaliações) com lista, formulário de criação, edição e exclusão; `GradeEntry` seleciona turma e avaliação, carrega o roster com `listClassStudents` e grava cada nota com `upsertGrade`, rejeitando valores fora de 0 a 10 no próprio formulário.
**Dependencies:** `frontend/src/api/academic.ts`, tipos.
**Error Handling:** estado de carregamento, estado vazio explícito e mensagem de erro por código; falha de gravação mantém o formulário aberto com a nota digitada.

### Component: Frontend ChildDetail (alterado)

**Purpose:** Exibir o boletim do aluno no portal dos pais.
**Interface:** a aba `grades` passa a consumir `getChildReportCard` de `frontend/src/api/parent.ts` e renderiza uma linha por componente com média regular, nota de recuperação, média final e rótulo da situação.
**Internal Logic:** dispara a busca ao abrir a aba, guarda o resultado em estado local e mapeia `aprovado` para `Aprovado`, `recuperacao` para `Em recuperação`, `reprovado` para `Reprovado` e `sem_nota` para `Sem nota`.
**Dependencies:** `frontend/src/api/parent.ts`, tipos de boletim.
**Error Handling:** estado de carregamento, estado vazio explícito quando não há componentes e mensagem de erro padronizada com ação de tentar novamente.

## Data Flow

**Cadastro de aluno com vínculo:** `POST api/students` chega em `StudentController`, que delega a `IStudentService.CreateAsync`. Antes do `SaveChangesAsync`, `ResolveAcademicStructureAsync` normaliza `Turma`, localiza ou cria `AcademicYear` e `SchoolClass`, grava `AcademicYearId` e `SchoolClassId` e reescreve o espelho. O `StudentDto` devolvido mantém `turma` e `anoLetivo` com os valores canônicos.

**Cadastro de estrutura:** `POST api/academic/years` chega em `AcademicController`, que chama `AcademicService.CreateYearAsync`. O serviço valida faixa de datas, ano e unicidade por tenant, cria a entidade e chama `SaveChangesAsync`, devolvendo `AcademicYearDto`. O mesmo fluxo vale para classes, componentes e avaliações.

**Lançamento de nota:** `PUT api/academic/assessments/{assessmentId}/grades/{studentId}` chega em `AcademicController`, que chama `AcademicService.UpsertGradeAsync`. O serviço valida a faixa 0 a 10, carrega a avaliação, confirma que o aluno pertence à turma da avaliação, localiza a nota existente e cria ou atualiza `Value` e `RecordedAt` em uma única chamada de `SaveChangesAsync`.

**Boletim no portal:** `GET api/parent/children/{id}/report-card` chega em `ParentController`, que chama `ParentService.GetChildReportCardAsync`. O serviço verifica o vínculo responsável-aluno, carrega a turma do aluno, as avaliações da turma e as notas do aluno, agrupa por componente e chama `GradeCalculation` uma vez por componente. O resultado é montado em `ReportCardDto` e serializado em camelCase.

**Backfill:** `dotnet ef database update` aplica a migração. As tabelas são criadas, as colunas nulas são adicionadas e `AcademicStructureBackfill.Sql` materializa anos e turmas distintas e atualiza os alunos por junção. Nada é removido.

## Data Structures

Tabela `AcademicYears`: `Id uuid` chave primária, `TenantId uuid NOT NULL`, `Year integer NOT NULL`, `StartDate date NOT NULL`, `EndDate date NOT NULL`, `IsActive boolean NOT NULL`; índice único `(TenantId, Year)`.

Tabela `SchoolClasses`: `Id uuid` chave primária, `TenantId uuid NOT NULL`, `Name varchar(50) NOT NULL`, `AcademicYearId uuid NOT NULL` referenciando `AcademicYears` com `DeleteBehavior.Restrict`; índice único `(TenantId, AcademicYearId, Name)`.

Tabela `CurriculumComponents`: `Id uuid` chave primária, `TenantId uuid NOT NULL`, `Name varchar(100) NOT NULL`, `Code varchar(20) NOT NULL`, `IsActive boolean NOT NULL`; índice único `(TenantId, Code)`.

Tabela `Assessments`: `Id uuid` chave primária, `TenantId uuid NOT NULL`, `SchoolClassId uuid NOT NULL` com `Restrict`, `CurriculumComponentId uuid NOT NULL` com `Restrict`, `Title varchar(100) NOT NULL`, `Weight numeric(5,2) NOT NULL DEFAULT 1`, `Date date NOT NULL`, `IsRecovery boolean NOT NULL`; índice único `(TenantId, SchoolClassId, CurriculumComponentId, Title)`.

Tabela `Grades`: `Id uuid` chave primária, `TenantId uuid NOT NULL`, `AssessmentId uuid NOT NULL` com `Restrict`, `StudentId uuid NOT NULL` com `Restrict`, `Value numeric(5,2) NOT NULL`, `RecordedAt timestamptz NOT NULL`; índice único `(AssessmentId, StudentId)`.

Tabela `Students` alterada: acrescenta `AcademicYearId uuid NULL` com `Restrict` e `SchoolClassId uuid NULL` com `Restrict`; mantém `Turma varchar(50)` e `AnoLetivo integer`.

`ReportCardDto`: `AlunoId` Guid, `Turma` string nulo, `AnoLetivo` int nulo, `Componentes` lista de `ReportCardEntryDto`.
`ReportCardEntryDto`: `Componente` string, `MediaRegular` decimal nulo, `NotaRecuperacao` decimal nulo, `MediaFinal` decimal nulo, `Situacao` string, `Avaliacoes` lista de `ReportCardScoreDto`.
`ReportCardScoreDto`: `Avaliacao` string, `Peso` decimal, `Nota` decimal nulo, `Data` DateOnly, `Recuperacao` bool.
`AssessmentScore`: `Value` decimal, `Weight` decimal, `IsRecovery` bool.

## File / Module Layout

Novos arquivos:

- `src/Ciclo.Core/Entities/AcademicYear.cs` — entidade ano letivo.
- `src/Ciclo.Core/Entities/SchoolClass.cs` — entidade turma.
- `src/Ciclo.Core/Entities/CurriculumComponent.cs` — entidade componente curricular.
- `src/Ciclo.Core/Entities/Assessment.cs` — entidade avaliação.
- `src/Ciclo.Core/Entities/Grade.cs` — entidade nota.
- `src/Ciclo.Infrastructure/Contracts/AcademicDtos.cs` — contratos de entrada e saída.
- `src/Ciclo.Infrastructure/Services/AcademicService.cs` — `IAcademicService` e `AcademicService`.
- `src/Ciclo.Infrastructure/Services/GradeCalculation.cs` — `AssessmentScore` e `GradeCalculation`.
- `src/Ciclo.Infrastructure/Migrations/AcademicStructureBackfill.cs` — `AcademicStructureBackfill.Sql`.
- `src/Ciclo.Api/Controllers/AcademicController.cs` — endpoints administrativos.
- Migração EF Core gerada na pasta de migrações de `Ciclo.Infrastructure`, aplicando o SQL do backfill.
- `frontend/src/api/academic.ts` — cliente das rotas administrativas.
- `frontend/src/pages/admin/AcademicStructure.tsx` — cadastro da estrutura.
- `frontend/src/pages/admin/GradeEntry.tsx` — lançamento de notas.

Arquivos alterados:

- `src/Ciclo.Core/Entities/Student.cs` — dois identificadores e duas navegações.
- `src/Ciclo.Infrastructure/Data/AppDbContext.cs` — DbSets e mapeamento.
- `src/Ciclo.Infrastructure/Services/StudentService.cs` — resolvedor chamado na criação e na atualização.
- `src/Ciclo.Infrastructure/Services/ParentService.cs` — notas reais e boletim.
- `src/Ciclo.Infrastructure/Contracts/ParentDtos.cs` — três registros do boletim.
- `src/Ciclo.Api/Controllers/ParentController.cs` — rota `report-card`.
- `src/Ciclo.Api/Program.cs` — registro de `IAcademicService`.
- `src/Ciclo.Infrastructure/Security/AccessPolicy.cs` — três constantes de role.
- `frontend/src/api/parent.ts` — `getChildReportCard`.
- `frontend/src/types/index.ts` — tipos de estrutura acadêmica e de boletim.
- `frontend/src/App.tsx` — rotas `/admin/academico` e `/admin/notas`.
- `frontend/src/pages/parent/ChildDetail.tsx` — aba de notas com o boletim.

## Testing Strategy

`tests/Ciclo.Api.Tests/AcademicStructureTests.cs` (xUnit, `AppDbContext` InMemory com o contexto de tenant fixo, no padrão de `StudentMappingAndParentDashboardTests`) prova: criação e listagem de ano, turma, componente e avaliação; rejeição de nome de turma repetido no mesmo ano; rejeição de `EndDate` anterior a `StartDate`; rejeição de peso menor ou igual a zero; exclusão bloqueada de ano com turmas e de turma com alunos; isolamento por tenant devolvendo não encontrado para identificador de outro tenant; autorização administrativa por role; `UpsertGradeAsync` criando uma linha e atualizando a mesma linha na segunda chamada; rejeição de nota fora de 0 a 10 e de aluno fora da turma da avaliação; `ListClassStudentsAsync` devolvendo apenas o roster da turma.

`tests/Ciclo.Api.Tests/GradeCalculationTests.cs` prova diretamente a classe pura: média ponderada com pesos distintos; média nula sem notas; recuperação menor que a média mantendo a média regular; recuperação maior assumindo a média final; recuperação isolada sem média regular; limiares exatos 6 e 4; situação `sem_nota`, `aprovado`, `recuperacao` e `reprovado`; arredondamento em duas casas com meio para cima. Prova também, via `ParentService`, que `GetChildGradesAsync` deixa de devolver lista vazia para aluno com notas lançadas e que o boletim traz média e situação por componente.

`tests/Ciclo.Api.Tests/AcademicMigrationTests.cs` prova o conteúdo de `AcademicStructureBackfill.Sql` a partir de um `MigrationBuilder` novo: presença da inserção em `AcademicYears` a partir de valores distintos de `AnoLetivo`, da inserção em `SchoolClasses` a partir de pares distintos de ano e turma, do `UPDATE` de `Students` por junção e ausência de remoção de coluna ou de tabela.

Testes existentes ajustados quando o construtor ou o comportamento do serviço mudar: `tests/Ciclo.Api.Tests/AccessPolicyTests.cs`, `tests/Ciclo.Api.Tests/StudentMappingAndParentDashboardTests.cs`, `tests/Ciclo.Api.Tests/ListFilterTests.cs` e `tests/Ciclo.Api.Tests/EnrollmentJourneyTests.cs`.

## Migration / Rollback

Migração para frente: `dotnet ef migrations add AcademicStructure` no projeto de infraestrutura com o projeto de API como startup, seguido de `dotnet ef database update`. A migração aplica `AcademicStructureBackfill.Sql` depois de criar as tabelas e as colunas, na ordem: tabelas, colunas e chaves estrangeiras, backfill SQL.

Rollback: o `Down()` da migração remove as duas colunas de `Students` e as cinco tabelas na ordem inversa de dependência. As colunas legadas `Turma` e `AnoLetivo` nunca são tocadas, então os dados originais continuam intactos e o sistema volta ao comportamento anterior sem perda de dado.

Não há feature flag: a mudança é aditiva e o comportamento antigo de leitura de aluno é preservado pelo espelho.
