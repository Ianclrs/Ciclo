import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { buttonClass, type ButtonSize, type ButtonVariant } from './buttonStyles';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Nome acessível para botão só de ícone, sem texto visível. */
  ariaLabel?: string;
  children: ReactNode;
}

/**
 * Botão compartilhado (Spec 190, FR-007).
 *
 * `ariaLabel` existe para o botão só de ícone: sem nome acessível, leitor de tela anuncia
 * apenas "botão". O atributo é renderizado ANTES do espalhamento, então um `aria-label`
 * explícito do chamador continua vencendo e um botão com texto não muda.
 */
export function Button({
  variant = 'primary',
  size = 'md',
  className = '',
  ariaLabel,
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      aria-label={ariaLabel}
      className={buttonClass(variant, size, className)}
      {...props}
    >
      {children}
    </button>
  );
}
