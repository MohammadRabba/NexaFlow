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

### 3.8 Audit Logging (Phase 8)

A dedicated `audit_logs` table records every security- or business-sensitive
operation (spec §25). The `AuditLog` domain entity is **not** tenant-scoped
at the EF level — `organization_id` is nullable because auth events (login,
password change, etc.) occur before tenant resolution. Two Application-layer
query methods (`GetPagedAuditLogsAsync` / `GetPagedAuditLogsForUserAsync`)
filter explicitly:
- the organization-scoped endpoint `/api/audit-logs` returns rows where
  `organization_id == resolved_tenant` (requires `audit_log.read`
  permission — granted to Owner + Admin only);
- the user-scoped endpoint `/api/audit-logs/my-activity` returns rows where
  `user_id == ambient user` (cross-tenant self-service "my login history"
  view; available to any authenticated user).

Audit rows are persisted **in the same `SaveChangesAsync` transaction** as
the business mutation (spec §14 transactional example explicitly lists
"Create Audit Log" alongside "Create Project + Add Project Member"). The
`IAuditService.RecordAsync(...)` helper reads ambient user / tenant / IP
from `ICurrentUserService` + `ICurrentTenantService`, constructs an
`AuditLog` entity via the `AuditLog.Create(...)` factory, and calls
`IApplicationDbContext.Add<AuditLog>(entry)`. The handler then calls
`SaveChangesAsync` once, persisting both the business change and the audit
row atomically.

For auth events that occur before the ambient context exists (login,
email verification), the handler passes an explicit `actorUserIdOverride`
and `organizationIdOverride: null`. For `OrganizationCreated` (a pre-tenant
operation), the handler passes the new org's id as the override so the
audit row is associated with the new org.

**Secrets policy (spec §25 "Be careful with sensitive information"):**
auth events record NO `oldValues` / `newValues` — the action name + entity
id + timestamp is sufficient context, and there are no safe fields to
record (passwords, refresh tokens, email-verification tokens are all
credential-like secrets). Tenant-scoped events (task / project / member
mutations) record JSON snapshots of relevant fields (e.g., from→to role
for `RoleChanged`) but never include credential data.

See ADR-011 for the full rationale, including why an asynchronous
audit-via-RabbitMQ fanout was rejected for Phase 8.

## 4. Phases

| Phase | Status | Scope |
|---|---|---|
| 1 — Foundation | ✅ Complete | Solution, Domain entities, Application abstractions, Infrastructure (DbContext + migrations), Api (Program, middleware, health, OpenAPI), 4 ADRs, Docker, tests. |
| 2 — Authentication | ✅ Complete | Eight auth endpoints, JWT (HS256), refresh-token rotation + family/reuse detection, email verification, password reset, account lockout, tenant-resolution middleware (HTTP), ADR-005. |
| 3 — Multi-Tenancy | ✅ Complete | Organization CRUD + soft-delete; Membership invite / role update / removal / ownership transfer; Permission model (const strings + role→permission mapping); cross-tenant guard (URL/header mismatch → 404); 8 cross-tenant integration tests (Tests 1-8); ADR-006, ADR-007. |
| 4 — Projects | ✅ Complete | Project aggregate + state machine; ProjectMember (Owner/Contributor/Reader); resource-level authorization (ProjectAccess helper); CRUD with filtering/sorting/pagination; project member management; migration; 6 project authorization integration tests; ADR-008. |
| 5 — Tasks | ✅ Complete | TaskItem aggregate + state machine (Todo/InProgress/Review/Done/Cancelled); TaskPriority; CRUD with filtering/sorting/pagination; assignee rules (must be project member); resource-level authorization via ProjectAccess; 5 task security integration tests. |
| 6 — Events & Notifications | ✅ Complete | Transactional Outbox (SaveChangesAsync intercepts domain events → outbox rows in same transaction); JsonEventSerializer; OutboxProcessor (background polling + RabbitMQ publish); RabbitMqPublisher (durable exchange, persistent messages); Notification entity + persistence; SignalR NotificationHub (authenticated, per-user delivery); DeadlineReminderWorker; TokenCleanupWorker; Notification API; ADR-009. |
| 7 — Redis | ✅ Complete | RedisCacheService (cache-aside, fail-open); cache keys `organization:{id}`, `project:{id}`; configurable TTL; invalidation on mutations (org update/delete, project update/delete); RedisRateLimiter (INCR+EXPIRE, distributed, fail-open); RedisRateLimitMiddleware; ADR-010; docker-compose Redis service. |
| 8 — Audit Logs | ✅ Complete | AuditLog domain entity (nullable OrganizationId for auth events); IAuditService Application abstraction (transactional Add + SaveChangesAsync); AuditService implementation reading ambient user/tenant/IP; AuditAction constants covering all spec §25 examples + additional spec-mandated operations; AuditLogConfiguration with 4 indexes (org+timestamp, user+timestamp, org+action+timestamp, entity+entityid+timestamp); Phase8_AuditLogs migration; `audit_log.read` permission granted to Owner + Admin; GetAuditLogsQuery + AuditLogsController (org-scoped + my-activity self-service endpoint); audit wired into all auth handlers (Login success/fail, Logout, Password change, EmailVerified, RefreshTokenRotated), member handlers (Invite, Remove, RoleChange, OwnershipTransfer), task handlers (Create, Update, StatusChanged, Delete, Assign), project handlers (Create, Update, Delete), organization handlers (Create, Update, Delete), comment handlers (Create, Edit, Delete), label handlers (Create, Delete), project member handlers (Add, Remove, RoleChange, OwnershipTransfer); ADR-011. |
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
