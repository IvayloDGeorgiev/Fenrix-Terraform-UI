namespace Fenrix.IaCStudio.Contracts.Providers;

/// <summary>Why a provider call failed, so the UI can give specific, actionable guidance.</summary>
public enum ProviderErrorKind
{
    None = 0,

    /// <summary>Missing/invalid/expired token, or the token lacks the required scope (HTTP 401/403).</summary>
    Authentication = 1,

    /// <summary>The repository/resource does not exist or is not visible to the token (HTTP 404).</summary>
    NotFound = 2,

    /// <summary>The provider rate-limited the request (HTTP 429 / rate-limit headers).</summary>
    RateLimited = 3,

    /// <summary>A network/DNS/TLS failure reaching the host (nothing was sent or no response).</summary>
    Network = 4,

    /// <summary>The provider rejected the request payload (HTTP 4xx other than the above).</summary>
    InvalidRequest = 5,

    /// <summary>A server-side error at the provider (HTTP 5xx).</summary>
    ServerError = 6,

    /// <summary>The provider has no adapter for this operation (Generic Git fallback).</summary>
    NotSupported = 7,

    /// <summary>Anything else.</summary>
    Unknown = 8
}

/// <summary>
/// The outcome of a repository-provider API call. Adapters never throw for expected failures (auth, 404,
/// rate-limit, network); they return a typed result so the UI can surface precise guidance — especially the
/// auth-failure guidance the credential UX depends on. See docs/09-provider-integrations.md, docs/16-error-handling.md.
/// </summary>
public sealed record ProviderResult<T>(
    bool Succeeded,
    T? Value,
    ProviderErrorKind ErrorKind,
    string? ErrorMessage)
{
    public static ProviderResult<T> Ok(T value) => new(true, value, ProviderErrorKind.None, null);

    public static ProviderResult<T> Fail(ProviderErrorKind kind, string message) =>
        new(false, default, kind, message);

    /// <summary>Human guidance for the failure, tuned per <see cref="ErrorKind"/>.</summary>
    public string? Guidance => ErrorKind switch
    {
        ProviderErrorKind.Authentication =>
            "Authentication failed. Check the connection's access token is present, unexpired, and has the required scopes, then test the connection again.",
        ProviderErrorKind.NotFound =>
            "Not found. The resource may not exist or the token may not have access to it.",
        ProviderErrorKind.RateLimited =>
            "The provider is rate-limiting requests. Wait a moment and retry.",
        ProviderErrorKind.Network =>
            "Could not reach the provider. Check your network connection and the host URL.",
        ProviderErrorKind.NotSupported =>
            "This provider has no adapter for this action. Core Git still works for this repository.",
        _ => ErrorMessage
    };
}
