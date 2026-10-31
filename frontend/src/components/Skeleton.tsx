import { Card } from './Card';

/**
 * Textura única de carregamento. Todo placeholder cinza da aplicação nasce daqui: a cor, o
 * pulso e o arredondamento ficam num lugar só, senão duas telas piscam em tons diferentes —
 * foi o que aconteceu quando `bg-gray-200`, `bg-gray-100` e `bg-stone-200` conviviam.
 *
 * O tamanho é responsabilidade de quem chama (é o que muda entre tabela, cartão e lista).
 */
export function Skeleton({ className = '' }: { className?: string }) {
  return <span aria-hidden="true" className={`skeleton block rounded bg-stone-200/80 ${className}`} />;
}

/**
 * Linhas no formato de lista/tabela. Larguras alternadas evitam a aparência de bloco
 * único e deixam claro que o conteúdo está a caminho.
 */
export function SkeletonRows({ rows = 4, className = 'h-4' }: { rows?: number; className?: string }) {
  return (
    <ul className="space-y-3" aria-hidden="true">
      {Array.from({ length: rows }, (_, index) => (
        <li key={index}>
          <Skeleton className={`${className} ${index % 2 === 0 ? 'w-full' : 'w-2/3'}`} />
        </li>
      ))}
    </ul>
  );
}

/**
 * Silhueta de uma seção ainda sem dados: link de volta, título, subtítulo e um cartão de
 * vidro com linhas — a mesma ordem que a página real usa.
 *
 * Existe porque o "Carregando..." de texto trocava de forma ao receber os dados: a página
 * entrava com uma linha de altura e crescia de uma vez, e esse pulo de layout é o que
 * aparece como piscada ao trocar de seção.
 */
export function PageSkeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div role="status" aria-live="polite">
      <span className="sr-only">Carregando…</span>
      <Skeleton className="h-4 w-24" />
      <Skeleton className="mt-4 h-7 w-56" />
      <Skeleton className="mt-2 h-4 w-40" />
      <div className="mt-6">
        <Card tone="glass">
          <SkeletonRows rows={rows} />
        </Card>
      </div>
    </div>
  );
}
