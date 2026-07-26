using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fenrix.IaCStudio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryConnectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedAt",
                table: "RepositoryConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "RepositoryConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastStatus",
                table: "RepositoryConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LastTestedAt",
                table: "RepositoryConnections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryConnections_DisplayName",
                table: "RepositoryConnections",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryConnections_ProviderType",
                table: "RepositoryConnections",
                column: "ProviderType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepositoryConnections_DisplayName",
                table: "RepositoryConnections");

            migrationBuilder.DropIndex(
                name: "IX_RepositoryConnections_ProviderType",
                table: "RepositoryConnections");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "RepositoryConnections");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "RepositoryConnections");

            migrationBuilder.DropColumn(
                name: "LastStatus",
                table: "RepositoryConnections");

            migrationBuilder.DropColumn(
                name: "LastTestedAt",
                table: "RepositoryConnections");
        }
    }
}
