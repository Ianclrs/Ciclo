# Architecture

## 1. Technical Architecture

- **Stack:** C# .NET 10 (ASP.NET Core Web API), Entity Framework Core 10, PostgreSQL 16
- **Frontend:** React 19, Vite 6, Tailwind CSS 4, PWA (Workbox)
- **Database:** PostgreSQL 16 — schema-per-tenant via EF Core global query filters
- **Auth:** ASP.NET Core Identity + JWT Bearer tokens + Google OAuth 2.0 (OpenID Connect)
- **Deployment:** Docker Compose (local dev), Docker Swarm or AWS ECS (production)
- **Platform:** Web (SPA) + PWA for mobile (iOS/Android)

## 2. Conceptual Architecture

### Layers

```
┌─────────────────────────────────────┐
│  Presentation (React SPA + PWA)     │  ← Frontend (Spec 100)
├─────────────────────────────────────┤
│  API Layer (ASP.NET Core Controllers)│  ← REST endpoints
├─────────────────────────────────────┤
│  Application Layer (Services)       │  ← Business logic, workflows
├─────────────────────────────────────┤
│  Domain Layer (Entities, Value Obj) │  ← Core domain models
├─────────────────────────────────────┤
│  Infrastructure Layer               │  ← EF Core, Auth, Storage, Email
├─────────────────────────────────────┤
│  PostgreSQL 16                       │  ← Data persistence
└─────────────────────────────────────┘
```

### Cross-Cutting Concerns

| Concern | Implementation |
|---|---|
| Multi-Tenancy | Middleware + EF Core Global Query Filter (`TenantId`) |
| Auth | JWT (access + refresh tokens), Google OAuth via OpenID Connect |
| Logging | Serilog → Console + File |
| Validation | FluentValidation |
| API Docs | Swagger / OpenAPI |
| Error Handling | Global exception middleware + ProblemDetails (RFC 9457) |
| Background Jobs | Hangfire or Quartz.NET (for notifications) |
| File Storage | Local disk (dev) / S3-compatible (prod) |

### API Route Convention

Every application endpoint lives under `/api` (`[Route("api/<resource>")]`). This is not cosmetic:

- The SPA calls the API on its **own origin** — Vite proxies `/api` in development and the Vercel rewrite proxies `/api/*` in production. Same-origin keeps the refresh cookie (`SameSite=Strict`) first-party, so no CSRF protection is weakened and no CORS policy is needed.
- It removes path collisions between SPA routes and API routes. Without the prefix, `/parent/children/:id` existed both as a React Router page and as an API endpoint, and `/auth/google/callback` was both a SPA route and an API callback.
- `/health` is intentionally **outside** `/api`: it is an infrastructure probe, not part of the application API surface.

Any new controller MUST declare `[Route("api/<resource>")]`.

### Fundamental Principles

These principles MUST be followed by ALL specs and implementations:

1. **Extreme Detail & Determinism:** All specs must be created, maintained, and evolved with extreme detail, total completeness, and absolute determinism. No decision may be left pending for implementation time. Every spec must be implementable 100% by autonomous AI.

2. **KISS (Keep It Simple, Stupid):** All implementations must be as simple as possible. Avoid unnecessary abstractions, patterns, or complexity. The AI must be able to implement, maintain, and evolve the code easily.

3. **DRY (Don't Repeat Yourself):** Avoid duplication whenever possible, provided it does not conflict with KISS. Simplicity takes precedence over deduplication when they conflict.

4. **AI-First Documentation:** All spec documents (requirements.md, design.md, task.md) are written exclusively for AI engines (especially DeepSeek V4 Pro). Human readability is secondary. Precision, completeness, and determinism are paramount.
