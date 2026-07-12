using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MQEL.Data.Migrations
{
    /// <inheritdoc />
    public partial class HeroInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HeroInventory",
                columns: table => new
                {
                    HeroId = table.Column<long>(type: "INTEGER", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    ArchetypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    DyeTemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Stat0 = table.Column<double>(type: "REAL", nullable: false),
                    Stat1 = table.Column<double>(type: "REAL", nullable: false),
                    Stat2 = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeroInventory", x => new { x.HeroId, x.Slot });
                    table.ForeignKey(
                        name: "FK_HeroInventory_Heroes_HeroId",
                        column: x => x.HeroId,
                        principalTable: "Heroes",
                        principalColumn: "HeroId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeroInventory");
        }
    }
}
