import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

/**
 * A tela de login foi redesenhada a partir de um layout estático. O risco dessa origem é
 * conhecido: o mockup traz elementos que PARECEM funcionar (abas de perfil, status
 * "operacional", métricas do colégio, botão de outro provedor) e o resultado fica com
 * controles mortos — o usuário clica e nada acontece, ou pior, a tela exibe número
 * inventado como se fosse dado do colégio.
 *
 * Este teste guarda o que o redesenho não pode perder:
 *
 * 1. as três portas de entrada reais (senha, cadastro de colégio, cadastro de responsável);
 * 2. nenhum link/controle decorativo sem destino;
 * 3. nenhuma métrica fabricada — a tela não consulta dado nenhum;
 * 4. cada campo com `label` ligado e cada botão só de ícone nomeado;
 * 5. a fonte da marca carregada de verdade (CSS sem `@font-face` não avisa que falhou).
 */

const root = new URL('..', import.meta.url);
const AUTH_PAGE = 'src/pages/AuthPage.tsx';
/** Na tela de entrada, o conteúdo vem da rota — e não de um estado local. */
const AUTH_SCREEN = { AUTH_PAGE, APP: 'src/App.tsx' };

function readText(path: string): Promise<string> {
  return readFile(new URL(path, root), 'utf8');
}

test('a tela de entrada mantém as portas de entrada reais', async () => {
  const source = await readText(AUTH_PAGE);

  assert.match(source, /to="\/forgot-password"/, 'a rota /forgot-password desapareceu da tela');

  // Os dois cadastros são servidos NO PAINEL, e a troca muda a URL (rota, não estado local).
  assert.match(source, /import \{ SchoolRegisterForm \}/, 'a tela deixou de usar o cadastro de colégio compartilhado');
  assert.match(source, /import \{ ParentRegisterForm \}/, 'a tela deixou de usar o cadastro de responsável compartilhado');
  assert.match(source, /navigate\(MODE_ROUTES\.register\)/, 'o botão de cadastrar colégio não troca mais o painel');
  assert.match(source, /navigate\(MODE_ROUTES\.parent\)/, 'o botão de conta de responsável não troca mais o painel');
  assert.match(source, /navigate\(MODE_ROUTES\.login\)/, 'não há caminho de volta do cadastro para o login');

  // O login por senha e o Google continuam sendo os mesmos caminhos de antes.
  assert.match(source, /await login\(email, password, remember\)/, 'o envio do formulário perdeu a chamada de login');
  assert.match(source, /\/api\/auth\/google/, 'o login com Google perdeu o destino');
  assert.match(source, /DEMO_ACCOUNTS\.admin\.email/, 'a entrada de demonstração do admin desapareceu');
  assert.match(source, /DEMO_ACCOUNTS\.parent\.email/, 'a entrada de demonstração do responsável desapareceu');
});

test('depois do login a sidebar do desktop abre aberta', async () => {
  const context = await readText('src/context/AuthContext.tsx');
  const sidebar = await readText('src/components/Sidebar.tsx');
  const marks = await readText('src/utils/sessionMarks.ts');

  // O login grava a marca de sessão nova...
  assert.match(context, /markJustLoggedIn\(\)/, 'o login deixou de marcar a sessão nova');
  const login = context.match(/const login = async[\s\S]*?\n  \};/)?.[0] ?? '';
  assert.match(login, /markJustLoggedIn\(\)/, 'a marca precisa ser gravada no login bem-sucedido, não em outro lugar');

  // ...a sidebar lê no inicializador (senão ela abriria só depois de um quadro piscando)...
  assert.match(
    sidebar,
    /useState\(\(\) => !wasJustLoggedIn\(\)\)/,
    'a sidebar voltou a nascer sempre recolhida',
  );

  // ...e apaga a marca uma única vez, para não reabrir sozinha a cada remontagem.
  assert.match(sidebar, /clearJustLoggedIn\(\)/, 'a marca nunca é apagada e a sidebar reabriria sozinha');
  assert.match(marks, /sessionStorage/, 'a marca precisa sobreviver à navegação — sessionStorage, não memória');
});

test('"lembrar o acesso" é enviado ao servidor, não é só um quadradinho', async () => {
  const page = await readText(AUTH_PAGE);
  const api = await readText('src/api/auth.ts');
  const context = await readText('src/context/AuthContext.tsx');

  // O campo precisa estar ligado ao rótulo e controlado.
  assert.match(page, /id="login-remember"/, 'o campo de lembrar perdeu o id');
  assert.match(page, /htmlFor="login-remember"/, 'o rótulo do lembrar não está ligado ao campo');
  assert.match(page, /type="checkbox"/, 'o lembrar deixou de ser um checkbox');
  assert.match(page, /const \[remember, setRemember\] = useState\(/, 'o estado do lembrar desapareceu');

  // A escolha viaja até o corpo da requisição: sem isso o servidor não tem como mudar o
  // cookie, e a opção vira decoração.
  assert.match(api, /rememberMe/, 'a chamada de login não envia `rememberMe`');
  assert.match(api, /api\.post\('\/api\/auth\/login', \{ email, password, rememberMe \}\)/, 'o corpo do login perdeu o `rememberMe`');
  assert.match(context, /authApi\.login\(email, password, rememberMe\)/, 'o contexto descarta a escolha de lembrar');
});

test('o lembrar é um círculo com o gradiente do botão de entrar', async () => {
  const page = await readText(AUTH_PAGE);

  // O desenho é um <span> (o input nativo é escondido com `sr-only`): só assim o controle
  // pode ser redondo com gradiente, mantendo teclado e leitor de tela no input.
  assert.match(page, /className="peer sr-only"/, 'o input do lembrar deixou de ser o controle real');
  const desenho = page.match(/<span\s+aria-hidden="true"[\s\S]*?className="([^"]+)"/)?.[1] ?? '';
  assert.match(desenho, /rounded-full/, 'o lembrar voltou a ser quadrado');
  assert.match(desenho, /peer-checked:bg-gradient-to-r/, 'o lembrar perdeu o gradiente do botão');
  assert.match(desenho, /peer-checked:from-indigo-600/, 'o lembrar perdeu o indigo-600 do botão');
  assert.match(desenho, /peer-checked:to-indigo-700/, 'o lembrar perdeu o indigo-700 do botão');
  assert.match(desenho, /peer-focus-visible:ring-4/, 'o lembrar ficou sem anel de foco visível');
});

test('a URL acompanha o painel: uma tela serve os três endereços', async () => {
  const page = await readText(AUTH_SCREEN.AUTH_PAGE);
  const app = await readText(AUTH_SCREEN.APP);

  // O modo é DERIVADO da rota; um `useState` de modo deixaria a URL mentindo.
  assert.match(page, /const mode = modeFromPath\(pathname\)/, 'o conteúdo do painel deixou de vir da rota');
  assert.doesNotMatch(page, /useState<PanelMode>/, 'o modo voltou a ser estado local, e a URL para de acompanhar');

  for (const route of ['/login', '/register', '/register/parent']) {
    assert.match(
      app,
      new RegExp(`<Route path="${route}" element=\\{[^}]*<AuthPage`),
      `a rota ${route} não renderiza a tela de entrada`,
    );
  }
});

test('a aba do navegador nomeia as três rotas de entrada', async () => {
  const titles = await readText('src/components/DocumentTitle.tsx');

  // Sem regra, o título cai em "Ciclo" sozinho e a aba deixa de dizer onde a pessoa está.
  const expected: Array<[string, string]> = [
    ['/login', 'Login'],
    ['/register', 'Cadastro de colégio'],
    ['/register/parent', 'Conta de responsável'],
  ];

  for (const [prefix, title] of expected) {
    assert.match(
      titles,
      new RegExp(`\\{ prefix: '${prefix}', title: '${title}' \\}`),
      `a rota ${prefix} perdeu o nome "${title}" na aba`,
    );
  }

  // O casamento é por prefixo: com `/register` antes, a rota de responsável herdaria o nome
  // do colégio. A ordem destas duas linhas é o contrato.
  assert.ok(
    titles.indexOf("prefix: '/register/parent'") < titles.indexOf("prefix: '/register'"),
    'a regra de `/register` precisa vir DEPOIS da de `/register/parent`',
  );

  // O nome na aba é o mesmo ao lado da logo; divergir faria a tela dizer uma coisa e a aba outra.
  const page = await readText(AUTH_PAGE);
  for (const title of ['Cadastro de colégio', 'Conta de responsável']) {
    assert.match(page, new RegExp(`brand: '${title}'`), `a marca "${title}" saiu do painel`);
  }
});

test('a abertura cobre as três rotas, e não o retorno do Google', async () => {
  const app = await readText(AUTH_SCREEN.APP);

  for (const route of ['/login', '/register', '/register/parent']) {
    assert.match(
      app,
      new RegExp(`<Route path="${route}" element=\\{<SplashGate>`),
      `a rota ${route} ficou fora da abertura — quem chega direto por ela não veria a splash`,
    );
  }

  // O callback do Google carrega o token na URL: uma tela para dispensar antes atrapalharia.
  assert.match(
    app,
    /<Route path="\/auth\/google\/callback" element=\{<AuthPage \/>\} \/>/,
    'o retorno do Google passou a exigir dispensa da splash',
  );
});

test('a marca ao lado da logo muda junto com o painel', async () => {
  const page = await readText(AUTH_SCREEN.AUTH_PAGE);

  // Um texto fixo ao lado do favicon faria a tela dizer "login" enquanto mostra cadastro.
  assert.doesNotMatch(
    page,
    /<span className="text-xs font-semibold text-slate-600">Sua Plataforma Educacional<\/span>/,
    'a marca ao lado da logo ficou fixa em vez de acompanhar o modo',
  );
  assert.match(page, /\{copy\.brand\}/, 'a marca ao lado da logo deixou de vir do conteúdo do modo');
  for (const brand of ['Sua Plataforma Educacional', 'Cadastro de colégio', 'Conta de responsável']) {
    assert.match(page, new RegExp(`brand: '${brand}'`), `a marca "${brand}" saiu da tabela de conteúdo`);
  }
});

test('o cadastro de colégio tem um dono só, usado pela rota e pelo painel de login', async () => {
  const form = await readText('src/components/SchoolRegisterForm.tsx');

  // A chamada de API, os campos e as máscaras por país vivem no componente, não na página.
  assert.match(form, /await registerSchool\(/, 'o formulário compartilhado perdeu a chamada de cadastro');
  assert.match(form, /applyMask\(/, 'as máscaras por país saíram do formulário compartilhado');
  assert.match(form, /passwordError\(/, 'a validação de senha saiu do formulário compartilhado');

  const login = await readText(AUTH_PAGE);
  assert.doesNotMatch(
    login,
    /Nome do colégio|registerSchool/,
    'a tela de login recriou o formulário de colégio em vez de hospedar o componente',
  );

  const page = await readText(AUTH_PAGE);
  assert.match(
    page,
    /<SchoolRegisterForm onSuccess=/,
    'a tela de entrada deixou de hospedar o cadastro de colégio',
  );
});

test('o cadastro de responsável tem um dono só, usado pela rota e pelo painel de login', async () => {
  const form = await readText('src/components/ParentRegisterForm.tsx');

  // A API, a criação de sessão e a validação de senha vivem no componente.
  assert.match(form, /await registerParent\(/, 'o formulário compartilhado perdeu a chamada de cadastro');
  assert.match(form, /setAccessToken\(/, 'o formulário compartilhado perdeu a criação de sessão');
  assert.match(form, /passwordError\(/, 'a validação de senha saiu do formulário compartilhado');

  const login = await readText(AUTH_PAGE);
  assert.doesNotMatch(
    login,
    /registerParent|schoolSlug/,
    'a tela de login recriou o cadastro de responsável em vez de hospedar o componente',
  );

  const page = await readText(AUTH_PAGE);
  assert.match(
    page,
    /<ParentRegisterForm \/>/,
    'a tela de entrada deixou de hospedar o cadastro de responsável',
  );
});

test('nenhum controle decorativo sem destino na tela de login', async () => {
  const source = await readText(AUTH_PAGE);

  // Link para âncora vazia é o resíduo mais comum de layout estático: parece acionável e
  // não leva a lugar nenhum.
  assert.doesNotMatch(source, /href="#/, 'a tela tem link de âncora vazia (`href="#"`)');
  assert.doesNotMatch(source, /href=""/, 'a tela tem link sem destino');

  // Todo `<button>` é `submit`, tem `onClick` ou é o de mostrar a senha — nada de botão
  // puramente visual.
  const buttons = source.match(/<button\b[\s\S]*?>/g) ?? [];
  assert.ok(buttons.length >= 3, 'a tela perdeu botões esperados');

  for (const tag of buttons) {
    const wired = /type="submit"|onClick=/.test(tag);
    assert.ok(wired, `botão sem ação: ${tag.replace(/\s+/g, ' ').slice(0, 90)}`);
  }
});

test('a tela de login não exibe métrica fabricada', async () => {
  const source = await readText(AUTH_PAGE);

  // A tela não consulta a API: qualquer percentual ou contagem grande ali é invenção.
  assert.doesNotMatch(source, /\d+\s*%/, 'a tela mostra percentual — não há dado de colégio para exibir');
  assert.doesNotMatch(
    source,
    /\b\d{2,}\s+(matrículas|alunos|retenção)/i,
    'a tela mostra contagem de alunos/matrículas sem consultar dado nenhum',
  );
});

test('campos e botões de ícone são acessíveis', async () => {
  const source = await readText(AUTH_PAGE);

  assert.match(source, /htmlFor="login-email"/, 'o rótulo do e-mail não está ligado ao campo');
  assert.match(source, /id="login-email"/, 'o campo de e-mail perdeu o id');
  assert.match(source, /htmlFor="login-password"/, 'o rótulo da senha não está ligado ao campo');
  assert.match(source, /id="login-password"/, 'o campo de senha perdeu o id');

  // O botão de mostrar a senha é só ícone: sem `aria-label` ele é mudo para leitor de tela.
  assert.match(source, /aria-label=\{showPassword \? 'Ocultar senha' : 'Mostrar senha'\}/);
  assert.match(source, /autoComplete="email"/, 'o campo de e-mail perdeu o autopreenchimento');
  assert.match(source, /autoComplete="current-password"/, 'o campo de senha perdeu o autopreenchimento');
});

test('a fonte da marca é carregada de fato e vale só onde é aplicada', async () => {
  const css = await readText('src/index.css');
  assert.match(
    css,
    /--font-brand:\s*'Plus Jakarta Sans'/,
    'o token `--font-brand` desapareceu do tema',
  );
  // Trocar `--font-sans` mudaria a fonte de TODA a aplicação; o token próprio não.
  assert.doesNotMatch(css, /--font-sans:\s*'Plus Jakarta Sans'/, 'a fonte da marca não deve trocar a fonte global');

  const html = await readText('index.html');
  assert.match(html, /fonts\.googleapis\.com\/css2\?family=Plus\+Jakarta\+Sans/, 'a folha da fonte não é carregada');

  const login = await readText(AUTH_PAGE);
  assert.match(login, /font-brand/, 'a tela de login não aplica o token da fonte');
});

test('as superfícies de vidro da tela existem no CSS', async () => {
  const css = await readText('src/index.css');
  const login = await readText(AUTH_PAGE);

  for (const name of ['glass-card', 'glass-pill']) {
    if (login.includes(name)) {
      assert.match(css, new RegExp(`\\.${name}\\s*\\{`), `a classe \`${name}\` não existe no CSS`);
    }
  }

  // Movimento decorativo: com "menos movimento" ligado, os cards não flutuam.
  assert.match(
    css,
    /prefers-reduced-motion: reduce[\s\S]*\.animate-float/,
    'a flutuação dos cards ignora `prefers-reduced-motion`',
  );
});

test('os cards que flutuam não filtram o fundo (é o que borra o texto em movimento)', async () => {
  const css = await readText('src/index.css');

  const card = css.match(/\.glass-card\s*\{[^}]*\}/)?.[0];
  assert.ok(card, 'regra `.glass-card` ausente no CSS');
  assert.doesNotMatch(
    card,
    /backdrop-filter/,
    'o card voltou a filtrar o fundo: com `transform` animado o Chrome rasteriza o texto em outra camada e ele aparece borrado durante a flutuação',
  );

  // A pill do cabeçalho fica parada — ali o filtro não causa o defeito e pode continuar.
  const pill = css.match(/\.glass-pill\s*\{[^}]*\}/)?.[0];
  assert.match(pill, /backdrop-filter/, 'a pill estática perdeu o desfoque sem motivo');

  // `translate3d` promove o card à própria camada: o texto viaja pronto em vez de ser
  // redesenhado a cada quadro.
  const keyframes = css.match(/@keyframes float-slow\s*\{[\s\S]*?\n\}/)?.[0];
  assert.ok(keyframes, 'keyframe `float-slow` ausente');
  assert.match(keyframes, /translate3d/, 'a flutuação voltou a `translateY` (sem camada própria)');

  // O `translate3d` sozinho não basta: o minificador do build o reduz a `translateY` e a
  // camada se perde. `will-change` é o que sobrevive ao build e garante a promoção.
  // A mesma classe reaparece em `prefers-reduced-motion` (`animation: none`), então basta
  // que UMA das regras declare a promoção.
  for (const cls of ['animate-float', 'animate-float-delayed']) {
    const rules = [...css.matchAll(new RegExp(`\\.${cls}\\s*\\{[^}]*\\}`, 'g'))].map((m) => m[0]);
    assert.ok(rules.length > 0, `regra \`.${cls}\` ausente`);
    assert.ok(
      rules.some((rule) => /will-change:\s*transform/.test(rule)),
      `\`.${cls}\` sem \`will-change\` — o texto volta a ser redesenhado a cada quadro`,
    );
  }
});
