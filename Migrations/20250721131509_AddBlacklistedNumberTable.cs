using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GittBilSmsCore.Migrations
{
    /// <inheritdoc />
    public partial class AddBlacklistedNumberTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
        name: "BlacklistedNumbers",
        columns: table => new
        {
            Id = table.Column<int>(nullable: false)
                .Annotation("SqlServer:Identity", "1, 1"),
            Number = table.Column<string>(maxLength: 20, nullable: false),
            CompanyId = table.Column<int>(nullable: false),
            CreatedByUserId = table.Column<int>(nullable: false),
            CreatedAt = table.Column<DateTime>(nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_BlacklistedNumbers", x => x.Id);
            table.ForeignKey(
                name: "FK_BlacklistedNumbers_Companies_CompanyId",
                column: x => x.CompanyId,
                principalTable: "Companies",
                principalColumn: "CompanyId",
                onDelete: ReferentialAction.Restrict);
            table.ForeignKey(
                name: "FK_BlacklistedNumbers_Users_CreatedByUserId",
                column: x => x.CreatedByUserId,
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        });

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistedNumbers_CompanyId",
                table: "BlacklistedNumbers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistedNumbers_CreatedByUserId",
                table: "BlacklistedNumbers",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
    name: "BlacklistedNumbers");
        }
    }
}
