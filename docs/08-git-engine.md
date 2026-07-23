# 08 · Git Engine

Uses the installed **Git CLI as the source of truth** rather than a .NET Git library, with stable machine-readable formats where available.

## Status parsing

```text
git status --porcelain=v2 -z --branch --show-stash
```

Porcelain v2 is designed for machine parsing and is stable regardless of user config. `-z` gives NUL-terminated records (safe for any path). This is the backbone of the working-changes view.

## Feature set

**Repository management.** Initialise, clone, open existing, add/remove remotes, change remote URL, fetch, pull, push, prune, repository health check.

**Working changes.** Modified / added / deleted / renamed / untracked / ignored files; staged & unstaged groups; stage individual files; **stage selected lines**; unstage; discard; open diff.

**Commits.** Create, amend, commit templates, sign-off, history, search history, commit details, file history, compare commits, revert, cherry-pick, copy commit hash.

**Branches.** Local & remote branches; create, checkout, rename, delete, set upstream, compare; merge, rebase, abort/continue merge/rebase; ahead/behind indicators.

**Stashes.** Create (optionally include untracked), list, view, apply, pop, drop, create branch from stash.

**Tags.** Lightweight and annotated tags; push tags; delete local & remote tags.

**Advanced.** Reset (soft/mixed/hard), interactive rebase, conflict editor, blame, submodules, Git LFS indicators, worktrees, reflog, clean preview, **force-with-lease** push.

## Delivery in layers

Git is a large surface, so it ships in layers (see [ROADMAP.md](ROADMAP.md)): **Phase 5** makes clone, status, stage, commit, fetch, pull, push, branches, history, diff, stash, merge, and conflict detection reliable first; **Phase 6** adds interactive rebase, cherry-pick, reset, reflog, blame, tags, submodules, worktrees, LFS, the conflict editor, partial staging, and commit-graph optimisation.

## Safety

Destructive Git operations require confirmation (see [00-overview.md](00-overview.md) → principle 3): `reset --hard`, `clean`, force push, branch deletion, discarding uncommitted changes. Prefer `--force-with-lease` over `--force`. Clean shows a preview before running.

## Command transparency

Every Git action shows the exact `git …` command it will run before executing it, with a Copy button and redacted remotes/credentials — the same cross-cutting component used for Terraform ([23-command-transparency.md](23-command-transparency.md)).

## Credentials

Use **Git Credential Manager** (bundled with Git for Windows) rather than storing Git passwords in SQLite. GCM supports OAuth and secure OS credential stores. Fenrix stores only a secret *reference*, never the credential itself (see [11-secrets.md](11-secrets.md)).

## Parsing strategy

Prefer porcelain/stable formats: `--porcelain=v2` for status, `git log --format=...` with explicit format strings for history, `--numstat`/`-z` for diffs. Keep fixtures of real output for contract tests so parser changes are caught early.
