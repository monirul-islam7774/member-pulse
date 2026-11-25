# MemberPulse dashboard web app: design

Status: approved by the user in conversation ("go ahead"), 2026-11-25.

## Intent
Staff of a credit union / bank use MemberPulse through a browser instead of calling JSON endpoints: sign in, see how
campaigns are performing, and work with members, segments and campaigns. The backend is complete and verified; this
adds the missing user interface. The user asked for "the dashboard web app" and wants to see the product working.

## Goals
- Sign in and out with the existing JWT login; admin-only areas appear only for admins.
- A dashboard that answers "are campaigns working?": totals (sent, converted, conversion rate, members reached,
  converted amount), a sent-vs-converted chart over time, and a per-campaign table.
- Members: paged list; detail with accounts, recent transactions, the segments the member is in; post a transaction.
- Segments: list, create with a criteria builder, view matching members.
- Campaigns: list, create, pause/resume, view deliveries (status, attempts, conversion).
- Admin: dead-lettered events (list, retry) and staff account unlock.
- One deployable: the API container serves the built UI.

## Non-goals
- No new backend features beyond what the UI needs (below). No member-facing UI. No SMS/email provider work.
- No refresh tokens, no self-service password reset, no multi-tenant concerns.

## Anti-goals
- A UI that only works against mocks. It must be exercised against the real API.
- Secrets or tokens leaking into logs, URLs or the repo.
- A second origin / CORS configuration to maintain.

## Constraints
- Existing API contract (JSON, camelCase, enums as strings, `Authorization: Bearer`). Tokens last 60 minutes with no
  refresh, so the UI must handle 401 by returning to sign-in.
- Docker image must stay buildable in CI (`docker-build` job) and small changes to the Dockerfile only.
- Existing .NET tests (100 normal + 6 integration) keep passing; CI stays green.

## Approaches considered
1. **React + TypeScript + Vite SPA in `web/`, served by the API from `wwwroot` (chosen).** One container, same
   origin so no CORS, mainstream stack, fast dev loop with Vite's `/api` proxy. Cost: a Node build stage in the Dockerfile.
2. Server-rendered Razor/Blazor inside the API. No JS toolchain, but a weaker fit for interactive charts and forms,
   and ties UI to the .NET release cycle.
3. Separate SPA container behind a reverse proxy. Cleanest separation, but two images, CORS or a proxy to maintain.
   Over-engineered for one team.

## Design
- `web/`: React 19 + TypeScript + Vite, React Router, Recharts for charts, plain CSS (custom properties, light/dark via
  `prefers-color-scheme`). Vitest + Testing Library for UI tests.
- API client: a small typed `fetch` wrapper that attaches the token, parses problem-details errors, and on `401`
  clears the session and routes to sign-in. Token kept in `sessionStorage` (cleared when the tab closes).
- Hosting: the API serves static files from `wwwroot` and falls back to `index.html` for client routes, but never for
  `/api/*`, `/health` or `/openapi`. Dev: Vite proxies `/api` to `http://localhost:5091`.
- Dockerfile: new Node stage builds `web/` into the publish output's `wwwroot`.
- Small API additions: `GET /api/reports/campaigns/daily` (overall sent/converted per day for the dashboard chart),
  `GET /api/users` (admin: list staff with lock status, so the UI can offer unlock without typing an email).
- Layout: left navigation (Dashboard, Members, Segments, Campaigns, Admin), content area, signed-in user and sign-out
  in the header.

## Left to the builder
Exact component structure, form validation messages, table density, empty/loading/error states (all required),
chart colour choices (accessible, distinguishable in both themes), and the precise shape of the two new endpoints
(follow existing conventions: DTO records, paged results where lists can grow).

## Testing and verification
- Unit/component tests for the API client (401 handling, error parsing) and key forms.
- .NET tests for the two new endpoints and for static-file hosting (SPA fallback does not swallow `/api` 404s).
- Verified against the real running stack in a browser, with screenshots of each page, before calling it done.
