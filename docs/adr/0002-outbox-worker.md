# ADR 0002 – Transactional outbox and worker host

Status: accepted (S1 Foundation, 2026-10-31)

Side effects (email now; webhooks, statutory exports later) are written to
`messaging.outbox_messages` in the same transaction as the business change. A separate
worker process, connecting as `yeakazi_worker` (select/update on the outbox only),
claims batches with `FOR UPDATE SKIP LOCKED`, retries with exponential backoff and
dead-letters after the configured number of attempts. Payloads never contain salary
values; secrets (invitation tokens) are encrypted with AES-GCM under a versioned key id.
A general PostgreSQL job queue for long-running work is added in S3 (payroll calculation). S2 employee imports run inside the API request because 2,000 rows complete in seconds (assumptions A-28).
