# ADR-011: Audit Logging (Phase 8)

- Status: Accepted
- Date: 2026-09-30
- Phase: 8 — Audit Logs

## Context

Section 25 of the master specification requires recording security- and
business-sensitive operations to an audit log. The required record fields are:

- UserId
- OrganizationId
- Action
- Entity
- EntityId
- OldValues
- NewValues
- IPAddress
- Timestamp

Phase 8 (per the implementation plan) covers:
- CRUD-sensitive operation auditing
- Permission changes
- Member changes
- Authentication events

The spec §14 transactional example explicitly lists "Create Audit Log" alongside
"Create Project + Add Project Member" — they must succeed or fail together.

The spec §25 warns: "Never put passwords or authentication secrets in audit logs."

## Decision

### A dedicated `AuditLog` domain entity

`NexaFlow.Domain.Entities.AuditLog` is a domain entity (spec §5 lists it alongside
the other domain entities). It inherits from `AuditableEntity` (timestamps +
created-by) but **NOT** from `AggregateRoot` — audit rows are immutable history
and never soft-deleted.

**OrganizationId is nullable.** Auth events (`LoginSucceeded`, `LoginFailed`,
`Logout`, `PasswordChanged`, `EmailVerified`, `RefreshTokenRotated`) occur
before tenant resolution — there is no ambient organization to record. All other
events (task / project / member / org mutations) carry the resolved tenant.

**AuditLog does NOT implement `ITenantEntity`.** This is intentional:
auth events span tenants, so the global query filter does not apply. The
Application-layer query methods filter explicitly by `organization_id`
(tenant-scoped view) or `user_id` (cross-tenant self-service view).

### Canonical action names

`NexaFlow.Domain.Entities.AuditAction` is a static class of `const string` values
covering every action the handlers record. Centralizing them prevents typos and
makes the audit log auditable for "what events can exist?". Spec §25 examples
are all covered (`TaskCreated`, `TaskUpdated`, `TaskDeleted`, `TaskStatusChanged`,
`MemberInvited`, `MemberRemoved`, `RoleChanged`, `LoginSucceeded`, `LoginFailed`,
`PasswordChanged`), plus a small number of additional operations the spec
explicitly lists as auditable elsewhere (project / org / comment / label
mutations, ownership transfers, project-member role changes, refresh-token
rotation, email verification).

### `IAuditService` Application abstraction

```csharp
public interface IAuditService
{
    Task RecordAsync(
        string action,
        string entity,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        Guid? actorUserIdOverride = null,
        Guid? organizationIdOverride = null,
        CancellationToken cancellationToken = default);
}
```

**Transactional contract:** the implementation calls
`IApplicationDbContext.Add<AuditLog>(entry)`. The handler then calls
`SaveChangesAsync` once — persisting both the business mutation AND the audit
row atomically. If the transaction fails, the audit row is also rolled back
(unless the handler explicitly persists before throwing — used only for
`LoginFailed` for non-existent users, where the audit row IS the only state
change).

**Override parameters** exist for two reasons:
1. **Auth events** have no ambient `ICurrentUserService` populated (the user
   is being authenticated at that moment). The handler passes the loaded
   user's id explicitly via `actorUserIdOverride`.
2. **`OrganizationCreated`** is a pre-tenant operation — the org is being
   created. The handler passes the new org's id via `organizationIdOverride`
   so the audit row is associated with the new org for later retrieval.

### Secrets policy

The `IAuditService` implementation does NOT redact. The caller is responsible
for redacting secrets BEFORE passing `oldValues` / `newValues`. This is
intentional:
- The implementation cannot know which fields are sensitive for any given entity.
- Forcing the caller to redact forces them to think about it.
- Auth handlers (`LoginSucceeded`, `LoginFailed`, `PasswordChanged`,
  `EmailVerified`, `Logout`, `RefreshTokenRotated`) record NO payload —
  the action name + entity id + timestamp is sufficient context, and there
  are no safe fields to record (the password hash is a credential-like secret
  even after hashing; the refresh token plaintext is one-time).

### AuditLog.Read permission + role matrix

A new permission `audit_log.read` (per spec §9 — `AuditLog.Read` listed
alongside other permissions) is granted to **Owner** and **Admin** roles only.
Viewer and Member cannot read the organization's audit log.

A separate `GET /api/audit-logs/my-activity` endpoint is available to any
authenticated user, scoped to their own user id — this is the self-service
"my login activity" view. It does NOT require `AuditLog.Read` because users
can always see their own auth history.

## Architecture

### Data flow

```
HTTP request
   ↓
Handler loads entity, mutates state
   ↓
Handler calls IAuditService.RecordAsync(...)
   ↓ AuditLog.Create(...) → IApplicationDbContext.Add<AuditLog>(entry)
Handler calls IApplicationDbContext.SaveChangesAsync
   ↓ EF Core transaction commits:
     - business mutation (e.g., Project rename)
     - audit_log row (OrganizationId, Action, Entity, EntityId, OldValues/NewValues)
   ↓
Done. The audit row is persisted in the same transaction.
```

For auth events that occur BEFORE the ambient context exists (login for a
non-existent email, registration), the handler:
1. Loads the user (if any).
2. Calls `IAuditService.RecordAsync(...)` with explicit overrides.
3. Calls `SaveChangesAsync` to persist the audit row.
4. Throws the appropriate domain exception.

The audit row is persisted BEFORE the throw so the failure is recorded even
though the request fails.

### Database layout

The `audit_logs` table:
- `id` (uuid, primary key)
- `user_id` (uuid, nullable) — the actor; null for events with no known user
- `organization_id` (uuid, nullable) — null for auth events
- `action` (varchar(64))
- `entity` (varchar(64))
- `entity_id` (uuid, nullable)
- `old_values` (text, JSON)
- `new_values` (text, JSON)
- `ip_address` (varchar(45)) — IPv6 max length
- `occurred_at_utc` (timestamptz) — the timestamp captured at audit time
- `created_at_utc` / `updated_at_utc` (timestamptz) — from AuditableEntity
- `created_by_user_id` / `updated_by_user_id` (uuid, nullable)

Indexes (chosen for actual query patterns):
1. `(organization_id, occurred_at_utc)` — the primary "list recent org audit
   rows" query, sorted descending by time.
2. `(user_id, occurred_at_utc)` — the self-service "my activity" view.
3. `(organization_id, action, occurred_at_utc)` — filter by action within
   an org (e.g., "all MemberInvited events for Acme").
4. `(entity, entity_id, occurred_at_utc)` — full history of one resource.

No foreign keys to `users`, `organizations`, etc. Audit rows MUST survive the
deletion of the referenced entity (audit history is immutable, even when the
subject is deleted).

### Application-layer query surface

`IApplicationDbContext.GetPagedAuditLogsAsync(...)` — tenant-scoped, supports
filtering by action, user, entity, entity id, time window. Returns total +
items. Page size is clamped to `[1, 100]` per spec §29.

`IApplicationDbContext.GetPagedAuditLogsForUserAsync(...)` — user-scoped
(cross-tenant), used for the self-service view.

### API surface

Two endpoints:
- `GET /api/audit-logs` — org-scoped. Requires `audit_log.read` policy
  (Owner + Admin). Filters: `action`, `userId`, `entity`, `entityId`,
  `fromUtc`, `toUtc`, `page`, `pageSize`.
- `GET /api/audit-logs/my-activity` — user-scoped (self-service). Available
  to any authenticated user. Filters: `action`, `fromUtc`, `toUtc`, `page`,
  `pageSize`. The handler ignores any client-supplied `userId` and uses the
  ambient `ICurrentUserService.UserId` (spec §49: do not trust client-supplied
  values when the trusted context can derive them).

## Security considerations

1. **Tenant isolation.** The organization-scoped endpoint filters explicitly by
   `organization_id == resolved_tenant`. The query is fail-closed: if no tenant
   is resolved, the handler throws `TENANT_NOT_RESOLVED`. Integration tests
   verify Org A cannot see Org B's audit rows.

2. **Permission enforcement.** The `audit_log.read` permission is checked
   declaratively via `[Authorize(Policy = Permissions.AuditLogRead)]` on the
   controller. Only Owner + Admin roles have the permission.

3. **No secrets in audit payloads.** Auth events record no `oldValues` /
   `newValues`. The `PasswordChanged` audit row has no payload (never record
   old or new passwords). The `MemberInvited` audit row does NOT include the
   invitation token hash (a credential-like secret). The `RefreshTokenRotated`
   audit row does NOT include the token plaintext or hash — only the token id
   of the rotated row, for forensics.

4. **No client-supplied identity.** The `my-activity` endpoint always uses the
   ambient `ICurrentUserService.UserId` — the client cannot ask for someone
   else's audit history by manipulating the query string.

5. **IP address recording.** The `ICurrentUserService.IPAddress` property was
   already plumbed in Phase 2 "for audit logging (section 25)". The
   `AuditService` reads it from the ambient context.

6. **No PII in old/new values beyond what the business payload already
   contains.** The user's email, display name, etc., may appear in audit
   payloads for org-level mutations (e.g., `MemberInvited` records the
   invitee's user id and the proposed role). This is acceptable: audit logs
   are organization-scoped administrative records, not public data.

## Failure behavior

- **AuditService throws** (e.g., `AuditLog.Create` rejects an over-long action
  string): the exception propagates to the calling handler, which fails the
  entire transaction. Audit failures are NEVER silently swallowed — a failure
  to record audit is treated as a failure of the operation.

- **Database transaction fails**: both the business mutation and the audit row
  are rolled back together. This is the spec §14 contract.

- **Handler explicitly persists audit before throwing** (used for
  `LoginFailed` on non-existent email): the audit row is the only state
  change; persisting it before throw ensures the failure is recorded.

## Alternatives considered

### Asynchronous audit via RabbitMQ (spec §21 "Audit Processing" fanout)

Spec §21 mentions `TaskCompleted → Event → Audit Processing` as one of the
event fanout paths, suggesting audit could be a RabbitMQ consumer.

**Rejected for Phase 8.** The synchronous, transactional approach is simpler,
more secure (no message loss window), and satisfies spec §14's transactional
example exactly. The asynchronous fanout remains a future option for
high-throughput audit enrichment (e.g., joining with user display names from
the read model) without compromising the core audit row's atomicity.

### EF Core SaveChanges interceptor

A `SaveChangesInterceptor` could automatically record audit entries by
inspecting `ChangeTracker.Entries()` and serializing original/current values.

**Rejected.** This approach:
1. Cannot know the semantic action (e.g., is this `TaskUpdated` or
   `TaskStatusChanged`? Both modify a task, but the spec lists them as
   distinct audit actions).
2. Cannot redact secrets per entity type without per-entity configuration.
3. Records every property change, including non-business fields (e.g.,
   `UpdatedAtUtc`), producing noisy logs.
4. Loses the explicit `IAuditService.RecordAsync(...)` call site, which is
   the documentation that "this handler audits X".

The explicit handler-driven approach is preferred: the handler decides
WHAT to audit, with WHAT action, and WHAT payload (after redacting secrets).

### Separate audit-log table per resource type

Per-entity audit tables (`task_audit_logs`, `project_audit_logs`, etc.) would
allow per-type schema enforcement.

**Rejected.** A single `audit_logs` table with `entity` + `entity_id` columns
is simpler, supports cross-entity queries ("show me everything that happened
to user X"), and avoids the schema-migration burden of adding a new audit
table for every new entity. The `entity` column is a free-form string (not
a foreign key) so audit rows survive entity deletion.

## Consequences

- **Every audited handler** now takes `IAuditService` as a constructor
  dependency. This is a one-time cost; new handlers simply add it.
- **The audit_logs table** grows monotonically. Future phases may add
  retention / archival (not in scope for Phase 8).
- **The `AuditLog.Read` permission** is added to the `Owner` and `Admin`
  permission sets; `Member` and `Viewer` do NOT receive it.
- **Auth events** have `organization_id IS NULL`. The cross-tenant
  `my-activity` query is the only path that returns them.
- **The DI container** registers `IAuditService` as scoped in
  `AddApplication()`, sharing the ambient `ICurrentUserService` /
  `ICurrentTenantService` of the calling handler.
