<!-- PIPELINE-CONTRACT: Task completion is determined by "**Status:** [x] done" at the end of each "### Task N" section.
     Checkboxes "- [ ]" under "**Validation:**" are human verification guides — the pipeline ignores them completely.
     To mark a task as done, add "**Status:** [x] done". To mark it as pending, use "**Status:** [ ] pending". -->
# Spec 190: experiencia-i18n-e-acessibilidade — Implementation Tasks

## Planned Scaffolds

- `frontend/src/i18n/locale.ts`
- `frontend/src/i18n/messages.ts`
- `frontend/src/i18n/LocaleContext.tsx`
- `frontend/src/api/errorMessages.ts`
- `frontend/src/components/EmptyState.tsx`
- `frontend/src/components/ErrorBoundary.tsx`
- `frontend/src/utils/focusTrap.ts`
- `frontend/src/utils/contrast.ts`
- `frontend/src/config/contrastPairs.ts`
- `frontend/tests/i18nLocale.test.ts`
- `frontend/tests/i18nMessages.test.ts`
- `frontend/tests/i18nFormat.test.ts`
- `frontend/tests/i18nScreenStates.test.ts`
- `frontend/tests/accessibility.test.ts`

## Task Order

Tasks MUST be executed sequentially in numerical order. Each task depends on the completion of all preceding tasks.

## Tasks

### Task 1: Locale resolver, message catalog and provider

**Objective:** Create the locale resolution module, the PT/EN/ES message catalog and the React provider, and mount the provider in the application root.
**Requirements Covered:** FR-001, FR-002
**Design References:** `## Component / Module Breakdown` sections `locale resolver`, `message catalog` and `locale context and provider`; `## Data Structures` resolution table.
**Actions:**
1. Create `frontend/src/i18n/locale.ts` with `type Language`, `LANGUAGE_ORDER`, `DEFAULT_LANGUAGE`, `DEFAULT_LOCALE`, `LANGUAGE_STORAGE_KEY`, `ResolvedLocale`, `LocaleResolutionInput`, `isLanguage`, `languageForCountry`, `defaultLocaleForLanguage`, `resolveLocale`, `readStoredLanguage` and `storeLanguage`, implementing the three mapping tables and the resolution rule of AD-190-2.
2. Create `frontend/src/i18n/messages.ts` with `type MessageKey`, the frozen `MESSAGES` object for `pt`, `en` and `es` carrying the eleven keys listed in the design, and `t(key, language, params)` applying the `{name}` substitution.
3. Create `frontend/src/i18n/LocaleContext.tsx` with `LocaleContextValue`, `LocaleProvider({ country, children })` and `useLocale()`, storing the preference in state, resolving through `resolveLocale`, memoizing `t`, and calling `setPreference` before `storeLanguage` inside `setLanguage`.
4. Modify `frontend/src/App.tsx` so `<LocaleProvider>` wraps the existing `<AuthProvider>` tree.
5. Create `frontend/tests/i18nLocale.test.ts` asserting the full resolution table, `isLanguage` rejection cases, and the storage read and write failure paths with a throwing fake storage.
6. Create `frontend/tests/i18nMessages.test.ts` asserting the shared key set across the three languages, the absence of empty values, placeholder parity, `t` interpolation and the untouched-placeholder contract.
**Expected Files:**
- `frontend/src/i18n/locale.ts`
- `frontend/src/i18n/messages.ts`
- `frontend/src/i18n/LocaleContext.tsx`
- `frontend/src/App.tsx`
- `frontend/tests/i18nLocale.test.ts`
- `frontend/tests/i18nMessages.test.ts`
**Validation:** Run `npx tsc -b` inside `frontend/` and `npm test` from `frontend/`; `frontend/tests/i18nLocale.test.ts` and `frontend/tests/i18nMessages.test.ts` pass, and `frontend/src/App.tsx` renders `<LocaleProvider>`.
**Status:** [x] done

### Task 2: Locale-aware date, number, currency and percent formatters

**Objective:** Extend the single formatting owner so every formatter takes an explicit locale and defaults to the pre-spec `pt-BR` output.
**Requirements Covered:** FR-003, NFR-002
**Design References:** `## Component / Module Breakdown` section `locale-aware formatters`.
**Actions:**
1. Rewrite `frontend/src/utils/format.ts` to export `DEFAULT_LOCALE`, `formatDate`, `formatDateTime`, `formatNumber`, `formatCurrency` and `formatPercent` with the locale parameter defaulting to `DEFAULT_LOCALE`, the `Number.isNaN` guard returning the empty string, and the `Intl.DateTimeFormat` and `Intl.NumberFormat` implementations described in the design.
2. Extend `frontend/tests/validationAndFormat.test.ts` with assertions that the one-argument calls keep the previous `pt-BR` output.
3. Create `frontend/tests/i18nFormat.test.ts` asserting distinct date, number and currency output for `pt-BR`, `en-US` and `pt-PT` on one fixed instant and one fixed amount, and asserting the empty string for an invalid date and for `NaN`.
**Expected Files:**
- `frontend/src/utils/format.ts`
- `frontend/tests/validationAndFormat.test.ts`
- `frontend/tests/i18nFormat.test.ts`
**Validation:** Run `npx tsc -b` inside `frontend/` and `npm test` from `frontend/`; `frontend/tests/i18nFormat.test.ts` and `frontend/tests/validationAndFormat.test.ts` pass.
**Status:** [x] done

### Task 3: Localized API error-code map and lookup

**Objective:** Move the single code-to-message map into a language-keyed module and teach the error lookup to resolve the active language.
**Requirements Covered:** FR-004
**Design References:** `## Component / Module Breakdown` section `localized error map and lookup`; AD-190-4 and AD-190-5.
**Actions:**
1. Create `frontend/src/api/errorMessages.ts` with `ERROR_MESSAGES: Record<string, Record<Language, string>>`, carrying every code present today in `CODE_MESSAGES` of `frontend/src/api/errors.ts`, each with `pt`, `en` and `es` text.
2. Modify `frontend/src/api/errors.ts` to import `ERROR_MESSAGES`, delete the local `CODE_MESSAGES` constant, and add the optional trailing `language?: Language` parameter to `codeMessage` and `apiErrorMessage`, keeping the null-code, known-code, readable-title and unknown-code rules unchanged.
3. Extend `frontend/tests/errors.test.ts` with the localized lookup assertions for `pt`, `en` and `es`.
4. Extend `frontend/tests/i18nMessages.test.ts` with the `ERROR_MESSAGES` parity assertion, failing when any code lacks one of the three translations or carries an empty value.
**Expected Files:**
- `frontend/src/api/errorMessages.ts`
- `frontend/src/api/errors.ts`
- `frontend/tests/errors.test.ts`
- `frontend/tests/i18nMessages.test.ts`
**Validation:** Run `npx tsc -b` inside `frontend/` and `npm test` from `frontend/`; `frontend/tests/errors.test.ts` and `frontend/tests/i18nMessages.test.ts` pass, and `frontend/src/api/errors.ts` no longer declares `CODE_MESSAGES`.
**Status:** [x] done

### Task 4: Shared empty state and localized load error in every list screen

**Objective:** Add the missing third screen state and render it in the six declared list screens, with both state components sourcing text from the catalog.
**Requirements Covered:** FR-005, NFR-001
**Design References:** `## Component / Module Breakdown` sections `empty state` and `localized load error`; `## Data Flow` screen state path.
**Actions:**
1. Create `frontend/src/components/EmptyState.tsx` with the `EmptyState({ title, message, action })` signature, the `useLocale` lookup and `role="status"`.
2. Modify `frontend/src/components/LoadError.tsx` to call `useLocale()` and render the fallback message from `t('common.error', language)` and the retry label from `t('common.retry', language)`.
3. Modify each of the six declared list screens to import `EmptyState` and render it instead of the table when the resolved collection has zero items, keeping `Skeleton` for the loading branch and `LoadError` for the failed branch.
4. Create `frontend/tests/i18nScreenStates.test.ts` reading each of the six screens, asserting the `EmptyState` import and render call, asserting `frontend/src/components/LoadError.tsx` calls `useLocale` and `t`, and asserting that no module under `frontend/src/pages/` contains the literal locale tag.
**Expected Files:**
- `frontend/src/components/EmptyState.tsx`
- `frontend/src/components/LoadError.tsx`
- `frontend/src/pages/admin/EnrollmentList.tsx`
- `frontend/src/pages/admin/StudentList.tsx`
- `frontend/src/pages/admin/NotificationList.tsx`
- `frontend/src/pages/parent/NotificationInbox.tsx`
- `frontend/src/pages/admin/UserManagement.tsx`
- `frontend/src/pages/admin/DocumentVerification.tsx`
- `frontend/tests/i18nScreenStates.test.ts`
**Validation:** Run `npx tsc -b` inside `frontend/` and `npm test` from `frontend/`; `frontend/tests/i18nScreenStates.test.ts` passes and no page module contains the literal `pt-BR` tag.
**Status:** [x] done

### Task 5: Global error boundary

**Objective:** Create the boundary that keeps the application mounted when a render throw occurs, and mount it around the routed tree.
**Requirements Covered:** FR-006
**Design References:** `## Component / Module Breakdown` section `global error boundary`; AD-190-9; `## Data Flow` render-throw path.
**Actions:**
1. Create `frontend/src/components/ErrorBoundary.tsx` with `ErrorBoundaryProps`, `ErrorBoundaryState`, `static getDerivedStateFromError`, a guarded `componentDidCatch`, the `fallback` prop branch and the default `role="alert"` fallback whose text comes from `t('errorBoundary.title', readStoredLanguage() ?? DEFAULT_LANGUAGE)` and `t('errorBoundary.body', readStoredLanguage() ?? DEFAULT_LANGUAGE)` with a reload button calling `window.location.reload`.
2. Modify `frontend/src/App.tsx` so `<ErrorBoundary>` is the outermost element around the existing `<LocaleProvider>` tree.
3. Create `frontend/tests/accessibility.test.ts` with the boundary assertions: `getDerivedStateFromError`, both catalog keys and the reload call present in `frontend/src/components/ErrorBoundary.tsx`, and `ErrorBoundary` mounted in `frontend/src/App.tsx`.
**Expected Files:**
- `frontend/src/components/ErrorBoundary.tsx`
- `frontend/src/App.tsx`
- `frontend/tests/accessibility.test.ts`
**Validation:** Run `npx tsc -b` inside `frontend/` and `npm test` from `frontend/`; the boundary assertions in `frontend/tests/accessibility.test.ts` pass.
**Status:** [x] done

### Task 6: Accessible labels, focus and keyboard navigation in shared controls

**Objective:** Guarantee accessible names, invalid-state exposure and keyboard operation once in the four shared controls.
**Requirements Covered:** FR-007
**Design References:** `## Component / Module Breakdown` sections `focus trap helper` and `accessible shared controls`; AD-190-6.
**Actions:**
1. Create `frontend/src/utils/focusTrap.ts` with `FOCUSABLE_SELECTOR` and `nextFocusIndex(currentIndex, length, shift)` implementing the wrap-around rule and the empty-list case.
2. Modify `frontend/src/components/Input.tsx` to derive `fieldId` from `props.id ?? useId()`, link the label with `htmlFor`, and set `id`, `aria-invalid` and `aria-describedby` on the input plus `id` on the error paragraph.
3. Modify `frontend/src/components/Select.tsx` with the same id, `htmlFor`, `aria-invalid` and `aria-describedby` wiring.
4. Modify `frontend/src/components/Button.tsx` to add `ariaLabel?: string` to `ButtonProps` and render `aria-label={ariaLabel}` before the prop spread.
5. Modify `frontend/src/components/Modal.tsx` to add the dialog ref, the previously-focused ref, the `useId` title id, the open effect that focuses the first focusable element, the `keydown` handler using `nextFocusIndex` for `Tab` and closing on `Escape`, the cleanup that restores focus, and the `role`, `aria-modal` and `aria-labelledby` attributes.
6. Extend `frontend/tests/accessibility.test.ts` with the `nextFocusIndex` wrap-around assertions and the source assertions over `Input.tsx`, `Select.tsx`, `Button.tsx` and `Modal.tsx`.
**Expected Files:**
- `frontend/src/utils/focusTrap.ts`
- `frontend/src/components/Input.tsx`
- `frontend/src/components/Select.tsx`
- `frontend/src/components/Button.tsx`
- `frontend/src/components/Modal.tsx`
- `frontend/tests/accessibility.test.ts`
**Validation:** Run `npx tsc -b` inside `frontend/` and `npm test` from `frontend/`; the keyboard and label assertions in `frontend/tests/accessibility.test.ts` pass.
**Status:** [x] done

### Task 7: Contrast helper and declared text pairs

**Objective:** Provide deterministic WCAG 2.1 contrast computation and assert the declared text pairs against the minimum ratio.
**Requirements Covered:** FR-008
**Design References:** `## Component / Module Breakdown` section `contrast helper and declared pairs`; AD-190-8.
**Actions:**
1. Create `frontend/src/utils/contrast.ts` with `AA_TEXT_MIN`, `relativeLuminance(hex)` and `contrastRatio(foreground, background)`, including the `#rrggbb` validation that throws `RangeError`.
2. Create `frontend/src/config/contrastPairs.ts` with `ContrastPair` and `TEXT_CONTRAST_PAIRS` holding the four pairs and their literal hex values.
3. Extend `frontend/tests/accessibility.test.ts` with the known-value assertions, the `RangeError` assertion for a malformed hex value, and the loop asserting every declared pair reaches at least `AA_TEXT_MIN`.
**Expected Files:**
- `frontend/src/utils/contrast.ts`
- `frontend/src/config/contrastPairs.ts`
- `frontend/tests/accessibility.test.ts`
**Validation:** Run `npx tsc -b` inside `frontend/` and `npm test` from `frontend/`; the contrast assertions in `frontend/tests/accessibility.test.ts` pass and every declared pair is at or above `4.5`.
**Status:** [x] done
