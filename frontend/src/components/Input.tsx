import { useId, type InputHTMLAttributes, type Ref } from 'react';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  /**
   * Ref do input, como no elemento nativo — o React 19 já entrega `ref` como prop.
   * Necessária para campos cujo valor é lido fora do React, como o `<input type="file">`.
   */
  ref?: Ref<HTMLInputElement>;
}

/**
 * Input compartilhado (Spec 190, FR-007).
 *
 * O rótulo era texto solto ao lado do campo: leitor de tela não associava um ao outro e o
 * erro não era anunciado. Agora o `id` é único (do chamador ou gerado), o `<label>` aponta
 * para ele com `htmlFor`, e o estado inválido é exposto por `aria-invalid` +
 * `aria-describedby` apontando para o parágrafo do erro.
 */
export function Input({ label, error, className = '', id, ref, ...props }: InputProps) {
  const generatedId = useId();
  const fieldId = id ?? generatedId;
  const errorId = `${fieldId}-error`;

  return (
    <div className="w-full">
      {label && (
        <label htmlFor={fieldId} className="block text-sm font-medium text-gray-700 mb-1">
          {label}
        </label>
      )}
      <input
        {...props}
        ref={ref}
        id={fieldId}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        className={`w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${error ? 'border-red-500' : ''} ${className}`}
      />
      {error && (
        <p id={errorId} className="mt-1 text-sm text-red-600">
          {error}
        </p>
      )}
    </div>
  );
}
