import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createSingleFlight } from '../src/api/singleFlight.ts';

const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 5));

test('chamadas concorrentes compartilham uma única execução', async () => {
  let runs = 0;
  const run = createSingleFlight(async () => {
    runs += 1;
    await tick();
    return `token-${runs}`;
  });

  const results = await Promise.all([run(), run(), run(), run(), run()]);

  assert.equal(runs, 1, 'a operação deve rodar uma única vez');
  assert.deepEqual(new Set(results), new Set(['token-1']));
});

test('após concluir, a próxima chamada executa de novo', async () => {
  let runs = 0;
  const run = createSingleFlight(async () => {
    runs += 1;
    await tick();
    return runs;
  });

  assert.equal(await run(), 1);
  assert.equal(await run(), 2);
  assert.equal(runs, 2);
});

test('falha é compartilhada por todos os chamadores concorrentes', async () => {
  let runs = 0;
  const failure = new Error('invalid_token');
  const run = createSingleFlight(async () => {
    runs += 1;
    await tick();
    throw failure;
  });

  const outcomes = await Promise.allSettled([run(), run(), run()]);

  assert.equal(runs, 1);
  assert.ok(outcomes.every((o) => o.status === 'rejected'));
  assert.ok(outcomes.every((o) => o.status === 'rejected' && o.reason === failure));
});

test('após falhar, a próxima chamada tenta de novo', async () => {
  let runs = 0;
  const run = createSingleFlight(async () => {
    runs += 1;
    await tick();
    if (runs === 1) {
      throw new Error('falha transitória');
    }
    return 'ok';
  });

  await assert.rejects(() => run());
  assert.equal(await run(), 'ok');
  assert.equal(runs, 2);
});

// Guarda estrutural: o coordenador só protege algo se o cliente realmente o usar.
// Sem isto, voltar a chamar o refresh por requisição não falharia teste nenhum.
test('o cliente renova a sessão através do coordenador', async () => {
  const source = await readFile(new URL('../src/api/client.ts', import.meta.url), 'utf8');

  // Exatamente uma chamada de refresh: a que vive dentro do coordenador.
  // Duas significa que algum caminho voltou a renovar por conta própria (A9).
  const refreshRequests = source.match(/axios\.post\([\s\S]{0,120}?\/api\/auth\/refresh/g) ?? [];
  assert.equal(refreshRequests.length, 1, 'o refresh deve ser chamado em um único lugar');

  assert.match(source, /createSingleFlight\(async \(\) => \{/, 'o refresh deve estar dentro do coordenador');
  assert.match(source, /await refreshAccessToken\(\)/, 'o interceptor deve usar o coordenador');
});
