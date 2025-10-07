using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GittBilSmsCore.Migrations
{
    /// <inheritdoc />
    public partial class RenameDirectoryToPhoneDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectoryNumbers_Directories_DirectoryId",
                table: "DirectoryNumbers");

            migrationBuilder.DropIndex(
                name: "IX_DirectoryNumbers_DirectoryId",
                table: "DirectoryNumbers");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "UserRoles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "PhoneDirectoryDirectoryId",
                table: "DirectoryNumbers",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "Directories",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "Directories",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "UserRoles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "Directories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "Directories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryNumbers_DirectoryId",
                table: "DirectoryNumbers",
                column: "DirectoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_DirectoryNumbers_Directories_DirectoryId",
                table: "DirectoryNumbers",
                column: "DirectoryId",
                principalTable: "Directories",
                principalColumn: "DirectoryId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
