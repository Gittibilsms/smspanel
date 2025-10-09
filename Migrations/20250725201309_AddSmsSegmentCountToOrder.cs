using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GittBilSmsCore.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsSegmentCountToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SmsCount",
                table: "Orders",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmsCount",
                table: "Orders");
        }
    }
}
