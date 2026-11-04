import type { MessageKey } from '../i18n/messages';

/**
 * Dono único do vocabulário de status exibido em etiqueta.
 *
 * O valor que chega da API é o nome do enum e continua sendo o contrato: nada aqui é enviado
 * ao servidor. O que faltava era a tradução — `StatusBadge` conhecia um único valor
 * (`DocumentacaoPendente`), então `Pendente`, `Aprovado`, `Rascunho`, `Ativo` e `Inativo`
 * apareciam crus em português mesmo com a interface em inglês ou espanhol. Isso afetava as
 * telas de documentos, matrículas e alunos.
 *
 * Fica fora do componente para poder ser verificado sem DOM, como os demais módulos de
 * domínio. Nenhuma tela deve manter um mapa próprio de status: um segundo mapa divergiria.
 *
 * `DocumentacaoPendente` aponta para `enrollment.status.documentsRequired` ("Documentação
 * pendente"): a etiqueta diz o que falta, em vez de repetir só a palavra "Documentação".
 */

const STATUS_LABEL_KEYS: Record<string, MessageKey> = {
  // Matrícula
  Rascunho: 'enrollment.status.draft',
  Pendente: 'enrollment.status.pending',
  DocumentacaoPendente: 'enrollment.status.documentsRequired',
  Aprovado: 'enrollment.status.approved',
  Rejeitado: 'enrollment.status.rejected',
  Cancelado: 'enrollment.status.cancelled',
  // Aluno
  Ativo: 'student.status.active',
  Inativo: 'student.status.inactive',
  Transferido: 'student.status.transferred',
};

/**
 * Chave de catálogo para o status, ou `null` quando o valor não é conhecido — a tela decide
 * mostrar o valor cru em vez de esconder a informação.
 */
export function statusLabelKey(status: string): MessageKey | null {
  return STATUS_LABEL_KEYS[status] ?? null;
}
