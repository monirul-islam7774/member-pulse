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

### Authentication

Every endpoint except `/health` and `/api/auth/login` requires `Authorization: Bearer <token>`.
Staff users have the role `Staff` or `Admin`; only admins can create users.

- **Signing key:** set `Jwt:Key` (32+ chars) via user-secrets or the `Jwt__Key` environment variable.
  Development uses a throwaway key from `appsettings.Development.json`; the app refuses to start without one.
- **First admin:** if `Seed:AdminEmail` and `Seed:AdminPassword` are set and no staff exist, that admin
  is created at startup. Development seeds `admin@memberpulse.local` / `Admin_Dev123!`.
- Passwords are hashed with ASP.NET Core's `PasswordHasher`. There is no lockout or rate limiting yet.

### Events (Kafka)

Every posted transaction (including an account's initial deposit) produces a `TransactionPosted`
event on the `memberpulse.transactions` topic, keyed by account id so each account's events stay in order.

- The event is written to an **outbox** table in the same database transaction as the posting, and a
  background dispatcher publishes it. A Kafka outage delays events but never loses them or reorders an account's events.
- Delivery is **at-least-once**: consumers should de-duplicate on `transactionId`.
- Set `Kafka:BootstrapServers` (Development uses `localhost:9092`, started by `docker compose up -d`).
  If it is empty, events are logged instead of delivered.
- A message that keeps failing blocks the ones behind it (check `OutboxMessages.LastError`).

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
- [ ] Trigger engine (BackgroundService) and campaigns
- [ ] Conversion tracking and KPI reporting