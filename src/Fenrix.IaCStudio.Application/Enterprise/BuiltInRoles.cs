using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Enterprise;

/// <summary>
/// The four seeded default roles. Created once when enterprise mode is first enabled; marked
/// <see cref="OrgRole.IsBuiltIn"/> so they cannot be deleted (an admin may still adjust their
/// permissions). See docs/29-enterprise.md.
/// </summary>
public static class BuiltInRoles
{
    public const string ViewerName = "Viewer";
    public const string OperatorName = "Operator";
    public const string ApproverName = "Approver";
    public const string AdministratorName = "Administrator";

    /// <summary>Read-only visibility.</summary>
    public static readonly Permission Viewer = Permission.ViewProjects | Permission.ViewAudit;

    /// <summary>Day-to-day non-production operations (plan/apply/state), but not production apply/destroy.</summary>
    public static readonly Permission Operator =
        Permission.ViewProjects | Permission.RunPlan | Permission.RunApply
        | Permission.ManageState | Permission.ManageConnections;

    /// <summary>Can approve deployments (plus view), typically held by a lead/release manager.</summary>
    public static readonly Permission Approver =
        Permission.ViewProjects | Permission.ViewAudit | Permission.ApproveDeployment;

    /// <summary>Everything.</summary>
    public static readonly Permission Administrator = Permission.All;

    /// <summary>The seed set, in display order.</summary>
    public static IReadOnlyList<(string Name, string Description, Permission Permissions)> All { get; } =
    [
        (ViewerName, "Read-only access to projects and audit.", Viewer),
        (OperatorName, "Plan/apply/state on non-production environments; manage connections.", Operator),
        (ApproverName, "Approve deployments; view projects and audit.", Approver),
        (AdministratorName, "Full control, including roles, policy, and production.", Administrator)
    ];
}
