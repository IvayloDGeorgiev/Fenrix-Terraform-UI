namespace Fenrix.IaCStudio.Contracts.Deployments;

/// <summary>The deploy state of a version in a given environment cell of the matrix.</summary>
public enum MatrixCellState
{
    /// <summary>This version is the environment's current (latest Succeeded) deployment.</summary>
    Current = 0,

    /// <summary>This version was deployed here before but is not the current one.</summary>
    Previous = 1,

    /// <summary>This version has never been deployed here; it is available to deploy.</summary>
    Available = 2
}

/// <summary>One environment column header of the version matrix.</summary>
public sealed record MatrixEnvironment(
    Guid EnvironmentId,
    string Name,
    bool IsProduction,
    int Order,
    bool HasCloudConnection);

/// <summary>One cell: a (version, environment) intersection with its deploy state and last deployment time.</summary>
public sealed record MatrixCell(
    Guid EnvironmentId,
    MatrixCellState State,
    DateTimeOffset? LastDeployedAt);

/// <summary>One version row of the matrix, with a cell per environment column (same order as the headers).</summary>
public sealed record MatrixRow(
    ProjectVersionSummary Version,
    IReadOnlyList<MatrixCell> Cells);

/// <summary>
/// The versions (rows) × environments (columns) grid. Makes the "v1 on Live / v1.5 on UAT / v2 on Dev"
/// picture immediate and drives the "deploy this version to one / several / all" action.
/// See docs/20-pipelines-deployments.md.
/// </summary>
public sealed record VersionMatrix(
    Guid ProjectId,
    IReadOnlyList<MatrixEnvironment> Environments,
    IReadOnlyList<MatrixRow> Rows);
