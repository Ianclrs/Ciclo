/**
 * Catálogo de texto do produto (Spec 190, FR-002; ampliado para o shell e a tela de
 * configurações).
 *
 * Dono único do texto visível em PT, EN e ES. É uma constante do projeto, não uma biblioteca
 * (AD-190-7): o teste de paridade recusa chave ausente em qualquer idioma, valor vazio e
 * placeholder divergente entre traduções.
 *
 * `t` recebe o idioma explicitamente (AD-190-1): nenhuma leitura de storage, de rede ou de
 * variável mutável — a mesma chamada produz o mesmo texto em qualquer execução.
 */

import { DEFAULT_LANGUAGE, type Language } from './locale.ts';

export type MessageKey =
  // Estado de tela (as onze chaves originais da Spec 190)
  | 'common.loading'
  | 'common.error'
  | 'common.empty'
  | 'common.retry'
  | 'common.reload'
  | 'common.close'
  | 'errorBoundary.title'
  | 'errorBoundary.body'
  | 'field.required'
  | 'list.emptyTitle'
  | 'language.label'
  // Shell: menu lateral e topbar
  | 'nav.dashboard'
  | 'nav.login'
  | 'nav.students'
  | 'nav.enrollments'
  | 'nav.documents'
  | 'nav.academic'
  | 'nav.notifications'
  | 'nav.users'
  | 'nav.settings'
  | 'nav.privacy'
  | 'nav.logout'
  | 'nav.account'
  | 'profile.changePhoto'
  | 'profile.removePhoto'
  | 'profile.photoSaved'
  | 'profile.photoError'
  | 'nav.openMenu'
  | 'nav.closeMenu'
  | 'nav.notificationsUnread'
  | 'nav.demo'
  | 'nav.demoTitle'
  // Seletor de idioma
  | 'language.switchAria'
  // Tela de configurações da escola
  | 'settings.title'
  | 'settings.subtitle'
  | 'settings.section.identity'
  | 'settings.section.regional'
  | 'settings.section.limits'
  | 'settings.section.plan'
  | 'settings.features.legend'
  | 'settings.field.displayName'
  | 'settings.field.timeZone'
  | 'settings.field.schoolYear'
  | 'settings.field.locale'
  | 'settings.field.storage'
  | 'settings.field.userQuota'
  | 'settings.field.plan'
  | 'settings.flag.documents'
  | 'settings.flag.notifications'
  | 'settings.flag.parentPortal'
  | 'settings.flag.academic'
  | 'settings.flag.reenrollment'
  | 'settings.flag.audit'
  | 'settings.action.save'
  | 'settings.action.saving'
  | 'settings.action.discard'
  | 'settings.state.unsaved'
  | 'settings.state.noChanges'
  | 'settings.state.saved'
  | 'settings.error.load'
  | 'settings.error.save'
  | 'settings.error.numbers'
  | 'settings.error.featuresRequired'
  | 'settings.hint.locale'
  | 'common.search'
  | 'common.clear'
  | 'users.col.user'
  | 'users.col.actions'
  | 'users.searchPlaceholder'
  | 'users.allRoles'
  | 'users.allStatuses'
  | 'users.stats.total'
  | 'users.stats.admins'
  | 'users.stats.unconfirmed'
  | 'users.stats.inactive'
  | 'users.emptyFiltered'
  | 'users.changeRoleTitle'
  | 'users.changeRoleHint'
  | 'users.deactivateTitle'
  | 'users.deactivateBody'
  | 'users.deactivateConfirm'
  | 'enrollments.column.student'
  | 'enrollments.column.status'
  | 'enrollments.batchRenewal'
  | 'enrollments.clear'
  | 'enrollments.reopenMessage'
  | 'priv.type.export'
  | 'priv.type.anonymization'
  | 'priv.type.revocation'
  | 'priv.status.submitted'
  | 'priv.status.approved'
  | 'priv.status.rejected'
  | 'priv.status.completed'
  | 'priv.loadError'
  | 'settings.hint.plan'
  // Paginação
  | 'pagination.showing'
  | 'pagination.of'
  | 'pagination.previous'
  | 'pagination.next'
  // Seletor de destinatários
  | 'pick.noUsers'
  | 'pick.noStudents'
  | 'pick.change'
  | 'pick.minChars'
  | 'pick.searching'
  | 'pick.selectedOne'
  | 'pick.selectedMany'
  // Cadastro
  | 'field.emailExample'
  | 'reg.schoolCodePlaceholder'
  | 'reg.schoolCodeHint'
  | 'reg.parentLinkHint'
  | 'reg.currencyLabel'
  | 'reg.dateLabel'
  | 'reg.schoolYearLabel'
  // Estrutura acadêmica
  | 'struct.actions.gradeEntry'
  | 'struct.actions.newYear'
  | 'struct.active'
  | 'struct.inactive'
  | 'struct.yearLabel'
  | 'struct.assessmentWeight'
  | 'struct.recoveryTag'
  | 'struct.kpi.activeOf'
  | 'struct.kpi.noActiveYear'
  | 'struct.kpi.classesInActiveYears'
  | 'struct.kpi.recovery'
  | 'struct.form.yearActive'
  | 'struct.form.componentActive'
  | 'struct.form.isRecovery'
  | 'struct.dateRange'
  | 'struct.deleteEntity.year'
  | 'struct.deleteEntity.class'
  | 'struct.deleteEntity.component'
  | 'struct.deleteEntity.assessment'
  | 'struct.deletePrompt'
  // Painel administrativo
  | 'dash.greeting'
  | 'dash.subtitle'
  | 'dash.updatedAt'
  | 'dash.refresh'
  | 'dash.partialWarning'
  | 'dash.activeOfTotal'
  | 'dash.awaitingReview'
  | 'dash.expiringIn30'
  | 'dash.ofTotal'
  | 'dash.progressPercent'
  | 'dash.lastDay'
  | 'dash.daysLeftOne'
  | 'dash.daysLeftMany'
  | 'dash.viewEnrollments'
  | 'dash.closed'
  | 'dash.closesToday'
  // Privacidade
  | 'priv.column.requester'
  | 'priv.column.type'
  | 'priv.column.status'
  | 'priv.column.student'
  | 'priv.column.legalHold'
  | 'priv.column.retainUntil'
  | 'priv.column.submittedAt'
  | 'priv.column.actions'
  | 'priv.yes'
  | 'priv.no'
  | 'priv.approve'
  | 'priv.reject'
  | 'priv.completing'
  | 'priv.complete'
  | 'priv.approveTitle'
  | 'priv.rejectTitle'
  | 'priv.resolutionOptional'
  | 'priv.reasonOptional'
  | 'priv.legalHoldHint'
  | 'priv.requestExport'
  | 'priv.requestAnonymization'
  | 'priv.granted'
  | 'priv.revoked'
  | 'priv.sending'
  | 'priv.downloading'
  | 'priv.downloadExport'
  | 'priv.legalBasis'
  | 'priv.grantedAt'
  | 'priv.revokedAt'
  | 'priv.openRequestHint'
  | 'priv.consentsTitle'
  | 'priv.openRequests'
  // Rótulos de domínio compartilhados entre telas
  | 'common.cancel'
  | 'common.confirm'
  | 'splash.subtitle'
  | 'splash.continue'
  | 'splash.prompt'
  | 'enrollment.status.draft'
  | 'enrollment.status.pending'
  | 'enrollment.status.documentsRequired'
  | 'enrollment.status.approved'
  | 'enrollment.status.rejected'
  | 'enrollment.status.cancelled'
  | 'enrollment.action.submit'
  | 'enrollment.action.approve'
  | 'enrollment.action.reject'
  | 'enrollment.action.cancel'
  | 'enrollment.action.reopen'
  | 'role.admin'
  | 'role.staff'
  | 'role.parent'
  | 'user.activate'
  | 'user.deactivate'
  | 'user.roleChanged'
  | 'field.name'
  | 'field.email'
  | 'field.role'
  | 'users.subtitle'
  | 'users.invite'
  | 'users.restricted'
  | 'users.loadError'
  | 'users.inviteError'
  | 'users.roleError'
  | 'users.activeError'
  | 'users.empty'
  | 'users.col.status'
  | 'users.you'
  | 'users.active'
  | 'users.inactive'
  | 'users.emailNotConfirmed'
  | 'users.activated'
  | 'users.deactivated'
  | 'users.inviteTitle'
  | 'users.inviteConfirm'
  | 'users.inviteHint'
  | 'users.inviteCreated'
  | 'users.inviteCreatedBody'
  | 'users.tokenCopied'
  | 'users.copyError'
  | 'users.copyToken'
  | 'users.tokenHidden'
  | 'users.validate.name'
  | 'users.validate.email'
  | 'users.validate.role'
  | 'common.back'
  | 'common.status'
  | 'common.remove'
  | 'students.loadError'
  | 'students.parentLinked'
  | 'students.parentLinkError'
  | 'students.parentUnlinked'
  | 'students.parentUnlinkError'
  | 'students.dataTitle'
  | 'students.field.class'
  | 'students.field.schoolYear'
  | 'students.field.notes'
  | 'students.parents'
  | 'students.noParents'
  | 'students.parentEmailPlaceholder'
  | 'students.linkParent'
  | 'students.detail.subtitle'
  | 'notifications.subtitle'
  | 'notifications.new'
  | 'notifications.history'
  | 'notifications.countOne'
  | 'notifications.countMany'
  | 'notifications.empty'
  | 'notifications.loadError'
  | 'notifications.recent'
  | 'notifications.unreadOne'
  | 'notifications.unreadMany'
  | 'notifications.viewAll'
  | 'notifications.recentEmpty'
  | 'auth.brand.login'
  | 'auth.brand.register'
  | 'auth.brand.parent'
  | 'auth.chip.institutional'
  | 'auth.chip.access'
  | 'auth.title.login'
  | 'auth.title.register'
  | 'auth.title.parent'
  | 'auth.subtitle.login'
  | 'auth.subtitle.register'
  | 'auth.subtitle.parent'
  | 'auth.capability.enrollments.title'
  | 'auth.capability.enrollments.text'
  | 'auth.capability.documents.title'
  | 'auth.capability.documents.text'
  | 'auth.capability.roles.title'
  | 'auth.capability.roles.text'
  | 'auth.field.password'
  | 'auth.forgot'
  | 'auth.showPassword'
  | 'auth.hidePassword'
  | 'auth.remember'
  | 'auth.submit'
  | 'auth.submitting'
  | 'auth.or'
  | 'auth.google'
  | 'auth.demoNotice'
  | 'auth.demoAdmin'
  | 'auth.demoParent'
  | 'auth.googleFailed'
  | 'auth.demoFailed'
  | 'auth.loginSuccess'
  | 'auth.offline'
  | 'auth.badCredentials'
  | 'auth.registerPrompt'
  | 'auth.registerAction'
  | 'auth.parentPrompt'
  | 'auth.parentAction'
  | 'auth.backToLogin'
  | 'auth.pedagogical.title'
  | 'auth.pedagogical.text'
  | 'auth.copyright'
  | 'auth.rights'
  | 'parent.children'
  | 'forgot.title.email'
  | 'forgot.title.reset'
  | 'forgot.subtitle.email'
  | 'forgot.subtitle.reset'
  | 'forgot.emailLabel'
  | 'forgot.continue'
  | 'forgot.verifying'
  | 'forgot.newPassword'
  | 'forgot.confirmPassword'
  | 'forgot.requirements'
  | 'forgot.req.length'
  | 'forgot.req.upper'
  | 'forgot.req.digit'
  | 'forgot.req.symbol'
  | 'forgot.change'
  | 'forgot.changing'
  | 'forgot.sent'
  | 'forgot.verified'
  | 'forgot.requestFailed'
  | 'forgot.changed'
  | 'forgot.resetFailed'
  | 'forgot.mismatch'
  | 'password.min'
  | 'password.upper'
  | 'password.digit'
  | 'password.symbol'
  | 'parent.title'
  | 'parent.greeting'
  | 'parent.loadFailed'
  | 'parent.childrenLinked'
  | 'parent.activeEnrollments'
  | 'parent.pendingDocs'
  | 'parent.unread'
  | 'parent.myChildren'
  | 'parent.noChildren'
  | 'parent.pendingDocsBadge'
  | 'parent.performanceUnavailable'
  | 'parent.noGrades'
  | 'parent.average'
  | 'parent.subjectOne'
  | 'parent.subjectMany'
  | 'students.subtitle'
  | 'students.new'
  | 'students.search'
  | 'students.searchPlaceholder'
  | 'students.filterClass'
  | 'students.classPlaceholder'
  | 'students.filterStatus'
  | 'students.allStatus'
  | 'students.clear'
  | 'students.loadListError'
  | 'students.removed'
  | 'students.removeError'
  | 'students.col.student'
  | 'students.edit'
  | 'students.removeTitle'
  | 'students.removeBody'
  | 'students.empty'
  | 'student.status.active'
  | 'student.status.inactive'
  | 'student.status.transferred'
  | 'students.form.editTitle'
  | 'students.form.editSubtitle'
  | 'students.form.newSubtitle'
  | 'students.field.photo'
  | 'students.field.choosePhoto'
  | 'students.field.removePhoto'
  | 'students.field.photoHint'
  | 'students.field.birthDate'
  | 'students.field.cpf'
  | 'students.photoTypeError'
  | 'students.photoProcessError'
  | 'students.updated'
  | 'students.created'
  | 'students.saveError'
  | 'common.save'
  | 'common.saving'
  | 'enrollments.loadError'
  | 'enrollments.approved'
  | 'enrollments.approveError'
  | 'enrollments.rejected'
  | 'enrollments.rejectError'
  | 'enrollments.detailTitle'
  | 'enrollments.info'
  | 'enrollments.field.period'
  | 'enrollments.field.reason'
  | 'enrollments.field.date'
  | 'enrollments.field.approvedAt'
  | 'enrollments.documents'
  | 'enrollments.missingDocs'
  | 'enrollments.docsOk'
  | 'enrollments.studentDocs'
  | 'enrollments.expired'
  | 'enrollments.validUntil'
  | 'enrollments.noExpiry'
  | 'enrollments.noDocs'
  | 'enrollments.actions'
  | 'enrollments.rejectTitle'
  | 'enrollments.rejectReason'
  | 'docs.uploadError'
  | 'docs.loadError'
  | 'docs.loadTypesError'
  | 'docs.selectType'
  | 'docs.tooLarge'
  | 'docs.uploaded'
  | 'docs.subtitle'
  | 'docs.field.type'
  | 'docs.field.file'
  | 'docs.selectPlaceholder'
  | 'docs.requiredSuffix'
  | 'docs.sending'
  | 'docs.upload'
  | 'docs.empty'
  | 'docs.rejectReason'
  | 'inbox.subtitle'
  | 'inbox.markAll'
  | 'inbox.title'
  | 'inbox.countOne'
  | 'inbox.countMany'
  | 'inbox.emptyUnread'
  | 'inbox.markRead'
  | 'inbox.markReadError'
  | 'inbox.markAllError'
  | 'inbox.markedAll'
  | 'academic.status.approved'
  | 'academic.status.recovery'
  | 'academic.status.failed'
  | 'academic.status.noGrade'
  | 'academic.status.none'
  | 'child.reportError'
  | 'child.tab.grades'
  | 'child.data'
  | 'child.field.year'
  | 'child.enrollment'
  | 'child.noEnrollment'
  | 'child.docsTitle'
  | 'child.uploadDoc'
  | 'child.report'
  | 'child.noGrades'
  | 'child.col.component'
  | 'child.col.regular'
  | 'child.col.recovery'
  | 'child.col.final'
  | 'child.kpi.components'
  | 'child.kpi.ofTotal'
  | 'notify.createTitle'
  | 'notify.createSubtitle'
  | 'notify.compose'
  | 'notify.field.title'
  | 'notify.field.message'
  | 'notify.field.type'
  | 'notify.field.target'
  | 'notify.mode.broadcast'
  | 'notify.mode.student'
  | 'notify.broadcastNotice'
  | 'notify.sendAll'
  | 'notify.send'
  | 'notify.missingRecipient'
  | 'notify.sentBroadcast'
  | 'notify.sentStudent'
  | 'notify.created'
  | 'notify.createError'
  | 'notify.kind.general'
  | 'notify.kind.docPending'
  | 'notify.kind.meeting'
  | 'notify.kind.other'
  | 'renewal.title'
  | 'renewal.subtitle'
  | 'renewal.periodsError'
  | 'renewal.previewError'
  | 'renewal.noPeriods'
  | 'renewal.periodLabel'
  | 'renewal.selectPeriod'
  | 'renewal.processing'
  | 'renewal.renewCount'
  | 'renewal.choosePeriod'
  | 'renewal.noCandidates'
  | 'renewal.summary'
  | 'renewal.col.carried'
  | 'renewal.col.pending'
  | 'renewal.selectStudent'
  | 'renewal.kpi.students'
  | 'renewal.kpi.eligible'
  | 'renewal.kpi.blocked'
  | 'renewal.kpi.carried'
  | 'renewal.kpi.ofTotal'
  | 'renewal.selectAll'
  | 'renewal.selectedCount'
  | 'renewal.reviewTitle'
  | 'renewal.notEligible'
  | 'renewal.noDoc'
  | 'renewal.expired'
  | 'renewal.reused'
  | 'renewal.none'
  | 'renewal.batchTitle'
  | 'renewal.noSelection'
  | 'renewal.batchDone'
  | 'renewal.batchError'
  | 'renewal.outcome.renewed'
  | 'renewal.outcome.skipped'
  | 'renewal.outcome.blocked'
  | 'renewal.outcome.failed'
  | 'grades.title'
  | 'grades.subtitle'
  | 'grades.classesError'
  | 'grades.rosterError'
  | 'grades.noClasses'
  | 'grades.academicStructure'
  | 'grades.field.assessment'
  | 'grades.noAssessment'
  | 'grades.weight'
  | 'grades.recovery'
  | 'grades.noAssessments'
  | 'grades.noStudents'
  | 'grades.col.grade'
  | 'grades.studentCount'
  | 'grades.gradeAria'
  | 'grades.saved'
  | 'grades.saveError'
  | 'grades.footnote'
  | 'grades.error.required'
  | 'grades.error.invalid'
  | 'grades.error.range'
  | 'reason.required'
  | 'reason.tooShort'
  | 'reason.tooLong'
  | 'docs.approveTitle'
  | 'docs.rejectTitle'
  | 'docs.approved'
  | 'docs.rejected'
  | 'docs.typesError'
  | 'docs.verifyError'
  | 'docs.downloadError'
  | 'docs.col.file'
  | 'docs.sentAt'
  | 'docs.col.validity'
  | 'docs.downloadAria'
  | 'docs.downloadTitle'
  | 'docs.subtitle.all'
  | 'docs.subtitle.expiring'
  | 'docs.view.all'
  | 'docs.view.expiring'
  | 'docs.filter.studentPlaceholder'
  | 'docs.filter.allTypes'
  | 'docs.filter.allStatus'
  | 'docs.loadListError'
  | 'docs.noneExpiring'
  | 'docs.confirmApprove'
  | 'docs.view.label'
  | 'docs.kpi.pending'
  | 'docs.kpi.approved'
  | 'docs.kpi.rejected'
  | 'docs.kpi.expiring'
  | 'docs.kpi.ofTotal'
  | 'docs.expiresToday'
  | 'docs.expiresTomorrow'
  | 'docs.expiringIn'
  | 'struct.title'
  | 'struct.subtitle'
  | 'struct.schoolYears'
  | 'struct.classes'
  | 'struct.components'
  | 'struct.assessments'
  | 'struct.empty.schoolYears'
  | 'struct.empty.classesNeedYear'
  | 'struct.empty.classes'
  | 'struct.empty.components'
  | 'struct.empty.assessmentsNeedClass'
  | 'struct.empty.assessments'
  | 'struct.loadError'
  | 'struct.saveError'
  | 'struct.deleteError'
  | 'struct.saved'
  | 'struct.deleted'
  | 'struct.edit'
  | 'struct.delete'
  | 'struct.form.title'
  | 'struct.form.new'
  | 'struct.form.edit'
  | 'struct.entity.year'
  | 'struct.entity.class'
  | 'struct.entity.component'
  | 'struct.entity.assessment'
  | 'struct.field.year'
  | 'struct.field.start'
  | 'struct.field.end'
  | 'struct.field.name'
  | 'struct.field.schoolYear'
  | 'struct.field.code'
  | 'struct.field.class'
  | 'struct.field.component'
  | 'struct.field.title'
  | 'struct.field.weight'
  | 'struct.field.date'
  | 'struct.deleteTitle'
  | 'struct.add.year'
  | 'struct.add.class'
  | 'struct.add.component'
  | 'struct.add.assessment'
  | 'struct.error.yearDigits'
  | 'struct.error.dates'
  | 'struct.error.dateOrder'
  | 'struct.error.className'
  | 'struct.error.selectYear'
  | 'struct.error.componentName'
  | 'struct.error.componentCode'
  | 'struct.error.assessmentTitle'
  | 'struct.error.selectClass'
  | 'struct.error.selectComponent'
  | 'struct.error.assessmentDate'
  | 'struct.error.weight'
  | 'dash.pendingDocs'
  | 'dash.enrollmentPeriod'
  | 'dash.noOpenPeriod'
  | 'enrollments.listSubtitle'
  | 'enrollments.allPeriods'
  | 'enrollments.loadListError'
  | 'enrollments.rejectedToast'
  | 'enrollments.reopenTitle'
  | 'priv.requestsSubtitle'
  | 'priv.emptyRequests'
  | 'priv.holdUntil'
  | 'priv.approved'
  | 'priv.refused'
  | 'priv.decisionError'
  | 'priv.completed'
  | 'priv.completeError'
  | 'priv.centerSubtitle'
  | 'priv.noStudents'
  | 'priv.noConsents'
  | 'priv.sent'
  | 'priv.sendError'
  | 'priv.downloadError'
  | 'reg.step.school'
  | 'reg.step.data'
  | 'reg.step.admin'
  | 'reg.passwordMismatch'
  | 'reg.schoolCreated'
  | 'reg.emailTaken'
  | 'reg.countryUnsupported'
  | 'reg.createFailed'
  | 'reg.schoolName'
  | 'reg.schoolNamePlaceholder'
  | 'reg.country'
  | 'reg.selectCountry'
  | 'reg.schoolData'
  | 'reg.phone'
  | 'reg.address'
  | 'reg.addressPlaceholder'
  | 'reg.city'
  | 'reg.adminData'
  | 'reg.submitting'
  | 'reg.submitSchool'
  | 'reg.parentCreated'
  | 'reg.parentCreateFailed'
  | 'reg.schoolCode'
  | 'reg.yourName'
  | 'reg.password'
  | 'reg.confirmPassword'
  | 'reg.creating'
  | 'reg.createAccount'
  | 'pick.usersLoadError'
  | 'pick.searchUsers'
  | 'pick.noRecipient'
  | 'pick.studentsLoadError'
  | 'pick.searchStudents'
  | 'country.br'
  | 'country.us'
  | 'country.pt';

const PT: Record<MessageKey, string> = {
  'common.loading': 'Carregando…',
  'common.error': 'Não foi possível carregar os dados.',
  'common.empty': 'Nada por aqui ainda.',
  'common.retry': 'Tentar novamente',
  'common.reload': 'Recarregar a página',
  'common.close': 'Fechar',
  'errorBoundary.title': 'Algo deu errado.',
  'errorBoundary.body': 'A página encontrou um erro inesperado. Recarregue para continuar.',
  'field.required': '{field} é obrigatório.',
  'list.emptyTitle': 'Nenhum registro encontrado.',
  'language.label': 'Idioma',

  'nav.dashboard': 'Dashboard',
  'nav.login': 'Login',
  'nav.students': 'Alunos',
  'nav.enrollments': 'Matrículas',
  'nav.documents': 'Documentos',
  'nav.academic': 'Acadêmico',
  'nav.notifications': 'Notificações',
  'nav.users': 'Usuários',
  'nav.settings': 'Configurações',
  'nav.privacy': 'Privacidade',
  'nav.logout': 'Sair',
  'nav.account': 'Minha conta',
  'profile.changePhoto': 'Trocar foto',
  'profile.removePhoto': 'Remover foto',
  'profile.photoSaved': 'Foto de perfil atualizada.',
  'profile.photoError': 'Não foi possível atualizar a foto.',
  'nav.openMenu': 'Abrir menu',
  'nav.closeMenu': 'Fechar menu',
  'nav.notificationsUnread': 'Notificações ({count} não lidas)',
  'nav.demo': 'demonstração',
  'nav.demoTitle': 'Os dados desta instalação são fictícios e ficam no seu navegador.',

  'language.switchAria': 'Idioma da interface',

  'settings.title': 'Configurações',
  'settings.subtitle': 'Identidade, preferências regionais, limites e funcionalidades da escola.',
  'settings.section.identity': 'Identidade da escola',
  'settings.section.regional': 'Preferências regionais',
  'settings.section.limits': 'Limites',
  'settings.section.plan': 'Plano e funcionalidades',
  'settings.features.legend': 'Funcionalidades',
  'settings.field.displayName': 'Nome de exibição',
  'settings.field.timeZone': 'Fuso horário',
  'settings.field.schoolYear': 'Ano letivo padrão',
  'settings.field.locale': 'Idioma padrão da escola',
  'settings.field.storage': 'Cota de armazenamento (MB)',
  'settings.field.userQuota': 'Cota de usuários',
  'settings.field.plan': 'Plano',
  'settings.flag.documents': 'Documentos',
  'settings.flag.notifications': 'Notificações',
  'settings.flag.parentPortal': 'Portal do responsável',
  'settings.flag.academic': 'Desempenho acadêmico',
  'settings.flag.reenrollment': 'Rematrícula em lote',
  'settings.flag.audit': 'Exportação de auditoria',
  'settings.action.save': 'Salvar',
  'settings.action.saving': 'Salvando…',
  'settings.action.discard': 'Descartar alterações',
  'settings.state.unsaved': 'Alterações não salvas',
  'settings.state.noChanges': 'Nenhuma alteração para salvar.',
  'settings.state.saved': 'Configuração salva.',
  'settings.error.load': 'Não foi possível carregar a configuração da escola.',
  'settings.error.save': 'Não foi possível salvar a configuração.',
  'settings.error.numbers': 'Informe números válidos no ano letivo e nas cotas.',
  'settings.error.featuresRequired': 'Mantenha pelo menos uma funcionalidade habilitada.',
  'settings.hint.locale': 'Trocar aqui também muda o idioma da interface; salvar define o padrão da escola.',
  'common.search': 'Buscar',
  'common.clear': 'Limpar',
  'users.col.user': 'Usuário',
  'users.col.actions': 'Ações',
  'users.searchPlaceholder': 'Buscar por nome ou e-mail',
  'users.allRoles': 'Todos os papéis',
  'users.allStatuses': 'Todas as situações',
  'users.stats.total': 'Total',
  'users.stats.admins': 'Administradores',
  'users.stats.unconfirmed': 'Sem confirmação',
  'users.stats.inactive': 'Inativos',
  'users.emptyFiltered': 'Nenhum usuário corresponde aos filtros.',
  'users.changeRoleTitle': 'Alterar papel',
  'users.changeRoleHint': 'O novo papel vale imediatamente para este usuário.',
  'users.deactivateTitle': 'Desativar usuário',
  'users.deactivateBody': 'Desativar {name}? O acesso à plataforma é bloqueado imediatamente.',
  'users.deactivateConfirm': 'Desativar',
  'enrollments.column.student': 'Aluno',
  'enrollments.column.status': 'Status',
  'enrollments.batchRenewal': 'Rematrícula em lote',
  'enrollments.clear': 'Limpar',
  'enrollments.reopenMessage': 'A matrícula volta para a fila de análise. A decisão anterior é desfeita e o registro guarda quem reabriu e quando.',
  'priv.type.export': 'Exportação',
  'priv.type.anonymization': 'Anonimização',
  'priv.type.revocation': 'Revogação de consentimento',
  'priv.status.submitted': 'Aguardando decisão',
  'priv.status.approved': 'Aprovada',
  'priv.status.rejected': 'Recusada',
  'priv.status.completed': 'Concluída',
  'priv.loadError': 'Não foi possível carregar as solicitações.',
  'pagination.showing': 'Mostrando',
  'pagination.of': 'de',
  'pagination.previous': 'Anterior',
  'pagination.next': 'Próximo',
  'pick.noUsers': 'Nenhum usuário encontrado.',
  'pick.noStudents': 'Nenhum aluno encontrado.',
  'pick.change': 'Trocar',
  'pick.minChars': 'Digite ao menos 2 letras para buscar.',
  'pick.searching': 'Buscando…',
  'pick.selectedOne': '{count} selecionado: {names}',
  'pick.selectedMany': '{count} selecionados: {names}',
  'field.emailExample': 'nome@colegio.com.br',
  'reg.schoolCodePlaceholder': 'ex.: colegio-modelo',
  'reg.schoolCodeHint': 'O colégio informa este código no primeiro contato. É o mesmo que aparece no cadastro da escola.',
  'reg.parentLinkHint': 'Depois do cadastro, a secretaria do colégio precisa vincular você ao seu filho. Até lá a conta existe, mas não mostra dados de aluno.',
  'reg.currencyLabel': 'Moeda:',
  'reg.dateLabel': 'Data:',
  'reg.schoolYearLabel': 'Ano letivo:',
  'struct.actions.gradeEntry': 'Lançar notas',
  'struct.actions.newYear': 'Novo ano letivo',
  'struct.active': 'Ativo',
  'struct.inactive': 'Inativo',
  'struct.yearLabel': 'Ano letivo {year}',
  'struct.assessmentWeight': 'peso {weight}',
  'struct.recoveryTag': 'Recuperação',
  'struct.kpi.activeOf': 'Ativos: {active} de {total}',
  'struct.kpi.noActiveYear': 'Nenhum ano letivo ativo',
  'struct.kpi.classesInActiveYears': 'Em anos letivos ativos: {count}',
  'struct.kpi.recovery': 'Recuperação: {count} de {total}',
  'struct.form.yearActive': 'Ano letivo ativo',
  'struct.form.componentActive': 'Componente ativo',
  'struct.form.isRecovery': 'É avaliação de recuperação',
  'struct.dateRange': '{start} a {end}',
  'struct.deleteEntity.year': 'o ano letivo',
  'struct.deleteEntity.class': 'a turma',
  'struct.deleteEntity.component': 'o componente',
  'struct.deleteEntity.assessment': 'a avaliação',
  'struct.deletePrompt': 'Excluir {entity}? Cadastros com dependentes são recusados pela API.',
  'dash.greeting': 'Olá, {name}. Visão geral da escola.',
  'dash.subtitle': 'Visão geral da escola.',
  'dash.updatedAt': 'Atualizado às {time}',
  'dash.refresh': 'Atualizar',
  'dash.partialWarning': 'Alguns indicadores não puderam ser carregados — os valores abaixo podem estar incompletos.',
  'dash.activeOfTotal': '{active} ativos de {total}',
  'dash.awaitingReview': '{count} aguardando análise',
  'dash.expiringIn30': '{count} vencem em 30 dias',
  'dash.ofTotal': 'de {total} no total',
  'dash.progressPercent': '{percent}% do período transcorrido',
  'dash.lastDay': 'último dia',
  'dash.daysLeftOne': 'resta {days} dia',
  'dash.daysLeftMany': 'restam {days} dias',
  'dash.viewEnrollments': 'Ver matrículas',
  'dash.closed': 'Encerrado',
  'dash.closesToday': 'Encerra hoje',
  'priv.column.requester': 'Solicitante',
  'priv.column.type': 'Tipo',
  'priv.column.status': 'Situação',
  'priv.column.student': 'Aluno',
  'priv.column.legalHold': 'Bloqueio legal',
  'priv.column.retainUntil': 'Reter até',
  'priv.column.submittedAt': 'Enviada em',
  'priv.column.actions': 'Ações',
  'priv.yes': 'Sim',
  'priv.no': 'Não',
  'priv.approve': 'Aprovar',
  'priv.reject': 'Recusar',
  'priv.completing': 'Concluindo…',
  'priv.complete': 'Concluir',
  'priv.approveTitle': 'Aprovar solicitação',
  'priv.rejectTitle': 'Recusar solicitação',
  'priv.resolutionOptional': 'Parecer (opcional)',
  'priv.reasonOptional': 'Motivo da recusa (opcional)',
  'priv.legalHoldHint': 'Bloqueio legal: impede a anonimização até a liberação',
  'priv.requestExport': 'Solicitar exportação',
  'priv.requestAnonymization': 'Solicitar anonimização',
  'priv.granted': 'Concedido',
  'priv.revoked': 'Revogado',
  'priv.sending': 'Enviando…',
  'priv.downloading': 'Baixando…',
  'priv.downloadExport': 'Baixar exportação',
  'priv.legalBasis': 'Base legal: {basis}',
  'priv.grantedAt': 'Concedido em {date}',
  'priv.revokedAt': 'Revogado em {date}',
  'priv.openRequestHint': 'Pedidos com solicitação aberta do mesmo tipo ficam desabilitados até a conclusão pelo colégio.',
  'priv.consentsTitle': 'Consentimentos',
  'priv.openRequests': 'Solicitações abertas: {count}',
  'settings.hint.plan': 'Trocar o plano sem alterar as funcionalidades aplica o conjunto padrão do plano.',

  'common.cancel': 'Cancelar',
  'common.confirm': 'Confirmar',
  'splash.subtitle': 'Seu Sistema de Gestão Escolar',
  'splash.continue': 'Continuar para o login',
  'splash.prompt': 'Toque ou pressione qualquer tecla para continuar',
  'enrollment.status.draft': 'Rascunho',
  'enrollment.status.pending': 'Pendente',
  'enrollment.status.documentsRequired': 'Documentação pendente',
  'enrollment.status.approved': 'Aprovado',
  'enrollment.status.rejected': 'Rejeitado',
  'enrollment.status.cancelled': 'Cancelado',
  'enrollment.action.submit': 'Enviar',
  'enrollment.action.approve': 'Aprovar',
  'enrollment.action.reject': 'Rejeitar',
  'enrollment.action.cancel': 'Cancelar',
  'enrollment.action.reopen': 'Reabrir',
  'role.admin': 'Administração',
  'role.staff': 'Secretaria',
  'role.parent': 'Responsável',
  'user.activate': 'Ativar',
  'user.deactivate': 'Desativar',
  'user.roleChanged': 'Papel de {name} alterado para {role}.',
  'field.name': 'Nome',
  'field.email': 'E-mail',
  'field.role': 'Papel',
  'users.subtitle': 'Quem tem acesso ao colégio e com qual papel.',
  'users.invite': 'Convidar',
  'users.restricted': 'Acesso restrito ao administrador do colégio.',
  'users.loadError': 'Não foi possível carregar os usuários.',
  'users.inviteError': 'Não foi possível convidar o usuário.',
  'users.roleError': 'Não foi possível alterar o papel.',
  'users.activeError': 'Não foi possível alterar a situação do usuário.',
  'users.empty': 'Nenhum usuário cadastrado.',
  'users.col.status': 'Situação',
  'users.you': '(você)',
  'users.active': 'Ativo',
  'users.inactive': 'Inativo',
  'users.emailNotConfirmed': 'E-mail não confirmado',
  'users.activated': '{name} foi ativado.',
  'users.deactivated': '{name} foi desativado.',
  'users.inviteTitle': 'Convidar usuário',
  'users.inviteConfirm': 'Convidar',
  'users.inviteHint': 'A conta nasce sem senha: o usuário só passa a entrar depois de aceitar o convite.',
  'users.inviteCreated': 'Convite criado',
  'users.inviteCreatedBody': 'Envie as instruções para {email}. O usuário define a senha pelo fluxo de recuperação, usando o token abaixo.',
  'users.tokenCopied': 'Token copiado.',
  'users.copyError': 'Não foi possível copiar — selecione o texto manualmente.',
  'users.copyToken': 'Copiar token',
  'users.tokenHidden': 'O token não é exibido neste ambiente: ele deve ser entregue por e-mail ao convidado.',
  'users.validate.name': 'Informe o nome.',
  'users.validate.email': 'Informe um e-mail válido.',
  'users.validate.role': 'Escolha um papel válido.',
  'common.back': 'Voltar',
  'common.status': 'Status',
  'common.remove': 'Remover',
  'students.loadError': 'Não foi possível carregar o aluno.',
  'students.parentLinked': 'Responsável vinculado!',
  'students.parentLinkError': 'Não foi possível vincular o responsável.',
  'students.parentUnlinked': 'Responsável desvinculado.',
  'students.parentUnlinkError': 'Não foi possível desvincular o responsável.',
  'students.dataTitle': 'Dados do aluno',
  'students.field.class': 'Turma',
  'students.field.schoolYear': 'Ano letivo',
  'students.field.notes': 'Observações',
  'students.parents': 'Responsáveis',
  'students.noParents': 'Nenhum responsável vinculado.',
  'students.parentEmailPlaceholder': 'Email do responsável',
  'students.linkParent': 'Vincular',
  'students.detail.subtitle': '{class} · ano letivo {year}',
  'notifications.subtitle': 'Avisos enviados e recebidos na escola.',
  'notifications.new': 'Nova',
  'notifications.history': 'Histórico',
  'notifications.countOne': '{count} registro',
  'notifications.countMany': '{count} registros',
  'notifications.empty': 'Nenhuma notificação registrada.',
  'notifications.loadError': 'Não foi possível carregar as notificações.',
  'notifications.recent': 'Atividade recente',
  'notifications.unreadOne': '{count} não lida',
  'notifications.unreadMany': '{count} não lidas',
  'notifications.viewAll': 'Ver todas',
  'notifications.recentEmpty': 'Nenhuma notificação por aqui.',
  'auth.brand.login': 'Sua Plataforma Educacional',
  'auth.brand.register': 'Cadastro de colégio',
  'auth.brand.parent': 'Conta de responsável',
  'auth.chip.institutional': 'Ambiente institucional',
  'auth.chip.access': 'Acesso institucional',
  'auth.title.login': 'Acesse seu espaço de trabalho',
  'auth.title.register': 'Cadastre seu colégio',
  'auth.title.parent': 'Crie sua conta de responsável',
  'auth.subtitle.login': 'Entre com suas credenciais para acompanhar alunos, matrículas e documentos.',
  'auth.subtitle.register': 'Leve a gestão escolar da sua escola para o Ciclo em três passos.',
  'auth.subtitle.parent': 'Informe o código do colégio e defina seu acesso.',
  'auth.capability.enrollments.title': 'Matrículas e turmas',
  'auth.capability.enrollments.text': 'Alunos, turmas e matrículas organizados em um fluxo só.',
  'auth.capability.documents.title': 'Documentos e avisos',
  'auth.capability.documents.text': 'Envio de documentos e comunicados com acompanhamento.',
  'auth.capability.roles.title': 'Acesso por perfil',
  'auth.capability.roles.text': 'Administração e responsáveis veem apenas o que é de cada um.',
  'auth.field.password': 'Senha',
  'auth.forgot': 'Esqueceu a senha?',
  'auth.showPassword': 'Mostrar senha',
  'auth.hidePassword': 'Ocultar senha',
  'auth.remember': 'Lembrar neste navegador por 30 dias',
  'auth.submit': 'Entrar na plataforma',
  'auth.submitting': 'Entrando...',
  'auth.or': 'ou acesso com Google',
  'auth.google': 'Entrar com Google',
  'auth.demoNotice': 'Demonstração — dados fictícios gravados neste navegador, sem backend.',
  'auth.demoAdmin': 'Entrar como administração',
  'auth.demoParent': 'Entrar como responsável',
  'auth.googleFailed': 'Falha na autenticação Google.',
  'auth.demoFailed': 'Não foi possível entrar na demonstração.',
  'auth.loginSuccess': 'Login realizado!',
  'auth.offline': 'Não foi possível conectar ao servidor. Verifique sua conexão.',
  'auth.badCredentials': 'E-mail ou senha incorretos.',
  'auth.registerPrompt': 'Precisa cadastrar sua instituição?',
  'auth.registerAction': 'Cadastrar meu colégio',
  'auth.parentPrompt': 'É responsável por um aluno?',
  'auth.parentAction': 'Criar conta de responsável',
  'auth.backToLogin': 'Voltar para o login',
  'auth.pedagogical.title': 'Visão pedagógica',
  'auth.pedagogical.text': 'Gestão escolar próxima e organizada.',
  'auth.copyright': '© 2026 Ciclo Plataforma Educacional',
  'auth.rights': 'Todos os direitos reservados',
  'parent.children': 'Filhos',
  'forgot.title.email': 'Esqueci minha senha',
  'forgot.title.reset': 'Nova senha',
  'forgot.subtitle.email': 'Informe o email cadastrado para continuar.',
  'forgot.subtitle.reset': 'Defina sua nova senha seguindo o padrão de segurança.',
  'forgot.emailLabel': 'Email cadastrado',
  'forgot.continue': 'Continuar',
  'forgot.verifying': 'Verificando...',
  'forgot.newPassword': 'Nova senha',
  'forgot.confirmPassword': 'Confirmar nova senha',
  'forgot.requirements': 'Requisitos da senha:',
  'forgot.req.length': 'Mínimo de 8 caracteres',
  'forgot.req.upper': 'Pelo menos 1 letra maiúscula',
  'forgot.req.digit': 'Pelo menos 1 número',
  'forgot.req.symbol': 'Pelo menos 1 símbolo (ex.: !@#$%)',
  'forgot.change': 'Alterar senha',
  'forgot.changing': 'Alterando senha...',
  'forgot.sent': 'Se o email estiver cadastrado, você receberá as instruções de recuperação.',
  'forgot.verified': 'Email verificado! Agora defina sua nova senha.',
  'forgot.requestFailed': 'Não foi possível processar a solicitação. Tente novamente.',
  'forgot.changed': 'Senha alterada com sucesso! Faça login com a sua nova senha.',
  'forgot.resetFailed': 'Não foi possível redefinir a senha.',
  'forgot.mismatch': 'As senhas não conferem.',
  'password.min': 'A senha deve ter no mínimo 8 caracteres.',
  'password.upper': 'A senha deve ter pelo menos uma letra maiúscula.',
  'password.digit': 'A senha deve ter pelo menos um número.',
  'password.symbol': 'A senha deve ter pelo menos um símbolo (ex.: !@#$%).',
  'parent.title': 'Meus Filhos',
  'parent.greeting': 'Olá, {name}. Acompanhe a vida escolar dos seus filhos.',
  'parent.loadFailed': 'Não foi possível carregar os dados.',
  'parent.childrenLinked': 'Filhos vinculados',
  'parent.activeEnrollments': 'Matrículas ativas',
  'parent.pendingDocs': 'Docs Pendentes',
  'parent.unread': 'Não Lidas',
  'parent.myChildren': 'Meus filhos',
  'parent.noChildren': 'Nenhum filho vinculado.',
  'parent.pendingDocsBadge': '{count} doc(s) pendente(s)',
  'parent.performanceUnavailable': 'Desempenho indisponível.',
  'parent.noGrades': 'Sem notas lançadas.',
  'parent.average': 'Média',
  'parent.subjectOne': '{count} disciplina',
  'parent.subjectMany': '{count} disciplinas',
  'students.subtitle': 'Cadastro e situação dos alunos da escola.',
  'students.new': 'Novo aluno',
  'students.search': 'Buscar',
  'students.searchPlaceholder': 'Buscar por nome',
  'students.filterClass': 'Filtrar por turma',
  'students.classPlaceholder': 'Turma',
  'students.filterStatus': 'Filtrar por status',
  'students.allStatus': 'Todos os status',
  'students.clear': 'Limpar',
  'students.loadListError': 'Não foi possível carregar os alunos. Tente novamente.',
  'students.removed': 'Aluno removido.',
  'students.removeError': 'Não foi possível remover o aluno.',
  'students.col.student': 'Aluno',
  'students.edit': 'Editar',
  'students.removeTitle': 'Remover aluno',
  'students.removeBody': 'O aluno e os dados vinculados a ele serão removidos. Esta ação não pode ser desfeita.',
  'students.empty': 'Nenhum aluno encontrado.',
  'student.status.active': 'Ativo',
  'student.status.inactive': 'Inativo',
  'student.status.transferred': 'Transferido',
  'students.form.editTitle': 'Editar aluno',
  'students.form.editSubtitle': 'Altere os dados e salve.',
  'students.form.newSubtitle': 'Preencha os dados para cadastrar o aluno.',
  'students.field.photo': 'Foto do aluno',
  'students.field.choosePhoto': 'Escolher foto',
  'students.field.removePhoto': 'Remover foto',
  'students.field.photoHint': 'A imagem é redimensionada para {size}px antes do envio.',
  'students.field.birthDate': 'Data de Nascimento',
  'students.field.cpf': 'CPF',
  'students.photoTypeError': 'Selecione um arquivo de imagem.',
  'students.photoProcessError': 'Não foi possível processar a imagem.',
  'students.updated': 'Aluno atualizado!',
  'students.created': 'Aluno criado!',
  'students.saveError': 'Não foi possível salvar o aluno.',
  'common.save': 'Salvar',
  'common.saving': 'Salvando...',
  'enrollments.loadError': 'Não foi possível carregar a matrícula.',
  'enrollments.approved': 'Aprovada!',
  'enrollments.approveError': 'Não foi possível aprovar a matrícula.',
  'enrollments.rejected': 'Rejeitada.',
  'enrollments.rejectError': 'Não foi possível rejeitar a matrícula.',
  'enrollments.detailTitle': 'Detalhes da matrícula',
  'enrollments.info': 'Informações',
  'enrollments.field.period': 'Período',
  'enrollments.field.reason': 'Motivo',
  'enrollments.field.date': 'Data',
  'enrollments.field.approvedAt': 'Aprovada em',
  'enrollments.documents': 'Situação documental',
  'enrollments.missingDocs': 'Documentos obrigatórios pendentes',
  'enrollments.docsOk': 'Documentação obrigatória em dia.',
  'enrollments.studentDocs': 'Documentos do aluno',
  'enrollments.expired': 'vencido — precisa reenviar',
  'enrollments.validUntil': 'válido até {date}',
  'enrollments.noExpiry': 'sem validade',
  'enrollments.noDocs': 'Nenhum documento registrado para este aluno.',
  'enrollments.actions': 'Ações',
  'enrollments.rejectTitle': 'Rejeitar matrícula',
  'enrollments.rejectReason': 'Motivo da rejeição',
  'docs.uploadError': 'Erro no upload. Tente novamente.',
  'docs.loadError': 'Não foi possível carregar os documentos.',
  'docs.loadTypesError': 'Não foi possível carregar os tipos de documento.',
  'docs.selectType': 'Selecione o tipo de documento.',
  'docs.tooLarge': 'Arquivo muito grande (máximo 10MB).',
  'docs.uploaded': 'Upload concluído!',
  'docs.subtitle': 'Envie e acompanhe os documentos do aluno.',
  'docs.field.type': 'Tipo de documento',
  'docs.field.file': 'Arquivo',
  'docs.selectPlaceholder': 'Selecione...',
  'docs.requiredSuffix': ' (obrigatório)',
  'docs.sending': 'Enviando...',
  'docs.upload': 'Upload',
  'docs.empty': 'Nenhum documento.',
  'docs.rejectReason': 'Motivo da rejeição: {reason}',
  'inbox.subtitle': 'Avisos da escola para você.',
  'inbox.markAll': 'Marcar todas',
  'inbox.title': 'Caixa de entrada',
  'inbox.countOne': '{count} aviso',
  'inbox.countMany': '{count} avisos',
  'inbox.emptyUnread': 'Nenhuma notificação não lida.',
  'inbox.markRead': 'Marcar como lida',
  'inbox.markReadError': 'Não foi possível marcar como lida.',
  'inbox.markAllError': 'Não foi possível marcar todas como lidas.',
  'inbox.markedAll': '{count} marcadas como lidas.',
  'academic.status.approved': 'Aprovado',
  'academic.status.recovery': 'Em recuperação',
  'academic.status.failed': 'Reprovado',
  'academic.status.noGrade': 'Sem nota',
  'academic.status.none': 'Sem situação',
  'child.reportError': 'Não foi possível carregar o boletim.',
  'child.tab.grades': 'Notas',
  'child.data': 'Dados',
  'child.field.year': 'Ano',
  'child.enrollment': 'Matrícula atual',
  'child.noEnrollment': 'Nenhuma matrícula ativa.',
  'child.docsTitle': 'Documentos ({count})',
  'child.uploadDoc': 'Fazer upload de documento',
  'child.report': 'Boletim',
  'child.noGrades': 'Nenhuma nota disponível.',
  'child.col.component': 'Componente',
  'child.col.regular': 'Média regular',
  'child.col.recovery': 'Recuperação',
  'child.col.final': 'Média final',
  'child.kpi.components': 'Componentes',
  'child.kpi.ofTotal': '{count} de {total}',
  'notify.createTitle': 'Nova notificação',
  'notify.createSubtitle': 'Escolha o destino, escreva e envie.',
  'notify.compose': 'Compor mensagem',
  'notify.field.title': 'Título',
  'notify.field.message': 'Mensagem',
  'notify.field.type': 'Tipo',
  'notify.field.target': 'Destino',
  'notify.mode.broadcast': 'Broadcast',
  'notify.mode.student': 'Por aluno',
  'notify.broadcastNotice': 'O aviso vai para todos os usuários do colégio.',
  'notify.sendAll': 'Enviar para todos',
  'notify.send': 'Enviar',
  'notify.missingRecipient': 'Escolha ao menos um destinatário para enviar.',
  'notify.sentBroadcast': 'Broadcast enviado!',
  'notify.sentStudent': 'Notificação enviada!',
  'notify.created': 'Notificação criada!',
  'notify.createError': 'Não foi possível criar a notificação.',
  'notify.kind.general': 'Geral',
  'notify.kind.docPending': 'Documento pendente',
  'notify.kind.meeting': 'Reunião',
  'notify.kind.other': 'Outro',
  'renewal.title': 'Rematrícula em lote',
  'renewal.subtitle': 'Prévia por aluno, com os documentos reaproveitados antes de confirmar.',
  'renewal.periodsError': 'Não foi possível carregar os períodos.',
  'renewal.previewError': 'Não foi possível carregar a prévia.',
  'renewal.noPeriods': 'Nenhum período de matrícula cadastrado. Crie o período do próximo ano antes de renovar.',
  'renewal.periodLabel': 'Período de destino',
  'renewal.selectPeriod': 'Selecione um período',
  'renewal.processing': 'Processando…',
  'renewal.renewCount': 'Renovar {count} selecionado(s)',
  'renewal.choosePeriod': 'Escolha um período para ver a prévia.',
  'renewal.noCandidates': 'Nenhum aluno candidato à renovação neste período.',
  'renewal.summary': '{total} aluno(s) candidato(s) · {eligible} elegível(is). A prévia não grava nada: só a confirmação renova.',
  'renewal.col.carried': 'Documentos reaproveitados',
  'renewal.col.pending': 'Pendências',
  'renewal.selectStudent': 'Selecionar {name}',
  'renewal.kpi.students': 'Alunos',
  'renewal.kpi.eligible': 'Elegíveis',
  'renewal.kpi.blocked': 'Bloqueados',
  'renewal.kpi.carried': 'Documentos reaproveitados',
  'renewal.kpi.ofTotal': '{count} de {total}',
  'renewal.selectAll': 'Selecionar todos os elegíveis',
  'renewal.selectedCount': 'Selecionados: {count}',
  'renewal.reviewTitle': 'Alunos para rematrícula',
  'renewal.notEligible': 'Não elegível para renovação.',
  'renewal.noDoc': 'Nenhum documento',
  'renewal.expired': 'vencido',
  'renewal.reused': 'reaproveitado',
  'renewal.none': 'Nenhuma',
  'renewal.batchTitle': 'Desfecho do lote · ano letivo {year}',
  'renewal.noSelection': 'Selecione ao menos um aluno elegível.',
  'renewal.batchDone': 'Lote processado. Confira o desfecho de cada aluno.',
  'renewal.batchError': 'Não foi possível executar o lote.',
  'renewal.outcome.renewed': 'Renovado',
  'renewal.outcome.skipped': 'Já existia',
  'renewal.outcome.blocked': 'Documentação pendente',
  'renewal.outcome.failed': 'Falhou',
  'grades.title': 'Lançamento de notas',
  'grades.subtitle': 'Escolha a turma e a avaliação, depois grave a nota de cada aluno.',
  'grades.classesError': 'Não foi possível carregar as turmas.',
  'grades.rosterError': 'Não foi possível carregar a turma.',
  'grades.noClasses': 'Nenhuma turma cadastrada ainda.',
  'grades.academicStructure': 'Estrutura acadêmica',
  'grades.field.assessment': 'Avaliação',
  'grades.noAssessment': 'Nenhuma avaliação nesta turma',
  'grades.weight': 'peso',
  'grades.recovery': 'recuperação',
  'grades.noAssessments': 'Esta turma não tem avaliações.',
  'grades.noStudents': 'Nenhum aluno nesta turma.',
  'grades.col.grade': 'Nota (0 a 10)',
  'grades.studentCount': 'Alunos: {count}',
  'grades.gradeAria': 'Nota de {name}',
  'grades.saved': 'Nota de {name} salva.',
  'grades.saveError': 'Não foi possível salvar a nota de {name}.',
  'grades.footnote': 'Notas já lançadas antes desta sessão não aparecem aqui — a API ainda não expõe leitura por avaliação. Lançar novamente a mesma nota atualiza o registro.',
  'grades.error.required': 'Informe a nota.',
  'grades.error.invalid': 'Nota inválida — use números, como 7,5.',
  'grades.error.range': 'A nota deve estar entre {min} e {max}.',
  'reason.required': 'Informe o motivo da rejeição.',
  'reason.tooShort': 'O motivo deve ter ao menos {min} caracteres.',
  'reason.tooLong': 'O motivo deve ter no máximo {max} caracteres.',
  'docs.approveTitle': 'Aprovar documento',
  'docs.rejectTitle': 'Rejeitar documento',
  'docs.approved': 'Documento aprovado.',
  'docs.rejected': 'Documento rejeitado.',
  'docs.typesError': 'Não foi possível carregar os tipos.',
  'docs.verifyError': 'Não foi possível concluir a verificação.',
  'docs.downloadError': 'Erro ao baixar o documento.',
  'docs.col.file': 'Arquivo',
  'docs.sentAt': 'Enviado em {date}',
  'docs.col.validity': 'Validade',
  'docs.downloadAria': 'Baixar {name}',
  'docs.downloadTitle': 'Baixar arquivo',
  'docs.subtitle.all': 'Documentos de todos os alunos, com filtros.',
  'docs.subtitle.expiring': 'Documentos aprovados que vencem nos próximos 30 dias.',
  'docs.view.all': 'Todos',
  'docs.view.expiring': 'A vencer',
  'docs.filter.studentPlaceholder': 'Buscar aluno por nome',
  'docs.filter.allTypes': 'Todos os tipos',
  'docs.filter.allStatus': 'Todos os status',
  'docs.loadListError': 'Não foi possível carregar os documentos. Tente novamente.',
  'docs.noneExpiring': 'Nenhum documento vence nos próximos 30 dias.',
  'docs.confirmApprove': 'Confirmar a aprovação deste documento?',
  'docs.view.label': 'Visão dos documentos',
  'docs.kpi.pending': 'Pendentes',
  'docs.kpi.approved': 'Aprovados',
  'docs.kpi.rejected': 'Rejeitados',
  'docs.kpi.expiring': 'A vencer em 30 dias',
  'docs.kpi.ofTotal': '{count} de {total}',
  'docs.expiresToday': 'vence hoje',
  'docs.expiresTomorrow': 'vence amanhã',
  'docs.expiringIn': 'vence em {days} dias',
  'struct.title': 'Estrutura acadêmica',
  'struct.subtitle': 'Anos letivos, turmas, componentes curriculares e avaliações.',
  'struct.schoolYears': 'Anos letivos',
  'struct.classes': 'Turmas',
  'struct.components': 'Componentes curriculares',
  'struct.assessments': 'Avaliações',
  'struct.empty.schoolYears': 'Nenhum ano letivo cadastrado.',
  'struct.empty.classesNeedYear': 'Cadastre um ano letivo antes de criar turmas.',
  'struct.empty.classes': 'Nenhuma turma cadastrada.',
  'struct.empty.components': 'Nenhum componente curricular cadastrado.',
  'struct.empty.assessmentsNeedClass': 'Cadastre uma turma e um componente antes de criar avaliações.',
  'struct.empty.assessments': 'Nenhuma avaliação cadastrada.',
  'struct.loadError': 'Não foi possível carregar a estrutura acadêmica.',
  'struct.saveError': 'Não foi possível salvar o cadastro.',
  'struct.deleteError': 'Não foi possível excluir o cadastro.',
  'struct.saved': 'Cadastro de {entity} salvo.',
  'struct.deleted': 'Cadastro excluído.',
  'struct.edit': 'Editar',
  'struct.delete': 'Excluir',
  'struct.form.title': '{action} {entity}',
  'struct.form.new': 'Novo',
  'struct.form.edit': 'Editar',
  'struct.entity.year': 'ano letivo',
  'struct.entity.class': 'turma',
  'struct.entity.component': 'componente curricular',
  'struct.entity.assessment': 'avaliação',
  'struct.field.year': 'Ano',
  'struct.field.start': 'Início',
  'struct.field.end': 'Fim',
  'struct.field.name': 'Nome',
  'struct.field.schoolYear': 'Ano letivo',
  'struct.field.code': 'Código',
  'struct.field.class': 'Turma',
  'struct.field.component': 'Componente',
  'struct.field.title': 'Título',
  'struct.field.weight': 'Peso',
  'struct.field.date': 'Data',
  'struct.deleteTitle': 'Confirmar exclusão',
  'struct.add.year': 'Novo ano',
  'struct.add.class': 'Nova turma',
  'struct.add.component': 'Novo componente',
  'struct.add.assessment': 'Nova avaliação',
  'struct.error.yearDigits': 'Informe o ano com quatro dígitos.',
  'struct.error.dates': 'Informe o início e o fim do ano letivo.',
  'struct.error.dateOrder': 'A data de término não pode ser anterior à de início.',
  'struct.error.className': 'Informe o nome da turma.',
  'struct.error.selectYear': 'Selecione o ano letivo.',
  'struct.error.componentName': 'Informe o nome do componente.',
  'struct.error.componentCode': 'Informe o código do componente.',
  'struct.error.assessmentTitle': 'Informe o título da avaliação.',
  'struct.error.selectClass': 'Selecione a turma.',
  'struct.error.selectComponent': 'Selecione o componente.',
  'struct.error.assessmentDate': 'Informe a data da avaliação.',
  'struct.error.weight': 'O peso deve ser maior que zero.',
  'dash.pendingDocs': 'Documentos pendentes',
  'dash.enrollmentPeriod': 'Período de matrícula',
  'dash.noOpenPeriod': 'Nenhum período de matrícula aberto.',
  'enrollments.listSubtitle': 'Acompanhe e aprove as matrículas por período.',
  'enrollments.allPeriods': 'Todos os períodos',
  'enrollments.loadListError': 'Não foi possível carregar as matrículas. Tente novamente.',
  'enrollments.rejectedToast': 'Matrícula rejeitada.',
  'enrollments.reopenTitle': 'Reabrir matrícula',
  'priv.requestsSubtitle': 'Solicitações de titulares, decisões e execução do colégio.',
  'priv.emptyRequests': 'Nenhuma solicitação registrada.',
  'priv.holdUntil': 'Reter até (opcional)',
  'priv.approved': 'Solicitação aprovada.',
  'priv.refused': 'Solicitação recusada.',
  'priv.decisionError': 'Não foi possível registrar a decisão.',
  'priv.completed': 'Solicitação concluída.',
  'priv.completeError': 'Não foi possível concluir a solicitação.',
  'priv.centerSubtitle': 'Consentimentos registrados e solicitações sobre os dados dos seus filhos.',
  'priv.noStudents': 'Nenhum aluno vinculado ao seu acesso.',
  'priv.noConsents': 'Nenhum consentimento registrado.',
  'priv.sent': 'Solicitação enviada.',
  'priv.sendError': 'Não foi possível enviar a solicitação.',
  'priv.downloadError': 'Não foi possível baixar o documento.',
  'reg.step.school': 'Escola',
  'reg.step.data': 'Cadastro',
  'reg.step.admin': 'Administrador',
  'reg.passwordMismatch': 'As senhas não conferem.',
  'reg.schoolCreated': 'Colégio cadastrado! Faça login com o email do administrador.',
  'reg.emailTaken': 'Este email já está cadastrado.',
  'reg.countryUnsupported': 'País não suportado.',
  'reg.createFailed': 'Não foi possível concluir o cadastro. Verifique os dados.',
  'reg.schoolName': 'Nome do colégio',
  'reg.schoolNamePlaceholder': 'Ex.: Colégio Dom Bosco',
  'reg.country': 'País',
  'reg.selectCountry': 'Selecione um país',
  'reg.schoolData': 'Dados da escola',
  'reg.phone': 'Telefone',
  'reg.address': 'Endereço',
  'reg.addressPlaceholder': 'Rua, número, bairro',
  'reg.city': 'Cidade',
  'reg.adminData': 'Administrador da conta',
  'reg.submitting': 'Cadastrando...',
  'reg.submitSchool': 'Cadastrar colégio',
  'reg.parentCreated': 'Conta criada. A secretaria precisa vincular seu filho para você ver os dados.',
  'reg.parentCreateFailed': 'Não foi possível criar a conta.',
  'reg.schoolCode': 'Código do colégio',
  'reg.yourName': 'Seu nome',
  'reg.password': 'Senha',
  'reg.confirmPassword': 'Confirmar senha',
  'reg.creating': 'Criando conta...',
  'reg.createAccount': 'Criar conta',
  'pick.usersLoadError': 'Não foi possível carregar os usuários.',
  'pick.searchUsers': 'Buscar por nome ou e-mail',
  'pick.noRecipient': 'Nenhum destinatário selecionado.',
  'pick.studentsLoadError': 'Não foi possível buscar os alunos.',
  'pick.searchStudents': 'Buscar aluno por nome',
  'country.br': 'Brasil',
  'country.us': 'Estados Unidos',
  'country.pt': 'Portugal',

};

const EN: Record<MessageKey, string> = {
  'common.loading': 'Loading…',
  'common.error': 'The data could not be loaded.',
  'common.empty': 'Nothing here yet.',
  'common.retry': 'Try again',
  'common.reload': 'Reload the page',
  'common.close': 'Close',
  'errorBoundary.title': 'Something went wrong.',
  'errorBoundary.body': 'The page hit an unexpected error. Reload to continue.',
  'field.required': '{field} is required.',
  'list.emptyTitle': 'No records found.',
  'language.label': 'Language',

  'nav.dashboard': 'Dashboard',
  'nav.login': 'Sign in',
  'nav.students': 'Students',
  'nav.enrollments': 'Enrollments',
  'nav.documents': 'Documents',
  'nav.academic': 'Academic',
  'nav.notifications': 'Notifications',
  'nav.users': 'Users',
  'nav.settings': 'Settings',
  'nav.privacy': 'Privacy',
  'nav.logout': 'Sign out',
  'nav.account': 'My account',
  'profile.changePhoto': 'Change photo',
  'profile.removePhoto': 'Remove photo',
  'profile.photoSaved': 'Profile photo updated.',
  'profile.photoError': 'The photo could not be updated.',
  'nav.openMenu': 'Open menu',
  'nav.closeMenu': 'Close menu',
  'nav.notificationsUnread': 'Notifications ({count} unread)',
  'nav.demo': 'demo',
  'nav.demoTitle': 'The data in this installation is fictional and stays in your browser.',

  'language.switchAria': 'Interface language',

  'settings.title': 'Settings',
  'settings.subtitle': 'School identity, regional preferences, limits and features.',
  'settings.section.identity': 'School identity',
  'settings.section.regional': 'Regional preferences',
  'settings.section.limits': 'Limits',
  'settings.section.plan': 'Plan and features',
  'settings.features.legend': 'Features',
  'settings.field.displayName': 'Display name',
  'settings.field.timeZone': 'Time zone',
  'settings.field.schoolYear': 'Default school year',
  'settings.field.locale': 'School default language',
  'settings.field.storage': 'Storage quota (MB)',
  'settings.field.userQuota': 'User quota',
  'settings.field.plan': 'Plan',
  'settings.flag.documents': 'Documents',
  'settings.flag.notifications': 'Notifications',
  'settings.flag.parentPortal': 'Guardian portal',
  'settings.flag.academic': 'Academic performance',
  'settings.flag.reenrollment': 'Batch re-enrollment',
  'settings.flag.audit': 'Audit export',
  'settings.action.save': 'Save',
  'settings.action.saving': 'Saving…',
  'settings.action.discard': 'Discard changes',
  'settings.state.unsaved': 'Unsaved changes',
  'settings.state.noChanges': 'No changes to save.',
  'settings.state.saved': 'Settings saved.',
  'settings.error.load': 'The school settings could not be loaded.',
  'settings.error.save': 'The settings could not be saved.',
  'settings.error.numbers': 'Enter valid numbers for the school year and quotas.',
  'settings.error.featuresRequired': 'Keep at least one feature enabled.',
  'settings.hint.locale': 'Changing this also switches the interface language; saving sets the school default.',
  'common.search': 'Search',
  'common.clear': 'Clear',
  'users.col.user': 'User',
  'users.col.actions': 'Actions',
  'users.searchPlaceholder': 'Search by name or email',
  'users.allRoles': 'All roles',
  'users.allStatuses': 'All statuses',
  'users.stats.total': 'Total',
  'users.stats.admins': 'Administrators',
  'users.stats.unconfirmed': 'Unconfirmed',
  'users.stats.inactive': 'Inactive',
  'users.emptyFiltered': 'No user matches the filters.',
  'users.changeRoleTitle': 'Change role',
  'users.changeRoleHint': 'The new role applies to this user immediately.',
  'users.deactivateTitle': 'Deactivate user',
  'users.deactivateBody': 'Deactivate {name}? Access to the platform is blocked immediately.',
  'users.deactivateConfirm': 'Deactivate',
  'enrollments.column.student': 'Student',
  'enrollments.column.status': 'Status',
  'enrollments.batchRenewal': 'Batch renewal',
  'enrollments.clear': 'Clear',
  'enrollments.reopenMessage': 'The enrollment returns to the review queue. The previous decision is undone and the record keeps who reopened it and when.',
  'priv.type.export': 'Export',
  'priv.type.anonymization': 'Anonymization',
  'priv.type.revocation': 'Consent revocation',
  'priv.status.submitted': 'Awaiting decision',
  'priv.status.approved': 'Approved',
  'priv.status.rejected': 'Rejected',
  'priv.status.completed': 'Completed',
  'priv.loadError': 'The requests could not be loaded.',
  'pagination.showing': 'Showing',
  'pagination.of': 'of',
  'pagination.previous': 'Previous',
  'pagination.next': 'Next',
  'pick.noUsers': 'No users found.',
  'pick.noStudents': 'No students found.',
  'pick.change': 'Change',
  'pick.minChars': 'Type at least 2 letters to search.',
  'pick.searching': 'Searching…',
  'pick.selectedOne': '{count} selected: {names}',
  'pick.selectedMany': '{count} selected: {names}',
  'field.emailExample': 'name@school.com',
  'reg.schoolCodePlaceholder': 'e.g. model-school',
  'reg.schoolCodeHint': 'The school gives you this code on first contact. It is the same code shown on the school registration.',
  'reg.parentLinkHint': 'After signing up, the school office must link you to your child. Until then the account exists but shows no student data.',
  'reg.currencyLabel': 'Currency:',
  'reg.dateLabel': 'Date:',
  'reg.schoolYearLabel': 'School year:',
  'struct.actions.gradeEntry': 'Enter grades',
  'struct.actions.newYear': 'New school year',
  'struct.active': 'Active',
  'struct.inactive': 'Inactive',
  'struct.yearLabel': 'School year {year}',
  'struct.assessmentWeight': 'weight {weight}',
  'struct.recoveryTag': 'Recovery',
  'struct.kpi.activeOf': 'Active: {active} of {total}',
  'struct.kpi.noActiveYear': 'No active school year',
  'struct.kpi.classesInActiveYears': 'In active school years: {count}',
  'struct.kpi.recovery': 'Recovery: {count} of {total}',
  'struct.form.yearActive': 'School year active',
  'struct.form.componentActive': 'Component active',
  'struct.form.isRecovery': 'This is a recovery assessment',
  'struct.dateRange': '{start} to {end}',
  'struct.deleteEntity.year': 'the school year',
  'struct.deleteEntity.class': 'the class',
  'struct.deleteEntity.component': 'the component',
  'struct.deleteEntity.assessment': 'the assessment',
  'struct.deletePrompt': 'Delete {entity}? Records with dependents are refused by the API.',
  'dash.greeting': 'Hello, {name}. School overview.',
  'dash.subtitle': 'School overview.',
  'dash.updatedAt': 'Updated at {time}',
  'dash.refresh': 'Refresh',
  'dash.partialWarning': 'Some indicators could not be loaded — the values below may be incomplete.',
  'dash.activeOfTotal': '{active} active of {total}',
  'dash.awaitingReview': '{count} awaiting review',
  'dash.expiringIn30': '{count} expire in 30 days',
  'dash.ofTotal': 'of {total} in total',
  'dash.progressPercent': '{percent}% of the period elapsed',
  'dash.lastDay': 'last day',
  'dash.daysLeftOne': '{days} day left',
  'dash.daysLeftMany': '{days} days left',
  'dash.viewEnrollments': 'View enrollments',
  'dash.closed': 'Closed',
  'dash.closesToday': 'Closes today',
  'priv.column.requester': 'Requester',
  'priv.column.type': 'Type',
  'priv.column.status': 'Status',
  'priv.column.student': 'Student',
  'priv.column.legalHold': 'Legal hold',
  'priv.column.retainUntil': 'Retain until',
  'priv.column.submittedAt': 'Submitted at',
  'priv.column.actions': 'Actions',
  'priv.yes': 'Yes',
  'priv.no': 'No',
  'priv.approve': 'Approve',
  'priv.reject': 'Reject',
  'priv.completing': 'Completing…',
  'priv.complete': 'Complete',
  'priv.approveTitle': 'Approve request',
  'priv.rejectTitle': 'Reject request',
  'priv.resolutionOptional': 'Opinion (optional)',
  'priv.reasonOptional': 'Rejection reason (optional)',
  'priv.legalHoldHint': 'Legal hold: blocks anonymization until released',
  'priv.requestExport': 'Request export',
  'priv.requestAnonymization': 'Request anonymization',
  'priv.granted': 'Granted',
  'priv.revoked': 'Revoked',
  'priv.sending': 'Sending…',
  'priv.downloading': 'Downloading…',
  'priv.downloadExport': 'Download export',
  'priv.legalBasis': 'Legal basis: {basis}',
  'priv.grantedAt': 'Granted on {date}',
  'priv.revokedAt': 'Revoked on {date}',
  'priv.openRequestHint': 'Requests of the same type stay disabled while one is open, until the school completes it.',
  'priv.consentsTitle': 'Consents',
  'priv.openRequests': 'Open requests: {count}',
  'settings.hint.plan': 'Changing the plan without touching the features applies the plan default set.',

  'common.cancel': 'Cancel',
  'common.confirm': 'Confirm',
  'splash.subtitle': 'Your School Management System',
  'splash.continue': 'Continue to sign in',
  'splash.prompt': 'Tap or press any key to continue',
  'enrollment.status.draft': 'Draft',
  'enrollment.status.pending': 'Pending',
  'enrollment.status.documentsRequired': 'Documents pending',
  'enrollment.status.approved': 'Approved',
  'enrollment.status.rejected': 'Rejected',
  'enrollment.status.cancelled': 'Cancelled',
  'enrollment.action.submit': 'Submit',
  'enrollment.action.approve': 'Approve',
  'enrollment.action.reject': 'Reject',
  'enrollment.action.cancel': 'Cancel',
  'enrollment.action.reopen': 'Reopen',
  'role.admin': 'Administration',
  'role.staff': 'Office',
  'role.parent': 'Guardian',
  'user.activate': 'Activate',
  'user.deactivate': 'Deactivate',
  'user.roleChanged': 'Role of {name} changed to {role}.',
  'field.name': 'Name',
  'field.email': 'Email',
  'field.role': 'Role',
  'users.subtitle': 'Who has access to the school and in which role.',
  'users.invite': 'Invite',
  'users.restricted': 'Access restricted to the school administrator.',
  'users.loadError': 'The users could not be loaded.',
  'users.inviteError': 'The user could not be invited.',
  'users.roleError': 'The role could not be changed.',
  'users.activeError': 'The user status could not be changed.',
  'users.empty': 'No users registered.',
  'users.col.status': 'Status',
  'users.you': '(you)',
  'users.active': 'Active',
  'users.inactive': 'Inactive',
  'users.emailNotConfirmed': 'Email not confirmed',
  'users.activated': '{name} was activated.',
  'users.deactivated': '{name} was deactivated.',
  'users.inviteTitle': 'Invite user',
  'users.inviteConfirm': 'Invite',
  'users.inviteHint': 'The account starts without a password: the user can only sign in after accepting the invite.',
  'users.inviteCreated': 'Invite created',
  'users.inviteCreatedBody': 'Send the instructions to {email}. The user sets the password through the recovery flow, using the token below.',
  'users.tokenCopied': 'Token copied.',
  'users.copyError': 'Could not copy — select the text manually.',
  'users.copyToken': 'Copy token',
  'users.tokenHidden': 'The token is not shown in this environment: it must be delivered to the guest by email.',
  'users.validate.name': 'Enter the name.',
  'users.validate.email': 'Enter a valid email.',
  'users.validate.role': 'Choose a valid role.',
  'common.back': 'Back',
  'common.status': 'Status',
  'common.remove': 'Remove',
  'students.loadError': 'The student could not be loaded.',
  'students.parentLinked': 'Guardian linked!',
  'students.parentLinkError': 'The guardian could not be linked.',
  'students.parentUnlinked': 'Guardian unlinked.',
  'students.parentUnlinkError': 'The guardian could not be unlinked.',
  'students.dataTitle': 'Student data',
  'students.field.class': 'Class',
  'students.field.schoolYear': 'School year',
  'students.field.notes': 'Notes',
  'students.parents': 'Guardians',
  'students.noParents': 'No guardian linked.',
  'students.parentEmailPlaceholder': 'Guardian email',
  'students.linkParent': 'Link',
  'students.detail.subtitle': '{class} · school year {year}',
  'notifications.subtitle': 'Notices sent and received at the school.',
  'notifications.new': 'New',
  'notifications.history': 'History',
  'notifications.countOne': '{count} record',
  'notifications.countMany': '{count} records',
  'notifications.empty': 'No notifications registered.',
  'notifications.loadError': 'The notifications could not be loaded.',
  'notifications.recent': 'Recent activity',
  'notifications.unreadOne': '{count} unread',
  'notifications.unreadMany': '{count} unread',
  'notifications.viewAll': 'View all',
  'notifications.recentEmpty': 'No notifications here.',
  'auth.brand.login': 'Your Education Platform',
  'auth.brand.register': 'School registration',
  'auth.brand.parent': 'Guardian account',
  'auth.chip.institutional': 'Institutional environment',
  'auth.chip.access': 'Institutional access',
  'auth.title.login': 'Sign in to your workspace',
  'auth.title.register': 'Register your school',
  'auth.title.parent': 'Create your guardian account',
  'auth.subtitle.login': 'Sign in with your credentials to follow students, enrollments and documents.',
  'auth.subtitle.register': 'Bring your school management to Ciclo in three steps.',
  'auth.subtitle.parent': 'Enter the school code and set up your access.',
  'auth.capability.enrollments.title': 'Enrollments and classes',
  'auth.capability.enrollments.text': 'Students, classes and enrollments in a single flow.',
  'auth.capability.documents.title': 'Documents and notices',
  'auth.capability.documents.text': 'Document and notice delivery with follow-up.',
  'auth.capability.roles.title': 'Access by role',
  'auth.capability.roles.text': 'Administration and guardians see only what belongs to them.',
  'auth.field.password': 'Password',
  'auth.forgot': 'Forgot your password?',
  'auth.showPassword': 'Show password',
  'auth.hidePassword': 'Hide password',
  'auth.remember': 'Remember me in this browser for 30 days',
  'auth.submit': 'Sign in to the platform',
  'auth.submitting': 'Signing in...',
  'auth.or': 'or continue with Google',
  'auth.google': 'Sign in with Google',
  'auth.demoNotice': 'Demo — fictional data stored in this browser, without a backend.',
  'auth.demoAdmin': 'Sign in as administration',
  'auth.demoParent': 'Sign in as guardian',
  'auth.googleFailed': 'Google authentication failed.',
  'auth.demoFailed': 'The demo could not be entered.',
  'auth.loginSuccess': 'Signed in!',
  'auth.offline': 'The server could not be reached. Check your connection.',
  'auth.badCredentials': 'Wrong email or password.',
  'auth.registerPrompt': 'Need to register your institution?',
  'auth.registerAction': 'Register my school',
  'auth.parentPrompt': 'Guardian of a student?',
  'auth.parentAction': 'Create a guardian account',
  'auth.backToLogin': 'Back to sign in',
  'auth.pedagogical.title': 'Pedagogical view',
  'auth.pedagogical.text': 'Close and organised school management.',
  'auth.copyright': '© 2026 Ciclo Education Platform',
  'auth.rights': 'All rights reserved',
  'parent.children': 'Children',
  'forgot.title.email': 'Forgot my password',
  'forgot.title.reset': 'New password',
  'forgot.subtitle.email': 'Enter the registered email to continue.',
  'forgot.subtitle.reset': 'Set your new password following the security pattern.',
  'forgot.emailLabel': 'Registered email',
  'forgot.continue': 'Continue',
  'forgot.verifying': 'Checking...',
  'forgot.newPassword': 'New password',
  'forgot.confirmPassword': 'Confirm new password',
  'forgot.requirements': 'Password requirements:',
  'forgot.req.length': 'At least 8 characters',
  'forgot.req.upper': 'At least 1 uppercase letter',
  'forgot.req.digit': 'At least 1 number',
  'forgot.req.symbol': 'At least 1 symbol (e.g. !@#$%)',
  'forgot.change': 'Change password',
  'forgot.changing': 'Changing password...',
  'forgot.sent': 'If the email is registered, you will receive recovery instructions.',
  'forgot.verified': 'Email verified! Now set your new password.',
  'forgot.requestFailed': 'The request could not be processed. Try again.',
  'forgot.changed': 'Password changed! Sign in with your new password.',
  'forgot.resetFailed': 'The password could not be reset.',
  'forgot.mismatch': 'The passwords do not match.',
  'password.min': 'The password must have at least 8 characters.',
  'password.upper': 'The password must have at least one uppercase letter.',
  'password.digit': 'The password must have at least one number.',
  'password.symbol': 'The password must have at least one symbol (e.g. !@#$%).',
  'parent.title': 'My Children',
  'parent.greeting': 'Hello, {name}. Follow your children school life.',
  'parent.loadFailed': 'The data could not be loaded.',
  'parent.childrenLinked': 'Linked children',
  'parent.activeEnrollments': 'Active enrollments',
  'parent.pendingDocs': 'Pending docs',
  'parent.unread': 'Unread',
  'parent.myChildren': 'My children',
  'parent.noChildren': 'No children linked.',
  'parent.pendingDocsBadge': '{count} pending doc(s)',
  'parent.performanceUnavailable': 'Performance unavailable.',
  'parent.noGrades': 'No grades recorded.',
  'parent.average': 'Average',
  'parent.subjectOne': '{count} subject',
  'parent.subjectMany': '{count} subjects',
  'students.subtitle': 'Registration and status of the school students.',
  'students.new': 'New student',
  'students.search': 'Search',
  'students.searchPlaceholder': 'Search by name',
  'students.filterClass': 'Filter by class',
  'students.classPlaceholder': 'Class',
  'students.filterStatus': 'Filter by status',
  'students.allStatus': 'All statuses',
  'students.clear': 'Clear',
  'students.loadListError': 'The students could not be loaded. Try again.',
  'students.removed': 'Student removed.',
  'students.removeError': 'The student could not be removed.',
  'students.col.student': 'Student',
  'students.edit': 'Edit',
  'students.removeTitle': 'Remove student',
  'students.removeBody': 'The student and the data linked to them will be removed. This action cannot be undone.',
  'students.empty': 'No students found.',
  'student.status.active': 'Active',
  'student.status.inactive': 'Inactive',
  'student.status.transferred': 'Transferred',
  'students.form.editTitle': 'Edit student',
  'students.form.editSubtitle': 'Change the data and save.',
  'students.form.newSubtitle': 'Fill in the data to register the student.',
  'students.field.photo': 'Student photo',
  'students.field.choosePhoto': 'Choose photo',
  'students.field.removePhoto': 'Remove photo',
  'students.field.photoHint': 'The image is resized to {size}px before upload.',
  'students.field.birthDate': 'Date of birth',
  'students.field.cpf': 'CPF',
  'students.photoTypeError': 'Select an image file.',
  'students.photoProcessError': 'The image could not be processed.',
  'students.updated': 'Student updated!',
  'students.created': 'Student created!',
  'students.saveError': 'The student could not be saved.',
  'common.save': 'Save',
  'common.saving': 'Saving...',
  'enrollments.loadError': 'The enrollment could not be loaded.',
  'enrollments.approved': 'Approved!',
  'enrollments.approveError': 'The enrollment could not be approved.',
  'enrollments.rejected': 'Rejected.',
  'enrollments.rejectError': 'The enrollment could not be rejected.',
  'enrollments.detailTitle': 'Enrollment details',
  'enrollments.info': 'Information',
  'enrollments.field.period': 'Period',
  'enrollments.field.reason': 'Reason',
  'enrollments.field.date': 'Date',
  'enrollments.field.approvedAt': 'Approved on',
  'enrollments.documents': 'Document status',
  'enrollments.missingDocs': 'Required documents pending',
  'enrollments.docsOk': 'Required documents up to date.',
  'enrollments.studentDocs': 'Student documents',
  'enrollments.expired': 'expired — must be re-sent',
  'enrollments.validUntil': 'valid until {date}',
  'enrollments.noExpiry': 'no expiry',
  'enrollments.noDocs': 'No document registered for this student.',
  'enrollments.actions': 'Actions',
  'enrollments.rejectTitle': 'Reject enrollment',
  'enrollments.rejectReason': 'Rejection reason',
  'docs.uploadError': 'Upload failed. Try again.',
  'docs.loadError': 'The documents could not be loaded.',
  'docs.loadTypesError': 'The document types could not be loaded.',
  'docs.selectType': 'Select the document type.',
  'docs.tooLarge': 'File too large (10MB maximum).',
  'docs.uploaded': 'Upload complete!',
  'docs.subtitle': 'Send and follow the student documents.',
  'docs.field.type': 'Document type',
  'docs.field.file': 'File',
  'docs.selectPlaceholder': 'Select...',
  'docs.requiredSuffix': ' (required)',
  'docs.sending': 'Sending...',
  'docs.upload': 'Upload',
  'docs.empty': 'No documents.',
  'docs.rejectReason': 'Rejection reason: {reason}',
  'inbox.subtitle': 'School notices for you.',
  'inbox.markAll': 'Mark all',
  'inbox.title': 'Inbox',
  'inbox.countOne': '{count} notice',
  'inbox.countMany': '{count} notices',
  'inbox.emptyUnread': 'No unread notification.',
  'inbox.markRead': 'Mark as read',
  'inbox.markReadError': 'It could not be marked as read.',
  'inbox.markAllError': 'They could not all be marked as read.',
  'inbox.markedAll': '{count} marked as read.',
  'academic.status.approved': 'Approved',
  'academic.status.recovery': 'In recovery',
  'academic.status.failed': 'Failed',
  'academic.status.noGrade': 'No grade',
  'academic.status.none': 'No status',
  'child.reportError': 'The report card could not be loaded.',
  'child.tab.grades': 'Grades',
  'child.data': 'Data',
  'child.field.year': 'Year',
  'child.enrollment': 'Current enrollment',
  'child.noEnrollment': 'No active enrollment.',
  'child.docsTitle': 'Documents ({count})',
  'child.uploadDoc': 'Upload document',
  'child.report': 'Report card',
  'child.noGrades': 'No grades available.',
  'child.col.component': 'Component',
  'child.col.regular': 'Regular average',
  'child.col.recovery': 'Recovery',
  'child.col.final': 'Final average',
  'child.kpi.components': 'Components',
  'child.kpi.ofTotal': '{count} of {total}',
  'notify.createTitle': 'New notification',
  'notify.createSubtitle': 'Choose the target, write and send.',
  'notify.compose': 'Compose message',
  'notify.field.title': 'Title',
  'notify.field.message': 'Message',
  'notify.field.type': 'Type',
  'notify.field.target': 'Target',
  'notify.mode.broadcast': 'Broadcast',
  'notify.mode.student': 'By student',
  'notify.broadcastNotice': 'The notice goes to every user of the school.',
  'notify.sendAll': 'Send to everyone',
  'notify.send': 'Send',
  'notify.missingRecipient': 'Choose at least one recipient to send.',
  'notify.sentBroadcast': 'Broadcast sent!',
  'notify.sentStudent': 'Notification sent!',
  'notify.created': 'Notification created!',
  'notify.createError': 'The notification could not be created.',
  'notify.kind.general': 'General',
  'notify.kind.docPending': 'Pending document',
  'notify.kind.meeting': 'Meeting',
  'notify.kind.other': 'Other',
  'renewal.title': 'Batch re-enrollment',
  'renewal.subtitle': 'Per-student preview, with reused documents before confirming.',
  'renewal.periodsError': 'The periods could not be loaded.',
  'renewal.previewError': 'The preview could not be loaded.',
  'renewal.noPeriods': 'No enrollment period registered. Create next year period before renewing.',
  'renewal.periodLabel': 'Target period',
  'renewal.selectPeriod': 'Select a period',
  'renewal.processing': 'Processing…',
  'renewal.renewCount': 'Renew {count} selected',
  'renewal.choosePeriod': 'Choose a period to see the preview.',
  'renewal.noCandidates': 'No student eligible for renewal in this period.',
  'renewal.summary': '{total} candidate(s) · {eligible} eligible. The preview writes nothing: only the confirmation renews.',
  'renewal.col.carried': 'Reused documents',
  'renewal.col.pending': 'Pending items',
  'renewal.selectStudent': 'Select {name}',
  'renewal.kpi.students': 'Students',
  'renewal.kpi.eligible': 'Eligible',
  'renewal.kpi.blocked': 'Blocked',
  'renewal.kpi.carried': 'Reused documents',
  'renewal.kpi.ofTotal': '{count} of {total}',
  'renewal.selectAll': 'Select every eligible student',
  'renewal.selectedCount': 'Selected: {count}',
  'renewal.reviewTitle': 'Students for re-enrollment',
  'renewal.notEligible': 'Not eligible for renewal.',
  'renewal.noDoc': 'No document',
  'renewal.expired': 'expired',
  'renewal.reused': 'reused',
  'renewal.none': 'None',
  'renewal.batchTitle': 'Batch outcome · school year {year}',
  'renewal.noSelection': 'Select at least one eligible student.',
  'renewal.batchDone': 'Batch processed. Check the outcome of each student.',
  'renewal.batchError': 'The batch could not run.',
  'renewal.outcome.renewed': 'Renewed',
  'renewal.outcome.skipped': 'Already existed',
  'renewal.outcome.blocked': 'Documents pending',
  'renewal.outcome.failed': 'Failed',
  'grades.title': 'Grade entry',
  'grades.subtitle': 'Choose the class and the assessment, then save each student grade.',
  'grades.classesError': 'The classes could not be loaded.',
  'grades.rosterError': 'The class could not be loaded.',
  'grades.noClasses': 'No class registered yet.',
  'grades.academicStructure': 'Academic structure',
  'grades.field.assessment': 'Assessment',
  'grades.noAssessment': 'No assessment in this class',
  'grades.weight': 'weight',
  'grades.recovery': 'recovery',
  'grades.noAssessments': 'This class has no assessments.',
  'grades.noStudents': 'No student in this class.',
  'grades.col.grade': 'Grade (0 to 10)',
  'grades.studentCount': 'Students: {count}',
  'grades.gradeAria': 'Grade for {name}',
  'grades.saved': 'Grade of {name} saved.',
  'grades.saveError': 'The grade of {name} could not be saved.',
  'grades.footnote': 'Grades entered before this session are not shown here — the API does not expose reading by assessment yet. Saving the same grade again updates the record.',
  'grades.error.required': 'Enter the grade.',
  'grades.error.invalid': 'Invalid grade — use numbers, such as 7.5.',
  'grades.error.range': 'The grade must be between {min} and {max}.',
  'reason.required': 'Enter the rejection reason.',
  'reason.tooShort': 'The reason must have at least {min} characters.',
  'reason.tooLong': 'The reason must have at most {max} characters.',
  'docs.approveTitle': 'Approve document',
  'docs.rejectTitle': 'Reject document',
  'docs.approved': 'Document approved.',
  'docs.rejected': 'Document rejected.',
  'docs.typesError': 'The types could not be loaded.',
  'docs.verifyError': 'The verification could not be completed.',
  'docs.downloadError': 'Failed to download the document.',
  'docs.col.file': 'File',
  'docs.sentAt': 'Sent on {date}',
  'docs.col.validity': 'Validity',
  'docs.downloadAria': 'Download {name}',
  'docs.downloadTitle': 'Download file',
  'docs.subtitle.all': 'Documents from every student, with filters.',
  'docs.subtitle.expiring': 'Approved documents expiring in the next 30 days.',
  'docs.view.all': 'All',
  'docs.view.expiring': 'Expiring',
  'docs.filter.studentPlaceholder': 'Search student by name',
  'docs.filter.allTypes': 'All types',
  'docs.filter.allStatus': 'All statuses',
  'docs.loadListError': 'The documents could not be loaded. Try again.',
  'docs.noneExpiring': 'No document expires in the next 30 days.',
  'docs.confirmApprove': 'Confirm the approval of this document?',
  'docs.view.label': 'Document view',
  'docs.kpi.pending': 'Pending',
  'docs.kpi.approved': 'Approved',
  'docs.kpi.rejected': 'Rejected',
  'docs.kpi.expiring': 'Expiring in 30 days',
  'docs.kpi.ofTotal': '{count} of {total}',
  'docs.expiresToday': 'expires today',
  'docs.expiresTomorrow': 'expires tomorrow',
  'docs.expiringIn': 'expires in {days} days',
  'struct.title': 'Academic structure',
  'struct.subtitle': 'School years, classes, curriculum components and assessments.',
  'struct.schoolYears': 'School years',
  'struct.classes': 'Classes',
  'struct.components': 'Curriculum components',
  'struct.assessments': 'Assessments',
  'struct.empty.schoolYears': 'No school year registered.',
  'struct.empty.classesNeedYear': 'Create a school year before creating classes.',
  'struct.empty.classes': 'No class registered.',
  'struct.empty.components': 'No curriculum component registered.',
  'struct.empty.assessmentsNeedClass': 'Create a class and a component before creating assessments.',
  'struct.empty.assessments': 'No assessment registered.',
  'struct.loadError': 'The academic structure could not be loaded.',
  'struct.saveError': 'The record could not be saved.',
  'struct.deleteError': 'The record could not be deleted.',
  'struct.saved': '{entity} record saved.',
  'struct.deleted': 'Record deleted.',
  'struct.edit': 'Edit',
  'struct.delete': 'Delete',
  'struct.form.title': '{action} {entity}',
  'struct.form.new': 'New',
  'struct.form.edit': 'Edit',
  'struct.entity.year': 'school year',
  'struct.entity.class': 'class',
  'struct.entity.component': 'curriculum component',
  'struct.entity.assessment': 'assessment',
  'struct.field.year': 'Year',
  'struct.field.start': 'Start',
  'struct.field.end': 'End',
  'struct.field.name': 'Name',
  'struct.field.schoolYear': 'School year',
  'struct.field.code': 'Code',
  'struct.field.class': 'Class',
  'struct.field.component': 'Component',
  'struct.field.title': 'Title',
  'struct.field.weight': 'Weight',
  'struct.field.date': 'Date',
  'struct.deleteTitle': 'Confirm deletion',
  'struct.add.year': 'New year',
  'struct.add.class': 'New class',
  'struct.add.component': 'New component',
  'struct.add.assessment': 'New assessment',
  'struct.error.yearDigits': 'Enter the year with four digits.',
  'struct.error.dates': 'Enter the start and end of the school year.',
  'struct.error.dateOrder': 'The end date cannot be earlier than the start date.',
  'struct.error.className': 'Enter the class name.',
  'struct.error.selectYear': 'Select the school year.',
  'struct.error.componentName': 'Enter the component name.',
  'struct.error.componentCode': 'Enter the component code.',
  'struct.error.assessmentTitle': 'Enter the assessment title.',
  'struct.error.selectClass': 'Select the class.',
  'struct.error.selectComponent': 'Select the component.',
  'struct.error.assessmentDate': 'Enter the assessment date.',
  'struct.error.weight': 'The weight must be greater than zero.',
  'dash.pendingDocs': 'Pending documents',
  'dash.enrollmentPeriod': 'Enrollment period',
  'dash.noOpenPeriod': 'No enrollment period open.',
  'enrollments.listSubtitle': 'Follow and approve enrollments by period.',
  'enrollments.allPeriods': 'All periods',
  'enrollments.loadListError': 'The enrollments could not be loaded. Try again.',
  'enrollments.rejectedToast': 'Enrollment rejected.',
  'enrollments.reopenTitle': 'Reopen enrollment',
  'priv.requestsSubtitle': 'Data subject requests, decisions and school execution.',
  'priv.emptyRequests': 'No request registered.',
  'priv.holdUntil': 'Hold until (optional)',
  'priv.approved': 'Request approved.',
  'priv.refused': 'Request refused.',
  'priv.decisionError': 'The decision could not be recorded.',
  'priv.completed': 'Request completed.',
  'priv.completeError': 'The request could not be completed.',
  'priv.centerSubtitle': 'Registered consents and requests about your children data.',
  'priv.noStudents': 'No student linked to your access.',
  'priv.noConsents': 'No consent registered.',
  'priv.sent': 'Request sent.',
  'priv.sendError': 'The request could not be sent.',
  'priv.downloadError': 'The document could not be downloaded.',
  'reg.step.school': 'School',
  'reg.step.data': 'Registration',
  'reg.step.admin': 'Administrator',
  'reg.passwordMismatch': 'The passwords do not match.',
  'reg.schoolCreated': 'School registered! Sign in with the administrator email.',
  'reg.emailTaken': 'This email is already registered.',
  'reg.countryUnsupported': 'Country not supported.',
  'reg.createFailed': 'The registration could not be completed. Check the data.',
  'reg.schoolName': 'School name',
  'reg.schoolNamePlaceholder': 'E.g.: Dom Bosco School',
  'reg.country': 'Country',
  'reg.selectCountry': 'Select a country',
  'reg.schoolData': 'School data',
  'reg.phone': 'Phone',
  'reg.address': 'Address',
  'reg.addressPlaceholder': 'Street, number, district',
  'reg.city': 'City',
  'reg.adminData': 'Account administrator',
  'reg.submitting': 'Registering...',
  'reg.submitSchool': 'Register school',
  'reg.parentCreated': 'Account created. The office must link your child before you can see the data.',
  'reg.parentCreateFailed': 'The account could not be created.',
  'reg.schoolCode': 'School code',
  'reg.yourName': 'Your name',
  'reg.password': 'Password',
  'reg.confirmPassword': 'Confirm password',
  'reg.creating': 'Creating account...',
  'reg.createAccount': 'Create account',
  'pick.usersLoadError': 'The users could not be loaded.',
  'pick.searchUsers': 'Search by name or email',
  'pick.noRecipient': 'No recipient selected.',
  'pick.studentsLoadError': 'The students could not be searched.',
  'pick.searchStudents': 'Search student by name',
  'country.br': 'Brazil',
  'country.us': 'United States',
  'country.pt': 'Portugal',

};

const ES: Record<MessageKey, string> = {
  'common.loading': 'Cargando…',
  'common.error': 'No fue posible cargar los datos.',
  'common.empty': 'Aún no hay nada aquí.',
  'common.retry': 'Intentar de nuevo',
  'common.reload': 'Recargar la página',
  'common.close': 'Cerrar',
  'errorBoundary.title': 'Algo salió mal.',
  'errorBoundary.body': 'La página encontró un error inesperado. Recargue para continuar.',
  'field.required': '{field} es obligatorio.',
  'list.emptyTitle': 'No se encontraron registros.',
  'language.label': 'Idioma',

  'nav.dashboard': 'Panel',
  'nav.login': 'Iniciar sesión',
  'nav.students': 'Alumnos',
  'nav.enrollments': 'Matrículas',
  'nav.documents': 'Documentos',
  'nav.academic': 'Académico',
  'nav.notifications': 'Notificaciones',
  'nav.users': 'Usuarios',
  'nav.settings': 'Configuración',
  'nav.privacy': 'Privacidad',
  'nav.logout': 'Salir',
  'nav.account': 'Mi cuenta',
  'profile.changePhoto': 'Cambiar foto',
  'profile.removePhoto': 'Quitar foto',
  'profile.photoSaved': 'Foto de perfil actualizada.',
  'profile.photoError': 'No fue posible actualizar la foto.',
  'nav.openMenu': 'Abrir menú',
  'nav.closeMenu': 'Cerrar menú',
  'nav.notificationsUnread': 'Notificaciones ({count} sin leer)',
  'nav.demo': 'demostración',
  'nav.demoTitle': 'Los datos de esta instalación son ficticios y permanecen en tu navegador.',

  'language.switchAria': 'Idioma de la interfaz',

  'settings.title': 'Configuración',
  'settings.subtitle': 'Identidad, preferencias regionales, límites y funcionalidades del colegio.',
  'settings.section.identity': 'Identidad del colegio',
  'settings.section.regional': 'Preferencias regionales',
  'settings.section.limits': 'Límites',
  'settings.section.plan': 'Plan y funcionalidades',
  'settings.features.legend': 'Funcionalidades',
  'settings.field.displayName': 'Nombre visible',
  'settings.field.timeZone': 'Zona horaria',
  'settings.field.schoolYear': 'Año lectivo por defecto',
  'settings.field.locale': 'Idioma predeterminado del colegio',
  'settings.field.storage': 'Cuota de almacenamiento (MB)',
  'settings.field.userQuota': 'Cuota de usuarios',
  'settings.field.plan': 'Plan',
  'settings.flag.documents': 'Documentos',
  'settings.flag.notifications': 'Notificaciones',
  'settings.flag.parentPortal': 'Portal del tutor',
  'settings.flag.academic': 'Rendimiento académico',
  'settings.flag.reenrollment': 'Rematrícula en lote',
  'settings.flag.audit': 'Exportación de auditoría',
  'settings.action.save': 'Guardar',
  'settings.action.saving': 'Guardando…',
  'settings.action.discard': 'Descartar cambios',
  'settings.state.unsaved': 'Cambios sin guardar',
  'settings.state.noChanges': 'No hay cambios para guardar.',
  'settings.state.saved': 'Configuración guardada.',
  'settings.error.load': 'No fue posible cargar la configuración del colegio.',
  'settings.error.save': 'No fue posible guardar la configuración.',
  'settings.error.numbers': 'Introduce números válidos en el año lectivo y las cuotas.',
  'settings.error.featuresRequired': 'Mantén al menos una funcionalidad habilitada.',
  'settings.hint.locale': 'Cambiar esto también cambia el idioma de la interfaz; guardar define el valor predeterminado del colegio.',
  'common.search': 'Buscar',
  'common.clear': 'Limpiar',
  'users.col.user': 'Usuario',
  'users.col.actions': 'Acciones',
  'users.searchPlaceholder': 'Buscar por nombre o correo',
  'users.allRoles': 'Todos los roles',
  'users.allStatuses': 'Todos los estados',
  'users.stats.total': 'Total',
  'users.stats.admins': 'Administradores',
  'users.stats.unconfirmed': 'Sin confirmación',
  'users.stats.inactive': 'Inactivos',
  'users.emptyFiltered': 'Ningún usuario coincide con los filtros.',
  'users.changeRoleTitle': 'Cambiar rol',
  'users.changeRoleHint': 'El nuevo rol se aplica de inmediato a este usuario.',
  'users.deactivateTitle': 'Desactivar usuario',
  'users.deactivateBody': '¿Desactivar a {name}? El acceso a la plataforma se bloquea de inmediato.',
  'users.deactivateConfirm': 'Desactivar',
  'enrollments.column.student': 'Alumno',
  'enrollments.column.status': 'Estado',
  'enrollments.batchRenewal': 'Renovación en lote',
  'enrollments.clear': 'Limpiar',
  'enrollments.reopenMessage': 'La matrícula vuelve a la cola de revisión. La decisión anterior se deshace y el registro guarda quién la reabrió y cuándo.',
  'priv.type.export': 'Exportación',
  'priv.type.anonymization': 'Anonimización',
  'priv.type.revocation': 'Revocación del consentimiento',
  'priv.status.submitted': 'Pendiente de decisión',
  'priv.status.approved': 'Aprobada',
  'priv.status.rejected': 'Rechazada',
  'priv.status.completed': 'Completada',
  'priv.loadError': 'No fue posible cargar las solicitudes.',
  'pagination.showing': 'Mostrando',
  'pagination.of': 'de',
  'pagination.previous': 'Anterior',
  'pagination.next': 'Siguiente',
  'pick.noUsers': 'Ningún usuario encontrado.',
  'pick.noStudents': 'Ningún alumno encontrado.',
  'pick.change': 'Cambiar',
  'pick.minChars': 'Escribe al menos 2 letras para buscar.',
  'pick.searching': 'Buscando…',
  'pick.selectedOne': '{count} seleccionado: {names}',
  'pick.selectedMany': '{count} seleccionados: {names}',
  'field.emailExample': 'nombre@colegio.com',
  'reg.schoolCodePlaceholder': 'p. ej. colegio-modelo',
  'reg.schoolCodeHint': 'El colegio te informa este código en el primer contacto. Es el mismo que aparece en el registro del colegio.',
  'reg.parentLinkHint': 'Después del registro, la secretaría del colegio debe vincularte a tu hijo. Hasta entonces la cuenta existe, pero no muestra datos de alumno.',
  'reg.currencyLabel': 'Moneda:',
  'reg.dateLabel': 'Fecha:',
  'reg.schoolYearLabel': 'Año lectivo:',
  'struct.actions.gradeEntry': 'Registrar notas',
  'struct.actions.newYear': 'Nuevo año lectivo',
  'struct.active': 'Activo',
  'struct.inactive': 'Inactivo',
  'struct.yearLabel': 'Año lectivo {year}',
  'struct.assessmentWeight': 'peso {weight}',
  'struct.recoveryTag': 'Recuperación',
  'struct.kpi.activeOf': 'Activos: {active} de {total}',
  'struct.kpi.noActiveYear': 'Ningún año lectivo activo',
  'struct.kpi.classesInActiveYears': 'En años lectivos activos: {count}',
  'struct.kpi.recovery': 'Recuperación: {count} de {total}',
  'struct.form.yearActive': 'Año lectivo activo',
  'struct.form.componentActive': 'Componente activo',
  'struct.form.isRecovery': 'Es una evaluación de recuperación',
  'struct.dateRange': '{start} a {end}',
  'struct.deleteEntity.year': 'el año lectivo',
  'struct.deleteEntity.class': 'la clase',
  'struct.deleteEntity.component': 'el componente',
  'struct.deleteEntity.assessment': 'la evaluación',
  'struct.deletePrompt': '¿Eliminar {entity}? La API rechaza registros con dependencias.',
  'dash.greeting': 'Hola, {name}. Resumen del colegio.',
  'dash.subtitle': 'Resumen del colegio.',
  'dash.updatedAt': 'Actualizado a las {time}',
  'dash.refresh': 'Actualizar',
  'dash.partialWarning': 'Algunos indicadores no pudieron cargarse — los valores de abajo pueden estar incompletos.',
  'dash.activeOfTotal': '{active} activos de {total}',
  'dash.awaitingReview': '{count} pendientes de revisión',
  'dash.expiringIn30': '{count} vencen en 30 días',
  'dash.ofTotal': 'de {total} en total',
  'dash.progressPercent': '{percent}% del período transcurrido',
  'dash.lastDay': 'último día',
  'dash.daysLeftOne': 'queda {days} día',
  'dash.daysLeftMany': 'quedan {days} días',
  'dash.viewEnrollments': 'Ver matrículas',
  'dash.closed': 'Cerrado',
  'dash.closesToday': 'Cierra hoy',
  'priv.column.requester': 'Solicitante',
  'priv.column.type': 'Tipo',
  'priv.column.status': 'Estado',
  'priv.column.student': 'Alumno',
  'priv.column.legalHold': 'Bloqueo legal',
  'priv.column.retainUntil': 'Retener hasta',
  'priv.column.submittedAt': 'Enviada el',
  'priv.column.actions': 'Acciones',
  'priv.yes': 'Sí',
  'priv.no': 'No',
  'priv.approve': 'Aprobar',
  'priv.reject': 'Rechazar',
  'priv.completing': 'Concluyendo…',
  'priv.complete': 'Concluir',
  'priv.approveTitle': 'Aprobar solicitud',
  'priv.rejectTitle': 'Rechazar solicitud',
  'priv.resolutionOptional': 'Dictamen (opcional)',
  'priv.reasonOptional': 'Motivo del rechazo (opcional)',
  'priv.legalHoldHint': 'Bloqueo legal: impide la anonimización hasta la liberación',
  'priv.requestExport': 'Solicitar exportación',
  'priv.requestAnonymization': 'Solicitar anonimización',
  'priv.granted': 'Concedido',
  'priv.revoked': 'Revocado',
  'priv.sending': 'Enviando…',
  'priv.downloading': 'Descargando…',
  'priv.downloadExport': 'Descargar exportación',
  'priv.legalBasis': 'Base legal: {basis}',
  'priv.grantedAt': 'Concedido el {date}',
  'priv.revokedAt': 'Revocado el {date}',
  'priv.openRequestHint': 'Las solicitudes del mismo tipo quedan deshabilitadas hasta que el colegio las complete.',
  'priv.consentsTitle': 'Consentimientos',
  'priv.openRequests': 'Solicitudes abiertas: {count}',
  'settings.hint.plan': 'Cambiar el plan sin tocar las funcionalidades aplica el conjunto predeterminado del plan.',

  'common.cancel': 'Cancelar',
  'common.confirm': 'Confirmar',
  'splash.subtitle': 'Tu Sistema de Gestión Escolar',
  'splash.continue': 'Continuar al inicio de sesión',
  'splash.prompt': 'Toca o pulsa cualquier tecla para continuar',
  'enrollment.status.draft': 'Borrador',
  'enrollment.status.pending': 'Pendiente',
  'enrollment.status.documentsRequired': 'Documentación pendiente',
  'enrollment.status.approved': 'Aprobado',
  'enrollment.status.rejected': 'Rechazado',
  'enrollment.status.cancelled': 'Cancelado',
  'enrollment.action.submit': 'Enviar',
  'enrollment.action.approve': 'Aprobar',
  'enrollment.action.reject': 'Rechazar',
  'enrollment.action.cancel': 'Cancelar',
  'enrollment.action.reopen': 'Reabrir',
  'role.admin': 'Administración',
  'role.staff': 'Secretaría',
  'role.parent': 'Tutor',
  'user.activate': 'Activar',
  'user.deactivate': 'Desactivar',
  'user.roleChanged': 'Rol de {name} cambiado a {role}.',
  'field.name': 'Nombre',
  'field.email': 'Correo',
  'field.role': 'Rol',
  'users.subtitle': 'Quién tiene acceso al colegio y con qué rol.',
  'users.invite': 'Invitar',
  'users.restricted': 'Acceso restringido al administrador del colegio.',
  'users.loadError': 'No fue posible cargar los usuarios.',
  'users.inviteError': 'No fue posible invitar al usuario.',
  'users.roleError': 'No fue posible cambiar el rol.',
  'users.activeError': 'No fue posible cambiar la situación del usuario.',
  'users.empty': 'Ningún usuario registrado.',
  'users.col.status': 'Situación',
  'users.you': '(tú)',
  'users.active': 'Activo',
  'users.inactive': 'Inactivo',
  'users.emailNotConfirmed': 'Correo no confirmado',
  'users.activated': '{name} fue activado.',
  'users.deactivated': '{name} fue desactivado.',
  'users.inviteTitle': 'Invitar usuario',
  'users.inviteConfirm': 'Invitar',
  'users.inviteHint': 'La cuenta nace sin contraseña: el usuario solo entra después de aceptar la invitación.',
  'users.inviteCreated': 'Invitación creada',
  'users.inviteCreatedBody': 'Envía las instrucciones a {email}. El usuario define la contraseña por el flujo de recuperación, usando el token de abajo.',
  'users.tokenCopied': 'Token copiado.',
  'users.copyError': 'No fue posible copiar — selecciona el texto manualmente.',
  'users.copyToken': 'Copiar token',
  'users.tokenHidden': 'El token no se muestra en este entorno: debe entregarse por correo al invitado.',
  'users.validate.name': 'Indica el nombre.',
  'users.validate.email': 'Indica un correo válido.',
  'users.validate.role': 'Elige un rol válido.',
  'common.back': 'Volver',
  'common.status': 'Estado',
  'common.remove': 'Quitar',
  'students.loadError': 'No fue posible cargar al alumno.',
  'students.parentLinked': '¡Tutor vinculado!',
  'students.parentLinkError': 'No fue posible vincular al tutor.',
  'students.parentUnlinked': 'Tutor desvinculado.',
  'students.parentUnlinkError': 'No fue posible desvincular al tutor.',
  'students.dataTitle': 'Datos del alumno',
  'students.field.class': 'Clase',
  'students.field.schoolYear': 'Año lectivo',
  'students.field.notes': 'Observaciones',
  'students.parents': 'Tutores',
  'students.noParents': 'Ningún tutor vinculado.',
  'students.parentEmailPlaceholder': 'Correo del tutor',
  'students.linkParent': 'Vincular',
  'students.detail.subtitle': '{class} · año lectivo {year}',
  'notifications.subtitle': 'Avisos enviados y recibidos en el colegio.',
  'notifications.new': 'Nueva',
  'notifications.history': 'Historial',
  'notifications.countOne': '{count} registro',
  'notifications.countMany': '{count} registros',
  'notifications.empty': 'Ninguna notificación registrada.',
  'notifications.loadError': 'No fue posible cargar las notificaciones.',
  'notifications.recent': 'Actividad reciente',
  'notifications.unreadOne': '{count} sin leer',
  'notifications.unreadMany': '{count} sin leer',
  'notifications.viewAll': 'Ver todas',
  'notifications.recentEmpty': 'Ninguna notificación por aquí.',
  'auth.brand.login': 'Tu Plataforma Educativa',
  'auth.brand.register': 'Registro de colegio',
  'auth.brand.parent': 'Cuenta de tutor',
  'auth.chip.institutional': 'Entorno institucional',
  'auth.chip.access': 'Acceso institucional',
  'auth.title.login': 'Accede a tu espacio de trabajo',
  'auth.title.register': 'Registra tu colegio',
  'auth.title.parent': 'Crea tu cuenta de tutor',
  'auth.subtitle.login': 'Entra con tus credenciales para seguir alumnos, matrículas y documentos.',
  'auth.subtitle.register': 'Lleva la gestión escolar de tu colegio a Ciclo en tres pasos.',
  'auth.subtitle.parent': 'Indica el código del colegio y define tu acceso.',
  'auth.capability.enrollments.title': 'Matrículas y clases',
  'auth.capability.enrollments.text': 'Alumnos, clases y matrículas organizados en un solo flujo.',
  'auth.capability.documents.title': 'Documentos y avisos',
  'auth.capability.documents.text': 'Envío de documentos y comunicados con seguimiento.',
  'auth.capability.roles.title': 'Acceso por rol',
  'auth.capability.roles.text': 'Administración y tutores ven solo lo que les corresponde.',
  'auth.field.password': 'Contraseña',
  'auth.forgot': '¿Olvidaste la contraseña?',
  'auth.showPassword': 'Mostrar contraseña',
  'auth.hidePassword': 'Ocultar contraseña',
  'auth.remember': 'Recordarme en este navegador por 30 días',
  'auth.submit': 'Entrar en la plataforma',
  'auth.submitting': 'Entrando...',
  'auth.or': 'o acceso con Google',
  'auth.google': 'Entrar con Google',
  'auth.demoNotice': 'Demostración — datos ficticios guardados en este navegador, sin backend.',
  'auth.demoAdmin': 'Entrar como administración',
  'auth.demoParent': 'Entrar como tutor',
  'auth.googleFailed': 'Falló la autenticación con Google.',
  'auth.demoFailed': 'No fue posible entrar en la demostración.',
  'auth.loginSuccess': '¡Sesión iniciada!',
  'auth.offline': 'No fue posible conectar con el servidor. Revisa tu conexión.',
  'auth.badCredentials': 'Correo o contraseña incorrectos.',
  'auth.registerPrompt': '¿Necesitas registrar tu institución?',
  'auth.registerAction': 'Registrar mi colegio',
  'auth.parentPrompt': '¿Eres tutor de un alumno?',
  'auth.parentAction': 'Crear cuenta de tutor',
  'auth.backToLogin': 'Volver al inicio de sesión',
  'auth.pedagogical.title': 'Visión pedagógica',
  'auth.pedagogical.text': 'Gestión escolar cercana y organizada.',
  'auth.copyright': '© 2026 Ciclo Plataforma Educativa',
  'auth.rights': 'Todos los derechos reservados',
  'parent.children': 'Hijos',
  'forgot.title.email': 'Olvidé mi contraseña',
  'forgot.title.reset': 'Nueva contraseña',
  'forgot.subtitle.email': 'Indica el correo registrado para continuar.',
  'forgot.subtitle.reset': 'Define tu nueva contraseña siguiendo el patrón de seguridad.',
  'forgot.emailLabel': 'Correo registrado',
  'forgot.continue': 'Continuar',
  'forgot.verifying': 'Verificando...',
  'forgot.newPassword': 'Nueva contraseña',
  'forgot.confirmPassword': 'Confirmar nueva contraseña',
  'forgot.requirements': 'Requisitos de la contraseña:',
  'forgot.req.length': 'Mínimo de 8 caracteres',
  'forgot.req.upper': 'Al menos 1 letra mayúscula',
  'forgot.req.digit': 'Al menos 1 número',
  'forgot.req.symbol': 'Al menos 1 símbolo (ej.: !@#$%)',
  'forgot.change': 'Cambiar contraseña',
  'forgot.changing': 'Cambiando contraseña...',
  'forgot.sent': 'Si el correo está registrado, recibirás las instrucciones de recuperación.',
  'forgot.verified': '¡Correo verificado! Ahora define tu nueva contraseña.',
  'forgot.requestFailed': 'No fue posible procesar la solicitud. Inténtalo de nuevo.',
  'forgot.changed': '¡Contraseña cambiada! Inicia sesión con tu nueva contraseña.',
  'forgot.resetFailed': 'No fue posible restablecer la contraseña.',
  'forgot.mismatch': 'Las contraseñas no coinciden.',
  'password.min': 'La contraseña debe tener al menos 8 caracteres.',
  'password.upper': 'La contraseña debe tener al menos una letra mayúscula.',
  'password.digit': 'La contraseña debe tener al menos un número.',
  'password.symbol': 'La contraseña debe tener al menos un símbolo (ej.: !@#$%).',
  'parent.title': 'Mis Hijos',
  'parent.greeting': 'Hola, {name}. Sigue la vida escolar de tus hijos.',
  'parent.loadFailed': 'No fue posible cargar los datos.',
  'parent.childrenLinked': 'Hijos vinculados',
  'parent.activeEnrollments': 'Matrículas activas',
  'parent.pendingDocs': 'Docs pendientes',
  'parent.unread': 'Sin leer',
  'parent.myChildren': 'Mis hijos',
  'parent.noChildren': 'Ningún hijo vinculado.',
  'parent.pendingDocsBadge': '{count} doc(s) pendiente(s)',
  'parent.performanceUnavailable': 'Rendimiento no disponible.',
  'parent.noGrades': 'Sin notas registradas.',
  'parent.average': 'Promedio',
  'parent.subjectOne': '{count} asignatura',
  'parent.subjectMany': '{count} asignaturas',
  'students.subtitle': 'Registro y situación de los alumnos del colegio.',
  'students.new': 'Nuevo alumno',
  'students.search': 'Buscar',
  'students.searchPlaceholder': 'Buscar por nombre',
  'students.filterClass': 'Filtrar por clase',
  'students.classPlaceholder': 'Clase',
  'students.filterStatus': 'Filtrar por estado',
  'students.allStatus': 'Todos los estados',
  'students.clear': 'Limpiar',
  'students.loadListError': 'No fue posible cargar los alumnos. Inténtalo de nuevo.',
  'students.removed': 'Alumno eliminado.',
  'students.removeError': 'No fue posible eliminar al alumno.',
  'students.col.student': 'Alumno',
  'students.edit': 'Editar',
  'students.removeTitle': 'Eliminar alumno',
  'students.removeBody': 'El alumno y los datos vinculados a él serán eliminados. Esta acción no se puede deshacer.',
  'students.empty': 'Ningún alumno encontrado.',
  'student.status.active': 'Activo',
  'student.status.inactive': 'Inactivo',
  'student.status.transferred': 'Transferido',
  'students.form.editTitle': 'Editar alumno',
  'students.form.editSubtitle': 'Cambia los datos y guarda.',
  'students.form.newSubtitle': 'Completa los datos para registrar al alumno.',
  'students.field.photo': 'Foto del alumno',
  'students.field.choosePhoto': 'Elegir foto',
  'students.field.removePhoto': 'Quitar foto',
  'students.field.photoHint': 'La imagen se redimensiona a {size}px antes del envío.',
  'students.field.birthDate': 'Fecha de nacimiento',
  'students.field.cpf': 'CPF',
  'students.photoTypeError': 'Selecciona un archivo de imagen.',
  'students.photoProcessError': 'No fue posible procesar la imagen.',
  'students.updated': '¡Alumno actualizado!',
  'students.created': '¡Alumno creado!',
  'students.saveError': 'No fue posible guardar al alumno.',
  'common.save': 'Guardar',
  'common.saving': 'Guardando...',
  'enrollments.loadError': 'No fue posible cargar la matrícula.',
  'enrollments.approved': '¡Aprobada!',
  'enrollments.approveError': 'No fue posible aprobar la matrícula.',
  'enrollments.rejected': 'Rechazada.',
  'enrollments.rejectError': 'No fue posible rechazar la matrícula.',
  'enrollments.detailTitle': 'Detalles de la matrícula',
  'enrollments.info': 'Información',
  'enrollments.field.period': 'Período',
  'enrollments.field.reason': 'Motivo',
  'enrollments.field.date': 'Fecha',
  'enrollments.field.approvedAt': 'Aprobada el',
  'enrollments.documents': 'Situación documental',
  'enrollments.missingDocs': 'Documentos obligatorios pendientes',
  'enrollments.docsOk': 'Documentación obligatoria al día.',
  'enrollments.studentDocs': 'Documentos del alumno',
  'enrollments.expired': 'vencido — hay que reenviar',
  'enrollments.validUntil': 'válido hasta {date}',
  'enrollments.noExpiry': 'sin caducidad',
  'enrollments.noDocs': 'Ningún documento registrado para este alumno.',
  'enrollments.actions': 'Acciones',
  'enrollments.rejectTitle': 'Rechazar matrícula',
  'enrollments.rejectReason': 'Motivo del rechazo',
  'docs.uploadError': 'Error en la subida. Inténtalo de nuevo.',
  'docs.loadError': 'No fue posible cargar los documentos.',
  'docs.loadTypesError': 'No fue posible cargar los tipos de documento.',
  'docs.selectType': 'Selecciona el tipo de documento.',
  'docs.tooLarge': 'Archivo demasiado grande (máximo 10MB).',
  'docs.uploaded': '¡Subida completada!',
  'docs.subtitle': 'Envía y sigue los documentos del alumno.',
  'docs.field.type': 'Tipo de documento',
  'docs.field.file': 'Archivo',
  'docs.selectPlaceholder': 'Selecciona...',
  'docs.requiredSuffix': ' (obligatorio)',
  'docs.sending': 'Enviando...',
  'docs.upload': 'Subir',
  'docs.empty': 'Ningún documento.',
  'docs.rejectReason': 'Motivo del rechazo: {reason}',
  'inbox.subtitle': 'Avisos del colegio para ti.',
  'inbox.markAll': 'Marcar todas',
  'inbox.title': 'Bandeja de entrada',
  'inbox.countOne': '{count} aviso',
  'inbox.countMany': '{count} avisos',
  'inbox.emptyUnread': 'Ninguna notificación sin leer.',
  'inbox.markRead': 'Marcar como leída',
  'inbox.markReadError': 'No fue posible marcarla como leída.',
  'inbox.markAllError': 'No fue posible marcar todas como leídas.',
  'inbox.markedAll': '{count} marcadas como leídas.',
  'academic.status.approved': 'Aprobado',
  'academic.status.recovery': 'En recuperación',
  'academic.status.failed': 'Reprobado',
  'academic.status.noGrade': 'Sin nota',
  'academic.status.none': 'Sin situación',
  'child.reportError': 'No fue posible cargar el boletín.',
  'child.tab.grades': 'Notas',
  'child.data': 'Datos',
  'child.field.year': 'Año',
  'child.enrollment': 'Matrícula actual',
  'child.noEnrollment': 'Ninguna matrícula activa.',
  'child.docsTitle': 'Documentos ({count})',
  'child.uploadDoc': 'Subir documento',
  'child.report': 'Boletín',
  'child.noGrades': 'Ninguna nota disponible.',
  'child.col.component': 'Componente',
  'child.col.regular': 'Promedio regular',
  'child.col.recovery': 'Recuperación',
  'child.col.final': 'Promedio final',
  'child.kpi.components': 'Componentes',
  'child.kpi.ofTotal': '{count} de {total}',
  'notify.createTitle': 'Nueva notificación',
  'notify.createSubtitle': 'Elige el destino, escribe y envía.',
  'notify.compose': 'Componer mensaje',
  'notify.field.title': 'Título',
  'notify.field.message': 'Mensaje',
  'notify.field.type': 'Tipo',
  'notify.field.target': 'Destino',
  'notify.mode.broadcast': 'Broadcast',
  'notify.mode.student': 'Por alumno',
  'notify.broadcastNotice': 'El aviso va a todos los usuarios del colegio.',
  'notify.sendAll': 'Enviar a todos',
  'notify.send': 'Enviar',
  'notify.missingRecipient': 'Elige al menos un destinatario para enviar.',
  'notify.sentBroadcast': '¡Broadcast enviado!',
  'notify.sentStudent': '¡Notificación enviada!',
  'notify.created': '¡Notificación creada!',
  'notify.createError': 'No fue posible crear la notificación.',
  'notify.kind.general': 'General',
  'notify.kind.docPending': 'Documento pendiente',
  'notify.kind.meeting': 'Reunión',
  'notify.kind.other': 'Otro',
  'renewal.title': 'Rematrícula en lote',
  'renewal.subtitle': 'Vista previa por alumno, con los documentos reutilizados antes de confirmar.',
  'renewal.periodsError': 'No fue posible cargar los períodos.',
  'renewal.previewError': 'No fue posible cargar la vista previa.',
  'renewal.noPeriods': 'Ningún período de matrícula registrado. Crea el período del próximo año antes de renovar.',
  'renewal.periodLabel': 'Período de destino',
  'renewal.selectPeriod': 'Selecciona un período',
  'renewal.processing': 'Procesando…',
  'renewal.renewCount': 'Renovar {count} seleccionado(s)',
  'renewal.choosePeriod': 'Elige un período para ver la vista previa.',
  'renewal.noCandidates': 'Ningún alumno candidato a la renovación en este período.',
  'renewal.summary': '{total} alumno(s) candidato(s) · {eligible} elegible(s). La vista previa no guarda nada: solo la confirmación renueva.',
  'renewal.col.carried': 'Documentos reutilizados',
  'renewal.col.pending': 'Pendientes',
  'renewal.selectStudent': 'Seleccionar {name}',
  'renewal.kpi.students': 'Alumnos',
  'renewal.kpi.eligible': 'Elegibles',
  'renewal.kpi.blocked': 'Bloqueados',
  'renewal.kpi.carried': 'Documentos reutilizados',
  'renewal.kpi.ofTotal': '{count} de {total}',
  'renewal.selectAll': 'Seleccionar todos los elegibles',
  'renewal.selectedCount': 'Seleccionados: {count}',
  'renewal.reviewTitle': 'Alumnos para rematrícula',
  'renewal.notEligible': 'No elegible para renovación.',
  'renewal.noDoc': 'Ningún documento',
  'renewal.expired': 'vencido',
  'renewal.reused': 'reutilizado',
  'renewal.none': 'Ninguna',
  'renewal.batchTitle': 'Desenlace del lote · año lectivo {year}',
  'renewal.noSelection': 'Selecciona al menos un alumno elegible.',
  'renewal.batchDone': 'Lote procesado. Revisa el desenlace de cada alumno.',
  'renewal.batchError': 'No fue posible ejecutar el lote.',
  'renewal.outcome.renewed': 'Renovado',
  'renewal.outcome.skipped': 'Ya existía',
  'renewal.outcome.blocked': 'Documentación pendiente',
  'renewal.outcome.failed': 'Falló',
  'grades.title': 'Registro de notas',
  'grades.subtitle': 'Elige la clase y la evaluación, luego guarda la nota de cada alumno.',
  'grades.classesError': 'No fue posible cargar las clases.',
  'grades.rosterError': 'No fue posible cargar la clase.',
  'grades.noClasses': 'Ninguna clase registrada aún.',
  'grades.academicStructure': 'Estructura académica',
  'grades.field.assessment': 'Evaluación',
  'grades.noAssessment': 'Ninguna evaluación en esta clase',
  'grades.weight': 'peso',
  'grades.recovery': 'recuperación',
  'grades.noAssessments': 'Esta clase no tiene evaluaciones.',
  'grades.noStudents': 'Ningún alumno en esta clase.',
  'grades.col.grade': 'Nota (0 a 10)',
  'grades.studentCount': 'Alumnos: {count}',
  'grades.gradeAria': 'Nota de {name}',
  'grades.saved': 'Nota de {name} guardada.',
  'grades.saveError': 'No fue posible guardar la nota de {name}.',
  'grades.footnote': 'Las notas registradas antes de esta sesión no aparecen aquí — la API aún no expone lectura por evaluación. Guardar de nuevo la misma nota actualiza el registro.',
  'grades.error.required': 'Indica la nota.',
  'grades.error.invalid': 'Nota inválida — usa números, como 7,5.',
  'grades.error.range': 'La nota debe estar entre {min} y {max}.',
  'reason.required': 'Indica el motivo del rechazo.',
  'reason.tooShort': 'El motivo debe tener al menos {min} caracteres.',
  'reason.tooLong': 'El motivo debe tener como máximo {max} caracteres.',
  'docs.approveTitle': 'Aprobar documento',
  'docs.rejectTitle': 'Rechazar documento',
  'docs.approved': 'Documento aprobado.',
  'docs.rejected': 'Documento rechazado.',
  'docs.typesError': 'No fue posible cargar los tipos.',
  'docs.verifyError': 'No fue posible concluir la verificación.',
  'docs.downloadError': 'Error al descargar el documento.',
  'docs.col.file': 'Archivo',
  'docs.sentAt': 'Enviado el {date}',
  'docs.col.validity': 'Caducidad',
  'docs.downloadAria': 'Descargar {name}',
  'docs.downloadTitle': 'Descargar archivo',
  'docs.subtitle.all': 'Documentos de todos los alumnos, con filtros.',
  'docs.subtitle.expiring': 'Documentos aprobados que caducan en los próximos 30 días.',
  'docs.view.all': 'Todos',
  'docs.view.expiring': 'Por caducar',
  'docs.filter.studentPlaceholder': 'Buscar alumno por nombre',
  'docs.filter.allTypes': 'Todos los tipos',
  'docs.filter.allStatus': 'Todos los estados',
  'docs.loadListError': 'No fue posible cargar los documentos. Inténtalo de nuevo.',
  'docs.noneExpiring': 'Ningún documento caduca en los próximos 30 días.',
  'docs.confirmApprove': '¿Confirmar la aprobación de este documento?',
  'docs.view.label': 'Vista de documentos',
  'docs.kpi.pending': 'Pendientes',
  'docs.kpi.approved': 'Aprobados',
  'docs.kpi.rejected': 'Rechazados',
  'docs.kpi.expiring': 'Por caducar en 30 días',
  'docs.kpi.ofTotal': '{count} de {total}',
  'docs.expiresToday': 'caduca hoy',
  'docs.expiresTomorrow': 'caduca mañana',
  'docs.expiringIn': 'caduca en {days} días',
  'struct.title': 'Estructura académica',
  'struct.subtitle': 'Años lectivos, clases, componentes curriculares y evaluaciones.',
  'struct.schoolYears': 'Años lectivos',
  'struct.classes': 'Clases',
  'struct.components': 'Componentes curriculares',
  'struct.assessments': 'Evaluaciones',
  'struct.empty.schoolYears': 'Ningún año lectivo registrado.',
  'struct.empty.classesNeedYear': 'Registra un año lectivo antes de crear clases.',
  'struct.empty.classes': 'Ninguna clase registrada.',
  'struct.empty.components': 'Ningún componente curricular registrado.',
  'struct.empty.assessmentsNeedClass': 'Registra una clase y un componente antes de crear evaluaciones.',
  'struct.empty.assessments': 'Ninguna evaluación registrada.',
  'struct.loadError': 'No fue posible cargar la estructura académica.',
  'struct.saveError': 'No fue posible guardar el registro.',
  'struct.deleteError': 'No fue posible eliminar el registro.',
  'struct.saved': 'Registro de {entity} guardado.',
  'struct.deleted': 'Registro eliminado.',
  'struct.edit': 'Editar',
  'struct.delete': 'Eliminar',
  'struct.form.title': '{action} {entity}',
  'struct.form.new': 'Nuevo',
  'struct.form.edit': 'Editar',
  'struct.entity.year': 'año lectivo',
  'struct.entity.class': 'clase',
  'struct.entity.component': 'componente curricular',
  'struct.entity.assessment': 'evaluación',
  'struct.field.year': 'Año',
  'struct.field.start': 'Inicio',
  'struct.field.end': 'Fin',
  'struct.field.name': 'Nombre',
  'struct.field.schoolYear': 'Año lectivo',
  'struct.field.code': 'Código',
  'struct.field.class': 'Clase',
  'struct.field.component': 'Componente',
  'struct.field.title': 'Título',
  'struct.field.weight': 'Peso',
  'struct.field.date': 'Fecha',
  'struct.deleteTitle': 'Confirmar eliminación',
  'struct.add.year': 'Nuevo año',
  'struct.add.class': 'Nueva clase',
  'struct.add.component': 'Nuevo componente',
  'struct.add.assessment': 'Nueva evaluación',
  'struct.error.yearDigits': 'Indica el año con cuatro dígitos.',
  'struct.error.dates': 'Indica el inicio y el fin del año lectivo.',
  'struct.error.dateOrder': 'La fecha de fin no puede ser anterior a la de inicio.',
  'struct.error.className': 'Indica el nombre de la clase.',
  'struct.error.selectYear': 'Selecciona el año lectivo.',
  'struct.error.componentName': 'Indica el nombre del componente.',
  'struct.error.componentCode': 'Indica el código del componente.',
  'struct.error.assessmentTitle': 'Indica el título de la evaluación.',
  'struct.error.selectClass': 'Selecciona la clase.',
  'struct.error.selectComponent': 'Selecciona el componente.',
  'struct.error.assessmentDate': 'Indica la fecha de la evaluación.',
  'struct.error.weight': 'El peso debe ser mayor que cero.',
  'dash.pendingDocs': 'Documentos pendientes',
  'dash.enrollmentPeriod': 'Período de matrícula',
  'dash.noOpenPeriod': 'Ningún período de matrícula abierto.',
  'enrollments.listSubtitle': 'Sigue y aprueba las matrículas por período.',
  'enrollments.allPeriods': 'Todos los períodos',
  'enrollments.loadListError': 'No fue posible cargar las matrículas. Inténtalo de nuevo.',
  'enrollments.rejectedToast': 'Matrícula rechazada.',
  'enrollments.reopenTitle': 'Reabrir matrícula',
  'priv.requestsSubtitle': 'Solicitudes de titulares, decisiones y ejecución del colegio.',
  'priv.emptyRequests': 'Ninguna solicitud registrada.',
  'priv.holdUntil': 'Retener hasta (opcional)',
  'priv.approved': 'Solicitud aprobada.',
  'priv.refused': 'Solicitud rechazada.',
  'priv.decisionError': 'No fue posible registrar la decisión.',
  'priv.completed': 'Solicitud completada.',
  'priv.completeError': 'No fue posible completar la solicitud.',
  'priv.centerSubtitle': 'Consentimientos registrados y solicitudes sobre los datos de tus hijos.',
  'priv.noStudents': 'Ningún alumno vinculado a tu acceso.',
  'priv.noConsents': 'Ningún consentimiento registrado.',
  'priv.sent': 'Solicitud enviada.',
  'priv.sendError': 'No fue posible enviar la solicitud.',
  'priv.downloadError': 'No fue posible descargar el documento.',
  'reg.step.school': 'Colegio',
  'reg.step.data': 'Registro',
  'reg.step.admin': 'Administrador',
  'reg.passwordMismatch': 'Las contraseñas no coinciden.',
  'reg.schoolCreated': '¡Colegio registrado! Inicia sesión con el correo del administrador.',
  'reg.emailTaken': 'Este correo ya está registrado.',
  'reg.countryUnsupported': 'País no soportado.',
  'reg.createFailed': 'No fue posible completar el registro. Revisa los datos.',
  'reg.schoolName': 'Nombre del colegio',
  'reg.schoolNamePlaceholder': 'Ej.: Colegio Dom Bosco',
  'reg.country': 'País',
  'reg.selectCountry': 'Selecciona un país',
  'reg.schoolData': 'Datos del colegio',
  'reg.phone': 'Teléfono',
  'reg.address': 'Dirección',
  'reg.addressPlaceholder': 'Calle, número, barrio',
  'reg.city': 'Ciudad',
  'reg.adminData': 'Administrador de la cuenta',
  'reg.submitting': 'Registrando...',
  'reg.submitSchool': 'Registrar colegio',
  'reg.parentCreated': 'Cuenta creada. La secretaría debe vincular a tu hijo para que veas los datos.',
  'reg.parentCreateFailed': 'No fue posible crear la cuenta.',
  'reg.schoolCode': 'Código del colegio',
  'reg.yourName': 'Tu nombre',
  'reg.password': 'Contraseña',
  'reg.confirmPassword': 'Confirmar contraseña',
  'reg.creating': 'Creando cuenta...',
  'reg.createAccount': 'Crear cuenta',
  'pick.usersLoadError': 'No fue posible cargar los usuarios.',
  'pick.searchUsers': 'Buscar por nombre o correo',
  'pick.noRecipient': 'Ningún destinatario seleccionado.',
  'pick.studentsLoadError': 'No fue posible buscar a los alumnos.',
  'pick.searchStudents': 'Buscar alumno por nombre',
  'country.br': 'Brasil',
  'country.us': 'Estados Unidos',
  'country.pt': 'Portugal',

};

/** Congelado: uma tela não pode "corrigir" o catálogo em tempo de execução. */
export const MESSAGES: Record<Language, Record<MessageKey, string>> = Object.freeze({
  pt: Object.freeze(PT),
  en: Object.freeze(EN),
  es: Object.freeze(ES),
});

/**
 * Texto da chave no idioma pedido, com substituição de `{name}`.
 *
 * Um placeholder sem parâmetro correspondente é devolvido literalmente: um `{field}` visível é
 * um defeito que o teste pega, enquanto `undefined` na tela passaria despercebido.
 */
export function t(
  key: MessageKey,
  language: Language,
  params?: Record<string, string | number>,
): string {
  const table = MESSAGES[language] ?? MESSAGES[DEFAULT_LANGUAGE];
  const template = table[key] ?? MESSAGES[DEFAULT_LANGUAGE][key];

  return template.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
    params !== undefined && name in params ? String(params[name]) : placeholder,
  );
}
