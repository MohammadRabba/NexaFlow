# NexaFlow Authentication

> Phase 2 — Authentication. **All eight auth endpoints are implemented and
> tested.** Phase 3 will add the organization membership endpoints.

## 1. Phase 2 — What's Implemented

### 1.1 Endpoints (all live)

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/register` | Register a new user; issue access + refresh tokens + email-verification token |
| `POST` | `/api/auth/login` | Authenticate; issue access + refresh tokens (new family) |
| `POST` | `/api/auth/refresh` | Rotate refresh token; detect reuse; revoke family on replay |
| `POST` | `/api/auth/logout` | Revoke a refresh token (idempotent) |
| `POST` | `/api/auth/forgot-password` | Initiate password reset (always 200 — never reveals whether email exists) |
| `POST` | `/api/auth/reset-password` | Complete password reset; invalidates existing sessions |
| `POST` | `/api/auth/verify-email` | Consume one-time email-verification token |
| `POST` | `/api/auth/change-password` | Change password for authenticated user; invalidates existing sessions |

All endpoints return RFC 7807 Problem Details on failure. See `docs/api.md`.

### 1.2 Token Lifetimes (per Phase 2 directive)

| Token | Lifetime | Storage |
|---|---|---|
| Access token (JWT, HS256) | 15 minutes | Returned to client; never persisted server-side |
| Refresh token | 7 days | SHA-256 hash only — `refresh_tokens.token_hash` |
| Email verification token | 24 hours | SHA-256 hash only — `users.email_verification_token_hash` |
| Password reset token | 1 hour (short — sensitive) | SHA-256 hash only — `users.password_reset_token_hash` |

### 1.3 JWT Claims

The JWT contains ONLY:
- `sub` — user id (Guid, as string)
- `email` — display email
- `email_verified` — boolean at issuance time
- `jti` — unique token id
- `iat` / `exp` — standard JWT timestamps

**NO organization membership or role claims** are included in the JWT.

Per Phase 2 directive:
> Do not use JWT claims as the authoritative source for current permissions
> or organization membership if that would allow stale authorization after
> membership/role changes.

The tenant-resolution middleware (`TenantResolutionMiddleware`) queries the
database on EVERY request to validate the `X-Organization-Id` header
against the user's current organization memberships. If the user's
membership or role changes after the access token was issued, the next
request reflects the change immediately — no token re-issuance needed.

### 1.4 Refresh-Token Rotation + Reuse Detection

See [ADR-005](decisions/ADR-005-refresh-token-security.md) for the full
rationale. Summary:

```
Login → A (new family F1, active)
Refresh(A) → B (same family F1, active); A marked revoked + replacedBy=B
Refresh(B) → C (same family F1, active); B marked revoked + replacedBy=C

Reuse: Client sends A again → A is already revoked → REUSE DETECTED
       → ENTIRE FAMILY F1 revoked (A, B, C all become revoked)
       → 401 INVALID_REFRESH_TOKEN returned
       → RefreshTokenReuseDetectedEvent raised for audit / security outbox
```

### 1.5 Account Lockout

- **Maximum failed attempts:** 5 (configurable: `Authentication:MaxFailedLoginAttempts`)
- **Lockout duration:** 15 minutes (configurable: `Authentication:LockoutDuration`)
- **Reset behavior:** A successful login resets the counter to 0.
- **Permanent vs. temporary:** Temporary — the account auto-unlocks after
  the lockout duration elapses. Manual unlock is supported via the
  `User.Unlock()` domain method (Phase 3 adds admin endpoints).
- **Enumeration resistance:** Failed login (whether user exists or not)
  returns the same `INVALID_CREDENTIALS` error code + message. The error
  message ("Invalid email or password.") does not differ between
  "user not found" and "wrong password".

### 1.6 Password Reset — Token Security

- **Single-use:** Each reset token is invalidated after consumption.
- **Hashed storage:** Only the SHA-256 hash of the plaintext is stored.
- **Expiry:** Default 1 hour; expired tokens cannot be consumed.
- **Reuse detection:** After a token is consumed, presenting it again
  returns `INVALID_RESET_TOKEN` (no enumeration leak — same error for
  unknown email or wrong token).
- **Session invalidation:** A successful password reset revokes ALL
  refresh tokens for the user (defense against stolen sessions post-reset).

### 1.7 Email Verification — Token Security

Same properties as password reset:
- Single-use, hashed storage, expiry-aware.
- After verification, the `email_verification_token_hash` column is
  set to NULL (the token cannot be reused).

### 1.8 Sensitive Data — Never Logged

The following are NEVER written to logs (verified by Phase 2 unit tests):
- Plaintext passwords
- BCrypt password hashes (the hash itself is OK to store, not to log)
- Access tokens (JWT strings)
- Refresh tokens (plaintext or hashes)
- Email verification tokens (plaintext or hashes)
- Password reset tokens (plaintext or hashes)
- JWT signing key

The `LoggingBehavior` (MediatR pipeline) logs only the request type name
and elapsed time — never the request payload. The `DevEmailService`
writes the plaintext verification / reset token to a local file (in
`$TMPDIR/nexaflow-dev-emails/`) so developers can copy it for local
testing — the structured log only includes the recipient (redacted)
and the file path.

## 2. Email Delivery (Phase 2 Dev Implementation)

Per Phase 2 directive:
> For Phase 2, email delivery can use a clearly documented development
> implementation if a real provider is not yet configured, but do not
> fake successful production email delivery.

- **Development environment:** `DevEmailService` writes the email as JSON
  to a file under `$TMPDIR/nexaflow-dev-emails/` AND logs a redacted entry
  (recipient, subject, file path). The body — including the verification
  or reset token — is in the file so the developer can copy it for
  testing; it never appears in logs.
- **Production environment:** `NotConfiguredEmailService` throws
  `InvalidOperationException` on every send. Production callers MUST
  wire a real SMTP or external API sender via Infrastructure DI before
  shipping. The `IEmailService` contract is unchanged; only the
  implementation differs.
- **Phase 6:** A real SMTP sender will be added and routed through the
  Outbox worker for retry / durability.

## 3. Phase 3 — What Will Be Added

The authentication architecture is ready for Phase 3's RBAC + permission
system:

- The `User` aggregate owns identity, password, lockout, and verification.
- The `Organization` aggregate owns the tenant and its `Members` collection.
- The `OrganizationMember` join carries the `OrganizationRole` (Owner / Admin / Member / Viewer).
- The tenant-resolution middleware resolves and validates the current
  organization on every request, providing the foundation for
  permission-based authorization checks.

Phase 3 will introduce:
- The permission matrix (`Project.Read`, `Task.Create`, etc.)
- `IPermissionService` and authorization handlers / policies
- Cross-tenant isolation integration tests
- The organization + member endpoints (`/api/organizations/*`,
  `/api/organizations/{id}/members/*`)
