namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// Options for <c>terraform state mv SOURCE DESTINATION</c> — moves or renames a resource within state.
/// State-changing, so gated behind confirmation + the per-environment lock. See docs/05-terraform-engine.md,
/// docs/22-terraform-files-model.md.
/// </summary>
public sealed record StateMoveOptions
{
    /// <summary>The existing resource address (e.g. <c>aws_instance.old</c>).</summary>
    public string? Source { get; init; }

    /// <summary>The new resource address (e.g. <c>aws_instance.new</c>).</summary>
    public string? Destination { get; init; }
}

/// <summary>
/// Options for <c>terraform state rm ADDRESS…</c> — removes resources from state without destroying the
/// real objects. State-changing. See docs/05-terraform-engine.md.
/// </summary>
public sealed record StateRemoveOptions
{
    /// <summary>One or more resource addresses to forget from state.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];
}

/// <summary>
/// Options for <c>terraform force-unlock LOCK_ID</c> — releases a stuck state lock. Fenrix always passes
/// <c>-force</c> (the UI supplies its own typed confirmation instead of Terraform's interactive prompt).
/// See docs/05-terraform-engine.md.
/// </summary>
public sealed record ForceUnlockOptions
{
    /// <summary>The lock identifier reported by a failed operation.</summary>
    public string? LockId { get; init; }
}

/// <summary>
/// Options for the workspace verbs (<c>select</c>/<c>new</c>/<c>delete</c>). See docs/05-terraform-engine.md.
/// </summary>
public sealed record WorkspaceOptions
{
    /// <summary>The workspace name to select, create, or delete.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// Options for the import assistant. Two modes:
/// <list type="bullet">
/// <item>CLI import (<see cref="GenerateConfigOut"/> false): <c>terraform import ADDRESS ID</c> writes the
/// existing object into state directly.</item>
/// <item>Config-generation (<see cref="GenerateConfigOut"/> true, Terraform 1.5+): an <c>import{}</c> block is
/// written to config and <c>terraform plan -generate-config-out=&lt;file&gt;</c> scaffolds HCL for the resource.</item>
/// </list>
/// See docs/22-terraform-files-model.md.
/// </summary>
public sealed record ImportOptions
{
    /// <summary>The target resource address in configuration (e.g. <c>aws_instance.web</c>).</summary>
    public string? Address { get; init; }

    /// <summary>The provider-specific real-world identifier of the existing object (e.g. <c>i-0abc123</c>).</summary>
    public string? Id { get; init; }

    /// <summary>When true, use the Terraform 1.5+ <c>import{}</c> block + config-generation flow instead of CLI import.</summary>
    public bool GenerateConfigOut { get; init; }
}

/// <summary>
/// A previewed, ready-to-run state-changing operation: the exact request/preview, plus a block reason when
/// Fenrix refuses (no cloud connection, environment locked, missing input, …) and whether a typed
/// confirmation is required (always, for state operations). The preview and the request share one argument
/// list, so the command shown is exactly what runs. See docs/06-plan-apply-safety.md, docs/23-command-transparency.md.
/// </summary>
public sealed record StateOpContext(
    Guid ProjectId,
    Guid EnvironmentId,
    TerraformCommandKind Kind,
    string OperationLabel,
    string WorkingDirectory,
    Guid? CloudConnectionId,
    TerraformRunSpec Spec,
    CommandPreview Preview,
    string ConfirmationPhrase,
    string? BlockReason)
{
    /// <summary>True when the operation may be executed (no block reason).</summary>
    public bool CanRun => BlockReason is null;
}

/// <summary>The outcome of a state-changing operation. See docs/25-execution-lifecycle.md.</summary>
public sealed record StateOpResult(
    Guid RunId,
    ProcessResult Process,
    string? BlockReason)
{
    public bool Succeeded => BlockReason is null && Process.Succeeded;

    public static StateOpResult Blocked(string reason) =>
        new(Guid.Empty, ProcessResult.NotRun, reason);
}

/// <summary>
/// The outcome of an import. For CLI import, <see cref="Process"/> reflects <c>terraform import</c>. For the
/// config-generation flow, <see cref="GeneratedConfigPath"/> points at the scaffolded HCL and
/// <see cref="GeneratedConfig"/> holds its (non-sensitive) contents for review. See docs/22-terraform-files-model.md.
/// </summary>
public sealed record ImportResult(
    Guid RunId,
    ProcessResult Process,
    bool GeneratedConfigMode,
    string? GeneratedConfigPath,
    string? GeneratedConfig,
    string? BlockReason)
{
    public bool Succeeded => BlockReason is null && Process.Succeeded;

    public static ImportResult Blocked(string reason) =>
        new(Guid.Empty, ProcessResult.NotRun, false, null, null, reason);
}
