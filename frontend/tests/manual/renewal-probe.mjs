/**
 * Sonda manual (não roda na suíte): abre a tela de rematrícula em lote no Chrome real.
 *
 * Uso:
 *   1. API em `http://localhost:5264` (Development) e `npm run dev` (proxy => 5264)
 *   2. chrome --headless=new --remote-debugging-port=9444 about:blank
 *   3. `node tests/manual/renewal-probe.mjs http://localhost:5173`
 *
 * Mede o que o teste estático não alcança: a página monta, a prévia responde 200, a linha do
 * aluno bloqueado aparece com o motivo em português, o checkbox fica desabilitado e confirmar
 * sem seleção não dispara requisição.
 */
const APP = process.argv[2] ?? 'http://localhost:5173';
const BASE = 'http://127.0.0.1:9444';
const ADMIN = { email: 'admin@colegiomodelo.com', password: 'Admin@123' };

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

async function login(cdp) {
  await cdp.send('Network.clearBrowserCookies');
  await goto(cdp, '/login');
  await evalJs(
    cdp,
    `
    if (!document.querySelector('input[type="email"]')) {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      await new Promise((r) => setTimeout(r, 900));
    }
    return 'splash';
  `,
  );
  await evalJs(
    cdp,
    `
    const email = document.querySelector('input[type="email"]');
    const pass = document.querySelector('input[type="password"]');
    if (!email || !pass) return 'sem formulário';
    setValue(email, ${JSON.stringify(ADMIN.email)});
    setValue(pass, ${JSON.stringify(ADMIN.password)});
    byText('button', 'Entrar na plataforma').click();
    return 'enviado';
  `,
  );
  await sleep(2600);
  return evalJs(cdp, 'return location.pathname;');
}

async function renewalScreen(cdp) {
  await goto(cdp, '/admin/enrollment-renewal');

  const initial = await evalJs(
    cdp,
    `
    return {
      pathname: location.pathname,
      heading: document.querySelector('h2')?.textContent ?? null,
      hasPeriodSelect: Boolean(document.querySelector('select')),
      hasEnrollmentLink: Boolean(byText('button', 'Rematrícula em lote')),
    };
  `,
  );

  const afterPeriod = await evalJs(
    cdp,
    `
    const select = document.querySelector('select');
    if (!select) return { error: 'seletor de período ausente' };
    const option = [...select.options].find((o) => o.value !== '');
    if (!option) return { error: 'nenhum período cadastrado' };
    setValue(select, option.value);
    await new Promise((r) => setTimeout(r, 1600));

    const rows = [...document.querySelectorAll('tbody tr')];
    const first = rows[0];
    const checkbox = first?.querySelector('input[type="checkbox"]');
    return {
      period: option.textContent.trim(),
      rowCount: rows.length,
      studentName: first?.querySelectorAll('td')[1]?.querySelector('p, span, div')
        ?.textContent?.trim() ?? first?.querySelectorAll('td')[1]?.textContent?.trim() ?? null,
      blockReasonShown: first?.querySelectorAll('td')[1]?.querySelector('p')?.textContent?.trim() ?? null,
      checkboxDisabled: checkbox ? checkbox.disabled : null,
      missingDocs: first?.querySelectorAll('td')[3]?.textContent?.trim() ?? null,
      showsEmptyMessage: document.body.innerText.includes('Nenhum aluno candidato'),
      confirmDisabled: byText('button', 'Renovar') ? byText('button', 'Renovar').disabled : null,
    };
  `,
  );

  const noSelection = await evalJs(
    cdp,
    `
    const confirm = byText('button', 'Renovar');
    if (!confirm) return { error: 'botão de confirmação ausente' };
    confirm.click();
    await new Promise((r) => setTimeout(r, 600));
    return {
      toast: document.querySelector('[role="status"]')?.textContent?.trim()
        ?? document.body.innerText.match(/Selecione ao menos um aluno[^\\n]*/)?.[0]
        ?? null,
      resultCardShown: document.body.innerText.includes('Desfecho do lote'),
    };
  `,
  );

  return { initial, afterPeriod, noSelection };
}

const cdp = await connect();
try {
  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');

  console.log('login ->', await login(cdp));
  console.log(JSON.stringify(await renewalScreen(cdp), null, 1));
} finally {
  cdp.close();
}
