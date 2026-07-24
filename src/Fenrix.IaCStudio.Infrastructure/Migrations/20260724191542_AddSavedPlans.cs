using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fenrix.IaCStudio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CloudConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderType = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantOrAccountId = table.Column<string>(type: "TEXT", nullable: true),
                    SubscriptionOrProjectId = table.Column<string>(type: "TEXT", nullable: true),
                    Region = table.Column<string>(type: "TEXT", nullable: true),
                    ProfileName = table.Column<string>(type: "TEXT", nullable: true),
                    Client = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                    SecretReferenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    LastTestedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommandRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tool = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Command = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RedactedArguments = table.Column<string>(type: "TEXT", nullable: false),
                    WorkingDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OutputLogPath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VersionLabel = table.Column<string>(type: "TEXT", nullable: false),
                    GitCommit = table.Column<string>(type: "TEXT", nullable: false),
                    GitBranch = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigurationHash = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderLockHash = table.Column<string>(type: "TEXT", nullable: false),
                    TerraformVersion = table.Column<string>(type: "TEXT", nullable: false),
                    StateBackend = table.Column<string>(type: "TEXT", nullable: true),
                    StateSerial = table.Column<long>(type: "INTEGER", nullable: true),
                    StateLineage = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    InitiatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    AddCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DestroyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplaceCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    OriginalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    RefCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentRelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastChangedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileIdentityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    BlobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GitCommit = table.Column<string>(type: "TEXT", nullable: true),
                    GitBranch = table.Column<string>(type: "TEXT", nullable: true),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryRootPath = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredTerraformVersion = table.Column<string>(type: "TEXT", nullable: true),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RepositoryConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsLinked = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastOpenedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    GitCommit = table.Column<string>(type: "TEXT", nullable: false),
                    GitTag = table.Column<string>(type: "TEXT", nullable: true),
                    GitBranch = table.Column<string>(type: "TEXT", nullable: true),
                    ConfigurationHash = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderLockHash = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredTerraformVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecentFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    LastOpenedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CursorLine = table.Column<int>(type: "INTEGER", nullable: false),
                    CursorColumn = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderType = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Organisation = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectOrWorkspace = table.Column<string>(type: "TEXT", nullable: true),
                    SecretReferenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PlanCommandRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlanFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RelativePlanFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    WorkingDirectory = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    TerraformVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ConfigHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LockHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PlanFileHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AddCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DestroyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplaceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsProductionTarget = table.Column<bool>(type: "INTEGER", nullable: false),
                    CloudConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GitCommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    GitBranch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    GitTreeDirty = table.Column<bool>(type: "INTEGER", nullable: true),
                    Applied = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppliedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ApplyCommandRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsInvalidated = table.Column<bool>(type: "INTEGER", nullable: false),
                    InvalidatedReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceKey = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    WorkingDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    TerraformWorkspace = table.Column<string>(type: "TEXT", nullable: true),
                    VariablesFile = table.Column<string>(type: "TEXT", nullable: true),
                    BackendConfigFile = table.Column<string>(type: "TEXT", nullable: true),
                    CloudConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GitBranchPolicy = table.Column<string>(type: "TEXT", nullable: true),
                    IsProduction = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Environments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Name",
                table: "Clients",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CloudConnections_ClientId",
                table: "CloudConnections",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudConnections_DisplayName",
                table: "CloudConnections",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_CloudConnections_ProviderType",
                table: "CloudConnections",
                column: "ProviderType");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRuns_EnvironmentId",
                table: "CommandRuns",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRuns_ProjectId_StartedAt",
                table: "CommandRuns",
                columns: new[] { "ProjectId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandRuns_StartedAt",
                table: "CommandRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_EnvironmentId_Status",
                table: "Deployments",
                columns: new[] { "EnvironmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ProjectId",
                table: "Deployments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ProjectVersionId",
                table: "Deployments",
                column: "ProjectVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Environments_CloudConnectionId",
                table: "Environments",
                column: "CloudConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Environments_ProjectId",
                table: "Environments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FileBlobs_ContentHash",
                table: "FileBlobs",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileIdentities_ProjectId_CurrentRelativePath",
                table: "FileIdentities",
                columns: new[] { "ProjectId", "CurrentRelativePath" });

            migrationBuilder.CreateIndex(
                name: "IX_FileIdentities_ProjectId_IsDeleted",
                table: "FileIdentities",
                columns: new[] { "ProjectId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_BlobId",
                table: "FileVersions",
                column: "BlobId");

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_FileIdentityId_CapturedAt",
                table: "FileVersions",
                columns: new[] { "FileIdentityId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_ProjectId",
                table: "FileVersions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ClientId",
                table: "Projects",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVersions_ProjectId",
                table: "ProjectVersions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentFiles_LastOpenedAt",
                table: "RecentFiles",
                column: "LastOpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecentFiles_ProjectId",
                table: "RecentFiles",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryConnections_ClientId",
                table: "RepositoryConnections",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPlans_EnvironmentId_CreatedAt",
                table: "SavedPlans",
                columns: new[] { "EnvironmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedPlans_ProjectId_CreatedAt",
                table: "SavedPlans",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key_Scope_ScopeId",
                table: "Settings",
                columns: new[] { "Key", "Scope", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "CloudConnections");

            migrationBuilder.DropTable(
                name: "CommandRuns");

            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "Environments");

            migrationBuilder.DropTable(
                name: "FileBlobs");

            migrationBuilder.DropTable(
                name: "FileIdentities");

            migrationBuilder.DropTable(
                name: "FileVersions");

            migrationBuilder.DropTable(
                name: "ProjectVersions");

            migrationBuilder.DropTable(
                name: "RecentFiles");

            migrationBuilder.DropTable(
                name: "RepositoryConnections");

            migrationBuilder.DropTable(
                name: "SavedPlans");

            migrationBuilder.DropTable(
                name: "SecretReferences");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
