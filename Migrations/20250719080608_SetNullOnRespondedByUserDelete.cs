using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GittBilSmsCore.Migrations
{
    /// <inheritdoc />
    public partial class SetNullOnRespondedByUserDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketResponses_AspNetUsers_RespondedByUserId",
                table: "TicketResponses");

            migrationBuilder.AddForeignKey(
                name: "FK_TicketResponses_AspNetUsers_RespondedByUserId",
                table: "TicketResponses",
                column: "RespondedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketResponses_AspNetUsers_RespondedByUserId",
                table: "TicketResponses");

            migrationBuilder.AddForeignKey(
                name: "FK_TicketResponses_AspNetUsers_RespondedByUserId",
                table: "TicketResponses",
                column: "RespondedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
