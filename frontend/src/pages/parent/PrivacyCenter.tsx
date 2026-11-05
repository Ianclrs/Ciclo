import { useEffect, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { Check, CircleX, Download, ShieldCheck } from 'lucide-react';
import { Card } from '../../components/Card';
import { CardHeading } from '../../components/CardHeading';
import { PageHeader } from '../../components/PageHeader';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import { accentChip, type Accent } from '../../components/accentStyles';
import { apiErrorMessage } from '../../api/errors';
import { useLocale } from '../../i18n/LocaleContext';
import type { MessageKey } from '../../i18n/messages';
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
const REQUESTABLE: { type: PrivacyRequestType; labelKey: MessageKey }[] = [
  { type: 'Export', labelKey: 'priv.requestExport' },
  { type: 'Anonymization', labelKey: 'priv.requestAnonymization' },
];

/** Cor do chip do consentimento: concedido em verde, revogado em vermelho. */
const CONSENT_ACCENT: Record<string, Accent> = { Granted: 'green', Revoked: 'red' };

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
 *
 * Apresentação: um cartão por filho, com a identidade no cabeçalho, os consentimentos como
 * linhas de estado e as ações separadas por uma divisória. O aviso sobre pedidos abertos só
 * aparece quando existe um — antes ele era texto fixo embaixo de cada filho, mesmo sem nada
 * desabilitado.
 */
export default function PrivacyCenter() {
  const { language, locale, t } = useLocale();
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
      toast.success(t('priv.sent'));
      setReloadKey((key) => key + 1);
    } catch (err: unknown) {
      toast.error(apiErrorMessage(err, t('priv.sendError'), language));
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
      toast.error(apiErrorMessage(err, t('priv.downloadError'), language));
    } finally {
      setBusy(null);
    }
  };

  return (
    <div>
      <PageHeader title={t('nav.privacy')} subtitle={t('priv.centerSubtitle')} />

      {error !== null ? (
        <LoadError message={error} onRetry={retry} />
      ) : children === null ? (
        <Card tone="glass">
          <SkeletonRows rows={3} />
        </Card>
      ) : children.length === 0 ? (
        <Card tone="glass">
          <EmptyState message={t('priv.noStudents')} />
        </Card>
      ) : (
        <div className="space-y-6">
          {children.map((child) => {
            const childConsents = consents[child.id] ?? [];
            const openRequests = requests.filter(
              (request) =>
                request.subjectStudentId === child.id &&
                (request.status === 'Submitted' || request.status === 'Approved'),
            ).length;
            // O aviso só faz sentido quando há botão desabilitado para explicar.
            const blocked = REQUESTABLE.some(({ type }) => hasOpenRequest(requests, child.id, type));

            return (
              <Card key={child.id} tone="glass">
                <CardHeading
                  icon={ShieldCheck}
                  title={child.nome}
                  accent="violet"
                  action={
                    <div className="flex flex-wrap items-center justify-end gap-2">
                      <span className="rounded-full bg-white/80 px-3 py-1 text-xs font-medium text-gray-600 ring-1 ring-inset ring-gray-200">
                        {child.turma}
                      </span>
                      {openRequests > 0 && (
                        <Badge variant="warning">
                          {t('priv.openRequests', { count: openRequests })}
                        </Badge>
                      )}
                    </div>
                  }
                />

                <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-500">
                  {t('priv.consentsTitle')}
                </p>

                {childConsents.length === 0 ? (
                  <p className="py-2 text-sm text-gray-600">{t('priv.noConsents')}</p>
                ) : (
                  <ul className="space-y-1">
                    {childConsents.map((consent) => {
                      const granted = consent.status === 'Granted';
                      return (
                        <li
                          key={consent.id}
                          className="flex items-start gap-3 rounded-xl px-2 py-2.5 transition-colors hover:bg-white/60"
                        >
                          <span
                            className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ring-1 ring-inset ${accentChip[CONSENT_ACCENT[consent.status] ?? 'indigo']}`}
                          >
                            {granted ? (
                              <Check size={16} aria-hidden="true" />
                            ) : (
                              <CircleX size={16} aria-hidden="true" />
                            )}
                          </span>
                          <div className="min-w-0 flex-1">
                            <p className="text-sm font-semibold text-gray-900">{consent.purpose}</p>
                            <p className="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-xs text-gray-500">
                              <span>{t('priv.legalBasis', { basis: consent.legalBasis })}</span>
                              <span aria-hidden="true" className="text-gray-300">·</span>
                              <span>
                                {t('priv.grantedAt', { date: formatDate(consent.grantedAt, locale) })}
                              </span>
                              {consent.revokedAt !== null && (
                                <>
                                  <span aria-hidden="true" className="text-gray-300">·</span>
                                  <span className="text-red-600">
                                    {t('priv.revokedAt', {
                                      date: formatDate(consent.revokedAt, locale),
                                    })}
                                  </span>
                                </>
                              )}
                            </p>
                          </div>
                          <Badge variant={granted ? 'success' : 'default'}>
                            {granted ? t('priv.granted') : t('priv.revoked')}
                          </Badge>
                        </li>
                      );
                    })}
                  </ul>
                )}

                {/* Ações separadas do conteúdo por uma divisória: são o que se faz com o que
                    está acima, e a exportação é a única que não depende de aprovação. */}
                <div className="mt-5 flex flex-wrap items-center gap-2 border-t border-gray-200/70 pt-4">
                  <Button
                    size="sm"
                    className="gap-1.5"
                    onClick={() => downloadExport(child)}
                    disabled={busy === `export:${child.id}`}
                  >
                    <Download size={14} aria-hidden="true" />
                    {busy === `export:${child.id}` ? t('priv.downloading') : t('priv.downloadExport')}
                  </Button>

                  {REQUESTABLE.map(({ type, labelKey }) => {
                    const key = `${child.id}:${type}`;
                    return (
                      <Button
                        key={type}
                        size="sm"
                        variant="secondary"
                        onClick={() => submitRequest(child, type)}
                        disabled={hasOpenRequest(requests, child.id, type) || busy === key}
                      >
                        {busy === key ? t('priv.sending') : t(labelKey)}
                      </Button>
                    );
                  })}

                  {blocked && (
                    <p className="basis-full text-xs text-gray-500">{t('priv.openRequestHint')}</p>
                  )}
                </div>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
