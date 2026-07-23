# 02 · Solution Structure

Clean Architecture boundaries, with the Application layer organised as **vertical feature slices** rather than a bag of generic services.

> **Note on current state.** The repository today is the default single-project MAUI template (`Fenrix Terraform UI.csproj`). The structure below is the target. The existing project becomes `Fenrix.IaCStudio.App`; the remaining class libraries are added incrementally as phases require them (see [ROADMAP.md](ROADMAP.md)). Do not create empty projects ahead of need — add each library in the phase that first uses it.

## Target solution

```text
Fenrix.IaCStudio.sln

src/
  Fenrix.IaCStudio.App/            # MAUI Blazor Hybrid shell (current project)
    MauiProgram.cs
    App.xaml
    MainPage.xaml
    Components/  Pages/  Layout/  State/
    wwwroot/

  Fenrix.IaCStudio.Domain/         # Entities, value objects, enums, rules
    Projects/  Environments/  Terraform/  Git/  Cloud/  Security/  Common/

  Fenrix.IaCStudio.Application/     # Use cases (vertical features)
    Projects/  Terraform/  Git/  Cloud/  Files/  Settings/  Jobs/  Validation/

  Fenrix.IaCStudio.Infrastructure/  # Concrete implementations
    Persistence/  FileSystem/  Processes/  Logging/  Security/  Windows/  Updates/

  Fenrix.IaCStudio.Terraform/       # Terraform engine
    Commands/  Discovery/  Execution/  Parsing/  Plans/  State/  Schemas/  Workspaces/

  Fenrix.IaCStudio.Git/             # Git engine
    Commands/  Parsing/  History/  Diff/  Conflicts/  Credentials/

  Fenrix.IaCStudio.Integrations/    # Provider & cloud adapters
    GitHub/  AzureDevOps/  Bitbucket/  GitLab/  AwsCodeCommit/  Azure/  Aws/  GoogleCloud/

  Fenrix.IaCStudio.Contracts/       # DTOs, events, results shared across boundaries
    DTOs/  Events/  Results/

tests/
  Fenrix.IaCStudio.Domain.Tests/
  Fenrix.IaCStudio.Application.Tests/
  Fenrix.IaCStudio.Infrastructure.Tests/
  Fenrix.IaCStudio.Terraform.Tests/
  Fenrix.IaCStudio.Git.Tests/
  Fenrix.IaCStudio.IntegrationTests/
  Fenrix.IaCStudio.UI.Tests/
```

## Project reference graph

```text
App ──────────► Application ──────► Domain
 │                  │                 ▲
 │                  ├──► Contracts ───┘
 │                  │
 │             (abstractions)
 ▼                  ▼
Infrastructure ─► Terraform, Git, Integrations ─► Domain, Contracts
        │
        └────────────────────────────► Domain, Contracts
```

Rules enforced by reference direction:

- `Domain` references nothing internal.
- `Contracts` references only `Domain` (or nothing).
- `Application` references `Domain`, `Contracts`, and interface definitions; **not** `Infrastructure`.
- `Terraform`, `Git`, `Integrations` reference `Domain`, `Contracts`, and Infrastructure abstractions.
- `Infrastructure` references the engines/integrations it implements plus `Domain`/`Contracts` and external SDKs.
- `App` references `Application` + `Contracts` and wires implementations at startup.

## Naming and layout conventions

- Namespaces mirror folders: `Fenrix.IaCStudio.Application.Terraform.Plan`.
- One public type per file; file name matches the type.
- Feature folders group a use case's request, handler, validators, and result together (vertical slice).
- Interfaces live with the abstraction they describe; implementations live in `Infrastructure` (or the relevant engine).
- Test projects mirror the source project they cover, one assembly each.

## Where existing template files map

| Current | Target |
|---------|--------|
| `Fenrix Terraform UI.csproj` | `src/Fenrix.IaCStudio.App/Fenrix.IaCStudio.App.csproj` |
| `MauiProgram.cs`, `App.xaml`, `MainPage.xaml` | move under `src/Fenrix.IaCStudio.App/` |
| `Components/`, `wwwroot/`, `Platforms/`, `Resources/` | stay under the App project |

The rename/move is a Phase 1 task. Until then, docs refer to logical projects by name.
