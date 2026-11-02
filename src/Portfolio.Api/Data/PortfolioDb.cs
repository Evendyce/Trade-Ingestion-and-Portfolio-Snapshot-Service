using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Domain;

namespace Portfolio.Api.Data;

public sealed class PortfolioDb(DbContextOptions<PortfolioDb> options) : DbContext(options)
{
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<MarketPrice> Prices => Set<MarketPrice>();
    public DbSet<TradeRevision> TradeRevisions => Set<TradeRevision>();
    public DbSet<IngestionReceipt> IngestionReceipts => Set<IngestionReceipt>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.UseCollation("Latin1_General_100_BIN2");
        model.Entity<Instrument>(b =>
        {
            b.HasKey(x => x.Isin);
            b.Property(x => x.Isin).HasMaxLength(12);
            b.Property(x => x.Symbol).HasMaxLength(20);
            b.Property(x => x.Name).HasMaxLength(100);
            b.HasData(new Instrument { Isin = "US0378331005", Symbol = "AAPL", Name = "Apple Inc." },
                new Instrument { Isin = "US5949181045", Symbol = "MSFT", Name = "Microsoft Corporation" });
        });
        model.Entity<MarketPrice>(b =>
        {
            b.HasKey(x => new { x.InstrumentId, x.Date });
            b.Property(x => x.InstrumentId).HasMaxLength(12);
            b.Property(x => x.Price).HasPrecision(19, 6);
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId);
            b.ToTable(t => t.HasCheckConstraint("CK_Prices_Positive", "[Price] > 0"));
            b.HasData(
                new MarketPrice { InstrumentId = "US0378331005", Date = new(2025, 3, 1), Price = 186m },
                new MarketPrice { InstrumentId = "US0378331005", Date = new(2025, 3, 3), Price = 190m },
                new MarketPrice { InstrumentId = "US5949181045", Date = new(2025, 3, 1), Price = 400m });
        });
        model.Entity<TradeRevision>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ExternalRef).HasMaxLength(80);
            b.Property(x => x.AccountId).HasMaxLength(64);
            b.Property(x => x.InstrumentId).HasMaxLength(12);
            b.Property(x => x.Side).HasMaxLength(4);
            b.Property(x => x.Quantity).HasPrecision(19, 6);
            b.Property(x => x.Price).HasPrecision(19, 6);
            b.HasIndex(x => new { x.ExternalRef, x.AsOf }).IsUnique();
            b.HasIndex(x => new { x.AccountId, x.ExternalRef, x.AsOf })
                .IncludeProperties(x => new { x.InstrumentId, x.Side, x.Quantity, x.Price, x.TradeDate });
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId);
            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TradeRevisions_Quantity", "[Quantity] > 0 AND [Quantity] <= 1000000000");
                t.HasCheckConstraint("CK_TradeRevisions_Price", "[Price] > 0 AND [Price] <= 1000000000");
                t.HasCheckConstraint("CK_TradeRevisions_Side", "[Side] IN ('BUY', 'SELL')");
            });
        });
        model.Entity<IngestionReceipt>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ExternalRef).HasMaxLength(80);
            b.Property(x => x.AccountId).HasMaxLength(64);
            b.Property(x => x.Outcome).HasMaxLength(32);
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.HasIndex(x => new { x.AccountId, x.ReceivedAt });
            b.HasOne<TradeRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
