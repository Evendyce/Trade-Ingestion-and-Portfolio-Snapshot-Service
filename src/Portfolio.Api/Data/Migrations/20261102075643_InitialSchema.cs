using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Portfolio.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    Isin = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Isin);
                });

            migrationBuilder.CreateTable(
                name: "Prices",
                columns: table => new
                {
                    InstrumentId = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prices", x => new { x.InstrumentId, x.Date });
                    table.CheckConstraint("CK_Prices_Positive", "[Price] > 0");
                    table.ForeignKey(
                        name: "FK_Prices_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Isin",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalRef = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InstrumentId = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRevisions", x => x.Id);
                    table.CheckConstraint("CK_TradeRevisions_Price", "[Price] > 0 AND [Price] <= 1000000000");
                    table.CheckConstraint("CK_TradeRevisions_Quantity", "[Quantity] > 0 AND [Quantity] <= 1000000000");
                    table.CheckConstraint("CK_TradeRevisions_Side", "[Side] IN ('BUY', 'SELL')");
                    table.ForeignKey(
                        name: "FK_TradeRevisions_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Isin",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IngestionReceipts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalRef = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RevisionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngestionReceipts_TradeRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "TradeRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Instruments",
                columns: new[] { "Isin", "Name", "Symbol" },
                values: new object[,]
                {
                    { "US0378331005", "Apple Inc.", "AAPL" },
                    { "US5949181045", "Microsoft Corporation", "MSFT" }
                });

            migrationBuilder.InsertData(
                table: "Prices",
                columns: new[] { "Date", "InstrumentId", "Price" },
                values: new object[,]
                {
                    { new DateOnly(2025, 3, 1), "US0378331005", 186m },
                    { new DateOnly(2025, 3, 3), "US0378331005", 190m },
                    { new DateOnly(2025, 3, 1), "US5949181045", 400m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionReceipts_AccountId_ReceivedAt",
                table: "IngestionReceipts",
                columns: new[] { "AccountId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionReceipts_RevisionId",
                table: "IngestionReceipts",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRevisions_AccountId_ExternalRef_AsOf",
                table: "TradeRevisions",
                columns: new[] { "AccountId", "ExternalRef", "AsOf" })
                .Annotation("SqlServer:Include", new[] { "InstrumentId", "Side", "Quantity", "Price", "TradeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeRevisions_ExternalRef_AsOf",
                table: "TradeRevisions",
                columns: new[] { "ExternalRef", "AsOf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeRevisions_InstrumentId",
                table: "TradeRevisions",
                column: "InstrumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestionReceipts");

            migrationBuilder.DropTable(
                name: "Prices");

            migrationBuilder.DropTable(
                name: "TradeRevisions");

            migrationBuilder.DropTable(
                name: "Instruments");
        }
    }
}
