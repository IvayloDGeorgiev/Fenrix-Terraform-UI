using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fenrix.IaCStudio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentPipelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentPipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentPipelines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PipelineStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequirePreviousStageSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireCleanWorkingTree = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireTypedConfirmationForProduction = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiredBranch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Approvers = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineStages_DeploymentPipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "DeploymentPipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentPipelines_ProjectId",
                table: "DeploymentPipelines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStages_EnvironmentId",
                table: "PipelineStages",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStages_PipelineId",
                table: "PipelineStages",
                column: "PipelineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineStages");

            migrationBuilder.DropTable(
                name: "DeploymentPipelines");
        }
    }
}
