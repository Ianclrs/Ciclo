/**
 * Medidor de troca de seção no app real (Chrome headless via CDP, sem dependências).
 *
 * Faz login na demonstração, clica em cada seção do menu e mede, a partir do momento em que
 * o React troca a seção (o nó `.section-enter` é substituído):
 *
 * - opacidade quadro a quadro do conteúdo;
 * - se o esqueleto aparece (e por quantos quadros);
 * - a altura do conteúdo em cada quadro (pulo de layout);
 * - o intervalo entre quadros (engasgo de animação).
 *
 * Também salva capturas nos primeiros 400 ms de uma troca.
 *
 * Uso: `vite preview` em 4173 + Chrome com `--remote-debugging-port=9333`, então
 * `node tests/manual/section-switch-probe.mjs`.
 */

const CDP_HTTP = process.env.CDP_HTTP ?? 'http://127.0.0.1:9333';
const APP = process.env.APP_URL ?? 'http://localhost:4173';
const OUT_DIR = process.env.OUT_DIR ?? '.dscode/tmp/cdp/frames';

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
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function screenshot(name) {
  const { mkdir, writeFile } = await import('node:fs/promises');
  const { data } = await send('Page.captureScreenshot', { format: 'png' });
  await mkdir(OUT_DIR, { recursive: true });
  await writeFile(`${OUT_DIR}/${name}.png`, Buffer.from(data, 'base64'));
}

await send('Page.enable');
await send('Runtime.enable');
await send('Emulation.setDeviceMetricsOverride', {
  width: 1280,
  height: 800,
  deviceScaleFactor: 1,
  mobile: false,
});

// Headless vem com `prefers-reduced-motion: reduce`, o que desliga as animações e não
// representa a máquina do usuário. Emular a preferência normal é o que torna a medição útil.
await send('Emulation.setEmulatedMedia', {
  features: [{ name: 'prefers-reduced-motion', value: 'no-preference' }],
});

await send('Page.navigate', { url: `${APP}/login` });
await sleep(2500);

// A rota passa por uma splash ("toque para continuar") antes da tela de login.
await evaluate(`document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); true`);
await sleep(1800);

// A sessão fica em `localStorage`: uma execução anterior já pode ter entrado.
const alreadyIn = await evaluate(`Boolean(document.querySelector('nav')) && location.pathname !== '/login'`);
if (!alreadyIn) {
  const login = await evaluate(`(() => {
    const button = [...document.querySelectorAll('button')].find((node) => /administra/i.test(node.textContent));
    if (!button) return false;
    button.click();
    return true;
  })()`);
  if (!login) throw new Error('botão de demonstração não encontrado');
  await sleep(2500);
}

console.log('rota:', await evaluate('location.pathname'));
console.log(
  'prefers-reduced-motion:',
  await evaluate(`matchMedia('(prefers-reduced-motion: reduce)').matches`),
);

await evaluate(`
/**
 * Mede UMA troca de seção, do commit em diante. O commit é detectado pela substituição do nó
 * \`.section-enter\` — antes disso a medição estaria lendo a tela antiga.
 */
window.__probe = async (label, ms = 500) => {
  const nav = document.querySelector('nav');
  const link = [...nav.querySelectorAll('a')].find((a) => a.textContent.trim().startsWith(label));
  if (!link) return { error: 'link não encontrado: ' + label };

  const before = document.querySelector('main .section-enter');
  link.click();

  const clickedAt = performance.now();
  while (document.querySelector('main .section-enter') === before) {
    if (performance.now() - clickedAt > 2000) return { error: 'a seção não foi substituída' };
    await new Promise(requestAnimationFrame);
  }
  const commitMs = Math.round(performance.now() - clickedAt);

  const frames = [];
  let start = 0;
  await new Promise((resolve) => {
    const tick = (time) => {
      if (!start) start = time;
      const content = document.querySelector('main .section-enter');
      const opacity = content ? Number(getComputedStyle(content).opacity) : null;
      const skeleton = [...document.querySelectorAll('.skeleton')].some((node) => {
        const style = getComputedStyle(node);
        const box = node.getBoundingClientRect();
        return Number(style.opacity) > 0.05 && box.width > 0 && box.height > 0;
      });
      frames.push({
        t: Math.round(time - start),
        opacity,
        skeleton,
        contentHeight: content ? Math.round(content.getBoundingClientRect().height) : null,
        scrollHeight: Math.round(document.querySelector('main').scrollHeight),
      });
      if (time - start < ms) requestAnimationFrame(tick);
      else resolve();
    };
    requestAnimationFrame(tick);
  });

  const heights = frames.map((frame) => frame.contentHeight);
  const finalHeight = heights[heights.length - 1];
  const settledAt = frames.find((frame) => frame.contentHeight === finalHeight);
  const gaps = frames.slice(1).map((frame, index) => frame.t - frames[index].t);
  return {
    label,
    commitMs,
    frames: frames.length,
    opacityStart: frames[0]?.opacity,
    fadeMs: frames.filter((frame) => frame.opacity < 0.99).map((frame) => frame.t).pop() ?? 0,
    skeletonFrames: frames.filter((frame) => frame.skeleton).length,
    heightStart: heights[0],
    heightEnd: finalHeight,
    jumpPx: finalHeight - heights[0],
    settledAtMs: settledAt ? settledAt.t : null,
    worstFrameGapMs: Math.max(...gaps),
    framesOver25ms: gaps.filter((gap) => gap > 25).length,
  };
};
`);

const sections = ['Alunos', 'Matrículas', 'Documentos', 'Notificações', 'Usuários', 'Dashboard'];
const report = [];
for (const section of sections) {
  report.push(await evaluate(`window.__probe(${JSON.stringify(section)})`));
  await sleep(900);
}

console.log('\n== troca de seção (medido a partir do commit) ==');
for (const row of report) {
  if (row.error) {
    console.log(`${row.label}: ${row.error}`);
    continue;
  }
  console.log(
    `${row.label.padEnd(13)} commit=${String(row.commitMs).padStart(3)}ms ` +
      `fade=${String(row.fadeMs).padStart(3)}ms(op0=${row.opacityStart}) ` +
      `esqueleto=${String(row.skeletonFrames).padStart(2)}q ` +
      `altura ${row.heightStart}->${row.heightEnd}px (${row.jumpPx >= 0 ? '+' : ''}${row.jumpPx}) ` +
      `estabiliza=${row.settledAtMs}ms piorQuadro=${row.worstFrameGapMs}ms`,
  );
}

// Capturas de uma troca, do commit em diante.
await evaluate(`document.querySelectorAll('nav a')[0].click()`);
await sleep(30);
await screenshot('t30');
await sleep(70);
await screenshot('t100');
await sleep(100);
await screenshot('t200');
await sleep(200);
await screenshot('t400');

console.log(`\ncapturas em ${OUT_DIR}`);
ws.close();
