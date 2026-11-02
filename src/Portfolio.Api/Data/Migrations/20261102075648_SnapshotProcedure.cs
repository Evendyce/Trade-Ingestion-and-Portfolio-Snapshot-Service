using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            using var stream = typeof(SnapshotProcedure).Assembly.GetManifestResourceStream("Portfolio.Api.Sql.PortfolioSnapshot.sql")
                ?? throw new InvalidOperationException("Snapshot SQL resource is missing.");
            using var reader = new StreamReader(stream);
            // Dynamic SQL also permits CREATE PROCEDURE inside EF's idempotent IF wrapper.
            var sql = reader.ReadToEnd().Replace("'", "''", StringComparison.Ordinal);
            migrationBuilder.Sql($"EXEC(N'{sql}');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.GetPortfolioSnapshot;");
        }
    }
}
