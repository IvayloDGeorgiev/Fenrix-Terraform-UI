# 03 · Domain Model

Projects and environments are the core domain concepts. Everything else (plans, command runs, connections) hangs off them.

## Recommended new-project structure

```text
Projects\
└── CustomerInfrastructure\
    ├── .git\
    ├── .gitignore
    ├── README.md
    ├── .fenrix\
    │   └── project.json
    ├── modules\
    │   ├── networking\
    │   ├── compute\
    │   └── database\
    └── environments\
        ├── dev\   { main.tf providers.tf variables.tf outputs.tf dev.tfvars  backend.hcl }
        ├── uat\   { main.tf providers.tf variables.tf outputs.tf uat.tfvars  backend.hcl }
        └── live\  { main.tf providers.tf variables.tf outputs.tf live.tfvars backend.hcl }
```

This is a *recommendation for new projects only*. Existing projects keep whatever they have (see [Import](#existing-project-import)).

## Logical project model

```csharp
public sealed class InfrastructureProject
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? RepositoryRootPath { get; set; }
    public string? Description { get; set; }
    public string? RequiredTerraformVersion { get; set; }
    public ICollection<ProjectEnvironment> Environments { get; set; } = [];
}
```

`RootPath` is the project folder; `RepositoryRootPath` is the Git repo root (may differ — a project can be a subfolder of a monorepo). `RequiredTerraformVersion` is detected from config where possible.

## Environment model

```csharp
public sealed class ProjectEnvironment
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }

    public string Name { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;

    public string? TerraformWorkspace { get; set; }
    public string? VariablesFile { get; set; }
    public string? BackendConfigFile { get; set; }

    public Guid? CloudConnectionId { get; set; }
    public string? GitBranchPolicy { get; set; }

    public bool IsProduction { get; set; }
    public int DisplayOrder { get; set; }
}
```

`WorkingDirectory` is the actual directory Terraform runs in for that environment — the crucial mapping that lets any folder layout work. An environment can also map to a Terraform **workspace** instead of, or in addition to, a directory. `CloudConnectionId` binds the environment to a connection from the global **Connections** library — each environment can target a different account/subscription by design (see [26-connections.md](26-connections.md)).

Default environments: **Dev · UAT · Live**. Users can rename, add, remove, and reorder them; mark any as production; map each to a different directory and/or workspace; and assign a different cloud account/subscription per environment.

## Project manifest

Optional, written to `.fenrix/project.json` inside the project. It records the logical structure so a project can be shared/re-opened consistently. It must **never** contain passwords, tokens, client secrets, or cloud access keys.

```json
{
  "schemaVersion": 1,
  "projectId": "f393a4e5-70fa-49cf-9841-636a67ccbcee",
  "name": "Customer Infrastructure",
  "terraformVersion": "1.15.0",
  "environments": [
    { "name": "Dev",  "path": "environments/dev",  "variablesFile": "dev.tfvars",  "backendConfigFile": "backend.hcl", "isProduction": false },
    { "name": "UAT",  "path": "environments/uat",  "variablesFile": "uat.tfvars",  "backendConfigFile": "backend.hcl", "isProduction": false },
    { "name": "Live", "path": "environments/live", "variablesFile": "live.tfvars", "backendConfigFile": "backend.hcl", "isProduction": true  }
  ]
}
```

## Existing project import

The **Add Existing Project** wizard:

1. User selects a folder.
2. Scan for `.tf`, `.tfvars`, `.hcl`, `.terraform.lock.hcl`.
3. Detect whether the folder is in a Git repository.
4. Detect likely environment directories.
5. Detect the currently selected Terraform workspace where possible.
6. Detect configured providers.
7. Detect backend configuration.
8. Detect a required Terraform version.
9. Suggest environment mappings.
10. Let the user correct the mappings.
11. Register the project **without moving or rewriting files**.
12. Optionally create `.fenrix/project.json`.
13. Suggest suitable `.gitignore` entries.

Projects outside the Fenrix projects directory are registered as **linked** projects. Fenrix never silently copies them into its own directory.

## Windows directory layout

Application binaries install separately from user-managed data.

```text
Install:   C:\Program Files\FenrixSource\Fenrix IaC Studio\

Data root: C:\FenrixSource\FenrixIaCStudio\
  Data\      fenrix.db · settings.json · migrations\
  Logs\      application\ terraform\ git\ diagnostics\
  Projects\
  Cache\     terraform-schemas\ repository-data\ provider-metadata\
  Temp\      commands\ plans\ downloads\
  Tools\     terraform\ graphviz\
  Backups\   manifests\ database\
```

Because normal apps may lack permission to write under `C:\`, the installer creates this root and grants the current user modify access. If creation fails, fall back to `%LOCALAPPDATA%\FenrixSource\FenrixIaCStudio\`. The root is configurable in Settings.
