import { useState } from 'react';
import toast from 'react-hot-toast';
import axios from 'axios';
import { Check, ChevronDown, Eye, EyeOff } from 'lucide-react';
import { Button } from './Button';
import { Input } from './Input';
import { CountryFlag } from './CountryFlag';
import { registerSchool } from '../api/tenants';
import { COUNTRIES, COUNTRY_ORDER, getCountry, applyMask, digitCount } from '../config/countries';
import { passwordError } from '../domain/password';

/**
 * Formulário de cadastro de colégio.
 *
 * Vive fora da tela porque tem DOIS hospedeiros: a rota `/register` e o painel de login
 * (`AuthPage`, que serve `/login`, `/register` e `/register/parent`). Os campos, as máscaras
 * por país, as gavetas e a chamada `registerSchool` pertencem a um dono só — duplicar isso
 * faria as cópias divergirem na primeira correção de validação.
 *
 * O que NÃO está aqui: título, moldura e o link de volta. Cada hospedeiro decide como
 * emoldurar e para onde voltar (`onSuccess`).
 */

const emptyForm = {
  schoolName: '',
  country: '',
  documentoFiscal: '',
  telefone: '',
  endereco: '',
  cidade: '',
  estado: '',
  cep: '',
  adminName: '',
  adminEmail: '',
  adminPassword: '',
  confirmPassword: '',
};

const STEPS = ['Escola', 'Cadastro', 'Administrador'] as const;

export function SchoolRegisterForm({ onSuccess }: { onSuccess: () => void }) {
  const [form, setForm] = useState(emptyForm);
  const [showPassword, setShowPassword] = useState(false);
  const [countryOpen, setCountryOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});

  const country = form.country ? getCountry(form.country) : null;

  // Gavetas: a 2ª abre ao escolher o país; a 3ª, ao completar os dados da escola.
  const showSchoolDetails = country !== null;
  const schoolDetailsComplete =
    country !== null &&
    form.schoolName.trim() !== '' &&
    digitCount(form.documentoFiscal) === country.documentoFiscal.digits &&
    form.telefone.trim() !== '' &&
    form.endereco.trim() !== '' &&
    form.cidade.trim() !== '' &&
    form.estado.trim() !== '' &&
    digitCount(form.cep) === country.cep.digits;

  const activeStep = !form.country ? 0 : !schoolDetailsComplete ? 1 : 2;

  const handleCountryChange = (code: string) => {
    // Ao trocar o país, limpa os campos cuja máscara é específica do país.
    setForm((f) => ({ ...f, country: code, documentoFiscal: '', cep: '' }));
    setErrors({});
    setCountryOpen(false);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const nextErrors: Record<string, string> = {};

    const pwError = passwordError(form.adminPassword);
    if (pwError) nextErrors.adminPassword = pwError;
    if (form.adminPassword !== form.confirmPassword) nextErrors.confirmPassword = 'As senhas não conferem.';

    setErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) return;

    setLoading(true);
    try {
      await registerSchool({
        schoolName: form.schoolName,
        country: form.country,
        documentoFiscal: form.documentoFiscal || undefined,
        telefone: form.telefone || undefined,
        endereco: form.endereco || undefined,
        cidade: form.cidade || undefined,
        estado: form.estado || undefined,
        cep: form.cep || undefined,
        adminName: form.adminName,
        adminEmail: form.adminEmail,
        adminPassword: form.adminPassword,
      });
      toast.success('Colégio cadastrado! Faça login com o email do administrador.');
      onSuccess();
    } catch (err) {
      const title =
        axios.isAxiosError(err) && typeof err.response?.data?.title === 'string'
          ? err.response.data.title
          : '';
      if (title === 'email_already_registered') {
        toast.error('Este email já está cadastrado.');
      } else if (title === 'unsupported_country') {
        toast.error('País não suportado.');
      } else {
        toast.error('Não foi possível concluir o cadastro. Verifique os dados.');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      {/*
       * Trilha de passos. Os passos têm a largura do próprio texto e quem absorve a folga é o
       * CONECTOR (`flex-1`). Dividir a linha em três partes iguais dava a mesma largura a
       * "Escola" e a "Administrador", e o rótulo mais longo era cortado no painel de 360 px.
       */}
      <ol className="mb-5 flex items-center">
        {STEPS.map((label, i) => (
          <li key={label} className="flex items-center">
            <span
              className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[10px] font-semibold transition-colors ${
                i <= activeStep
                  ? 'bg-gradient-to-r from-indigo-600 to-indigo-700 text-white shadow-sm'
                  : 'bg-stone-300 text-stone-500'
              }`}
            >
              {i < activeStep ? <Check size={12} /> : i + 1}
            </span>
            <span
              className={`ml-1.5 truncate text-[11px] whitespace-nowrap transition-colors ${
                i <= activeStep ? 'font-medium text-stone-700' : 'text-stone-400'
              }`}
            >
              {label}
            </span>
            {i < STEPS.length - 1 && (
              <span
                className={`mx-2 h-px min-w-2 flex-1 transition-colors ${
                  i < activeStep ? 'bg-stone-500' : 'bg-stone-300'
                }`}
              />
            )}
          </li>
        ))}
      </ol>

      <form onSubmit={handleSubmit} className="space-y-4">
        {/* Gaveta 1 — sempre visível: nome do colégio e país */}
        <Input
          label="Nome do colégio"
          value={form.schoolName}
          onChange={(e) => setForm({ ...form, schoolName: e.target.value })}
          required
          placeholder="Ex.: Colégio Dom Bosco"
          className="bg-white !border-stone-400 focus:!ring-stone-500"
        />

        <div className="relative">
          <span className="mb-1 block text-xs font-medium text-gray-600">País</span>
          <button
            type="button"
            onClick={() => setCountryOpen((o) => !o)}
            className="flex w-full items-center justify-between gap-2 rounded-lg border border-stone-400 bg-white px-3 py-2 text-sm text-gray-700 focus:ring-2 focus:ring-stone-500 focus:outline-none"
            aria-haspopup="listbox"
            aria-expanded={countryOpen}
          >
            <span className="flex items-center gap-2">
              {country ? (
                <>
                  <CountryFlag code={country.code} />
                  <span>{country.label}</span>
                </>
              ) : (
                <span className="text-gray-400">Selecione um país</span>
              )}
            </span>
            <ChevronDown
              size={16}
              className={`text-stone-400 transition-transform ${countryOpen ? 'rotate-180' : ''}`}
            />
          </button>

          {countryOpen && (
            <>
              <div className="fixed inset-0 z-10" onClick={() => setCountryOpen(false)} />
              <ul
                role="listbox"
                className="animate-drawer absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border border-stone-300 bg-white shadow-lg"
              >
                {COUNTRY_ORDER.map((code) => (
                  <li key={code}>
                    <button
                      type="button"
                      role="option"
                      aria-selected={form.country === code}
                      onClick={() => handleCountryChange(code)}
                      className={`flex w-full items-center gap-3 px-3 py-2 text-left text-sm transition-colors hover:bg-stone-100 ${
                        form.country === code ? 'bg-stone-50' : ''
                      }`}
                    >
                      <CountryFlag code={code} />
                      <span className="flex-1">{COUNTRIES[code].label}</span>
                      {form.country === code && <Check size={15} className="text-emerald-600" />}
                    </button>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>

        {/* Gaveta 2 — abre após escolher o país */}
        {showSchoolDetails && country && (
          <div className="animate-drawer space-y-4 border-t border-stone-300 pt-4">
            <p className="text-sm font-medium text-stone-700">Dados da escola</p>

            <div className="flex flex-wrap gap-x-4 gap-y-1 rounded-lg border border-stone-300 bg-stone-100 px-3 py-2 text-xs text-stone-600">
              <span>
                Moeda: <strong>{country.moeda} ({country.moedaSimbolo})</strong>
              </span>
              <span>
                Data: <strong>{country.formatoData}</strong>
              </span>
              <span>
                Ano letivo: <strong>{country.calendarioLabel}</strong>
              </span>
            </div>

            <Input
              label={country.documentoFiscal.label}
              value={form.documentoFiscal}
              onChange={(e) =>
                setForm({ ...form, documentoFiscal: applyMask(e.target.value, country.documentoFiscal.mask) })
              }
              placeholder={country.documentoFiscal.placeholder}
              inputMode="numeric"
              className="bg-white !border-stone-400 focus:!ring-stone-500"
            />

            <Input
              label="Telefone"
              value={form.telefone}
              onChange={(e) => setForm({ ...form, telefone: e.target.value })}
              placeholder={`${country.ddi} ${country.telefonePlaceholder}`}
              className="bg-white !border-stone-400 focus:!ring-stone-500"
            />

            <Input
              label="Endereço"
              value={form.endereco}
              onChange={(e) => setForm({ ...form, endereco: e.target.value })}
              placeholder="Rua, número, bairro"
              className="bg-white !border-stone-400 focus:!ring-stone-500"
            />

            <div className="grid grid-cols-2 gap-3">
              <Input
                label="Cidade"
                value={form.cidade}
                onChange={(e) => setForm({ ...form, cidade: e.target.value })}
                className="bg-white !border-stone-400 focus:!ring-stone-500"
              />
              <Input
                label={country.estadoLabel}
                value={form.estado}
                onChange={(e) => setForm({ ...form, estado: e.target.value })}
                className="bg-white !border-stone-400 focus:!ring-stone-500"
              />
            </div>

            <Input
              label={country.cep.label}
              value={form.cep}
              onChange={(e) => setForm({ ...form, cep: applyMask(e.target.value, country.cep.mask) })}
              placeholder={country.cep.placeholder}
              inputMode="numeric"
              className="bg-white !border-stone-400 focus:!ring-stone-500"
            />
          </div>
        )}

        {/* Gaveta 3 — abre após completar os dados da escola */}
        {schoolDetailsComplete && (
          <div className="animate-drawer space-y-3 border-t border-stone-300 pt-4">
            <p className="text-sm font-medium text-stone-700">Administrador da conta</p>

            <Input
              label="Nome"
              value={form.adminName}
              onChange={(e) => setForm({ ...form, adminName: e.target.value })}
              required
              className="bg-white !border-stone-400 focus:!ring-stone-500"
            />
            <Input
              label="Email"
              type="email"
              value={form.adminEmail}
              onChange={(e) => setForm({ ...form, adminEmail: e.target.value })}
              required
              placeholder="voce@colegio.com"
              className="bg-white !border-stone-400 focus:!ring-stone-500"
            />
            <div className="relative">
              <Input
                label="Senha"
                type={showPassword ? 'text' : 'password'}
                value={form.adminPassword}
                onChange={(e) => setForm({ ...form, adminPassword: e.target.value })}
                required
                error={errors.adminPassword}
                className="bg-white !border-stone-400 focus:!ring-stone-500 !pr-10"
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                className="absolute right-3 top-[34px] text-stone-400 transition-colors hover:text-stone-600"
                tabIndex={-1}
              >
                {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
              </button>
            </div>
            <Input
              label="Confirmar senha"
              type={showPassword ? 'text' : 'password'}
              value={form.confirmPassword}
              onChange={(e) => setForm({ ...form, confirmPassword: e.target.value })}
              required
              error={errors.confirmPassword}
              className="bg-white !border-stone-400 focus:!ring-stone-500"
            />

            <Button type="submit" className="mt-1 w-full !bg-stone-700 shadow-md hover:!bg-stone-800" disabled={loading}>
              {loading ? 'Cadastrando...' : 'Cadastrar colégio'}
            </Button>
          </div>
        )}
      </form>
    </>
  );
}
