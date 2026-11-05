import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (path: string) => readFile(new URL(`../${path}`, import.meta.url), 'utf8');

/**
 * Foto de perfil do usuário (Admin, Secretaria e responsável). O reconhecimento em listas longas
 * depende de a foto chegar até a lista — e de a redução no navegador ser uma só, não uma cópia
 * por tela.
 */
test('a redução da imagem tem um dono só, usado pelo aluno e pelo perfil', async () => {
  const form = await read('src/pages/admin/StudentForm.tsx');
  const sidebar = await read('src/components/Sidebar.tsx');

  assert.doesNotMatch(
    form,
    /async function toResizedDataUrl/,
    'a tela do aluno voltou a ter a própria cópia da redução',
  );
  assert.match(
    form,
    /import \{[^}]*toResizedDataUrl[^}]*\} from '\.\.\/\.\.\/utils\/image'/,
    'a tela do aluno perdeu a importação do dono único',
  );
  assert.match(sidebar, /from '\.\.\/utils\/image'/, 'o perfil não usa o dono único da redução');
});

test('o diálogo da conta permite escolher e remover a foto', async () => {
  const sidebar = await read('src/components/Sidebar.tsx');

  assert.match(sidebar, /type="file"/, 'o perfil perdeu o seletor de arquivo');
  assert.match(
    sidebar,
    /accept="image\/png,image\/jpeg,image\/webp"/,
    'o seletor aceita formatos que a API recusa',
  );
  assert.match(sidebar, /onChange=\{handlePhotoChange\}/, 'escolher um arquivo não faz nada');
  assert.match(sidebar, /\{t\('profile\.removePhoto'\)\}/, 'não há como remover a foto');
});

test('a foto é enviada ao dono da escrita e o estado do usuário acompanha', async () => {
  const auth = await read('src/api/auth.ts');
  const context = await read('src/context/AuthContext.tsx');

  assert.match(auth, /'\/api\/auth\/me\/photo'/, 'a API da foto de perfil sumiu');
  assert.match(
    context,
    /authApi\.updateMyPhoto\(foto\)/,
    'o contexto não envia a foto pelo caminho existente',
  );
  assert.match(
    context,
    /setUser\(\(current\) => \(current \? \{ \.\.\.current, foto: updated\.foto \?\? null \}/,
    'o contexto não propaga a foto nova para a sidebar e as listas',
  );
});

test('a lista de usuários mostra a foto, não só as iniciais', async () => {
  const page = await read('src/pages/admin/UserManagement.tsx');

  assert.match(
    page,
    /<StudentAvatar name=\{item\.name\} photo=\{item\.foto\} size="sm" \/>/,
    'a lista perdeu a foto — o reconhecimento em muitos cadastros volta a depender do nome',
  );
});
