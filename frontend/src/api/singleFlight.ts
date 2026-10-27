/**
 * Serializa operações que não podem rodar em paralelo.
 *
 * Motivo (A9): o refresh de sessão rotaciona o token no servidor — o token
 * apresentado é revogado na mesma requisição. Se N requisições recebem 401 e
 * cada uma dispara seu próprio refresh, só a primeira vence; as demais enviam
 * o token já revogado, recebem 401 e a sessão é encerrada sem motivo.
 *
 * Com uma única execução em voo compartilhada, todos os chamadores recebem o
 * mesmo resultado (token novo ou a mesma falha).
 */
export function createSingleFlight<T>(operation: () => Promise<T>): () => Promise<T> {
  let inFlight: Promise<T> | null = null;

  return () => {
    if (inFlight === null) {
      inFlight = operation().finally(() => {
        inFlight = null;
      });
    }
    return inFlight;
  };
}
