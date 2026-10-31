# ADR 0001 – Tenant isolation in depth

Status: accepted (S1 Foundation, 2026-10-31)

## Context
YeaKazi is multi-tenant SaaS holding salary and identity data. One bug in a query must
not be able to leak another employer's data.

## Decision
Isolation is enforced in three independent layers:

1. **PostgreSQL row-level security**, enabled and forced on every tenant table from the
   first migration. Policies compare `tenant_id` with the `app.tenant_id` session
   setting; the runtime roles have no BYPASSRLS and no DELETE grant.
2. **Session binding**: one connection per request, shared by all module DbContexts.
   `app.tenant_id` and `app.scope` are set from the authenticated token every time the
   connection opens. A tenant id in a request body is never used.
3. **Data layer write controls**: global query filters, `tenant_id` stamped from context,
   cross-tenant writes and tenant changes throw, and composite foreign keys
   `(tenant_id, legal_entity_id, id)` stop cross-tenant or cross-employer references.

Every write must be accompanied by an audit event in the same transaction; the audit
table is append-only and SHA-256 hash-chained per tenant.

## Consequences
Cross-module work shares one transaction without distributed transactions. Platform
operator work runs in an explicit "platform scope" with its own audit chain. Tests
include negative isolation tests that bypass the API filters and rely on RLS alone.
