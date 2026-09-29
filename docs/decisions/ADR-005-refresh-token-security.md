# ADR-005: Refresh-Token Rotation, Family, and Reuse Detection

- Status: Accepted
- Date: 2026-09-30
- Phase: 2 — Authentication

## Context

Phase 2 implements authentication. Refresh tokens are long-lived (7 days
default) and grant access to user-scoped resources. Because they outlast
access tokens by 4+ orders of magnitude, refresh-token security is the
highest-impact security surface in Phase 2.

The user directive in Phase 2 explicitly requires:
> Refresh tokens must:
> - Never be stored plaintext
> - Be securely hashed
> - Have expiration
> - Support revocation
> - Support rotation
> - Track replacement/reuse where appropriate
> - Prevent replay/reuse of an already-rotated token

And specifically:
> Client sends refresh token A
> → server issues refresh token B
> → token A becomes invalid
> → client later tries token A again
> → request must be rejected safely

> Consider token-family/reuse detection where appropriate.

## Decision

Adopt **single-use refresh tokens with family-wide revocation on reuse**.

### Token storage

- Only the SHA-256 hash of the plaintext token is persisted (`refresh_tokens.token_hash`).
- The plaintext is returned to the client once at issuance; the server never
  has access to it again.
- Lookups at refresh time: hash the presented plaintext, look up by hash
  (unique index on `token_hash`).

### Family-based rotation

- Each login issues a refresh token with a new `family_id` (UUID v4).
- Each rotation creates a new refresh token with the SAME `family_id` as
  the presented token; the old token is marked `revoked_at_utc` + `replaced_by_token_id`.
- All tokens in a family are linked; revoking one (for reuse detection)
  revokes them all.

### Reuse detection (the key security property)

When the store receives a refresh request and the presented token is
already revoked (i.e., already rotated):

1. The store revokes ALL active tokens in the family
   (sets `revoked_at_utc` + `revocation_reason = "REUSE_DETECTED"`).
2. The store raises `RefreshTokenReuseDetectedEvent` (consumed by Phase 8
   audit log + Phase 6 security outbox).
3. The store throws `RefreshTokenReuseException` to the handler.
4. The handler propagates the exception. The API's `ExceptionHandlingMiddleware`
   maps it to HTTP 401 + `INVALID_REFRESH_TOKEN` (section 8: do not reveal
   account state).

### Defense against token theft

Consider the theft-replay attack:

```
Attacker steals refresh token A (active).
Attacker submits A → server issues B → A is revoked.
Legitimate user submits A → reuse detected → ENTIRE FAMILY revoked (A, B).
Legitimate user must re-authenticate.
```

The legitimate user is inconvenienced (must log in again), but the attacker's
access is also cut off. Both parties see the same "invalid refresh token"
response — neither party learns whether the other party was involved.

### Revocation reasons

The store tracks revocation reasons for audit:
- `USER_LOGOUT` — explicit logout
- `PASSWORD_CHANGED` — password change invalidated the session
- `PASSWORD_RESET` — password reset invalidated the session
- `ACCOUNT_LOCKED` — user account is locked
- `USER_DELETED` — user no longer exists
- `REUSE_DETECTED` — replay-defense triggered (entire family)

## Alternatives Considered

- **Stateless refresh tokens (JWT-based)**: Rejected. Refresh tokens must be
  revocable; stateless tokens cannot be revoked without an allow-list, which
  is itself stateful and defeats the purpose.
- **Single-token rotation (no family)**: Rejected. Without family tracking,
  reuse of a rotated token would only invalidate the new replacement, not the
  entire chain — a stolen-then-rotated token could still be used by the
  attacker until natural expiry.
- **Database-per-token revocation list**: Rejected. Requires polling a
  revocation list on every refresh — more DB round-trips, more failure
  modes. Family-based revocation is simpler and achieves the same property.
- **Reference refresh tokens by plaintext (no hash)**: Rejected. Plaintext
  token storage is a critical security violation (section 8). SHA-256 hash
  lookups via a unique index are fast and constant-time-ish at the DB level.

## Consequences

- **Positive:** Reuse of a rotated token invalidates the entire session
  family — strong defense against theft replay.
- **Positive:** Token revocation is O(family_size) — typically ≤ a few
  tokens per login session, so very fast.
- **Positive:** Plaintext tokens never touch the database; only SHA-256
  hashes do.
- **Negative:** Per-refresh DB lookup required (cannot be stateless).
  Acceptable: refreshes happen at most every 15 minutes per active user
  (matching the access token lifetime).
- **Negative:** The store must be transactional — rotation writes the new
  token AND updates the old token's `revoked_at_utc` + `replaced_by_token_id`
  atomically. EF Core transactions via `IUnitOfWork` (Phase 4 will use
  the explicit `BeginTransactionAsync` / `CommitTransactionAsync` pattern;
  Phase 2's `SaveChangesAsync` is single-statement-atomic per aggregate).
- **Risk:** An attacker who can READ the database (e.g., SQL injection,
  backup leak) gains access to all refresh-token hashes. SHA-256 is fast
  to compute, so the attacker could brute-force short tokens. Mitigation:
  Phase 2 tokens are 256 bits of entropy from `RandomNumberGenerator` —
  brute-force is computationally infeasible.
- **Risk:** If the attacker can WRITE the database, they can insert a
  forged refresh token. Mitigation: defense-in-depth — this would also
  require forging the JWT signing key, which is a separate secret
  (`Authentication:JwtSigningKey`, min 256 bits).

## Reference

- Original specification: master prompt sections 8 + Phase 2 directive.
- Implementation: `src/NexaFlow.Domain/Entities/RefreshToken.cs`,
  `src/NexaFlow.Infrastructure/Authentication/RefreshTokenStore.cs`,
  `tests/NexaFlow.Application.Tests/Auth/RefreshCommandHandlerTests.cs`.
