using Fenrix.IaCStudio.Domain.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Locates Terraform binaries and reads their versions. Resolution order: the configured executable
/// (Settings <c>terraform.executable</c>) first, then the system <c>PATH</c>. See docs/05-terraform-engine.md.
/// </summary>
public interface ITerraformDiscovery
{
    /// <summary>
    /// Resolves the Terraform installation Fenrix should use for the given project, honouring a
    /// configured path override before falling back to <c>PATH</c>. Returns <c>null</c> when no binary
    /// can be found.
    /// </summary>
    Task<TerraformInstallation?> ResolveAsync(Guid? projectId = null, CancellationToken ct = default);

    /// <summary>Reads the version of a specific executable by running <c>terraform version -json</c>.</summary>
    Task<TerraformInstallation?> ProbeAsync(string executablePath, TerraformExecutableSource source, CancellationToken ct = default);

    /// <summary>Lists every distinct Terraform binary discoverable on the machine (configured + PATH).</summary>
    Task<IReadOnlyList<TerraformInstallation>> DiscoverAllAsync(CancellationToken ct = default);
}
