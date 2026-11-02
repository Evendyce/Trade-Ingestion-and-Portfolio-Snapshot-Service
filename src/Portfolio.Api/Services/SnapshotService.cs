using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;

namespace Portfolio.Api.Services;

public sealed class SnapshotService(PortfolioDb db)
{
    public async Task<PortfolioSnapshot> Get(string accountId, DateOnly date, DateTimeOffset? asOf, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "dbo.GetPortfolioSnapshot";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 30;
            command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.NVarChar, 64) { Value = accountId });
            command.Parameters.Add(new SqlParameter("@ValuationDate", SqlDbType.Date) { Value = date.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@AsOf", SqlDbType.DateTimeOffset) { Value = (object?)asOf ?? DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync(ct);
            var positions = new List<Position>();
            while (await reader.ReadAsync(ct))
            {
                positions.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2),
                    reader.GetDecimal(3), Money(reader.GetDecimal(4)), NullableDecimal(5),
                    reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                    NullableMoney(7), NullableMoney(8)));
            }
            await reader.NextResultAsync(ct);
            await reader.ReadAsync(ct);
            var total = NullableMoney(0);
            var overall = NullableMoney(1);
            var cost = Money(reader.GetDecimal(2));
            var pnl = NullableMoney(3);
            var warnings = positions.Where(x => x.MarketPriceUsd is null)
                .Select(x => $"No price on or before {date:yyyy-MM-dd} for {x.InstrumentId}.").ToArray();
            return new(accountId, date, asOf, "USD", positions, total, overall, cost, pnl, warnings.Length == 0, warnings);

            decimal? NullableDecimal(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            decimal? NullableMoney(int i) => reader.IsDBNull(i) ? null : Money(reader.GetDecimal(i));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
