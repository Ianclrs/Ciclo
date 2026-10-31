/**
 * Sonda manual (não roda na suíte): mede o layout da tela de login no Chrome real.
 *
 * Uso:
 *   1. `npm run build && npx vite preview --port 4188 --strictPort`
 *   2. chrome --headless=new --remote-debugging-port=9444 about:blank
 *   3. `node tests/manual/login-layout-probe.mjs http://localhost:4188/login 1440`
 *
 * Serve para o que o teste estático não alcança: largura real do painel, se a fonte da
 * marca carregou de fato, e se a página estoura a horizontal.
 */
const BASE = 'http://127.0.0.1:9444';

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

const probe = `(() => {
  const aside = document.querySelector('aside');
  const main = document.querySelector('main');
  const doc = document.documentElement;
  const diag = {
    href: location.href,
    title: document.title,
    rootChildren: document.getElementById('root')?.children.length ?? -1,
    bodyLength: document.body.innerHTML.length,
  };
  if (!aside) return { ...diag, error: 'a tela não renderizou um <aside>' };
  return {
    ...diag,
    viewport: innerWidth + 'x' + innerHeight,
    fontFamily: getComputedStyle(aside).fontFamily,
    fontLoaded: document.fonts.check('16px "Plus Jakarta Sans"'),
    // Os cards flutuantes: filtro de fundo aqui é o que borra o texto em movimento.
    cardBackdrop: (() => {
      const card = document.querySelector('.glass-card');
      return card ? getComputedStyle(card).backdropFilter : 'sem card';
    })(),
    pillBackdrop: (() => {
      const pill = document.querySelector('.glass-pill');
      return pill ? getComputedStyle(pill).backdropFilter : 'sem pill';
    })(),
    cardAnimation: (() => {
      const card = document.querySelector('.animate-float');
      return card ? getComputedStyle(card).animationName : 'sem animação';
    })(),
    asideWidth: Math.round(aside.getBoundingClientRect().width),
    asideHeight: Math.round(aside.getBoundingClientRect().height),
    // Conteúdo maior que a caixa aparece aqui: é a rolagem DENTRO do painel.
    asideScroll: aside.scrollHeight - aside.clientHeight,
    asideRadius: getComputedStyle(aside).borderTopLeftRadius,
    logoSize: (() => {
      const logo = aside.querySelector('img');
      if (!logo) return 'ausente';
      const box = logo.getBoundingClientRect();
      return Math.round(box.width) + 'x' + Math.round(box.height);
    })(),
    mainDisplay: main ? getComputedStyle(main).display : 'ausente',
    // Quais ícones o painel institucional está desenhando (o lucide marca a classe).
    panelIcons: [...document.querySelectorAll('main svg')].map(
      (svg) => (svg.getAttribute('class') || '').replace('lucide ', '') || svg.getAttribute('viewBox') || 'sem classe',
    ),
    cardWillChange: (() => {
      const card = document.querySelector('.animate-float');
      return card ? getComputedStyle(card).willChange : 'sem card';
    })(),
    horizontalOverflow: doc.scrollWidth - doc.clientWidth,
    pageHeight: doc.scrollHeight,
    unlabeledIconButtons: [...document.querySelectorAll('button')]
      .filter((b) => !b.textContent.trim() && !b.getAttribute('aria-label')).length,
    deadLinks: [...document.querySelectorAll('a')]
      .filter((a) => !a.getAttribute('href') || a.getAttribute('href') === '#').length,
    percentsOnScreen: document.body.textContent.match(/\\d+\\s*%/g) || [],
  };
})()`;

const [url, widthArg] = process.argv.slice(2);
if (!url) throw new Error('informe a URL: node tests/manual/login-layout-probe.mjs <url> [largura]');
const width = Number(widthArg || 1440);

const { send, close } = await connect();
await send('Page.enable');
await send('Runtime.enable');
await send('Emulation.setDeviceMetricsOverride', {
  width,
  height: 900,
  deviceScaleFactor: 1,
  mobile: width < 700,
});
await send('Page.navigate', { url });
await sleep(1200);

// O perfil do Chrome guarda a sessão da execução anterior: sem limpar, `/login` redireciona
// para o painel e a sonda mediria o dashboard.
await send('Runtime.evaluate', { expression: 'localStorage.clear(); sessionStorage.clear();' });
await send('Page.reload');
await sleep(1500);

// A splash de abertura cobre a tela e só sai com tecla/clique/rolagem. Sem dispensá-la a
// sonda mediria a splash, não o login.
await send('Runtime.evaluate', {
  expression: "window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))",
});
await sleep(900);

const result = await send('Runtime.evaluate', { expression: probe, returnByValue: true });
console.log(JSON.stringify(result.result?.result?.value ?? result.result, null, 2));

// `--lembrar`: mede o desenho do "lembrar o acesso" antes e depois de clicar.
if (process.argv.includes('--lembrar')) {
  const read = () =>
    send('Runtime.evaluate', {
      expression: `(() => {
        const input = document.getElementById('login-remember');
        const box = input?.nextElementSibling;
        if (!input || !box) return 'controle ausente';
        const css = getComputedStyle(box);
        return {
          marcado: input.checked,
          raio: css.borderRadius,
          fundo: css.backgroundImage.slice(0, 46),
          borda: css.borderTopColor,
          tamanho: Math.round(box.getBoundingClientRect().width) + 'x' + Math.round(box.getBoundingClientRect().height),
        };
      })()`,
      returnByValue: true,
    });

  console.log('inicial:', JSON.stringify((await read()).result?.result?.value));

  await send('Runtime.evaluate', {
    expression: "document.querySelector('label[for=\"login-remember\"]').click()",
  });
  await sleep(400);
  console.log('após clicar:', JSON.stringify((await read()).result?.result?.value));

  await send('Runtime.evaluate', {
    expression: "document.querySelector('label[for=\"login-remember\"]').click()",
  });
  await sleep(400);
  console.log('após desmarcar:', JSON.stringify((await read()).result?.result?.value));
}

// `--splash`: a abertura aparece em qualquer um dos três endereços, mas só uma vez por sessão.
if (process.argv.includes('--splash')) {
  const steps = [];
  const read = async (label) => {
    const out = await send('Runtime.evaluate', {
      expression: `({
        caminho: location.pathname,
        temSplash: !!document.querySelector('[aria-label="Continuar para o login"]'),
        temPainel: !!document.querySelector('aside img'),
        marca: document.querySelector('aside img + span span:last-child')?.textContent?.trim() ?? 'ausente',
      })`,
      returnByValue: true,
    });
    steps.push(`${label}: ${JSON.stringify(out.result?.result?.value)}`);
  };

  // Abre o cadastro direto, com a sessão do navegador zerada.
  await send('Runtime.evaluate', { expression: 'localStorage.clear(); sessionStorage.clear();' });
  await send('Page.navigate', { url: url.replace(/\/login.*$/, '/register/parent') });
  await sleep(1500);
  await read('link direto /register/parent (sessão nova)');

  // Dismissar e conferir que o painel certo aparece.
  await send('Runtime.evaluate', {
    expression: "window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))",
  });
  await sleep(800);
  await read('depois de dispensar');

  // Trocar de painel NÃO pode trazer a abertura de volta.
  await send('Runtime.evaluate', {
    expression: `(() => {
      const btn = [...document.querySelectorAll('aside button')]
        .find((b) => b.textContent.trim() === 'Voltar para o login');
      if (!btn) return 'botão ausente';
      btn.click();
      return 'clicado';
    })()`,
    returnByValue: true,
  });
  await sleep(800);
  await read('de volta ao /login');

  console.log(steps.join('\n'));
}

// `--rotas`: percorre os três conteúdos e confere se a URL e a marca ao lado da logo mudam.
if (process.argv.includes('--rotas')) {
  const steps = [];
  const state = () => `({
    caminho: location.pathname,
    aba: document.title,
    marca: document.querySelector('aside img + span span:last-child')?.textContent?.trim() ?? 'ausente',
    titulo: document.querySelector('aside h1')?.textContent?.trim() ?? 'ausente',
  })`;
  const clickAndRead = async (label, text) => {
    await send('Runtime.evaluate', {
      expression: `(() => {
        const btn = [...document.querySelectorAll('aside button')]
          .find((b) => b.textContent.trim() === ${JSON.stringify(text)});
        if (!btn) return 'botão ausente: ${text}';
        btn.click();
        return 'clicado';
      })()`,
      returnByValue: true,
    });
    await sleep(500);
    const out = await send('Runtime.evaluate', { expression: state(), returnByValue: true });
    steps.push(`${label}: ${JSON.stringify(out.result?.result?.value)}`);
  };

  steps.push(`inicial: ${JSON.stringify((await send('Runtime.evaluate', { expression: state(), returnByValue: true })).result?.result?.value)}`);
  await clickAndRead('cadastro de colégio', 'Cadastrar meu colégio');
  await clickAndRead('volta ao login', 'Voltar para o login');
  await clickAndRead('conta de responsável', 'Criar conta de responsável');
  await clickAndRead('volta ao login (2)', 'Voltar para o login');

  // Link direto: abrir `/register` na barra de endereço tem de mostrar o mesmo painel.
  await send('Page.navigate', { url: url.replace(/\/login.*$/, '/register') });
  await sleep(1800);
  const direct = await send('Runtime.evaluate', { expression: state(), returnByValue: true });
  steps.push(`link direto /register: ${JSON.stringify(direct.result?.result?.value)}`);

  console.log(steps.join('\n'));
}

// `--cadastro`: troca o painel para o cadastro de colégio e percorre as gavetas.
if (process.argv.includes('--cadastro')) {
  const steps = [];
  const run = async (label, expression) => {
    const out = await send('Runtime.evaluate', { expression, returnByValue: true });
    steps.push(`${label}: ${JSON.stringify(out.result?.result?.value)}`);
    await sleep(400);
  };

  await run('clique em cadastrar', `(() => {
    const btn = [...document.querySelectorAll('aside button')]
      .find((b) => b.textContent.includes('Cadastrar meu colégio'));
    if (!btn) return 'botão ausente';
    btn.click();
    return 'clicado';
  })()`);

  await run('painel após troca', `(() => {
    const aside = document.querySelector('aside');
    const nome = [...aside.querySelectorAll('label')].find((l) => l.textContent.includes('Nome do colégio'));
    const loginAindaVisivel = !!document.getElementById('login-email');
    const titulo = aside.querySelector('h1')?.textContent?.trim();
    return {
      titulo,
      temNomeDoColegio: !!nome,
      loginAindaVisivel,
      altura: Math.round(aside.getBoundingClientRect().height),
      rolagemInterna: aside.scrollHeight - aside.clientHeight,
      pagina: document.documentElement.scrollHeight,
      barraEscondida: getComputedStyle(aside).scrollbarWidth,
      // A trilha de passos: estouro horizontal e rótulos cortados aparecem aqui.
      trilhaTransborda: (() => {
        const ol = aside.querySelector('ol');
        return ol ? ol.scrollWidth - ol.clientWidth : 'sem trilha';
      })(),
      rotulosCortados: [...aside.querySelectorAll('ol span')]
        .filter((s) => s.scrollWidth > s.clientWidth + 1)
        .map((s) => s.textContent.trim()),
      circuloAtivo: (() => {
        const c = aside.querySelector('ol span');
        return c ? getComputedStyle(c).backgroundImage.slice(0, 60) : 'ausente';
      })(),
    };
  })()`);

  await run('abre o seletor de país', `(() => {
    const btn = [...document.querySelectorAll('aside button')]
      .find((b) => b.getAttribute('aria-haspopup') === 'listbox');
    if (!btn) return 'seletor ausente';
    btn.click();
    return 'aberto';
  })()`);

  await run('escolhe o primeiro país', `(() => {
    const option = document.querySelector('aside [role="option"]');
    if (!option) return 'nenhuma opção';
    const label = option.textContent.trim();
    option.click();
    return label;
  })()`);

  await run('gaveta de dados da escola', `(() => {
    const aside = document.querySelector('aside');
    const texto = aside.textContent;
    return {
      temDadosDaEscola: texto.includes('Dados da escola'),
      temAdministrador: texto.includes('Administrador da conta'),
      rolagemInterna: aside.scrollHeight - aside.clientHeight,
      pagina: document.documentElement.scrollHeight,
    };
  })()`);

  console.log(steps.join('\n'));
}

// `--responsavel`: troca o painel para o cadastro de responsável e tenta criar a conta.
if (process.argv.includes('--responsavel')) {
  const steps = [];
  const run = async (label, expression) => {
    const out = await send('Runtime.evaluate', { expression, returnByValue: true });
    steps.push(`${label}: ${JSON.stringify(out.result?.result?.value)}`);
    await sleep(400);
  };

  await run('clique em conta de responsável', `(() => {
    const btn = [...document.querySelectorAll('aside button')]
      .find((b) => b.textContent.includes('Criar conta de responsável'));
    if (!btn) return 'botão ausente';
    btn.click();
    return 'clicado';
  })()`);

  await run('painel após troca', `(() => {
    const aside = document.querySelector('aside');
    const texto = aside.textContent;
    return {
      titulo: aside.querySelector('h1')?.textContent?.trim(),
      campos: aside.querySelectorAll('input').length,
      temCodigoDoColegio: texto.includes('Código do colégio'),
      loginAindaVisivel: !!document.getElementById('login-email'),
      barra: getComputedStyle(aside).scrollbarWidth,
      altura: Math.round(aside.getBoundingClientRect().height),
      rolagemInterna: aside.scrollHeight - aside.clientHeight,
      pagina: document.documentElement.scrollHeight,
    };
  })()`);

  await run('cria a conta', `(() => {
    const setNative = (el, value) => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
      setter.call(el, value);
      el.dispatchEvent(new Event('input', { bubbles: true }));
    };
    const inputs = [...document.querySelectorAll('aside input')];
    if (inputs.length < 5) return 'campos insuficientes: ' + inputs.length;
    const valores = ['colegio-modelo', 'Marta Almeida', 'marta@teste.local', 'Senha1!abc', 'Senha1!abc'];
    inputs.forEach((el, i) => setNative(el, valores[i]));
    document.querySelector('aside form').requestSubmit();
    return 'enviado';
  })()`);

  await sleep(2500);
  await run('resultado', `({
    pathname: location.pathname,
    heading: document.querySelector('h1, h2')?.textContent?.trim() ?? '',
    erroNaTela: document.querySelector('aside [role="alert"]')?.textContent?.trim() ?? null,
    // Um recarregamento de documento explica o painel voltar ao login "sozinho".
    tipoDeNavegacao: performance.getEntriesByType('navigation')[0]?.type ?? 'desconhecido',
    trechoDaTela: document.querySelector('aside')?.innerText.replace(/\\s+/g, ' ').slice(0, 110) ?? 'sem painel',
  })`);

  // Leitura tardia: se a sessão foi criada e a rota ficou presa em `/login` por um instante,
  // o redirecionamento do próprio `LoginPage` aparece aqui.
  await sleep(3000);
  await run('resultado tardio', `({
    pathname: location.pathname,
    heading: document.querySelector('h1, h2')?.textContent?.trim() ?? '',
    temSidebar: !!document.querySelector('aside nav'),
  })`);

  console.log(steps.join('\n'));
}

// `--nodemo`: o bloco violeta de demonstração só existe em build de demo. Removê-lo mede a
// altura do painel no build de produção, que é o que o usuário final vê.
if (process.argv.includes('--nodemo')) {
  const after = await send('Runtime.evaluate', {
    expression: `(() => {
      const box = document.querySelector('aside .border-violet-300');
      if (!box) return 'bloco de demonstração ausente';
      const alturaDoBloco = Math.round(box.getBoundingClientRect().height);
      box.remove();
      const aside = document.querySelector('aside');
      return {
        alturaDoBloco,
        asideHeightSemDemo: Math.round(aside.getBoundingClientRect().height),
        pageHeightSemDemo: document.documentElement.scrollHeight,
      };
    })()`,
    returnByValue: true,
  });
  console.log('sem demonstração:', JSON.stringify(after.result?.result?.value));
}

// `--login`: prova que o formulário continua entrando de verdade (modo demonstração).
if (process.argv.includes('--login')) {
  const click = await send('Runtime.evaluate', {
    expression: `(() => {
      const target = [...document.querySelectorAll('button')]
        .find((b) => b.textContent.includes('Entrar como administração'));
      if (!target) return 'botão de demonstração ausente';
      target.click();
      return 'clicado';
    })()`,
    returnByValue: true,
    awaitPromise: false,
  });
  console.log('clique:', click.result?.result?.value);
  await sleep(2500);
  const after = await send('Runtime.evaluate', {
    expression: `({
      pathname: location.pathname,
      hasSidebar: !!document.querySelector('nav, aside'),
      heading: document.querySelector('h1, h2')?.textContent?.trim() ?? '',
      // Aberta a sidebar tem 208px (w-52); recolhida tem 0. É isto que prova a abertura.
      menuWidth: (() => {
        const box = document.querySelector('aside');
        return box ? Math.round(box.getBoundingClientRect().width) : 'sem aside';
      })(),
      menuAriaExpanded: (() => {
        const btn = [...document.querySelectorAll('button')].find((b) => b.getAttribute('aria-expanded') !== null);
        return btn ? btn.getAttribute('aria-expanded') : 'sem botão';
      })(),
      // A marca é de uma vez só: depois que a sidebar monta, ela não pode mais existir.
      marcaConsumida: sessionStorage.getItem('ciclo.sessao.recem-iniciada') === null,
    })`,
    returnByValue: true,
  });
  console.log('depois do login:', JSON.stringify(after.result?.result?.value));
}

// `--form`: digita e envia o formulário. Valida que os campos crus da nova tela continuam
// controlados pelo React (setter nativo + evento `input`, como o navegador faria).
if (process.argv.includes('--form')) {
  const submit = await send('Runtime.evaluate', {
    expression: `(() => {
      const setNative = (el, value) => {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        setter.call(el, value);
        el.dispatchEvent(new Event('input', { bubbles: true }));
      };
      const email = document.getElementById('login-email');
      const password = document.getElementById('login-password');
      if (!email || !password) return 'campos ausentes';
      setNative(email, 'diretor@demo.ciclo');
      setNative(password, 'demo');
      const form = email.closest('form');
      if (!form) return 'formulário ausente';
      form.requestSubmit();
      return 'enviado';
    })()`,
    returnByValue: true,
  });
  console.log('formulário:', submit.result?.result?.value);
  await sleep(2500);
  const after = await send('Runtime.evaluate', {
    expression: `({ pathname: location.pathname, heading: document.querySelector('h1, h2')?.textContent?.trim() ?? '' })`,
    returnByValue: true,
  });
  console.log('depois do envio:', JSON.stringify(after.result?.result?.value));
}

close();
