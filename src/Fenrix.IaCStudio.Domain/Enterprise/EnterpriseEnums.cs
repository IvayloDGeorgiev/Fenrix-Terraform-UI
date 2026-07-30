namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// Fine-grained, org-assignable capabilities. A <see cref="OrgRole"/> is a bundle of these.
/// Flags so a role's permissions are a single stored value and unions are cheap. See docs/29-enterprise.md.
/// </summary>
[Flags]
public enum Permission
{
    None = 0,
    ViewProjects = 1 << 0,
    RunPlan = 1 << 1,
    RunApply = 1 << 2,
    RunApplyProduction = 1 << 3,
    RunDestroy = 1 << 4,
    ManageState = 1 << 5,
    ForceUnlock = 1 << 6,
    ManageConnections = 1 << 7,
    ExportPrivateKey = 1 << 8,
    ApproveDeployment = 1 << 9,
    ManageTemplates = 1 << 10,
    ManagePolicy = 1 << 11,
    ManageRoles = 1 << 12,
    ViewAudit = 1 << 13,

    /// <summary>Everything — the Administrator role.</summary>
    All = ViewProjects | RunPlan | RunApply | RunApplyProduction | RunDestroy | ManageState
        | ForceUnlock | ManageConnections | ExportPrivateKey | ApproveDeployment | ManageTemplates
        | ManagePolicy | ManageRoles | ViewAudit
}

/// <summary>
/// The level a <see cref="RoleAssignment"/> applies at. Resolved most-specific-first
/// (Environment beats Project beats Global), mirroring settings resolution.
/// </summary>
public enum AccessScopeLevel
{
    Global = 0,
    Project = 1,
    Environment = 2
}

/// <summary>A safety-relevant, audited action. Names match docs/15-logging-auditing.md + Phase 11 additions.</summary>
public enum AuditAction
{
    ProjectCreated = 0,
    ProjectImported = 1,
    EnvironmentCreated = 2,
    ConnectionChanged = 3,
    ApplyStarted = 4,
    ApplyCompleted = 5,
    DestroyAttempted = 6,
    StateChanged = 7,
    ForceUnlockPerformed = 8,
    ForcePushPerformed = 9,
    SettingsChanged = 10,
    RoleChanged = 11,
    PolicyChanged = 12,
    TemplateApplied = 13,
    ApprovalRequested = 14,
    ApprovalDecided = 15,
    PrivateKeyExported = 16,
    AuthorizationDenied = 17
}

/// <summary>Whether an audited action was permitted or blocked.</summary>
public enum AuditOutcome
{
    Allowed = 0,
    Blocked = 1,
    Failed = 2
}

/// <summary>Lifecycle of a role-gated approval request. See docs/29-enterprise.md.</summary>
public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
    Expired = 4
}

/// <summary>The value type of a template parameter (drives input rendering + HCL emission).</summary>
public enum TemplateParameterType
{
    String = 0,
    Number = 1,
    Bool = 2,
    Expression = 3
}

/// <summary>Where a governed run executes. Phase 11 only ever produces <see cref="Local"/>. See ADR-0007.</summary>
public enum ExecutionLocation
{
    Local = 0,
    Agent = 1
}
