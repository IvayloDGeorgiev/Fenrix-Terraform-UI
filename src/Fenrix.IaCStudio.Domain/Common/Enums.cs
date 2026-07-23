namespace Fenrix.IaCStudio.Domain.Common;

/// <summary>Cloud platforms Fenrix can build a credential environment for.</summary>
public enum CloudProviderType
{
    Unknown = 0,
    Azure = 1,
    Aws = 2,
    GoogleCloud = 3
}

/// <summary>Version-control hosts with provider-specific adapters.</summary>
public enum RepositoryProviderType
{
    GenericGit = 0,
    GitHub = 1,
    AzureDevOps = 2,
    Bitbucket = 3,
    GitLab = 4,
    AwsCodeCommit = 5
}

/// <summary>What kind of thing a connection points at.</summary>
public enum ConnectionKind
{
    Cloud = 0,
    Repository = 1
}

/// <summary>Result of the last connection test.</summary>
public enum ConnectionStatus
{
    Untested = 0,
    Ok = 1,
    Failed = 2
}

/// <summary>Lifecycle of a governed deployment. See docs/20-pipelines-deployments.md.</summary>
public enum DeploymentStatus
{
    Queued = 0,
    Planning = 1,
    AwaitingApproval = 2,
    Applying = 3,
    Succeeded = 4,
    Failed = 5,
    RolledBack = 6
}

/// <summary>Risk classification used by the safety policy. See docs/06-plan-apply-safety.md.</summary>
public enum TerraformRiskLevel
{
    ReadOnly = 0,
    Safe = 1,
    StateChanging = 2,
    Destructive = 3
}

/// <summary>Scope at which a setting applies; resolved most-specific-first.</summary>
public enum SettingScope
{
    Global = 0,
    Project = 1,
    Environment = 2
}

/// <summary>Which secure store backs a secret reference. See docs/11-secrets.md.</summary>
public enum SecretProvider
{
    WindowsCredentialManager = 0,
    WindowsDpapi = 1,
    GitCredentialManager = 2,
    AzureCliCache = 3,
    AwsSharedCredentials = 4,
    GoogleAdc = 5
}
