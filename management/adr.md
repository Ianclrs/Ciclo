# Architecture Decision Records (ADR)

This file records architecture decisions that affect the project. Each ADR describes a decision, its context, the rationale behind it, and its consequences. ADRs are numbered sequentially (ADR-001, ADR-002, ...) and never renumbered after creation.

---

## ADR-001: .NET 10 + EF Core + PostgreSQL Stack

**Date:** 2026-08-07
**Status:** Accepted

**Context:** We need to choose the backend stack for a multi-tenant school management SaaS. Requirements: REST API, relational data model, JWT auth, file storage, background jobs.

**Decision:** Use **C# .NET 10 (ASP.NET Core Web API)** + **Entity Framework Core 10** + **PostgreSQL 16**.

**Rationale:**
- .NET 10 is the latest LTS-targeted release with the best performance profile for APIs.
- EF Core 10 provides a mature ORM with global query filters (essential for multi-tenancy).
- PostgreSQL 16 is robust, free, and has first-class EF Core provider via Npgsql.
- C# strong typing ensures compile-time safety — ideal for AI-driven development.

**Consequences:**
- EF Core global query filters enable clean tenant isolation without schema-per-tenant complexity.
- PostgreSQL allows JSONB columns for flexible data (performance records, custom fields).
- Requires Npgsql.EntityFrameworkCore.PostgreSQL package.

---

## ADR-002: Shared Database with TenantId Column

**Date:** 2026-08-07
**Status:** Accepted

**Context:** We need to isolate data between multiple schools (tenants). Options: database-per-tenant, schema-per-tenant, or shared database with discriminator column.

**Decision:** Use a **shared database with a `TenantId` discriminator column** on all tenant-scoped tables, enforced by EF Core global query filters and a tenant-aware middleware.

**Rationale:**
- Single database to manage and backup — operational simplicity.
- EF Core global query filters automatically append `WHERE TenantId = @currentTenantId` to every query.
- Tenant middleware resolves `TenantId` from the JWT claim on every request.
- Avoids the complexity of per-tenant schema migrations.

**Consequences:**
- Every tenant-scoped entity must include `TenantId`.
- Tenant context must flow from HTTP request through the entire pipeline.
- Migration discipline required: never forget to include TenantId in new entities.

---

## ADR-003: JWT Access + Refresh Tokens

**Date:** 2026-08-07
**Status:** Accepted

**Context:** The system needs stateless authentication for the SPA/PWA with support for Google OAuth.

**Decision:** Use **JWT Bearer tokens** (access: 15 min, refresh: 7 days) + **ASP.NET Core Identity** for credential management. Google OAuth via OpenID Connect middleware.

**Rationale:**
- Access tokens are short-lived (15 min) and stored in memory (SPA) — never in localStorage.
- Refresh tokens are stored in an HttpOnly cookie and in the database (revocable).
- Google OAuth uses the standard ASP.NET Core `Microsoft.AspNetCore.Authentication.Google` package.
- Stateless API — no server-side session store needed.

**Consequences:**
- Refresh token rotation prevents long-term token theft.
- Google OAuth onboarding is frictionless for parents.
- SPA must handle token refresh transparently via Axios interceptors.

---

## ADR-004: Retenção e Imutabilidade da Trilha de Auditoria

**Status:** Accepted (Spec 110)

**Context**

A trilha de auditoria responde "quem fez o quê, quando e de onde" para operações sensíveis — aprovar, rejeitar ou cancelar matrícula, verificar documento e revogar sessão. Um registro que pode ser editado ou apagado não serve como prova, e um registro mantido para sempre conflita com o direito de eliminação da LGPD.

**Decision**

- **Imutabilidade em duas camadas, com a mesma regra:** `AppDbContext.GuardAuditImmutability` bloqueia `Modified` e `Deleted` dentro do piso; no PostgreSQL, a função `audit_events_append_only_guard` com os gatilhos `audit_events_no_update` e `audit_events_no_delete` bloqueia `UPDATE` e `DELETE` dentro do piso. A duplicação é intencional: a aplicação falha antes de ir ao banco e o banco protege contra qualquer cliente que não passe pela aplicação.
- **Piso de imutabilidade: 90 dias.** Nenhum evento pode ser alterado em nenhuma hipótese; nenhum evento pode ser removido antes de 90 dias.
- **Retenção configurável: 365 dias por padrão**, faixa válida de 90 a 3650 dias (`Audit:RetentionDays`). Valor inválido impede a inicialização (fail-closed).
- **Expurgo** por `AuditRetentionPurger`, em lotes de 5000, atravessando tenants de forma explícita (`IgnoreQueryFilters`), executado a cada 24 horas por `AuditRetentionService`.
- **Consulta e exportação restritas a Admin** (`GET /api/audit-events` e `GET /api/audit-events/export`), exportação limitada a 10000 linhas — acima disso a resposta é 400 `export_exceeds_limit`, nunca truncamento silencioso.

**Consequences**

- Eventos antigos (fora do piso) podem ser expurgados pela política de retenção; eventos dentro do piso não podem ser apagados nem pela aplicação nem por SQL direto.
- Corrigir um evento exige uma nova operação e uma nova entrada na trilha, não uma edição — o histórico permanece fiel.
- A base legal registrada é a LGPD Art. 7º, II e IX (cumprimento de obrigação legal e legítimo interesse) e Art. 16 (conservação pelo tempo necessário à finalidade).
- Alterar o piso de 90 dias ou o default de retenção exige nova decisão registrada e uma spec de evolução: a spec 110 está finalizada.
