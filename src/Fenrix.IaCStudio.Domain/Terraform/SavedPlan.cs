namespace Fenrix.IaCStudio.Domain.Terraform;

/// <summary>
/// The persisted safety record for one saved plan: where the plan file lives, the integrity hashes that
/// gate apply, the redacted change counts, a snapshot of the target environment, and the apply outcome.
/// The raw plan JSON is never stored here — only these redacted summaries and hashes (ADR-0003,
/// docs/06-plan-apply-safety.md, docs/11-secrets.md).
///
/// A plan is <see cref="IsInvalidated"/> (and cannot be applied) once the configuration or provider-lock
/// hashes diverge from what produced it. Git provenance fields are captured from Phase 5 onward and are
/// null until then.
/// </summary>
public sealed class SavedPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }

    /// <summary>Snapshot of the environment name at plan time (environments can be renamed).</summary>
    public string EnvironmentName { get; set; } = string.Empty;

    public PlanMode Mode { get; init; } = PlanMode.Normal;

    /// <summary>The command-run history row for the <c>plan</c> invocation.</summary>
    public Guid? PlanCommandRunId { get; set; }

    /// <summary>Absolute path to the saved <c>.tfplan</c> file.</summary>
    public string PlanFilePath { get; set; } = string.Empty;

    /// <summary>Project-relative path to the plan file (for display and git tracking).</summary>
    public string? RelativePlanFilePath { get; set; }

    /// <summary>The working directory the plan was produced in (and must be applied from).</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Terraform version that produced the plan (informational + apply-time cross-check).</summary>
    public string? TerraformVersion { get; set; }

    // ---- integrity hashes (drive invalidation) ----

    /// <summary>Combined SHA-256 over the environment's configuration files at plan time.</summary>
    public string? ConfigHash { get; set; }

    /// <summary>SHA-256 of <c>.terraform.lock.hcl</c> at plan time (null when absent).</summary>
    public string? LockHash { get; set; }

    /// <summary>SHA-256 of the saved plan file itself (verifies it was not swapped/modified).</summary>
    public string? PlanFileHash { get; set; }

    // ---- redacted change counts ----

    public int AddCount { get; set; }
    public int ChangeCount { get; set; }
    public int DestroyCount { get; set; }
    public int ReplaceCount { get; set; }

    // ---- environment snapshot at plan time ----

    public bool IsProductionTarget { get; set; }

    /// <summary>The environment's bound cloud connection at plan time (verified unchanged before apply).</summary>
    public Guid? CloudConnectionId { get; set; }

    // ---- Git provenance (Phase 5; null until then) ----

    public string? GitCommitSha { get; set; }
    public string? GitBranch { get; set; }
    public bool? GitTreeDirty { get; set; }

    // ---- lifecycle ----

    public bool Applied { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public Guid? ApplyCommandRunId { get; set; }

    /// <summary>Set once the plan is detected stale (config/lock hash drift); it can no longer be applied.</summary>
    public bool IsInvalidated { get; set; }
    public string? InvalidatedReason { get; set; }

    public bool HasDeletions => DestroyCount > 0;
    public bool HasReplacements => ReplaceCount > 0;

    /// <summary>True when the plan is a still-valid, un-applied candidate for apply.</summary>
    public bool CanApply => !Applied && !IsInvalidated;
}
