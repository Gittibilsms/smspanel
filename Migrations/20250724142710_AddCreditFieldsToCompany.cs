using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GittBilSmsCore.Migrations
{
    public partial class AddCreditFieldsToCompany : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Credit",
                table: "Companies",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Companies",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentBalance",
                table: "Companies",
                type: "decimal(18,2)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentBalance",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Credit",
                table: "Companies");
        }
    }
}