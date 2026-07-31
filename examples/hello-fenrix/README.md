# Example · hello-fenrix

A minimal, **credential-free** Terraform project for trying Fenrix end to end. It uses only the `random` and
`local` providers, so `init` / `plan` / `apply` run without any cloud account — it just writes a text file into
`generated/`.

## Try it in Fenrix

1. **Projects → Import** and point Fenrix at this `hello-fenrix` folder. Fenrix reads it in place without
   changing anything.
2. Open the **Terraform** screen and run **Init** (watch the command preview, then the streamed output).
3. Open **Plan & apply**, generate a plan, and review the graph — you'll see two resources to add
   (`random_id.suffix`, `local_file.greeting`) and two outputs.
4. **Apply the reviewed plan.** A `generated/greeting-XXXX.txt` file appears; the outputs show its path.
5. Re-run plan: it now reports no changes. Edit `greeting` in `terraform.tfvars` and plan again to see a change.
6. Try **Inspect** to browse state, outputs, and the dependency graph, then **Destroy** from Plan & apply to
   clean up.

## Files

- `versions.tf` — required Terraform + provider versions.
- `variables.tf` — inputs (`greeting`, `output_dir`, `suffix_length`).
- `main.tf` — a random suffix + a local file resource.
- `outputs.tf` — the created file path and the random suffix.
- `terraform.tfvars` — default values.

Nothing here needs secrets, but as a habit keep real Terraform repos **private**: plan and state files can carry
plaintext secrets.
