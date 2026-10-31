import { AlertTriangle } from 'lucide-react';
import { Button } from './Button';
import { useLocale } from '../i18n/LocaleContext';

/**
 * Estado de erro obrigatório de toda tela que carrega dados (A8, Spec 190 FR-005).
 *
 * Antes, uma falha de carregamento deixava a tela mostrando a lista vazia — o usuário concluía
 * que não havia dados, quando na verdade a consulta falhou. O texto e o rótulo da ação agora
 * vêm do catálogo no idioma ativo, então a mensagem não fica presa ao português.
 */
export function LoadError({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const { t } = useLocale();

  return (
    <div
      role="alert"
      className="flex flex-col items-center gap-3 rounded-lg border border-red-200 bg-red-50 p-6 text-center"
    >
      <AlertTriangle className="text-red-600" size={24} aria-hidden="true" />
      <p className="text-sm text-red-800">{message || t('common.error')}</p>
      {onRetry && (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          {t('common.retry')}
        </Button>
      )}
    </div>
  );
}
