import test from 'node:test';
import assert from 'node:assert/strict';
import {
  INVITE_ROLES,
  activationLabelKey,
  canChangeRole,
  roleLabelKey,
  validateInvite,
} from '../src/domain/userAdmin.ts';

/**
 * M11 — regras da tela de gestão de usuários.
 *
 * Só a validação do convite e as decisões de habilitação podem ser provadas sem DOM; o resto
 * (chamadas, tabela) é verificado pelo build e pela API, que recusa as mesmas ações.
 */

test('papéis têm chave de rótulo e papel desconhecido não inventa texto', () => {
  assert.equal(roleLabelKey('Admin'), 'role.admin');
  assert.equal(roleLabelKey('Staff'), 'role.staff');
  assert.equal(roleLabelKey('Parent'), 'role.parent');
  assert.equal(roleLabelKey('Outro'), null);
});

test('convite válido não tem erro', () => {
  assert.equal(validateInvite({ name: 'Ana', email: 'ana@escola.com', role: 'Staff' }), null);
});

test('convite sem nome é rejeitado', () => {
  assert.equal(validateInvite({ name: '   ', email: 'ana@escola.com', role: 'Staff' }), 'users.validate.name');
});

test('e-mail inválido é rejeitado', () => {
  for (const email of ['ana', 'ana@', 'ana@escola', 'ana escola@x.com']) {
    assert.equal(
      validateInvite({ name: 'Ana', email, role: 'Staff' }),
      'users.validate.email',
      `deveria recusar: ${email}`,
    );
  }
});

test('papel fora da lista de convite é rejeitado', () => {
  const invalid = { name: 'Ana', email: 'ana@escola.com', role: 'Parent' as never };
  assert.equal(validateInvite(invalid), 'users.validate.role');
});

test('apenas Admin e Staff podem ser convidados (responsável se cadastra sozinho)', () => {
  assert.deepEqual([...INVITE_ROLES], ['Admin', 'Staff']);
});

test('o próprio usuário não pode trocar o próprio papel', () => {
  assert.equal(canChangeRole('u1', 'u2'), true);
  assert.equal(canChangeRole('u1', 'u1'), false);
});

test('chave da ação de status acompanha o estado', () => {
  assert.equal(activationLabelKey(true), 'user.deactivate');
  assert.equal(activationLabelKey(false), 'user.activate');
});
