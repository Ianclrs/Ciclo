import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import axios from 'axios';
import { ArrowLeft, Check, ChevronDown, Eye, EyeOff, School } from 'lucide-react';
import { Button } from '../components/Button';
import { Input } from '../components/Input';
import { CountryFlag } from '../components/CountryFlag';
import { registerSchool } from '../api/tenants';
import { COUNTRIES, COUNTRY_ORDER, getCountry, applyMask, digitCount } from '../config/countries';

function passwordError(pw: string): string | null {
  if (pw.length < 8) return 'A senha deve ter no mínimo 8 caracteres.';
  if (!/[A-Z]/.test(pw)) return 'A senha deve ter pelo menos uma letra maiúscula.';
  if (!/\d/.test(pw)) return 'A senha deve ter pelo menos um número.';
  if (!/[^A-Za-z0-9]/.test(pw)) return 'A senha deve ter pelo menos um símbolo (ex.: !@#$%).';
  return null;
}

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

export default function RegisterPage() {
  const navigate = useNavigate();
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
      navigate('/login');
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
    <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-stone-100 via-stone-50 to-stone-100 animate-gradient px-4 py-8 relative">
      {/* Os blobs ficam num wrapper com overflow-hidden próprio para não recortar
          o dropdown de países nem gerar scroll. */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <div className="absolute -top-40 -right-40 w-96 h-96 bg-violet-300/40 rounded-full blur-3xl animate-blob" />
        <div className="absolute -bottom-32 left-1/4 w-80 h-80 bg-emerald-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '4s', animationDuration: '27s' }} />
        <div className="absolute top-1/4 right-1/3 w-60 h-60 bg-amber-300/35 rounded-full blur-3xl animate-blob" style={{ animationDelay: '6s', animationDuration: '35s', animationDirection: 'reverse' }} />
      </div>

      <div className="w-full max-w-lg bg-stone-200 rounded-2xl shadow-2xl border border-stone-300 relative z-10">
        <div className="h-1 bg-gradient-to-r from-violet-500 via-blue-500 to-emerald-500 animate-gradient rounded-t-2xl" />
        <div className="p-5 sm:p-8 pt-2 sm:pt-3">
          <div className="text-center mb-4">
            <div className="w-16 h-16 mx-auto sm:w-20 sm:h-20 rounded-full bg-stone-700/90 flex items-center justify-center">
              <School className="w-8 h-8 sm:w-10 sm:h-10 text-white" />
            </div>
            <h1 className="text-2xl font-bold text-stone-800 tracking-tight mt-2">Cadastrar Colégio</h1>
            <p className="text-stone-500 mt-0.5 text-sm">Leve a gestão escolar da sua escola para o Ciclo</p>
          </div>

          <div className="flex items-center justify-center mb-5">
            {STEPS.map((label, i) => (
              <div key={label} className="flex items-center">
                <div className="flex items-center gap-1.5">
                  <span
                    className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-semibold transition-colors ${
                      i <= activeStep ? 'bg-stone-700 text-white' : 'bg-stone-300 text-stone-500'
                    }`}
                  >
                    {i < activeStep ? <Check size={13} /> : i + 1}
                  </span>
                  <span className={`text-xs transition-colors ${i <= activeStep ? 'text-stone-700 font-medium' : 'text-stone-400'}`}>
                    {label}
                  </span>
                </div>
                {i < STEPS.length - 1 && (
                  <div className={`w-5 sm:w-8 h-px mx-2 transition-colors ${i < activeStep ? 'bg-stone-500' : 'bg-stone-300'}`} />
                )}
              </div>
            ))}
          </div>

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
              <span className="block text-xs font-medium text-gray-600 mb-1">País</span>
              <button
                type="button"
                onClick={() => setCountryOpen((o) => !o)}
                className="w-full flex items-center justify-between gap-2 rounded-lg border border-stone-400 bg-white px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-stone-500"
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
                <ChevronDown size={16} className={`text-stone-400 transition-transform ${countryOpen ? 'rotate-180' : ''}`} />
              </button>

              {countryOpen && (
                <>
                  <div className="fixed inset-0 z-10" onClick={() => setCountryOpen(false)} />
                  <ul
                    role="listbox"
                    className="absolute z-20 mt-1 w-full max-h-64 overflow-y-auto bg-white border border-stone-300 rounded-lg shadow-lg animate-drawer"
                  >
                    {COUNTRY_ORDER.map((code) => (
                      <li key={code}>
                        <button
                          type="button"
                          role="option"
                          aria-selected={form.country === code}
                          onClick={() => handleCountryChange(code)}
                          className={`w-full flex items-center gap-3 px-3 py-2 text-sm text-left hover:bg-stone-100 transition-colors ${
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

                <div className="bg-stone-100 border border-stone-300 rounded-lg px-3 py-2 text-xs text-stone-600 flex flex-wrap gap-x-4 gap-y-1">
                  <span>Moeda: <strong>{country.moeda} ({country.moedaSimbolo})</strong></span>
                  <span>Data: <strong>{country.formatoData}</strong></span>
                  <span>Ano letivo: <strong>{country.calendarioLabel}</strong></span>
                </div>

                <Input
                  label={country.documentoFiscal.label}
                  value={form.documentoFiscal}
                  onChange={(e) => setForm({ ...form, documentoFiscal: applyMask(e.target.value, country.documentoFiscal.mask) })}
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
                    className="absolute right-3 top-[34px] text-stone-400 hover:text-stone-600 transition-colors"
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

                <Button type="submit" className="w-full !bg-stone-700 hover:!bg-stone-800 shadow-md mt-1" disabled={loading}>
                  {loading ? 'Cadastrando...' : 'Cadastrar colégio'}
                </Button>
              </div>
            )}
          </form>

          <div className="mt-4 text-center">
            <Link to="/login" className="inline-flex items-center gap-1 text-sm text-stone-600 hover:text-stone-800 transition-colors">
              <ArrowLeft size={16} />
              Voltar para o login
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
