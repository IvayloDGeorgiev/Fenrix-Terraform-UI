using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Application.Abstractions.Projects;

/// <summary>
/// Scans an existing folder for the import wizard: Terraform files, Git repo, likely environment
/// directories, providers, backend config, and a required Terraform version. Read-only — never
/// modifies the folder. See docs/03-domain-model.md.
/// </summary>
public interface IProjectImportScanner
{
    Task<ImportScanResult> ScanAsync(string folderPath, CancellationToken ct = default);
}
