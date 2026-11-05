import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (path: string) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');

/**
 * O bloco de perfil no rodapé da sidebar era só texto: nome e papel, sem nada a clicar. Estes
 * testes fixam a função que ele ganhou — abrir o diálogo da conta — porque uma regressão aqui é
 * silenciosa: a tela continua bonita, apenas sem ação.
 */
test('o perfil do rodapé abre o diálogo da conta', async () => {
  const sidebar = await read('src/components/Sidebar.tsx');

  assert.match(
    sidebar,
    /onClick=\{\(\) => setAccountOpen\(true\)\}/,
    'o bloco de perfil voltou a ser texto sem ação',
  );
  assert.match(
    sidebar,
    /aria-haspopup="dialog"/,
    'o botão do perfil não anuncia que abre um diálogo',
  );
  assert.match(sidebar, /<Modal\b/, 'o perfil não monta o diálogo da conta');
});

test('o diálogo da conta mostra quem está logado', async () => {
  const sidebar = await read('src/components/Sidebar.tsx');

  assert.match(sidebar, /\{user\?\.name\}/, 'o diálogo não mostra o nome');
  assert.match(sidebar, /\{user\?\.email\}/, 'o diálogo não mostra o e-mail');
  assert.match(sidebar, /<Badge variant="info">\{t\(ROLE_KEYS\[user\.role\]\)\}<\/Badge>/, 'o papel saiu do diálogo');
});

test('sair continua visível no rodapé, fora do diálogo', async () => {
  const sidebar = await read('src/components/Sidebar.tsx');

  // A saída é imediata: um botão no rodapé. Enterrá-la dentro do diálogo custava um clique a mais
  // na ação mais frequente da barra.
  assert.match(sidebar, /onClick=\{handleLogout\}/, 'o rodapé perdeu o botão de sair');
  assert.match(sidebar, /\{t\('nav\.logout'\)\}/, 'o botão de sair perdeu o rótulo traduzido');
  assert.doesNotMatch(
    sidebar,
    /confirmLabel=\{t\('nav\.logout'\)\}/,
    'o diálogo voltou a duplicar a saída',
  );
});

test('o diálogo se fecha como diálogo informativo, não como formulário', async () => {
  const sidebar = await read('src/components/Sidebar.tsx');

  assert.match(sidebar, /title=\{t\('nav\.account'\)\}/, 'o diálogo perdeu o título da conta');
  assert.match(
    sidebar,
    /closeLabel=\{t\('common\.close'\)\}/,
    'o diálogo informativo voltou a oferecer "Cancelar"',
  );
  assert.doesNotMatch(sidebar, /onConfirm=/, 'o diálogo da conta ganhou uma confirmação que não existe');
});

test('as configurações só aparecem para quem pode abri-las', async () => {
  const sidebar = await read('src/components/Sidebar.tsx');

  assert.match(
    sidebar,
    /user\?\.role === 'Admin' && \(/,
    'o atalho de configurações deixou de ser restrito ao Admin',
  );
  assert.match(
    sidebar,
    /navigate\('\/admin\/settings'\)/,
    'o atalho de configurações perdeu o destino',
  );
});

test('o papel aparece traduzido, não como o valor da API', async () => {
  const sidebar = await read('src/components/Sidebar.tsx');

  assert.doesNotMatch(
    sidebar,
    /\{user\?\.role\}/,
    'o papel voltou a ser impresso cru (Admin/Staff/Parent)',
  );
  assert.match(sidebar, /ROLE_KEYS: Record<User\['role'\], MessageKey>/, 'o mapa de papéis saiu');
});
