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

**A-08 Public holidays are not seeded.** *Superseded by A-16 (S2).* We do not invent Kenyan gazetted holiday dates.
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
queue (beyond the outbox) is deferred to S2. *Updated in S2 (A-28): the general job queue moves to S3,
where payroll calculation is the first long-running job.*

## S2 Employees (2026-11-01)

**A-16 Kenya public holidays come from a rule version.** Manish supplied the fixed-date list
(2026-11-01). It lives in `rules/ke/calendar/public-holidays.v2026.1.json`, effective from
2026-01-01, and is loaded into the immutable `rules.rule_versions` table by `load-rules`.
Status is `seed_pending_specialist_signoff` until a Kenyan payroll/legal specialist signs
it. Nothing about holidays is hard-coded in C#. New tenants get a `KE-PUBLIC` calendar for
the current and next year; if no rule version exists the seed is skipped and audited
(`org.holiday_calendar.statutory_missing`), never guessed. Mazingira Day is 10 October as
in the Act; Utamaduni Day and Huduma Day are not in the Act and are not generated.

**A-17 Sunday rule follows section 4 of the Public Holidays Act (Cap. 110) exactly.**
"Where a day in Part I of the Schedule falls on a Sunday, the first succeeding day, not
being a public holiday, shall be a public holiday and the first-mentioned day shall cease to
be a public holiday." So the Sunday itself is *not* a holiday, and if Monday is already a
holiday the day moves to Tuesday (e.g. 2033: Christmas Sunday 25 Dec moves to Tuesday 27
Dec because Boxing Day is Monday). Source:
https://new.kenyalaw.org/akn/ke/act/1912/21/eng@2024-04-26. The moved day is labelled
"(moved from Sunday d MMM)".

**A-18 Movable and ad-hoc holidays are entered by people, with a source.** Good Friday,
Easter Monday and Idd-ul-Fitr are listed as `notGenerated` in the rule file and are added
per year. Government announcements use `POST /api/v1/org/holidays/announce` (adds to every
calendar the user may change, skips duplicates). Gazetted and short-notice holidays require
a `sourceReference` such as "Gazette Notice No. 1234 of 2026"; company days do not.
Removing a holiday requires a reason. Every change is audited and emits `org.holiday_changed`.

**A-19 MFA recovery (A-04 kept).** TOTP stays mandatory for privileged roles. On enrolment
the user gets 10 single-use recovery codes (shown once, stored as SHA-256 of user id +
code). Using one counts toward lockout like a wrong TOTP, is audited and emails the user.
Regenerating codes needs a current TOTP code and cancels the old set. A tenant administrator
(users.manage) can reset another user's MFA with a reason (cannot reset their own); this
rotates the security stamp, ends sessions, revokes codes and emails the user. Recovery codes
and admin reset cover tenant users only; platform operators still need an operator
re-enrolment (gap).

**A-20 Required identifiers at hire.** KRA PIN and (national ID or passport) are required.
Non-citizens must give a passport, work permit number and an unexpired permit expiry.
NSSF and SHA numbers are optional at hire because new hires often register after joining,
but they are reported as statutory gaps on the employee and must be present before payroll
approval (PAY-05, S3). Contractors are not expected to have NSSF/SHA. Identifiers are
unique per legal employer, not across the tenant.

**A-21 Format checks only.** We validate shapes (individual KRA PIN `A#########X`, national
ID 6–9 digits, NSSF digits) but do not call KRA iTax, IPRS or NSSF to verify numbers.

**A-22 Identifiers are restricted.** National ID, passport, permit, KRA PIN, NSSF, SHA,
HELB and disability certificate are masked (last 3 characters) unless the user holds
`people.sensitive.read` for that employer. Each unmasked view is audited
(`people.employee.sensitive_viewed`). Audit snapshots mask them too. Only holders of the
sensitive permission may change them.

**A-23 Employment history is effective-dated and never overwritten.** Each employment is
an episode (hire to termination); job, organization placement, worker category and pay
basis are assignment versions with an effective-from date. Back-dated and future-dated
changes are allowed and audited. A mistake is fixed by "correct", which keeps the original
marked cancelled and links the replacement; a future change can be cancelled. A database
trigger rejects any update that rewrites a version's content or an episode's hire date,
even for the schema owner. Termination is a date on the episode (future-dated allowed and
can be withdrawn before it takes effect); rehire opens a new episode and keeps the old one.
The employee number never changes.

**A-24 Worker category limits pay basis.** Casual: daily or hourly. Piece-rate: piece
rate only. Contractor: any basis, and flagged as non-employee for statutory purposes.
Permanent, fixed-term, probation and intern: monthly, daily or hourly. Fixed-term needs a
contract end date and probation needs a probation end date. Under-18s may be hired only as
interns with recorded guardian consent.

**A-25 Payment details need a second person.** Bank/M-Pesa/cash details are never applied
directly. They are a pending change that someone other than the requester approves (the
requester can only withdraw). One pending change per employee. On import, committing the
batch (which the uploader cannot do) is the second-person approval. Bank details are
checked against a tenant-maintained bank/branch reference table.

**A-26 Bank reference table is maintained by the tenant.** We have no approved, current
source of Kenyan bank and branch codes to seed, so we do not invent them. HR adds the codes
their bank publishes. Seeding from a CBK/KBA source is open question Q-02.

**A-27 Import (MIG-01, employees) is all-or-nothing and four-eyes.** CSV only (UTF-8, up
to 2,000 rows and 4 MB), dates as YYYY-MM-DD or DD/MM/YYYY, phones normalized to
+254 format. Every row is validated before anything is saved and errors are shown per row
and column. A batch with any error cannot be committed: the user fixes the file and uploads
again. A different user with `people.write` commits; commit re-validates against current
data and creates everyone in one transaction. Managers may refer to another row in the
same file. Cells that look like spreadsheet formulas are neutralised when we write CSV.

**A-28 Import runs in the API request.** 2,000 rows validate and commit in a few seconds,
so the general job queue (A-15) moves to S3 where payroll calculation needs it.

**A-29 Salary is not part of S2.** Salary and recurring pay elements are versioned in the
payroll slice (S3); S2 stores pay basis only.

**A-30 Gender values are female, male or intersex** and are used only for statutory leave
eligibility (S4).

**A-31 Not yet in S2.** Linking a user account to an employee record, and automatic access
removal on termination, come with the employee portal (ESS). Work-permit alerts (60/30/7
days and expired) are listed on the HR home page; email reminders come with notifications.

**A-32 Employee list filters by status/department in memory** after loading the tenant's
employees for the selected employer. Fine for S2 volumes (thousands); a materialised
"current assignment" view is planned when reporting needs it.

### Open questions
- **Q-01** (S1) Approved source for Kenyan public holidays. *Answered 2026-11-01 by Manish:
  fixed-date list supplied; ad-hoc holidays entered by authorised admins (A-16 to A-18).*
- **Q-02** Is there an approved, maintained source of Kenyan bank and branch codes (CBK or
  Kenya Bankers Association) we may seed from? Until then tenants maintain the table (A-26).
- **Q-03** Should NSSF and SHA numbers become mandatory at hire for statutory employees,
  or is "required before first payroll" (A-20) right?
