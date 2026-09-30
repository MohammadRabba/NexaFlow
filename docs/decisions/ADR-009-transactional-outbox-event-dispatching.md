# ADR-009: Transactional Outbox and Event Dispatching

- Status: Accepted
- Date: 2026-09-30
- Phase: 6 — Events & Notifications

## Context

The master specification (sections 19, 21, 22, 24) requires reliable asynchronous
event processing. The critical requirement is:

> A successful database transaction must contain both the business state change and
> the corresponding outbox message. RabbitMQ publication must NOT be required for
> the original business transaction to succeed.

Without the Outbox pattern, a failure between SaveChanges and RabbitMQ publish
would silently lose events — the business state changes but no notification,
audit, or downstream processing happens.

## Decision

Implement the **Transactional Outbox Pattern**:

```
HTTP Request → Handler → Domain mutation (AddDomainEvent) → SaveChangesAsync
     ↓
  DB Transaction
     ├── Business state changes
     └── OutboxMessage rows (one per domain event)
     ↓
  COMMIT
     ↓
  OutboxProcessor (background, polls every 10s)
     ├── SELECT FROM outbox WHERE processed_on_utc IS NULL
     ├── Publish to RabbitMQ (durable exchange + persistent messages)
     └── UPDATE outbox SET processed_on_utc = now()
```

### OutboxMessage model

| Field | Type | Purpose |
|---|---|---|
| Id | Guid (PK) | Stable unique identifier |
| EventType | string(256) | Full type name of the domain event |
| Payload | text | JSON-serialized event |
| OccurredOnUtc | timestamptz | When the event was raised |
| ProcessedOnUtc | timestamptz? | When the OutboxProcessor published it (null = pending) |
| AttemptCount | int | Retry count |
| Error | string(2000)? | Last error message |

### SaveChangesAsync interception

The `ApplicationDbContext.SaveChangesAsync` override:
1. Iterates `ChangeTracker.Entries<Entity>()` to find aggregates with queued domain events
2. For each event, serializes it via `IEventSerializer` and creates an `OutboxMessage`
3. Clears the events on the aggregate (`entity.ClearDomainEvents()`)
4. Calls `base.SaveChangesAsync()` — both business data and outbox rows persist atomically

### Event serialization

`JsonEventSerializer` uses `System.Text.Json` with the full type name (no assembly
version) as the event type identifier. Safe for cross-version compatibility.

### RabbitMQ topology

- **Exchange**: `nexaflow.events` (durable, topic)
- **Routing key**: the event type full name (e.g., `NexaFlow.Domain.Events.Tasks.TaskAssignedEvent`)
- **Queue**: `nexaflow.notifications` (durable) — bound to the exchange with routing
  key pattern `#.TaskAssignedEvent` (and other notification-producing events)
- Messages are **persistent** (DeliveryMode = 2)

### OutboxProcessor

- `BackgroundService` (IHostedService) that polls every 10 seconds (configurable)
- Processes in batches (default 50)
- Uses `SemaphoreSlim` to prevent concurrent processing within the same instance
- On publish failure: increments `AttemptCount`, stores `Error`, leaves
  `ProcessedOnUtc` null (will be retried next cycle)
- On publish success: sets `ProcessedOnUtc`
- Designed for at-least-once delivery

### Notification lifecycle (section 19)

```
Business Action (TaskAssigned) → Domain Event → Outbox → RabbitMQ →
Consumer → Persist Notification → SignalR Push → Connected Client
```

Notifications are persisted BEFORE SignalR delivery. If the client is offline,
the notification is still there for later retrieval via the API.

## Alternatives Considered

- **Direct RabbitMQ publish in the handler**: Rejected. If RabbitMQ is down, the
  business transaction would fail — unacceptable. The outbox decouples.
- **Change Data Capture (CDC) with Debezium**: Rejected for Phase 6 — adds
  operational complexity (Kafka + Connect) without a concrete requirement.
- **No outbox, just retry the handler**: Rejected. Handlers are user-facing HTTP
  requests; retrying would re-execute the entire business operation, not just
  the message publication.

## Consequences

- **Positive**: Business transactions succeed independently of RabbitMQ availability.
  At-least-once delivery is guaranteed (outbox + persistent messages + ack).
- **Positive**: Events are durable — if RabbitMQ is down when the event occurs,
  the OutboxProcessor will publish it when RabbitMQ comes back.
- **Negative**: The OutboxProcessor must be running for events to be published.
  In a multi-instance deployment, multiple processors could try to publish the
  same message — mitigated by the `SemaphoreSlim` within a single instance, and
  by the `ProcessedOnUtc` flag (once set, the message won't be re-processed).
  For multi-instance production: add a `SELECT ... FOR UPDATE SKIP LOCKED` claim
  pattern (PostgreSQL native).
- **Negative**: At-least-once delivery means consumers must be idempotent.
  The TaskAssigned consumer checks for existing notifications before creating
  a new one.
- **Risk**: If the OutboxProcessor fails to publish (e.g., RabbitMQ permanently
  down), outbox messages accumulate. Mitigation: the `AttemptCount` and `Error`
  fields allow monitoring and manual intervention.
