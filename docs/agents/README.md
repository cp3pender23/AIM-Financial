# AIM — Agent Team Reference

All agents live in `.claude/agents/` — that is the single source of truth. This page is an index and an SDLC-ordered invocation playbook.

## 13-agent roster (as of 2026-04-15)

Two new agents were added during the BSA port: **Data Analyst** and **Data Scientist**.

### SDLC invocation order

| # | Agent | Phase | Auto-invokes when… |
|---|---|---|---|
| 1 | `business-analyst` | Discovery | Status / scope / PRD / feature planning |
| 2 | `data-analyst` **(new)** | Discovery | KPIs, dashboard numbers, filter design, data-quality spot-checks |
| 3 | `data-scientist` **(new)** | Design | Risk thresholds, derivation (RiskLevel / Zip3), detection logic, ML roadmap |
| 4 | `database-administrator` | Design | Schema / indexes / migrations / retention policy |
| 5 | `sql-developer` | Design / Impl. | Raw SQL in migrations, LINQ translation issues, performance tuning |
| 6 | `csharp-developer` | Implementation | Entities, services, EF Core LINQ, workflow logic, audit wiring |
| 7 | `dotnet-developer` | Implementation | `Program.cs`, DI, middleware, Minimal APIs, NuGet pinning |
| 8 | `ui-ux-developer` | Implementation | Any `Pages/*.cshtml` edit; Alpine, AG Grid, ApexCharts, Leaflet |
| 9 | `security-reviewer` | Review | PII, policies, auth, OWASP checks |
| 10 | `qa-testing` | Review | xUnit + Playwright, regression checks |
| 11 | `data-operations` | Ops | CSV ingest, batch rollback, header alias additions |
| 12 | `devops-engineer` | Ops / Release | CI/CD, deployment, env vars, HTTPS, FinCEN stub swap |
| 13 | `memory-keeper` | Continuous | Session start/end, record decisions to `.remember/` |

See `memory/agent-playbook.md` (auto-memory) for the same order with expanded "how to use" notes.

## How they coordinate

```
business-analyst
     │
     ├─► data-analyst / data-scientist  (discovery + design)
     │
     ├─► database-administrator / sql-developer  (data layer)
     ├─► csharp-developer / dotnet-developer     (code layer)
     └─► ui-ux-developer                          (frontend)
     │
     ├─► security-reviewer + qa-testing  (review)
     ├─► data-operations + devops-engineer (ops / release)
     └─► memory-keeper (captures decisions throughout)
```

For any new feature that touches multiple layers, invoke all relevant agents. When unsure which agent owns a task, start at the Discovery wave (1–3) and let them route.

## Note on domain focus

All 13 agents are now aligned with the **BSA/FinCEN** domain. The vendor-era versions are recoverable via the `legacy/vendor-scoring` git branch if ever needed — do not assume an agent knows vendor-era patterns anymore.
