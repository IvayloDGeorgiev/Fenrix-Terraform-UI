using Fenrix.IaCStudio.Contracts.Deployments;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Deployments;

/// <summary>
/// Pure builder for the versions × environments matrix. An environment's <em>current</em> version is the one
/// deployed by its latest <see cref="DeploymentStatus.Succeeded"/> deployment (by completion/start time). A
/// cell is <see cref="MatrixCellState.Current"/> for that version, <see cref="MatrixCellState.Previous"/> for
/// any other version that has a Succeeded deployment there, and <see cref="MatrixCellState.Available"/>
/// otherwise. Rows are ordered newest-first (created time, then semver precedence as a tiebreak). No IO —
/// verified by a reference port. See docs/20-pipelines-deployments.md.
/// </summary>
public static class VersionMatrixBuilder
{
    public static VersionMatrix Build(
        Guid projectId,
        IReadOnlyList<MatrixEnvironment> environments,
        IReadOnlyList<ProjectVersionSummary> versions,
        IReadOnlyList<DeploymentSummary> deployments)
    {
        var envs = environments.OrderBy(e => e.Order).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Latest successful deployment per environment → its version = the environment's current version.
        var currentByEnv = new Dictionary<Guid, Guid>();
        foreach (var env in envs)
        {
            var latest = deployments
                .Where(d => d.EnvironmentId == env.EnvironmentId && d.Status == DeploymentStatus.Succeeded)
                .OrderByDescending(d => d.CompletedAt ?? d.StartedAt)
                .FirstOrDefault();
            if (latest is not null)
                currentByEnv[env.EnvironmentId] = latest.ProjectVersionId;
        }

        // (version, env) → most recent Succeeded deployment time (drives Previous + LastDeployedAt).
        var succeededAt = new Dictionary<(Guid Version, Guid Env), DateTimeOffset>();
        foreach (var d in deployments.Where(d => d.Status == DeploymentStatus.Succeeded))
        {
            var key = (d.ProjectVersionId, d.EnvironmentId);
            var when = d.CompletedAt ?? d.StartedAt;
            if (!succeededAt.TryGetValue(key, out var existing) || when > existing)
                succeededAt[key] = when;
        }

        var orderedVersions = versions
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => SemVerLabel.Parse(v.Label))
            .ToList();

        var rows = new List<MatrixRow>(orderedVersions.Count);
        foreach (var v in orderedVersions)
        {
            var cells = new List<MatrixCell>(envs.Count);
            foreach (var env in envs)
            {
                var deployedHere = succeededAt.TryGetValue((v.Id, env.EnvironmentId), out var when);
                var isCurrent = currentByEnv.TryGetValue(env.EnvironmentId, out var curVersion) && curVersion == v.Id;

                var state = isCurrent ? MatrixCellState.Current
                    : deployedHere ? MatrixCellState.Previous
                    : MatrixCellState.Available;

                cells.Add(new MatrixCell(env.EnvironmentId, state, deployedHere ? when : null));
            }
            rows.Add(new MatrixRow(v, cells));
        }

        return new VersionMatrix(projectId, envs, rows);
    }
}
