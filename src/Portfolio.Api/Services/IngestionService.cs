using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Api.Data;
using Portfolio.Api.Domain;

namespace Portfolio.Api.Services;

public sealed class IngestionService(PortfolioDb db, IOptionsMonitor<IngestionOptions> options,
    ILogger<IngestionService> logger)
{
    public async Task<IngestionResult> Submit(TradeRequest request, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled || (settings.AllowedAccounts.Length > 0 &&
                !settings.AllowedAccounts.Contains(request.AccountId, StringComparer.Ordinal)))
            throw new DomainException(503, "ingestion_disabled", "Trade ingestion is disabled for this account.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Global reference first, then the immutable account: consistent lock ordering across processes.
        await Lock("trade:" + request.ExternalRef, ct);
        var current = await db.TradeRevisions.Where(x => x.ExternalRef == request.ExternalRef)
            .OrderByDescending(x => x.AsOf).FirstOrDefaultAsync(ct);
        if (current is not null && (current.AccountId != request.AccountId ||
                current.InstrumentId != request.InstrumentId || current.Side != request.Side))
            throw new DomainException(409, "immutable_trade_identity", "Account, instrument and side cannot change for an existing external reference.");

        await Lock("account:" + request.AccountId, ct);
        string outcome;
        TradeRevision revision;
        if (current is not null && request.AsOf <= current.AsOf)
        {
            var samePayload = current.Quantity == request.Quantity && current.Price == request.Price && current.TradeDate == request.TradeDate;
            outcome = request.AsOf < current.AsOf ? "stale" : samePayload ? "duplicate" : "version_conflict";
            revision = current;
        }
        else
        {
            if (current is not null && !settings.CorrectionsEnabled)
                throw new DomainException(503, "corrections_disabled", "Corrections are temporarily disabled; retry later.");
            if (!await db.Instruments.AnyAsync(x => x.Isin == request.InstrumentId, ct))
                throw new DomainException(422, "unknown_instrument", "Instrument is not registered.");

            await ValidateLedger(request, ct);
            revision = new TradeRevision
            {
                ExternalRef = request.ExternalRef, AccountId = request.AccountId,
                InstrumentId = request.InstrumentId, Side = request.Side,
                Quantity = request.Quantity, Price = request.Price, TradeDate = request.TradeDate,
                AsOf = request.AsOf, ReceivedAt = DateTimeOffset.UtcNow
            };
            db.TradeRevisions.Add(revision);
            await db.SaveChangesAsync(ct);
            outcome = current is null ? "accepted" : "corrected";
        }

        db.IngestionReceipts.Add(new IngestionReceipt
        {
            ExternalRef = request.ExternalRef, AccountId = request.AccountId, AsOf = request.AsOf,
            Outcome = outcome, ReceivedAt = DateTimeOffset.UtcNow, RevisionId = revision.Id,
            TraceId = Activity.Current?.TraceId.ToString() ?? ""
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Trade {Outcome}: {ExternalRef} account {AccountId}, incoming {AsOf}, accepted {AcceptedAsOf}, revision {RevisionId}",
            outcome, request.ExternalRef, request.AccountId, request.AsOf, revision.AsOf, revision.Id);
        return new(outcome, revision.Id, revision.AsOf);
    }

    private async Task Lock(string resource, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51000, 'Could not acquire ingestion lock; retry the request.', 1;
            """, ct);
    }

    private async Task ValidateLedger(TradeRequest request, CancellationToken ct)
    {
        var history = await db.TradeRevisions.AsNoTracking()
            .Where(x => x.AccountId == request.AccountId && x.InstrumentId == request.InstrumentId)
            .ToListAsync(ct);
        // Check every affected version cutoff, including out-of-order versions of other trades.
        // This prevents a newly received, backdated version from introducing historical short positions.
        var candidate = new TradeRevision
        {
            ExternalRef = request.ExternalRef, InstrumentId = request.InstrumentId, Side = request.Side,
            Quantity = request.Quantity, Price = request.Price, TradeDate = request.TradeDate, AsOf = request.AsOf
        };
        history.Add(candidate);
        foreach (var cutoff in history.Where(x => x.AsOf >= request.AsOf).Select(x => x.AsOf).Distinct())
        {
            var ledger = history.Where(x => x.AsOf <= cutoff).GroupBy(x => x.ExternalRef)
                .Select(g => g.MaxBy(x => x.AsOf)!)
                .OrderBy(x => x.TradeDate).ThenBy(x => x.ExternalRef, StringComparer.Ordinal);
            decimal quantity = 0;
            foreach (var trade in ledger)
            {
                quantity += trade.Side == "BUY" ? trade.Quantity : -trade.Quantity;
                if (quantity < 0)
                    throw new DomainException(409, "short_position", "This submission would create a short position in a current or historical ledger.");
                if (quantity > 1_000_000_000m)
                    throw new DomainException(409, "position_limit", "Position quantity cannot exceed 1 billion units.");
            }
        }
    }
}
