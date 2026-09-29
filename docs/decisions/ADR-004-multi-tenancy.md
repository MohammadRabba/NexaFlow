# ADR-004: Shared Database, Shared Schema, Row-Level Tenancy

- Status: Accepted
- Date: 2026-09-30
- Phase: 1 — Foundation

## Context

Multi-tenancy is one of the most important requirements (section 6). The
three canonical tenancy models are:

1. **Database-per-tenant** — each tenant gets its own PostgreSQL database.
2. **Schema-per-tenant** — each tenant gets its own schema inside a shared
   database (`tenant_a.users`, `tenant_b.users`).
3. **Shared database, shared schema, row-level discriminator** — every
   tenant-owned row carries an `organization_id` column; queries are
   scoped at runtime.

The selection affects:
- Operational complexity (migrations, backups, monitoring).
- Cost (database instances, connections, storage).
- Isolation guarantees (data leakage, noisy-neighbor problems).
- Performance per tenant at scale.

## Decision

Adopt **shared database, shared schema, row-level tenancy** with an
`OrganizationId` discriminator column on every tenant-owned entity.

### Defense-in-depth (section 6)

Tenant isolation is enforced at **four layers**:

1. **Tenant resolution.** The `ICurrentTenantService` abstraction
   resolves the tenant id for the current scope. Resolution is performed
   by an `ITenantResolutionStrategy` — Phase 1 ships the abstraction
   only; Phase 2 adds the HTTP middleware implementation that reads a
   candidate from JWT claims or `X-Organization-Id` header, **and
   validates it against the user's organization memberships**.
   Client-supplied OrganizationId is data, not authority.

2. **Authorization.** Permission-based authorization (section 9)
   checks that the user has a role in the resolved organization that
   grants the requested permission. A user cannot act on a resource in
   another tenant — even with the right tenant id in the request — if
   they don't have membership.

3. **Application-level tenant validation.** Command handlers always
   construct tenant-owned entities with the tenant id from
   `ICurrentTenantService.OrganizationId`. They never accept an
   `organizationId` parameter from the client. The DbContext's
   `SaveChangesAsync` override **rejects** any tenant-owned entity whose
   `OrganizationId` does not equal the resolved tenant id.

4. **EF Core global query filters.** Every `ITenantEntity` gets a query
   filter that scopes all reads to the current tenant. When no tenant is
   resolved (host-scope background job, unauthenticated endpoint), the
   filter excludes ALL tenant rows — **fail-closed, never leak**.

5. **Database constraints.** Unique indexes include `organization_id`
   where appropriate (e.g., `(organization_id, user_id)` for memberships)
   so cross-tenant duplicates cannot persist even if a higher layer has
   a bug.

### Cross-tenant leak avoidance (section 27)

When a user requests a resource in another tenant, we return **404
Not Found** — not 403. Returning 404 avoids confirming that the resource
exists in another tenant. This is consistent with section 8's
"security-conscious error messages" and is tested in Phase 3.

## Alternatives Considered

- **Database-per-tenant.** Strongest isolation; simplest "blast radius"
  for failures. But N databases × M migrations × K backups is a
  significant operational cost. Connection pool sizing across N
  databases is non-trivial. Migration rollouts across hundreds of
  tenants must be coordinated. Rejected for NexaFlow's expected tenant
  count and team size.
- **Schema-per-tenant.** Middle ground. PostgreSQL supports it natively.
  Migrations are still per-tenant. Cross-tenant queries (rare in NexaFlow
  but useful for analytics, support tooling, multi-tenant reports) require
  UNION ALL across schemas — annoying. Rejected.
- **Database-per-tenant only for "enterprise" tenants.** Hybrid model.
  Adds code complexity for marginal benefit. Rejected until a real
  tenant demands physical isolation.

## Consequences

- **Positive:** One database, one connection pool, one migration path,
  one backup strategy. Simplest possible operations.
- **Positive:** Adding a new tenant is a single INSERT — no DDL, no
  provisioning, no restart.
- **Positive:** Global queries (e.g., "how many users total?") are
  trivial.
- **Negative:** A single slow query from one tenant can degrade
  performance for all tenants (noisy neighbor). Mitigation: rate limiting
  per user (section 23), per-tenant query timeout configuration in
  later phases, partitioning by `organization_id` for large tables in
  Phase 8 (audit_logs is the prime candidate).
- **Negative:** Cross-tenant data leakage is a high-impact security
  incident if any of the five layers has a bug. Mitigation: extensive
  cross-tenant integration tests in Phase 3, and the multi-layer
  defense ensures that a single bug is unlikely to leak data alone.
- **Negative:** Migrations touch every tenant's data at once. Migration
  design must keep this in mind — non-blocking DDL where possible
  (PostgreSQL 16 supports `ADD COLUMN ... DEFAULT ...` without table
  rewrite for most cases).
- **Risk:** If a future tenant requires physical isolation (e.g.,
  regulated industry, contractual requirement), the migration is
  non-trivial. Documented for future revision. The migration path:
  create dedicated database → add a `Database-per-tenant` strategy
  alongside the existing row-level strategy → tenant resolution
  dispatches based on the tenant's assigned strategy → run a one-time
  data extraction job.
