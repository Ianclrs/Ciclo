import type { ReactNode } from 'react';
import { Button } from './Button';

interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  onConfirm?: () => void;
  confirmLabel?: string;
  confirmVariant?: 'primary' | 'danger';
  /** Bloqueia a confirmação enquanto o formulário do modal não estiver completo. */
  confirmDisabled?: boolean;
}

export function Modal({
  open,
  onClose,
  title,
  children,
  onConfirm,
  confirmLabel,
  confirmVariant = 'primary',
  confirmDisabled = false,
}: ModalProps) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-xl max-w-md w-full mx-4 p-6">
        <h3 className="text-lg font-semibold text-gray-900 mb-4">{title}</h3>
        <div className="text-sm text-gray-600 mb-6">{children}</div>
        <div className="flex justify-end gap-3">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          {onConfirm && (
            <Button variant={confirmVariant} onClick={onConfirm} disabled={confirmDisabled}>
              {confirmLabel || 'Confirmar'}
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}
