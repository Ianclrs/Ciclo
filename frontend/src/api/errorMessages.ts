import type { Language } from '../i18n/locale.ts';

/**
 * Dono único da tradução código-de-erro da API → mensagem ao usuário (Spec 190, FR-004).
 *
 * Antes este mapa era a constante `CODE_MESSAGES` dentro de `api/errors.ts`, só em português.
 * Ele foi movido para cá com as três traduções **adjacentes por código** (AD-190-4): a paridade
 * vira uma asserção estrutural única e uma tradução parcial não entra em silêncio.
 *
 * `pt` preserva exatamente o texto que a aplicação já mostrava.
 */
export const ERROR_MESSAGES: Record<string, Record<Language, string>> = {
  // Acesso e sessão
  not_linked_to_student: {
    pt: 'Você não está vinculado a este aluno.',
    en: 'You are not linked to this student.',
    es: 'No estás vinculado a este alumno.',
  },
  user_not_authenticated: {
    pt: 'Sua sessão expirou. Entre novamente.',
    en: 'Your session expired. Sign in again.',
    es: 'Tu sesión expiró. Inicia sesión de nuevo.',
  },
  unauthorized: {
    pt: 'Sua sessão expirou. Entre novamente.',
    en: 'Your session expired. Sign in again.',
    es: 'Tu sesión expiró. Inicia sesión de nuevo.',
  },
  forbidden: {
    pt: 'Você não tem permissão para esta ação.',
    en: 'You do not have permission for this action.',
    es: 'No tienes permiso para esta acción.',
  },
  invalid_token: {
    pt: 'Sua sessão expirou. Entre novamente.',
    en: 'Your session expired. Sign in again.',
    es: 'Tu sesión expiró. Inicia sesión de nuevo.',
  },
  invalid_credentials: {
    pt: 'E-mail ou senha incorretos.',
    en: 'Wrong e-mail or password.',
    es: 'Correo electrónico o contraseña incorrectos.',
  },
  account_locked_30min: {
    pt: 'Conta bloqueada temporariamente por tentativas de acesso. Tente em 30 minutos.',
    en: 'Account temporarily locked after repeated sign-in attempts. Try again in 30 minutes.',
    es: 'Cuenta bloqueada temporalmente por intentos de acceso. Inténtalo en 30 minutos.',
  },
  account_inactive: {
    pt: 'Esta conta está inativa. Procure a secretaria do colégio.',
    en: 'This account is inactive. Contact the school office.',
    es: 'Esta cuenta está inactiva. Contacta con la secretaría del colegio.',
  },
  same_password: {
    pt: 'A nova senha precisa ser diferente da atual.',
    en: 'The new password must differ from the current one.',
    es: 'La contraseña nueva debe ser diferente de la actual.',
  },
  tenant_not_resolved: {
    pt: 'Colégio não identificado. Entre novamente.',
    en: 'School not identified. Sign in again.',
    es: 'Colegio no identificado. Inicia sesión de nuevo.',
  },

  // Alunos e vínculos
  student_not_found: {
    pt: 'Aluno não encontrado.',
    en: 'Student not found.',
    es: 'Alumno no encontrado.',
  },
  student_inactive: {
    pt: 'Este aluno está inativo.',
    en: 'This student is inactive.',
    es: 'Este alumno está inactivo.',
  },
  student_transferred: {
    pt: 'Este aluno foi transferido e não pode ser matriculado.',
    en: 'This student has been transferred and cannot be enrolled.',
    es: 'Este alumno fue transferido y no puede matricularse.',
  },
  student_has_active_enrollment: {
    pt: 'O aluno já possui uma matrícula ativa.',
    en: 'The student already has an active enrollment.',
    es: 'El alumno ya tiene una matrícula activa.',
  },
  cpf_already_exists: {
    pt: 'Já existe um aluno com este CPF.',
    en: 'A student with this document already exists.',
    es: 'Ya existe un alumno con este documento.',
  },
  cpf_invalid: {
    pt: 'CPF inválido — confira os 11 dígitos.',
    en: 'Invalid document number — check the 11 digits.',
    es: 'Documento inválido — revisa los 11 dígitos.',
  },
  parent_not_found: {
    pt: 'Responsável não encontrado.',
    en: 'Guardian not found.',
    es: 'Responsable no encontrado.',
  },
  parent_already_linked: {
    pt: 'Este responsável já está vinculado ao aluno.',
    en: 'This guardian is already linked to the student.',
    es: 'Este responsable ya está vinculado al alumno.',
  },
  parent_belongs_to_different_tenant: {
    pt: 'Este responsável pertence a outro colégio.',
    en: 'This guardian belongs to another school.',
    es: 'Este responsable pertenece a otro colegio.',
  },
  user_is_not_parent: {
    pt: 'O usuário selecionado não é um responsável.',
    en: 'The selected user is not a guardian.',
    es: 'El usuario seleccionado no es un responsable.',
  },
  users_must_belong_to_same_tenant: {
    pt: 'Todos os usuários devem pertencer ao mesmo colégio.',
    en: 'All users must belong to the same school.',
    es: 'Todos los usuarios deben pertenecer al mismo colegio.',
  },
  link_not_found: {
    pt: 'Vínculo não encontrado.',
    en: 'Link not found.',
    es: 'Vínculo no encontrado.',
  },

  // Matrícula e período
  enrollment_not_found: {
    pt: 'Matrícula não encontrada.',
    en: 'Enrollment not found.',
    es: 'Matrícula no encontrada.',
  },
  enrollment_already_exists: {
    pt: 'Já existe uma matrícula deste aluno neste período.',
    en: 'This student already has an enrollment in this period.',
    es: 'Ya existe una matrícula de este alumno en este período.',
  },
  enrollment_period_closed: {
    pt: 'O período de matrícula está encerrado.',
    en: 'The enrollment period is closed.',
    es: 'El período de matrícula está cerrado.',
  },
  enrollment_period_out_of_window: {
    pt: 'Fora da janela de matrícula deste período.',
    en: 'Outside this period enrollment window.',
    es: 'Fuera de la ventana de matrícula de este período.',
  },
  cannot_cancel_approved_enrollment: {
    pt: 'Uma matrícula aprovada não pode ser cancelada.',
    en: 'An approved enrollment cannot be cancelled.',
    es: 'Una matrícula aprobada no puede cancelarse.',
  },
  pending_required_documents: {
    pt: 'Há documentos obrigatórios pendentes. Regularize antes de aprovar.',
    en: 'There are required documents pending. Resolve them before approving.',
    es: 'Hay documentos obligatorios pendientes. Regulariza antes de aprobar.',
  },
  period_not_found: {
    pt: 'Período de matrícula não encontrado.',
    en: 'Enrollment period not found.',
    es: 'Período de matrícula no encontrado.',
  },
  invalid_period_dates: {
    pt: 'As datas do período são inválidas.',
    en: 'The period dates are invalid.',
    es: 'Las fechas del período son inválidas.',
  },
  overlapping_periods: {
    pt: 'Já existe um período com estas datas.',
    en: 'A period with these dates already exists.',
    es: 'Ya existe un período con estas fechas.',
  },
  period_name_required: {
    pt: 'Informe o nome do período.',
    en: 'Provide the period name.',
    es: 'Indica el nombre del período.',
  },

  // Documentos
  document_not_found: {
    pt: 'Documento não encontrado.',
    en: 'Document not found.',
    es: 'Documento no encontrado.',
  },
  document_type_not_found: {
    pt: 'Tipo de documento não encontrado. Selecione um tipo válido.',
    en: 'Document type not found. Select a valid type.',
    es: 'Tipo de documento no encontrado. Selecciona un tipo válido.',
  },
  document_type_inactive: {
    pt: 'Este tipo de documento está desativado.',
    en: 'This document type is disabled.',
    es: 'Este tipo de documento está desactivado.',
  },
  document_file_not_found: {
    pt: 'O arquivo deste documento não está mais disponível.',
    en: 'The file for this document is no longer available.',
    es: 'El archivo de este documento ya no está disponible.',
  },
  invalid_extension: {
    pt: 'Formato de arquivo não permitido. Use PDF, JPG ou PNG.',
    en: 'File format not allowed. Use PDF, JPG or PNG.',
    es: 'Formato de archivo no permitido. Usa PDF, JPG o PNG.',
  },
  invalid_file_content: {
    pt: 'O conteúdo do arquivo não corresponde ao tipo informado.',
    en: 'The file content does not match the declared type.',
    es: 'El contenido del archivo no corresponde al tipo indicado.',
  },
  file_too_large: {
    pt: 'Arquivo muito grande (máximo 10MB).',
    en: 'File too large (10MB maximum).',
    es: 'Archivo demasiado grande (máximo 10 MB).',
  },
  days_out_of_range: {
    pt: 'O prazo deve estar entre 1 e 365 dias.',
    en: 'The deadline must be between 1 and 365 days.',
    es: 'El plazo debe estar entre 1 y 365 días.',
  },
  foto_invalid_format: {
    pt: 'Formato de foto inválido. Use JPG ou PNG.',
    en: 'Invalid photo format. Use JPG or PNG.',
    es: 'Formato de foto inválido. Usa JPG o PNG.',
  },
  foto_too_large: {
    pt: 'A foto enviada é muito grande.',
    en: 'The uploaded photo is too large.',
    es: 'La foto enviada es demasiado grande.',
  },

  // Notificações
  notification_not_found: {
    pt: 'Notificação não encontrada.',
    en: 'Notification not found.',
    es: 'Notificación no encontrada.',
  },
  user_ids_required: {
    pt: 'Selecione ao menos um destinatário.',
    en: 'Select at least one recipient.',
    es: 'Selecciona al menos un destinatario.',
  },
  titulo_required_max_200: {
    pt: 'Informe um título de até 200 caracteres.',
    en: 'Provide a title of up to 200 characters.',
    es: 'Indica un título de hasta 200 caracteres.',
  },
  mensagem_required_max_2000: {
    pt: 'Informe uma mensagem de até 2000 caracteres.',
    en: 'Provide a message of up to 2000 characters.',
    es: 'Indica un mensaje de hasta 2000 caracteres.',
  },
  motivo_required_10_to_500_chars: {
    pt: 'O motivo deve ter entre 10 e 500 caracteres.',
    en: 'The reason must be between 10 and 500 characters.',
    es: 'El motivo debe tener entre 10 y 500 caracteres.',
  },

  // Validações de cadastro
  nome_required: {
    pt: 'Informe o nome.',
    en: 'Provide the name.',
    es: 'Indica el nombre.',
  },
  nome_too_long: {
    pt: 'O nome é longo demais.',
    en: 'The name is too long.',
    es: 'El nombre es demasiado largo.',
  },
  turma_required: {
    pt: 'Informe a turma.',
    en: 'Provide the class.',
    es: 'Indica la clase.',
  },
  turma_too_long: {
    pt: 'A turma é longa demais.',
    en: 'The class name is too long.',
    es: 'El nombre de la clase es demasiado largo.',
  },
  ano_letivo_invalid: {
    pt: 'Ano letivo inválido.',
    en: 'Invalid school year.',
    es: 'Año lectivo inválido.',
  },
  ano_letivo_must_be_current_or_future: {
    pt: 'O ano letivo deve ser o atual ou um ano futuro.',
    en: 'The school year must be the current or a future year.',
    es: 'El año lectivo debe ser el actual o un año futuro.',
  },
  status_invalid: {
    pt: 'Status inválido — escolha um dos status disponíveis.',
    en: 'Invalid status — choose one of the available statuses.',
    es: 'Estado inválido — elige uno de los estados disponibles.',
  },
  data_nascimento_must_be_past: {
    pt: 'A data de nascimento deve estar no passado.',
    en: 'The birth date must be in the past.',
    es: 'La fecha de nacimiento debe estar en el pasado.',
  },
  data_nascimento_too_recent: {
    pt: 'A data de nascimento é recente demais para um aluno.',
    en: 'The birth date is too recent for a student.',
    es: 'La fecha de nacimiento es demasiado reciente para un alumno.',
  },
  data_nascimento_too_old: {
    pt: 'A data de nascimento é antiga demais.',
    en: 'The birth date is too old.',
    es: 'La fecha de nacimiento es demasiado antigua.',
  },
  school_name_required: {
    pt: 'Informe o nome do colégio.',
    en: 'Provide the school name.',
    es: 'Indica el nombre del colegio.',
  },
  type_name_required: {
    pt: 'Informe o nome do tipo de documento.',
    en: 'Provide the document type name.',
    es: 'Indica el nombre del tipo de documento.',
  },

  // Estrutura acadêmica e notas (Spec 150)
  academic_year_not_found: {
    pt: 'Ano letivo não encontrado.',
    en: 'School year not found.',
    es: 'Año lectivo no encontrado.',
  },
  academic_year_already_exists: {
    pt: 'Já existe um ano letivo com este ano.',
    en: 'A school year with this year already exists.',
    es: 'Ya existe un año lectivo con este año.',
  },
  academic_year_invalid: {
    pt: 'Ano letivo inválido.',
    en: 'Invalid school year.',
    es: 'Año lectivo inválido.',
  },
  academic_year_range_invalid: {
    pt: 'As datas do ano letivo são inválidas.',
    en: 'The school year dates are invalid.',
    es: 'Las fechas del año lectivo son inválidas.',
  },
  academic_year_in_use: {
    pt: 'Este ano letivo tem turmas e não pode ser excluído.',
    en: 'This school year has classes and cannot be deleted.',
    es: 'Este año lectivo tiene clases y no puede eliminarse.',
  },
  school_class_not_found: {
    pt: 'Turma não encontrada.',
    en: 'Class not found.',
    es: 'Clase no encontrada.',
  },
  class_name_already_exists: {
    pt: 'Já existe uma turma com este nome neste ano letivo.',
    en: 'A class with this name already exists in this school year.',
    es: 'Ya existe una clase con este nombre en este año lectivo.',
  },
  school_class_in_use: {
    pt: 'Esta turma tem alunos ou avaliações e não pode ser excluída.',
    en: 'This class has students or assessments and cannot be deleted.',
    es: 'Esta clase tiene alumnos o evaluaciones y no puede eliminarse.',
  },
  component_not_found: {
    pt: 'Componente curricular não encontrado.',
    en: 'Curriculum component not found.',
    es: 'Componente curricular no encontrado.',
  },
  component_code_already_exists: {
    pt: 'Já existe um componente com este código.',
    en: 'A component with this code already exists.',
    es: 'Ya existe un componente con este código.',
  },
  component_in_use: {
    pt: 'Este componente tem avaliações e não pode ser excluído.',
    en: 'This component has assessments and cannot be deleted.',
    es: 'Este componente tiene evaluaciones y no puede eliminarse.',
  },
  assessment_title_already_exists: {
    pt: 'Já existe uma avaliação com este título nesta turma e componente.',
    en: 'An assessment with this title already exists in this class and component.',
    es: 'Ya existe una evaluación con este título en esta clase y componente.',
  },
  assessment_weight_invalid: {
    pt: 'O peso da avaliação deve ser maior que zero.',
    en: 'The assessment weight must be greater than zero.',
    es: 'El peso de la evaluación debe ser mayor que cero.',
  },
  assessment_has_grades: {
    pt: 'Esta avaliação já tem notas lançadas e não pode ser excluída.',
    en: 'This assessment already has grades and cannot be deleted.',
    es: 'Esta evaluación ya tiene notas registradas y no puede eliminarse.',
  },
  student_not_in_class: {
    pt: 'Este aluno não pertence à turma desta avaliação.',
    en: 'This student does not belong to this assessment class.',
    es: 'Este alumno no pertenece a la clase de esta evaluación.',
  },
  grade_out_of_range: {
    pt: 'A nota deve estar entre 0 e 10.',
    en: 'The grade must be between 0 and 10.',
    es: 'La nota debe estar entre 0 y 10.',
  },
};
