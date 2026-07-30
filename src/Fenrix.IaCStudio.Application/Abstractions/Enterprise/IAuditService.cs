using Fenrix.IaCStudio.Contracts.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// Records and reads the central audit trail. <see cref="WriteAsync"/> stamps the current user and persists a
/// redacted row (summaries/identifiers only — never secrets or raw output); it is best-effort and never throws
/// or blocks the underlying action. Reading requires <see cref="Domain.Enterprise.Permission.ViewAudit"/>.
/// See docs/15-logging-auditing.md, docs/29-enterprise.md.
/// </summary>
public interface IAuditService
{
    /// <summary>Persists an audit event for the current user. Best-effort; swallows its own failures.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Reads a filtered, paged page of the audit trail (newest first).</summary>
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
