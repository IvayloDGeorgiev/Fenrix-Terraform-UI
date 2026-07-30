using Fenrix.IaCStudio.Contracts.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// Resolves the current user for auditing and authorisation. A narrow seam so the OS-user default
/// (<c>WindowsUserContext</c>) can be replaced by a verified sign-in (Entra/OIDC) later without
/// touching call sites. Replaces the inlined <c>Environment.UserName</c>. See docs/29-enterprise.md, ADR-0006.
/// </summary>
public interface IUserContext
{
    /// <summary>The current user. Never null; falls back to <see cref="CurrentUser.Unknown"/> if unresolved.</summary>
    CurrentUser Current { get; }
}
