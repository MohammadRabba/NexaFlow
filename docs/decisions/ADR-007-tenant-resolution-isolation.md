# ADR-007: Tenant Resolution and Isolation Strategy

- Status: Accepted
- Date: 2026-09-30
- Phase: 3 — Multi-Tenancy

## Context

ADR-004 (Phase 1) chose shared-database, shared-schema, row-level
`OrganizationId` tenancy. Phase 3 must answer HOW the tenant is resolved
per-request and HOW cross-tenant leaks are prevented.

The Phase 3 directive (points 4, 5, 7, 9) is explicit:
- The header `X-Organization-Id` is NEVER authoritative.
- The database is authoritative for current membership.
- Tenant resolution must fail closed.
- "Do not claim that global query filters alone provide complete security."
- Cross-tenant access must return 404 (no enumeration leak).

## Decision

### 1. Five-layer defense-in-depth

1. **TenantResolutionMiddleware** (API layer): reads `X-Organization-Id`,
   validates against the user's current DB membership, pushes the resolved
   tenant into `CurrentTenantService`. Rejects with 404 on non-membership.
2. **`PermissionAuthorizationHandler`** (Infrastructure): on every
   `[Authorize(Policy = Permissions.X)]`, re-queries the DB to read the
   user's current role in the resolved tenant and checks the role-permission
   mapping. Database authoritative, JWT carries no role claims.
3. **Cross-tenant guard in handlers**: `ICurrentTenantService.EnsureMatchesTenantId
   (expectedOrgId)` is called at the start of every handler that takes
   `organizationId` from the URL. Throws `NotFoundException` (→ 404) if
   the resolved tenant differs from the URL's org id. Prevents the
   URL/header-mismatch attack vector.
4. **EF Core global query filter**: every `ITenantEntity` gets a query
   filter scoped to the resolved tenant. Fail-closed when no tenant
   resolved (returns no rows).
5. **`SaveChangesAsync` override**: rejects insert of a tenant-owned
   entity whose `OrganizationId` differs from the resolved tenant;
   rejects mutation of `OrganizationId` on update (immutable).

### 2. Fail-closed status code mapping

| Scenario | Status | Error code |
|---|---|---|
| Not authenticated | 401 | (handled by `[Authorize]`) |
| Authenticated, no `X-Organization-Id` for a tenant-scoped endpoint | 404 | `NOT_FOUND` |
| `X-Organization-Id` malformed | 400 | `INVALID_TENANT_HEADER` |
| User not a member of the requested org | 404 | `TENANT_NOT_FOUND` |
| URL `organizationId` ≠ resolved tenant | 404 | `NOT_FOUND` |
| Permission not granted by the role | 403 | (handled by authorization handler) |

404 is chosen over 403 for "not a member" because returning 403 would
confirm the organization exists in another tenant (enumeration leak).
The directive point 5 explicitly endorses this.

### 3. Where `IgnoreQueryFilters()` is allowed

There are exactly three places where the global tenant filter is bypassed,
all carefully justified:

1. **`TenantResolutionMiddleware`**: queries `OrganizationMembers` to
   validate membership. We're RESOLVING the tenant, so the filter (which
   depends on the resolved tenant) would exclude all rows.
2. **`PermissionAuthorizationHandler`**: same reason — we're checking
   membership in the resolved tenant, the filter is unhelpful.
3. **`IApplicationDbContext.FindMembershipAsync`**: bypasses the filter
   to look up a specific (org, user) pair, used by handlers that need
   to read the actor's role directly. The cross-tenant guard
   (`EnsureMatchesTenantId`) runs FIRST, so by the time `FindMembershipAsync`
   is called, the URL's org id has been validated to match the resolved tenant.

`FindOrganizationWithMembersAsync` also bypasses the filter — it loads all
members of a specific org. This is only called AFTER the cross-tenant guard
runs, so the URL's org id has been validated.

### 4. Client-supplied `OrganizationId` is never authoritative

No command handler accepts an `OrganizationId` from the request body. The
`organizationId` parameter on Members/Organizations endpoints is from the
URL route, validated against the resolved tenant. The CreateOrganization
command derives the new org's Owner from the current user (not from the
request body). The InviteMember command takes `OrganizationId` from the
URL only.

## Alternatives Considered

- **Trust the JWT's `orgs` claim** — explicitly rejected by Phase 2
  directive: "Do not use JWT claims as the authoritative source for
  current permissions or organization membership if that would allow
  stale authorization after membership/role changes." Membership revoked
  after the access token was issued must take effect on the next request.
  The cost: one DB round-trip per authorization check.

- **Use only the URL's `organizationId`, drop the `X-Organization-Id`
  header** — would simplify the model but break endpoints that don't have
  an org id in the URL (e.g., `GET /api/organizations` — list user's orgs,
  `POST /api/organizations` — create new). The header is the unified
  mechanism for "what tenant am I acting in?".

- **Per-tenant database connection (swap the connection string per
  request)** — would give physical isolation. ADR-004 considered and
  rejected this; the row-level approach is the chosen trade-off.

- **Use a single `IAuthorizationHandler` interface with strategy pattern**
  — premature abstraction. ASP.NET Core's `AuthorizationHandler<TRequirement>`
  + named policies is sufficient.

## Consequences

- **Positive**: Cross-tenant leaks require simultaneous bugs in MULTIPLE
  layers (tenant middleware + cross-tenant guard + global query filter +
  SaveChanges override + DB unique constraint).
- **Positive**: Membership / role changes take effect immediately on the
  next request — no JWT re-issuance needed.
- **Negative**: One DB round-trip per authorization check. Indexed
  lookups; ~1 ms. Phase 7 may add a Redis cache with short TTL.
- **Negative**: The 404-instead-of-403 choice means a legitimate user who
  types the wrong org id gets a 404 that looks identical to "not a member".
  Trade-off: defense-in-depth against enumeration is more important than
  debuggability for the typo case.

## Verification

Cross-tenant isolation tests (8 scenarios from Phase 3 directive point 10)
are implemented in `tests/NexaFlow.IntegrationTests/Tenant/CrossTenantIsolationTests.cs`.
They run against a real PostgreSQL instance via Testcontainers when Docker
is available. In this sandbox (no Docker), they fail with an explicit
"Docker unavailable — test implemented but not executed against PostgreSQL"
message (per directive point 15: tests are NOT reported as passed when
not actually executed).
