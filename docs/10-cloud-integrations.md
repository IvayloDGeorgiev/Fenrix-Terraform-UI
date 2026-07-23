# 10 · Cloud Account Integrations

Terraform providers already consume credentials from environment variables, shared credentials files, CLI login caches, and workload identities. **Fenrix does not create a competing credential system** — it selects accounts and builds the correct environment for each command at execution time.

## Common interface

```csharp
public interface ICloudConnectionProvider
{
    CloudProviderType ProviderType { get; }

    Task<CloudConnectionStatus> TestAsync(CloudConnection connection, CancellationToken cancellationToken);
    Task<IReadOnlyList<CloudScope>> GetAvailableScopesAsync(CloudConnection connection, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> BuildEnvironmentAsync(
        CloudConnection connection, ProjectEnvironment environment, CancellationToken cancellationToken);
}
```

`BuildEnvironmentAsync` returns the process-scoped environment variables for a command. **Secret values are resolved only at execution time**, never persisted in the returned map's stored form.

## Azure

Supports Azure CLI user login, tenant selection, subscription selection, service principal, client certificate, managed identity (in compatible environments), and environment-variable auth. For desktop users, launch `az login` (browser or Windows account-manager auth; supports subscription selection).

Environment bindings may include:

```text
ARM_TENANT_ID
ARM_SUBSCRIPTION_ID
ARM_CLIENT_ID
ARM_CLIENT_SECRET
```

Secret values (e.g. client secret) are resolved only at command execution time from secure storage.

## AWS

Supports shared profiles, IAM Identity Center / SSO, access-key profiles, assume-role profiles, region selection, account verification. Prefer IAM Identity Center:

```text
aws configure sso
aws sso login --profile <profile>
```

Environment bindings:

```text
AWS_PROFILE
AWS_REGION
AWS_DEFAULT_REGION
```

Do **not** copy credentials from the AWS credentials file into the Fenrix database — reference the profile.

## Google Cloud

Supports gcloud user account, Application Default Credentials, service-account file reference, project selection, workforce identity. Local developer auth:

```text
gcloud auth application-default login
```

Environment bindings may include:

```text
GOOGLE_PROJECT
GOOGLE_CLOUD_PROJECT
GOOGLE_APPLICATION_CREDENTIALS
```

Store only the service-account file **path**, not its JSON contents, unless the user explicitly imports it into secure Windows storage.

## Per-environment mapping

Each `ProjectEnvironment` may reference a different `CloudConnection` (subscription/account/project). Selecting an environment selects its cloud scope; `BuildEnvironmentAsync` composes the exact variables for that environment's commands. Connection testing (`TestAsync`) and scope discovery (`GetAvailableScopesAsync`) power the Connections hub.

Connections are defined once in a **global library** and bound per environment (with a project-level default and creation-time guidance). The full model — library + per-environment binding + project default + validation — is in [26-connections.md](26-connections.md) and [ADR-0005](adr/0005-connections-model.md).
