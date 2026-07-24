namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// Options for <c>terraform init</c>. Fenrix always passes <c>-input=false</c> (no interactive prompts
/// from the UI path). See docs/05-terraform-engine.md and docs/25-execution-lifecycle.md.
/// </summary>
public sealed record InitOptions
{
    /// <summary>Upgrade modules and plugins to the latest allowed versions (<c>-upgrade</c>).</summary>
    public bool Upgrade { get; init; }

    /// <summary>Reconfigure the backend, ignoring any saved configuration (<c>-reconfigure</c>).</summary>
    public bool Reconfigure { get; init; }

    /// <summary>Skip backend initialization (<c>-backend=false</c>).</summary>
    public bool DisableBackend { get; init; }

    /// <summary>Optional backend configuration file (<c>-backend-config=&lt;file&gt;</c>), e.g. <c>backend.hcl</c>.</summary>
    public string? BackendConfigFile { get; init; }
}

/// <summary>
/// Options for <c>terraform fmt</c>. Defaults to a non-mutating check so the UI can preview
/// formatting differences before writing. See docs/05-terraform-engine.md.
/// </summary>
public sealed record FormatOptions
{
    /// <summary>Check only; do not write changes (<c>-check</c>). When false, files are rewritten in place.</summary>
    public bool CheckOnly { get; init; } = true;

    /// <summary>Show a diff of formatting changes (<c>-diff</c>).</summary>
    public bool ShowDiff { get; init; } = true;

    /// <summary>Also process files in nested directories (<c>-recursive</c>).</summary>
    public bool Recursive { get; init; } = true;
}

/// <summary>
/// Options for <c>terraform validate</c>. Fenrix requests <c>-json</c> so results can be shown as
/// structured diagnostics. See docs/05-terraform-engine.md.
/// </summary>
public sealed record ValidateOptions
{
    /// <summary>Emit machine-readable JSON (<c>-json</c>). Kept on by default for structured display.</summary>
    public bool Json { get; init; } = true;
}
