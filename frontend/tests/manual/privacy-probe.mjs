/**
 * Sonda manual da spec 210 — mede no Chrome real o que os testes de fonte não provam.
 *
 * Uso: `vite dev` em 5173 + API em 5264 + Chrome com `--remote-debugging-port=9444`,
 * então `node tests/manual/privacy-probe.mjs`.
 *
 * Mede:
 * 1. login de administrador e navegação até /admin/privacy;
 * 2. o item de menu "Privacidade" existe e a página monta sem erro de console;
 * 3. o estado carregado é a tabela (com linhas) ou o vazio declarado — nunca o erro de carga;
 * 4. /parent/privacy não é acessível com sessão de administrador.
 */

const CDP_HTTP = process.env.CDP_HTTP ?? 'http://127.0.0.1:9444';
const APP = process.env.APP_URL ?? 'http://localhost:5173';

const list = await (await fetch(`${CDP_HTTP}/json/list`)).json();
const page = list.find((target) => target.type === 'page');
if (!page) throw new Error('nenhuma aba disponível no Chrome');

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
  ws.onopen = resolve;
  ws.onerror = reject;
});

let id = 0;
const pending = new Map();
const consoleErrors = [];

ws.onmessage = (event) => {
  const message = JSON.parse(event.data);
  if (message.method === 'Runtime.exceptionThrown') {
    consoleErrors.push(message.params.exceptionDetails?.text ?? 'exceção sem texto');
  }
  if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error') {
    consoleErrors.push(message.params.args.map((arg) => arg.value ?? arg.description).join(' '));
  }
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

const evaluate = async (expression) => {
  const response = await send('Runtime.evaluate', {
    expression,
    awaitPromise: true,
    returnByValue: true,
  });
  return response.result?.result?.value;
};

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

await send('Page.enable');
await send('Runtime.enable');

// ── 1. Login ────────────────────────────────────────────────────────────────
await send('Page.navigate', { url: `${APP}/login` });
await sleep(1500);

await evaluate(`(() => {
  const splash = document.body.innerText.includes('pressione qualquer tecla');
  if (splash) {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    document.body.click();
  }
  return splash;
})()`);
await sleep(800);

const loggedIn = await evaluate(`(async () => {
  const inputs = [...document.querySelectorAll('input')];
  const email = inputs.find((i) => i.type === 'email');
  const password = inputs.find((i) => i.type === 'password');
  if (!email || !password) return 'sem formulário: ' + document.body.innerText.slice(0, 120);

  const setValue = (element, value) => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    setter.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  };
  setValue(email, 'admin@colegiomodelo.com');
  setValue(password, 'Admin@123');

  const form = email.closest('form');
  form.requestSubmit();
  await new Promise((r) => setTimeout(r, 2500));
  return location.pathname;
})()`);

console.log('login →', loggedIn);

// ── 2. Menu e página de privacidade ─────────────────────────────────────────
console.log('item no menu:', await evaluate(`(() => {
  const links = [...document.querySelectorAll('nav a')].map((a) => a.textContent.trim());
  return links.includes('Privacidade') ? 'Privacidade presente' : 'AUSENTE em ' + links.join(' | ');
})()`));

await send('Page.navigate', { url: `${APP}/admin/privacy` });
await sleep(2500);

const rendered = await evaluate(`(() => {
  const emptyState = document.querySelector('[role="status"]');
  const rows = document.querySelectorAll('table tbody tr').length;
  const alert = document.querySelector('[role="alert"]');
  const headers = [...document.querySelectorAll('table thead th')].map((th) => th.textContent.trim());
  return {
    href: location.pathname,
    title: document.querySelector('h2')?.textContent ?? null,
    emptyText: emptyState?.innerText ?? null,
    alertText: alert?.innerText ?? null,
    rows,
    headers,
  };
})()`);
console.log('página:', JSON.stringify(rendered));

// ── 3. Guarda de papel: administrador não entra na central do responsável ───
await send('Page.navigate', { url: `${APP}/parent/privacy` });
await sleep(2000);
console.log('admin em /parent/privacy →', await evaluate(`location.pathname`));

console.log('erros de console:', consoleErrors.length === 0 ? 'nenhum' : consoleErrors.join(' | '));

ws.close();
