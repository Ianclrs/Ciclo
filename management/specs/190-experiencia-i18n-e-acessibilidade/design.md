# Spec 190: experiencia-i18n-e-acessibilidade — Design

## Design Approach

Four owners already exist and each receives the minimum extension the value block requires, instead of a parallel layer:

- locale selection is new (`frontend/src/i18n/locale.ts`), because nothing in the tree chooses a locale today: `frontend/src/utils/format.ts` hardcodes `pt-BR` in a module constant;
- formatting stays in `frontend/src/utils/format.ts` and gains an explicit optional locale argument, so every existing caller keeps working untouched;
- error translation stays in `frontend/src/api/errors.ts`; only the data moves to a language-keyed module so one map serves three languages;
- component-level guarantees land in the four shared controls (`Input`, `Select`, `Button`, `Modal`) and in `LoadError`, which every screen already imports.

Two new leaf components are added because no owner exists: `EmptyState` (the missing third screen state) and `ErrorBoundary` (the missing global boundary). Three new data or pure-function modules back the deterministic proofs that cannot be obtained from a DOM-less runner: `focusTrap.ts` for the keyboard cycle, `contrast.ts` for the WCAG ratio and `contrastPairs.ts` for the declared colour pairs.

The rule applied throughout: a behaviour is enforced once at the shared owner, never repeated per screen. A screen contributes only the wiring call.

## Architecture Decisions

- **AD-190-1 — Locale is an explicit argument, never ambient state inside pure modules.** The formatters, the catalog, the error map and the contrast helper receive the locale or language as a parameter. Only the React context in `frontend/src/i18n/LocaleContext.tsx` holds ambient state, so pure functions stay testable with `node --test` without a DOM.
- **AD-190-2 — Country refines the region of its own language; a foreign preference discards the region.** `BR` plus stored `pt` yields `pt-BR`; `PT` plus stored `pt` yields `pt-PT`; `BR` plus stored `en` yields `en-US`. Without this rule the two Portuguese locales collapse into one and the European date format is lost.
- **AD-190-3 — The language preference is persisted under one storage key and is optional.** A missing, malformed or unreadable value degrades to the tenant default; storage is never a hard dependency of rendering.
- **AD-190-4 — The error map is one data structure keyed by code then language.** `Record<string, Record<Language, string>>` keeps every translation of a code adjacent, so parity is a single structural assertion and a partial translation cannot merge silently.
- **AD-190-5 — Backward compatibility is preserved by optional trailing parameters.** `formatDate(value, locale?)` and `codeMessage(code, fallback, language?)` keep the pre-spec arity; the defaults `pt-BR` and `pt` reproduce the previous output.
- **AD-190-6 — Keyboard and contrast proofs are pure functions, not DOM tests.** The project runner is `node --test` with no DOM library installed, so `nextFocusIndex` and `contrastRatio` are extracted as pure functions and asserted directly, while the components are verified by literal source inspection plus the TypeScript build.
- **AD-190-7 — The message catalog is a project-owned constant, not a library.** V19 requires PT, EN and ES; a small lookup function covers that need without a runtime dependency, matching the anti-scope of this spec.
- **AD-190-8 — Contrast pairs are pinned to explicit hex values.** The four text and background pairs mirror the colour tokens declared in `frontend/src/components/accentStyles.ts` and are written as literal hex in `frontend/src/config/contrastPairs.ts`, so the assertion never depends on how a CSS framework compiles a class name.
- **AD-190-9 — The error boundary does not depend on React context.** A class component cannot call a hook, and the context itself may be the thing that threw, so the fallback resolves the language directly from storage with the default as fallback.

## Component / Module Breakdown

### Component: locale resolver

**Purpose:** Turns a tenant country and a stored language preference into the single active language and locale tag.
**Interface:** `frontend/src/i18n/locale.ts` exports:
- `type Language = 'pt' | 'en' | 'es'`
- `const LANGUAGE_ORDER: readonly Language[] = ['pt', 'en', 'es']`
- `const DEFAULT_LANGUAGE: Language = 'pt'`
- `const DEFAULT_LOCALE = 'pt-BR'`
- `const LANGUAGE_STORAGE_KEY = 'dscode.language'`
- `interface ResolvedLocale { language: Language; locale: string }`
- `interface LocaleResolutionInput { tenantCountry?: string | null; userLanguage?: string | null }`
- `function isLanguage(value: unknown): value is Language`
- `function languageForCountry(code: string | null | undefined): Language`
- `function defaultLocaleForLanguage(language: Language): string`
- `function resolveLocale(input: LocaleResolutionInput): ResolvedLocale`
- `function readStoredLanguage(): Language | null`
- `function storeLanguage(language: Language): void`

**Internal Logic:**
1. `COUNTRY_LANGUAGES` is `{ BR: 'pt', PT: 'pt', US: 'en' }`; `COUNTRY_LOCALES` is `{ BR: 'pt-BR', PT: 'pt-PT', US: 'en-US' }`; `LANGUAGE_LOCALES` is `{ pt: 'pt-BR', en: 'en-US', es: 'es-ES' }`.
2. `languageForCountry` returns `COUNTRY_LANGUAGES[code ?? 'BR'] ?? 'pt'`.
3. `resolveLocale` computes `countryLanguage = languageForCountry(input.tenantCountry)` and `preferred = isLanguage(input.userLanguage) ? input.userLanguage : countryLanguage`.
4. When `preferred === countryLanguage`, the locale is `COUNTRY_LOCALES[input.tenantCountry ?? 'BR'] ?? LANGUAGE_LOCALES[preferred]`; otherwise it is `LANGUAGE_LOCALES[preferred]`.
5. The result is `{ language: preferred, locale }`.
6. `readStoredLanguage` wraps `localStorage.getItem(LANGUAGE_STORAGE_KEY)` in `try`/`catch` and returns the value only when `isLanguage` accepts it.
7. `storeLanguage` wraps `localStorage.setItem(LANGUAGE_STORAGE_KEY, language)` in `try`/`catch`.
**Dependencies:** `frontend/src/config/countries.ts` supplies the supported country codes; the browser `localStorage` is the only external surface.
**Error Handling:** Any thrown storage error is caught and converted to `null` on read or ignored on write; no exception escapes either function. A country outside the table resolves through the `pt` fallback.

### Component: message catalog

**Purpose:** Single source of catalog text for PT, EN and ES with placeholder interpolation.
**Interface:** `frontend/src/i18n/messages.ts` exports `type MessageKey`, `const MESSAGES: Record<Language, Record<MessageKey, string>>` and `function t(key: MessageKey, language: Language, params?: Record<string, string | number>): string`. The `MESSAGES` object is exported so the parity test can enumerate keys.
**Internal Logic:**
1. `MESSAGES` is a frozen literal with the three language records, each containing the same key set.
2. Keys: `common.loading`, `common.error`, `common.empty`, `common.retry`, `common.reload`, `common.close`, `errorBoundary.title`, `errorBoundary.body`, `field.required`, `list.emptyTitle`, `language.label`.
3. `t` reads `MESSAGES[language][key]`, then replaces each `{name}` occurrence matched by `/\{(\w+)\}/g` with `String(params[name])` when the parameter exists.
4. The replacement is skipped when `params` is absent or the key is missing from it, so the placeholder remains visible in the returned string.
**Dependencies:** `type Language` from `frontend/src/i18n/locale.ts`.
**Error Handling:** An unknown language cannot occur through the type system; at runtime the function falls back to `MESSAGES[DEFAULT_LANGUAGE][key]` when `MESSAGES[language]` is `undefined`, so a malformed caller produces text instead of `undefined`.

### Component: locale context and provider

**Purpose:** Holds the resolved locale for one render tree and exposes it to components through a hook.
**Interface:** `frontend/src/i18n/LocaleContext.tsx` exports:
- `interface LocaleContextValue { language: Language; locale: string; t: (key: MessageKey, params?: Record<string, string | number>) => string; setLanguage: (language: Language) => void }`
- `function LocaleProvider(props: { country?: string | null; children: ReactNode })`
- `function useLocale(): LocaleContextValue`

**Internal Logic:**
1. `LocaleProvider` keeps `preference` in `useState<Language | null>(readStoredLanguage)`.
2. It memoizes `resolveLocale({ tenantCountry: country ?? null, userLanguage: preference })`.
3. It memoizes `t` as `(key, params) => t(key, resolved.language, params)`.
4. `setLanguage` calls `setPreference(language)` first and `storeLanguage(language)` second, so a storage failure cannot leave the interface on the previous language.
5. `useLocale` reads the context and throws `new Error('useLocale must be used within LocaleProvider')` when the context is `null`.
**Dependencies:** `frontend/src/i18n/locale.ts` and `frontend/src/i18n/messages.ts`.
**Error Handling:** `useLocale` outside the provider is a programming error and surfaces as a thrown `Error` naming the missing provider. `LocaleProvider` never throws for a bad country or a bad preference; resolution degrades to `pt-BR`.

### Component: locale-aware formatters

**Purpose:** One implementation of date, number, currency and percent formatting for the whole frontend.
**Interface:** `frontend/src/utils/format.ts` exports `const DEFAULT_LOCALE = 'pt-BR'`, `function formatDate(value: string | Date, locale?: string): string`, `function formatDateTime(value: string | Date, locale?: string): string`, `function formatNumber(value: number, locale?: string, options?: Intl.NumberFormatOptions): string`, `function formatCurrency(value: number, currency: string, locale?: string): string`, `function formatPercent(value: number, locale?: string, options?: Intl.NumberFormatOptions): string`.
**Internal Logic:**
1. `formatDate` builds `new Date(value)`, returns the empty string when `Number.isNaN(date.getTime())`, otherwise `new Intl.DateTimeFormat(locale ?? DEFAULT_LOCALE, { year: 'numeric', month: '2-digit', day: '2-digit' }).format(date)`.
2. `formatDateTime` uses the same guard with `{ year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }`.
3. `formatNumber` returns the empty string when `Number.isNaN(value)`, otherwise `new Intl.NumberFormat(locale ?? DEFAULT_LOCALE, options).format(value)`.
4. `formatCurrency` delegates to `formatNumber` with `{ style: 'currency', currency }`.
5. `formatPercent` delegates to `formatNumber` with `{ style: 'percent' }` merged with `options`.
**Dependencies:** the runtime `Intl` implementation; no other module.
**Error Handling:** Invalid input returns the empty string and never throws. An invalid `currency` code propagates the `RangeError` raised by `Intl`, because it is a programming error that must surface during development.

### Component: localized error map and lookup

**Purpose:** Single map from API error code to a message in the active language.
**Interface:** `frontend/src/api/errorMessages.ts` exports `const ERROR_MESSAGES: Record<string, Record<Language, string>>`. `frontend/src/api/errors.ts` keeps `function apiErrorCode(error: unknown): string | null` unchanged and exports `function codeMessage(code: string | null, fallback: string, language?: Language): string` and `function apiErrorMessage(error: unknown, fallback: string, language?: Language): string`.
**Internal Logic:**
1. `ERROR_MESSAGES` holds every code currently present in `CODE_MESSAGES` of `frontend/src/api/errors.ts`, each with `pt`, `en` and `es` text.
2. `codeMessage` returns `fallback` when `code` is `null`; otherwise it looks up `ERROR_MESSAGES[code]?.[language ?? 'pt']` and returns it when non-empty.
3. When the code is unknown, `codeMessage` returns `code` itself if `code.includes(' ')` and `fallback` otherwise.
4. `apiErrorMessage` is `codeMessage(apiErrorCode(error), fallback, language)`.
**Dependencies:** `frontend/src/i18n/locale.ts` for `Language`; `axios` for `apiErrorCode`.
**Error Handling:** A missing translation for the requested language falls back to the `pt` entry of the same code; a completely unknown code never returns a raw identifier.

### Component: empty state

**Purpose:** The third screen state, shared by every list screen.
**Interface:** `frontend/src/components/EmptyState.tsx` exports `function EmptyState(props: { title?: string; message?: string; action?: ReactNode })`.
**Internal Logic:**
1. Calls `useLocale()` to obtain `language`.
2. Renders `title ?? t('list.emptyTitle', language)` inside a heading element and `message ?? t('common.empty', language)` inside a paragraph.
3. Renders `action` after the paragraph when supplied.
4. Sets `role="status"` so an empty result is announced.
**Dependencies:** `frontend/src/i18n/LocaleContext.tsx` and `frontend/src/i18n/messages.ts`.
**Error Handling:** With both props absent the component renders the catalog defaults; it never renders an empty container.

### Component: localized load error

**Purpose:** Keeps the existing mandatory error state of every data screen, with its text taken from the catalog.
**Interface:** `frontend/src/components/LoadError.tsx` keeps `function LoadError(props: { message: string; onRetry?: () => void })`.
**Internal Logic:**
1. Calls `useLocale()` for `t` and `language`.
2. Renders the `message` prop when supplied, otherwise `t('common.error', language)`.
3. Renders the retry button labelled `t('common.retry', language)`.
**Dependencies:** `frontend/src/components/Button.tsx`, `lucide-react`, the locale context.
**Error Handling:** An absent `message` uses the catalog text; an absent `onRetry` hides the retry button, preserving the current behaviour.

### Component: global error boundary

**Purpose:** Prevents a render throw from blanking the application.
**Interface:** `frontend/src/components/ErrorBoundary.tsx` exports `class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState>` where `ErrorBoundaryProps = { children: ReactNode; fallback?: ReactNode }` and `ErrorBoundaryState = { error: Error | null }`.
**Internal Logic:**
1. `state = { error: null }`.
2. `static getDerivedStateFromError(error: Error)` returns `{ error }`.
3. `componentDidCatch(error: Error, info: ErrorInfo)` writes one `console.error` call inside a `try`/`catch`.
4. `render` returns `this.props.children` when the error state is `null`.
5. Otherwise it returns the `fallback` prop when supplied; without it, a section with `role="alert"` containing the catalog title, the catalog body and a reload button whose handler is `window.location.reload()`.
6. The fallback text is produced by `t(key, readStoredLanguage() ?? DEFAULT_LANGUAGE)` because a class component cannot call a hook; the boundary therefore never depends on context that may have thrown.
**Dependencies:** `frontend/src/i18n/messages.ts`, `frontend/src/i18n/locale.ts`, `frontend/src/components/Button.tsx`.
**Error Handling:** A throw inside `componentDidCatch` is caught locally so the boundary is not re-entered. A missing persisted preference degrades to `pt`. The reload handler holds no other side effect.

### Component: focus trap helper

**Purpose:** Pure keyboard-cycle arithmetic and the focusable-element selector used by the modal.
**Interface:** `frontend/src/utils/focusTrap.ts` exports `const FOCUSABLE_SELECTOR: string` and `function nextFocusIndex(currentIndex: number, length: number, shift: boolean): number`.
**Internal Logic:**
1. `nextFocusIndex` returns `0` when `length <= 0`.
2. With `shift === false` it returns `(currentIndex + 1) % length`.
3. With `shift === true` it returns `(currentIndex - 1 + length) % length`.
4. `FOCUSABLE_SELECTOR` is `'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'`.
**Dependencies:** none.
**Error Handling:** An out-of-range `currentIndex` is normalized by the modulo operation; an empty list returns `0`, which the caller treats as "nothing to focus".

### Component: accessible shared controls

**Purpose:** Guarantees accessible names, invalid state and keyboard operation once for every screen.
**Interface:**
- `frontend/src/components/Input.tsx`: `interface InputProps extends InputHTMLAttributes<HTMLInputElement> { label?: string; error?: string }`; `function Input(props: InputProps)`.
- `frontend/src/components/Select.tsx`: the same shape over `SelectHTMLAttributes<HTMLSelectElement>`; `function Select(props: SelectProps)`.
- `frontend/src/components/Modal.tsx`: `function Modal(props: ModalProps)` keeps the current props.
- `frontend/src/components/Button.tsx`: `interface ButtonProps` gains `ariaLabel?: string`.

**Internal Logic:**
1. `Input` computes `const fieldId = props.id ?? useId()` and `const errorId = '${fieldId}-error'`.
2. The label element receives `htmlFor={fieldId}`; the input receives `id={fieldId}`, `aria-invalid={error ? true : undefined}` and `aria-describedby={error ? errorId : undefined}`; the error paragraph receives `id={errorId}`.
3. `Select` applies the same id, `htmlFor`, `aria-invalid` and `aria-describedby` rules.
4. `Button` renders `aria-label={ariaLabel}` before spreading the remaining props, so an explicit `ariaLabel` wins and a plain text button is unaffected.
5. `Modal` keeps a `dialogRef`, a `previousFocusRef` and a `titleId` produced by `useId`.
6. On open, an effect stores `document.activeElement` in `previousFocusRef` and focuses the first element matched by `FOCUSABLE_SELECTOR` inside the dialog, or the dialog itself when none matches.
7. A `keydown` listener on the dialog calls `onClose` on `Escape`, and on `Tab` computes `nextFocusIndex` over the matched elements and focuses that element.
8. On cleanup the effect restores focus to `previousFocusRef.current` when that node is still connected, otherwise to `document.body`, and removes the listener.
9. The dialog container receives `role="dialog"`, `aria-modal="true"` and `aria-labelledby={titleId}`; the title element receives `id={titleId}`.
**Dependencies:** `frontend/src/utils/focusTrap.ts` and `frontend/src/i18n/LocaleContext.tsx` for the cancel and confirm labels.
**Error Handling:** An empty focusable set leaves focus on the dialog container. A detached previously focused node falls back to `document.body`. The `keydown` listener is removed on cleanup, so a closed modal never intercepts `Escape`.

### Component: contrast helper and declared pairs

**Purpose:** Deterministic WCAG 2.1 contrast computation over the declared colour pairs.
**Interface:** `frontend/src/utils/contrast.ts` exports `const AA_TEXT_MIN = 4.5`, `function relativeLuminance(hex: string): number` and `function contrastRatio(foreground: string, background: string): number`. `frontend/src/config/contrastPairs.ts` exports `interface ContrastPair { name: string; foreground: string; background: string }` and `const TEXT_CONTRAST_PAIRS: readonly ContrastPair[]`.
**Internal Logic:**
1. `relativeLuminance` validates `hex` against `/^#[0-9a-fA-F]{6}$/`, converts each channel with `c / 255`, applies `c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4`, and returns `0.2126 * r + 0.7152 * g + 0.0722 * b`.
2. `contrastRatio` returns `(lighter + 0.05) / (darker + 0.05)`.
3. `TEXT_CONTRAST_PAIRS` declares four pairs with literal hex: `body-on-page` (`#374151` on `#FFFFFF`), `heading-on-card` (`#111827` on `#FFFFFF`), `danger-on-danger-surface` (`#991B1B` on `#FEF2F2`) and `error-text-on-card` (`#DC2626` on `#FFFFFF`). Their computed ratios are `10.30`, `17.74`, `7.59` and `4.83`, all above `AA_TEXT_MIN`.
**Dependencies:** none.
**Error Handling:** A malformed hex value throws `RangeError` naming the offending value; the helper never returns a silently wrong ratio.

## Data Flow

**Bootstrap.** `frontend/src/App.tsx` renders `<ErrorBoundary>` as the outermost element, then `<LocaleProvider>`, then the existing `<AuthProvider>`, `<BrowserRouter>` and `<Toaster>`. `LocaleProvider` reads the persisted language once at mount and resolves `{ language, locale }`.

**Render-time locale access.** Any component calls `useLocale()`, receives the memoized `{ language, locale, t, setLanguage }` and formats through `formatDate(value, locale)`, `formatNumber(value, locale)` or `apiErrorMessage(error, fallback, language)`.

**Error path.** A rejected request reaches a screen; the screen calls `apiErrorMessage(error, fallback, language)`; `errors.ts` extracts the code with `apiErrorCode`, `codeMessage` looks it up in `ERROR_MESSAGES[code][language]`, and `LoadError` renders the result inside `role="alert"`.

**Screen state path.** A screen keeps one state value per request: `loading`, `failed` or `ready(items)`. `loading` renders `Skeleton`, `failed` renders `LoadError`, `ready` with zero items renders `EmptyState`, `ready` with items renders the table. Exactly one branch renders per pass.

**Render-throw path.** A throw inside the boundary subtree propagates to `getDerivedStateFromError`, the boundary state becomes the error, and `render` returns the localized fallback. The reload button calls `window.location.reload()`.

**Keyboard path.** A modal opens, stores `document.activeElement` and focuses the first focusable descendant. `Tab` and `Shift+Tab` are computed by `nextFocusIndex` and refocus inside the dialog. `Escape` calls `onClose`. On close, focus returns to the stored element or to `document.body`.

**Preference change path.** `setLanguage(language)` updates state and then calls `storeLanguage(language)`; the value applies to the current session even when the storage write fails.

## Data Structures

```ts
type Language = 'pt' | 'en' | 'es';

interface ResolvedLocale {
  language: Language;
  locale: string;
}

interface LocaleResolutionInput {
  tenantCountry?: string | null;
  userLanguage?: string | null;
}

type MessageKey =
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
  | 'language.label';

type MessageCatalog = Record<Language, Record<MessageKey, string>>;

type ErrorMessageCatalog = Record<string, Record<Language, string>>;

interface ContrastPair {
  name: string;
  foreground: string;
  background: string;
}
```

Resolution table:

| tenantCountry | stored language | language | locale |
|---|---|---|---|
| BR | null | pt | pt-BR |
| BR | pt | pt | pt-BR |
| BR | en | en | en-US |
| BR | es | es | es-ES |
| PT | null | pt | pt-PT |
| PT | pt | pt | pt-PT |
| PT | en | en | en-US |
| US | null | en | en-US |
| US | pt | pt | pt-BR |
| unknown or empty | a supported language | that language | its default locale |
| any | an unsupported value | country default | country default locale |

## File / Module Layout

New files:

- `frontend/src/i18n/locale.ts` — language union, resolution table, storage read and write.
- `frontend/src/i18n/messages.ts` — the PT, EN and ES catalog and the `t` function.
- `frontend/src/i18n/LocaleContext.tsx` — `LocaleProvider` and `useLocale`.
- `frontend/src/api/errorMessages.ts` — the single code to message map.
- `frontend/src/components/EmptyState.tsx` — the shared empty state.
- `frontend/src/components/ErrorBoundary.tsx` — the global boundary.
- `frontend/src/utils/focusTrap.ts` — the focus cycle helper and the focusable selector.
- `frontend/src/utils/contrast.ts` — WCAG luminance and ratio.
- `frontend/src/config/contrastPairs.ts` — the four declared text pairs.
- `frontend/tests/i18nLocale.test.ts` — locale resolution and storage tests.
- `frontend/tests/i18nMessages.test.ts` — catalog and error map parity tests.
- `frontend/tests/i18nFormat.test.ts` — locale-aware formatting tests.
- `frontend/tests/i18nScreenStates.test.ts` — screen state and locale-tag guard tests.
- `frontend/tests/accessibility.test.ts` — keyboard, labels, boundary wiring and contrast tests.

Modified files:

- `frontend/src/utils/format.ts` — the locale parameter and the number, currency and percent formatters.
- `frontend/src/api/errors.ts` — the language parameter and the import of `ERROR_MESSAGES`.
- `frontend/src/components/Input.tsx` — generated id, `htmlFor`, `aria-invalid`, `aria-describedby`.
- `frontend/src/components/Select.tsx` — the same id and assistive-technology wiring.
- `frontend/src/components/Button.tsx` — the `ariaLabel` prop.
- `frontend/src/components/Modal.tsx` — dialog semantics, focus management and `Escape`.
- `frontend/src/components/LoadError.tsx` — catalog text through `useLocale`.
- `frontend/src/App.tsx` — `ErrorBoundary` and `LocaleProvider` mounted around the tree.
- `frontend/src/pages/admin/EnrollmentList.tsx` — `EmptyState` for an empty collection.
- `frontend/src/pages/admin/StudentList.tsx` — `EmptyState` for an empty collection.
- `frontend/src/pages/admin/NotificationList.tsx` — `EmptyState` for an empty collection.
- `frontend/src/pages/parent/NotificationInbox.tsx` — `EmptyState` for an empty collection.
- `frontend/src/pages/admin/UserManagement.tsx` — `EmptyState` for an empty collection.
- `frontend/src/pages/admin/DocumentVerification.tsx` — `EmptyState` for an empty collection.
- `frontend/tests/validationAndFormat.test.ts` — the locale-aware formatting assertions.
- `frontend/tests/errors.test.ts` — the localized lookup assertions.

## Testing Strategy

The project runner is `node --test --experimental-test-coverage --test-coverage-lines=60 "tests/**/*.test.ts"` inside `frontend/`, with no DOM library, so the tests combine direct assertions over pure functions with literal source inspection of component files.

- `frontend/tests/i18nLocale.test.ts` proves `languageForCountry` for `BR`, `US`, `PT` and an unknown code; `resolveLocale` for every row of the resolution table, including `PT` plus `pt` yielding `pt-PT` and `BR` plus `en` yielding `en-US`; `isLanguage` rejecting `'fr'`, an empty string and `null`; `readStoredLanguage` returning `null` with a throwing storage and with an unparsable value; `storeLanguage` not throwing when the write throws.
- `frontend/tests/i18nMessages.test.ts` enumerates `MESSAGES`, asserts the three language records share exactly one key set, asserts no value is empty or whitespace-only, asserts every placeholder appearing in any language exists in all three, asserts `t` interpolation and the untouched-placeholder contract, and asserts `ERROR_MESSAGES` parity per code plus `codeMessage` resolution for `pt`, `en` and `es` and the unknown-code rules.
- `frontend/tests/i18nFormat.test.ts` asserts `formatDate` output for `pt-BR`, `en-US` and `pt-PT` on one fixed instant, asserts `formatNumber` and `formatCurrency` differ per locale, asserts the no-locale call reproduces the `pt-BR` result, and asserts the empty string for an invalid date and for `NaN`.
- `frontend/tests/i18nScreenStates.test.ts` reads each of the six declared list screens, asserts it imports `EmptyState` and renders it, reads `frontend/src/components/LoadError.tsx` and asserts it calls `useLocale` and `t`, and asserts no page module under `frontend/src/pages/` contains the literal locale tag.
- `frontend/tests/accessibility.test.ts` asserts `nextFocusIndex` wrap-around in both directions and the empty-list case; reads `Modal.tsx` for `role="dialog"`, `aria-modal`, `aria-labelledby`, `Escape`, `nextFocusIndex` and the focus restore call; reads `ErrorBoundary.tsx` for `getDerivedStateFromError`, the two catalog keys and the reload call; reads `Input.tsx` and `Select.tsx` for `htmlFor`, `aria-invalid` and `aria-describedby`; reads `Button.tsx` for `ariaLabel`; asserts `contrastRatio` gives `21` for black on white and `1` for a colour against itself, asserts the `RangeError` for a malformed hex value, and asserts every entry of `TEXT_CONTRAST_PAIRS` reaches at least `AA_TEXT_MIN`.

`npm run build` inside `frontend/` runs the TypeScript project build and the Vite build; `npm run lint` runs `oxlint`; `npm test` runs the whole suite.

## Migration / Rollback

No data migration: every new value is derived at render time, and the only persistence is the optional language key.

Migration is incremental because of AD-190-5: `formatDate(value)` and `codeMessage(code, fallback)` keep their previous output, so the spec ships before any screen passes an explicit locale. Screens that adopt `useLocale()` gain the tenant locale; screens that do not are unchanged.

Rollback is a plain revert: removing the `LocaleProvider` and `ErrorBoundary` wrappers from `frontend/src/App.tsx` restores the previous tree, because no other module depends on the provider to render. The catalog and map modules become inert once no caller passes a language. The screen-state and accessibility changes are independent of the locale layer and revert per file.
