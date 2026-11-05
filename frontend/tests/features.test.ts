import test from 'node:test';
import assert from 'node:assert/strict';
import {
  TENANT_FEATURES,
  isTenantFeature,
  parseTenantFeatures,
} from '../src/domain/features.ts';

/**
 * O vocabulário do frontend espelha `TenantFeature` na API. Se um lado ganhar uma flag nova e o
 * outro não, a funcionalidade fica ligada no servidor e invisível (ou o contrário) — por isso o
 * conjunto é fixado aqui.
 */
test('o vocabulário das funcionalidades é o mesmo da API', () => {
  assert.deepEqual(
    [...TENANT_FEATURES].sort(),
    [
      'AcademicPerformance',
      'AuditExport',
      'BatchReenrollment',
      'Documents',
      'Notifications',
      'ParentPortal',
    ],
  );
});

test('valor desconhecido não vira funcionalidade conhecida', () => {
  assert.equal(isTenantFeature('Documents'), true);
  assert.equal(isTenantFeature('Documents '), false);
  assert.equal(isTenantFeature('RelatoriosAvancados'), false);
});

test('parse descarta o que não é conhecido e preserva o resto', () => {
  assert.deepEqual(
    parseTenantFeatures(['Documents', 'RelatoriosAvancados', 'ParentPortal']),
    ['Documents', 'ParentPortal'],
  );
});

test('parse trata ausência de lista como nenhuma funcionalidade', () => {
  assert.deepEqual(parseTenantFeatures(null), []);
  assert.deepEqual(parseTenantFeatures(undefined), []);
  assert.deepEqual(parseTenantFeatures([]), []);
});
