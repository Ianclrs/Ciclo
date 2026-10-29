import { useEffect, useState } from 'react';
import { Check, Search } from 'lucide-react';
import { getUsers } from '../api/users';
import { getStudents } from '../api/students';
import { apiErrorMessage } from '../api/errors';
import { roleLabel } from '../domain/userAdmin';
import { matches } from '../utils/search';
import type { ManagedUser, Student } from '../types';

/**
 * Seleção de destinatários por busca, não por identificador.
 *
 * Antes estas telas pediam o UUID do usuário ou do aluno digitado à mão — inviável para
 * quem atende no balcão. A busca aqui é por nome/e-mail (usuários) e nome/turma (alunos).
 */

const listClass =
  'no-scrollbar max-h-60 overflow-y-auto rounded-lg border border-gray-200/80 bg-white/60';

const rowClass =
  'flex w-full items-center gap-3 px-3 py-2 text-left text-sm transition-colors hover:bg-white/80';

function SearchField({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
}) {
  return (
    <div className="relative">
      <Search
        size={16}
        className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-gray-400"
      />
      <input
        type="search"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        aria-label={placeholder}
        className="w-full rounded-lg border border-gray-300 bg-white/80 py-2 pl-9 pr-3 text-sm focus:border-transparent focus:outline-none focus:ring-2 focus:ring-indigo-500"
      />
    </div>
  );
}

export function UserPicker({
  selected,
  onChange,
}: {
  selected: ManagedUser[];
  onChange: (users: ManagedUser[]) => void;
}) {
  const [query, setQuery] = useState('');
  const [users, setUsers] = useState<ManagedUser[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    getUsers()
      .then((list) => {
        if (active) setUsers(list.filter((user) => user.isActive));
      })
      .catch((err) => {
        if (active) setError(apiErrorMessage(err, 'Não foi possível carregar os usuários.'));
      });
    return () => {
      active = false;
    };
  }, []);

  const visible = users?.filter((user) => matches(`${user.name} ${user.email}`, query)) ?? [];
  const isSelected = (id: string) => selected.some((user) => user.id === id);

  const toggle = (user: ManagedUser) => {
    onChange(isSelected(user.id) ? selected.filter((u) => u.id !== user.id) : [...selected, user]);
  };

  return (
    <div className="space-y-2">
      <SearchField value={query} onChange={setQuery} placeholder="Buscar por nome ou e-mail" />

      {error ? (
        <p role="alert" className="text-sm text-red-700">{error}</p>
      ) : users === null ? (
        <div className={listClass}>
          {[0, 1, 2].map((i) => (
            <div key={i} className="px-3 py-2">
              <span className="block h-4 w-1/2 rounded bg-gray-200 animate-pulse" />
            </div>
          ))}
        </div>
      ) : visible.length === 0 ? (
        <p className="rounded-lg border border-gray-200/80 bg-white/60 px-3 py-4 text-center text-sm text-gray-600">
          Nenhum usuário encontrado.
        </p>
      ) : (
        <div className={listClass}>
          {visible.map((user) => {
            const active = isSelected(user.id);
            return (
              <button
                key={user.id}
                type="button"
                onClick={() => toggle(user)}
                aria-pressed={active}
                className={`${rowClass} ${active ? 'bg-violet-50' : ''}`}
              >
                <span
                  className={`flex h-4 w-4 shrink-0 items-center justify-center rounded border ${
                    active ? 'border-violet-600 bg-violet-600 text-white' : 'border-gray-300'
                  }`}
                  aria-hidden="true"
                >
                  {active && <Check size={12} strokeWidth={3} />}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium text-gray-800">{user.name}</span>
                  <span className="block truncate text-xs text-gray-600">{user.email}</span>
                </span>
                <span className="shrink-0 text-xs text-gray-500">{roleLabel(user.role)}</span>
              </button>
            );
          })}
        </div>
      )}

      <p className="text-xs text-gray-600">
        {selected.length === 0
          ? 'Nenhum destinatário selecionado.'
          : `${selected.length} selecionado${selected.length === 1 ? '' : 's'}: ${selected
              .map((user) => user.name)
              .join(', ')}`}
      </p>
    </div>
  );
}

export function StudentPicker({
  selected,
  onChange,
}: {
  selected: Student | null;
  onChange: (student: Student | null) => void;
}) {
  const [query, setQuery] = useState('');
  // Guarda o termo junto dos resultados: resultado de uma busca antiga não é "carregando"
  // o termo atual, e isso é derivado no render em vez de escrito de dentro do efeito.
  const [found, setFound] = useState<{ term: string; items: Student[] } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const term = query.trim();

  useEffect(() => {
    if (term.length < 2) return;

    let active = true;
    // Espera o usuário parar de digitar: cada tecla dispararia uma consulta à API.
    const timer = setTimeout(() => {
      getStudents({ search: term, pageSize: 8 })
        .then((res) => {
          if (!active) return;
          setFound({ term, items: res.items });
          setError(null);
        })
        .catch((err) => {
          if (!active) return;
          setFound({ term, items: [] });
          setError(apiErrorMessage(err, 'Não foi possível buscar os alunos.'));
        });
    }, 300);

    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [term]);

  const results = found?.term === term ? found.items : null;
  const searching = term.length >= 2 && results === null;

  if (selected) {
    return (
      <div className="flex items-center justify-between gap-3 rounded-lg border border-violet-200 bg-violet-50 px-3 py-2">
        <span className="min-w-0">
          <span className="block truncate text-sm font-medium text-gray-800">{selected.nome}</span>
          <span className="block text-xs text-gray-600">
            {selected.turma} · {selected.anoLetivo}
          </span>
        </span>
        <button
          type="button"
          onClick={() => onChange(null)}
          className="shrink-0 text-sm font-medium text-violet-700 hover:text-violet-800"
        >
          Trocar
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <SearchField value={query} onChange={setQuery} placeholder="Buscar aluno por nome" />

      {error ? (
        <p role="alert" className="text-sm text-red-700">{error}</p>
      ) : term.length < 2 ? (
        <p className="rounded-lg border border-gray-200/80 bg-white/60 px-3 py-4 text-center text-sm text-gray-600">
          Digite ao menos 2 letras para buscar.
        </p>
      ) : searching ? (
        <p className="rounded-lg border border-gray-200/80 bg-white/60 px-3 py-4 text-center text-sm text-gray-600">
          Buscando…
        </p>
      ) : results && results.length === 0 ? (
        <p className="rounded-lg border border-gray-200/80 bg-white/60 px-3 py-4 text-center text-sm text-gray-600">
          Nenhum aluno encontrado.
        </p>
      ) : (
        <div className={listClass}>
          {results?.map((student) => (
            <button
              key={student.id}
              type="button"
              onClick={() => onChange(student)}
              className={rowClass}
            >
              <span className="min-w-0 flex-1">
                <span className="block truncate font-medium text-gray-800">{student.nome}</span>
                <span className="block truncate text-xs text-gray-600">
                  {student.turma} · {student.anoLetivo}
                </span>
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
