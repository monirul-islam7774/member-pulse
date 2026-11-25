# MemberPulse Dashboard Web App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A browser UI for MemberPulse staff: sign in, a KPI dashboard, and screens for members, segments, campaigns and admin tasks, served by the existing API container.

**Architecture:** A React + TypeScript + Vite single-page app in `web/` calls the existing JSON API with a bearer token. The API serves the built files from `wwwroot` (same origin, no CORS) with a client-route fallback that never swallows `/api`, `/health` or `/openapi`. Two small read endpoints are added to the API.

**Tech Stack:** React 19, TypeScript, Vite, React Router, Recharts, Vitest + Testing Library (jsdom), plain CSS; .NET 10 / xUnit for the API additions; Node 22 in Docker and CI.

**Spec:** `docs/superpowers/specs/2026-11-25-dashboard-web-app-design.md`

## Global Constraints

- API contract unchanged: JSON, camelCase, enums as strings (e.g. `"Debit"`, `"Sent"`), `Authorization: Bearer <token>`; paged results are `{ items, page, pageSize, totalCount }`; errors are RFC 7807 problem details (`title`, `detail`, `errors`).
- Tokens last 60 minutes with no refresh: any `401` from a non-login call ends the session and routes to `/login`.
- Token stored in `sessionStorage` only, never in a URL, `localStorage` or logs.
- Dev: Vite proxies `/api` and `/health` to `http://localhost:5091`. Production: same origin, no CORS.
- Existing .NET suite must stay green (100 normal tests, 6 integration tests); CI jobs `build-and-test`, `docker-build`, `integration-tests` stay green.
- All user-supplied text (names, emails, descriptions, message templates) is rendered as text, never as HTML.
- Currency formatting is USD (`Intl.NumberFormat`), dates in the viewer's locale/timezone; both are my calls, change in one place (`web/src/format.ts`).
- Light and dark themes follow `prefers-color-scheme`; charts use colours that are distinguishable in both.
- Every list/detail screen has loading, empty and error states.
- Node 22 in `Dockerfile` and CI; local dev uses whatever Node >= 22 is installed.

## Review Focus

1. A token expires while a form is open (e.g. posting a transaction): the user lands on sign-in with a "session expired" note, and nothing is silently lost or double-submitted. (Task 3)
2. Login failures: wrong password, locked account and unknown email all show the same generic message; a `429` shows "too many attempts, wait a minute"; a network failure shows a connection error, not a crash. (Task 3)
3. Empty and zero data: no campaigns, a campaign with 0 sent (rate 0%, no `NaN`/`Infinity`), a member with no accounts or transactions, an empty segment. (Tasks 4, 5, 6, 7)
4. Refreshing or deep-linking a client route (`/campaigns/<id>`) works, while an unknown `/api/...` path still returns the API's own `401`/`404`, not `index.html`. (Task 2)
5. Names or descriptions containing `<script>` / HTML render as literal text; a non-admin who opens `/admin` sees a forbidden page without calling admin endpoints. (Tasks 5, 8)

---

### Task 1: API additions for the dashboard

**Files:**
- Modify: `src/MemberPulse.Api/Controllers/ReportsController.cs`, `src/MemberPulse.Api/Contracts/ReportContracts.cs`
- Modify: `src/MemberPulse.Api/Controllers/AuthController.cs`, `src/MemberPulse.Api/Contracts/AuthContracts.cs`
- Test: `tests/MemberPulse.Tests/Api/DashboardEndpointsTests.cs`

**Interfaces:**
- Produces `GET /api/reports/campaigns/daily?from=&to=` -> `DailyOverviewDto(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<DailyKpiDto> Daily)`; same default range (last 30 days), same range validation (400 if from > to or > 366 days) as the existing overview; days are UTC, bucketed by message send time, `Daily` ordered ascending and containing only days that had messages. `DailyKpiDto(DateOnly Date, int Sent, int Converted)` already exists.
- Produces `GET /api/users` (Admin policy) -> `IReadOnlyList<StaffUserSummaryDto>` where `StaffUserSummaryDto(Guid Id, string Email, StaffRole Role, DateTimeOffset CreatedAt, bool IsLockedOut, DateTimeOffset? LockedUntil)`, ordered by email.
- The literal route `daily` must not collide with `{id:guid}`.

- [ ] **Step 1: Write failing tests** in `DashboardEndpointsTests` (use `MemberPulseApiFactory`; create deliveries the way `ConversionsApiTests` does):
  `Daily_overview_sums_sent_and_converted_per_utc_day_across_campaigns` (two campaigns, assert the day's `Sent`/`Converted` equal the sums), `Daily_overview_rejects_reversed_and_oversized_ranges_with_400`, `Daily_overview_is_empty_when_nothing_was_sent` (`Daily` empty, 200), `Users_list_is_admin_only_and_shows_lock_state` (staff client -> 403; admin client -> 200; a user locked via 5 failed logins has `IsLockedOut == true` and `LockedUntil` set, an untouched user has both false/null).
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~DashboardEndpointsTests"`; expected: FAIL (404/compile errors).
- [ ] **Step 3: Implement** `ReportsController.DailyOverview([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken)` at `[HttpGet("daily")]` reusing `TryResolveRange` and `InRange`; group rows in memory by `DateOnly.FromDateTime(CreatedAt.UtcDateTime)`. Implement `AuthController.ListUsers` at `[HttpGet("api/users")]` with `[Authorize(Policy = AuthSetup.AdminPolicy)]`, using `StaffUser.IsLockedOut(time.GetUtcNow())`.
- [ ] **Step 4: Run** the same filter; expected: PASS. Run `dotnet test`; expected: all pass.
- [ ] **Step 5: Commit** `git add -A && git commit -m "Add daily KPI overview and staff user list endpoints"`.

### Task 2: Serve the built UI from the API

**Files:**
- Create: `src/MemberPulse.Api/Hosting/SpaHosting.cs`
- Modify: `src/MemberPulse.Api/Program.cs`
- Test: `tests/MemberPulse.Tests/Api/SpaHostingTests.cs`

**Interfaces:**
- Produces `static class SpaHosting { public static void UseSpa(this WebApplication app); }`: calls `UseDefaultFiles()`, `UseStaticFiles()`, and maps a fallback that serves `index.html` for any GET whose path does not start with `/api`, `/health` or `/openapi`, and returns 404 if there is no `index.html`. The fallback endpoint must be `.AllowAnonymous()` (the app's fallback authorization policy would otherwise demand a login to load the page). Call it in `Program.cs` after `MapControllers`/`MapHealthChecks`.

- [ ] **Step 1: Write failing tests** `SpaHostingTests` using a factory subclass that sets `UseWebRoot` to a temp folder containing `index.html` (`<html>spa</html>`) and `app.js`: `Client_routes_return_index_html_to_anonymous_users` (`GET /campaigns/abc` -> 200, body contains `spa`), `Static_files_are_served` (`GET /app.js` -> 200), `Unknown_api_paths_are_not_swallowed` (`GET /api/nope` anonymous -> 401, authenticated -> 404, never 200), `Health_still_works`, `Without_a_built_ui_client_routes_are_404` (default factory, `GET /campaigns/abc` -> 404).
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~SpaHostingTests"`; expected: FAIL.
- [ ] **Step 3: Implement** `UseSpa` as described; wire into `Program.cs`.
- [ ] **Step 4: Run** the filter then `dotnet test`; expected: PASS, all green.
- [ ] **Step 5: Commit** `git commit -am "Serve a built SPA from wwwroot with a safe client-route fallback"`.

### Task 3: Web scaffold, API client, sign-in, app shell

**Files:**
- Create: `web/package.json`, `web/tsconfig.json`, `web/vite.config.ts` (proxy + Vitest `jsdom` config), `web/index.html`, `web/.gitignore` (`node_modules`, `dist`), `web/src/main.tsx`, `web/src/App.tsx`, `web/src/styles.css`, `web/src/format.ts`, `web/src/api/types.ts`, `web/src/api/client.ts`, `web/src/auth/AuthContext.tsx`, `web/src/components/{Layout,Loading,EmptyState,ErrorState}.tsx`, `web/src/pages/{LoginPage,NotFoundPage}.tsx`
- Test: `web/src/api/client.test.ts`, `web/src/pages/LoginPage.test.tsx`, `web/src/format.test.ts`

**Interfaces:**
- Produces `types.ts`: TypeScript mirrors of the DTOs in `src/MemberPulse.Api/Contracts/*.cs` (Member, Account, Transaction, Segment+Criterion, Campaign+ConversionGoal, Delivery, Kpi, CampaignReport, CampaignDetailReport, DailyOverview, StaffUser(+Summary), DeadLetter, `Paged<T>`, `ProblemDetails`, enums as string unions).
- Produces `client.ts`: `class ApiError extends Error { status: number; problem?: ProblemDetails }`; `api.get<T>(path, query?)`, `api.post<T>(path, body?)`; attaches `Authorization`; on `401` from any path except `/api/auth/login` calls the registered `onUnauthorized()` then throws `ApiError`; non-2xx -> `ApiError` with parsed problem details when JSON; `204` -> `undefined`; network failure -> `ApiError` with `status 0`. `setAuthHandlers({ getToken, onUnauthorized })`.
- Produces `AuthContext`: `useAuth(): { user: {email, role} | null; signIn(email, password): Promise<void>; signOut(): void; sessionExpired: boolean }`; token + user in `sessionStorage`; after login it calls `GET /api/auth/me`.
- Produces `format.ts`: `formatCurrency(n)`, `formatPercent(rate)` (0..1 -> `"12.5%"`, never `NaN`), `formatDateTime(iso)`, `formatNumber(n)`.
- Produces `Layout`: left nav (Dashboard, Members, Segments, Campaigns, Admin shown only to admins), header with email and Sign out, `<Outlet/>`. Routes: `/login`, `/` (dashboard), `/members`, `/members/:id`, `/segments`, `/campaigns`, `/campaigns/:id`, `/admin`, `*`. Unauthenticated access redirects to `/login`.

- [ ] **Step 1: Scaffold** `web/` (Vite react-ts template shape), add deps `react-router-dom`, `recharts`; dev deps `vitest`, `jsdom`, `@testing-library/react`, `@testing-library/user-event`, `@testing-library/jest-dom`. Scripts: `dev`, `build` (`tsc -b && vite build`), `test` (`vitest run`).
- [ ] **Step 2: Write failing tests.** `client.test.ts`: `attaches_the_bearer_token`, `a_401_calls_onUnauthorized_and_throws_ApiError`, `a_401_from_login_does_not_call_onUnauthorized` (wrong password must not look like an expired session), `parses_problem_details_into_ApiError`, `a_network_failure_becomes_status_0`, `204_resolves_undefined`. `LoginPage.test.tsx`: `shows_one_generic_message_for_a_401`, `shows_a_wait_message_for_a_429`, `shows_a_connection_error_when_the_network_fails`, `shows_session_expired_when_redirected_after_expiry`, `disables_the_button_while_signing_in`. `format.test.ts`: `formatPercent(0) == "0.0%"`, `formatPercent(Number.NaN) == "0.0%"`, `formatCurrency(640.25) == "$640.25"`.
- [ ] **Step 3: Run** `cd web && npm test`; expected: FAIL.
- [ ] **Step 4: Implement** the interfaces above. Styling: CSS custom properties for colour/spacing/type, dark values under `@media (prefers-color-scheme: dark)`; a distinctive, data-forward look (follow the frontend-design skill; no default-template feel). Keep `format.ts` the only place with locale/currency decisions.
- [ ] **Step 5: Run** `npm test` and `npm run build`; expected: PASS, build succeeds. With the API running (`dotnet run --project src/MemberPulse.Api`, Development) run `npm run dev` and sign in as `admin@memberpulse.local` / `Admin_Dev123!`; expected: shell renders, sign-out returns to `/login`.
- [ ] **Step 6: Commit** `git add web && git commit -m "Add web app scaffold with API client, sign-in and shell"`.

### Task 4: Dashboard page

**Files:**
- Create: `web/src/api/reports.ts`, `web/src/pages/DashboardPage.tsx`, `web/src/components/{StatCard,ActivityChart}.tsx`
- Test: `web/src/pages/DashboardPage.test.tsx`

**Interfaces:**
- Consumes `api` (Task 3), Task 1 `GET /api/reports/campaigns/daily`, existing `GET /api/reports/campaigns`.
- Produces `reports.ts`: `getOverview(range?)`, `getDailyOverview(range?)`, `getCampaignReport(id, range?)` where `range = { from?: string; to?: string }` (ISO).
- Page: range selector (7 / 30 / 90 days, default 30) -> stat cards (Sent, Converted, Conversion rate, Members reached, Converted amount, plus Failed and Pending shown only when > 0), a `ActivityChart` (sent vs converted per day, accessible: titled, with a data table fallback for screen readers), and a per-campaign table (name linking to `/campaigns/:id`, sent, pending, failed, converted, rate, amount).

- [ ] **Step 1: Write failing tests** with `api` mocked: `renders_totals_and_campaign_rows`, `shows_zero_rate_not_NaN_when_nothing_was_sent`, `shows_an_empty_state_with_no_campaigns`, `changing_the_range_refetches_with_from_and_to`, `shows_an_error_state_with_retry_when_the_api_fails`.
- [ ] **Step 2: Run** `npm test -- DashboardPage`; expected: FAIL.
- [ ] **Step 3: Implement** the page and components.
- [ ] **Step 4: Run** `npm test`; expected: PASS.
- [ ] **Step 5: Commit** `git commit -am "Add dashboard page"`.

### Task 5: Members

**Files:**
- Create: `web/src/api/members.ts`, `web/src/components/{Pager,DataTable}.tsx`, `web/src/pages/{MembersPage,MemberDetailPage}.tsx`
- Test: `web/src/pages/MembersPage.test.tsx`, `web/src/pages/MemberDetailPage.test.tsx`

**Interfaces:**
- Consumes `GET /api/members`, `GET /api/members/{id}`, `GET /api/members/{id}/segments`, `GET /api/accounts/{id}/transactions`, `POST /api/accounts/{id}/transactions`, `POST /api/members`, `POST /api/members/{id}/accounts`.
- Produces `members.ts`: `listMembers(page, pageSize)`, `getMember(id)`, `getMemberSegments(id)`, `listTransactions(accountId, page, pageSize)`, `postTransaction(accountId, {amount, description, merchantCategory?})`, `createMember(...)`, `openAccount(memberId, {type, initialDeposit})`.
- Produces `Pager({page, pageSize, totalCount, onChange})` and `DataTable` (typed columns, empty and loading states) reused by Tasks 6-8.
- Members page: paged table (name, email, accounts) linking to detail; "New member" form. Detail: profile, accounts with balances, segment chips, transactions for the selected account (paged), "Open account" and "Post transaction" forms (amount sign explained: negative = purchase; shows the API's `422` detail such as "Insufficient funds" inline).

- [ ] **Step 1: Write failing tests:** `lists_members_and_pages`, `renders_html_in_names_as_text` (a member named `<img src=x onerror=alert(1)>` appears literally, no `img` element), `detail_shows_accounts_balances_and_segments`, `detail_handles_a_member_with_no_accounts`, `posting_a_transaction_shows_the_api_error_inline_on_422_and_refreshes_on_success`, `unknown_member_shows_not_found`.
- [ ] **Step 2: Run** `npm test -- Member`; expected: FAIL.
- [ ] **Step 3: Implement** pages, `members.ts`, `Pager`, `DataTable`.
- [ ] **Step 4: Run** `npm test`; expected: PASS.
- [ ] **Step 5: Commit** `git commit -am "Add members list and detail pages"`.

### Task 6: Segments

**Files:**
- Create: `web/src/api/segments.ts`, `web/src/pages/SegmentsPage.tsx`, `web/src/components/CriteriaBuilder.tsx`
- Test: `web/src/pages/SegmentsPage.test.tsx`, `web/src/components/CriteriaBuilder.test.tsx`

**Interfaces:**
- Consumes `GET /api/segments`, `POST /api/segments`, `GET /api/segments/{id}/members`.
- Produces `segments.ts`: `listSegments()`, `createSegment({name, description?, criteria})`, `listSegmentMembers(id, page, pageSize)`.
- `CriteriaBuilder({value, onChange})`: rows of metric / operator / value, plus an account-type picker only for `TotalBalance` and `AccountCount` and a merchant-category input only for `Spend30d` and `TransactionCount30d` (the API rejects other combinations); add/remove rows, at least one required.
- Page: segment list with criteria summaries ("Total balance >= 500,000"), expand to see matching members (paged), create form, `409` (name taken) shown inline.

- [ ] **Step 1: Write failing tests:** `builder_offers_scope_fields_only_for_the_metrics_that_accept_them`, `builder_requires_at_least_one_criterion`, `creating_a_segment_posts_the_expected_body`, `duplicate_name_409_shows_inline`, `shows_matching_members_and_an_empty_state_when_none_match`.
- [ ] **Step 2: Run** `npm test -- Segment`; expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `npm test`; expected: PASS.
- [ ] **Step 5: Commit** `git commit -am "Add segments page with criteria builder"`.

### Task 7: Campaigns

**Files:**
- Create: `web/src/api/campaigns.ts`, `web/src/components/StatusBadge.tsx`, `web/src/pages/{CampaignsPage,CampaignDetailPage}.tsx`
- Test: `web/src/pages/CampaignsPage.test.tsx`, `web/src/pages/CampaignDetailPage.test.tsx`

**Interfaces:**
- Consumes `GET/POST /api/campaigns`, `GET /api/campaigns/{id}`, `POST /api/campaigns/{id}/pause|resume`, `GET /api/campaigns/{id}/deliveries`, `getCampaignReport` (Task 4), `listSegments` (Task 6).
- Produces `campaigns.ts`: `listCampaigns()`, `getCampaign(id)`, `createCampaign(body)` (body fields exactly as `CreateCampaignRequest`: `name, segmentId, channel, messageTemplate, direction, minAmount?, merchantCategory?, cooldownDays, conversionWindowDays, conversionDirection, conversionMinAmount?, conversionMerchantCategory?`), `pauseCampaign(id)`, `resumeCampaign(id)`, `listDeliveries(id, page, pageSize)`.
- Produces `StatusBadge({status})` for `Active/Paused` and `Pending/Sent/Failed` with text (not colour alone).
- List page: table with status badge, segment name, channel, pause/resume buttons, create form (segment picker; template hint listing `{FirstName} {Amount} {MerchantCategory}`; conversion goal section with defaults 7 days / Debit). Detail: settings summary, the campaign's KPIs, daily chart, deliveries table (status, attempts, error, converted amount/time).

- [ ] **Step 1: Write failing tests:** `lists_campaigns_with_status_and_toggles_pause_and_resume`, `create_form_defaults_conversion_to_7_days_debit`, `create_sends_exactly_the_api_field_names`, `detail_shows_kpis_deliveries_attempts_and_errors`, `a_campaign_with_no_deliveries_shows_zero_rate_and_an_empty_state`, `message_templates_render_as_text`.
- [ ] **Step 2: Run** `npm test -- Campaign`; expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `npm test`; expected: PASS.
- [ ] **Step 5: Commit** `git commit -am "Add campaigns list and detail pages"`.

### Task 8: Admin

**Files:**
- Create: `web/src/api/admin.ts`, `web/src/pages/AdminPage.tsx`, `web/src/auth/RequireAdmin.tsx`
- Test: `web/src/pages/AdminPage.test.tsx`

**Interfaces:**
- Consumes Task 1 `GET /api/users`, `POST /api/users`, `POST /api/users/unlock`, `GET /api/admin/outbox/dead`, `POST /api/admin/outbox/{id}/retry`.
- Produces `admin.ts`: `listUsers()`, `createUser({email, password, role})`, `unlockUser(email)`, `listDeadLetters()`, `retryDeadLetter(id)`.
- `RequireAdmin` wraps the route: non-admins see a forbidden page and no admin endpoint is called.
- Page: staff table with lock state and an Unlock button; create-user form (password >= 12 chars, matches the API); dead-letter table (account key, attempts, last error, created) with Retry.

- [ ] **Step 1: Write failing tests:** `non_admins_see_forbidden_and_no_admin_calls_are_made`, `locked_users_show_until_when_and_unlock_refreshes_the_list`, `create_user_enforces_a_12_character_password_before_calling_the_api`, `dead_letters_list_and_retry_removes_the_row`, `shows_an_empty_state_when_there_are_no_dead_letters`.
- [ ] **Step 2: Run** `npm test -- Admin`; expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `npm test`; expected: PASS.
- [ ] **Step 5: Commit** `git commit -am "Add admin page"`.

### Task 9: Delivery: image, CI, docs, and real-browser verification

**Files:**
- Modify: `Dockerfile` (Node stage), `.dockerignore` (add `web/node_modules`, `web/dist`), `.github/workflows/ci.yml` (job `web`), `README.MD`
- Create: none beyond the above

**Interfaces:**
- Dockerfile: add `FROM node:22 AS web` that runs `npm ci && npm run build` in `web/`, then `COPY --from=web /web/dist /app/publish/wwwroot` in the final stage (path must match what `UseSpa` serves).
- CI job `web`: `actions/setup-node` Node 22, `npm ci`, `npm run build`, `npm test` in `web/`.

- [ ] **Step 1: Update** Dockerfile, `.dockerignore`, CI, and README (a "Web app" section: `npm run dev` for development, served by the container in production, the default dev login).
- [ ] **Step 2: Build and run the real stack:** `docker build -t memberpulse-api .` then `docker compose -f docker-compose.yml -f <fresh-db override> --profile app up -d --no-build`; expected: `memberpulse-api` healthy and `GET http://localhost:5091/` returns the app's HTML.
- [ ] **Step 3: Verify in a real browser** (Claude in Chrome) against `http://localhost:5091`: sign in; seed realistic data through the UI/API (several members, a segment, a campaign, purchases that trigger and convert messages); screenshot Dashboard, Members, Member detail, Segments, Campaigns, Campaign detail, Admin; deep-link refresh on `/campaigns/<id>`; wrong password message; sign out; confirm no console errors. Expected: every page renders real data; fix anything that does not.
- [ ] **Step 4: Run everything:** `dotnet test`, `cd web && npm test && npm run build`; expected: all pass.
- [ ] **Step 5: Commit and report** with the screenshots listed; do not push without being asked.
