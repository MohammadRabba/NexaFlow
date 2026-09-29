# NexaFlow API Documentation

> Phase 2 — Authentication. All eight auth endpoints + tenant resolution
> are implemented. Phase 3 adds organization / member endpoints.

## 1. Base URLs

| Environment | Base URL |
|---|---|
| Local dev (docker compose) | `http://localhost:8080` |
| Local dev (dotnet run) | `http://localhost:5229` (http) or `https://localhost:7286` (https) |

## 2. OpenAPI / Swagger

In Development environment, the API serves:
- `/openapi/v1.json` — the OpenAPI 3.1 document (built-in .NET 10 generator).
- `/scalar/v1` — interactive API explorer (Scalar.AspNetCore).

In Production, these are disabled.

## 3. Endpoints

### 3.1 Health (Phase 1)

| Method | Path | Description | Auth |
|---|---|---|---|
| `GET` | `/health/live` | Liveness check — process is running. | Anonymous |
| `GET` | `/health/ready` | Readiness check — Postgres reachable. | Anonymous |
| `GET` | `/health` | Alias for `/health/ready`. | Anonymous |

### 3.2 Authentication (Phase 2)

#### POST `/api/auth/register`

Register a new user. Issues an access token (15 min), a refresh token
(7 days, new family), and an email-verification token (sent via IEmailService).

**Request body:**
```json
{
  "email": "alice@example.com",
  "displayName": "Alice",
  "password": "StrongPass1!"
}
```

**Response 200:**
```json
{
  "accessToken": "eyJhbGc...",
  "refreshToken": "AbC...def",
  "accessTokenExpiresAtUtc": "2025-01-01T12:15:00Z",
  "refreshTokenExpiresAtUtc": "2025-01-08T12:00:00Z",
  "user": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "email": "alice@example.com",
    "displayName": "Alice",
    "emailVerified": false
  }
}
```

**Errors:**
- `409 EMAIL_ALREADY_REGISTERED` — the normalized email is already in use.
- `422 VALIDATION` — invalid email, display name, or password (with field-level errors).

#### POST `/api/auth/login`

Authenticate. Issues a new access + refresh token (start of a new family).

**Request body:**
```json
{
  "email": "alice@example.com",
  "password": "StrongPass1!"
}
```

**Errors:**
- `400 INVALID_CREDENTIALS` — wrong password OR account doesn't exist (same error for both — enumeration resistance).
- `400 EMAIL_NOT_VERIFIED` — account exists but email not verified.
- `400 ACCOUNT_LOCKED` — too many failed attempts; the account is locked for 15 minutes.

#### POST `/api/auth/refresh`

Rotate a refresh token. The old refresh token is revoked; a new access + refresh
token is returned. If a revoked (already-rotated) token is presented, the
entire family is revoked and a 401 is returned (reuse detection — see
ADR-005).

**Request body:**
```json
{
  "refreshToken": "AbC...def"
}
```

**Errors:**
- `401 INVALID_REFRESH_TOKEN` — token not found, expired, revoked, or
  reuse-detected (the response is the same for all four — no enumeration).

#### POST `/api/auth/logout`

Revoke a refresh token. Idempotent — returns 204 regardless of whether
the token was valid (do not reveal whether it existed).

**Request body:**
```json
{
  "refreshToken": "AbC...def"
}
```

**Response:** `204 No Content`

#### POST `/api/auth/forgot-password`

Initiate password reset. Always returns 200 — never reveals whether an
email exists. If the email is registered, a reset token (hashed) is
generated and emailed.

**Request body:**
```json
{
  "email": "alice@example.com"
}
```

**Response:** `200 OK` (always)

#### POST `/api/auth/reset-password`

Complete password reset. The token must be valid (unexpired, unused).
On success, ALL refresh tokens for the user are revoked (defense against
stolen sessions after a password reset).

**Request body:**
```json
{
  "email": "alice@example.com",
  "token": "the-plaintext-token-from-the-email",
  "newPassword": "NewStrongPass2!"
}
```

**Errors:**
- `400 INVALID_RESET_TOKEN` — token not found, expired, or already used.
- `422 VALIDATION` — new password doesn't meet policy.

#### POST `/api/auth/verify-email`

Verify the user's email using the one-time token issued at registration.

**Request body:**
```json
{
  "email": "alice@example.com",
  "token": "the-plaintext-verification-token"
}
```

**Errors:**
- `400 INVALID_VERIFICATION_TOKEN` — token not found, expired, or already used.

#### POST `/api/auth/change-password`

Change the password for the currently-authenticated user. Requires the
current password for verification (defense against stolen access tokens
being used to lock out the real owner). On success, ALL refresh tokens
are revoked (the user must re-authenticate on other devices).

**Headers:**
```
Authorization: Bearer <access-token>
```

**Request body:**
```json
{
  "currentPassword": "OldPassword1!",
  "newPassword": "NewStrongPass2!"
}
```

**Errors:**
- `401 Unauthorized` — missing or invalid access token.
- `400 INVALID_CURRENT_PASSWORD` — current password is incorrect.
- `422 VALIDATION` — new password doesn't meet policy.

## 4. Tenant Resolution (Phase 2)

For organization-scoped operations (Phase 3+ endpoints), the client
supplies the `X-Organization-Id` header:

```
X-Organization-Id: 550e8400-e29b-41d4-a716-446655440000
```

The `TenantResolutionMiddleware`:
1. Reads the authenticated principal's `sub` claim (JWT bearer middleware populates it).
2. If the user is unauthenticated: no tenant resolved — tenant-scoped
   operations fail-closed downstream.
3. If no `X-Organization-Id` header: tenant is null; tenant-scoped reads
   return no rows; tenant-scoped writes fail.
4. If the header is supplied: **validates it against the user's current
   organization memberships in the database**. Membership / role changes
   are reflected immediately (database is authoritative; JWT is not).
5. On mismatch or non-membership: 404 (not 403 — to avoid confirming
   the organization exists in another tenant — section 27 leak-avoidance).

## 5. Future Endpoints (Phase 4+)

| Phase | Method | Path | Purpose |
|---|---|---|---|
| 4 | `GET` `POST` | `/api/projects` | List / create projects |
| 4 | `GET` `PUT` `DELETE` | `/api/projects/{id}` | CRUD on a single project |
| 5 | `GET` `POST` | `/api/projects/{projectId}/tasks` | List / create tasks |
| 5 | `GET` `PUT` `DELETE` | `/api/tasks/{id}` | CRUD on a single task |
| 6 | (SignalR) | `/hubs/notifications` | Real-time notifications |

## 5.1 Organization Management (Phase 3)

### POST `/api/organizations`
Create a new organization. The current user becomes the Owner. The slug is
generated server-side from the name (lowercase, kebab-cased) — the client
supplies only `name` + an optional `slugSuggestion`.

**Request body:**
```json
{ "name": "Acme Inc.", "slugSuggestion": "acme" }
```

**Response 200:** `OrganizationDto` (id, name, slug, ownerUserId, createdAtUtc)
**Errors:** `409 ORGANIZATION_SLUG_TAKEN`, `422 VALIDATION`

### GET `/api/organizations`
Page the organizations the current user belongs to. No `X-Organization-Id`
header required (the result is scoped server-side to the user's memberships).

**Query:** `?page=1&pageSize=20`

### GET `/api/organizations/{id}`
Get a single organization. Returns 404 if not found OR if the user is not
a member (no enumeration leak — same response).

### PUT `/api/organizations/{id}`
Rename the organization. The slug is immutable (URL identifier).
Requires `X-Organization-Id` header matching the URL id; the user must hold
a role granting `organization.update` (Owner or Admin).

**Request body:** `{ "newName": "Acme Renamed" }`
**Response:** `204 No Content`
**Errors:** `404 NOT_FOUND`, `422 VALIDATION`

### DELETE `/api/organizations/{id}`
Soft-delete the organization. The org row remains in the DB (audit trail),
excluded from future reads. Memberships are retained (also for audit).
Requires `organization.delete` permission (Owner only).

**Response:** `204 No Content`

## 5.2 Organization Membership (Phase 3)

All endpoints require:
- `Authorization: Bearer <access-token>`
- `X-Organization-Id: <org-id>` matching the URL's `{organizationId}` (else 404)

### GET `/api/organizations/{organizationId}/members`
List the active members. Requires `member.read` (Owner, Admin, Member, Viewer).

### POST `/api/organizations/{organizationId}/members/invite`
Invite a user by email. The invitee must already be registered. Creates a
pending membership (IsActive=false) with a hashed invitation token. The
plaintext token is returned once in the response. Requires `member.invite`
(Owner, Admin).

**Request body:** `{ "inviteeEmail": "alice@example.com", "role": "Member" }`
**Response 200:** `InviteSummaryDto` (organizationId, userId, token, expiresAtUtc)
**Errors:** `404 INVITEE_NOT_REGISTERED`, `409 USER_ALREADY_MEMBER`, `422 VALIDATION`

### PUT `/api/organizations/{organizationId}/members/{userId}`
Change a member's role. Authorization rules (enforced inline in the handler,
NOT in the domain):
- Owner can change anyone's role (except another Owner's — use transfer-ownership)
- Admin can change Member/Viewer roles, NOT another Admin's
- Member/Viewer cannot change roles at all

**Request body:** `{ "newRole": "Admin" }`
**Response:** `204 No Content`
**Errors:** `403 CANNOT_MODIFY_ADMIN`, `403 INSUFFICIENT_ROLE`, `404 NOT_FOUND`

### DELETE `/api/organizations/{organizationId}/members/{userId}`
Remove a member. Hard-deletes the membership row (audit logs reference user_id
+ organization_id, not the row, so the trail persists). Authorization rules:
- Owner cannot self-remove (must transfer ownership first)
- Non-Owner can self-remove (leave voluntarily)
- Removing someone else requires Owner or Admin role
- Admin cannot remove another Admin

**Response:** `204 No Content`

### POST `/api/organizations/{organizationId}/members/transfer-ownership`
Transfer ownership to an existing member. The current Owner becomes an Admin.
Only the current Owner can call (live re-check against the aggregate).

**Request body:** `{ "toUserId": "..." }`
**Response:** `204 No Content`

## 6. Error Responses

All errors use RFC 7807 Problem Details. Examples:

### 400 — Domain Exception
```json
{
  "type": "https://api.nexaflow.com/errors/invalid_credentials",
  "title": "Domain rule violated",
  "status": 400,
  "detail": "Invalid email or password.",
  "errorCode": "INVALID_CREDENTIALS",
  "traceId": "6a94443d5ae326b7a570a6566658d252"
}
```

### 401 — Refresh Token Reuse
```json
{
  "type": "https://api.nexaflow.com/errors/invalid_refresh_token",
  "title": "Invalid or expired refresh token",
  "status": 401,
  "detail": "Invalid or expired refresh token.",
  "errorCode": "INVALID_REFRESH_TOKEN",
  "traceId": "..."
}
```

### 404 — Not Found (also used for cross-tenant leak avoidance)
```json
{
  "type": "https://api.nexaflow.com/errors/not_found",
  "title": "Resource not found",
  "status": 404,
  "detail": "Organization with id '...' was not found.",
  "errorCode": "NOT_FOUND",
  "traceId": "..."
}
```

### 422 — Validation Error
```json
{
  "type": "https://api.nexaflow.com/errors/validation",
  "title": "Validation failed",
  "status": 422,
  "errorCode": "VALIDATION",
  "traceId": "...",
  "errors": {
    "Password": [
      "Password must be at least 12 characters.",
      "Password must contain at least one digit.",
      "Password must contain at least one uppercase letter."
    ]
  }
}
```

### 429 — Rate Limited
Returned when the per-IP fixed window (100 req/min default) is exceeded.
Includes `Retry-After: 60` header.

### 500 — Internal Server Error
In Development, includes the full exception + stack trace. In Production,
returns a generic message — **stack traces are NEVER exposed in Production**.
