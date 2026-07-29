using Fenrix.IaCStudio.Application.Authoring;

namespace Fenrix.IaCStudio.Application.Abstractions.Authoring;

/// <summary>
/// Persists visual-builder output as real <c>.tf</c> files through the atomic-write + file-history path
/// (ADR-0002: files are the source of truth). Generation itself is pure (ConfigHclBuilder / HclEmitter); this
/// service is the thin filesystem seam: list target files, append a generated block, load a file's outline for
/// round-trip editing, and apply in-place literal edits that preserve unsupported HCL. See docs/07-visual-builder.md.
/// </summary>
public interface IConfigAuthoringService
{
    /// <summary>Lists existing <c>.tf</c> files under the environment's working directory (root-relative paths).</summary>
    Task<IReadOnlyList<string>> ListConfigFilesAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>
    /// Appends <paramref name="hcl"/> to <paramref name="relativePath"/> (creating the file if needed), separated
    /// from existing content by a blank line. Writes atomically and records a recovery version.
    /// </summary>
    Task<AuthoringWriteResult> AppendAsync(Guid projectId, string relativePath, string hcl, CancellationToken ct = default);

    /// <summary>Loads a file's content + located top-level blocks for the round-trip editor, or null if absent.</summary>
    Task<AuthoringFile?> ReadFileAsync(Guid projectId, string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Applies in-place literal value edits (span replacements) to a file and writes it atomically. Everything
    /// outside the edited spans is preserved exactly.
    /// </summary>
    Task<AuthoringWriteResult> ApplyLiteralEditsAsync(
        Guid projectId, string relativePath, IReadOnlyList<LiteralEdit> edits, CancellationToken ct = default);
}
