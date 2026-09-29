# ADR-003: Prefer a Modular Monolith over Microservices

- Status: Accepted
- Date: 2026-09-30
- Phase: 1 — Foundation

## Context

Section 1 of the master prompt mandates: *"Do not add Kafka, Kubernetes,
Elasticsearch, microservices, etc. unless a concrete architectural
requirement emerges. Prefer a Modular Monolith over premature
Microservices."*

NexaFlow at launch is a single product with a small user base. Operational
costs of microservices include:
- Distributed transactions / saga coordination (or eventual consistency
  with compensating actions).
- Network-failure modes at every boundary.
- Multi-service tracing, metrics, and log aggregation overhead.
- Independent deployment of N services with N build pipelines.
- Cross-service data ownership disputes.

## Decision

Adopt a **Modular Monolith**: a single deployable ASP.NET Core Web API
with internal module boundaries. Modules are namespaces, not separate
deployables. Each module owns its own data, exposes a typed in-process
contract to other modules, and never shares a database table.

Phase-by-phase modules that will emerge:
- **Auth** — registration, login, JWT, refresh tokens (Phase 2).
- **Organizations** — tenant lifecycle, membership, roles (Phase 3).
- **Projects** — CRUD, members, filtering (Phase 4).
- **Tasks** — CRUD, assignment, status, labels, comments (Phase 5).
- **Notifications** — outbox consumer, SignalR hub, delivery (Phase 6).
- **AuditLogs** — read-side query + write-side hook (Phase 8).
- **Observability** — health checks, metrics, tracing (Phase 9).

Each module's namespace boundary is enforced via `internal` access
modifiers on application services and `InternalsVisibleTo` only for tests.

## Alternatives Considered

- **Microservices from day one.** Explicitly rejected by the master
  prompt. Premature decomposition slows feature delivery and adds
  operational overhead with no proven payoff.
- **Multi-process modular monolith.** Like Shopify's "Modular
  Monolith with multiple Rails apps" — useful at large scale but
  overkill for a portfolio project.
- **Single project with no module boundaries.** Rejected — without
  boundaries, a monolith degrades into a "Big Ball of Mud" rapidly.

## Consequences

- **Positive:** Single deployable. Trivial local dev (`docker compose up`).
  Atomic cross-module transactions are possible (e.g., creating an
  Organization and its Owner membership in one transaction — section 14).
- **Positive:** Module boundaries are enforced via namespaces and
  `internal` types, so the architecture stays honest as the codebase grows.
- **Positive:** If a real boundary emerges (e.g., Notifications needs to
  be extracted because it has very different scaling characteristics),
  the module is already a clean candidate for extraction.
- **Negative:** The single deployable limits independent scaling of
  modules. For NexaFlow's scale, this is not a problem.
- **Negative:** Cross-module changes affect the same deployable, so the
  team must be disciplined about not breaking module boundaries.
- **Risk:** "Module boundary erosion" — developers may accidentally
  reach into another module's internals. Mitigation: `internal` access
  + code-review checklist item: "Does this PR cross a module boundary?"
