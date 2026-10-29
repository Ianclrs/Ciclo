/**
 * Contas da demonstração.
 *
 * Módulo mínimo, sem dependências: é consumido tanto pela semente quanto pela tela de login.
 * Fica separado para a tela de login não arrastar o roteador e os dados da demo para o bundle
 * de um build que fala com a API real.
 */
export const DEMO_ACCOUNTS = {
  admin: { email: 'diretor@demo.ciclo', label: 'Diretor Demo (Admin)' },
  parent: { email: 'responsavel@demo.ciclo', label: 'Marta Almeida (Responsável)' },
} as const;
