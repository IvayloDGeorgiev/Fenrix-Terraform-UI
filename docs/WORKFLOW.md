# Development Workflow

How we build Fenrix IaC Studio. This is the operating manual; [ROADMAP.md](ROADMAP.md) is *what* we build and in what order, [PROGRESS.md](PROGRESS.md) is *where we are*.

## Guiding rules (repeat until reflexive)

1. Terraform and Git remain the execution engines.
2. Files on disk are the source of truth; the database is an index.
3. No infrastructure change applies unless the exact reviewed plan passes the safety checks.

Every code review checks the change against these three rules.

## Branching

- `main` — always buildable and releasable.
- `feature/<phase>-<slug>` — one feature slice per branch (e.g. `feature/03-terraform-process-runner`).
- `fix/<slug>`, `chore/<slug>`, `docs/<slug>` as appropriate.
- Short-lived branches; rebase or squash-merge to keep history clean.

## Commit conventions

Conventional-commit style prefixes: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `build:`. Reference the phase and, where useful, the doc: `feat(terraform): saved-plan hashing (#06)`.

## Definition of Done

A feature slice is done when:

- [ ] Behaviour matches the relevant design doc (link it in the PR).
- [ ] Unit tests cover the logic; integration tests cover any real-tool interaction.
- [ ] Safety-relevant paths have security tests ([17-testing-strategy.md](17-testing-strategy.md)).
- [ ] No secret can reach logs, history, DB, or manifests ([11-secrets.md](11-secrets.md)).
- [ ] Cancellation and error classification handled ([16-error-handling.md](16-error-handling.md)).
- [ ] `dotnet build` and `dotnet test` pass on `net10.0-windows`.
- [ ] The matching [PROGRESS.md](PROGRESS.md) item is ticked with a PR link.

## Build & test loop

```powershell
# restore, build, test (Windows target)
dotnet restore
dotnet build -f net10.0-windows10.0.19041.0
dotnet test
```

Inner loop stays unpackaged (`WindowsPackageType=None`); packaging is a release concern ([18-packaging-deployment.md](18-packaging-deployment.md)).

## Adding a new project to the solution

Add libraries only when a phase first needs them (see [02-solution-structure.md](02-solution-structure.md)); don't scaffold empty projects ahead of need. When adding one:

1. `dotnet new classlib -n Fenrix.IaCStudio.<Layer>` under `src/`.
2. Add to the solution; set references per the dependency rules.
3. Add the mirror test project under `tests/`.
4. Register implementations in `MauiProgram` at the composition root.

## Coding standards

- Nullable enabled; treat warnings seriously.
- One public type per file; namespaces mirror folders.
- Vertical feature slices in Application (request + handler + validators + result together).
- No `cmd.exe` string commands — always `ProcessStartInfo.ArgumentList`.
- Async + `CancellationToken` on every I/O and process call.
- Interfaces in the layer that needs them; implementations in Infrastructure/engines.

## Working with the docs

Design docs are the contract. If implementation reveals a doc is wrong, **update the doc in the same PR** and, for significant decisions, add an [ADR](adr/README.md). Docs and code never drift silently.

## Per-phase rhythm

For each phase in [ROADMAP.md](ROADMAP.md): pick the phase → break into feature slices as tasks → implement slice-by-slice against the docs → keep [PROGRESS.md](PROGRESS.md) current → close the phase only when its acceptance items pass.
