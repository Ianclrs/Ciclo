import { useEffect, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import { apiErrorMessage } from '../../api/errors';
import { useLocale } from '../../i18n/LocaleContext';
import { formatDate } from '../../utils/format';
import { getChildren } from '../../api/parent';
import {
  downloadStudentExport,
  getConsents,
  getPrivacyRequests,
  submitPrivacyRequest,
  type Consent,
  type PrivacyRequest,
  type PrivacyRequestType,
} from '../../api/privacy';
import type { Student } from '../../types';

/** Tipos que o responsável pode pedir. A revogação de consentimento é feita pelo colégio. */
const REQUESTABLE: { type: PrivacyRequestType; label: string }[] = [
  { type: 'Export', label: 'Solicitar exportação' },
  { type: 'Anonymization', label: 'Solicitar anonimização' },
];

/** Solicitação ainda em andamento: impede pedir a mesma coisa duas vezes para o mesmo aluno. */
function hasOpenRequest(
  requests: PrivacyRequest[],
  studentId: string,
  type: PrivacyRequestType,
): boolean {
  return requests.some(
    (request) =>
      request.subjectStudentId === studentId &&
      request.type === type &&
      (request.status === 'Submitted' || request.status === 'Approved'),
  );
}

/**
 * Central de privacidade do responsável (Spec 210, FR-006).
 *
 * Mostra, por filho vinculado, os consentimentos registrados e permite pedir exportação ou
 * anonimização. O pedido fica desabilitado enquanto existir solicitação aberta do mesmo tipo.
 * A exportação é baixada de um `Blob` da rota de exportação — nenhum arquivo é montado aqui.
 *
 * Toda autorização é da API: o vínculo com o aluno é conferido no servidor, e a tela apenas
 * apresenta o erro devolvido, preservando a lista já carregada.
 */
export default function PrivacyCenter() {
  const { locale } = useLocale();
  const [children, setChildren] = useState<Student[] | null>(null);
  const [consents, setConsents] = useState<Record<string, Consent[]>>({});
  const [requests, setRequests] = useState<PrivacyRequest[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  /** Chave ocupada: `${alunoId}:${tipo}` para pedido, `export:${alunoId}` para download. */
  const [busy, setBusy] = useState<string | null>(null);
  /**
   * Já houve carga bem-sucedida. Uma falha depois disso é de recarga: vira aviso e a lista
   * anterior fica na tela; antes da primeira carga não há o que preservar, então vira `LoadError`.
   */
  const loaded = useRef(false);

  useEffect(() => {
    let active = true;

    const load = async () => {
      const [kids, openRequests] = await Promise.all([getChildren(), getPrivacyRequests()]);
      const perChild = await Promise.all(
        kids.map(async (child) => [child.id, await getConsents(child.id)] as const),
      );

      if (!active) return;
      loaded.current = true;
      setChildren(kids);
      setRequests(openRequests);
      setConsents(Object.fromEntries(perChild));
      setError(null);
    };

    load().catch((err: unknown) => {
      if (!active) return;
      // Com dados já na tela, a falha é da recarga: avisa e mantém a lista anterior.
      const message = apiErrorMessage(err, 'Não foi possível carregar os dados de privacidade.');
      if (loaded.current) toast.error(message);
      else setError(message);
    });

    return () => {
      active = false;
    };
  }, [reloadKey]);

  const retry = () => {
    setChildren(null);
    setReloadKey((key) => key + 1);
  };

  const submitRequest = async (child: Student, type: PrivacyRequestType) => {
    setBusy(`${child.id}:${type}`);

    try {
      await submitPrivacyRequest({ studentId: child.id, type });
      toast.success('Solicitação enviada.');
      setReloadKey((key) => key + 1);
    } catch (err: unknown) {
      toast.error(apiErrorMessage(err, 'Não foi possível enviar a solicitação.'));
    } finally {
      setBusy(null);
    }
  };

  const downloadExport = async (child: Student) => {
    setBusy(`export:${child.id}`);

    try {
      const blob = await downloadStudentExport(child.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      // Mesmo nome que a API sugere no cabeçalho do arquivo.
      link.download = `privacy-export-${child.id}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err: unknown) {
      // Nenhum arquivo é criado quando o download falha.
      toast.error(apiErrorMessage(err, 'Não foi possível baixar o documento.'));
    } finally {
      setBusy(null);
    }
  };

  return (
    <div>
      <PageHeader
        title="Privacidade"
        subtitle="Consentimentos registrados e solicitações sobre os dados dos seus filhos."
      />

      {error !== null ? (
        <LoadError message={error} onRetry={retry} />
      ) : children === null ? (
        <Card>
          <SkeletonRows rows={3} />
        </Card>
      ) : children.length === 0 ? (
        <Card>
          <EmptyState message="Nenhum aluno vinculado ao seu acesso." />
        </Card>
      ) : (
        <div className="space-y-6">
          {children.map((child) => {
            const childConsents = consents[child.id] ?? [];

            return (
              <Card key={child.id} title={`${child.nome} — ${child.turma}`}>
                <div className="space-y-4">
                  {childConsents.length === 0 ? (
                    <p className="text-sm text-gray-600">Nenhum consentimento registrado.</p>
                  ) : (
                    <ul className="space-y-2">
                      {childConsents.map((consent) => (
                        <li
                          key={consent.id}
                          className="flex flex-wrap items-center justify-between gap-2 border-b border-gray-100 pb-2 last:border-0 last:pb-0"
                        >
                          <div className="min-w-0">
                            <p className="text-sm font-medium text-gray-800">{consent.purpose}</p>
                            <p className="text-xs text-gray-600">
                              Base legal: {consent.legalBasis} · Concedido em{' '}
                              {formatDate(consent.grantedAt, locale)}
                              {consent.revokedAt === null
                                ? ''
                                : ` · Revogado em ${formatDate(consent.revokedAt, locale)}`}
                            </p>
                          </div>
                          <Badge variant={consent.status === 'Granted' ? 'success' : 'default'}>
                            {consent.status === 'Granted' ? 'Concedido' : 'Revogado'}
                          </Badge>
                        </li>
                      ))}
                    </ul>
                  )}

                  <div className="flex flex-wrap gap-2">
                    {REQUESTABLE.map(({ type, label }) => {
                      const open = hasOpenRequest(requests, child.id, type);
                      const key = `${child.id}:${type}`;

                      return (
                        <Button
                          key={type}
                          size="sm"
                          variant="secondary"
                          onClick={() => submitRequest(child, type)}
                          disabled={open || busy === key}
                        >
                          {busy === key ? 'Enviando…' : label}
                        </Button>
                      );
                    })}

                    <Button
                      size="sm"
                      onClick={() => downloadExport(child)}
                      disabled={busy === `export:${child.id}`}
                    >
                      {busy === `export:${child.id}` ? 'Baixando…' : 'Baixar exportação'}
                    </Button>
                  </div>

                  <p className="text-xs text-gray-500">
                    Pedidos com solicitação aberta do mesmo tipo ficam desabilitados até a
                    conclusão pelo colégio.
                  </p>
                </div>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
