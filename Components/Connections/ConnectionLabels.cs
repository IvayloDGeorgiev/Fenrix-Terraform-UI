using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix_Terraform_UI.Components.Connections;

/// <summary>Display names + icon names for connection provider types (UI only).</summary>
public static class ConnectionLabels
{
    public static string Repo(RepositoryProviderType type) => type switch
    {
        RepositoryProviderType.GenericGit => "Generic Git",
        RepositoryProviderType.GitHub => "GitHub",
        RepositoryProviderType.AzureDevOps => "Azure DevOps",
        RepositoryProviderType.Bitbucket => "Bitbucket",
        RepositoryProviderType.GitLab => "GitLab",
        RepositoryProviderType.AwsCodeCommit => "AWS CodeCommit",
        _ => type.ToString()
    };

    public static string Cloud(CloudProviderType type) => type switch
    {
        CloudProviderType.Azure => "Microsoft Azure",
        CloudProviderType.Aws => "Amazon Web Services",
        CloudProviderType.GoogleCloud => "Google Cloud",
        _ => "Unknown"
    };

    public static string Status(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Ok => "Connected",
        ConnectionStatus.Failed => "Failed",
        _ => "Untested"
    };

    public static string StatusClass(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Ok => "ok",
        ConnectionStatus.Failed => "fail",
        _ => "untested"
    };
}
