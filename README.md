# YeaKazi HRM (by Yeasoft)

Multi-tenant HR and payroll for Kenyan employers. ASP.NET Core (.NET 10 LTS) modular
monolith on PostgreSQL, React + TypeScript (Vite) portal, PostgreSQL-backed outbox worker.

Build progress is tracked in [BUILD_STATUS.md](BUILD_STATUS.md); decisions taken where
the specification was ambiguous are in [docs/assumptions.md](docs/assumptions.md).

## Layout

| Path | What |
|---|---|
| `src/backend/YeaKazi.BuildingBlocks` | Tenancy context, audit trail, outbox, write controls, errors, permissions |
| `src/backend/YeaKazi.Platform` | Tenants and provisioning, users, roles, MFA sign-in, audit log API |
| `src/backend/YeaKazi.Organization` | Legal entities, branches, cost centres, grades, departments, positions, holidays, work schedules |
| `src/backend/YeaKazi.Api` | HTTP host, migrations runner, CLI commands |
| `src/workers/YeaKazi.Workers` | Outbox dispatcher (invitation email) |
| `src/frontend` | Portal and platform console (React + TS, API client generated from OpenAPI) |
| `rules/ke`, `tests/fixtures/ke` | Statutory rule packs and fixtures (empty until the payroll slice) |
| `deploy` | PostgreSQL role scripts and docker-compose for local development |
| `docs` | Assumptions, ADRs, generated OpenAPI document |

## Prerequisites

.NET SDK 10.0, Node.js 22 LTS, PostgreSQL 16 (local install or Docker).

## 1. Database

With Docker:

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Or against an existing PostgreSQL 16 as a superuser:

```bash
psql -U postgres -f deploy/postgres/00-roles.sql
psql -U postgres -f deploy/postgres/01-database.sql
```

The scripts create `yeakazi_owner` (migrations), `yeakazi_app` (API) and
`yeakazi_worker` (outbox) with **development-only** passwords, and the `yeakazi` database.

## 2. Build and migrate

```bash
dotnet build YeaKazi.sln
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/backend/YeaKazi.Api --no-launch-profile -- migrate
```

Migrations run as `yeakazi_owner` and install row-level security, grants, audit
triggers and the hash chain.

Then load the statutory rule versions from `/rules` (Kenya public holidays in S2). Rule
versions are immutable; re-running skips unchanged files and refuses a changed file that
keeps the same version label:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/backend/YeaKazi.Api --no-launch-profile -- load-rules --path rules
```

## 3. Create the first platform administrator

```bash
YEAKAZI_BOOTSTRAP_PASSWORD='choose-a-long-passphrase' \
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/backend/YeaKazi.Api --no-launch-profile -- \
  create-platform-admin --email ops@yeasoft.co.ke --name "Platform Admin"
```

The administrator enrols an authenticator app (TOTP) on first sign-in.

## 4. Run

```bash
# API on http://localhost:5080
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/backend/YeaKazi.Api --no-launch-profile

# Worker (writes invitation emails as .eml files to ./.maildrop in development)
DOTNET_ENVIRONMENT=Development dotnet run --project src/workers/YeaKazi.Workers

# Portal on http://localhost:5173 (proxies /api to :5080)
cd src/frontend && npm ci && npm run dev
```

Flow: sign in to the platform console at `/platform`, provision a tenant (an invitation
for the tenant administrator lands in `.maildrop/`), open the activation link, set a
password, enrol MFA, then set up the organisation.

## 5. Test

```bash
# Unit + PostgreSQL integration tests (integration tests create and drop a throwaway database)
YEAKAZI_TEST_PG_ADMIN='Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres' \
  dotnet test YeaKazi.sln

# Frontend
cd src/frontend && npm test && npm run build && npm run lint
```

The roles from `deploy/postgres/00-roles.sql` must exist before running integration tests.

## API client

The OpenAPI document is generated on build into `docs/openapi/yeakazi-api.json`.
Regenerate the typed frontend client after changing endpoints:

```bash
dotnet build src/backend/YeaKazi.Api && cd src/frontend && npm run gen:api
```

## Configuration secrets

`appsettings.Development.json` contains clearly marked development-only keys. Other
environments must provide `ConnectionStrings__App`, `ConnectionStrings__Migrations`,
`ConnectionStrings__Worker`, `Auth__SigningKey` and `Security__FieldEncryption__*`
from a secret store.
