import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

/**
 * B11 — o PWA só é instalável se o HTML apontar para um manifest VÁLIDO e se os ícones que ele
 * declara existirem de verdade, com o tamanho declarado.
 *
 * O defeito era silencioso: os arquivos existiam (70 bytes, 1×1 px) e o manifest era copiado,
 * mas o `index.html` não tinha `<link rel="manifest">` — nada falhava no build, e o app
 * simplesmente não era instalável. Estes testes olham o fonte, não o `dist`, para rodarem antes
 * do build.
 */

const root = new URL('..', import.meta.url);

async function readText(path: string): Promise<string> {
  return readFile(new URL(path, root), 'utf8');
}

function pngSize(bytes: Buffer): { width: number; height: number } {
  const signature = bytes.subarray(0, 8).toString('hex');
  assert.equal(signature, '89504e470d0a1a0a', 'não é um PNG');
  assert.equal(bytes.subarray(12, 16).toString('ascii'), 'IHDR', 'PNG sem IHDR');
  return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) };
}

test('index.html declara o manifest', async () => {
  const html = await readText('index.html');
  assert.match(html, /<link[^>]+rel="manifest"[^>]+href="\/manifest\.json"/);
});

test('manifest declara ícones 192 e 512 que existem e têm o tamanho certo', async () => {
  const manifest = JSON.parse(await readText('public/manifest.json')) as {
    name: string;
    start_url: string;
    display: string;
    icons: Array<{ src: string; sizes: string; type: string }>;
  };

  assert.ok(manifest.name.length > 0);
  assert.equal(manifest.display, 'standalone');
  assert.ok(manifest.start_url.startsWith('/'));

  const bySize = new Map(manifest.icons.map((icon) => [icon.sizes, icon]));

  for (const size of ['192x192', '512x512']) {
    const icon = bySize.get(size);
    assert.ok(icon, `manifest sem ícone ${size}`);
    assert.equal(icon.type, 'image/png');

    const bytes = await readFile(new URL(`public${icon.src}`, root));
    const expected = Number(size.split('x')[0]);
    const actual = pngSize(bytes);

    assert.equal(actual.width, expected, `${icon.src} com largura errada`);
    assert.equal(actual.height, expected, `${icon.src} com altura errada`);
    // Guarda contra o ícone-placeholder de 1×1 (70 bytes) que existia antes do B11.
    assert.ok(bytes.length > 500, `${icon.src} parece um placeholder (${bytes.length} bytes)`);
  }
});
