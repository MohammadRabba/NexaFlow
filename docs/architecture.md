# NexaFlow Architecture

> Phase 1 — Foundation. Subsequent phases extend this document.

This document describes the high-level architecture, layering, and
cross-cutting concerns of NexaFlow.

## 1. Architecture Style

**Clean Architecture + Modular Monolith + CQRS + Domain/Application Events.**

NexaFlow is a single deployable ASP.NET Core Web API composed of four
projects with strict dependency direction:

```
NexaFlow.Api            ──▶ NexaFlow.Application
                        ──▶ NexaFlow.Infrastructure   (DI wiring only)

NexaFlow.Application    ──▶ NexaFlow.Domain

NexaFlow.Infrastructure ──▶ NexaFlow.Application  (implements abstractions)
                        ──▶ NexaFlow.Domain            (entity shape)

NexaFlow.Domain         ──✗─ (anything infrastructure)
```

See [ADR-002](decisions/ADR-002-clean-architecture.md) for the rationale.

## 2. Layers

### 2.1 Domain (`NexaFlow.Domain`)

Pure C# — no infrastructure references whatsoever (Rule 49). Contains:
- **Entities** — `User`, `Organization`, `OrganizationMember` in Phase 1
  (more in later phases: `Project`, `TaskItem`, `Comment`, `Label`,
  `TaskLabel`, `Notification`, `RefreshToken`, `AuditLog`, `Attachment`).
- **ValueObjects** — `Email`, `TenantId`.
- **Enums** — `OrganizationRole`.
- **Common** — `Entity`, `AuditableEntity`, `AggregateRoot`,
  `ITenantEntity`, `IDomainEvent`.
- **Events** — `OrganizationCreatedEvent` (more in Phase 5+).
- **Exceptions** — `DomainException`, `NotFoundException`,
  `TenantViolationException`, `InvalidStateTransitionException`.

Audit setters (`StampCreated` / `StampUpdated`) are `internal` and
visible only to Infrastructure via `InternalsVisibleTo` — Domain
invariants stay strong.

### 2.2 Application (`NexaFlow.Application`)

CQRS use cases (Phase 2+) + abstractions + cross-cutting behaviors.
Contains:
- **Abstractions**:
  - `IApplicationDbContext` — exposes `DbSet<T>` properties +
    `SaveChangesAsync`. Application uses EF Core's `DbSet<T>` directly
    (no repository wrappers — Rule 38).
  - `ICurrentUserService` — ambient authenticated user id, IP, trace id.
  - `ICurrentTenantService` + `ITenantResolutionStrategy` — **transport-agnostic**
    tenant resolution (Phase 1 ships the abstraction; the HTTP middleware
    implementation lands in Phase 2 with Auth).
  - `IUnitOfWork` — transactional control (Phase 4 first uses it).
  - `IEmailService` — declared but not yet implemented; **deferred** to
    Phase 2 (real sender) and Phase 6 (Outbox-routed).
  - `ICacheService` — declared but not yet implemented; **deferred** to
    Phase 7 (Redis).
  - `IEventPublisher` — declared but not yet implemented; **deferred**
    to Phase 6 (RabbitMQ + Outbox).
- **Common** — `Result`, `Result<T>`, `PagedResult<T>`, `PageQuery`.
- **Behaviors** — `LoggingBehavior` (logs every MediatR request + elapsed
  time), `ValidationBehavior` (runs FluentValidation before the handler).
  `TransactionBehavior` will be added in Phase 4 when transactions are
  first needed.
- **DI** — `ServiceCollectionExtensions.AddApplication` wires MediatR +
  FluentValidation + behaviors.

### 2.3 Infrastructure (`NexaFlow.Infrastructure`)

Concrete implementations. Contains:
- **Persistence** — `ApplicationDbContext` with:
  - Centralized global query filter for every `ITenantEntity`
    (Expression-tree-built lambda per entity type — fail-closed when no
    tenant resolved).
  - `SaveChangesAsync` override that:
    - Stamps audit metadata (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
      from `ICurrentUserService`.
    - **Verifies** that every tenant-owned entity's `OrganizationId`
      equals the resolved tenant id at insert time (defense-in-depth #3).
    - Rejects `OrganizationId` mutations on update (immutable).
  - Snake-case naming via `EFCore.NamingConventions`.
  - EF Core entity configurations in `Persistence/Configurations/`
    (no EF attributes on Domain entities — Rule 38).
- **Services**:
  - `CurrentUserService` — scoped, `AsyncLocal`-backed ambient principal.
  - `CurrentTenantService` — scoped, holds the resolved tenant for the
    request; implements both `ICurrentTenantService` (for Application)
    and `ITenantServiceAccessor` (for DbContext).
  - `EfUnitOfWork` — EF Core transaction wrapper.
  - `BCryptPasswordHasher` — BCrypt with work factor 12.
- **Auth** (Phase 2) — `JwtTokenService`, `RefreshTokenStore`.

### 2.4 Api (`NexaFlow.Api`)

Composition root. Phase 1:
- `Program.cs` — Serilog, DI wiring, health checks, OpenAPI (via
  built-in .NET 10 generator + Scalar UI), rate limiting, CORS, HSTS,
  HTTPS redirection.
- Middleware: `TraceIdMiddleware` (response header), `CurrentUserMiddleware`
  (populates `CurrentUserService`), `ExceptionHandlingMiddleware`
  (RFC 7807 Problem Details — section 31).
- Endpoints: `HealthEndpoints` (`/health/live`, `/health/ready`, `/health`).

Phase 2 will add auth endpoints + JWT bearer auth + tenant resolution
middleware. Phase 6 will add the SignalR NotificationHub.

## 3. Cross-Cutting Concerns

### 3.1 Logging

Serilog with structured logging templates (section 32). Output to console
in Phase 1; Phase 9 will add OpenTelemetry-compatible sinks. Secrets
(passwords, tokens, reset tokens, email verification tokens) are NEVER
logged — the `LoggingBehavior` only logs the request type name and
elapsed time, never the payload.

### 3.2 Validation

FluentValidation, run by `ValidationBehavior` before every MediatR
handler (section 13). On failure, throws `ValidationException`, mapped
to a 422 Problem Details response by `ExceptionHandlingMiddleware`.

### 3.3 Error Handling

RFC 7807 Problem Details (section 31). Centralized in
`ExceptionHandlingMiddleware`. Mapping:
- `DomainException` → 400
- `NotFoundException` → 404 (avoids confirming resource existence)
- `TenantViolationException` → 403
- `InvalidStateTransitionException` → 409
- `ValidationException` → 422 (with field-level error dictionary)
- `OperationCanceledException` → 499
- Otherwise → 500 (no stack trace in production)

### 3.4 Multi-Tenancy

See [ADR-004](decisions/ADR-004-multi-tenancy.md) for the full
strategy. Five-layer defense-in-depth: tenant resolution, authorization,
application-level validation, EF Core global query filters, database
constraints.

### 3.5 Rate Limiting

Per-IP fixed window: 100 req/min default. Phase 7 may switch to a
distributed Redis-backed limiter when multiple app instances exist.

### 3.6 Health Checks

- `/health/live` — process is alive; no dependencies checked.
- `/health/ready` — Postgres reachable. Phase 6 will add RabbitMQ;
  Phase 7 will add Redis.
- `/health` — alias for `/health/ready`.

### 3.7 OpenAPI

.NET 10's built-in document generator serves `/openapi/v1.json` in
Development. Scalar UI serves `/scalar/v1` for interactive exploration.

## 4. Phases

| Phase | Status | Scope |
|---|---|---|
| 1 — Foundation | ✅ Complete | Solution, Domain entities, Application abstractions, Infrastructure (DbContext + migrations), Api (Program, middleware, health, OpenAPI), 4 ADRs, Docker, tests. |
| 2 — Authentication | ✅ Complete | Eight auth endpoints, JWT (HS256), refresh-token rotation + family/reuse detection, email verification, password reset, account lockout, tenant-resolution middleware (HTTP), ADR-005. |
| 3 — Multi-Tenancy | ✅ Complete | Organization CRUD + soft-delete; Membership invite / role update / removal / ownership transfer; Permission model (const strings + role→permission mapping); cross-tenant guard (URL/header mismatch → 404); 8 cross-tenant integration tests (Tests 1-8); ADR-006, ADR-007. |
| 4 — Projects | ✅ Complete | Project aggregate + state machine; ProjectMember (Owner/Contributor/Reader); resource-level authorization (ProjectAccess helper); CRUD with filtering/sorting/pagination; project member management; migration; 6 project authorization integration tests; ADR-008. |
| 5 — Tasks | Pending | CRUD, assignment, status transitions, priority, labels, comments, attachments. |
| 6 — Events & Notifications | Pending | Domain events → Outbox → RabbitMQ, notification worker, SignalR hub. |
| 7 — Redis | Pending | Caching, cache invalidation, distributed rate limiting. |
| 8 — Audit Logs | Pending | Full audit record persistence, member-change / role-change / auth-event auditing. |
| 9 — Observability | Pending | OpenTelemetry tracing + metrics, structured logging sinks. |
| 10 — Testing | Pending | Tenant isolation, authorization matrix, refresh-token reuse detection, messaging idempotency. |
| 11 — CI/CD | Pending | Full CI pipeline with integration tests + Docker build + security scans. |

## 5. Phase 1 — Definition of Done

- ✅ Solution compiles with `TreatWarningsAsErrors=true`.
- ✅ Four ADRs written.
- ✅ EF Core migration `InitialCreate` generated.
- ✅ Dockerfile + docker-compose wired up.
- ✅ 37 tests passing (31 Domain + 2 Application + 3 Api smoke + 1 Integration placeholder).
- ✅ Solution builds in CI-equivalent command (`dotnet build NexaFlow.slnx`).
- ✅ Test project wired up for Phase 3 cross-tenant isolation tests
  (PostgreSqlFixture ready; not yet exercised).

See `README.md` for the project overview, `docs/database.md` for the
database schema, and `docs/decisions/` for ADRs.
