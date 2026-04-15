# AIM — Risk Level Derivation

The only scoring in AIM today is a 4-tier threshold rule on `amount_total`. It runs at CSV import time and at draft creation. No pre-computed tables, no database-side triggers.

## Formula

```
amount_total >= 50000  → TOP
amount_total >= 20000  → HIGH
amount_total >=  5000  → MODERATE
else (including null)  → LOW
```

Implemented at `Models/BsaReport.cs` → `BsaReport.DeriveRiskLevel(decimal?)`.

## Zip3

Also derived: `Zip3` = first 3 digits of `subject_ein_ssn` after stripping non-digits. Used for coarse geographic bucketing in link analysis. Implemented at `BsaReport.DeriveZip3(string?)`.

## Ownership

`.claude/agents/data-scientist.md` owns any change to the thresholds or derivation rules. A threshold change requires:

1. Update the constants in `BsaReport.DeriveRiskLevel`.
2. One-shot SQL backfill of `risk_level` for existing rows (written in `database/ops/`).
3. Distribution sanity check — no single tier should collapse to 0% or dominate at 90%+ without explanation.
4. Update this doc AND `.remember/core-memories.md`.

## Future: ML scoring

See `.claude/agents/data-scientist.md` for the candidate-features list: velocity, geographic clustering, structuring indicators, link-cluster size, amendment cascades. Each new signal should be derivable from `bsa_reports` alone, have a documented threshold + false-positive rate, and expose a stable column or computed view for the dashboard to aggregate.
