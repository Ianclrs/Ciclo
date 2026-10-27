import test from 'node:test';
import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';

/**
 * Guarda estrutural do A8: nenhuma tela pode engolir a falha de carregamento.
 *
 * Um `catch` vazio mostra "lista vazia" quando a consulta falhou — o usuário
 * conclui que não há dados. Este teste existe porque a falha não aparece em
 * teste de comportamento: só se manifesta com a API fora do ar.
 */
const SILENT_CATCH_BLOCK = /catch\s*(?:\([^)]*\))?\s*\{\s*(?:\/\*[\s\S]*?\*\/)?\s*\}/g;
// A forma mais comum no código é o callback de promise — o primeiro padrão não a vê.
const SILENT_CATCH_CALLBACK =
  /\.catch\(\s*(?:\([^)]*\)|[A-Za-z_$][\w$]*)\s*=>\s*\{\s*(?:\/\*[\s\S]*?\*\/)?\s*\}\s*\)/g;

async function filesUnder(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { withFileTypes: true });
  const files = await Promise.all(
    entries.map(async (entry) => {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) {
        return filesUnder(path);
      }
      return /\.(ts|tsx)$/.test(entry.name) ? [path] : [];
    }),
  );
  return files.flat();
}

test('telas e componentes não engolem erro de carregamento', async () => {
  const roots = ['src/pages', 'src/components'];
  const offenders: string[] = [];

  for (const root of roots) {
    for (const file of await filesUnder(root)) {
      const source = await readFile(file, 'utf8');
      for (const pattern of [SILENT_CATCH_BLOCK, SILENT_CATCH_CALLBACK]) {
        for (const match of source.matchAll(pattern)) {
          const line = source.slice(0, match.index).split('\n').length;
          offenders.push(`${file}:${line}`);
        }
      }
    }
  }

  assert.deepEqual(offenders, [], 'catch silencioso encontrado (use LoadError/estado de erro)');
});
