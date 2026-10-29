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
| 10 | **Auditoria** | Trilha imutável por escola de aprovações, rejeições, cancelamentos e revogações de sessão, com consulta e exportação CSV restritas ao Admin. |

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

A configuração de deploy vive em **dois** `vercel.json`, porque a Vercel lê **apenas** o arquivo que está no *Root Directory* do projeto — e os caminhos relativos mudam conforme esse campo:

| Root Directory no painel | Arquivo lido pela Vercel | Comandos |
|---|---|---|
| **vazio** (recomendado) | `vercel.json` na raiz | `cd frontend && npm install` · `cd frontend && npm run build` · `outputDirectory: frontend/dist` |
| `frontend` | `frontend/vercel.json` | `npm install` · `npm run build` · `outputDirectory: dist` |

Os dois arquivos têm os mesmos rewrites. O teste `frontend/tests/deployConfig.test.ts` falha se eles divergirem — a duplicação existe só para o deploy funcionar nas duas escolhas, não para permitir configurações diferentes.

> **Sintomas de configuração incoerente** (já aconteceram neste projeto):
> - `cd: frontend: No such file or directory` no passo de install → o Root Directory aponta para `frontend`, mas o arquivo lido é o da raiz (que faz `cd frontend`).
> - Build passa e **todas as rotas respondem 404** (`/index.html` some) → nenhum `vercel.json` foi encontrado no Root Directory; a Vercel serviu o repositório cru.
>
> Em qualquer um dos casos, alinhe o campo com a tabela acima e refaça o deploy. Overrides de *Install Command* / *Build Command* no painel vencem o arquivo — deixe-os vazios.

Cada `vercel.json` faz três coisas:

1. **Build do frontend**, conforme a tabela acima.
2. **Rewrite de `/api/*` para a API.** O navegador continua falando com o próprio domínio, então o cookie de refresh (`HttpOnly`, `SameSite=Strict`, `Secure` — `AuthController.cs`) é first-party e nenhuma política de CORS é necessária.
3. **Fallback do SPA:** `/(.*)` → `/index.html`, para que recarregar `/admin/students` não retorne 404. Arquivos estáticos são servidos direto do disco — a Vercel checa o filesystem antes de aplicar os rewrites.

> **Ação obrigatória antes de integrar com a API:** trocar `https://TROCAR-PELO-HOST-DA-API` nos **dois** arquivos pelo host real da API. **Substitua o valor, não apague a regra:** sem ela, `/api/*` cairia no fallback do SPA e o frontend receberia HTML (status 200) no lugar de JSON.

Em desenvolvimento nada disso é necessário: o `vite.config.ts` faz o mesmo proxy de `/api` para `localhost:5264`.

### Modo demonstração (deploy sem API)

Quando o build **não recebe `VITE_API_URL`**, o frontend entra em modo demonstração: uma API falsa
responde no lugar do backend, com dados fictícios gravados no navegador. Serve para publicar o
produto como vitrine antes de a API estar no ar.

| Item | Como funciona |
|---|---|
| Ativação | automática quando `VITE_API_URL` está ausente no build; `VITE_DEMO_MODE=1` força a demo também em desenvolvimento |
| Transporte | adaptador do axios (`src/mocks/index.ts`) — sem Service Worker, então funciona em qualquer navegador sem exigir HTTPS especial |
| Dados | `src/mocks/seed.ts`: colégio, 12 alunos, matrículas, documentos, notificações e usuários |
| Persistência | escrita sobrevive ao recarregar (`localStorage['ciclo.demo.v1']`). Para zerar, apague essa chave |
| Entrada | a tela de login mostra dois acessos de um clique: `diretor@demo.ciclo` (Admin) e `responsavel@demo.ciclo` (Responsável); a senha é ignorada |
| Sinalização | a topbar exibe o selo **demonstração** enquanto os dados são fictícios |

> A demo não substitui a API: para apontar o app a um backend real, defina `VITE_API_URL` e
> rebuild — sem essa variável e sem a demo forçada, o frontend chamaria `/api` no próprio domínio.

### Backend

A API precisa estar publicada em **HTTPS** e alcançável pela internet — o rewrite da Vercel chama o host diretamente.

| Configuração | Por quê |
|---|---|
| `Jwt:Secret` | **Ausente** de `appsettings.json` por decisão; a API **não inicia** sem ele (`JwtSettings.cs`). Defina pela variável `Jwt__Secret` |
| `FileStorage:EncryptionKey` | Base64 de 32 bytes; a API **não inicia** sem ela (`FileStorageKey.cs`). Defina pela variável `FileStorage__EncryptionKey` |
| `FileStorage:RootPath` | Disco local: em container sem volume os arquivos somem a cada deploy |

O endpoint `/health` fica **fora** de `/api` de propósito — é probe de infraestrutura, e pode ser usado como health check do host da API.

### Rotação de segredos

Três segredos exigem rotação periódica: `Jwt:Secret`, a senha do banco e
`FileStorage:EncryptionKey`. Os dois primeiros são troca direta; a **chave de arquivos precisa
re-encriptar o que já existe**, senão todos os documentos ficam ilegíveis.

**1. JWT e senha do banco** — gere o novo valor, publique o segredo no ambiente e reinicie.
Tokens emitidos com o segredo antigo deixam de valer (todos precisam entrar de novo).

**2. Chave de armazenamento (exige re-encriptação):**

```bash
# 1) gere a chave nova (32 bytes em Base64)
openssl rand -base64 32

# 2) configure: a NOVA em EncryptionKey, a ANTIGA em PreviousEncryptionKeys
dotnet user-secrets set "FileStorage:EncryptionKey" "<nova>" --project src/Ciclo.Api
dotnet user-secrets set "FileStorage:PreviousEncryptionKeys:0" "<antiga>" --project src/Ciclo.Api

# 3) re-encripte todos os documentos (idempotente; exit != 0 se algo falhar)
dotnet run --project src/Ciclo.Api -- --rotate-storage-key

# 4) só depois de "0 failed", remova a chave antiga
dotnet user-secrets remove "FileStorage:PreviousEncryptionKeys:0" --project src/Ciclo.Api
```

Enquanto a chave antiga estiver no keyring, a leitura continua funcionando nos dois sentidos —
é o que permite rotacionar sem janela de indisponibilidade. O comando cobre **todos os tenants**
e relata arquivos ausentes (registros órfãos) separadamente de falhas de decifração.

> Se um arquivo falhar com "No configured storage key could decrypt this file", a chave antiga
> não está no keyring ou o arquivo foi criptografado com uma chave anterior a ela. Não remova o
> keyring antes de resolver: sem a chave correta, o conteúdo é irrecuperável.

### Operação e observabilidade

| Endpoint | Uso |
|---|---|
| `/health/live` | liveness — o processo responde; **não** consulta banco, disco ou fila |
| `/health/ready` | readiness — banco e armazenamento; responde **503** detalhando o check que falhou |
| `/metrics` | scrape Prometheus (texto), fora de `/api` e sem dado pessoal |

A correlação de uma requisição aparece em três lugares com o mesmo valor: cabeçalho
`X-Correlation-Id` da resposta, campo `[<id>]` do log e extensão `correlationId` do corpo de erro.

#### Limiares de alerta

| Sinal | Limiar | Ação |
|---|---|---|
| `/health/ready` com `Unhealthy` | por mais de 2 minutos | investigar a dependência indicada em `checks` |
| `http_requests_received_total` com `code=5xx` | acima de 1% em 5 minutos | buscar no log o `correlationId` do relato |
| `http_request_duration_seconds` (p95) | acima de 1 segundo em 5 minutos | verificar banco e profundidade da fila |
| `ciclo_email_queue_depth` | acima de 80% de `ciclo_email_queue_capacity` por 5 minutos | verificar o consumidor de e-mail e o provedor |

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
