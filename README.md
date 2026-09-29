# NexaFlow

**Multi-Tenant Project Management SaaS** — Phase 1 (Foundation)

> Portfolio-grade .NET 10 backend demonstrating Clean Architecture,
> Modular Monolith, CQRS, Domain-Driven Design, multi-tenancy with
> defense-in-depth, and production-oriented engineering practices.

---

## Overview

NexaFlow is a multi-tenant Project Management SaaS backend. Each tenant
(an "Organization") is fully isolated from others at the row level;
authorization is permission-based rather than role-only; and the system
is designed for incremental evolution across eleven phases.

**Phase 1 (this repository) implements Foundation:**
- Clean Architecture with strict dependency direction
- Domain layer (entities, value objects, enums, events, exceptions)
- Application layer (abstractions, MediatR behaviors)
- Infrastructure layer (EF Core + Npgsql, audit stamping, multi-tenant
  global query filters)
- Api layer (Program.cs composition root, middleware, health checks,
  OpenAPI document)
- Four ADRs documenting the major architectural decisions
- Production-oriented Dockerfile (non-root user, multi-stage)
- docker-compose for local development (API + PostgreSQL 16)
- 37 tests passing across 4 test projects

---

## Features

### Phase 1 ✅

- ✅ Domain model with strong invariants (`User`, `Organization`,
  `OrganizationMember`, `Email`, `TenantId`)
- ✅ Audit stamping via `InternalsVisibleTo` (no public setters; no
  leakage of audit concerns into Domain)
- ✅ Domain events queued on aggregates (dispatcher lands in Phase 6)
- ✅ Centralized EF Core global query filter for `ITenantEntity` —
  fail-closed when no tenant resolved
- ✅ `SaveChangesAsync` override that **rejects** mismatched
  OrganizationId on insert and **rejects** OrganizationId mutations
  on update
- ✅ RFC 7807 Problem Details error handling
- ✅ Serilog structured logging with correlation scope
- ✅ Per-IP fixed window rate limiting (100 req/min)
- ✅ Health checks at `/health/live` and `/health/ready`
- ✅ OpenAPI document at `/openapi/v1.json` + Scalar UI at `/scalar/v1`
- ✅ Four ADRs documenting decisions (Postgres, Clean Architecture,
  Modular Monolith, Multi-Tenancy)
- ✅ Test infrastructure for Phase 3 cross-tenant isolation tests
  (Testcontainers PostgreSql fixture ready)

### Phase 2+ (Pending)

| Phase | What lands |
|---|---|
| 2 | Authentication (JWT, refresh token rotation, lockout, email verification) |
| 3 | Multi-tenancy middleware, cross-tenant isolation tests |
| 4 | Projects CRUD, members, filtering, sorting, pagination |
| 5 | Tasks CRUD, assignment, status, labels, comments, attachments |
| 6 | Domain events → Outbox → RabbitMQ, SignalR NotificationHub, background workers |
| 7 | Redis caching, distributed rate limiting |
| 8 | Audit log persistence (CRUD + auth-event auditing) |
| 9 | OpenTelemetry tracing + metrics |
| 10 | Comprehensive testing matrix (auth, authorization, tenant isolation, messaging) |
| 11 | Full CI/CD pipeline (build, format, unit, integration, security, Docker) |

---

## Architecture

**Style:** Clean Architecture + Modular Monolith + CQRS +
Domain/Application Events.

**Dependency direction:**
```
NexaFlow.Api            ──▶ NexaFlow.Application
                        ──▶ NexaFlow.Infrastructure (DI wiring only)

NexaFlow.Application    ──▶ NexaFlow.Domain

NexaFlow.Infrastructure ──▶ NexaFlow.Application  (implements abstractions)
                        ──▶ NexaFlow.Domain            (entity shape)

NexaFlow.Domain         ──✗─ (anything infrastructure)
```

See [docs/architecture.md](docs/architecture.md) for the full
architecture documentation and [docs/decisions/](docs/decisions/) for
ADRs.

---

## Technology Stack

| Layer | Technology | Why |
|---|---|---|
| Runtime | .NET 10 LTS | Current stable supported LTS |
| Web framework | ASP.NET Core | Built-in, framework primitive |
| ORM | Entity Framework Core 10 | Mature, supports global query filters + migrations |
| Database | PostgreSQL 16 | Open-source, native `uuid` + `timestamptz` + `JSONB`, container-friendly |
| Postgres provider | Npgsql.EntityFrameworkCore.PostgreSQL 10 | Reference implementation |
| CQRS | MediatR 12 | Industry-standard, supports pipeline behaviors |
| Validation | FluentValidation 12 | Composable rules; auto-DI registration |
| Password hashing | BCrypt.Net-Next 4 | Mature, well-audited adaptive hashing |
| Logging | Serilog 9 | Structured logging with correlation scope |
| Health checks | AspNetCore.HealthChecks.NpgSql | Postgres readiness probe |
| API documentation | .NET 10 built-in OpenAPI + Scalar | Replaces Swashbuckle (compatibility issues) |
| Testing | xUnit + FluentAssertions + Testcontainers | Real-infrastructure integration tests |
| Containerization | Docker (alpine images) | Reproducible local + production |

**Phase 1 deliberately does NOT include:** Redis, RabbitMQ, SignalR,
OpenTelemetry, Quartz, NSwag — added in their respective phases per
the master prompt's "no technology soup" rule.

---

## Security

- **Tenant isolation:** defense-in-depth at five layers (tenant
  resolution, authorization, application-level validation, EF Core
  global query filters, database constraints). See
  [ADR-004](docs/decisions/ADR-004-multi-tenancy.md).
- **Password storage:** BCrypt with work factor 12 (no custom crypto —
  section 40).
- **Error responses:** RFC 7807 Problem Details; no stack traces in
  production; no resource enumeration (404 over 403 for cross-tenant
  access — ADR-004 §3).
- **Rate limiting:** per-IP fixed window (100 req/min default).
- **HTTP security:** HSTS, HTTPS redirection in production.
- **Secrets:** connection strings and token keys are read from
  environment variables in production (Phase 11). The Phase 1
  `appsettings.json` contains only placeholders.

---

## Multi-Tenancy

Shared database, shared schema, row-level `OrganizationId`
discriminator. See [ADR-004](docs/decisions/ADR-004-multi-tenancy.md).

Tenant resolution is **transport-agnostic** — the Application layer
depends on `ICurrentTenantService` + `ITenantResolutionStrategy`
abstractions, not on the `X-Organization-Id` HTTP header. Phase 2 adds
the HTTP middleware strategy that validates the candidate OrganizationId
against the user's JWT membership claims.

Cross-tenant isolation tests land in Phase 3.

---

## Authentication

Phase 2 will implement:

| Endpoint | Purpose |
|---|---|
| `POST /api/auth/register` | Create user (pending email verification) |
| `POST /api/auth/login` | Authenticate, obtain access + refresh tokens |
| `POST /api/auth/refresh` | Rotate refresh token |
| `POST /api/auth/logout` | Revoke refresh token |
| `POST /api/auth/forgot-password` | Initiate password reset |
| `POST /api/auth/reset-password` | Complete password reset |
| `POST /api/auth/verify-email` | Complete email verification |
| `POST /api/auth/change-password` | Change password (authenticated) |

Access token lifetime: 15 minutes. Refresh token lifetime: 7 days,
single-use with rotation. Reuse of a revoked refresh token revokes the
entire chain.

See [docs/authentication.md](docs/authentication.md) for full design.

---

## Authorization

Permission-based (not role-only — section 9). Phase 3 implements the
permission matrix and the authorization handlers. Planned permissions:

| Resource | Permissions |
|---|---|
| Project | `Project.Read` `Project.Create` `Project.Update` `Project.Delete` |
| Task | `Task.Read` `Task.Create` `Task.Update` `Task.Delete` |
| Member | `Member.Read` `Member.Invite` `Member.Update` `Member.Remove` |
| Organization | `Organization.Update` |
| AuditLog | `AuditLog.Read` |

---

## Messaging

Phase 6 will introduce RabbitMQ + the Outbox pattern for asynchronous
event-driven workflows:

```
HTTP Request
     ↓
DB Transaction
     ├── Update Task
     └── Store Outbox Message
              ↓
        Background Publisher
              ↓
           RabbitMQ
              ↓
     Notification / Audit / Analytics consumers
```

Consumers will be idempotent and tolerate duplicate delivery (at-least-once).

---

## Caching

Phase 7 will add Redis caching for read-side projections:
- `organization:{id}`
- `project:{id}`
- `user:{id}`

Cache invalidation rules will be enforced; authorization data will
never be cached in a way that can bypass security (section 23).

---

## Background Processing

Phase 6 will introduce a hosted `IHostedService` worker for:
- Email delivery
- Notification processing
- Upcoming deadline notifications
- Expired refresh-token cleanup
- Outbox processing

---

## Observability

Phase 9 will add OpenTelemetry tracing + metrics, building on the
Serilog structured logging already wired up in Phase 1. Health check
endpoints `/health/live` and `/health/ready` are already live.

---

## Testing

| Project | Type | Phase 1 status |
|---|---|---|
| `NexaFlow.Domain.Tests` | Pure unit tests for domain invariants | 31 tests passing |
| `NexaFlow.Application.Tests` | DI + behavior smoke tests | 2 tests passing |
| `NexaFlow.Api.Tests` | WebApplicationFactory smoke tests (health, OpenAPI, 404) | 3 tests passing |
| `NexaFlow.IntegrationTests` | Real-Postgres integration tests via Testcontainers | Fixture wired up; tests added in Phase 3 |

```bash
dotnet test NexaFlow.slnx --nologo
```

---

## Docker

Production-oriented multi-stage Dockerfile — runs as a non-root user
(`nexaflow`), uses `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` for
runtime, includes a `HEALTHCHECK` against `/health/live`.

```bash
docker compose up -d          # API + Postgres
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
curl http://localhost:8080/openapi/v1.json
```

---

## CI/CD

Phase 11 will wire up the full GitHub Actions pipeline:

```
Checkout → Restore → Build → Format/lint → Unit tests →
Integration tests → Security/dependency checks → Docker build →
Deploy (after required checks pass; secrets via GitHub Secrets)
```

Phase 1 includes the CI skeleton at
[.github/workflows/ci.yml](.github/workflows/ci.yml).

---

## Running Locally

### Option 1 — Docker Compose (recommended)

```bash
git clone <repo>
cd NexaFlow
docker compose up -d
```

Verify:
- `http://localhost:8080/health/live` → `{"status":"Healthy","kind":"live"}`
- `http://localhost:8080/health/ready` → `{"status":"Healthy","kind":"ready"}`
- `http://localhost:8080/openapi/v1.json` → OpenAPI document
- `http://localhost:8080/scalar/v1` → Interactive API explorer

### Option 2 — Local dotnet

Prerequisites:
- .NET 10 SDK
- PostgreSQL 16+ running on localhost:5432

```bash
git clone <repo>
cd NexaFlow

# Apply migrations
ASPNETCORE_ENVIRONMENT=Development \
  dotnet ef database update \
    --project src/NexaFlow.Infrastructure/NexaFlow.Infrastructure.csproj \
    --startup-project src/NexaFlow.Api/NexaFlow.Api.csproj

# Run
dotnet run --project src/NexaFlow.Api/NexaFlow.Api.csproj
```

---

## API Documentation

See [docs/api.md](docs/api.md). Phase 1 exposes only health endpoints;
Phase 2 adds the auth endpoints listed above.

---

## Architecture Diagram

```
                         ┌──────────────────────┐
                         │      Frontend        │
                         └──────────┬───────────┘
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │    NexaFlow API      │
                         │   ASP.NET Core 10    │
                         └──────────┬───────────┘
                                    │
                         ┌──────────▼───────────┐
                         │     Application      │
                         │ CQRS / Use Cases     │
                         └──────────┬───────────┘
                                    │
                         ┌──────────▼───────────┐
                         │        Domain        │
                         │ Business Rules       │
                         │ Entities / Events    │
                         └──────────────────────┘

                                    ▲
                                    │
                         ┌──────────┴───────────┐
                         │    Infrastructure    │
                         ├──────────────────────┤
                         │ PostgreSQL (Phase 1) │
                         │ Redis       (Phase 7) │
                         │ RabbitMQ    (Phase 6) │
                         │ Email       (Phase 2) │
                         │ SignalR     (Phase 6) │
                         │ OpenTelemetry(Phase 9)│
                         └──────────────────────┘
```

---

## Database Diagram

Phase 1 (entity relationships — see [docs/database.md](docs/database.md)
for full schema):

```
                   ┌──────────────┐
                   │ organizations│
                   │──────────────│
                   │ id           │←────────────────┐
                   │ name         │                 │
                   │ slug (unique)│                 │
                   │ owner_user_id│──┐               │
                   └──────┬───────┘  │               │
                          │ FK       │               │
                          ▼          │ FK            │
              ┌────────────────────────┐             │
              │ organization_members    │             │
              │─────────────────────────│             │
              │ id                      │             │
              │ organization_id (FK)────│─────────────┘
              │ user_id (FK)───────────│──┐
              │ role                    │  │
              │ is_active               │  │
              │ invitation_token_hash   │  │
              └─────────────────────────┘  │
                                            │
              ┌────────────────────────┐    │
              │ users                  │    │
              │────────────────────────│    │
              │ id                     │←───┘
              │ email_normalized (uniq)│
              │ password_hash          │
              │ email_verified          │
              │ lockout_end_utc        │
              └────────────────────────┘
```

---

## ADR Summary

| ADR | Decision |
|---|---|
| [ADR-001](docs/decisions/ADR-001-postgresql.md) | Use PostgreSQL 16 as the primary relational database |
| [ADR-002](docs/decisions/ADR-002-clean-architecture.md) | Clean Architecture with four projects (Domain, Application, Infrastructure, Api) |
| [ADR-003](docs/decisions/ADR-003-modular-monolith.md) | Prefer Modular Monolith over Microservices |
| [ADR-004](docs/decisions/ADR-004-multi-tenancy.md) | Shared database, shared schema, row-level OrganizationId |

Future ADRs (created when their phase lands):
- ADR-005: RabbitMQ + Outbox (Phase 6)
- ADR-006: Redis caching strategy (Phase 7)
- ADR-007: Permission-based authorization matrix (Phase 3)

---

## Future Improvements

- **Schema partitioning** for `audit_logs` and `outbox_messages` once
  the row count warrants it (likely Phase 8+).
- **Per-tenant rate limits** in addition to the per-IP limit.
- **Database-per-tenant strategy** as a future alternative for
  regulated industries — the multi-tenancy abstraction is designed so
  this is a non-breaking extension (see ADR-004).
- **Async API spec** alongside the OpenAPI doc for the SignalR hub
  (Phase 6).
- **OpenTelemetry traces exported** to Jaeger / Tempo (Phase 9).

---

## License

Proprietary — portfolio demonstration project.
