# AIM Documentation

**AIM (Adaptive Intelligence Monitor)** is a BSA (Bank Secrecy Act) / FinCEN suspicious-activity reporting and analytics platform. It ingests SAR filings (via CSV or interactive drafts), computes a risk tier, tracks each filing through a Draft → Submitted → Acknowledged lifecycle, and surfaces analytics to investigators, analysts, and supervisors.

## Audiences & personas

| Persona | AIM role | Primary activities |
|---|---|---|
| Investigator | Analyst or Admin | Drill into subjects, trace link-analysis clusters, export evidence |
| Analyst | Analyst | Triage new filings, prepare drafts, respond to reviewer feedback |
| Supervisor / Manager | Admin | Review queues, approve filings, monitor compliance metrics |
| Task-force member | Viewer | Read-only access to shared filings |

## Doc map

| Doc | Audience | Purpose |
|---|---|---|
| [PRD.md](PRD.md) | Everyone | Product scope, functional & non-functional requirements, open questions |
| [architecture.md](architecture.md) | Developers | Stack, layers, request flow, deployment topology |
| [database.md](database.md) | DBAs, developers | Schema reference for `bsa_reports`, `audit_log`, Identity tables |
| [api.md](api.md) | Integrators | REST endpoints, auth headers, query params, response shapes |
| [frontend.md](frontend.md) | UI devs | Alpine/AG Grid/ApexCharts/Leaflet patterns; `_g` hoist rule |
| [data-pipeline.md](data-pipeline.md) | Data Ops | CSV import via UI or CLI, batch rollback |
| [scoring.md](scoring.md) (see `.claude/agents/data-scientist.md` for active ownership) | Data Scientist | RiskLevel thresholds, derivation |
| [developer-setup.md](developer-setup.md) | New devs | Local bootstrap: DB, migrations, seed, run |
| [agents/README.md](agents/README.md) | Everyone | Index of `.claude/agents/` dev subagents (single source of truth lives in `.claude/agents/`) |

## Known gaps

- Live FinCEN HTTP client (stub today, swap path documented).
- External SSO / SAML / OAuth.
- Multi-tenancy.
- Viewer-role CSV export — should it redact `subject_ein_ssn`? Open question, tracked in PRD §9.

## Project history

AIM was ported from a vendor-risk-scoring application to its current BSA/FinCEN form on 2026-04-15. The vendor-era codebase is preserved on branch `legacy/vendor-scoring` and tag `aim-fincen-vendor-final`. Do not attempt to merge schemas.
