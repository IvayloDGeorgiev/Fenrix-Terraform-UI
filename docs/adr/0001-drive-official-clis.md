# ADR-0001 · Drive the official CLIs; do not reimplement Terraform or Git

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

Fenrix needs to plan/apply Terraform and perform Git operations. Two broad options exist: (a) reimplement or embed engine logic in .NET (e.g. a Git library, a custom HCL/plan engine), or (b) shell out to the genuine `terraform.exe` and `git.exe`.

## Decision

Fenrix **drives the official command-line tools** as the execution engines:

- `terraform.exe` for all Terraform operations.
- `git.exe` for all normal Git operations.
- `az`, `aws`, `gcloud` optionally, for cloud account discovery only.

Provider **REST APIs** are used only for host-specific features that Git/Terraform cannot provide (repository discovery/creation, pull/merge requests, pipeline status, branch policies, identity, org/workspace discovery).

Structured output is preferred everywhere it exists: Terraform JSON (`plan` streams, `show -json`, `providers schema -json`, `validate -json`) and Git porcelain v2 (`git status --porcelain=v2 -z`).

## Consequences

**Positive.** Exact behavioural parity with the tools users already trust; automatic support for new Terraform/Git versions and commands; smaller, safer codebase; no divergent state handling.

**Negative / mitigations.** Output formats can change between versions → parse versioned JSON, ignore unknown minor fields, reject unsupported majors, keep fixtures from multiple versions, and keep a raw terminal fallback. Interactive commands (`terraform console`, `login`) need a real pseudoconsole terminal rather than pure redirection.

**Rejected alternative.** Embedding a .NET Git library or custom plan engine — rejected for divergence risk, security surface, and maintenance cost.
