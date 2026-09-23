const SIZES = {
  sm: 'h-8 w-8 text-xs',
  md: 'h-10 w-10 text-sm',
  lg: 'h-16 w-16 text-lg',
};

/** Iniciais do nome (primeira e última palavra), para quando não há foto. */
function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

interface StudentAvatarProps {
  name: string;
  photo?: string | null;
  size?: 'sm' | 'md' | 'lg';
}

/** Foto do aluno em círculo; sem foto, mostra as iniciais do nome. */
export function StudentAvatar({ name, photo, size = 'md' }: StudentAvatarProps) {
  const dimension = `${SIZES[size]} rounded-full shrink-0`;

  if (photo) {
    // alt vazio: o nome do aluno é sempre exibido ao lado, então a imagem é decorativa.
    return <img src={photo} alt="" className={`${dimension} object-cover bg-gray-100`} />;
  }

  // inline-flex (e não flex): mantém o avatar no fluxo inline para que o text-center
  // da célula centralize as DUAS formas (foto e iniciais) na mesma posição.
  return (
    <span
      aria-hidden="true"
      className={`${dimension} inline-flex items-center justify-center bg-gray-200 font-semibold text-gray-600`}
    >
      {initials(name)}
    </span>
  );
}
