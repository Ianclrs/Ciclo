import { useId, type ReactNode, type SelectHTMLAttributes } from 'react';

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  error?: string;
  children: ReactNode;
}

/**
 * Select nativo padronizado com o visual do Input (Spec 190, FR-007).
 *
 * A associação implícita (envolver o controle no `<label>`) funciona no navegador, mas não
 * dá ao `label` um `htmlFor` verificável nem permite descrever o estado de erro. Agora o
 * rótulo é explícito e o estado inválido é exposto por `aria-invalid` + `aria-describedby`,
 * exatamente como no `Input`.
 */
export function Select({ label, error, className = '', children, id, ...props }: SelectProps) {
  const generatedId = useId();
  const fieldId = id ?? generatedId;
  const errorId = `${fieldId}-error`;

  return (
    <div className="block">
      {label && (
        <label htmlFor={fieldId} className="block text-xs font-medium text-gray-600 mb-1">
          {label}
        </label>
      )}
      <select
        {...props}
        id={fieldId}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        className={`rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${error ? 'border-red-500' : ''} ${className}`}
      >
        {children}
      </select>
      {error && (
        <p id={errorId} className="mt-1 text-sm text-red-600">
          {error}
        </p>
      )}
    </div>
  );
}
