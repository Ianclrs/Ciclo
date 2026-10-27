# Ciclo

> Plataforma SaaS multi-tenant de gestão escolar que conecta colégios e pais em um único lugar.

---

## Sobre

O **Ciclo** centraliza e automatiza o ciclo de vida acadêmico dos alunos: matrículas, rematrículas, documentos obrigatórios, notificações e acompanhamento de desempenho — eliminando papelada, reduzindo erros e acelerando a comunicação entre escola e família.

- **Gestores escolares (Admin/Staff):** gerenciam matrículas, documentos, turmas e comunicação com os pais.
- **Pais/Responsáveis:** acompanham o desempenho dos filhos, recebem notificações de documentos pendentes e acompanham status de matrícula/rematrícula.

### Funcionalidades

| # | Funcionalidade | Descrição |
|---|---------------|-----------|
| 1 | **Multi-Tenant** | Cada colégio tem seu ambiente isolado via `TenantId`. Dados nunca vazam entre tenants. |
| 2 | **Autenticação** | Login com email/senha. JWT access tokens (15min) + refresh tokens (7 dias). *(Google OAuth foi removido no C8.)* |
| 3 | **Gestão de Alunos** | CRUD completo com dados pessoais, contatos, filiação e informações acadêmicas. |
| 4 | **Matrícula** | Workflow completo: abertura de período, inscrição, checklist de documentos, aprovação. |
| 5 | **Rematrícula** | Renovação com reaproveitamento de documentos validados e alertas de vencimento. |
| 6 | **Documentos** | Upload, categorização, verificação pela secretaria, tracking de validade. |
| 7 | **Notificações** | In-app para documentos pendentes, comunicados e lembretes. O e-mail está **preparado, não entregue**: o dispatcher enfileira e o `ConsoleEmailSender` apenas registra (provedor em aberto). |
| 8 | **Portal dos Pais** | Dashboard dos filhos, desempenho, inbox de notificações e upload de documentos. |
| 9 | **PWA Mobile** | Aplicativo progressivo instalável no celular (iOS/Android) com suporte offline. |

---

## Tecnologias

| Camada | Tecnologia |
|--------|-----------|
| **Backend** | C# .NET 10 — ASP.NET Core Web API |
| **ORM** | Entity Framework Core 10 + Npgsql |
| **Banco de Dados** | PostgreSQL 16 |
| **Autenticação** | ASP.NET Core Identity + JWT Bearer (Google OAuth removido no C8) |
| **Validação** | FluentValidation |
| **Logging** | Serilog (Console + File) |
| **Documentação da API** | Swagger / OpenAPI |
| **Erros** | Global exception middleware + ProblemDetails (RFC 9457) |
| **Armazenamento** | Disco local (dev) / S3-compatible (prod) |
| **Frontend** | React 19 + Vite 8 + Tailwind CSS 4 + TypeScript 6 |
| **PWA** | Workbox (service worker, cache offline, install prompt) |
| **Deploy** | Docker Compose |

### Projetos da Solution

| Projeto | Responsabilidade |
|---------|-----------------|
| `Ciclo.Api` | Controllers, middleware, configuração da aplicação |
| `Ciclo.Core` | Entidades, interfaces, regras de domínio |
| `Ciclo.Infrastructure` | DbContext, serviços, storage, email |
| `Ciclo.Api.Tests` | Testes unitários e de integração |

---

## Como Rodar

### Pré-requisitos

| Ferramenta | Versão |
|-----------|--------|
| .NET SDK | 10.0 |
| Node.js | 22+ |
| Docker + Docker Compose | 24+ / 2+ |

### Backend + Banco

O `docker compose` exige dois segredos da API. Copie o exemplo e preencha:

```bash
cp .env.example .env      # .env é ignorado pelo git
# edite .env: JWT_SECRET (>= 32 bytes) e ENCRYPTION_KEY (Base64 de 32 bytes)
openssl rand -base64 48   # sugestão de valor para JWT_SECRET
openssl rand -base64 32   # sugestão de valor para ENCRYPTION_KEY

docker compose up -d
```

Sem essas variáveis o `docker compose` falha com uma mensagem explícita em vez de subir com valores padrão.
Isso vale para o arquivo inteiro — inclusive para subir **só o banco** (`docker compose up -d postgres`),
porque a interpolação do compose acontece antes de escolher os serviços. Como a API é *fail-closed* e
em `Development` aplica as migrations, o banco precisa estar no ar antes de rodar a API localmente.

A API estará em `http://localhost:5000` e o Swagger em `http://localhost:5000/swagger`.

### Frontend

```bash
cd frontend
npm install
npm run dev
```

O frontend estará em `http://localhost:5173`. O Vite faz proxy das chamadas da API para o backend automaticamente.

### Sem Docker (PostgreSQL local)

```bash
# 1. Criar banco e usuário
psql -U postgres -f db/init-db.sql

# 2. Configurar os segredos da API (uma vez, ficam fora do repositório)
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)" --project src/Ciclo.Api
dotnet user-secrets set "FileStorage:EncryptionKey" "$(openssl rand -base64 32)" --project src/Ciclo.Api

# 3. Iniciar a API
dotnet run --project src/Ciclo.Api
```

A API **não inicia** sem `Jwt:Secret` e `FileStorage:EncryptionKey` — os valores não têm padrão no
repositório (ver `JwtSettings.cs` e `FileStorageKey.cs`). Em outros ambientes, use as variáveis
`Jwt__Secret` e `FileStorage__EncryptionKey`.

### Schema do banco

O schema tem **um dono único**: as migrations em `src/Ciclo.Infrastructure/Migrations/`, com o
`AppDbContextModelSnapshot` que as acompanha. O `db/migrations.sql` que existia antes foi removido —
era uma segunda fonte de schema, escrita à mão, que já divergia do modelo.

**Em `Development`** a API aplica as migrations sozinha na inicialização e depois roda os seeders.
É *fail-closed*: se o banco não responder, a API **não sobe**. (Antes ela subia com banco vazio e
falhava em toda consulta, sem sinal claro na inicialização.)

**Fora de `Development`** a API **não** aplica schema. O deploy precisa rodar, antes de subir:

```bash
dotnet ef database update --project src/Ciclo.Infrastructure --startup-project src/Ciclo.Api
```

Assim o processo em runtime não precisa de permissão de DDL, duas instâncias não disputam a
migration, e uma migration problemática não impede a API de subir. Se o passo for esquecido, a API
**falha na inicialização** listando as migrations pendentes — em vez de servir erro em toda
requisição.

Ao alterar o modelo (entidade ou `OnModelCreating`), gere a migration correspondente. A suíte falha
em `MigrationHistoryTests` se o modelo divergir do histórico.

### Testes

```bash
dotnet test
```

---

## Deploy

### Frontend na Vercel

O `vercel.json` na **raiz do repositório** contém toda a configuração de deploy — build, pasta de saída e rewrites. Nada precisa ser preenchido no painel da Vercel.

| Configuração do projeto | Valor |
|---|---|
| Root Directory | **vazio** (raiz do repositório) |
| Install / Build / Output | definidos no `vercel.json`, não no painel |

> **Root Directory precisa ficar vazio.** A Vercel só lê o `vercel.json` que está **dentro** do Root Directory. Se você apontar o Root Directory para `frontend`, o arquivo da raiz é ignorado: o build não roda, nenhum `dist/` é publicado e **todas as rotas respondem 404** (o repositório cru é servido — `/AGENTS.md` responde 200, `/index.html` responde 404). Foi exatamente esse o sintoma do primeiro deploy.

O `vercel.json` faz três coisas:

1. **Build do frontend a partir da raiz:** `cd frontend && npm install` e `cd frontend && npm run build`, com `outputDirectory: frontend/dist`.
2. **Rewrite de `/api/*` para a API.** O navegador continua falando com o próprio domínio, então o cookie de refresh (`HttpOnly`, `SameSite=Strict`, `Secure` — `AuthController.cs`) é first-party e nenhuma política de CORS é necessária.
3. **Fallback do SPA:** `/(.*)` → `/index.html`, para que recarregar `/admin/students` não retorne 404. Arquivos estáticos são servidos direto do disco — a Vercel checa o filesystem antes de aplicar os rewrites.

> **Ação obrigatória antes de integrar com a API:** trocar `https://TROCAR-PELO-HOST-DA-API` no `vercel.json` pelo host real da API. **Substitua o valor, não apague a regra:** sem ela, `/api/*` cairia no fallback do SPA e o frontend receberia HTML (status 200) no lugar de JSON.

Em desenvolvimento nada disso é necessário: o `vite.config.ts` faz o mesmo proxy de `/api` para `localhost:5264`.

### Backend

A API precisa estar publicada em **HTTPS** e alcançável pela internet — o rewrite da Vercel chama o host diretamente.

| Configuração | Por quê |
|---|---|
| `Jwt:Secret` | **Ausente** de `appsettings.json` por decisão; a API **não inicia** sem ele (`JwtSettings.cs`). Defina pela variável `Jwt__Secret` |
| `FileStorage:EncryptionKey` | Base64 de 32 bytes; a API **não inicia** sem ela (`FileStorageKey.cs`). Defina pela variável `FileStorage__EncryptionKey` |
| `FileStorage:RootPath` | Disco local: em container sem volume os arquivos somem a cada deploy |

O endpoint `/health` fica **fora** de `/api` de propósito — é probe de infraestrutura, e pode ser usado como health check do host da API.

---

## Estrutura de Diretórios

```
├── src/
│   ├── Ciclo.Api/            # API REST (controllers, middleware)
│   ├── Ciclo.Core/            # Domínio (entidades, interfaces)
│   └── Ciclo.Infrastructure/  # Dados, serviços, autenticação
├── tests/
│   └── Ciclo.Api.Tests/       # Testes unitários e de integração
├── frontend/                  # SPA React + PWA
├── db/                        # Scripts SQL de inicialização
├── management/                # Documentação SDD (specs, ADRs, roadmap)
└── docker-compose.yml
```
