import { useEffect, useId, useRef, type ReactNode } from 'react';
import { Button } from './Button';
import { FOCUSABLE_SELECTOR, nextFocusIndex } from '../utils/focusTrap';
import { useLocale } from '../i18n/LocaleContext';

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

/**
 * Modal compartilhado com semântica de diálogo e navegação por teclado (Spec 190, FR-007).
 *
 * Antes era só um `div` sobreposto: leitor de tela não o reconhecia como diálogo, `Tab` saía
 * do modal para a página atrás, `Escape` não fechava e o foco não voltava ao botão de origem.
 * Agora o modal declara `role="dialog"`, recebe o foco na abertura, cicla `Tab`/`Shift+Tab`
 * pelos próprios controles e devolve o foco ao fechar.
 */
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
  const { t } = useLocale();
  const dialogRef = useRef<HTMLDivElement>(null);
  const previousFocusRef = useRef<Element | null>(null);
  const titleId = useId();

  useEffect(() => {
    if (!open) {
      return;
    }

    const dialog = dialogRef.current;
    const focusableElements = () =>
      Array.from(dialog?.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR) ?? []);

    // Guarda quem tinha o foco para devolvê-lo no fechamento.
    previousFocusRef.current = document.activeElement;

    // Primeiro focável dentro do diálogo; sem nenhum, o próprio diálogo (que tem tabIndex -1).
    (focusableElements()[0] ?? dialog)?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
        return;
      }

      if (event.key !== 'Tab') {
        return;
      }

      const elements = focusableElements();
      if (elements.length === 0) {
        return;
      }

      const currentIndex = elements.indexOf(document.activeElement as HTMLElement);
      const nextIndex = nextFocusIndex(currentIndex, elements.length, event.shiftKey);

      event.preventDefault();
      elements[nextIndex]?.focus();
    };

    dialog?.addEventListener('keydown', handleKeyDown);

    return () => {
      dialog?.removeEventListener('keydown', handleKeyDown);

      const previous = previousFocusRef.current;
      if (previous instanceof HTMLElement && previous.isConnected) {
        previous.focus();
      } else {
        // O elemento de origem saiu do documento (lista recarregada, por exemplo).
        document.body.focus();
      }
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className="relative bg-white rounded-xl shadow-xl max-w-md w-full mx-4 p-6"
      >
        <h3 id={titleId} className="text-lg font-semibold text-gray-900 mb-4">{title}</h3>
        <div className="text-sm text-gray-600 mb-6">{children}</div>
        <div className="flex justify-end gap-3">
          <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          {onConfirm && (
            <Button variant={confirmVariant} onClick={onConfirm} disabled={confirmDisabled}>
              {confirmLabel || t('common.confirm')}
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}
