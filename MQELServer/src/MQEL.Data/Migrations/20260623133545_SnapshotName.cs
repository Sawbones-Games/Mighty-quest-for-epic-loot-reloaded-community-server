using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MQEL.Data.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SnapshotName",
                table: "Accounts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SnapshotName",
                table: "Accounts");
        }
    }
}
