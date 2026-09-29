# ADR-001: Use PostgreSQL as the Primary Relational Database

- Status: Accepted
- Date: 2026-09-30
- Phase: 1 — Foundation

## Context

NexaFlow is a multi-tenant SaaS that needs a primary relational store. The
choice of database affects data integrity, operational complexity, hosting
cost, and the ecosystem of tooling available for migrations, observability,
and disaster recovery.

The application's data access needs include:
- Strong relational consistency (foreign keys, unique constraints, transactions).
- A native `uuid` type for primary keys.
- A native `timestamptz` type for audit fields.
- `JSONB` columns for audit-log old/new values (section 25).
- Global query filters scoped by tenant (section 27) — supports indexes on
  the `organization_id` column.
- High concurrency with optimistic concurrency tokens (section 26).

## Decision

**Use PostgreSQL 16** (or newer) as the primary relational database for
NexaFlow.

The database is accessed exclusively through EF Core using the
`Npgsql.EntityFrameworkCore.PostgreSQL` provider. Snake-case naming is
applied via `EFCore.NamingConventions` so all tables, columns, indexes,
and constraints follow PostgreSQL idioms.

## Alternatives Considered

- **SQL Server.** Mature, first-class EF Core support, but licensing is
  more restrictive for self-hosting and the container image is significantly
  larger. Also lacks a true `JSONB` analogue.
- **MySQL / Mariaia.** Popular, but historically weaker than PostgreSQL on
  concurrency, query-planner sophistication, and `JSONB` ergonomics.
- **SQLite.** Excellent for tests, but unsuitable as a production primary
  store for a multi-tenant SaaS — file locking, no row-level concurrency,
  limited type system.
- **Document databases (Mongo, etc.)** — explicitly rejected by section 1
  of the master prompt: "PostgreSQL → primary relational database."

## Consequences

- **Positive:** PostgreSQL is open-source with a strong track record. Its
  native `uuid`, `timestamptz`, and `JSONB` types map cleanly to our
  EF Core models. Container images are small (alpine) and fast to start,
  making `docker compose up` pleasant. Npgsql is a mature provider that
  supports all EF Core features we need (global query filters, migrations,
  concurrency tokens, transactions).
- **Negative:** The team must be fluent in PostgreSQL-specific idioms
  (`SERIAL` vs `IDENTITY`, `JSONB` operators, `tsrange` for date ranges,
  advisory locks). Tooling like `pgAdmin` is not as polished as SSMS,
  though `psql` + a query plan inspector is sufficient.
- **Risk:** Multi-tenancy is implemented at the row level (see ADR-004).
  If a tenant later requires physical isolation, the migration is non-trivial.
  Mitigation: ADR-004 documents the alternatives and the conditions under
  which we'd revisit.
- **Migration tooling:** EF Core migrations are the only path for schema
  changes; raw SQL is reserved for index tuning, partitioning, and
  background maintenance (e.g., outbox cleanup, audit log archival).
