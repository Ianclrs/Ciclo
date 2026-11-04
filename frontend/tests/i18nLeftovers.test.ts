import test from 'node:test';
import assert from 'node:assert/strict';
import ts from 'typescript';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Guarda contra texto de interface fora do catálogo (Spec 190).
 *
 * O defeito que originou este teste foi de inventário: uma auditoria manual declarou a
 * tradução concluída enquanto telas inteiras ainda tinham literais em português — no texto do
 * JSX, em `header` de coluna e em `toLocaleString('pt-BR')`. Filtrar por acentos não pegava
 * nada disso ("Excluir", "Aprovar", "Revogado em" não têm acento), então a verificação passa a
 * ser estrutural: o AST diz o que é texto de tela, não uma expressão regular.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = join(HERE, '..', 'src');

/** Nome próprio da aplicação: marca não é tradução. */
const BRAND = new Set(['Ciclo']);

/**
 * Valores de atributo que são dado, não idioma: exemplo de cor, fuso e URL de logotipo. Ficam
 * explícitos para que qualquer texto novo em atributo exija uma decisão consciente.
 */
const DATA_ATTRIBUTE_VALUES = new Set(['#1A2B3C', 'America/Sao_Paulo', 'https://…/logo.png', 'Ciclo']);

/** Atributos cujo conteúdo aparece para o usuário (inclusive para o leitor de tela). */
const TEXT_ATTRIBUTES = new Set(['aria-label', 'placeholder', 'title', 'alt']);

/** Formatação com locale literal: o formato deixaria de acompanhar o idioma ativo. */
const LOCALE_METHODS = new Set(['toLocaleString', 'toLocaleDateString', 'toLocaleTimeString']);

function files(extension: string): string[] {
  const found: string[] = [];

  function walk(directory: string): void {
    for (const name of readdirSync(directory)) {
      const path = join(directory, name);
      if (statSync(path).isDirectory()) walk(path);
      else if (path.endsWith(extension)) found.push(path);
    }
  }

  walk(SRC);
  return found;
}

function parse(path: string): ts.SourceFile {
  const kind = path.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
  return ts.createSourceFile(path, readFileSync(path, 'utf8'), ts.ScriptTarget.Latest, true, kind);
}

function position(source: ts.SourceFile, node: ts.Node): string {
  const line = source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
  return `${relative(SRC, source.fileName)}:${line}`;
}

/** Texto visível escrito direto no JSX, em vez de `t(...)`. */
test('nenhum texto de interface fica literal no JSX', () => {
  const offenders: string[] = [];

  for (const path of files('.tsx')) {
    const source = parse(path);

    (function visit(node: ts.Node): void {
      if (ts.isJsxText(node)) {
        const text = node.getText(source).replace(/\s+/g, ' ').trim();
        if (/[A-Za-zÀ-ÿ]{2,}/.test(text) && !BRAND.has(text)) {
          offenders.push(`${position(source, node)} → "${text.slice(0, 60)}"`);
        }
      }
      ts.forEachChild(node, visit);
    })(source);
  }

  assert.deepEqual(offenders, [], `texto fora do catálogo:\n${offenders.join('\n')}`);
});

/** Rótulo acessível e placeholder também são interface: precisam vir de `t(...)`. */
test('atributos de texto usam o catálogo em vez de literal', () => {
  const offenders: string[] = [];

  for (const path of files('.tsx')) {
    const source = parse(path);

    (function visit(node: ts.Node): void {
      if (
        ts.isJsxAttribute(node) &&
        node.initializer !== undefined &&
        ts.isStringLiteral(node.initializer) &&
        TEXT_ATTRIBUTES.has(node.name.getText(source))
      ) {
        const value = node.initializer.text.trim();
        if (/[A-Za-zÀ-ÿ]{2,}/.test(value) && !DATA_ATTRIBUTE_VALUES.has(value)) {
          offenders.push(`${position(source, node)} → ${node.name.getText(source)}="${value}"`);
        }
      }
      ts.forEachChild(node, visit);
    })(source);
  }

  assert.deepEqual(offenders, [], `atributo com texto fixo:\n${offenders.join('\n')}`);
});

/** Cabeçalho de coluna vem de `t('...')`: era um dos pontos cegos da auditoria anterior. */
test('nenhuma coluna de tabela guarda o título em texto fixo', () => {
  const offenders: string[] = [];

  for (const path of files('.tsx')) {
    const source = parse(path);

    (function visit(node: ts.Node): void {
      if (
        ts.isPropertyAssignment(node) &&
        (ts.isIdentifier(node.name) || ts.isStringLiteral(node.name)) &&
        node.name.getText(source).replace(/['"]/g, '') === 'header' &&
        ts.isStringLiteral(node.initializer) &&
        node.initializer.text.trim() !== ''
      ) {
        offenders.push(`${position(source, node)} → header: "${node.initializer.text}"`);
      }
      ts.forEachChild(node, visit);
    })(source);
  }

  assert.deepEqual(offenders, [], `cabeçalho com texto fixo:\n${offenders.join('\n')}`);
});

/** `toLocaleString('pt-BR')` fixava a data no português mesmo com a tela em inglês. */
test('a formatação de data e número não fixa o locale', () => {
  const offenders: string[] = [];

  for (const path of [...files('.ts'), ...files('.tsx')]) {
    const source = parse(path);

    (function visit(node: ts.Node): void {
      if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)) {
        const method = node.expression.name.text;
        const first = node.arguments[0];
        if (LOCALE_METHODS.has(method) && first !== undefined && ts.isStringLiteralLike(first)) {
          offenders.push(`${position(source, node)} → ${method}('${first.text}')`);
        }
      }
      ts.forEachChild(node, visit);
    })(source);
  }

  assert.deepEqual(offenders, [], `locale fixo na formatação:\n${offenders.join('\n')}`);
});
