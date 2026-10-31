import axios from 'axios';

/**
 * Dono único da tradução código-de-erro da API → mensagem ao usuário.
 *
 * O backend devolve o código em `title` (ProblemDetails) ou em `error`
 * (contratos mais antigos). Antes desta função cada tela repetia o próprio
 * `switch` sobre `title` e caía num texto genérico ("Erro.") quando o código
 * não estava mapeado — o usuário não sabia o que corrigir (A8).
 */
const CODE_MESSAGES: Record<string, string> = {
  // Acesso e sessão
  not_linked_to_student: 'Você não está vinculado a este aluno.',
  user_not_authenticated: 'Sua sessão expirou. Entre novamente.',
  // M3: o 401/403 do framework passou a chegar com código (antes vinha sem corpo).
  unauthorized: 'Sua sessão expirou. Entre novamente.',
  forbidden: 'Você não tem permissão para esta ação.',
  invalid_token: 'Sua sessão expirou. Entre novamente.',
  invalid_credentials: 'E-mail ou senha incorretos.',
  account_locked_30min: 'Conta bloqueada temporariamente por tentativas de acesso. Tente em 30 minutos.',
  account_inactive: 'Esta conta está inativa. Procure a secretaria do colégio.',
  same_password: 'A nova senha precisa ser diferente da atual.',
  tenant_not_resolved: 'Colégio não identificado. Entre novamente.',

  // Alunos e vínculos
  student_not_found: 'Aluno não encontrado.',
  student_inactive: 'Este aluno está inativo.',
  student_transferred: 'Este aluno foi transferido e não pode ser matriculado.',
  student_has_active_enrollment: 'O aluno já possui uma matrícula ativa.',
  cpf_already_exists: 'Já existe um aluno com este CPF.',
  cpf_invalid: 'CPF inválido — confira os 11 dígitos.',
  parent_not_found: 'Responsável não encontrado.',
  parent_already_linked: 'Este responsável já está vinculado ao aluno.',
  parent_belongs_to_different_tenant: 'Este responsável pertence a outro colégio.',
  user_is_not_parent: 'O usuário selecionado não é um responsável.',
  users_must_belong_to_same_tenant: 'Todos os usuários devem pertencer ao mesmo colégio.',
  link_not_found: 'Vínculo não encontrado.',

  // Matrícula e período
  enrollment_not_found: 'Matrícula não encontrada.',
  enrollment_already_exists: 'Já existe uma matrícula deste aluno neste período.',
  enrollment_period_closed: 'O período de matrícula está encerrado.',
  enrollment_period_out_of_window: 'Fora da janela de matrícula deste período.',
  cannot_cancel_approved_enrollment: 'Uma matrícula aprovada não pode ser cancelada.',
  pending_required_documents: 'Há documentos obrigatórios pendentes. Regularize antes de aprovar.',
  period_not_found: 'Período de matrícula não encontrado.',
  invalid_period_dates: 'As datas do período são inválidas.',
  overlapping_periods: 'Já existe um período com estas datas.',
  period_name_required: 'Informe o nome do período.',

  // Documentos
  document_not_found: 'Documento não encontrado.',
  document_type_not_found: 'Tipo de documento não encontrado. Selecione um tipo válido.',
  document_type_inactive: 'Este tipo de documento está desativado.',
  document_file_not_found: 'O arquivo deste documento não está mais disponível.',
  invalid_extension: 'Formato de arquivo não permitido. Use PDF, JPG ou PNG.',
  invalid_file_content: 'O conteúdo do arquivo não corresponde ao tipo informado.',
  file_too_large: 'Arquivo muito grande (máximo 10MB).',
  days_out_of_range: 'O prazo deve estar entre 1 e 365 dias.',
  foto_invalid_format: 'Formato de foto inválido. Use JPG ou PNG.',
  foto_too_large: 'A foto enviada é muito grande.',

  // Notificações
  notification_not_found: 'Notificação não encontrada.',
  user_ids_required: 'Selecione ao menos um destinatário.',
  titulo_required_max_200: 'Informe um título de até 200 caracteres.',
  mensagem_required_max_2000: 'Informe uma mensagem de até 2000 caracteres.',
  motivo_required_10_to_500_chars: 'O motivo deve ter entre 10 e 500 caracteres.',

  // Validações de cadastro
  nome_required: 'Informe o nome.',
  nome_too_long: 'O nome é longo demais.',
  turma_required: 'Informe a turma.',
  turma_too_long: 'A turma é longa demais.',
  ano_letivo_invalid: 'Ano letivo inválido.',
  ano_letivo_must_be_current_or_future: 'O ano letivo deve ser o atual ou um ano futuro.',
  status_invalid: 'Status inválido — escolha um dos status disponíveis.',
  data_nascimento_must_be_past: 'A data de nascimento deve estar no passado.',
  data_nascimento_too_recent: 'A data de nascimento é recente demais para um aluno.',
  data_nascimento_too_old: 'A data de nascimento é antiga demais.',
  school_name_required: 'Informe o nome do colégio.',
  type_name_required: 'Informe o nome do tipo de documento.',

  // Estrutura acadêmica e notas (Spec 150). Um texto por código que a tela pode encontrar;
  // sem isto o usuário via o texto genérico da página e não sabia o que corrigir.
  academic_year_not_found: 'Ano letivo não encontrado.',
  academic_year_already_exists: 'Já existe um ano letivo com este ano.',
  academic_year_invalid: 'Ano letivo inválido.',
  academic_year_range_invalid: 'As datas do ano letivo são inválidas.',
  academic_year_in_use: 'Este ano letivo tem turmas e não pode ser excluído.',
  school_class_not_found: 'Turma não encontrada.',
  class_name_already_exists: 'Já existe uma turma com este nome neste ano letivo.',
  school_class_in_use: 'Esta turma tem alunos ou avaliações e não pode ser excluída.',
  component_not_found: 'Componente curricular não encontrado.',
  component_code_already_exists: 'Já existe um componente com este código.',
  component_in_use: 'Este componente tem avaliações e não pode ser excluído.',
  assessment_title_already_exists: 'Já existe uma avaliação com este título nesta turma e componente.',
  assessment_weight_invalid: 'O peso da avaliação deve ser maior que zero.',
  assessment_has_grades: 'Esta avaliação já tem notas lançadas e não pode ser excluída.',
  student_not_in_class: 'Este aluno não pertence à turma desta avaliação.',
  grade_out_of_range: 'A nota deve estar entre 0 e 10.',
};

/** Código de erro devolvido pela API, se houver. */
export function apiErrorCode(error: unknown): string | null {
  if (!axios.isAxiosError(error)) {
    return null;
  }
  // `code` é o contrato (extensão do ProblemDetails, M3). `title` e `error` continuam aceitos
  // porque respostas antigas e o Identity ainda usam esses campos — a ordem é do mais novo ao
  // mais legado, para que a transição não quebre nenhuma tela.
  const data = error.response?.data as { code?: unknown; title?: unknown; error?: unknown } | undefined;
  if (typeof data?.code === 'string' && data.code.trim() !== '') {
    return data.code;
  }
  if (typeof data?.title === 'string' && data.title.trim() !== '') {
    return data.title;
  }
  if (typeof data?.error === 'string' && data.error.trim() !== '') {
    return data.error;
  }
  return null;
}

/**
 * Mensagem para o usuário. Código conhecido → texto específico; título já
 * legível da API (ex.: falha de validação) → ele mesmo; caso contrário, o
 * texto de apoio da tela. Nunca devolve o código cru ao usuário.
 */
/**
 * Texto para um código de erro da API. Dono único da tradução código → mensagem: a tela que
 * recebe um código fora de um `Error` de requisição (o `blockReason` da prévia de rematrícula,
 * por exemplo) usa esta função em vez de repetir um mapa local.
 */
export function codeMessage(code: string | null, fallback: string): string {
  if (code === null) {
    return fallback;
  }
  const known = CODE_MESSAGES[code];
  if (known !== undefined) {
    return known;
  }
  // ProblemDetails com frase legível já é apresentável; identificador não é.
  return code.includes(' ') ? code : fallback;
}

export function apiErrorMessage(error: unknown, fallback: string): string {
  return codeMessage(apiErrorCode(error), fallback);
}
