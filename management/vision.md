# Ciclo Vision

## Product Vision

Ciclo é um sistema de gestão escolar SaaS multi-tenant que conecta colégios e pais em uma única plataforma. Ele centraliza e automatiza todo o ciclo de vida acadêmico dos alunos: matrículas, rematrículas, documentos obrigatórios, notificações e acompanhamento de desempenho — eliminando papelada, reduzindo erros e acelerando a comunicação entre escola e família.

## Target Audience

- **Gestores escolares (Admin/Staff):** Diretores, secretários e coordenadores que gerenciam matrículas, documentos e comunicação com os pais.
- **Pais/Responsáveis:** Usuários finais que acompanham o desempenho dos filhos, recebem notificações de documentos pendentes e comunicados do colégio.

## Value Proposition

- **Centralização:** Todos os dados de alunos, matrículas, documentos e notificações em um só lugar.
- **Automação:** Alertas automáticos de documentos vencidos/pendentes e lembretes de rematrícula.
- **Multi-tenant SaaS:** Cada colégio tem seu ambiente isolado, com seus próprios gestores, alunos e pais.
- **PWA Mobile-Ready:** Acesso completo via navegador no celular com experiência nativa (PWA).
- **Login Simplificado:** Email/senha para os pais, reduzindo fricção no onboarding. (O login com Google foi **removido** no C8 — nunca chegou a funcionar.)

---

## Value Blocks

<!-- Value blocks are added incrementally by /spec-plan and /spec-new. Do not edit manually. -->

### V1: Project Bootstrap & Infrastructure
Inicialização da solução .NET 10 com estrutura de projetos (API, Core, Infrastructure), configuração do PostgreSQL via Docker Compose, EF Core com Npgsql, e pipeline base de CI/CD. Define a fundação sobre a qual todas as outras specs são construídas.

**Delivered by:** Spec 10 (project-bootstrap)

### V2: Multi-Tenant Architecture
Isolamento completo de dados entre colégios usando `TenantId` como coluna discriminadora em todas as tabelas tenant-scoped. Middleware que resolve o tenant a partir do JWT do usuário logado. EF Core global query filters garantem que queries nunca vazem dados entre tenants.

**Delivered by:** Spec 20 (multi-tenant)

### V3: Authentication & Authorization
Login com email/senha. ASP.NET Core Identity para gestão de credenciais. JWT access tokens (15min) + refresh tokens (7 dias) com rotação. Roles: Admin, Staff, Parent. Cada role tem permissões específicas por tenant. *(O Google OAuth foi removido no C8.)*

**Delivered by:** Spec 30 (auth)

### V4: Student Management
CRUD completo de alunos com dados pessoais, contatos, filiação (vínculo pais/responsáveis), e informações acadêmicas. Cada aluno pertence a um tenant. Busca e filtros por nome, turma, status de matrícula.

**Delivered by:** Spec 40 (student-management)

### V5: Enrollment System (Matrícula)
Workflow completo de matrícula: abertura de período de matrícula, inscrição de novos alunos, checklist de documentos obrigatórios, aprovação pela secretaria, e confirmação de matrícula. Estados: pendente, documentacao_pendente, aprovado, recusado, cancelado.

**Delivered by:** Spec 50 (enrollment)

### V6: Re-enrollment System (Rematrícula)
Workflow de rematrícula para alunos existentes: renovação automática ou manual, reaproveitamento de documentos já validados, notificação de documentos vencidos que precisam ser reenviados, e confirmação para o próximo ano letivo.

**Delivered by:** Spec 60 (reenrollment)

### V7: Document Management
Upload de documentos, categorização por tipo (RG, CPF, comprovante de residência, histórico escolar, etc.), verificação pela secretaria (aprovado/rejeitado), tracking de validade/vencimento, e notificação automática de documentos pendentes ou vencidos.

**Delivered by:** Spec 70 (documents)

### V8: Notification System
Sistema de notificações para comunicar gestores e pais. Canais: notificação in-app + email. Tipos: documentos pendentes, reuniões agendadas, comunicados gerais, lembretes de rematrícula. Tracking de leitura e histórico por usuário.

**Delivered by:** Spec 80 (notifications)

### V9: Parent Portal API
API do portal dos pais: dashboard com resumo dos filhos, visualização de desempenho/notas, inbox de notificações, upload de documentos pendentes, e acompanhamento de status de matrícula/rematrícula. Cada pai vê apenas seus filhos vinculados.

**Delivered by:** Spec 90 (parent-portal)

### V10: Frontend Application (React + PWA)
SPA React 19 + Vite 8 + Tailwind CSS 4 com suporte PWA (Workbox). Telas separadas para Admin/Staff (dashboard, gestão de alunos, matrículas, documentos, notificações) e Pais (dashboard dos filhos, documentos, notificações). Install prompt para mobile. Offline-first com cache strategy.

**Delivered by:** Spec 100 (frontend)

### V11: Auditoria de Operações
Trilha de auditoria imutável por tenant para toda operação sensível: aprovar, rejeitar ou cancelar matrícula; verificar documento; revogar sessão; alterar papel de usuário. Cada evento registra ator, tenant, ação, alvo, valores antes/depois, origem (IP e user-agent) e identificador de correlação. A consulta é restrita a Admin, com retenção configurável e exportação. É a base para reverter decisões com responsabilidade e para responder a auditorias escolares.

**Delivered by:** Spec 110 (auditoria-de-operacoes)

**Canonical creation path without chat memory:** `management/specs/110-auditoria-de-operacoes/` — `/spec-new 110` deve derivar: entidade `AuditEvent` tenant-scoped com ator, ação, alvo, valores antes/depois, IP, user-agent, correlação e timestamp; escrita transversal nas operações sensíveis (matrícula, documento, usuário, sessão); leitura paginada e filtrada restrita a Admin; política de retenção; migration; testes de cobertura por operação, isolamento por tenant e imutabilidade. Camadas: Core (entidade), Infrastructure (persistência e escrita), Api (consulta), testes. Decisão a registrar: prazo de retenção e base legal.

### V12: Ciclo de Vida de Identidade
Gestão completa de usuários por escola: convite de Admin/Staff, verificação de e-mail antes do primeiro login, troca de papel, desativação e reativação, revogação de sessões e poda de refresh tokens expirados. Hoje apenas o cadastro da escola cria um Admin e o e-mail nunca é verificado; com este valor a escola consegue operar com equipe real sem intervenção manual no banco.

**Delivered by:** Spec 120 (ciclo-de-vida-de-identidade)

**Canonical creation path without chat memory:** `management/specs/120-ciclo-de-vida-de-identidade/` — `/spec-new 120` deve derivar: convite de staff por token de uso único com hash e expiração; verificação de e-mail obrigatória antes do primeiro login; troca de papel e desativação/reativação restritas a Admin; revogação de todas as sessões ao desativar; poda de refresh tokens expirados; endpoints e tela de gestão de usuários; testes de permissão e ciclo de convite. Absorve os itens M11, B3 e B4 do plano de correções (retirar de lá ao criar a spec). Camadas: Core (campos de usuário), Infrastructure (Identity/Auth), Api, frontend e testes.

### V13: Observabilidade Operacional
Saúde real do sistema: probes de liveness e readiness cobrindo banco, armazenamento e fila de e-mail; identificador de correlação propagado da requisição até o log e o ProblemDetails; métricas de requisição, erro e profundidade da fila; alertas para falha de dependência. Substitui os probes manuais atuais e torna incidentes rastreáveis a partir do relato do usuário.

**Delivered by:** Spec 130 (observabilidade-operacional)

**Canonical creation path without chat memory:** `management/specs/130-observabilidade-operacional/` — `/spec-new 130` deve derivar: `AddHealthChecks` com verificações de banco, armazenamento e fila de e-mail; endpoints `/health/live` e `/health/ready`; middleware de correlação que gera e propaga id no header, no escopo de log e no ProblemDetails; métricas de requisição, erro e profundidade da fila; relatório de validação de startup; testes de transição de estado e de correlação. Sem tela nova. Decisão a registrar: destino das métricas (OTLP ou exporter).

### V14: Comunicação Proativa
Notificações disparadas por evento e por agendamento, não apenas sob ação manual: matrícula submetida, aprovada ou rejeitada; documento verificado ou rejeitado; documento a vencer; abertura e prazo de rematrícula. Inclui templates por tipo, envio in-app e e-mail por provedor real, idempotência e registro de entrega. Completa a promessa das specs 70 e 80.

**Delivered by:** Spec 140 (comunicacao-proativa)

**Canonical creation path without chat memory:** `management/specs/140-comunicacao-proativa/` — `/spec-new 140` deve derivar: host de jobs agendados com agenda persistida (decidir entre `BackgroundService` próprio e Hangfire/Quartz); gatilhos de evento para matrícula submetida/aprovada/rejeitada, documento verificado/rejeitado e documento a vencer; catálogo de templates por `NotificationType` e locale; `IEmailSender` real com configuração fail-closed; chave de idempotência por evento e destinatário; registro de entrega; testes por gatilho e supressão de duplicata. Resolve a decisão §6-3 do plano. Camadas: Core (eventos), Infrastructure (jobs/email), Api (sem endpoint novo obrigatório), frontend (inbox existente) e testes.

### V15: Estrutura Acadêmica e Desempenho
Modelo acadêmico explícito: turma/série como entidade (não string livre), ano letivo, componentes curriculares, avaliações e notas, com boletim por aluno no portal dos pais. Substitui o endpoint de notas que hoje devolve lista vazia e permite acompanhar desempenho de verdade.

**Delivered by:** Spec 150 (estrutura-academica-e-desempenho)

**Canonical creation path without chat memory:** `management/specs/150-estrutura-academica-e-desempenho/` — `/spec-new 150` deve derivar: entidades de turma/série, ano letivo, componente curricular, avaliação e nota; migração de `Student.Turma` e `Student.AnoLetivo` para referência com backfill; CRUD administrativo, lançamento de notas e boletim; substituição do `GetChildGradesAsync` que hoje devolve lista vazia; testes de autorização, cálculo e migração. Camadas: Core, Infrastructure, Api, frontend (admin e portal dos pais) e testes. Decisão a registrar: modelo de cálculo (média ponderada, recuperação).

### V16: Rematrícula em Lote
Renovação de matrícula assistida para um período: prévia do que será renovado, reaproveitamento de documentos válidos, lista de pendências por aluno, execução em lote idempotente e promoção de ano letivo. Elimina a operação aluno a aluno e cumpre a renovação automática ou manual prevista na V6.

**Delivered by:** Spec 160 (rematricula-em-lote)

**Canonical creation path without chat memory:** `management/specs/160-rematricula-em-lote/` — `/spec-new 160` deve derivar: consulta de prévia (elegíveis, documentos reaproveitáveis, vencidos e tipos pendentes); criação em lote idempotente por aluno e período; relatório de falhas parciais; regra de promoção de ano; integração com a auditoria (110); tela administrativa de prévia e confirmação; testes de idempotência e de bloqueio documental. Depende de 110 e 150. Camadas: Core, Infrastructure, Api, frontend e testes.

### V17: Armazenamento de Documentos em Escala
Armazenamento plugável (disco local em desenvolvimento, S3-compatível em produção) com URL assinada, streaming sem carregar o arquivo inteiro em memória, checksum, verificação de tipo real, quarentena de arquivos suspeitos, varredura de órfãos e cota por escola. Prepara o volume real de documentos sem perder o controle de acesso.

**Delivered by:** Spec 170 (armazenamento-de-documentos)

**Canonical creation path without chat memory:** `management/specs/170-armazenamento-de-documentos/` — `/spec-new 170` deve derivar: extensão do contrato `IFileStorage` (put/get/delete/exists/URL assinada); driver local preservando a criptografia atual e driver S3-compatível; upload e download em streaming com limite de tamanho; checksum SHA-256 e verificação de tipo real; quarentena de arquivos suspeitos; varredura de órfãos; cota por tenant; novos campos em `Document` (tamanho, hash, MIME, autor); migration; testes por driver e por falha. Decisão a registrar: provedor S3. Camadas: Core, Infrastructure, Api, frontend (download) e testes.

### V18: Qualidade Automatizada
Pipeline de integração contínua que compila, testa e faz lint em cada alteração, com testes de serviço para as regras de negócio, teste ponta a ponta do fluxo crítico (matrícula → documentos → aprovação) e testes de frontend. Fecha a lacuna de verificação que hoje depende de execução manual.

**Delivered by:** Spec 180 (qualidade-automatizada)

**Canonical creation path without chat memory:** `management/specs/180-qualidade-automatizada/` — `/spec-new 180` deve derivar: workflow de CI (restore, build, testes, lint e build do frontend) com SDK fixado; fixtures de teste de serviço; teste ponta a ponta do fluxo matrícula → documentos → aprovação; infraestrutura de teste de frontend (Vitest + Testing Library) cobrindo o módulo de domínio e as telas críticas; limiares de cobertura; comportamento em falha, timeout e execução ignorada. Absorve T1 e M8 do plano de correções (retirar de lá ao criar a spec). Camadas: infraestrutura de CI e testes; sem mudança de produto.

### V19: Experiência Internacionalizada e Acessível
Interface com idioma, formato de data e número e mensagens por locale (BR/US/PT já previstos na configuração de países), estados explícitos de carregamento e erro em todas as telas, navegação por teclado, rótulos acessíveis e error boundary global. Elimina textos e formatos fixos em pt-BR.

**Delivered by:** Spec 190 (experiencia-i18n-e-acessibilidade)

**Canonical creation path without chat memory:** `management/specs/190-experiencia-i18n-e-acessibilidade/` — `/spec-new 190` deve derivar: fonte de locale (país do tenant + preferência do usuário); catálogo de mensagens PT/EN/ES; formatação de data e número via `Intl`; mapa único de código de erro da API para mensagem; estados de carregamento, erro e vazio em todas as telas; error boundary global; checklist de acessibilidade (rótulos, foco, contraste, teclado); testes de formatação e de mapa de erro. Absorve parte de A8 e A10. Camadas: frontend e testes; Api apenas expõe códigos estáveis.

### V20: Configuração e Limites por Escola
Cada tenant passa a ter configuração própria: identidade visual (nome de exibição, cores, logotipo), fuso horário, ano letivo padrão, cota de armazenamento e de usuários, e flags de funcionalidades por plano. Permite operar várias escolas com políticas distintas sem código específico.

**Delivered by:** Spec 200 (configuracao-e-limites-por-tenant)

**Canonical creation path without chat memory:** `management/specs/200-configuracao-e-limites-por-tenant/` — `/spec-new 200` deve derivar: entidade de configuração por tenant (nome de exibição, logotipo, cores, fuso, ano letivo padrão, locale, flags); cotas de armazenamento e de usuários aplicadas no serviço dono; API e tela administrativa; cache com invalidação; testes de isolamento e de limite. Camadas: Core, Infrastructure, Api, frontend e testes. Decisão a registrar: modelo de planos e flags.

### V21: Privacidade e LGPD
Direitos do titular e ciclo de vida de dados pessoais: registro de consentimento, exportação dos dados do aluno e do responsável, eliminação ou anonimização com retenção definida, e trilha de quem acessou dados sensíveis. Necessário para tratar dados de menores em conformidade com a LGPD.

**Delivered by:** Spec 210 (privacidade-lgpd)

**Canonical creation path without chat memory:** `management/specs/210-privacidade-lgpd/` — `/spec-new 210` deve derivar: registro de consentimento por responsável e aluno; exportação dos dados do titular em arquivo estruturado; fluxo de eliminação/anonimização com retenção e bloqueio legal; trilha de acesso a dados sensíveis reutilizando a spec 110; API, tela administrativa e fluxo de solicitação do responsável; testes de completude da exportação e de irreversibilidade da anonimização. Depende de 110. Decisões a registrar: prazos de retenção e base legal por tipo de dado.
