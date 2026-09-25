// Configuração central dos países suportados no cadastro de colégio.
// Mantenha em sincronia com a lista do backend (TenantService.SupportedCountries).

export interface DocumentFieldConfig {
  label: string;
  mask: string; // '0' = dígito, demais caracteres são literais
  placeholder: string;
  digits: number; // quantidade de dígitos esperados
}

export interface CountryConfig {
  code: string; // ISO 3166-1 alpha-2
  label: string;
  ddi: string;
  moeda: string; // código ISO 4217
  moedaSimbolo: string;
  documentoFiscal: DocumentFieldConfig;
  cep: DocumentFieldConfig;
  estadoLabel: string;
  telefonePlaceholder: string;
  formatoData: string;
  calendarioLabel: string;
}

export const COUNTRIES: Record<string, CountryConfig> = {
  BR: {
    code: 'BR',
    label: 'Brasil',
    ddi: '+55',
    moeda: 'BRL',
    moedaSimbolo: 'R$',
    documentoFiscal: { label: 'CNPJ', mask: '00.000.000/0000-00', placeholder: '00.000.000/0000-00', digits: 14 },
    cep: { label: 'CEP', mask: '00000-000', placeholder: '00000-000', digits: 8 },
    estadoLabel: 'UF',
    telefonePlaceholder: '(11) 91234-5678',
    formatoData: 'dd/mm/aaaa',
    calendarioLabel: 'Fev – Dez (hemisfério sul)',
  },
  US: {
    code: 'US',
    label: 'Estados Unidos',
    ddi: '+1',
    moeda: 'USD',
    moedaSimbolo: '$',
    documentoFiscal: { label: 'EIN (Tax ID)', mask: '00-0000000', placeholder: '00-0000000', digits: 9 },
    cep: { label: 'ZIP Code', mask: '00000', placeholder: '00000', digits: 5 },
    estadoLabel: 'Estado',
    telefonePlaceholder: '(555) 123-4567',
    formatoData: 'mm/dd/aaaa',
    calendarioLabel: 'Set – Jun (hemisfério norte)',
  },
  PT: {
    code: 'PT',
    label: 'Portugal',
    ddi: '+351',
    moeda: 'EUR',
    moedaSimbolo: '€',
    documentoFiscal: { label: 'NIPC/NIF', mask: '000 000 000', placeholder: '000 000 000', digits: 9 },
    cep: { label: 'Código Postal', mask: '0000-000', placeholder: '0000-000', digits: 7 },
    estadoLabel: 'Distrito',
    telefonePlaceholder: '912 345 678',
    formatoData: 'dd/mm/aaaa',
    calendarioLabel: 'Set – Jun (hemisfério norte)',
  },
};

export const COUNTRY_ORDER = ['BR', 'US', 'PT'] as const;

export function getCountry(code: string): CountryConfig {
  return COUNTRIES[code] ?? COUNTRIES.BR;
}

/** Aplica a máscara (insere separadores) mantendo apenas os dígitos digitados. */
export function applyMask(value: string, mask: string): string {
  const digits = value.replace(/\D/g, '');
  let result = '';
  let digitIndex = 0;
  for (const char of mask) {
    if (digitIndex >= digits.length) break;
    if (char === '0') {
      result += digits[digitIndex];
      digitIndex += 1;
    } else {
      result += char;
    }
  }
  return result;
}

/** Conta os dígitos de um valor (ignora máscara). */
export function digitCount(value: string): number {
  return value.replace(/\D/g, '').length;
}
