# MemberPulse

A member data and engagement platform for credit unions and banks, built with C# and .NET 10.

MemberPulse manages member, account and transaction data, and is being extended to segment
members and trigger personalized campaigns in real time from transaction events.

## Architecture

```
src/
  MemberPulse.Domain          Business model and rules. No framework dependencies.
  MemberPulse.Infrastructure  EF Core DbContext, mappings, SQL Server.
  MemberPulse.Api             ASP.NET Core Web API: controllers, DTOs, startup.
tests/
  MemberPulse.Tests           xUnit domain tests and API integration tests.
```

Dependencies point inward: Api → Infrastructure → Domain.

## Tech stack

C# · .NET 10 · ASP.NET Core Web API · Entity Framework Core · SQL Server · Kafka · xUnit · Docker · GitHub Actions

## Getting started

Prerequisites: .NET 10 SDK, Docker.

```bash
docker compose up -d
dotnet run --project src/MemberPulse.Api
dotnet test
```

### Running the API in Docker

```bash
cp .env.example .env          # then set JWT_KEY (32+ random characters) and ADMIN_PASSWORD
docker compose --profile app up --build
```

This starts SQL Server, Kafka and the API (image built from the `Dockerfile`) on `http://localhost:5091`. A plain
`docker compose up -d` still starts only SQL Server and Kafka, for running the API from your IDE.

The container runs in the `Production` environment, as an unprivileged user, and is configured entirely through
environment variables (`ConnectionStrings__MemberPulse`, `Jwt__Key`, `Kafka__BootstrapServers`, `Seed__AdminEmail`,
`Seed__AdminPassword`). Production never creates the dev sample data or the development admin, and doesn't serve
OpenAPI. It applies database migrations on startup only when `Database__MigrateOnStartup=true` (set in the compose
file); with more than one instance, run migrations as a separate deploy step instead.

### Web app

The dashboard lives in `web/` (React + TypeScript + Vite). In production the API serves it, so one container and one
address do everything: open `http://localhost:5091/` and sign in. Pages: a dashboard (conversion headline, daily chart,
per-campaign table), members (accounts, transactions, post a transaction), segments (criteria builder), campaigns
(create, pause, delivery log) and, for admins, staff accounts and dead-lettered events.

For development, run the API (`dotnet run --project src/MemberPulse.Api`) and then:

```bash
cd web
npm install
npm run dev      # http://localhost:5173, forwards /api to the API on :5091
npm test         # component and API-client tests
npm run build    # type-check and production build into web/dist
```

Sign in with the development admin from the Authentication section. The Docker image builds the web app in its own
stage and copies it into the API's `wwwroot`.

## API

| Endpoint | Description |
|---|---|
| `GET /health` | Health check, including the database (public) |
| `POST /api/auth/login` | Exchange email + password for a JWT (public) |
| `GET /api/auth/me` | The signed-in staff user |
| `POST /api/users` | Create a staff user (Admin only) |
| `GET /api/members?page=1&pageSize=20` | Paged member list |
| `GET /api/members/{id}` | Member with accounts |
| `POST /api/members` | Create a member |
| `POST /api/members/{id}/accounts` | Open an account |
| `GET /api/accounts/{id}/transactions` | Paged transactions, newest first |
| `GET /api/accounts/{id}/transactions/{txId}` | Single transaction |
| `POST /api/accounts/{id}/transactions` | Post a transaction (negative = debit; overdrafts on Savings/Checking return 422) |
| `GET/POST /api/segments`, `GET /api/segments/{id}` | List, create and read segments |
| `GET /api/segments/{id}/members` | Members currently matching a segment |
| `GET /api/members/{id}/segments` | Segments a member currently belongs to |
| `GET/POST /api/campaigns`, `GET /api/campaigns/{id}` | List, create and read campaigns |
| `POST /api/campaigns/{id}/pause`, `.../resume` | Stop or restart a campaign |
| `GET /api/campaigns/{id}/deliveries` | Messages the campaign has triggered, with conversion details |
| `GET /api/reports/campaigns?from=&to=` | KPIs per campaign and in total (default: last 30 days, max 366) |
| `GET /api/reports/campaigns/{id}?from=&to=` | One campaign's KPIs plus a daily series |

### Authentication

Every endpoint except `/health` and `/api/auth/login` requires `Authorization: Bearer <token>`.
Staff users have the role `Staff` or `Admin`; only admins can create users.

- **Signing key:** set `Jwt:Key` (32+ chars) via user-secrets or the `Jwt__Key` environment variable.
  Development uses a throwaway key from `appsettings.Development.json`; the app refuses to start without one.
- **First admin:** if `Seed:AdminEmail` and `Seed:AdminPassword` are set and no staff exist, that admin
  is created at startup. Development seeds `admin@memberpulse.local` / `Admin_Dev123!`.
- Passwords are hashed with ASP.NET Core's `PasswordHasher`.
- **Lockout:** 5 wrong passwords in a row lock the account for 15 minutes. While locked, even the right password
  is refused, with the same `401` as any other failure so nothing is revealed. Admins can clear it early with
  `POST /api/users/unlock {"email": ...}`.
- **Rate limit:** `POST /api/auth/login` allows `Security:LoginPermitsPerMinute` (default 10) per client IP, then
  answers `429` with `Retry-After`. Behind a reverse proxy, forward the real client IP (e.g. forwarded-headers
  middleware) or all users share one limit.

### Events (Kafka)

Every posted transaction (including an account's initial deposit) produces a `TransactionPosted`
event on the `memberpulse.transactions` topic, keyed by account id so each account's events stay in order.

- The event is written to an **outbox** table in the same database transaction as the posting, and a
  background dispatcher publishes it. A Kafka outage delays events but never loses them or reorders an account's events.
- Delivery is **at-least-once**: consumers should de-duplicate on `transactionId`.
- Set `Kafka:BootstrapServers` (Development uses `localhost:9092`, started by `docker compose up -d`).
  If it is empty, events are handed straight to the trigger engine in-process, so campaigns still work.
- **Failures back off, they don't block.** A failed publish is retried after 2s, 4s, 8s ... up to 5 minutes. Events for
  the same account never overtake a failing one, but other accounts carry on.
- After `Kafka:MaxPublishAttempts` (default 20, roughly an hour of outage) a message is **dead-lettered** so it stops
  holding up its account. Admins can list them with `GET /api/admin/outbox/dead` and give one a fresh run with
  `POST /api/admin/outbox/{id}/retry`. A revived event arrives after any newer ones for that account.
- Published rows are deleted after `Kafka:ProcessedRetentionDays` (default 7). Dead letters are kept.

### Campaigns and the trigger engine

A campaign targets a **segment** and reacts to posted transactions. Optional filters narrow which
transactions count: `direction` (`Any`/`Debit`/`Credit`), `minAmount` (absolute value) and `merchantCategory`.
The `messageTemplate` can use `{FirstName}`, `{Amount}` and `{MerchantCategory}`, and `cooldownDays`
limits how often one member hears from the same campaign.

For every `TransactionPosted` event the engine finds the active campaigns that apply, checks the member is
in the campaign's segment *at that moment*, then records a delivery and sends the message.

- Each transaction triggers a given campaign at most once, so replayed Kafka events are harmless.
- With Kafka configured a consumer (group `memberpulse-triggers`) feeds the engine; otherwise the in-process publisher does.
- Messages go through `IMessageSender`. The default only **logs** them; plug in an email/SMS/push provider there.
- **Failed sends are retried.** A failed send stays `Pending` and is retried after 30s, 2m, 8m and 32m. After 5 attempts
  it becomes `Failed` for good. `attempts` and `error` are shown on each delivery. A background worker does this every
  `Campaigns:RetryPollSeconds` (default 30).
- A send that was lost mid-flight (the process died) is picked up again after 5 minutes. In that rare case a message
  can be sent twice, so the sender should tolerate duplicates.
- Reports count `pending` messages separately, and a message can only convert once it has actually been `Sent`.

### Conversions and KPIs

Each campaign has a **conversion goal**: `conversionWindowDays` (1-90, default 7), `conversionDirection`
(default `Debit`), and optional `conversionMinAmount` / `conversionMerchantCategory`. A message *converts*
when the same member later posts a transaction that meets the goal inside the window.

- The first qualifying transaction wins; a delivery converts once, with that transaction's absolute amount.
- The transaction that triggered the message can't convert it, and failed or unsent messages can't convert.
- Paused campaigns keep tracking conversions of messages they already sent.
- A transaction can convert messages from several campaigns (every campaign that reached the member gets credit).

Reports give, per campaign and in total: `sent`, `failed`, `converted`, `conversionRate` (converted / sent),
`membersReached` and `convertedAmount`. Messages are counted by when they were **sent**, so a message sent
inside the range counts as converted even if the conversion happened after the range ended, and rates for
recent messages keep rising until their windows close.

### Segments

A segment is a name plus criteria; a member belongs when **all** criteria hold. Criteria are
`metric` / `operator` / `value`, where metric is one of `Age`, `TotalBalance`, `AccountCount`,
`TransactionCount30d`, `Spend30d`, `DaysSinceJoined`. `TotalBalance`/`AccountCount` can be scoped
with `accountType`; `Spend30d`/`TransactionCount30d` with `merchantCategory`.

## Roadmap

- [x] Domain model, EF Core persistence, Members API, tests, CI
- [x] Transactions endpoint
- [x] Rule-based member segmentation
- [x] JWT authentication
- [x] Kafka event publishing
- [x] Trigger engine (BackgroundService) and campaigns
- [x] Conversion tracking and KPI reporting