using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GittBilSmsCore.Migrations
{
    /// <inheritdoc />
    public partial class Update_BlacklistedNumber_WithCompanyAndUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectoryNumbers_Directories_PhoneDirectoryDirectoryId",
                table: "DirectoryNumbers");

            migrationBuilder.DropIndex(
                name: "IX_DirectoryNumbers_PhoneDirectoryDirectoryId",
                table: "DirectoryNumbers");

            migrationBuilder.DropColumn(
                name: "PhoneDirectoryDirectoryId",
                table: "DirectoryNumbers");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "BlacklistNumbers");

            migrationBuilder.RenameColumn(
                name: "BlacklistNumberId",
                table: "BlacklistNumbers",
                newName: "Id");

            migrationBuilder.AlterColumn<string>(
                name: "Number",
                table: "BlacklistNumbers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "BlacklistNumbers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "BlacklistNumbers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryNumbers_DirectoryId",
                table: "DirectoryNumbers",
                column: "DirectoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistNumbers_CompanyId",
                table: "BlacklistNumbers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistNumbers_CreatedByUserId",
                table: "BlacklistNumbers",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BlacklistNumbers_AspNetUsers_CreatedByUserId",
                table: "BlacklistNumbers",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BlacklistNumbers_Companies_CompanyId",
                table: "BlacklistNumbers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "CompanyId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DirectoryNumbers_Directories_DirectoryId",
                table: "DirectoryNumbers",
                column: "DirectoryId",
                principalTable: "Directories",
                principalColumn: "DirectoryId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BlacklistNumbers_AspNetUsers_CreatedByUserId",
                table: "BlacklistNumbers");

            migrationBuilder.DropForeignKey(
                name: "FK_BlacklistNumbers_Companies_CompanyId",
                table: "BlacklistNumbers");

            migrationBuilder.DropForeignKey(
                name: "FK_DirectoryNumbers_Directories_DirectoryId",
                table: "DirectoryNumbers");

            migrationBuilder.DropIndex(
                name: "IX_DirectoryNumbers_DirectoryId",
                table: "DirectoryNumbers");

            migrationBuilder.DropIndex(
                name: "IX_BlacklistNumbers_CompanyId",
                table: "BlacklistNumbers");

            migrationBuilder.DropIndex(
                name: "IX_BlacklistNumbers_CreatedByUserId",
                table: "BlacklistNumbers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "BlacklistNumbers");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "BlacklistNumbers");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "BlacklistNumbers",
                newName: "BlacklistNumberId");

            migrationBuilder.AddColumn<int>(
                name: "PhoneDirectoryDirectoryId",
                table: "DirectoryNumbers",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Number",
                table: "BlacklistNumbers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "BlacklistNumbers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryNumbers_PhoneDirectoryDirectoryId",
                table: "DirectoryNumbers",
                column: "PhoneDirectoryDirectoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_DirectoryNumbers_Directories_PhoneDirectoryDirectoryId",
                table: "DirectoryNumbers",
                column: "PhoneDirectoryDirectoryId",
                principalTable: "Directories",
                principalColumn: "DirectoryId");
        }
    }
}
