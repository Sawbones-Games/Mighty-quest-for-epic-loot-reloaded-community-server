using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MQEL.Data.Migrations
{
    /// <inheritdoc />
    public partial class InventoryStackCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StackCount",
                table: "InventoryItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StackCount",
                table: "InventoryItems");
        }
    }
}
