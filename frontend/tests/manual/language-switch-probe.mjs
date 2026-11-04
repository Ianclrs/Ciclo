/**
 * Sonda manual (não roda na suíte): mede a troca de idioma no Chrome real, em modo demo.
 *
 * Uso:
 *   1. `npm run build && npx vite preview --port 4188 --strictPort`
 *   2. chrome --headless=new --remote-debugging-port=9444 about:blank
 *   3. `node tests/manual/language-switch-probe.mjs http://localhost:4188`
 *
 * Serve para o que o teste estático não alcança: se clicar em EN realmente reescreve o menu,
 * se a preferência sobrevive ao recarregamento e se o `<html lang>` acompanha o idioma.
 */
const CDP_HTTP = 'http://127.0.0.1:9444';
const SITE = process.argv[2] ?? 'http://localhost:4188';

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function connect() {
  // Uma aba nova por execução: a página inicial do Chrome headless pode estar em about:blank.
  await fetch(`${CDP_HTTP}/json/new?about:blank`, { method: 'PUT' });

  const list = await (await fetch(`${CDP_HTTP}/json/list`)).json();
  const page = list.find((target) => target.type === 'page');
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
    const settle = pending.get(message.id);
    if (settle) {
      pending.delete(message.id);
      settle(message);
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

async function main() {
  const { send, close } = await connect();

  const evaluate = async (expression) => {
    const reply = await send('Runtime.evaluate', {
      expression,
      awaitPromise: true,
      returnByValue: true,
    });

    const details = reply.result?.exceptionDetails;
    if (details) throw new Error(details.exception?.description ?? JSON.stringify(details));

    return reply.result?.result?.value;
  };

  const waitFor = async (expression, timeoutMs = 15000) => {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
      const value = await evaluate(expression).catch(() => null);
      if (value) return value;
      await sleep(200);
    }

    throw new Error(`timeout esperando: ${expression}`);
  };

  await send('Page.enable');
  await send('Runtime.enable');

  /** A splash de abertura só sai com tecla ou toque: sem isso o login nem monta. */
  const dismissSplash = async () => {
    await send('Input.dispatchKeyEvent', {
      type: 'keyDown', key: 'Enter', code: 'Enter',
      windowsVirtualKeyCode: 13, nativeVirtualKeyCode: 13,
    });
    await send('Input.dispatchKeyEvent', {
      type: 'keyUp', key: 'Enter', code: 'Enter',
      windowsVirtualKeyCode: 13, nativeVirtualKeyCode: 13,
    });
  };

  /**
   * A splash monta alguns quadros depois do `complete`: a tecla é reenviada até o formulário
   * aparecer, em vez de apostar em um único instante.
   */
  const waitForLogin = async () => {
    const deadline = Date.now() + 20000;

    while (Date.now() < deadline) {
      const ready = await evaluate(
        'document.body.innerText.includes("Entrar como administração")',
      ).catch(() => false);
      if (ready) return;
      await dismissSplash();
      await sleep(500);
    }

    throw new Error('a tela de login não apareceu');
  };

  // Começa sem preferência salva: o cenário de quem nunca escolheu idioma.
  await send('Page.navigate', { url: `${SITE}/login` });
  await waitFor('document.readyState === "complete"');
  await evaluate('localStorage.removeItem("dscode.language")');
  await send('Page.navigate', { url: `${SITE}/login` });
  await waitFor('document.readyState === "complete"');
  await waitForLogin();

  // Entra pelo atalho de demonstração do perfil Admin.
  await evaluate(`
    (() => {
      const buttons = [...document.querySelectorAll('button')];
      const demo = buttons.find((b) => /administração/i.test(b.textContent || ''));
      if (!demo) throw new Error('atalho de demonstração não encontrado');
      demo.click();
      return true;
    })()
  `);

  await waitFor('location.pathname.startsWith("/admin")');

  const readLabels = () =>
    evaluate(`[...document.querySelectorAll('aside nav a span.truncate')].map((el) => el.textContent)`);

  const beforeSwitch = await readLabels();
  const langBefore = await evaluate('document.documentElement.lang');
  const switcher = await evaluate(`
    [...document.querySelectorAll('div[role="group"] button')].map((b) => b.textContent)
  `);

  // Troca para inglês pelo controle da topbar.
  await evaluate(`
    (() => {
      const button = [...document.querySelectorAll('div[role="group"] button')]
        .find((b) => b.textContent.trim() === 'EN');
      if (!button) throw new Error('botão EN não encontrado');
      button.click();
      return true;
    })()
  `);

  await sleep(400);

  const afterSwitch = await readLabels();
  const langAfter = await evaluate('document.documentElement.lang');
  const stored = await evaluate('localStorage.getItem("dscode.language")');

  // A preferência precisa sobreviver ao recarregamento.
  await send('Page.navigate', { url: `${SITE}/admin` });
  await waitFor('location.pathname.startsWith("/admin")');
  await sleep(600);
  const afterReload = await readLabels();

  // Configurações: título e seções no idioma escolhido.
  await evaluate(`
    (() => {
      const link = [...document.querySelectorAll('aside nav a')].find((a) => a.getAttribute('href') === '/admin/settings');
      if (!link) return false;
      link.click();
      return true;
    })()
  `);
  await sleep(900);

  const pageTitle = await evaluate('document.querySelector("main h2")?.textContent ?? null');
  const cardTitles = await evaluate(`
    [...document.querySelectorAll('main form > div > div:first-child')].map((el) => el.textContent)
  `);
  const fieldLabels = await evaluate(`
    [...document.querySelectorAll('main form label')].map((el) => el.textContent?.trim())
  `);

  const report = {
    antes: { idioma: langBefore, menu: beforeSwitch, controle: switcher },
    depois: { idioma: langAfter, menu: afterSwitch, preferencia: stored },
    recarregado: { menu: afterReload },
    configuracoes: { titulo: pageTitle, secoes: cardTitles, campos: fieldLabels },
  };

  console.log(JSON.stringify(report, null, 2));

  close();
  // A conexão CDP e o keep-alive do `fetch` mantêm o event loop vivo: sem saída explícita o
  // processo fica pendurado e a sonda nunca termina.
  process.exit(0);
}

main().catch((error) => {
  console.error('FALHA:', error.message);
  process.exit(1);
});
