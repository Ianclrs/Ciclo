// Bandeiras em SVG inline (sem dependências externas e sem emoji, que não
// renderiza de forma consistente no Windows).

interface CountryFlagProps {
  code: string;
  className?: string;
}

/** Pontos de uma estrela de 5 pontas centrada em (cx, cy) com raio externo r. */
function starPoints(cx: number, cy: number, r: number): string {
  const pts: string[] = [];
  for (let i = 0; i < 10; i++) {
    const radius = i % 2 === 0 ? r : r * 0.382;
    const angle = (Math.PI / 5) * i - Math.PI / 2;
    pts.push(
      `${(cx + radius * Math.cos(angle)).toFixed(2)},${(cy + radius * Math.sin(angle)).toFixed(2)}`,
    );
  }
  return pts.join(' ');
}

const US_STRIPE_H = 20 / 13;
const US_CANTON_H = (7 * 20) / 13;

export function CountryFlag({ code, className = 'w-6 h-4' }: CountryFlagProps) {
  const classes = `inline-block rounded-[2px] ring-1 ring-black/10 shadow-sm ${className}`;

  switch (code) {
    case 'BR':
      return (
        <svg viewBox="0 0 30 20" className={classes} role="img" aria-label="Brasil">
          <rect width="30" height="20" fill="#009c3b" />
          <polygon points="15,2 28,10 15,18 2,10" fill="#fedf00" />
          <circle cx="15" cy="10" r="4.5" fill="#002776" />
          <path d="M11 11.1c2-1.9 6-2 8-3.6" stroke="#fff" strokeWidth="1.15" fill="none" strokeLinecap="round" />
          <polygon points={starPoints(13.3, 8.9, 0.34)} fill="#fff" />
          <polygon points={starPoints(16.7, 8.9, 0.34)} fill="#fff" />
          <polygon points={starPoints(15, 7.6, 0.3)} fill="#fff" />
          <polygon points={starPoints(12.3, 11.7, 0.3)} fill="#fff" />
          <polygon points={starPoints(15.2, 12, 0.3)} fill="#fff" />
          <polygon points={starPoints(17.7, 11.7, 0.3)} fill="#fff" />
        </svg>
      );
    case 'US':
      return (
        <svg viewBox="0 0 30 20" className={classes} role="img" aria-label="Estados Unidos">
          <rect width="30" height="20" fill="#fff" />
          {[0, 1, 2, 3, 4, 5, 6].map((i) => (
            <rect key={i} y={i * 2 * US_STRIPE_H} width="30" height={US_STRIPE_H} fill="#b22234" />
          ))}
          <rect width="12" height={US_CANTON_H} fill="#3c3b6e" />
          {Array.from({ length: 9 }).map((_, row) =>
            Array.from({ length: row % 2 === 0 ? 6 : 5 }).map((_, col) => (
              <polygon
                key={`${row}-${col}`}
                points={starPoints((row % 2 === 0 ? 1.1 : 2.1) + col * 1.96, 0.55 + row * 1.22, 0.34)}
                fill="#fff"
              />
            )),
          )}
        </svg>
      );
    case 'PT':
      return (
        <svg viewBox="0 0 30 20" className={classes} role="img" aria-label="Portugal">
          <rect width="30" height="20" fill="#da291c" />
          <rect width="12" height="20" fill="#046a38" />
          <circle cx="12" cy="10" r="4.2" fill="#ffd700" />
          <ellipse cx="12" cy="10" rx="4.2" ry="1.75" fill="none" stroke="#dcb800" strokeWidth="0.35" />
          <ellipse cx="12" cy="10" rx="1.75" ry="4.2" fill="none" stroke="#dcb800" strokeWidth="0.35" />
          <path
            d="M10.1 8.3h3.8v2.7c0 1.15-0.9 2-1.9 2.4-1-0.4-1.9-1.25-1.9-2.4z"
            fill="#fff"
            stroke="#da291c"
            strokeWidth="0.5"
          />
          <circle cx="11.05" cy="9.9" r="0.3" fill="#003399" />
          <circle cx="12" cy="9.35" r="0.3" fill="#003399" />
          <circle cx="12.95" cy="9.9" r="0.3" fill="#003399" />
          <circle cx="11.5" cy="11" r="0.3" fill="#003399" />
          <circle cx="12.5" cy="11" r="0.3" fill="#003399" />
        </svg>
      );
    default:
      return null;
  }
}
