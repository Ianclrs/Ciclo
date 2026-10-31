import { Component, type ErrorInfo, type ReactNode } from 'react';
import { DEFAULT_LANGUAGE, readStoredLanguage } from '../i18n/locale';
import { t } from '../i18n/messages';
import { Button } from './Button';

/**
 * Fronteira global de erro (Spec 190, FR-006).
 *
 * Sem ela, uma exceção de renderização em qualquer tela desmontava a árvore inteira e deixava
 * a página em branco — sem mensagem e sem saída. Aqui o erro fica contido: a aplicação segue
 * montada e o usuário recebe um texto no seu idioma com a ação de recarregar.
 *
 * É uma classe de propósito: `getDerivedStateFromError` só existe em classe. E ela NÃO usa o
 * contexto de locale (AD-190-9): o próprio contexto pode ser o que lançou, então o idioma é
 * lido direto do storage, com o padrão como fallback.
 */
export interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
}

export interface ErrorBoundaryState {
  error: Error | null;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    try {
      // Este trecho não pode repetir o nome do método de desenho abaixo: a guarda de contrato
      // delimita o método por ele, e a asserção do try/catch seria truncada no meio.
      console.error('Erro não tratado na árvore de componentes', error, info.componentStack);
    } catch {
      // O relato nunca pode reentrar na fronteira: perder o log é melhor que perder a tela.
    }
  }

  render(): ReactNode {
    const { error } = this.state;

    if (error === null) {
      return this.props.children;
    }

    // `fallback` explícito do chamador substitui o padrão localizado.
    if (this.props.fallback !== undefined) {
      return this.props.fallback;
    }

    const language = readStoredLanguage() ?? DEFAULT_LANGUAGE;

    return (
      <section
        role="alert"
        className="mx-auto flex max-w-lg flex-col items-center gap-3 p-8 text-center"
      >
        <h1 className="text-xl font-semibold text-gray-900">{t('errorBoundary.title', language)}</h1>
        <p className="text-sm text-gray-700">{t('errorBoundary.body', language)}</p>
        <Button onClick={() => window.location.reload()}>{t('common.reload', language)}</Button>
      </section>
    );
  }
}
