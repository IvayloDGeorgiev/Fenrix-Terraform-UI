using Fenrix.IaCStudio.Application.Editor;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Editor;

/// <summary>
/// Formats an editor buffer with <c>terraform fmt -</c> (stdin → stdout) through the shared process runner and
/// command catalog — the same argument list the preview shows, never a shell string. The buffer is piped to
/// stdin, so it never enters the argument list, the redacted history, or a run log (the run is recorded with
/// <c>captureLog:false</c>). The formatted result is returned for the caller to swap into the buffer; the
/// on-disk save still goes through the atomic-write + file-history path. See docs/05-terraform-engine.md,
/// docs/13-ui-design.md, docs/23-command-transparency.md.
/// </summary>
public interface IEditorFormatService
{
    /// <summary>Builds the redacted command preview for the "Beautify" action (or the reason it can't run).</summary>
    Task<EditorFormatPreview> PreviewAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>
    /// Runs <c>terraform fmt -</c> over <paramref name="buffer"/> and returns the canonically-formatted text.
    /// Records a redacted history row like any other Terraform command; the buffer itself is never logged.
    /// </summary>
    Task<EditorFormatResult> FormatAsync(
        Guid projectId, Guid environmentId, string buffer,
        IProgress<ProcessOutputEvent>? output = null, CancellationToken ct = default);
}
