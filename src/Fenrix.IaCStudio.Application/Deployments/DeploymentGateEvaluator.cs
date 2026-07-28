using Fenrix.IaCStudio.Contracts.Deployments;

namespace Fenrix.IaCStudio.Application.Deployments;

/// <summary>
/// Pure evaluation of a stage's non-interactive gates for a governed deploy. The interactive gates — approval
/// acknowledgement and production typed-confirmation — are not evaluated here (they are satisfied by the user
/// at execute time); this returns whether each is <em>required</em>. Everything else (cloud bound, repository
/// at the version commit, required branch, clean working tree, previous stage succeeded) is a blocker that is
/// checked from the supplied facts. No IO — verified by a reference port. See docs/20-pipelines-deployments.md.
/// </summary>
public static class DeploymentGateEvaluator
{
    /// <summary>The facts a stage's gates are evaluated against (all gathered by the caller, so this stays pure).</summary>
    public readonly record struct GateInputs(
        bool HasCloudConnection,
        bool IsProduction,
        string VersionCommit,
        string? CurrentCommit,
        string? CurrentBranch,
        bool WorkingTreeDirty,
        // Stage rules
        bool RequireApproval,
        bool RequirePreviousStageSuccess,
        bool RequireCleanWorkingTree,
        bool RequireTypedConfirmationForProduction,
        string? RequiredBranch,
        // Previous-stage state: null when there is no upstream stage (first stage always passes this gate).
        bool? PreviousStageHasVersion);

    public sealed record Result(
        IReadOnlyList<DeployGate> Gates,
        bool RequiresApproval,
        bool RequiresTypedConfirmation);

    public static Result Evaluate(GateInputs i)
    {
        var gates = new List<DeployGate>();

        // Always-on: a bound cloud connection is required to apply (Phase 8 authentication-required rule).
        gates.Add(new DeployGate(
            DeployGateKind.CloudConnection,
            "Environment has a bound cloud connection",
            i.HasCloudConnection,
            IsBlocker: true,
            i.HasCloudConnection ? null : "Bind a cloud connection to this environment before deploying."));

        // Always-on: the working tree should be at the version's commit so what deploys is what was cut.
        var atVersion = i.CurrentCommit is not null &&
                        string.Equals(i.CurrentCommit, i.VersionCommit, StringComparison.OrdinalIgnoreCase);
        gates.Add(new DeployGate(
            DeployGateKind.RepositoryAtVersion,
            "Repository is at the version's commit",
            atVersion,
            IsBlocker: true,
            atVersion ? null
                : i.CurrentCommit is null ? "The project is not a Git repository, or HEAD could not be read."
                : $"HEAD is at {Short(i.CurrentCommit)}, the version is {Short(i.VersionCommit)}. Check out the version first."));

        if (!string.IsNullOrWhiteSpace(i.RequiredBranch))
        {
            var branchOk = i.CurrentBranch is not null &&
                           string.Equals(i.CurrentBranch, i.RequiredBranch, StringComparison.Ordinal);
            gates.Add(new DeployGate(
                DeployGateKind.RequiredBranch,
                $"On required branch '{i.RequiredBranch}'",
                branchOk,
                IsBlocker: true,
                branchOk ? null : $"This stage only deploys '{i.RequiredBranch}' (currently on '{i.CurrentBranch ?? "?"}')."));
        }

        if (i.RequireCleanWorkingTree)
        {
            gates.Add(new DeployGate(
                DeployGateKind.CleanWorkingTree,
                "Working tree is clean",
                !i.WorkingTreeDirty,
                IsBlocker: true,
                i.WorkingTreeDirty ? "There are uncommitted changes in the working tree." : null));
        }

        if (i.RequirePreviousStageSuccess)
        {
            // First stage (no upstream) passes automatically; otherwise the upstream must hold this version.
            var passed = i.PreviousStageHasVersion ?? true;
            gates.Add(new DeployGate(
                DeployGateKind.PreviousStageSuccess,
                "Previous stage has this version deployed",
                passed,
                IsBlocker: true,
                passed ? null : "Deploy this version to the upstream stage first (promote in order)."));
        }

        var requiresTyped = i.IsProduction && i.RequireTypedConfirmationForProduction;
        return new Result(gates, i.RequireApproval, requiresTyped);
    }

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
}
