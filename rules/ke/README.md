# /rules/ke – Kenyan statutory rule packs

Statutory rates, bands, limits, reliefs and calendars live **only** here, as
effective-dated RuleVersion data (Specification Section 13). Application code never
hard-codes a rate or a statutory date.

## Format

One JSON file per rule version:

| Field | Meaning |
|---|---|
| `jurisdiction` | `KE` |
| `ruleCode` | Stable rule identifier, e.g. `calendar.public_holidays` |
| `version` | Version label; a version is immutable once loaded |
| `effectiveFrom` / `effectiveTo` | Inclusive business dates; `effectiveTo` null = open-ended |
| `authoritySource` | Citation of the official source |
| `applicability` | Who and what the rule applies to |
| `reviewer`, `approvedAt` | Payroll-specialist sign-off (null until signed off) |
| `status` | `seed_pending_specialist_signoff` or `approved` |
| `payload` | Rule-specific data |

## Loading

`dotnet run --project src/backend/YeaKazi.Api -- load-rules --path rules` loads every file
into `rules.rule_versions` as the schema owner. Loading is idempotent: an unchanged file is
skipped, a changed file with an existing version label is rejected (publish a new version
instead), and overlapping effective periods of one rule are rejected. The API role can
only read rule versions.

## Contents

| Rule | Version | Effective | Source |
|---|---|---|---|
| `calendar.public_holidays` | 2026.1 | 2026-01-01 → | Public Holidays Act (Cap. 110) Schedule Part I and s.4 |

No value may be added here without a cited official source (Act, gazette notice,
KRA/NSSF/SHA/AHL publication). Payroll rule packs arrive with S3.
