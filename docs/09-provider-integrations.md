# 09 · Version-Control Provider Integrations

Core Git stays provider-independent (via `git.exe`). Host-specific features go through small provider **adapters** behind a common interface, so a missing adapter degrades gracefully to generic Git.

## Common abstraction

```csharp
public interface IRepositoryProvider
{
    string ProviderType { get; }

    Task<ProviderUser> GetCurrentUserAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RemoteRepository>> GetRepositoriesAsync(CancellationToken cancellationToken);
    Task<RemoteRepository> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken);
    Task<PullRequestResult> CreatePullRequestAsync(CreatePullRequestRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(string repositoryId, CancellationToken cancellationToken);
}
```

Each provider exposes **capability flags** so the UI only shows features the provider actually supports.

## Supported providers

**Generic Git.** Any Git-compatible HTTPS or SSH remote, including self-hosted servers, Gitea, Forgejo, GitBucket, and unknown providers. Host-specific PR/MR functionality is unavailable without an adapter — normal Git still works fully.

**GitHub.** Account/org login, repository discovery, repository creation, pull requests, reviews, branch-protection info, Actions status, releases. Uses GitHub's versioned REST APIs.

**Azure DevOps.** Organisation selection, team project selection, repository discovery/creation, pull requests, branch policies, pipeline status, work-item links. Uses Azure DevOps repo/branch/commit/push/PR APIs.

**Bitbucket Cloud.** Workspace selection, repository discovery, pull requests, branch restrictions, pipelines, repository creation.

**GitLab.** Both GitLab.com and self-managed. Projects, branches, merge requests, pipelines, groups, protected branches.

**AWS CodeCommit.** AWS account & region, repository discovery, HTTPS / SSH / `git-remote-codecommit`, branches, pull requests where supported, IAM-based permissions. (CodeCommit was reopened to new customers in November 2025; current docs support Git credentials and `git-remote-codecommit` auth.)

## Delivery order (Phase 7)

1. Generic Git → 2. GitHub → 3. Azure DevOps → 4. Bitbucket → 5. GitLab → 6. AWS CodeCommit → 7. Self-hosted provider configurations.

Each adds repository browsing, repository creation, pull/merge requests, pipeline status, and branch-policy display, gated by capability flags.

## Design principles

- Keep core Git operations provider-independent; adapters are thin.
- Prefer official REST APIs with versioning; store fixtures for contract tests ([17-testing-strategy.md](17-testing-strategy.md)).
- Store only a secret *reference* for each provider connection ([11-secrets.md](11-secrets.md)).
- Fall back to generic Git when no adapter matches a remote.
