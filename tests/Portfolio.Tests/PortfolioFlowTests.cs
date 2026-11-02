using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Api;
using Portfolio.Api.Data;
using Portfolio.Api.Domain;

namespace Portfolio.Tests;

public sealed class PortfolioFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string databaseName = "PortfolioTests_" + Guid.NewGuid().ToString("N");
    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["Database:Initialize"] = "true",
        ["Logging:LogLevel:Default"] = "Warning"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PORTFOLIO_TEST_SQL")
            ?? "Server=(localdb)\\ProjectModels;Integrated Security=True;Encrypt=False") { InitialCatalog = databaseName };
        Settings["ConnectionStrings:Portfolio"] = connection.ConnectionString;
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
    }

    public Task InitializeAsync() { using var client = CreateClient(); return Task.CompletedTask; }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // This factory generates and owns only this uniquely named test database.
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDb>();
        if (db.Database.GetDbConnection().Database == databaseName && databaseName.StartsWith("PortfolioTests_", StringComparison.Ordinal))
            await db.Database.EnsureDeletedAsync();
        await DisposeAsync();
    }
}

public sealed class PortfolioFlowTests(PortfolioFactory factory) : IClassFixture<PortfolioFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static readonly DateOnly Day = new(2025, 3, 1);
    private static TradeRequest Buy(string account, string reference = "A", decimal quantity = 120, decimal price = 185.4m,
        int hour = 10) => new(account + "-" + reference, account, "US0378331005", "BUY", quantity, price,
            Day, new DateTimeOffset(2025, 3, 1, hour, 0, 0, TimeSpan.Zero));

    private async Task<IngestionResult> Send(TradeRequest trade, string? outcome = null)
    {
        var response = await client.PostAsJsonAsync("/api/trades", trade);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var result = (await response.Content.ReadFromJsonAsync<IngestionResult>())!;
        if (outcome is not null) Assert.Equal(outcome, result.Outcome);
        return result;
    }

    private async Task<PortfolioSnapshot> Snapshot(string account, string date = "2025-03-01", string? cutoff = null)
    {
        var url = $"/api/accounts/{account}/snapshot?date={date}" + (cutoff is null ? "" : "&asOf=" + Uri.EscapeDataString(cutoff));
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PortfolioSnapshot>())!;
    }

    [Fact]
    public async Task Submission_duplicate_correction_stale_cutoff_and_audit_agree()
    {
        const string account = "FLOW";
        var trade = Buy(account);
        var first = await Send(trade, "accepted");
        Assert.Equal(22320m, (await Snapshot(account)).TotalMarketValueUsd);
        var duplicate = await Send(trade, "duplicate");
        Assert.Equal(first.RevisionId, duplicate.RevisionId);
        var corrected = await Send(trade with { Quantity = 100, AsOf = trade.AsOf.AddHours(1) }, "corrected");
        await Send(trade, "stale");
        var latest = await Snapshot(account);
        Assert.Equal(100, Assert.Single(latest.Positions).Quantity);
        Assert.Equal(18600m, latest.TotalMarketValueUsd);
        Assert.Equal(18540m, latest.TotalCostBasisUsd);
        Assert.Equal(60m, latest.UnrealizedPnlUsd);
        var old = await Snapshot(account, cutoff: "2025-03-01T10:30:00Z");
        Assert.Equal(120m, Assert.Single(old.Positions).Quantity);
        Assert.Equal(72m, old.UnrealizedPnlUsd);
        var revisions = await client.GetFromJsonAsync<TradeRevision[]>($"/api/trades/{trade.ExternalRef}/revisions");
        Assert.Equal(2, revisions!.Length);
        Assert.Equal(corrected.RevisionId, revisions[0].Id);
        var receipts = await client.GetFromJsonAsync<IngestionReceipt[]>($"/api/accounts/{account}/events");
        Assert.Equal(4, receipts!.Length);
        Assert.Equal(4, receipts.Select(x => x.Outcome).Distinct().Count());
        Assert.All(receipts, r => Assert.NotEmpty(r.TraceId));
    }

    [Fact]
    public async Task Concurrent_resends_and_corrections_keep_one_winning_version()
    {
        var trade = Buy("RACE");
        var first = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Send(trade)));
        Assert.Single(first.Where(x => x.Outcome == "accepted"));
        Assert.Equal(11, first.Count(x => x.Outcome == "duplicate"));
        await Task.WhenAll(Enumerable.Range(1, 6).Select(i => Send(trade with { Quantity = 100 + i, AsOf = trade.AsOf.AddMinutes(i) })));
        Assert.Equal(106, Assert.Single((await Snapshot(trade.AccountId)).Positions).Quantity);
    }

    [Fact]
    public async Task Moving_average_cost_handles_sells_rebuys_and_full_closure()
    {
        const string account = "COST";
        await Send(Buy(account, "01", 10, 100));
        await Send(Buy(account, "02", 10, 200));
        await Send(Buy(account, "03", 5, 999) with { Side = "SELL" });
        await Send(Buy(account, "04", 5, 300));
        var position = Assert.Single((await Snapshot(account)).Positions);
        Assert.Equal(20, position.Quantity);
        Assert.Equal(187.5m, position.UnitCostUsd);
        Assert.Equal(3750m, position.CostBasisUsd);
        Assert.Equal(-30m, position.UnrealizedPnlUsd);
        await Send(Buy(account, "05", 20, 190) with { Side = "SELL" });
        var closed = await Snapshot(account);
        Assert.Empty(closed.Positions);
        Assert.Equal(0m, closed.TotalCostBasisUsd);
        await Send(Buy(account, "06", 0.125m, 80));
        Assert.Equal(10m, Assert.Single((await Snapshot(account)).Positions).CostBasisUsd);
    }

    [Fact]
    public async Task Fractional_moving_average_matches_an_independent_decimal_ledger()
    {
        const string account = "FRACTIONAL";
        var random = new Random(42);
        decimal quantity = 0, cost = 0;
        for (var i = 0; i < 80; i++)
        {
            var buy = quantity < 2 || random.Next(3) != 0;
            var units = buy ? random.Next(1, 50000) / 1000m : decimal.Round(quantity * 0.37m, 6);
            var price = random.Next(1, 999999) / 1000m;
            cost = buy ? cost + units * price : cost - units * (cost / quantity);
            quantity += buy ? units : -units;
            await Send(Buy(account, i.ToString("D3"), units, price) with { Side = buy ? "BUY" : "SELL" });
        }
        var position = Assert.Single((await Snapshot(account)).Positions);
        Assert.Equal(quantity, position.Quantity);
        Assert.Equal(decimal.Round(cost, 2, MidpointRounding.AwayFromZero), position.CostBasisUsd);
        Assert.Equal(decimal.Round(quantity * 186 - cost, 2, MidpointRounding.AwayFromZero), position.UnrealizedPnlUsd);
    }

    [Fact]
    public async Task Corrected_trade_date_is_filtered_after_version_selection()
    {
        var trade = Buy("DATES");
        await Send(trade);
        await Send(trade with { TradeDate = Day.AddDays(1), AsOf = trade.AsOf.AddHours(1) });
        Assert.Empty((await Snapshot(trade.AccountId)).Positions);
        Assert.Single((await Snapshot(trade.AccountId, "2025-03-02")).Positions);
        Assert.Single((await Snapshot(trade.AccountId, cutoff: "2025-03-01T10:30:00Z")).Positions);
    }

    [Fact]
    public async Task Oversells_and_corrections_that_break_later_sells_are_rejected_atomically()
    {
        var trade = Buy("SHORT", quantity: 10);
        await Send(trade);
        await Send(Buy(trade.AccountId, "B", 8, hour: 11) with { Side = "SELL", TradeDate = Day.AddDays(1) });
        var response = await client.PostAsJsonAsync("/api/trades", trade with { Quantity = 5, AsOf = trade.AsOf.AddHours(2) });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("short_position", await response.Content.ReadAsStringAsync());
        Assert.Equal(2m, Assert.Single((await Snapshot(trade.AccountId, "2025-03-02")).Positions).Quantity);
        var history = await client.GetFromJsonAsync<TradeRevision[]>($"/api/trades/{trade.ExternalRef}/revisions");
        Assert.Single(history!);
        response = await client.PostAsJsonAsync("/api/trades", Buy("NAKED") with { Side = "SELL" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Out_of_order_versions_cannot_introduce_a_historical_short_position()
    {
        var trade = Buy("HISTORICAL", quantity: 10);
        await Send(trade);
        await Send(Buy(trade.AccountId, "B", 8, hour: 11) with { Side = "SELL" });
        await Send(trade with { Quantity = 12, AsOf = trade.AsOf.AddHours(2) });
        // A newer sell version than B's current version, but older than A's correction.
        var sell = Buy(trade.AccountId, "B", 11, hour: 11) with { Side = "SELL", AsOf = trade.AsOf.AddMinutes(90) };
        var response = await client.PostAsJsonAsync("/api/trades", sell);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Same_version_conflict_is_audited_without_overwriting()
    {
        var trade = Buy("CONFLICT");
        var original = await Send(trade);
        var conflict = await Send(trade with { Price = 1 }, "version_conflict");
        Assert.Equal(original.RevisionId, conflict.RevisionId);
        Assert.Equal(185.4m, Assert.Single((await Snapshot(trade.AccountId)).Positions).UnitCostUsd);
        var response = await client.PostAsJsonAsync("/api/trades", trade with { AccountId = "OTHER", AsOf = trade.AsOf.AddHours(1) });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Prices_never_look_forward_and_missing_prices_make_totals_incomplete()
    {
        var trade = Buy("PRICES") with { TradeDate = new DateOnly(2025, 2, 28) };
        await Send(trade);
        var missing = await Snapshot(trade.AccountId, "2025-02-28");
        Assert.False(missing.IsComplete);
        Assert.Null(missing.TotalMarketValueUsd);
        Assert.Null(Assert.Single(missing.Positions).MarketPriceUsd);
        Assert.Single(missing.Warnings);
        Assert.Equal(186m, Assert.Single((await Snapshot(trade.AccountId, "2025-03-02")).Positions).MarketPriceUsd);
        Assert.Equal(190m, Assert.Single((await Snapshot(trade.AccountId, "2025-03-03")).Positions).MarketPriceUsd);
    }

    [Fact]
    public async Task Invalid_requests_return_validation_errors_and_trace_id()
    {
        var trade = Buy("INVALID") with { Quantity = -1, Price = 0, Side = "HOLD" };
        var response = await client.PostAsJsonAsync("/api/trades", trade);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.Contains("X-Trace-Id"));
        response = await client.PostAsJsonAsync("/api/trades", Buy("UNKNOWN") with { InstrumentId = "XX0000000000" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        response = await client.PostAsJsonAsync("/api/trades", Buy("PRECISION") with { Quantity = 0.0000001m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        response = await client.PostAsJsonAsync("/api/trades", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_account_has_a_complete_zero_snapshot_and_openapi_is_available()
    {
        var snapshot = await Snapshot("EMPTY");
        Assert.Empty(snapshot.Positions);
        Assert.True(snapshot.IsComplete);
        Assert.Equal(0m, snapshot.TotalMarketValueUsd);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}

public sealed class RolloutTests
{
    [Fact]
    public async Task Account_allowlist_limits_adoption_without_changing_deduplication()
    {
        await using var factory = new PortfolioFactory();
        factory.Settings["Ingestion:AllowedAccounts:0"] = "PILOT";
        using var client = factory.CreateClient();
        try
        {
            var trade = new TradeRequest("PILOT-1", "PILOT", "US0378331005", "BUY", 1, 100,
                new DateOnly(2025, 3, 1), DateTimeOffset.UtcNow);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/trades", trade)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.PostAsJsonAsync("/api/trades", trade with { ExternalRef = "BLOCKED-1", AccountId = "BLOCKED" })).StatusCode);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Disabled_ingestion_returns_retryable_503_and_preserves_database()
    {
        await using var factory = new PortfolioFactory();
        factory.Settings["Ingestion:Enabled"] = "false";
        using var client = factory.CreateClient();
        try
        {
            var response = await client.PostAsJsonAsync("/api/trades", new TradeRequest("DISABLED", "DISABLED", "US0378331005", "BUY", 1, 1,
                new DateOnly(2025, 3, 1), DateTimeOffset.UtcNow));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.NotNull(response.Headers.RetryAfter);
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<PortfolioDb>().TradeRevisions.CountAsync());
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }

    [Fact]
    public async Task Correction_gate_preserves_existing_revision_and_still_deduplicates()
    {
        await using var factory = new PortfolioFactory();
        factory.Settings["Ingestion:CorrectionsEnabled"] = "false";
        using var client = factory.CreateClient();
        try
        {
            var trade = new TradeRequest("GATE", "GATE", "US0378331005", "BUY", 1, 100,
                new DateOnly(2025, 3, 1), DateTimeOffset.UtcNow);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/trades", trade)).StatusCode);
            var duplicate = await client.PostAsJsonAsync("/api/trades", trade);
            Assert.Equal("duplicate", (await duplicate.Content.ReadFromJsonAsync<IngestionResult>())!.Outcome);
            var correction = await client.PostAsJsonAsync("/api/trades", trade with { Quantity = 2, AsOf = trade.AsOf.AddSeconds(1) });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, correction.StatusCode);
            var revisions = await client.GetFromJsonAsync<TradeRevision[]>("/api/trades/GATE/revisions");
            Assert.Single(revisions!);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }
}
