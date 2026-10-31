/**
 * Sonda manual da spec 190 — mede no Chrome real o que os testes de fonte não provam.
 *
 * Uso: `vite dev` em 5173 + API em 5264 + Chrome com `--remote-debugging-port=9444`,
 * então `node tests/manual/i18n-a11y-probe.mjs`.
 *
 * Mede:
 * 1. login e navegação até uma listagem;
 * 2. estado vazio em pt (EmptyState com role=status);
 * 3. troca de idioma via localStorage → mesmo estado vazio em inglês;
 * 4. modal com role=dialog/aria-modal/aria-labelledby, Escape fechando e foco devolvido;
 * 5. data formatada pelo dono único (dd/mm/aaaa), sem literal de locale na tela.
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

let nextId = 1;
const pending = new Map();
ws.onmessage = (event) => {
  const message = JSON.parse(event.data);
  if (message.id && pending.has(message.id)) {
    const { resolve, reject } = pending.get(message.id);
    pending.delete(message.id);
    if (message.error) reject(new Error(JSON.stringify(message.error)));
    else resolve(message.result);
  }
};

function send(method, params = {}) {
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

async function evaluate(expression) {
  const result = await send('Runtime.evaluate', {
    expression,
    awaitPromise: true,
    returnByValue: true,
  });
  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description ?? 'erro na avaliação');
  }
  return result.result.value;
}

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

// ── 2. Varredura das listagens: qual delas está vazia agora ────────────────
const ROUTES = [
  ['matrículas', '/admin/enrollments'],
  ['alunos', '/admin/students'],
  ['notificações (admin)', '/admin/notifications'],
  ['documentos', '/admin/documents'],
  ['usuários', '/admin/users'],
];

const emptyRoute = (await (async () => {
  for (const [label, route] of ROUTES) {
    await send('Page.navigate', { url: `${APP}${route}` });
    await sleep(2000);
    const text = await evaluate(`(() => {
      const status = document.querySelector('[role="status"]');
      return status ? status.innerText.replace(/\\\\s+/g, ' ').trim() : null;
    })()`);
    console.log(`  ${label}: ${text === null ? '(com dados)' : text}`);
    if (text !== null) return route;
  }
  return null;
})());

// ── 3. O mesmo estado vazio em inglês ──────────────────────────────────────
if (emptyRoute === null) {
  console.log('estado vazio: nenhuma listagem vazia no ambiente — não medido');
} else {
  await evaluate(`localStorage.setItem('dscode.language', 'en')`);
  await send('Page.navigate', { url: `${APP}${emptyRoute}` });
  await sleep(2500);

  const emptyEn = await evaluate(`(() => {
    const status = document.querySelector('[role="status"]');
    return status ? status.innerText.replace(/\\\\s+/g, ' ').trim() : null;
  })()`);
  console.log('estado vazio (en):', emptyEn);
}

// ── 4. Modal: semântica, Escape e foco ─────────────────────────────────────
await evaluate(`localStorage.setItem('dscode.language', 'pt')`);
await send('Page.navigate', { url: `${APP}/admin/users` });
await sleep(2500);

const modal = await evaluate(`(async () => {
  const trigger = [...document.querySelectorAll('button')].find((b) => /Convidar|Novo usuário|Adicionar/i.test(b.innerText));
  if (!trigger) return { error: 'gatilho do modal não encontrado: ' + document.body.innerText.slice(0, 120) };

  trigger.focus();
  const triggerLabel = trigger.innerText.trim();
  trigger.click();
  await new Promise((r) => setTimeout(r, 600));

  const dialog = document.querySelector('[role="dialog"]');
  if (!dialog) return { error: 'modal não abriu' };

  const info = {
    role: dialog.getAttribute('role'),
    ariaModal: dialog.getAttribute('aria-modal'),
    labelledBy: dialog.getAttribute('aria-labelledby'),
    labelledByResolves: Boolean(dialog.getAttribute('aria-labelledby') && document.getElementById(dialog.getAttribute('aria-labelledby'))),
    focusInsideOnOpen: dialog.contains(document.activeElement),
    triggerLabel,
  };

  dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  await new Promise((r) => setTimeout(r, 600));

  info.closedByEscape = document.querySelector('[role="dialog"]') === null;
  info.focusReturned = document.activeElement?.innerText?.trim() === triggerLabel;
  return info;
})()`);
console.log('modal:', JSON.stringify(modal));

// ── 5. Data formatada pelo dono único ──────────────────────────────────────
await send('Page.navigate', { url: `${APP}/admin/documents` });
await sleep(2500);

const dates = await evaluate(`(() => {
  const text = document.body.innerText;
  const matches = text.match(/\\d{2}\\/\\d{2}\\/\\d{4}/g) ?? [];
  return { count: matches.length, sample: matches.slice(0, 3), hasInvalidDate: text.includes('Invalid Date') };
})()`);
console.log('datas na tela:', JSON.stringify(dates));

ws.close();
