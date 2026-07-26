namespace Fenrix.IaCStudio.Contracts.Providers;

/// <summary>
/// Capability flags a repository-host adapter advertises so the UI only shows features the provider actually
/// supports. A missing adapter (Generic Git) advertises <see cref="None"/> and degrades gracefully to plain
/// Git. See docs/09-provider-integrations.md.
/// </summary>
[Flags]
public enum ProviderCapabilities
{
    None = 0,

    /// <summary>Can list the authenticated user's / org's repositories.</summary>
    BrowseRepositories = 1 << 0,

    /// <summary>Can create a new remote repository.</summary>
    CreateRepository = 1 << 1,

    /// <summary>Can list pull/merge requests.</summary>
    ListPullRequests = 1 << 2,

    /// <summary>Can open a pull/merge request.</summary>
    CreatePullRequest = 1 << 3,

    /// <summary>Can read CI/pipeline / Actions run status.</summary>
    PipelineStatus = 1 << 4,

    /// <summary>Can read branch-protection / branch-policy rules.</summary>
    BranchPolicies = 1 << 5,

    /// <summary>Uses "merge request" (GitLab) rather than "pull request" terminology.</summary>
    UsesMergeRequestTerminology = 1 << 6
}
