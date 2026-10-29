# DsCode Roadmap

## Spec Statuses

| Status | Meaning |
|---|---|---|---|
| `proposed` | Idea stage, no spec written yet |
| `planned` | Roadmap entry created via /spec-plan |
| `created` | Spec documents created via /spec-new |
| `verified` | Spec documents verified via /spec-verify |
| `implemented` | Code implemented via /spec-implement |
| `in-progress` | Under active development on a feature branch |
| `audited` | Final stage — implementation audited via /spec-audit. Feature is live. |
| `discarded` | Intentionally abandoned |

---

## Specs

| # | Name | Status | References |
|---|---|---|---|
| 10 | Project Bootstrap & Infrastructure | audited | V1, ADR-001 |
| 20 | Multi-Tenant Architecture | audited | V2, ADR-002 |
| 30 | Authentication & Authorization | audited | V3, ADR-003 |
| 40 | Student Management | audited | V4 |
| 50 | Enrollment System (Matrícula) | audited | V5 |
| 60 | Re-enrollment System (Rematrícula) | audited | V6 |
| 70 | Document Management | audited | V7 |
| 80 | Notification System | audited | V8 |
| 90 | Parent Portal API | audited | V9 |
| 100 | Frontend Application (React + PWA) | audited | V10 |
| 110 | auditoria-de-operacoes | verified | V11 |
| 120 | ciclo-de-vida-de-identidade | planned | V12 |
| 130 | observabilidade-operacional | verified | V13 |
| 140 | comunicacao-proativa | planned | V14 |
| 150 | estrutura-academica-e-desempenho | planned | V15 |
| 160 | rematricula-em-lote | planned | V16 |
| 170 | armazenamento-de-documentos | planned | V17 |
| 180 | qualidade-automatizada | planned | V18 |
| 190 | experiencia-i18n-e-acessibilidade | planned | V19 |
| 200 | configuracao-e-limites-por-tenant | planned | V20 |
| 210 | privacidade-lgpd | planned | V21 |

---

## Dependency Graph

```
Spec 10: Bootstrap & Infra
  └─► Spec 20: Multi-Tenant
        ├─► Spec 30: Auth
        │     ├─► Spec 40: Student Management
        │     │     ├─► Spec 50: Enrollment
        │     │     │     └─► Spec 60: Re-enrollment
        │     │     ├─► Spec 70: Documents
        │     │     └─► Spec 90: Parent Portal API
        │     │           └─► Spec 100: Frontend
        │     └─► Spec 80: Notifications
        │           └─► Spec 90: Parent Portal API
        └─► Spec 70: Documents
              └─► Spec 80: Notifications
```
