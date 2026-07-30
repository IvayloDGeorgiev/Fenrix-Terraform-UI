using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Contracts.Enterprise;

/// <summary>A request to record an audit event (identity is filled in by the sink from the current user).</summary>
public sealed record AuditEntry(
    AuditAction Action,
    AuditOutcome Outcome = AuditOutcome.Allowed,
    Guid? ProjectId = null,
    string? ProjectName = null,
    Guid? EnvironmentId = null,
    string? EnvironmentName = null,
    string? Target = null,
    string? Detail = null);

/// <summary>An audit row as shown in the viewer.</summary>
public sealed record AuditEventSummary(
    Guid Id,
    AuditAction Action,
    AuditOutcome Outcome,
    string UserDisplayName,
    string UserKey,
    Guid? ProjectId,
    string? ProjectName,
    Guid? EnvironmentId,
    string? EnvironmentName,
    string? Target,
    string? Detail,
    DateTimeOffset OccurredAt);

/// <summary>Filter for the paged audit viewer. Null fields are unconstrained.</summary>
public sealed record AuditQuery(
    string? UserKey = null,
    Guid? ProjectId = null,
    AuditAction? Action = null,
    AuditOutcome? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Skip = 0,
    int Take = 100);

/// <summary>A page of audit rows plus the total match count (for paging).</summary>
public sealed record AuditPage(IReadOnlyList<AuditEventSummary> Items, int TotalCount);
