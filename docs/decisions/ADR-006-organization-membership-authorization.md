# ADR-006: Organization Membership and Authorization Model

- Status: Accepted
- Date: 2026-09-30
- Phase: 3 — Multi-Tenancy

## Context

Phase 3 introduces organization management, membership, and authorization. We
must answer three questions:

1. **What business invariants does the Organization aggregate enforce?**
   E.g., "exactly one Owner", "cannot remove the Owner", "ownership
   transfer is atomic".
2. **Where do the authorization rules live?** E.g., "Admin cannot modify
   another Admin", "Member cannot invite", "Owner can transfer ownership".
3. **How are permissions expressed and enforced?** The master prompt
   (section 9) demands permission-based (not role-only) authorization.
   The Phase 3 directive demands no `if (role == Owner)` scattered in code.

## Decision

### 1. Domain invariants live on the Organization aggregate

The Organization aggregate enforces cross-member invariants:

- `AddMember(userId, role, atUtc)` — rejects duplicates, rejects Owner/None role.
- `RemoveMember(userId, atUtc)` — refuses to remove the current Owner
  (must transfer first).
- `TransferOwnership(toUserId, byUserId, atUtc)` — atomically promotes the
  target to Owner and demotes the previous Owner to Admin. Updates
  `Organization.OwnerUserId` (denormalized).

OrganizationMember enforces single-member invariants:

- `ChangeRole(newRole, ...)` — refuses Owner role (must use TransferOwnership).
- `AcceptInvitation(presentedTokenHash, atUtc)` — single-use hash consumption.
- `CreatePendingInvite(...)` — factory for invitation flow (cannot assign Owner).

Authorization rules (who can call these methods) do NOT live in the domain —
the domain doesn't make policy decisions about who-can-do-what, only state
invariants about the aggregate.

### 2. Authorization rules live in the Application handlers

Each Members/Organizations handler enforces the relevant authorization rules
inline at the start of `Handle`. Examples:

- `UpdateMemberRoleCommandHandler`: Owner can change anyone's role (except
  another Owner's); Admin can change Member/Viewer roles but NOT another
  Admin's; Member/Viewer cannot change roles at all.
- `RemoveMemberCommandHandler`: Owner cannot self-remove (must transfer first);
  non-Owner can self-remove (leave voluntarily); removing someone else
  requires Owner or Admin role; Admin cannot remove another Admin.

These checks are not delegated to a generic `IAuthorizationService` indirection
— they're inline, readable, and tested.

### 3. Permissions are const strings; role→permission mapping is one static class

- `Permissions` class: `const string` for each permission
  (`organization.read`, `member.invite`, etc.).
- `RolePermissions.For(role)` returns the set of permission strings granted
  to that role. Single source of truth — no scattered `if (role == Owner)`.
- `PermissionAuthorizationHandler` (in Infrastructure) queries the DB on
  every check to read the user's current role in the resolved tenant —
  database authoritative, JWT carries no role claims (per Phase 2 directive).
- The handler succeeds if the user is an active member of the resolved
  tenant and the role grants the required permission; otherwise the handler
  fails (the API maps failed authorization to 403, or 401 if unauthenticated).

### 4. ASP.NET Core named policies = permission strings

At DI time, `AddInfrastructure` calls `services.AddAuthorization` and
registers a named policy for each permission string. Controllers use
`[Authorize(Policy = Permissions.MemberInvite)]` — clean, no framework
indirection.

## Alternatives Considered

- **Generic `IAuthorizationService` with requirement objects** — would
  require an interface, a handler, and a requirement class per permission.
  Overkill for the current matrix (16 permissions). Rejected; the inline
  approach is more readable.

- **Role-based checks inline in handlers** (`if (user.Role == Owner)`) —
  directly violates the Phase 3 directive. Roles would be scattered; adding
  a new role would require finding every check. Rejected.

- **Permissions as enum** — works, but enum values can't be claimed
  strings in ASP.NET Core authorization without conversion. Const strings
  are simpler and integrate cleanly.

- **Policy server / external authorization** — premature for a single
  deployable. The model is designed so that switching to an external
  policy server in the future only touches the `RolePermissions` mapping
  and the `PermissionAuthorizationHandler` — not the handlers or domain.

## Consequences

- **Positive**: Authorization rules are co-located with the operation they
  guard — easy to audit "who can call `UpdateMemberRole`?" by reading the
  handler.
- **Positive**: Adding a new permission is a one-line addition to `Permissions`
  and the role mappings; the policy is auto-registered.
- **Positive**: Database is authoritative for roles — revoking membership
  takes effect on the next request, no JWT re-issuance needed.
- **Negative**: One DB round-trip per authorization check. Acceptable
  (indexed lookup, ~1 ms). Phase 7 may add a Redis-backed membership cache
  with short TTL if this becomes a hot path.
- **Negative**: Authorization rules are inline in handlers, not unit-testable
  in isolation without instantiating the handler. We mitigate by writing
  integration tests against the real pipeline (Test 5 in the cross-tenant
  tests verifies role change effectiveness).

## Concurrency (DEFERRED)

Per Phase 3 directive point 11: optimistic concurrency on OrganizationMember
was investigated but DEFERRED. See the comment in
`OrganizationMemberConfiguration.cs` for the rationale:
- Concurrent invites: protected by the (organization_id, user_id) unique constraint.
- Concurrent removes: the second remover hits the domain-level "not a member" guard.
- Concurrent role changes / ownership transfers: last-write-wins; the "exactly one
  Owner" invariant is preserved, but admin A's intent may be silently lost.

The Npgsql `xmin-as-concurrency-token` pattern is documented but cannot be
verified without a real PostgreSQL instance in this sandbox. When Docker is
available in CI, we'll re-add the configuration and ship the migration.
