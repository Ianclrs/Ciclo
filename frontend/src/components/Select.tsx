import type { ReactNode, SelectHTMLAttributes } from 'react';

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  children: ReactNode;
}

/** Select nativo padronizado com o visual do Input, com rótulo associado implicitamente. */
export function Select({ label, className = '', children, ...props }: SelectProps) {
  return (
    <label className="block">
      {label && <span className="block text-xs font-medium text-gray-600 mb-1">{label}</span>}
      <select
        className={`rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${className}`}
        {...props}
      >
        {children}
      </select>
    </label>
  );
}
