/**
 * Sonda manual (não roda na suíte): abre as telas da Spec 150 no Chrome real.
 *
 * Uso:
 *   1. API em `http://localhost:5264` (Development) e `npm run dev` (proxy => 5264)
 *   2. chrome --headless=new --remote-debugging-port=9444 about:blank
 *   3. `node tests/manual/academic-routes-probe.mjs http://localhost:5173`
 *
 * Mede o que o teste estático não alcança: as rotas montam, o roster da turma aparece, o
 * formulário recusa nota fora de 0 a 10, a gravação de uma nota válida conclui e o boletim do
 * responsável exibe média final e situação.
 */
const APP = process.argv[2] ?? 'http://localhost:5173';
const BASE = 'http://127.0.0.1:9444';

const ADMIN = { email: 'admin@colegiomodelo.com', password: 'Admin@123' };
const PARENT = { email: 'paulo.silva@email.com', password: 'Admin@123' };

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function connect() {
  const list = await (await fetch(`${BASE}/json/list`)).json();
  const page = list.find((t) => t.type === 'page');
  if (!page) throw new Error('nenhuma aba disponível no Chrome de depuração');

  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    ws.onopen = resolve;
    ws.onerror = reject;
  });

  let id = 0;
  const pending = new Map();
  ws.onmessage = (event) => {
    const message = JSON.parse(event.data);
    const resolve = pending.get(message.id);
    if (resolve) {
      pending.delete(message.id);
      resolve(message);
    }
  };

  const send = (method, params = {}) =>
    new Promise((resolve) => {
      const mid = ++id;
      pending.set(mid, resolve);
      ws.send(JSON.stringify({ id: mid, method, params }));
    });

  return { send, close: () => ws.close() };
}

const HELPERS = `
  const setValue = (el, value) => {
    const setter = Object.getOwnPropertyDescriptor(el.constructor.prototype, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const byText = (selector, text) =>
    [...document.querySelectorAll(selector)].find((el) => el.textContent.trim().includes(text));
`;

async function evalJs(cdp, expression) {
  const result = await cdp.send('Runtime.evaluate', {
    expression: `(async () => { ${HELPERS} ${expression} })()`,
    awaitPromise: true,
    returnByValue: true,
  });
  if (result.result?.exceptionDetails) {
    throw new Error(result.result.exceptionDetails.exception?.description ?? 'falha no evaluate');
  }
  return result.result?.result?.value;
}

async function goto(cdp, path) {
  await cdp.send('Page.navigate', { url: `${APP}${path}` });
  await sleep(1400);
}

async function login(cdp, { email, password }) {
  // Sessão anterior sobrevive no perfil do Chrome (cookie de refresh): sem limpar, o `/login`
  // redireciona para o painel do usuário anterior e a sonda mede a tela errada.
  await cdp.send('Network.clearBrowserCookies');
  await goto(cdp, '/login');
  // O `SplashGate` aparece uma vez por sessão e cobre o formulário até uma tecla ou toque.
  await evalJs(
    cdp,
    `
    if (!document.querySelector('input[type="email"]')) {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      await new Promise((r) => setTimeout(r, 900));
    }
    return 'splash dispensada';
  `,
  );
  const status = await evalJs(
    cdp,
    `
    const email = document.querySelector('input[type="email"]');
    const pass = document.querySelector('input[type="password"]');
    if (!email || !pass) return 'formulário não renderizou';
    setValue(email, ${JSON.stringify(email)});
    setValue(pass, ${JSON.stringify(password)});
    const button = byText('button', 'Entrar na plataforma');
    if (!button) return 'botão de entrada não encontrado';
    button.click();
    return 'enviado';
  `,
  );
  await sleep(2600);
  return { status, pathname: await evalJs(cdp, 'return location.pathname;') };
}

async function academicScreen(cdp) {
  await goto(cdp, '/admin/academico');
  return evalJs(
    cdp,
    `
    const cards = [...document.querySelectorAll('h3, [class*="font-semibold"]')]
      .map((el) => el.textContent.trim())
      .filter((text) => text.length > 0 && text.length < 60);
    return {
      pathname: location.pathname,
      heading: document.querySelector('h2')?.textContent ?? null,
      sections: ['Anos letivos', 'Turmas', 'Componentes curriculares', 'Avaliações']
        .filter((title) => document.body.innerText.includes(title)),
      hasComponent: document.body.innerText.includes('Matemática'),
      hasAssessment: document.body.innerText.includes('Prova 1'),
      hasGradesLink: Boolean(document.body.innerText.includes('Lançar notas')),
      cards: cards.slice(0, 6),
    };
  `,
  );
}

async function gradeEntryScreen(cdp) {
  await goto(cdp, '/admin/notas');
  return evalJs(
    cdp,
    `
    const rows = [...document.querySelectorAll('tbody tr')];
    const selects = [...document.querySelectorAll('select')];
    const assessmentSelect = selects[1];
    const firstInput = rows[0]?.querySelector('input');
    return {
      pathname: location.pathname,
      heading: document.querySelector('h2')?.textContent ?? null,
      classValue: selects[0]?.selectedOptions[0]?.textContent ?? null,
      assessmentValue: assessmentSelect?.selectedOptions[0]?.textContent ?? null,
      rosterCount: rows.length,
      firstStudent: rows[0]?.querySelector('td')?.textContent?.trim() ?? null,
      hasGradeInput: Boolean(firstInput),
    };
  `,
  );
}

async function tryGrade(cdp, value) {
  return evalJs(
    cdp,
    `
    const rows = [...document.querySelectorAll('tbody tr')];
    const input = rows[0].querySelector('input');
    setValue(input, ${JSON.stringify(String(value))});
    const button = rows[0].querySelector('button');
    button.click();
    await new Promise((r) => setTimeout(r, 900));
    return {
      fieldError: rows[0].querySelector('p')?.textContent?.trim() ?? null,
      toast: document.querySelector('[role="status"]')?.textContent?.trim()
        ?? document.body.innerText.match(/Nota de .* salva\\./)?.[0]
        ?? null,
      value: input.value,
    };
  `,
  );
}

async function emptyState(cdp) {
  await goto(cdp, '/admin/notas');
  return evalJs(
    cdp,
    `
    const select = document.querySelectorAll('select')[0];
    if (!select) return { error: 'seletor de turma ausente' };
    // 2º Ano B não tem avaliação cadastrada: é o caminho do estado vazio, não do erro.
    const option = [...select.options].find((o) => o.textContent.includes('2º Ano B'));
    if (!option) return { error: 'turma sem avaliações não encontrada' };
    setValue(select, option.value);
    await new Promise((r) => setTimeout(r, 1400));
    return {
      classValue: document.querySelectorAll('select')[0].selectedOptions[0].textContent.trim(),
      showsEmptyMessage: document.body.innerText.includes('Esta turma não tem avaliações'),
      showsError: Boolean(document.querySelector('[role="alert"]')),
      rows: document.querySelectorAll('tbody tr').length,
    };
  `,
  );
}

async function parentReport(cdp, childName) {
  await goto(cdp, '/parent');
  const opened = await evalJs(
    cdp,
    `
    // A aluna com nota lançada: o primeiro cartão pode ser um irmão sem nota.
    const links = [...document.querySelectorAll('a')]
      .filter((a) => a.getAttribute('href')?.startsWith('/parent/children/'));
    const link = links.find((a) => a.textContent.includes(${JSON.stringify(childName)})) ?? links[0];
    link.click();
    await new Promise((r) => setTimeout(r, 1200));
    const tab = byText('button', 'Notas');
    if (!tab) return { pathname: location.pathname, error: 'aba de notas ausente' };
    tab.click();
    await new Promise((r) => setTimeout(r, 1200));
    const rows = [...document.querySelectorAll('tbody tr')];
    return {
      pathname: location.pathname,
      headers: [...document.querySelectorAll('thead th')].map((th) => th.textContent.trim()),
      firstRow: rows[0] ? [...rows[0].children].map((td) => td.textContent.trim()) : null,
      rowCount: rows.length,
      emptyMessage: document.body.innerText.includes('Nenhuma nota disponível.'),
    };
  `,
  );
  return opened;
}

const cdp = await connect();
try {
  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');

  const adminPath = await login(cdp, ADMIN);
  console.log('login admin ->', adminPath);

  console.log('tela acadêmica:', JSON.stringify(await academicScreen(cdp), null, 1));
  console.log('tela de notas:', JSON.stringify(await gradeEntryScreen(cdp), null, 1));
  console.log('nota 11 (fora da faixa):', JSON.stringify(await tryGrade(cdp, '11'), null, 1));
  console.log('nota 7,5 (válida):', JSON.stringify(await tryGrade(cdp, '7,5'), null, 1));
  console.log('estado vazio (turma sem avaliações):', JSON.stringify(await emptyState(cdp), null, 1));

  // Troca de usuário: os cookies de sessão do admin sairiam sozinhos pelo refresh token, mas
  // limpar explicitamente garante que o próximo login parta de sessão vazia.
  await cdp.send('Network.clearBrowserCookies');
  const parentPath = await login(cdp, PARENT);
  console.log('login responsável ->', parentPath);
  console.log('boletim:', JSON.stringify(await parentReport(cdp, 'Ana Clara'), null, 1));
} finally {
  cdp.close();
}
