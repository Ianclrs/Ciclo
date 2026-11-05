import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
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
import {
  LANGUAGE_NAMES,
  LANGUAGE_ORDER,
  defaultLocaleForLanguage,
  languageForLocale,
  readStoredLanguage,
} from '../../i18n/locale';
import type { MessageKey } from '../../i18n/messages';
import { TENANT_FEATURES, type TenantFeatureName } from '../../domain/features';
import {
  getTenantConfiguration,
  updateTenantConfiguration,
  type TenantConfiguration,
  type UpdateTenantConfiguration,
} from '../../api/tenants';

/** Planos aceitos pela API (`TenantPlan`), na ordem de exibição. */
const PLANS = ['Basic', 'Standard', 'Premium'];

/**
 * Texto de cada funcionalidade conhecida. O mapa é exaustivo pelo tipo: uma flag nova no dono
 * único (`domain/features`) quebra a compilação aqui até ganhar rótulo no catálogo.
 */
const FEATURE_LABELS: Record<TenantFeatureName, MessageKey> = {
  Documents: 'settings.flag.documents',
  Notifications: 'settings.flag.notifications',
  ParentPortal: 'settings.flag.parentPortal',
  AcademicPerformance: 'settings.flag.academic',
  BatchReenrollment: 'settings.flag.reenrollment',
  AuditExport: 'settings.flag.audit',
};

/** Opções do painel na ordem do dono único; o `value` é o identificador persistido pela API. */
const FEATURE_FLAGS = TENANT_FEATURES.map((value) => ({ value, labelKey: FEATURE_LABELS[value] }));

/**
 * Opções de idioma da escola: o valor é o locale que a API persiste, o rótulo é o nome nativo.
 * Derivado do dono único (`i18n/locale.ts`), sem repetir códigos de locale nesta tela.
 */
const LOCALE_OPTIONS = LANGUAGE_ORDER.map((language) => ({
  value: defaultLocaleForLanguage(language),
  label: LANGUAGE_NAMES[language],
}));

const BYTES_PER_MB = 1024 * 1024;

let cachedTimeZones: string[] | null = null;

/**
 * Fusos conhecidos pela plataforma, para sugerir valores sem manter uma lista própria que
 * envelhece. `supportedValuesOf` não existe em todo motor: sem ele o campo continua livre.
 */
function supportedTimeZones(): string[] {
  if (cachedTimeZones !== null) return cachedTimeZones;

  try {
    const intl = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
    cachedTimeZones = intl.supportedValuesOf?.('timeZone') ?? [];
  } catch {
    // Motor sem a API: o campo permanece texto livre e a API valida o valor.
    cachedTimeZones = [];
  }

  return cachedTimeZones;
}

/**
 * Formulário em texto: campo vazio significa "sem valor" para a API, que distingue nulo
 * (preserva) de branco (limpa). Tipos numéricos ficam como texto para não perder o estado
 * intermediário da digitação (o campo vazio viraria 0).
 */
interface FormState {
  displayName: string;
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
 * Configuração da escola (Spec 200, FR-006), em quatro seções: identidade, preferências
 * regionais, limites e plano. Agrupar por significado — e não por quantidade de campos — é o
 * que permite abrir a tela sabendo onde cada ajuste mora.
 *
 * As quatro seções ficam numa mesma grade, preenchida na ordem: identidade ao lado de
 * preferências regionais, e limites ao lado de plano e funcionalidades. Uma grade só mantém o
 * par alinhado sem duplicar contêineres. Ela abre duas colunas a partir de `md` — a janela
 * pela metade já mostra o arranjo 2×2 — e abaixo disso empilha na mesma ordem.
 *
 * A autorização real é da API: o `GET` exige sessão e o `PUT` exige papel Admin. A rota já é
 * restrita ao Admin em `App.tsx`; a tela não repete a regra, apenas apresenta o código de erro
 * devolvido em recusa (400/403) sem limpar o que está na tela.
 *
 * O campo "idioma padrão da escola" troca a interface no ato, como o seletor da topbar: escolher
 * no `select` não pode parecer inerte. A escolha também fica no rascunho e só o "Salvar" a
 * persiste como padrão do colégio — até lá a tela avisa que há alteração não salva.
 */
export default function TenantSettings() {
  const { language, t, setLanguage } = useLocale();
  const [config, setConfig] = useState<TenantConfiguration | null>(null);
  const [form, setForm] = useState<FormState | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<{ error: unknown } | null>(null);
  const [saveError, setSaveError] = useState<{ error: unknown } | { textKey: MessageKey } | null>(null);
  const [saving, setSaving] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  const timeZones = useMemo(() => supportedTimeZones(), []);

  /**
   * Caminho único do locale da escola até o idioma da interface. Serve à semeadura da primeira
   * visita e à escolha feita neste campo: duas conversões separadas divergiriam.
   */
  const applySchoolLocale = useCallback(
    (localeValue: string): void => {
      const nextLanguage = languageForLocale(localeValue);
      if (nextLanguage !== null) setLanguage(nextLanguage);
    },
    [setLanguage],
  );

  useEffect(() => {
    let cancelled = false;

    getTenantConfiguration()
      .then((loaded) => {
        if (cancelled) return;
        setConfig(loaded);
        setForm(formFrom(loaded));
        setLoading(false);

        // Primeira visita sem preferência salva: o padrão do colégio vira o idioma da
        // interface. Depois que o usuário escolhe — aqui ou na topbar — a escolha dele vence.
        if (readStoredLanguage() === null) applySchoolLocale(loaded.locale);
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setLoadError({ error });
        setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey, applySchoolLocale]);

  /**
   * Único cálculo do diff: alimenta o botão (habilitado só com alteração), o aviso de estado e
   * o corpo do `PUT`. `null` significa número inválido — diferente de "nada mudou".
   */
  const draft = useMemo<UpdateTenantConfiguration | null>(() => {
    if (!config || !form) return null;

    const defaultSchoolYear = parseNumber(form.defaultSchoolYear);
    const storageMegabytes = parseNumber(form.storageMegabytes);
    const userQuota = parseNumber(form.userQuota);

    if (defaultSchoolYear === null || storageMegabytes === null || userQuota === null) return null;

    return buildPayload(form, config, {
      defaultSchoolYear,
      storageQuotaBytes: Math.round(storageMegabytes * BYTES_PER_MB),
      userQuota,
    });
  }, [config, form]);

  const isDirty = draft === null || Object.keys(draft).length > 0;

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

  function discard(): void {
    if (!config) return;
    setForm(formFrom(config));
    setSaveError(null);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    if (!config || !form || saving) return;

    if (draft === null) {
      setSaveError({ textKey: 'settings.error.numbers' });
      return;
    }
    // Conjunto vazio é recusado aqui porque não é distinguível de "nunca configurado" no banco —
    // e essa ambiguidade faria o portão da API tratar uma escola desligada como escola padrão.
    if (draft.featureFlags !== undefined && draft.featureFlags.length === 0) {
      setSaveError({ textKey: 'settings.error.featuresRequired' });
      return;
    }


    // Envio sem alteração não chama a API: evita escrita vazia e revalidação sem motivo.
    if (Object.keys(draft).length === 0) {
      toast(t('settings.state.noChanges'));
      return;
    }

    setSaving(true);
    setSaveError(null);
    try {
      const updated = await updateTenantConfiguration(draft);
      setConfig(updated);
      setForm(formFrom(updated));
      toast.success(t('settings.state.saved'));
    } catch (error: unknown) {
      // Recusa mantém os valores carregados: o formulário não é limpo.
      setSaveError({ error });
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <PageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />

      {loadError && (
        <LoadError
          message={apiErrorMessage(loadError.error, t('settings.error.load'), language)}
          onRetry={retry}
        />
      )}

      {loading && !loadError && (
        <Card tone="glass">
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
              {'textKey' in saveError
                ? t(saveError.textKey)
                : apiErrorMessage(saveError.error, t('settings.error.save'), language)}
            </div>
          )}

          <div className="grid gap-6 md:grid-cols-2">
            <Card tone="glass" title={t('settings.section.identity')}>
              <Input
                label={t('settings.field.displayName')}
                value={form.displayName}
                onChange={(event) => update({ displayName: event.target.value })}
              />
            </Card>

            <Card tone="glass" title={t('settings.section.regional')}>
              <div className="space-y-4">
                <div>
                  <Input
                    label={t('settings.field.timeZone')}
                    placeholder="America/Sao_Paulo"
                    list="tenant-time-zones"
                    value={form.timeZone}
                    onChange={(event) => update({ timeZone: event.target.value })}
                  />
                  <datalist id="tenant-time-zones">
                    {timeZones.map((zone) => (
                      <option key={zone} value={zone} />
                    ))}
                  </datalist>
                </div>

                <Input
                  label={t('settings.field.schoolYear')}
                  type="number"
                  value={form.defaultSchoolYear}
                  onChange={(event) => update({ defaultSchoolYear: event.target.value })}
                />

                <div>
                  <Select
                    label={t('settings.field.locale')}
                    value={form.locale}
                    onChange={(event) => {
                      // Troca a interface no ato e mantém a escolha no rascunho: o botão
                      // "Salvar" é quem persiste o padrão do colégio.
                      update({ locale: event.target.value });
                      applySchoolLocale(event.target.value);
                    }}
                  >
                    {LOCALE_OPTIONS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </Select>
                  <p className="mt-1 text-xs text-gray-500">{t('settings.hint.locale')}</p>
                </div>
              </div>
            </Card>

            <Card tone="glass" title={t('settings.section.limits')}>
              <div className="space-y-4">
                <Input
                  label={t('settings.field.storage')}
                  type="number"
                  min={1}
                  value={form.storageMegabytes}
                  onChange={(event) => update({ storageMegabytes: event.target.value })}
                />
                <Input
                  label={t('settings.field.userQuota')}
                  type="number"
                  min={1}
                  value={form.userQuota}
                  onChange={(event) => update({ userQuota: event.target.value })}
                />
              </div>
            </Card>

            <Card tone="glass" title={t('settings.section.plan')}>
              <Select
                label={t('settings.field.plan')}
                className="w-full"
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
                <legend className="text-sm font-medium text-gray-700">
                  {t('settings.features.legend')}
                </legend>
                {/*
                 * Largura da coluna manda nesta grade: em `md` o cartão passa a dividir a linha
                 * e duas colunas de caixas de seleção ficariam com ~100px cada, quebrando
                 * rótulos como "Portal do responsável". Por isso duas colunas só quando o cartão
                 * está largo (tela cheia em `sm`, metade larga a partir de `lg`).
                 */}
                <div className="mt-2 grid gap-2 sm:grid-cols-2 md:grid-cols-1 lg:grid-cols-2">
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
                      {t(flag.labelKey)}
                    </label>
                  ))}
                </div>
              </fieldset>

              <p className="mt-3 text-xs text-gray-500">{t('settings.hint.plan')}</p>
            </Card>
          </div>

          <div className="flex flex-wrap items-center justify-end gap-3">
            {isDirty && (
              <span className="mr-auto text-sm text-amber-700">{t('settings.state.unsaved')}</span>
            )}
            <Button type="button" variant="ghost" onClick={discard} disabled={!isDirty || saving}>
              {t('settings.action.discard')}
            </Button>
            <Button type="submit" disabled={!isDirty || saving}>
              {saving ? t('settings.action.saving') : t('settings.action.save')}
            </Button>
          </div>
        </form>
      )}
    </>
  );
}
