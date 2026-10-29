import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

/**
 * Guarda da configuração de deploy.
 *
 * O deploy falhou duas vezes porque a Vercel executou os comandos de dentro de `frontend/`,
 * enquanto o `vercel.json` da raiz supõe a raiz do repositório (`cd frontend && ...`). A Vercel
 * lê **um único** `vercel.json`: o que está no Root Directory do projeto. Por isso existem dois
 * arquivos, cada um coerente com o seu diretório — e este teste impede que eles divirjam.
 */

interface VercelConfig {
  installCommand?: string;
  buildCommand?: string;
  outputDirectory?: string;
  rewrites?: { source: string; destination: string }[];
}

function readConfig(path: string): VercelConfig {
  return JSON.parse(readFileSync(new URL(path, import.meta.url), 'utf8')) as VercelConfig;
}

// O teste vive em `frontend/tests/`: a raiz do repositório está dois níveis acima.
const rootConfig = readConfig('../../vercel.json');
const frontendConfig = readConfig('../vercel.json');

test('as duas configurações aplicam os mesmos rewrites', () => {
  assert.deepEqual(rootConfig.rewrites, frontendConfig.rewrites);
});

test('configuração da raiz pressupõe o diretório do repositório', () => {
  assert.match(rootConfig.installCommand ?? '', /^cd frontend && /);
  assert.match(rootConfig.buildCommand ?? '', /^cd frontend && /);
  assert.equal(rootConfig.outputDirectory, 'frontend/dist');
});

test('configuração de frontend pressupõe já estar em frontend/', () => {
  assert.doesNotMatch(frontendConfig.installCommand ?? '', /cd /);
  assert.doesNotMatch(frontendConfig.buildCommand ?? '', /cd /);
  assert.equal(frontendConfig.outputDirectory, 'dist');
});

test('o rewrite da API não foi removido (sem ele /api viraria HTML do SPA)', () => {
  const api = rootConfig.rewrites?.find((rewrite) => rewrite.source === '/api/:path*');
  assert.ok(api, 'rewrite de /api ausente');
  assert.match(api.destination, /^https:\/\//);
});

test('o fallback do SPA continua sendo a última regra', () => {
  const rewrites = rootConfig.rewrites ?? [];
  assert.equal(rewrites.at(-1)?.destination, '/index.html');
});
