using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fenrix.IaCStudio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ProjectVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VersionLabel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    GitCommit = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SavedPlanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlanFileHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RequestedByKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RequestedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DecidedByKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DecidedByName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DecidedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UserDisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnvironmentName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Target = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultTargetFile = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequireApprovalForProduction = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireApprovalForEnvironments = table.Column<string>(type: "TEXT", nullable: false),
                    BlockProductionDestroy = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequirePrivateRepositories = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiredBranchForProduction = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AllowedTerraformVersionConstraint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Permissions = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateParameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DefaultValue = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Required = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateParameters_ConfigTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ConfigTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_ProjectId_EnvironmentId_Status",
                table: "ApprovalRequests",
                columns: new[] { "ProjectId", "EnvironmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_SavedPlanId",
                table: "ApprovalRequests",
                column: "SavedPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status_RequestedAt",
                table: "ApprovalRequests",
                columns: new[] { "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action",
                table: "AuditEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                table: "AuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ProjectId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "ProjectId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UserKey",
                table: "AuditEvents",
                column: "UserKey");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigTemplates_Category",
                table: "ConfigTemplates",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigTemplates_Name",
                table: "ConfigTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_OrgRoles_Name",
                table: "OrgRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgUsers_UserKey",
                table: "OrgUsers",
                column: "UserKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserKey",
                table: "RoleAssignments",
                column: "UserKey");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserKey_EnvironmentId",
                table: "RoleAssignments",
                columns: new[] { "UserKey", "EnvironmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserKey_ProjectId",
                table: "RoleAssignments",
                columns: new[] { "UserKey", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateParameters_TemplateId",
                table: "TemplateParameters",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "OrgPolicies");

            migrationBuilder.DropTable(
                name: "OrgRoles");

            migrationBuilder.DropTable(
                name: "OrgUsers");

            migrationBuilder.DropTable(
                name: "RoleAssignments");

            migrationBuilder.DropTable(
                name: "TemplateParameters");

            migrationBuilder.DropTable(
                name: "ConfigTemplates");
        }
    }
}
