using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Portfolio.Api;
using Portfolio.Api.Data;
using Portfolio.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "O"; o.UseUtcTimestamp = true; });
builder.Services.AddDbContext<PortfolioDb>(o => o.UseSqlServer(
    builder.Configuration.GetConnectionString("Portfolio") ?? throw new InvalidOperationException("Portfolio connection string is required.")));
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<SnapshotService>();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "Portfolio Data Service",
    Version = "v1",
    Description = "USD trade ingestion, immutable revisions and moving-average portfolio snapshots. Local development API."
}));

var app = builder.Build();
app.Use(async (context, next) =>
{
    var timer = Stopwatch.StartNew();
    context.Response.Headers["X-Trace-Id"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    try
    {
        await next(context);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        context.Response.StatusCode = 499;
    }
    catch (Exception error) when (!context.Response.HasStarted)
    {
        var (status, code, detail) = error switch
        {
            DomainException e => (e.Status, e.Code, e.Message),
            SqlException { Number: 51000 or 1205 or -2 } => (503, "database_busy", "Database is busy; retry this request."),
            SqlException { Number: 51001 } => (409, "short_position", "The selected historical ledger contains a short position."),
            BadHttpRequestException => (400, "invalid_request", "Request body or query parameters are invalid."),
            _ => (500, "unexpected_error", "An unexpected error occurred. Use the trace ID when reporting this issue.")
        };
        if (status == 503) context.Response.Headers.RetryAfter = "5";
        if (status >= 500 && error is not DomainException)
            app.Logger.LogError(error, "Request failed with {ErrorCode}", code);
        else
            app.Logger.LogWarning("Request rejected with {ErrorCode}, status {Status}, trace {TraceId}",
                code, status, Activity.Current?.TraceId.ToString());
        await Results.Problem(statusCode: status, title: code, detail: detail).ExecuteAsync(context);
    }
    finally
    {
        if (context.Request.Path.StartsWithSegments("/api"))
            app.Logger.LogInformation("HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs}ms",
                context.Request.Method, context.Request.Path.Value, context.Response.StatusCode, timer.ElapsedMilliseconds);
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI(o => { o.DocumentTitle = "Portfolio API explorer"; });

app.MapGet("/health", async (PortfolioDb db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503))
    .WithTags("Operations");
app.MapGet("/api/status", (IOptionsMonitor<IngestionOptions> options) => Results.Ok(new
{
    ingestionEnabled = options.CurrentValue.Enabled,
    correctionsEnabled = options.CurrentValue.CorrectionsEnabled,
    accountRolloutRestricted = options.CurrentValue.AllowedAccounts.Length > 0,
    currency = "USD"
})).WithTags("Operations");
app.MapGet("/api/instruments", async (PortfolioDb db, CancellationToken ct) =>
    Results.Ok(await db.Instruments.AsNoTracking().OrderBy(x => x.Symbol).ToListAsync(ct)))
    .WithTags("Reference data");
app.MapPost("/api/trades", async (TradeRequest request, IngestionService service, CancellationToken ct) =>
{
    var errors = request.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var result = await service.Submit(request, ct);
    return result.Outcome == "accepted"
        ? Results.Created($"/api/trades/{request.ExternalRef}/revisions", result)
        : Results.Ok(result);
}).WithTags("Trades").Produces<IngestionResult>(201).Produces<IngestionResult>()
    .ProducesValidationProblem().ProducesProblem(409).ProducesProblem(422).ProducesProblem(503);
app.MapGet("/api/trades/{externalRef}/revisions", async (string externalRef, PortfolioDb db, CancellationToken ct) =>
    Results.Ok(await db.TradeRevisions.AsNoTracking().Where(x => x.ExternalRef == externalRef)
        .OrderByDescending(x => x.AsOf).ToListAsync(ct))).WithTags("Trades");
app.MapGet("/api/accounts/{accountId}/events", async (string accountId, PortfolioDb db, CancellationToken ct) =>
    Results.Ok(await db.IngestionReceipts.AsNoTracking().Where(x => x.AccountId == accountId)
        .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id).Take(100).ToListAsync(ct)))
    .WithTags("Trades");
app.MapGet("/api/accounts/{accountId}/trades", async (string accountId, PortfolioDb db, CancellationToken ct) =>
    Results.Ok(await db.TradeRevisions.AsNoTracking().Where(x => x.AccountId == accountId)
        .GroupBy(x => x.ExternalRef).Select(g => g.OrderByDescending(x => x.AsOf).First())
        .ToListAsync(ct))).WithTags("Trades");
app.MapGet("/api/accounts/{accountId}/snapshot", async (string accountId, DateOnly date,
    DateTimeOffset? asOf, SnapshotService service, CancellationToken ct) =>
{
    if (!TradeRequest.ValidKey(accountId, 64) || date < new DateOnly(1900, 1, 1) ||
            (asOf.HasValue && asOf.Value.Offset != TimeSpan.Zero))
        return Results.Problem(statusCode: 400, title: "invalid_snapshot_parameters",
            detail: "Use a valid account identifier, a date on or after 1900-01-01 and an optional UTC asOf timestamp.");
    return Results.Ok(await service.Get(accountId, date, asOf, ct));
}).WithTags("Snapshots").Produces<PortfolioSnapshot>().ProducesProblem(400).ProducesProblem(409);

if (app.Configuration.GetValue<bool>("Database:Initialize"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PortfolioDb>().Database.MigrateAsync();
}
await app.RunAsync();

public partial class Program;
