import test from 'node:test';
import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';

/**
 * O carregamento é silencioso quando regride: a tela funciona, só volta a piscar. O defeito
 * original era a seção aparecer VAZIA (ou com "Carregando...") e crescer de uma vez quando a
 * API respondia — o pulo de layout que se vê como piscada ao trocar de seção.
 *
 * Este teste guarda as três coisas que impedem a volta:
 *
 * 1. a textura de placeholder tem um dono só (`Skeleton.tsx`);
 * 2. nenhuma página mostra o texto "Carregando..." no lugar da silhueta do conteúdo;
 * 3. as telas que renderizavam estado vazio enquanto buscavam continuam com estado de carga.
 */

const root = new URL('..', import.meta.url);

function readText(path: string): Promise<string> {
  return readFile(new URL(path, root), 'utf8');
}

/** Caminhos de `dir` (relativos à raiz do frontend), recursivos, só TS/TSX. */
async function sourceFiles(dir: string): Promise<string[]> {
  const entries = await readdir(new URL(`${dir}/`, root), { withFileTypes: true });
  const found = await Promise.all(
    entries.map(async (entry) => {
      const path = `${dir}/${entry.name}`;
      if (entry.isDirectory()) return sourceFiles(path);
      return /\.tsx?$/.test(entry.name) ? [path] : [];
    }),
  );
  return found.flat();
}

const SKELETON_OWNER = 'src/components/Skeleton.tsx';
/** `SplashGate` pulsa texto (a splash do app), não um bloco de conteúdo. */
const PULSE_ALLOWED = new Set(['src/components/SplashGate.tsx']);

test('a textura de carregamento tem um dono só', async () => {
  const offenders: string[] = [];
  for (const path of await sourceFiles('src')) {
    const source = await readText(path);
    // `animate-pulse` (pulso do Tailwind) recriaria a textura fora do dono. A textura
    // oficial é a classe `.skeleton`, definida no CSS e aplicada só pelo componente.
    if (/animate-pulse/.test(source) && !PULSE_ALLOWED.has(path)) {
      offenders.push(path);
    }
  }

  assert.deepEqual(
    offenders,
    [],
    `estes arquivos repetem a textura em vez de usar \`Skeleton\`: ${offenders.join(', ')}`,
  );

  const owner = await readText(SKELETON_OWNER);
  assert.match(owner, /className=\{`skeleton /, 'o componente não aplica a textura oficial');
});

test('o esqueleto espera antes de aparecer, para não piscar em resposta rápida', async () => {
  const css = await readText('src/index.css');
  const rule = css.match(/\n\.skeleton\s*\{[^}]*\}/)?.[0];

  assert.ok(rule, 'regra `.skeleton` ausente no CSS');
  assert.match(rule, /skeleton-appear 0s linear \d+ms both/, 'a aparição precisa do atraso e do preenchimento');
  assert.match(rule, /skeleton-pulse[^,;]*\d+ms infinite/, 'o pulso precisa começar depois do atraso');
  assert.match(
    css,
    /@keyframes skeleton-appear\s*\{\s*from\s*\{\s*opacity:\s*0/,
    'sem começar invisível o atraso não tem efeito',
  );
});

test('nenhuma página troca conteúdo por um "Carregando..." de texto', async () => {
  const pages = [
    'src/pages/admin/StudentDetail.tsx',
    'src/pages/admin/EnrollmentDetail.tsx',
    'src/pages/parent/ChildDetail.tsx',
    'src/pages/parent/ChildDocuments.tsx',
  ];

  for (const page of pages) {
    const source = await readText(page);
    assert.doesNotMatch(
      source,
      />\s*Carregando\.\.\./,
      `${page} volta a mostrar texto solto no lugar da silhueta do conteúdo`,
    );
    assert.match(source, /(PageSkeleton|SkeletonRows)/, `${page} não usa o esqueleto de carregamento`);
  }
});

test('telas que mostravam estado vazio enquanto buscavam continuam com estado de carga', async () => {
  const users = await readText('src/pages/admin/UserManagement.tsx');
  assert.match(users, /const \[loading, setLoading\] = useState\(true\)/, 'Usuários perdeu o estado de carga');
  assert.match(users, /loading \? \([\s\S]{0,200}<SkeletonRows/, 'Usuários não mostra esqueleto enquanto busca');

  // O dashboard mostrava "Nenhum período de matrícula aberto" antes de receber a resposta.
  const dashboard = await readText('src/pages/admin/AdminDashboard.tsx');
  assert.match(
    dashboard,
    /loading \? \([\s\S]{0,400}<Skeleton/,
    'o cartão de período volta a exibir estado vazio durante a busca',
  );
});

test('o esqueleto da tabela reserva as mesmas linhas da página', async () => {
  const table = await readText('src/components/Table.tsx');
  assert.match(table, /loadingRows\?: number/, 'a tabela precisa aceitar o número de linhas de esqueleto');

  // Medido no app real: com 5 linhas de esqueleto e páginas de 10, a tabela carregava com
  // metade da altura e crescia 311px quando os dados chegavam.
  const pages = [
    'src/pages/admin/StudentList.tsx',
    'src/pages/admin/EnrollmentList.tsx',
    'src/pages/admin/DocumentVerification.tsx',
  ];

  for (const page of pages) {
    const source = await readText(page);
    assert.match(
      source,
      /loading=\{loading\}[\s\S]{0,80}loadingRows=\{PAGE_SIZE\}/,
      `${page} não alinha o esqueleto com o tamanho da página`,
    );
  }
});
