# Assumptions log

Where the specification (HRM_Development_Specification_v1.1) is ambiguous, we chose the
safer and more auditable option. Each entry gives the decision and the reason. Manish can
overturn any of these; entries are never silently removed, only marked "Superseded by".

## S1 Foundation (2026-10-31)

**A-01 Database roles and row-level security.** Three PostgreSQL roles: `yeakazi_owner`
(owns objects, runs migrations), `yeakazi_app` (API runtime) and `yeakazi_worker`
(outbox only). None has BYPASSRLS. RLS is enabled *and forced* on every tenant table, so
even the owner role is filtered. The app role has no DELETE grant on any table; records
are deactivated, never deleted. "Platform scope" (SaaS operator console) is a separate
session flag (`app.scope = 'platform'`) that only platform-authenticated requests can set.

**A-02 Tenant comes only from the session.** `tenant_id` is taken from the authenticated
token, set in the PostgreSQL session on every connection open, and written by the data
layer. It is never read from a request body; a mismatch throws. It can never be changed
on an existing row.

**A-03 Platform tables have no tenant_id.** `platform.tenants`, `platform.platform_users`
and `platform.platform_audit_events` describe the operator, not a tenant. Platform audit
events carry `target_tenant_id` instead, and have their own hash chain. Login resolves a
tenant slug through a narrow SECURITY DEFINER function rather than opening the tenants
table to tenant sessions.

**A-04 MFA rule.** The spec requires MFA for privileged users. We treat any user who holds
*any* privileged permission (payroll, approvals, user/role admin, audit read, statutory
data, org admin) as privileged, so HR officers and auditors also need MFA. The check is
repeated on every request, not only at login, so a role granted mid-session takes effect.
TOTP (RFC 6238, 30 s, 6 digits) with replay protection; no SMS.

**A-05 Login hardening.** Login errors are generic (no user enumeration). 5 failed
attempts lock the account for 15 minutes. Disabling a user rotates their security
stamp, which invalidates existing tokens; permissions are re-read on every request, so a
revoked role stops working immediately. Access tokens last 30
minutes; there are no refresh tokens yet (users sign in again). The browser keeps the
token in sessionStorage only.

**A-06 KRA PIN format.** Employer PIN must match `^[AP]\d{9}[A-Z]$` (companies start
with P, sole proprietors with A); individual PIN `^A\d{9}[A-Z]$`. Format check only; no
KRA iTax lookup.

**A-07 Separation of duties in access admin.** Users cannot change their own roles or
disable themselves. The last active tenant administrator cannot be removed or disabled.
The 8 system roles from Section 6 are read-only; tenants create custom roles for
variations. Role assignments may be limited to specific legal entities.

**A-08 Public holidays are not seeded.** We do not invent Kenyan gazetted holiday dates.
Holiday calendars start empty and are maintained by HR (every change is audited and emits
`org.holiday_changed`). A seeded calendar needs an approved source (open question Q-01).

**A-09 Audit log.** Every write produces an audit event in the same transaction; the data
layer refuses to save business data without one. The audit table is append-only (UPDATE,
DELETE and TRUNCATE are rejected by triggers) and hash-chained per tenant with SHA-256.
`POST /api/v1/audit-events/verify-chain` recomputes the chain. Searching the audit log is itself
audited. Personal identifiers and emails are masked in snapshots; secrets are never
logged.

**A-10 Idempotent provisioning.** Tenant provisioning requires an `Idempotency-Key`
header. The same key with the same body replays the original result
(`Idempotent-Replayed: true`); the same key with a different body is rejected with 422
`idempotency_key_reused`.

**A-11 Invitations.** Invitation tokens are stored only as hashes and expire after 72
hours. The outbox payload carries the token encrypted (AES-GCM with a key id) so the
plaintext never sits in the database.

**A-12 Hosting regions** come from operator configuration (`Platform:HostingRegions`),
not from user input, so data residency is a deployment decision.

**A-13 Reporting lines are position-based** and must stay within one legal entity.
Cycles in department and position hierarchies are rejected under an advisory lock, so two
concurrent edits cannot create a loop.

**A-14 Creating a legal entity needs tenant-wide `org.write`.** A user whose role is
limited to one legal entity can manage that entity's structure but cannot create new
employers.

**A-15 Deferred to later slices.** Pay groups move to the payroll slice (S3) because they
depend on pay calendars. Seeding of rule packs, leave types and document templates at
provisioning is deferred to the slices that own them (S3, S4, S8). The PostgreSQL job
queue (beyond the outbox) is deferred to S2.
