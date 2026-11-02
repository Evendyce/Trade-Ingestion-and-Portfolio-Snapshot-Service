namespace Portfolio.Api.Domain;

public sealed class Instrument
{
    public string Isin { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class MarketPrice
{
    public string InstrumentId { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal Price { get; set; }
}

public sealed class TradeRevision
{
    public long Id { get; set; }
    public string ExternalRef { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string InstrumentId { get; set; } = "";
    public string Side { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public DateOnly TradeDate { get; set; }
    public DateTimeOffset AsOf { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class IngestionReceipt
{
    public long Id { get; set; }
    public string ExternalRef { get; set; } = "";
    public string AccountId { get; set; } = "";
    public DateTimeOffset AsOf { get; set; }
    public string Outcome { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public string TraceId { get; set; } = "";
    public long? RevisionId { get; set; }
}
