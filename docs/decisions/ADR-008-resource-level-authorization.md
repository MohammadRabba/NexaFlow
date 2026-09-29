# ADR-008: Resource-Level Authorization for Projects

- Status: Accepted
- Date: 2026-09-30
- Phase: 4 — Projects

## Context

Phase 3's authorization model is organization-level: a user holds a role
(Owner/Admin/Member/Viewer) in an organization, and that role grants a set
of permissions (project.read, project.create, etc.). The
`PermissionAuthorizationHandler` queries the DB on every request to check
the user's role in the resolved tenant.

Phase 4 introduces a new requirement: a user with `project.update` must
NOT automatically mean they can update every project in the organization.
The directive point 7 is explicit:

> A user having project.update must NOT automatically mean they can
> update every project in the organization. The authorization model
> should distinguish:
> - organization-level permission
> - project-level access
> - project ownership/member rules

## Decision

**Two-tier authorization**: organization-level (Phase 3) + resource-level (Phase 4).

### Organization-level (unchanged from Phase 3)

- `[Authorize(Policy = Permissions.ProjectRead)]` on the controller
- `PermissionAuthorizationHandler` queries the DB for the user's role in the
  resolved tenant + checks `RolePermissions.Has(role, permission)`
- Used for: Create (needs `project.create`), List (needs `project.read`)

### Resource-level (new in Phase 4)

- The handler calls `ProjectAccess.LoadAndAuthorizeAsync(projectId, minimumRole, ct)`
- `ProjectAccess` is a small, stateless, DI-registered class — NOT a base class,
  NOT a generic framework. It's shared code for the 5 project mutation handlers.
- `LoadAndAuthorizeAsync` performs 5 checks in sequence:
  1. Authenticated user (defense in depth)
  2. Tenant resolved (`RequireTenantId` throws if not)
  3. Load the project with its Members (bypasses the tenant filter — justified
     because we need to LOAD the project to verify it belongs to the tenant)
  4. Cross-tenant guard: `project.OrganizationId` must equal the resolved tenant.
     If not → `NotFoundException` (→ 404, no enumeration leak)
  5. Resource-level check: the user must be an active member of THIS project
     with at least the `minimumRole`. If not → `NotFoundException` (→ 404).
- Returns the loaded project for the handler to mutate.

### Role hierarchy

`ProjectMemberRole` has a strict hierarchy: Owner > Contributor > Reader.

- **Owner** — full project control: rename, change status, manage members,
  delete, transfer ownership.
- **Contributor** — edit project details (name, description, status, dates);
  cannot manage members or delete.
- **Reader** — read-only.

The `RoleGrants(held, required)` helper encodes this hierarchy. Adding a
new role means updating one switch expression.

### Where the checks live

- **Organization-level**: controller attributes (`[Authorize(Policy = ...)]`)
- **Resource-level**: Application-layer handlers (inline, via `ProjectAccess`)
- **Domain invariants**: the Project aggregate (e.g., "cannot remove Owner",
  "status transitions validated by state machine")

This three-way split mirrors Phase 3's pattern:
- Policy decisions (who can call X) → Application handlers
- State invariants (the aggregate is always valid) → Domain

## Alternatives Considered

- **ASP.NET Core resource-based authorization (`IAuthorizationService.AuthorizeAsync(user, project, requirement)`)** — would load the resource in the controller,
  then call the authorization service. Downside: puts DB access in the controller
  (violates ADR-002 — Application layer owns DB access). Also requires
  `AuthorizationHandler<TRequirement, Project>` — more ceremony than the
  inline check.

- **Custom `AuthorizationFilter<T>` that loads the resource from route data** —
  premature abstraction for the current scale. The `ProjectAccess` helper is
  simpler and co-located with the handlers that use it.

- **Single combined check (org-level + project-level in one handler)** —
  would require the handler to know both the organization role and the project
  role. The two checks are conceptually distinct (org-level = "can you access
  this tenant?" vs project-level = "can you act on this specific resource?").
  Keeping them separate makes the failure modes clearer (404 for either = same
  response, but the log message distinguishes them).

## Consequences

- **Positive**: The organization-level `project.update` permission grants the
  ABILITY to update projects — but the resource-level check restricts WHICH
  projects. A Member with `project.update` (org-level) can only update
  projects they're a Contributor or Owner of (resource-level).
- **Positive**: All failures return 404 (not 403) — the attacker can't
  distinguish "project doesn't exist", "wrong tenant", "not a member", or
  "insufficient role". Same response shape for all.
- **Negative**: Each project mutation requires loading the project + its
  members from the DB. Acceptable: it's an indexed lookup, and the loaded
  entity is used for the mutation anyway (no extra round-trip).
- **Negative**: The `ProjectAccess` helper is shared across 5 handlers. If
  a future phase needs different resource-level rules (e.g., task-level
  authorization), a similar `TaskAccess` helper would be needed. Acceptable —
  the pattern is explicit and tested.

## Verification

6 project authorization integration tests in
`tests/NexaFlow.IntegrationTests/Projects/ProjectAuthorizationTests.cs`:
- Create + GET (happy path)
- Cross-tenant access (404)
- Non-member in same org (404)
- Reader cannot update (404)
- Client-injected OrganizationId ignored
- OrganizationId is immutable across mutations

Plus 10 Application-layer unit tests in
`tests/NexaFlow.Application.Tests/Projects/ProjectCommandHandlerTests.cs`
covering the same scenarios with the in-memory test double.

All tests require Docker (Testcontainers PostgreSQL) to execute against a
real database. In this sandbox (no Docker), they fail with an explicit
"Docker unavailable — test implemented but not executed" message.
