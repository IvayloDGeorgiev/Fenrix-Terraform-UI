using Fenrix.IaCStudio.Contracts.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// The bootstrap enterprise configuration, resolved once at startup from <c>enterprise.json</c> in the
/// data root (see docs/29-enterprise.md). Exposes whether governance is enabled and which metadata
/// provider is active. The connection string is never exposed here — only whether it resolved.
/// </summary>
public interface IEnterpriseConfig
{
    /// <summary>True when governance (RBAC/policy/central audit) is active. False ⇒ single-user local posture.</summary>
    bool IsEnabled { get; }

    /// <summary>"Sqlite" (default) or "SqlServer".</summary>
    string MetadataProvider { get; }

    /// <summary>Display name of the organisation, if configured.</summary>
    string? Organisation { get; }

    /// <summary>Read-only status for Settings → Enterprise.</summary>
    EnterpriseStatus Status { get; }
}
