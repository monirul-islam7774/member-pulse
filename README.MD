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

C# · .NET 10 · ASP.NET Core Web API · Entity Framework Core · SQL Server · xUnit · Docker · GitHub Actions

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
| `GET /health` | Health check, including the database |
| `GET /api/members?page=1&pageSize=20` | Paged member list |
| `GET /api/members/{id}` | Member with accounts |
| `POST /api/members` | Create a member |
| `POST /api/members/{id}/accounts` | Open an account |

## Roadmap

- [x] Domain model, EF Core persistence, Members API, tests, CI
- [ ] Transactions endpoint
- [ ] Rule-based member segmentation
- [ ] JWT authentication
- [ ] Kafka event publishing
- [ ] Trigger engine (BackgroundService) and campaigns
- [ ] Conversion tracking and KPI reporting