# Ledger — Trade Ingestion and Portfolio Snapshot Service

A runnable .NET 8 starting point for the senior developer assignment: SQL Server persistence, immutable trade revisions, correction-aware USD snapshots, moving-average cost basis, a dashboard, and an API explorer.

## Run locally

Prerequisites: .NET SDK 8 or newer with the .NET 8 runtime, Windows, and SQL Server LocalDB. This workspace uses the existing `(localdb)\ProjectModels` instance. The application owns a separate database named `PortfolioAssignment`; it does not use or change other application databases.

```powershell
dotnet restore Portfolio.sln
dotnet tool restore
dotnet run --project src/Portfolio.Api
```

Open [the dashboard](http://localhost:5080) or [Swagger / API explorer](http://localhost:5080/swagger). The Development profile applies EF Core migrations and seeds two instruments and three prices at startup. No trade is inserted automatically. Click **Load example portfolio** for a populated account, or submit your own trade. Example buttons always use fresh accounts and never clear existing data.

The dashboard example holds 100 AAPL units (after correcting 120) and 12 MSFT units. On 2025-03-01 the total market value is **$23,400**, cost basis **$23,160**, and unrealized P/L **$240**. Snapshot selections are stored in the URL, so refresh and bookmarks retain the account/date/cutoff. The dashboard's default account is empty until you add trades to it.

The UI uses plain HTML/CSS/JavaScript served by ASP.NET Core, with no Node build step. Optional Google Fonts have system-font fallbacks.

## Connection configuration

Default local development connection:

```text
Server=(localdb)\ProjectModels;Database=PortfolioAssignment;Integrated Security=True;Encrypt=False;MultipleActiveResultSets=False;Connect Timeout=15
```

This installed LocalDB instance does not support the requested encrypted connection. `Encrypt=False` is scoped to this local development configuration. For a full SQL Server instance, supply its own connection string and certificate settings:

```powershell
$env:ConnectionStrings__Portfolio = 'Server=YOUR_SERVER;Database=PortfolioAssignment;Integrated Security=True;Encrypt=True;TrustServerCertificate=False'
dotnet run --project src/Portfolio.Api
```

LocalDB runs as your Windows user. A Windows service running as another identity will need a server instance accessible to that identity. The test connection can be overridden separately using `PORTFOLIO_TEST_SQL`.

## Test and demo

```powershell
dotnet test Portfolio.sln
./scripts/demo.ps1
```

Tests use real SQL Server through HTTP endpoints hosted by `WebApplicationFactory`. They create uniquely named `PortfolioTests_<guid>` databases on the test connection and delete only those databases on completion. The login needs create/drop database permission. A failure to connect fails the suite rather than silently skipping it.

Coverage includes duplicate/correction/audit flow, historical cutoffs, simultaneous resends and corrections, date-changing corrections, moving-average sells and fractional quantities, full closure/reopening, oversell rejection, backdated-version validation, missing prices, validation errors, and rollout gates.

The PowerShell demo checks an initial 120-unit AAPL buy, duplicate, correction to 100, stale resend, corrected totals, and a historical cutoff. It creates a fresh account per invocation and prints a dashboard URL. Supply `-BaseUrl` to target another local port.

## API

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/trades` | Submit an initial trade or a later correction |
| GET | `/api/accounts/{accountId}/snapshot?date=2025-03-01&asOf=2025-03-01T10:30:00Z` | USD valuation; optional upstream-version cutoff |
| GET | `/api/accounts/{accountId}/trades` | Latest trade versions, across all trade dates |
| GET | `/api/trades/{externalRef}/revisions` | All accepted versions for a globally unique reference |
| GET | `/api/accounts/{accountId}/events` | Most recent 100 ingestion receipts |
| GET | `/api/instruments` | Registered instruments |
| GET | `/api/status` | Rollout status |
| GET | `/health` | Database connectivity check |

```json
{
  "externalRef": "TRX-1001",
  "accountId": "ACC-001",
  "instrumentId": "US0378331005",
  "side": "BUY",
  "quantity": 120,
  "price": 185.40,
  "tradeDate": "2025-03-01",
  "asOf": "2025-03-01T10:15:00Z"
}
```

Initial acceptance returns HTTP 201. Corrections and ignored resends return HTTP 200 with an explicit outcome. Equal timestamps with changed payloads return `version_conflict` and preserve the existing version. Errors use Problem Details: 400 validation, 409 business conflicts, 422 unknown instrument, and 503 temporary disablement/busy database. The `X-Trace-Id` response header and error `traceId` correlate with structured logs.

## Schema and SQL artifact

EF migrations are in `src/Portfolio.Api/Data/Migrations`. The canonical procedure is `src/Portfolio.Api/Sql/PortfolioSnapshot.sql`; the API executes this same procedure. It returns a position result set and a second account-total result set.

```sql
EXEC dbo.GetPortfolioSnapshot
    @AccountId = N'ACC-001',
    @ValuationDate = '2025-03-01',
    @AsOf = NULL;
```

The generated `database/deploy.sql` contains the idempotent schema, seed, procedure, and index definitions. It can be inspected or applied with SSMS/sqlcmd against the chosen database. It does not create the database itself. Alternatively:

```powershell
dotnet ef database update --project src/Portfolio.Api
```

Automatic migrations are enabled only by Development configuration. For deployment, apply reviewed migrations using a separate deployment identity. Keep `Database:Initialize=false` for the application identity.

## Rollout

`Ingestion` configuration supports:

```json
{
  "Enabled": true,
  "CorrectionsEnabled": true,
  "AllowedAccounts": ["ACC-001"]
}
```

An empty allowlist permits all accounts. Disable ingestion to pause all writes; disable corrections to keep new trades and deduplication while returning retryable 503s for newer versions of existing trades. JSON configuration reloads affect subsequent requests. Environment-variable changes require a process restart. Existing requests finish under the configuration they captured at entry.

See [SOLUTION.md](SOLUTION.md) for temporal semantics, precision, performance, deployment boundaries, and .NET Framework integration. See [docs/DEMO.md](docs/DEMO.md) for a video walkthrough outline.

This is a local assignment implementation. Authentication, tenant authorization, upstream delivery/outbox integration, and production hosting remain deployment work; the application defaults to a localhost binding. No GitHub publication or video recording is performed automatically.
