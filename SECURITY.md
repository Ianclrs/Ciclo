# Política de Segurança

O Ciclo é uma plataforma multi-tenant de gestão escolar e trata dados pessoais de menores de idade.
Falhas de segurança são tratadas como prioridade máxima e devem ser relatadas de forma privada.

**Não abra issue pública, pull request ou comentário para relatar uma vulnerabilidade.**

---

## Versões suportadas

O projeto é distribuído pelo próprio repositório, sem releases versionadas. A única linha suportada é a
`main`, sempre publicada após passar pelo CI.

| Linha | Suporte |
|---|---|
| `main` | Recebe correções de segurança |
| Branches de trabalho e forks | Sem suporte |

---

## Como relatar

| Canal | Uso |
|---|---|
| **GitHub Security Advisories** — aba *Security* → *Report a vulnerability* | Preferencial: o relato fica privado e vinculado ao repositório |
| **ianclrso@gmail.com** | Alternativa por e-mail, com assunto `[SEGURANÇA] Ciclo` |

Inclua no relato:

1. Descrição da falha e do impacto observado.
2. Passos para reproduzir (versão/commit, rota, payload).
3. Prova de conceito mínima, quando possível.
4. Se há dado pessoal exposto — isso eleva a severidade e aciona o tratamento de incidente.
5. Sugestão de correção, se você já tiver uma.

### O que esperar

| Etapa | Prazo |
|---|---|
| Confirmação de recebimento | até 5 dias úteis |
| Triagem inicial e severidade | até 10 dias úteis |
| Correção | depende da severidade e é combinada no relato |

Não há recompensa financeira por relatos. O reconhecimento no aviso de segurança é opcional e
depende do seu consentimento.

---

## Escopo

**Dentro do escopo:** API (`src/`), SPA/PWA (`frontend/`), autenticação e sessão, isolamento entre
escolas, upload/download e armazenamento de documentos, endpoints de privacidade (LGPD), configuração
de deploy e CI do repositório.

**Fora do escopo:**

- O modo demonstração do frontend: os dados são fictícios e ficam no `localStorage` do navegador.
- Ambiente local de desenvolvimento, seeds e massas de teste.
- Vulnerabilidades em dependências de terceiros (NuGet/npm) sem impacto demonstrável neste código —
  relate upstream; se houver caminho de exploração aqui, o relato é bem-vindo.
- Ataques de negação de serviço volumétricos e resultado de scanner automatizado sem prova de conceito.
- Engenharia social, phishing e acesso físico.
- Configuração e infraestrutura do host de deploy (TLS, firewall, proxy), que não pertencem ao código.

---

## Medidas implementadas

Referência para quem for avaliar o sistema: estes controles existem hoje no código ou no pipeline,
cobertos por teste automatizado ou portão de build.

| Área | Controle |
|---|---|
| **Segredos** | `Jwt:Secret` e `FileStorage:EncryptionKey` não têm padrão: a API **não inicia** sem eles. `.env` e o diretório de configuração local da ferramenta são ignorados pelo git |
| **Senhas** | Mínimo de 8 caracteres, com dígito e caractere não alfanumérico (ASP.NET Core Identity) |
| **Bloqueio de conta** | 5 tentativas falhas → 30 minutos de bloqueio |
| **Sessão** | Access token JWT de 15 minutos; refresh token de 7 dias em cookie `HttpOnly`, `Secure` e `SameSite=Strict`; revogação de sessão registrada em auditoria |
| **Autorização** | Política padrão exige usuário autenticado; acesso por papel (`Admin`, `Staff`, `Parent`, `Student`) por endpoint |
| **Limite de requisições** | Por origem: 30/min nos endpoints de autenticação, 5/h no cadastro de escola, 120/min no download de documento — resposta `429` com `Retry-After` |
| **Isolamento entre escolas** | `tenant_id` vem do token, vira contexto da requisição e é aplicado por filtro global de consulta no EF Core; sem tenant resolvido, endpoint autenticado responde `401` |
| **Documentos** | Criptografia em repouso com rotação de chave (`PreviousEncryptionKeys`), checksum e tamanho conferidos na gravação, validação de assinatura de arquivo, quarentena de conteúdo inválido, cota por escola e URL assinada de 5 minutos |
| **Cadeia de suprimentos** | Versões de pacote fixas (versão flutuante quebra o build), auditoria do NuGet no restore e todo aviso tratado como erro |
| **CI** | Restore, build, testes, lint e portão de cobertura de 60% nos dois lados; teste marcado como ignorado reprova o job |
| **Auditoria** | Trilha por escola, imutável, de aprovações, rejeições, cancelamentos e revogações de sessão; consulta e exportação CSV restritas ao Admin |
| **LGPD** | Consentimento por finalidade e base legal, solicitações de titular, exportação, anonimização com bloqueio legal (*legal hold*) |
| **Observabilidade** | `X-Correlation-Id` em toda resposta, erros em ProblemDetails sem detalhe interno e `/metrics` sem dado pessoal |

> A API deve ser publicada atrás de HTTPS. Terminação TLS, cabeçalhos de segurança do host e firewall
> são responsabilidade da infraestrutura de deploy, não do código da aplicação.

---

## Ao contribuir

- Nunca versione segredo, token, chave, dump de banco ou arquivo de upload real.
- Se um segredo for exposto em commit, histórico, log ou PR: **rotacione antes de qualquer outra ação**
  e trate como incidente. Remover o arquivo do commit não basta — o histórico permanece.
- Não enfraqueça os portões do repositório (`TreatWarningsAsErrors`, auditoria do NuGet, portão de
  cobertura, proibição de versão flutuante) para fazer um build passar.
- Rode os gates antes de abrir PR: `dotnet test` e, em `frontend/`, `npm test`, `npm run lint` e
  `npm run build`.
- Ao alterar autenticação, autorização, isolamento por escola ou criptografia, inclua teste de regressão.

---

## Divulgação coordenada

Pedimos que o relato permaneça privado até a correção ser publicada. O prazo alvo é de até **90 dias**
entre o relato e a divulgação pública. Se a correção demorar mais, avisaremos e combinaremos novo prazo
com quem relatou.
