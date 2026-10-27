/**
 * Regra de senha espelhada do Identity (`Password1!` é o formato dos testes do backend).
 * Antes vivia só dentro de `RegisterPage`; o cadastro de responsável usa a mesma regra,
 * então ela passou a ter um dono único.
 */
export function passwordError(password: string): string | null {
  if (password.length < 8) return 'A senha deve ter no mínimo 8 caracteres.';
  if (!/[A-Z]/.test(password)) return 'A senha deve ter pelo menos uma letra maiúscula.';
  if (!/\d/.test(password)) return 'A senha deve ter pelo menos um número.';
  if (!/[^A-Za-z0-9]/.test(password)) return 'A senha deve ter pelo menos um símbolo (ex.: !@#$%).';
  return null;
}
