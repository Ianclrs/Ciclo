import type { ReactNode } from 'react';

interface PageHeaderProps {
  title: string;
  subtitle?: string;
  /** Ações da página (botão novo, alternador de visão), alinhadas à direita. */
  actions?: ReactNode;
  /** Elemento antes do título — o avatar nas telas de detalhe. */
  leading?: ReactNode;
}

/**
 * Cabeçalho padrão das telas internas.
 *
 * Vive em um só lugar porque a hierarquia (título grande, subtítulo e ações) precisa ser
 * a mesma em todas as páginas: repetir o markup em cada tela foi o que produziu títulos de
 * tamanhos diferentes ao longo do produto.
 */
export function PageHeader({ title, subtitle, actions, leading }: PageHeaderProps) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="flex min-w-0 items-center gap-3">
        {leading}
        <div className="min-w-0">
          <h2 className="text-3xl font-bold tracking-tight text-gray-900">{title}</h2>
          {subtitle && <p className="mt-1 text-base text-gray-700">{subtitle}</p>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
