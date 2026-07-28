using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fenrix.IaCStudio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeyPairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Bits = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PublicKeyOpenSsh = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    EncryptedFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SecretReferenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CloudConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CloudKeyName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    RegistrationWorkingDir = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastExportedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyPairs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeyPairs_ProjectId_CreatedAt",
                table: "KeyPairs",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeyPairs");
        }
    }
}
