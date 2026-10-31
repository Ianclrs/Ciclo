/**
 * Marca de "a sessão acabou de nascer".
 *
 * Serve para a sidebar do desktop abrir já aberta depois do login. É uma marca de UMA vez:
 * o `AuthContext` grava no login e a `Sidebar` lê e apaga na primeira montagem. Sem o apagar,
 * a sidebar se reabriria em toda remontagem — inclusive depois de a pessoa tê-la fechado.
 *
 * Fica em `sessionStorage` porque a marca só faz sentido dentro desta aba/sessão do navegador:
 * uma nova aba é um novo login (ou nenhum), e não deve herdar a intenção de abrir.
 */

const KEY = 'ciclo.sessao.recem-iniciada';

/** Chamado pelo login bem-sucedido. */
export function markJustLoggedIn(): void {
  try {
    sessionStorage.setItem(KEY, '1');
  } catch {
    // Sem `sessionStorage` (ex.: cookies bloqueados): a sidebar abre no estado padrão.
    // Não é erro de carregamento de dados, então não há o que mostrar na tela.
  }
}

/** Espia a marca sem consumi-la — seguro para rodar duas vezes (StrictMode). */
export function wasJustLoggedIn(): boolean {
  try {
    return sessionStorage.getItem(KEY) === '1';
  } catch {
    return false;
  }
}

/** Apaga a marca: ela vale para a primeira montagem da sidebar, e só. */
export function clearJustLoggedIn(): void {
  try {
    sessionStorage.removeItem(KEY);
  } catch {
    // Sem `sessionStorage` a marca nunca existiu.
  }
}
