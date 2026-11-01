import { useEffect, useState, type FormEvent } from 'react';
import toast from 'react-hot-toast';
import { Card } from '../../components/Card';
import { PageHeader } from '../../components/PageHeader';
import { Button } from '../../components/Button';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { LoadError } from '../../components/LoadError';
import { SkeletonRows } from '../../components/Skeleton';
import { apiErrorMessage } from '../../api/errors';
import { useLocale } from '../../i18n/LocaleContext';
import { LANGUAGE_ORDER, defaultLocaleForLanguage } from '../../i18n/locale';
import {
  getTenantConfiguration,
  updateTenantConfiguration,
  type TenantConfiguration,
  type UpdateTenantConfiguration,
} from '../../api/tenants';

/** Planos aceitos pela API (`TenantPlan`), na ordem de exibição. */
const PLANS = ['Basic', 'Standard', 'Premium'];

/**
 * Flags conhecidas (`TenantFeature`). O `value` é o identificador que a API persiste; o rótulo
 * é só de exibição e não participa do contrato.
 */
const FEATURE_FLAGS = [
  { value: 'Documents', label: 'Documentos' },
  { value: 'Notifications', label: 'Notificações' },
  { value: 'ParentPortal', label: 'Portal do responsável' },
  { value: 'AcademicPerformance', label: 'Desempenho acadêmico' },
  { value: 'BatchReenrollment', label: 'Rematrícula em lote' },
  { value: 'AuditExport', label: 'Exportação de auditoria' },
];

/**
 * Locales aceitos pela API na configuração da escola. Derivados do dono único do mapeamento
 * idioma → locale (`i18n/locale.ts`), em vez de repetir os códigos aqui.
 */
const LOCALES = LANGUAGE_ORDER.map(defaultLocaleForLanguage);

const BYTES_PER_MB = 1024 * 1024;

/**
 * Formulário em texto: campo vazio significa "sem valor" para a API, que distingue nulo
 * (preserva) de branco (limpa). Tipos numéricos ficam como texto para não perder o estado
 * intermediário da digitação (o campo vazio viraria 0).
 */
interface FormState {
  displayName: string;
  logoUrl: string;
  primaryColor: string;
  secondaryColor: string;
  timeZone: string;
  defaultSchoolYear: string;
  locale: string;
  storageMegabytes: string;
  userQuota: string;
  plan: string;
  featureFlags: string[];
}

interface Numbers {
  defaultSchoolYear: number;
  storageQuotaBytes: number;
  userQuota: number;
}

function formFrom(config: TenantConfiguration): FormState {
  return {
    displayName: config.displayName,
    logoUrl: config.logoUrl ?? '',
    primaryColor: config.primaryColor ?? '',
    secondaryColor: config.secondaryColor ?? '',
    timeZone: config.timeZone,
    defaultSchoolYear: String(config.defaultSchoolYear),
    locale: config.locale,
    storageMegabytes: String(Math.round(config.storageQuotaBytes / BYTES_PER_MB)),
    userQuota: String(config.userQuota),
    plan: config.plan,
    featureFlags: config.featureFlags,
  };
}

/** Cores são gravadas em maiúsculas pela API: comparar sem caixa evita escrita vazia. */
function textChanged(before: string | null, after: string, caseless = false): boolean {
  const previous = (before ?? '').trim();
  const next = after.trim();
  return caseless ? previous.toLowerCase() !== next.toLowerCase() : previous !== next;
}

function flagsChanged(next: string[], previous: string[]): boolean {
  const sortedNext = [...next].sort();
  const sortedPrevious = [...previous].sort();
  return (
    sortedNext.length !== sortedPrevious.length ||
    sortedNext.some((flag, index) => flag !== sortedPrevious[index])
  );
}

function parseNumber(value: string): number | null {
  const parsed = Number(value.trim());
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * Só o que mudou vai no corpo: campo ausente preserva o valor atual (ADR-200-3). As regras de
 * faixa continuam só na API — aqui não há validação de domínio duplicada.
 */
function buildPayload(
  form: FormState,
  config: TenantConfiguration,
  numbers: Numbers,
): UpdateTenantConfiguration {
  const payload: UpdateTenantConfiguration = {};

  if (textChanged(config.displayName, form.displayName)) payload.displayName = form.displayName.trim();
  if (textChanged(config.logoUrl, form.logoUrl)) payload.logoUrl = form.logoUrl.trim();
  if (textChanged(config.primaryColor, form.primaryColor, true)) {
    payload.primaryColor = form.primaryColor.trim();
  }
  if (textChanged(config.secondaryColor, form.secondaryColor, true)) {
    payload.secondaryColor = form.secondaryColor.trim();
  }
  if (textChanged(config.timeZone, form.timeZone)) payload.timeZone = form.timeZone.trim();
  if (numbers.defaultSchoolYear !== config.defaultSchoolYear) {
    payload.defaultSchoolYear = numbers.defaultSchoolYear;
  }
  if (form.locale !== config.locale) payload.locale = form.locale;
  if (numbers.storageQuotaBytes !== config.storageQuotaBytes) {
    payload.storageQuotaBytes = numbers.storageQuotaBytes;
  }
  if (numbers.userQuota !== config.userQuota) payload.userQuota = numbers.userQuota;
  if (form.plan !== config.plan) payload.plan = form.plan;
  if (flagsChanged(form.featureFlags, config.featureFlags)) payload.featureFlags = form.featureFlags;

  return payload;
}

/**
 * Configuração da escola (Spec 200, FR-006).
 *
 * A autorização real é da API: o `GET` exige sessão e o `PUT` exige papel Admin. A rota já é
 * restrita ao Admin em `App.tsx`; a tela não repete a regra, apenas apresenta o código de erro
 * devolvido em recusa (400/403) sem limpar o que está na tela.
 */
export default function TenantSettings() {
  const { language } = useLocale();
  const [config, setConfig] = useState<TenantConfiguration | null>(null);
  const [form, setForm] = useState<FormState | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<{ error: unknown } | null>(null);
  const [saveError, setSaveError] = useState<{ error: unknown } | { text: string } | null>(null);
  const [saving, setSaving] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    getTenantConfiguration()
      .then((loaded) => {
        if (cancelled) return;
        setConfig(loaded);
        setForm(formFrom(loaded));
        setLoading(false);
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setLoadError({ error });
        setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  /** O estado de carga é ligado no evento (aqui), não no efeito, para não re-renderizar em cascata. */
  function retry(): void {
    setLoading(true);
    setLoadError(null);
    setReloadKey((key) => key + 1);
  }

  function update(patch: Partial<FormState>): void {
    setForm((current) => (current ? { ...current, ...patch } : current));
  }

  function toggleFlag(flag: string): void {
    setForm((current) => {
      if (!current) return current;
      return {
        ...current,
        featureFlags: current.featureFlags.includes(flag)
          ? current.featureFlags.filter((item) => item !== flag)
          : [...current.featureFlags, flag],
      };
    });
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    if (!config || !form || saving) return;

    const year = parseNumber(form.defaultSchoolYear);
    const storageMegabytes = parseNumber(form.storageMegabytes);
    const userQuota = parseNumber(form.userQuota);

    if (year === null || storageMegabytes === null || userQuota === null) {
      setSaveError({ text: 'Informe números válidos no ano letivo e nas cotas.' });
      return;
    }

    const payload = buildPayload(form, config, {
      defaultSchoolYear: year,
      storageQuotaBytes: Math.round(storageMegabytes * BYTES_PER_MB),
      userQuota,
    });

    // Envio sem alteração não chama a API: evita escrita vazia e revalidação sem motivo.
    if (Object.keys(payload).length === 0) {
      toast('Nenhuma alteração para salvar.');
      return;
    }

    setSaving(true);
    setSaveError(null);
    try {
      const updated = await updateTenantConfiguration(payload);
      setConfig(updated);
      setForm(formFrom(updated));
      toast.success('Configuração salva.');
    } catch (error: unknown) {
      // Recusa mantém os valores carregados: o formulário não é limpo.
      setSaveError({ error });
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <PageHeader title="Configurações" subtitle="Identidade, limites e funcionalidades da escola." />

      {loadError && (
        <LoadError
          message={apiErrorMessage(
            loadError.error,
            'Não foi possível carregar a configuração da escola.',
            language,
          )}
          onRetry={retry}
        />
      )}

      {loading && !loadError && (
        <Card>
          <SkeletonRows rows={6} />
        </Card>
      )}

      {!loading && !loadError && form && (
        <form onSubmit={handleSubmit} className="space-y-6">
          {saveError && (
            <div
              role="alert"
              className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"
            >
              {'text' in saveError
                ? saveError.text
                : apiErrorMessage(
                    saveError.error,
                    'Não foi possível salvar a configuração.',
                    language,
                  )}
            </div>
          )}

          <Card title="Identidade da escola">
            <div className="grid gap-4 md:grid-cols-2">
              <Input
                label="Nome de exibição"
                value={form.displayName}
                onChange={(event) => update({ displayName: event.target.value })}
              />
              <Input
                label="URL do logotipo"
                value={form.logoUrl}
                onChange={(event) => update({ logoUrl: event.target.value })}
              />
              <Input
                label="Cor primária"
                placeholder="#1A2B3C"
                value={form.primaryColor}
                onChange={(event) => update({ primaryColor: event.target.value })}
              />
              <Input
                label="Cor secundária"
                placeholder="#1A2B3C"
                value={form.secondaryColor}
                onChange={(event) => update({ secondaryColor: event.target.value })}
              />
              <Input
                label="Fuso horário"
                placeholder="America/Sao_Paulo"
                value={form.timeZone}
                onChange={(event) => update({ timeZone: event.target.value })}
              />
              <Input
                label="Ano letivo padrão"
                type="number"
                value={form.defaultSchoolYear}
                onChange={(event) => update({ defaultSchoolYear: event.target.value })}
              />
              <Select
                label="Idioma"
                value={form.locale}
                onChange={(event) => update({ locale: event.target.value })}
              >
                {LOCALES.map((locale) => (
                  <option key={locale} value={locale}>
                    {locale}
                  </option>
                ))}
              </Select>
            </div>
          </Card>

          <Card title="Cotas">
            <div className="grid gap-4 md:grid-cols-2">
              <Input
                label="Cota de armazenamento (MB)"
                type="number"
                min={1}
                value={form.storageMegabytes}
                onChange={(event) => update({ storageMegabytes: event.target.value })}
              />
              <Input
                label="Cota de usuários"
                type="number"
                min={1}
                value={form.userQuota}
                onChange={(event) => update({ userQuota: event.target.value })}
              />
            </div>
          </Card>

          <Card title="Plano e funcionalidades">
            <Select
              label="Plano"
              className="md:w-64"
              value={form.plan}
              onChange={(event) => update({ plan: event.target.value })}
            >
              {PLANS.map((plan) => (
                <option key={plan} value={plan}>
                  {plan}
                </option>
              ))}
            </Select>

            <fieldset className="mt-4">
              <legend className="text-sm font-medium text-gray-700">Funcionalidades</legend>
              <div className="mt-2 grid gap-2 md:grid-cols-2">
                {FEATURE_FLAGS.map((flag) => (
                  <label
                    key={flag.value}
                    htmlFor={`flag-${flag.value}`}
                    className="flex items-center gap-2 text-sm text-gray-700"
                  >
                    <input
                      id={`flag-${flag.value}`}
                      type="checkbox"
                      checked={form.featureFlags.includes(flag.value)}
                      onChange={() => toggleFlag(flag.value)}
                      className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                    />
                    {flag.label}
                  </label>
                ))}
              </div>
            </fieldset>

            <p className="mt-3 text-xs text-gray-500">
              Trocar o plano sem alterar as funcionalidades aplica o conjunto padrão do plano.
            </p>
          </Card>

          <div className="flex justify-end">
            <Button type="submit" disabled={saving}>
              {saving ? 'Salvando…' : 'Salvar'}
            </Button>
          </div>
        </form>
      )}
    </>
  );
}
