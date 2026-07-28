namespace Fenrix.IaCStudio.Contracts.Terraform;

// ---- State browser (terraform show -json of current state, redacted, parsed in memory) ----

/// <summary>
/// One top-level attribute of a state resource instance, already redacted: sensitive values (flagged by
/// the state JSON <c>sensitive_values</c> map) are reduced to a placeholder. See docs/06-plan-apply-safety.md,
/// docs/11-secrets.md.
/// </summary>
public sealed record StateAttribute(string Name, string? Value, bool Sensitive);

/// <summary>
/// A single resource instance in current state, redacted for display. <see cref="Attributes"/> is the
/// flattened top-level attribute set. See docs/22-terraform-files-model.md.
/// </summary>
public sealed record StateResourceInstance(
    string Address,
    string? ModuleAddress,
    ResourceMode Mode,
    string Type,
    string Name,
    string ProviderName,
    int? IndexKeyOrdinal,
    IReadOnlyList<StateAttribute> Attributes);

/// <summary>
/// The parsed, redacted current state: managed + data resource instances and the state's serial/lineage
/// metadata. Produced from <c>terraform show -json</c> entirely in memory — raw JSON is never persisted or
/// logged (it can contain plaintext secrets). See docs/06-plan-apply-safety.md, docs/11-secrets.md,
/// docs/22-terraform-files-model.md.
/// </summary>
public sealed record StateSnapshot(
    string? FormatVersion,
    string? TerraformVersion,
    long? Serial,
    string? Lineage,
    IReadOnlyList<StateResourceInstance> Resources)
{
    public bool IsEmpty => Resources.Count == 0;

    public int ManagedCount => Resources.Count(r => r.Mode == ResourceMode.Managed);
    public int DataCount => Resources.Count(r => r.Mode == ResourceMode.Data);

    public static readonly StateSnapshot Empty = new(null, null, null, null, []);
}

// ---- Outputs (terraform output -json, sensitive redacted) ----

/// <summary>
/// A single root output value, redacted: a sensitive output's value is reduced to a placeholder. See
/// docs/06-plan-apply-safety.md, docs/11-secrets.md.
/// </summary>
public sealed record TerraformOutput(string Name, string TypeLabel, string? Value, bool Sensitive);

/// <summary>The parsed, redacted set of root outputs from <c>terraform output -json</c>.</summary>
public sealed record OutputCollection(IReadOnlyList<TerraformOutput> Outputs)
{
    public bool IsEmpty => Outputs.Count == 0;
    public int SensitiveCount => Outputs.Count(o => o.Sensitive);

    public static readonly OutputCollection Empty = new([]);
}

// ---- Dependency graph (terraform graph -> DOT -> parsed for rendering) ----

/// <summary>The kind of node in a Terraform dependency graph, inferred from its label.</summary>
public enum GraphNodeKind
{
    Resource = 0,
    DataSource = 1,
    Variable = 2,
    Output = 3,
    Local = 4,
    Provider = 5,
    Module = 6,
    Other = 7
}

/// <summary>A node in the dependency graph. <see cref="Id"/> is the DOT node id; <see cref="Label"/> is display text.</summary>
public sealed record GraphNode(string Id, string Label, GraphNodeKind Kind);

/// <summary>A directed dependency edge (<see cref="From"/> depends on <see cref="To"/>), by node id.</summary>
public sealed record GraphEdge(string From, string To);

/// <summary>
/// A parsed Terraform dependency graph: nodes + directed edges, ready for the visual renderer. Parsed from
/// the DOT emitted by <c>terraform graph</c>. See docs/25-execution-lifecycle.md.
/// </summary>
public sealed record DependencyGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges)
{
    public bool IsEmpty => Nodes.Count == 0;

    public static readonly DependencyGraph Empty = new([], []);
}

// ---- Workspaces (terraform workspace list) ----

/// <summary>
/// The parsed workspace list: all workspace names and which one is current (the line marked <c>*</c>).
/// See docs/05-terraform-engine.md.
/// </summary>
public sealed record WorkspaceSnapshot(IReadOnlyList<string> Names, string? Current)
{
    public bool IsEmpty => Names.Count == 0;

    public static readonly WorkspaceSnapshot Empty = new([], null);
}

/// <summary>
/// A resolved, read-only inspection query: the exact command preview plus a block reason when it can't run.
/// Read-only inspection never takes the environment lock and is not blocked on a missing cloud connection
/// (it does not change infrastructure). See docs/25-execution-lifecycle.md "Read-only inspection".
/// </summary>
public sealed record InspectionContext(
    Guid ProjectId,
    Guid EnvironmentId,
    TerraformCommandKind Kind,
    string WorkingDirectory,
    TerraformRunSpec Spec,
    CommandPreview Preview,
    string? BlockReason)
{
    public bool CanRun => BlockReason is null;
}
