using System.Runtime.Versioning;
using System.Security.Principal;
using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// Resolves the current user from the Windows OS identity: the SID is the stable <see cref="CurrentUser.UserKey"/>
/// and the account name is the display name (with the UPN as email when domain-joined). Resolved once and
/// cached — the OS user does not change during a session. Off Windows (or on failure) it degrades to the
/// environment user name so nothing breaks. A future Entra/OIDC context replaces this behind
/// <see cref="IUserContext"/> without touching callers. See docs/29-enterprise.md, ADR-0006.
/// </summary>
public sealed class WindowsUserContext : IUserContext
{
    private readonly CurrentUser _current;

    public WindowsUserContext() => _current = Resolve();

    public CurrentUser Current => _current;

    private static CurrentUser Resolve()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return ResolveWindows();
            }
            catch
            {
                // Fall through to the environment-name fallback below.
            }
        }

        var name = SafeEnvironmentUserName();
        return new CurrentUser(UserKey: $"env:{name}", DisplayName: name, Email: null, IsAuthenticated: false);
    }

    [SupportedOSPlatform("windows")]
    private static CurrentUser ResolveWindows()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var key = identity.User?.Value ?? $"env:{SafeEnvironmentUserName()}";
        var name = string.IsNullOrWhiteSpace(identity.Name) ? SafeEnvironmentUserName() : identity.Name;

        // UPN is a good "email"-shaped identifier when domain-joined; not always present.
        string? upn = null;
        try
        {
            upn = identity.Claims
                .FirstOrDefault(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")
                ?.Value;
        }
        catch { /* claims may be unavailable in some contexts */ }

        return new CurrentUser(key, name, upn, IsAuthenticated: identity.IsAuthenticated);
    }

    private static string SafeEnvironmentUserName()
    {
        try { return string.IsNullOrWhiteSpace(Environment.UserName) ? "unknown" : Environment.UserName; }
        catch { return "unknown"; }
    }
}
