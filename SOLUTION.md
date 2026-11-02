# Design and trade-offs

## Scope and architecture

ASP.NET Core 8 minimal API, EF Core 8 with the SQL Server provider, and a static dashboard served from the same origin. SQL Server LocalDB is the local development engine. There are no alternative in-memory persistence paths. Two focused application services handle ingestion and snapshots; this keeps the starting point small without adding repositories around EF Core or duplicating valuation logic in C# and SQL.

The SQL stored procedure is the canonical snapshot calculation. EF Core owns schema, constraints, reference seeds, and insertion of revisions/receipts; a parameterized SqlClient command reads the procedure's two result sets. A migration installs the embedded SQL source so a clean database has the same calculation as the API. For future changes, freeze existing migration SQL and add a new migration rather than changing a previously deployed artifact.

Implemented scope: USD BUY/SELL, fractional quantities, moving weighted average cost, long-only positions, latest-corrected daily views and optional historical version cutoffs. FX, cash, settlement, fees, tax lots, corporate actions, cancellations, realized P/L, price ingestion and a scheduled/materialized snapshot pipeline are outside this starting point. Account total/overall value therefore equals total investment market value.

## Identity, versions and audit

`externalRef` is globally unique within the single upstream source. Identifiers use case-sensitive binary collation and restricted ASCII keys; ISIN values use uppercase. For multiple sources, introduce `SourceId` into identity, uniqueness constraints, partitions, and locks.

Accepted trade versions are append-only through the service, with a unique `(ExternalRef, AsOf)` index. The highest accepted upstream `AsOf` wins. Account, instrument and side are immutable for an existing reference; quantity, trade price and effective trade date can change. Moving a trade between accounts/instruments or changing side would require an explicit cancellation/rebooking design.

Earlier versions return `stale`. Identical equal versions return `duplicate`. Equal versions with different quantity/price/date return `version_conflict`, are logged/audited, and do not overwrite accepted data. The brief treats earlier/same timestamps as resends, so this conflict outcome is HTTP 200, not an applied change. Later identical payloads still produce an accepted revision because their upstream version advanced.

Every successfully processed request creates an ingestion receipt, including ignored resends; each accepted initial/corrected version retains its full payload and receipt time. Failed business validation/disablement produces logs and no committed revision/receipt. Receipt metadata for a version conflict does not retain the rejected payload. A production compliance audit may require a dedicated durable ingress journal for all original payloads, rejection details and retention rules. Database permissions must prohibit direct UPDATE/DELETE of accepted history if immutability must also be enforced against privileged writers.

## Time semantics

- `tradeDate` is the effective trading date. A daily snapshot includes dates up to and including its valuation date.
- `asOf` is the upstream version timestamp; it is not our receipt timestamp. Default snapshots use the latest accepted versions, including corrections received after the valuation date.
- Optional snapshot `asOf` first restricts versions to `AsOf <= cutoff` and then selects the highest version per reference. Effective trade-date filtering occurs after version selection. This avoids resurrecting a superseded trade when its corrected date moves outside the requested day.
- `ReceivedAt` records service arrival time. The cutoff reproduces upstream-version history using all currently accepted records. It does **not** reconstruct exactly what this service knew at a historical wall-clock instant; that would additionally require a receipt-time cutoff or bitemporal contract.
- All event timestamps must use UTC. Trade dates are calendar dates. Within an instrument, trades are ordered by `(TradeDate, ExternalRef)` using ordinal reference order. There is no intraday trade timestamp in the assignment payload; the deterministic tie-break is a documented simplification. Upstream version time does not reorder trading activity.
- Prices use the latest stored USD price with `Price.Date <= ValuationDate`. Prices themselves are not versioned by the trade cutoff. Corrected historical market-data reproduction would require price revisions too.

Newer accepted versions may arrive out of order relative to other trades. Before committing, ingestion validates each affected upstream-version cutoff, selecting that cutoff's winning versions and replaying their effective-date order. This prevents a correction/backdated event from introducing either a current or an intermediate historical short position. The same validation caps an instrument position at 1 billion units.

## Concurrency and atomicity

Each ingestion transaction acquires a SQL application lock for the global reference and then one for its immutable account. These locks are transaction-owned, use the same order for all writers, and coordinate multiple API processes. Different accounts can proceed independently. Account serialization allows validation against a stable ledger and prevents simultaneous sells from overselling. Unique keys and check constraints add database-level protection. A lock timeout or deadlock produces a retryable 503; upstream should retry with the same reference/version.

Revision insertion and receipt insertion commit together. Failed validation or any database error rolls back the transaction. Successful response loss after commit is safe because resubmission deduplicates.

The procedure takes a shared account application lock while materializing effective trades into a temporary table, then releases it before calculating valuation. This prevents it from reading a mixture of account revisions during concurrent writes. All ledger writers must honor this protocol. Reference prices are static seeded data in this implementation; adding concurrent price writers requires an explicit consistent market-data read/version policy.

The locking implementation follows [SQL Server transaction-owned application locks](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql) and uses [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions). No distributed lock or in-process mutex is needed.

## Cost basis and precision

BUY adds quantity multiplied by execution price to remaining cost. SELL removes quantity at the pre-sale moving average, independent of sell proceeds. A full sale resets cost to zero; a subsequent purchase starts a fresh holding. Closed positions are excluded from the position result. Unrealized P/L is market value minus remaining cost, not realized profit from sales.

Quantity and trade/market price use SQL `decimal(19,6)`; requests with greater precision are rejected rather than silently rounded. Per-event quantity and price are positive and at most 1 billion. Cost basis and valuation use `decimal(28,8)`, with 12-decimal average-cost intermediates for cost removal. SQL casts are explicit to control multiplication/division scale. Unit cost is returned with up to 8 decimal places. Money in HTTP responses rounds to two decimals, midpoint away from zero. Totals sum internal values before response rounding, so displayed position amounts can differ by a cent from a displayed aggregate.

Missing prices leave that position's valuation and P/L null. Any missing open-position price makes account valuation/P&L null and `isComplete=false`; available partial values are not presented as a complete total. Cost remains available and warnings identify instruments. Empty accounts return a complete zero snapshot. Account existence is not modeled separately.

## SQL artifact and indexes

`dbo.GetPortfolioSnapshot` is a single parameterized stored procedure with relational CTEs/window functions and no cursor or procedural per-trade loop. It ranks versions, materializes effective events, computes running quantities, and uses a recursive relational CTE for the moving-average cost dependency within each instrument. The procedure returns position rows followed by one account-total row.

The recurrence is deliberate: remaining moving-average cost depends on previous buys and sells. A simplistic average of all buys or a net sum of buy/sell proceeds is incorrect for buy/sell/rebuy sequences. `MAXRECURSION 0` permits histories beyond 100 trades; execution time remains bounded by the command timeout. It is correct for this scope but is not a claim that unbounded full-history replay is suitable at production volume.

Supporting indexes are defined in the EF initial migration and included in `database/deploy.sql`:

- Unique `(ExternalRef, AsOf)` protects version identity and supports newest-version lookup.
- `(AccountId, ExternalRef, AsOf)` including `InstrumentId, Side, Quantity, Price, TradeDate` covers account version selection.
- Price primary key `(InstrumentId, Date)` supports the latest non-future price lookup by backward date scan.
- `(AccountId, ReceivedAt)` supports the recent receipt feed.
- A temporary clustered `(InstrumentId, Step)` key supports recurrence joins.

Predicates compare typed parameters directly with indexed columns; no date casts or functions wrap source filter columns. An optional-cutoff predicate uses statement recompilation so SQL can specialize the current and historical paths. Effective trade dates intentionally cannot be pushed ahead of version selection. Index order, sort memory and execution plans should be evaluated with representative account/history distributions before tuning; the indexes here are justified access paths, not measured production recommendations.

## Higher volumes and maintainability

Current ingestion loads one account/instrument revision history and validates all affected version boundaries in memory. This favors inspectable correctness but can become expensive (multiple replays while holding an account lock). The snapshot likewise replays full effective history, with sequential dependence per instrument and parallel independent instruments. Recent event reads are capped; latest-trade/history reads currently return all matching records and need pagination at scale.

At higher volume, measure request latency, lock waits, deadlocks, rows/versions per account, missing prices, correction rates, and query plans. Introduce version-aware position/cost checkpoints and invalidate/replay from the earliest effective date affected by a correction. A current-version projection can accelerate default snapshots while preserving immutable history for historical cutoffs. Use a transactional outbox for downstream notifications rather than dual-writing the database and a message bus.

Partition/archival decisions should follow measured size and access patterns. Receipt/audit history can be partitioned by receipt time and archived in bounded batches. Trade revisions cannot be discarded purely because their receipt date is old: historical valuation may require them. Preserve a current baseline and archival retrieval contract, retain corrections with their trade identity, and align partition keys/unique-index design before adopting partition switching. Avoid partitioning solely for appearance; small operational tables benefit more from appropriate covering indexes and maintained statistics.

Reference history, SQL artifact and endpoint contracts should be version-controlled together. Future database changes should use expand/contract migrations and a deployment identity. The runtime identity should have only required read/insert/execute rights, with schema modification separated.

## Operations, rollout and production boundaries

JSON logs contain outcome, external reference, account, incoming/accepted upstream timestamps, revision ID, trace scope, and HTTP status/duration. Domain rejections are warnings; unexpected failures include exception details in logs but return a generic Problem Details response. `X-Trace-Id` and problem `traceId` correlate failures. No payload bodies/connection strings are logged. Some SQL/hosting errors will naturally include technical context and require normal log access controls.

`Enabled` is a write kill switch. `AllowedAccounts` scopes adoption. `CorrectionsEnabled=false` gates later versions without ever disabling idempotency; duplicates still remain harmless and corrections receive 503 plus Retry-After. Reads remain available. Settings are captured per request, so an in-flight transaction can finish after a configuration reload. A hard operational stop would drain/stop writers. A multi-instance rollout needs a shared configuration provider and coordinated change propagation; these local JSON flags are a demonstrable mechanism, not a distributed control plane.

LocalDB is suitable for local correctness tests, but not for validating production network TLS, service-account access, failover, or capacity. The configured `Encrypt=False` is specific to the installed LocalDB transport. Use certificate-validated encryption and deployment-specific credentials on server-hosted SQL. Auto-migration is Development-only by default. The localhost demo deliberately has no authentication; before external hosting add authentication, account/tenant authorization, TLS termination, rate limits, audit/retention policy, health/readiness separation, and controlled API explorer exposure. No service has been published externally.

## Existing .NET Framework 4.8 integration

Keep this .NET 8 service behind an HTTP contract when integrating with a mature Framework application; do not attempt to load the .NET 8 API assembly or EF Core 8 into Framework 4.8. A Framework consumer can call the API using HttpClient and compatible JSON tooling. If direct database reporting is required, use parameterized ADO.NET/stored-procedure calls or the existing EF6 infrastructure rather than sharing the modern EF context.

Keep DTO contracts independent of runtime-specific types: ISO date/time and decimal fields allow Framework consumers without relying on DateOnly support. Verify compatible SqlClient/logging library versions and any binding redirects against the legacy application's dependency graph. Forward correlation IDs and adapt logs into the existing structured logging pipeline. Keep migrations owned by this service's deployment workflow, version the procedure/API contract, and avoid having both applications independently own schema changes or bypass the ingestion locking protocol.
