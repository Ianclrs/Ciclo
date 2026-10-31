import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Guarda dos estados de tela da Spec 190 (FR-005 / NFR-001).
 *
 * O projeto não tem biblioteca de DOM, então a verificação é inspeção literal dos arquivos —
 * o mesmo padrão das outras guardas de contrato do frontend. Remover o `EmptyState` de
 * qualquer uma das seis telas declaradas quebra este teste.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = join(HERE, '..', 'src');

const LIST_SCREENS = [
  'pages/admin/EnrollmentList.tsx',
  'pages/admin/StudentList.tsx',
  'pages/admin/NotificationList.tsx',
  'pages/parent/NotificationInbox.tsx',
  'pages/admin/UserManagement.tsx',
  'pages/admin/DocumentVerification.tsx',
];

function read(relativePath: string): string {
  return readFileSync(join(SRC, relativePath), 'utf8');
}

function allPageFiles(directory: string): string[] {
  const absolute = join(SRC, directory);
  const found: string[] = [];

  for (const entry of readdirSync(absolute)) {
    const entryPath = join(absolute, entry);
    if (statSync(entryPath).isDirectory()) {
      found.push(...allPageFiles(join(directory, entry)));
    } else if (entryPath.endsWith('.ts') || entryPath.endsWith('.tsx')) {
      found.push(entryPath);
    }
  }

  return found;
}

test('as seis telas declaradas importam e renderizam o estado vazio', () => {
  for (const screen of LIST_SCREENS) {
    const source = read(screen);
    assert.match(source, /import \{ EmptyState \} from '.*EmptyState'/, `${screen} não importa EmptyState`);
    assert.match(source, /<EmptyState/, `${screen} não renderiza <EmptyState>`);
  }
});

test('o erro de carga lê o locale ativo', () => {
  const source = read('components/LoadError.tsx');
  assert.match(source, /useLocale/);
  assert.match(source, /\bt\('common\.(error|retry)'/);
});

test('nenhuma página declara o locale fixo pt-BR', () => {
  const offenders = allPageFiles('pages')
    .filter((file) => readFileSync(file, 'utf8').includes('pt-BR'))
    .map((file) => file.slice(SRC.length + 1));

  assert.deepEqual(offenders, [], `páginas com locale fixo: ${offenders.join(', ')}`);
});
