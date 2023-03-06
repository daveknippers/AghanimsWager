using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace csharp_ef_webapi.Migrations
{
    public partial class DiscordName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Kali");

            migrationBuilder.RenameTable(
                name: "balance_ledger",
                newName: "balance_ledger",
                newSchema: "Kali");

            migrationBuilder.AlterColumn<long>(
                name: "discord_id",
                schema: "Kali",
                table: "balance_ledger",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateTable(
                name: "discord_ids",
                schema: "Kali",
                columns: table => new
                {
                    discord_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    steam_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    discord_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discord_ids", x => x.discord_id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discord_ids",
                schema: "Kali");

            migrationBuilder.RenameTable(
                name: "balance_ledger",
                schema: "Kali",
                newName: "balance_ledger");

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "balance_ledger",
                type: "text",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
