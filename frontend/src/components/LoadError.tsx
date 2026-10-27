import { AlertTriangle } from 'lucide-react';
import { Button } from './Button';

/**
 * Estado de erro obrigatório de toda tela que carrega dados (A8).
 *
 * Antes, uma falha de carregamento deixava a tela mostrando a lista vazia —
 * o usuário concluía que não havia dados, quando na verdade a consulta falhou.
 */
export function LoadError({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div
      role="alert"
      className="flex flex-col items-center gap-3 rounded-lg border border-red-200 bg-red-50 p-6 text-center"
    >
      <AlertTriangle className="text-red-600" size={24} aria-hidden="true" />
      <p className="text-sm text-red-800">{message}</p>
      {onRetry && (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          Tentar novamente
        </Button>
      )}
    </div>
  );
}
