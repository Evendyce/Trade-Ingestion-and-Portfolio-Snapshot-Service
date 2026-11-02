using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Api.Data;

namespace Portfolio.Tests;

public sealed class DeploymentTests
{
    [Fact]
    public async Task Deployment_script_initializes_a_clean_database_and_can_be_reapplied()
    {
        await using var factory = new PortfolioFactory();
        factory.Settings["Database:Initialize"] = "false";
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDb>();
        var builder = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        var database = builder.InitialCatalog;
        Assert.Matches("^PortfolioTests_[a-f0-9]{32}$", database);
        builder.InitialCatalog = "master";
        try
        {
            await using (var master = new SqlConnection(builder.ConnectionString))
            {
                await master.OpenAsync();
                await using var command = master.CreateCommand();
                // The generated and validated identifier is the only interpolated SQL identifier.
                command.CommandText = $"CREATE DATABASE [{database}] COLLATE Latin1_General_100_BIN2;";
                await command.ExecuteNonQueryAsync();
            }
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Portfolio.sln"))) root = root.Parent;
            Assert.NotNull(root);
            var sql = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database", "deploy.sql"));
            var batches = Regex.Split(sql, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            await db.Database.OpenConnectionAsync();
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var batch in batches) await db.Database.ExecuteSqlRawAsync(batch);
            }
            await db.Database.CloseConnectionAsync();
            Assert.Equal(2, await db.Instruments.CountAsync());
            Assert.Equal(3, await db.Prices.CountAsync());
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Equal(2, applied.Count());
            var snapshot = await client.GetFromJsonAsync<Portfolio.Api.PortfolioSnapshot>(
                "/api/accounts/EMPTY/snapshot?date=2025-03-01");
            Assert.True(snapshot!.IsComplete);
        }
        finally { await ((IAsyncLifetime)factory).DisposeAsync(); }
    }
}
