# YeaKazi build status

Slices follow Section 15 of HRM_Development_Specification_v1.1. One block per slice.

| Slice | Status |
|---|---|
| S1 Foundation | Built, PR open for review |
| S2 Employees | Not started |

## Slice S1 – Foundation – 2026-10-31
Built: Modular monolith on .NET 10 LTS / EF Core 10 / PostgreSQL 16 (BuildingBlocks, Platform, Organization, Api) plus outbox worker and React + TS (Vite) portal. Tenancy with PostgreSQL row-level security (enabled and forced from the first migration, three least-privilege DB roles, no DELETE grant), tenant bound from the token into the DB session, composite tenant/employer foreign keys. Sign-in with password + TOTP MFA (required for privileged permissions, re-checked per request), lockout, security stamp, rate limiting. Permission catalogue and the 8 Section 6 system roles, custom roles, legal-entity-scoped assignments, separation-of-duties guards. Append-only, SHA-256 hash-chained audit log for every write with search and chain verification. Platform console: platform admins with MFA, idempotent tenant provisioning (SAAS-01) that seeds the first legal entity and invites the tenant administrator via the transactional outbox. Organization setup (ORG-01): legal entities, branches, cost centres, grades, departments, positions with reporting lines (cycle-safe), holiday calendars, work schedules (incl. overnight). Savannah-trust theme with light/dark tokens, tenant logo slot, status badges with text labels, tabular money. Generated OpenAPI + typed client. RFC 7807 errors with correlation ids.
Requirement IDs covered: SAAS-01 (full). ORG-01 (all except payroll groups, deferred to S3, see A-15).
Files created/changed: src/backend/{YeaKazi.BuildingBlocks,YeaKazi.Platform,YeaKazi.Organization,YeaKazi.Api}, src/workers/YeaKazi.Workers, src/frontend, tests/{YeaKazi.UnitTests,YeaKazi.IntegrationTests}, deploy/postgres/*.sql, deploy/docker-compose.yml, docs/{assumptions.md,adr/0001,adr/0002,openapi/yeakazi-api.json}, rules/ke/README.md, tests/fixtures/ke/README.md, README.md, BUILD_STATUS.md.
Tests: 53 unit, 32 PostgreSQL integration (incl. 9 tenant-isolation negatives that bypass app filters and rely on RLS, cross-tenant FK rejection, audit tamper rejection), 12 frontend component tests: all passing. Manual Playwright walkthrough of provisioning → invitation → activation → MFA → org setup → circular reporting line rejected → audit chain verified, in light/dark and mobile.
Assumptions added: A-01 to A-15 in docs/assumptions.md.
Open questions / known gaps: Q-01 approved source for Kenyan gazetted public holidays (calendars start empty). No refresh tokens or MFA recovery codes yet (admin re-enrolment path needed). No production email provider (dev writes .eml files). No logo upload yet (slot only). General job queue deferred to S2. docker-compose not exercised in CI (tests ran on a local PostgreSQL 16). No CI workflow yet.
Next slice prerequisites: Manish names S2 Employees; confirm employee number format and whether national ID / KRA PIN / NSSF / SHA numbers are mandatory at hire; decide on MFA recovery approach.
