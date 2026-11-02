# Suggested 5-10 minute demonstration

1. **0:00-1:00** — State scope: .NET 8, SQL Server/EF Core, USD, BUY/SELL and moving-average cost. Show the dashboard and API explorer.
2. **1:00-2:30** — Run `./scripts/demo.ps1`. Explain accepted, duplicate, corrected and stale outcomes. Open its dashboard URL and show 100 AAPL units, $18,600 value, and $60 unrealized P/L.
3. **2:30-3:30** — Set the UTC version cutoff to `2025-03-01T10:30:00`; show the original 120 units/$22,320. Clear the cutoff. Inspect both accepted versions and the ingestion receipts.
4. **3:30-5:00** — Walk through IngestionService: reference/account SQL locks, append-only revisions, transactional receipt, and historical short-position validation. Explain immutable identity and the equal-timestamp conflict policy.
5. **5:00-6:30** — Show the stored procedure: version selection before effective-date filtering, moving-average SELL treatment, date-appropriate prices, and account totals. Explain the recursion/checkpoint trade-off and supporting indexes.
6. **6:30-7:30** — Run tests. Highlight concurrency, fractional cost, historical corrections, missing-price semantics and rollout gates. Explain why they use actual SQL Server.
7. **7:30-8:30** — Explain the safe kill switch, account allowlist and correction gate. Show JSON logs/trace IDs, README startup steps, and SOLUTION trade-offs/Framework 4.8 integration.

Keep the terminal, dashboard and code windows prepared before recording. The optional **Load example portfolio** dashboard button adds an independent two-instrument example worth $23,400; distinguish that scenario from the single-instrument script totals.

Video recording, narration, hosting and GitHub publication remain manual submission steps.
