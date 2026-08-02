# 33 · Variables manager (Phase 12)

A per-environment, typed editor for Terraform input variables — so you set values in a form instead of hand-editing
tfvars text, and can see at a glance which required variables are missing.

## What it does

- Parses every `variable "x" { … }` declaration from the environment's `.tf` files (type, description, default,
  `sensitive`) and merges them with the current values from the environment's tfvars file.
- Renders a typed control per variable: text for `string`, number for `number`, a true/false select for `bool`,
  and a raw-HCL box for lists/maps/objects/`any`.
- Flags **required** variables (no default) and highlights any that are **missing** a value; masks **sensitive**
  values behind a reveal toggle.
- Saves back to the environment's `<slug>.tfvars` (the file the environment loads via `-var-file`) through the
  atomic-write + file-history path — so every save is versioned and recoverable.

## Design

- **Reuses the HCL toolkit** (`HclReader`) — no new parser. `VariableParser` (pure, Application) parses
  declarations and tfvars; `VariablesService` (Infrastructure) reads the env's `.tf`/tfvars and writes via
  `IFileTreeService`. New ribbon tab **Variables** → `VariablesEditor.razor`.
- **No database, no migration.** Files stay the source of truth; the tfvars file is rewritten from the form
  values (each value stored as verbatim HCL; unset values are omitted).
- Sits outside the verified plan/apply services — it only reads config and writes the tfvars file.
