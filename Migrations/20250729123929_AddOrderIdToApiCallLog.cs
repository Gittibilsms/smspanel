using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GittBilSmsCore.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderIdToApiCallLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "ApiCallLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderRecipients",
                columns: table => new
                {
                    OrderRecipientId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecipientNumber = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRecipients", x => x.OrderRecipientId);
                    table.ForeignKey(
                        name: "FK_OrderRecipients_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiCallLogs_OrderId",
                table: "ApiCallLogs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRecipients_OrderId",
                table: "OrderRecipients",
                column: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiCallLogs_Orders_OrderId",
                table: "ApiCallLogs",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiCallLogs_Orders_OrderId",
                table: "ApiCallLogs");

            migrationBuilder.DropTable(
                name: "OrderRecipients");

            migrationBuilder.DropIndex(
                name: "IX_ApiCallLogs_OrderId",
                table: "ApiCallLogs");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "ApiCallLogs");
        }
    }
}
