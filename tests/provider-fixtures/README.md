# Provider contract fixtures (Phase 7)

Representative JSON responses from each version-control host's REST API, used as **contract fixtures** for the
adapter parsers in `Fenrix.IaCStudio.Infrastructure/Providers/*`. They pin the exact field names each adapter
reads, so a parser that drifts from the real API shape is caught.

Layout: one folder per provider (`github`, `gitlab`, `azuredevops`, `bitbucket`), with a sample repository,
pull/merge request, pipeline/build, and (GitHub) branch-protection payload.

## What was verified in the authoring environment

MAUI itself can't be compiled here, so the pure logic was cross-checked with a Python reference port
(same approach as the Phase 5/6 git-parser verification):

- **`RepoUrlParser`** — 11/11 cases across HTTPS + scp-like SSH URLs for GitHub, GitLab (incl. subgroups),
  Azure DevOps (`_git` and `v3/…` SSH), Bitbucket, and generic remotes.
- **Adapter field mappings** — 106/106 key-presence checks: every JSON key each `Map*` method reads exists in
  the fixtures (nested paths like `head.ref`, `_links.web.href`, `links.clone[]` included).
- **Redaction/leakage** — grep confirms access tokens are confined to the DTO → `ConnectionService` →
  `RepositoryCredential` → `ISecretStore` path; no logger call references a token/secret value, and no token
  is written to a SQLite column (only a `SecretReference`).

To re-run against real APIs later, replace these fixtures with recorded live responses and keep the same
key-presence assertions.
