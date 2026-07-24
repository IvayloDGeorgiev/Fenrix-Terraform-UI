# 11 · Secrets Architecture

**Never store plaintext secrets** in SQLite, SQL Server, project manifests, application logs, Terraform command history, Git remote URLs, or crash reports.

Fenrix uses a **secret-reference model**: it stores a pointer to where a secret lives in secure OS storage, and resolves the actual value only at command execution time.

```csharp
public sealed class SecretReference
{
    public Guid Id { get; init; }
    public string Provider { get; set; } = "WindowsCredentialManager";
    public string ReferenceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
```

## Secret providers

1. **Git Credential Manager** — for Git.
2. **Azure CLI cache** — for Azure user authentication.
3. **AWS shared credential & SSO stores** — for AWS.
4. **Google Application Default Credentials** — for GCP.
5. **Windows Credential Manager** — for Fenrix-specific secrets.
6. **Windows DPAPI** — for small local encrypted values.

Fenrix prefers to delegate to the tool-native store (1–4) so it never becomes the custodian of cloud/Git credentials. Only genuinely Fenrix-specific secrets use (5)/(6).

## Redaction

Logs and history redact values matching: known secret references, Terraform sensitive markers, environment-variable secrets, tokens, authorization headers, and password-like command arguments. Redaction happens **before** anything is written to disk. Raw plan/state/output JSON is parsed in memory and never persisted (see [06-plan-apply-safety.md](06-plan-apply-safety.md)).

## Rules of thumb

- Store a *reference*, never a *value*.
- Resolve values at execution time into a process-scoped environment; discard after the process ends.
- Redact before persist, not after.
- Prefer the tool-native credential store over Fenrix storage.
- Never place secrets in Git remote URLs — use the credential helper.
- Offer a "clear clipboard" option after copying sensitive values.

## Managed key material (SSH / EC2 key pairs)

Private keys are Fenrix-specific secrets, so they use DPAPI (6) and follow the same rules: stored
**encrypted at rest** outside the project folder (`Data\keys\<projectId>\`), with only a `KeyPair`
record + `SecretReference` in the DB. Per-project key-pair import, Terraform-backed generation, and use
are specified in [28-key-pair-management.md](28-key-pair-management.md).

See [15-logging-auditing.md](15-logging-auditing.md) for what is logged and [17-testing-strategy.md](17-testing-strategy.md) for redaction and leakage tests.
