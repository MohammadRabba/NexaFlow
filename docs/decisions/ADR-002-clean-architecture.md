# ADR-002: Clean Architecture with Four Projects

- Status: Updated in Phase 2
- Original Date: 2026-09-30
- Updated: 2026-09-30 (Phase 2 architectural correction)
- Phase: 2 — Authentication

## Context

NexaFlow uses Clean Architecture with four projects. The dependency
direction was originally set up in Phase 1:

```
NexaFlow.Api            ──▶ NexaFlow.Application
                        ──▶ NexaFlow.Infrastructure   (DI wiring only)

NexaFlow.Application    ──▶ NexaFlow.Domain

NexaFlow.Infrastructure ──▶ NexaFlow.Application  (implements abstractions)
                        ──▶ NexaFlow.Domain            (entity shape)

NexaFlow.Domain         ──✗─ (anything infrastructure)
```

### Phase 2 Architectural Correction

At the start of Phase 2 the user reviewed the Phase 1 completion report
and directed: *"Fix the Application → EF Core dependency if practical."*

The Phase 1 `IApplicationDbContext` exposed `DbSet<T>` properties,
forcing the Application project to reference `Microsoft.EntityFrameworkCore`.
This was an architectural impurity — a project that should depend only on
abstractions and the Domain layer was depending on the EF Core runtime.

## Decision

**Updated Phase 2 decision:** Application no longer references EF Core.
The `IApplicationDbContext` abstraction exposes:

1. **`IQueryable<T>` properties** for read-side composition (BCL — `System.Linq`).
2. **Named async query methods** for queries that justify a method-wrapped
   implementation (Phase 2's auth queries: `FindUserByNormalizedEmailAsync`,
   `FindUserByIdAsync`, `EmailIsInUseAsync`, `FindRefreshTokenByHashAsync`,
   `CountActiveRefreshTokensInFamilyAsync`).
3. **`Add<T>` / `Remove<T>` mutation methods** — stage changes for the next
   `SaveChangesAsync` call.

### Trade-off Analysis

| Path | Application ref | Cost | Benefit |
|---|---|---|---|
| **A.** Keep `DbSet<T>` (Phase 1) | EF Core runtime | 0 | Full LINQ composability |
| **B.** `IQueryable<T>` + named methods (Phase 2) | None (BCL only) | Lose direct `Include`, `AsNoTracking`, `TagWith`, `IgnoreQueryFilters` in Application | Clean architecture |
| **C.** Wrap everything behind repositories | None | High — violates Rule 38 ("don't create a repository abstraction over EF Core") | Extreme isolation |

**Path B was chosen** for the following reasons:
- Phase 2 auth queries are simple lookups (`Where + SingleOrDefault`, `Any`)
  that do not require eager loading or read-only optimizations.
- When a later phase introduces queries that genuinely require `Include`
  (e.g., Project → Members, Task → Comments), we will add **named query
  methods** on `IApplicationDbContext` (the "thin repository" pattern):
  `Task<ProjectWithMembersDto?> GetProjectWithMembersAsync(...)`. Only
  queries that justify it get a method-wrapped form. Simple queries keep
  using `IQueryable<T>` directly.
- The "thin repository" extension path is documented here so future
  contributors know the rule: don't reintroduce EF Core to Application;
  add a named method on the abstraction instead.

### Example: a method-wrapped complex query (Phase 4 preview)

When Phase 4 introduces `ProjectWithMembers` reads, the abstraction will
look like:

```csharp
public interface IApplicationDbContext
{
    // Simple reads — BCL IQueryable<T>
    IQueryable<User> Users { get; }
    IQueryable<Project> Projects { get; }

    // Named async queries — wrapped because they need Include
    Task<ProjectWithMembersDto?> FindProjectWithMembersAsync(
        Guid projectId, CancellationToken ct = default);
}
```

The Infrastructure implementation uses `Include` internally:

```csharp
public Task<ProjectWithMembersDto?> FindProjectWithMembersAsync(Guid id, CancellationToken ct)
    => Projects
        .Where(p => p.Id == id)
        .Select(p => new ProjectWithMembersDto(...))  // DTO projected at SQL layer
        .SingleOrDefaultAsync(ct);  // EF Core extension — internal to Infrastructure
```

The Application layer never sees `Include`, `AsNoTracking`, or any other
EF Core extension. The contract stays BCL-only.

## Alternatives Considered (Phase 2 review)

- **Path A** (keep `DbSet<T>`): Rejected — user explicitly asked to fix the
  dependency; the cost was small for Phase 2 queries.
- **Path C** (full repositories): Rejected — violates Rule 38; adds boilerplate
  without architectural benefit.
- **Vertical slices**: Rejected — same reasons as Phase 1 (cross-cutting
  concerns are easier to enforce per-layer than per-slice).

## Consequences

- **Positive:** Application is EF Core-free. Application unit tests don't
  transitively pull in EF Core, making them faster and lighter.
- **Positive:** Replacing EF Core with a different ORM (e.g., Dapper) would
  touch only Infrastructure, not Application.
- **Negative:** Adding new query patterns requires updating the
  `IApplicationDbContext` interface AND the Infrastructure implementation.
  Mitigation: only add methods when a query genuinely needs wrapping.
- **Negative:** Application authors must learn the "named query method" pattern
  for complex queries instead of composing `Include` chains inline. The
  rule is simple and documented above; review checklist enforces it.

## Phase 1 Decision (unchanged)

The original Clean Architecture layering from Phase 1 — four projects
(Domain, Application, Infrastructure, Api) with strict dependency direction —
remains the architecture's foundation.
