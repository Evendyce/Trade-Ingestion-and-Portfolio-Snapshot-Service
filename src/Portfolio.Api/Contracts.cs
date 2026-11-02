using System.Text.RegularExpressions;

namespace Portfolio.Api;

public sealed record TradeRequest(
    string ExternalRef, string AccountId, string InstrumentId, string Side,
    decimal Quantity, decimal Price, DateOnly TradeDate, DateTimeOffset AsOf)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        void Check(bool valid, string name, string message) { if (!valid) errors[name] = [message]; }
        Check(ValidKey(ExternalRef, 80), nameof(ExternalRef), "Use 1-80 ASCII letters, digits, dots, colons, underscores or hyphens.");
        Check(ValidKey(AccountId, 64), nameof(AccountId), "Use 1-64 ASCII letters, digits, dots, colons, underscores or hyphens.");
        Check(InstrumentId is { Length: 12 } && Regex.IsMatch(InstrumentId, "^[A-Z0-9]{12}$"), nameof(InstrumentId), "Use a 12-character uppercase instrument ISIN.");
        Check(Side is "BUY" or "SELL", nameof(Side), "Side must be BUY or SELL.");
        Check(Quantity > 0 && Quantity <= 1_000_000_000m && decimal.Round(Quantity, 6) == Quantity,
            nameof(Quantity), "Quantity must be positive, at most 1 billion, with at most 6 decimal places.");
        Check(Price > 0 && Price <= 1_000_000_000m && decimal.Round(Price, 6) == Price,
            nameof(Price), "Price must be positive, at most 1 billion, with at most 6 decimal places.");
        Check(TradeDate >= new DateOnly(1900, 1, 1), nameof(TradeDate), "Trade date must be on or after 1900-01-01.");
        Check(AsOf >= new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero) && AsOf.Offset == TimeSpan.Zero,
            nameof(AsOf), "AsOf must be an ISO 8601 UTC timestamp on or after 1900-01-01.");
        return errors;
    }

    public static bool ValidKey(string? key, int max) => key is not null && key.Length <= max
        && Regex.IsMatch(key, "^[A-Za-z0-9_.:-]+$");
}

public sealed record IngestionResult(string Outcome, long RevisionId, DateTimeOffset AcceptedAsOf);
public sealed record Position(string InstrumentId, string Symbol, decimal Quantity, decimal UnitCostUsd,
    decimal CostBasisUsd, decimal? MarketPriceUsd, DateOnly? PriceDate, decimal? MarketValueUsd,
    decimal? UnrealizedPnlUsd);
public sealed record PortfolioSnapshot(string AccountId, DateOnly Date, DateTimeOffset? AsOf,
    string Currency, IReadOnlyList<Position> Positions, decimal? TotalMarketValueUsd,
    decimal? OverallValueUsd, decimal TotalCostBasisUsd, decimal? UnrealizedPnlUsd, bool IsComplete,
    IReadOnlyList<string> Warnings);

public sealed class DomainException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class IngestionOptions
{
    public bool Enabled { get; set; } = true;
    public bool CorrectionsEnabled { get; set; } = true;
    public string[] AllowedAccounts { get; set; } = [];
}
