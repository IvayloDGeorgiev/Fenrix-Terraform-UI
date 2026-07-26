using System.Text.Json;

namespace Fenrix.IaCStudio.Infrastructure.Security;

/// <summary>
/// The shape stored in the OS secret store for a repository connection: the access token plus an optional
/// username (needed by Basic-auth providers such as Bitbucket app passwords). Only this blob lives in the
/// secure store; the database holds just a <c>SecretReference</c> pointing at it. Kept together so a single
/// credential read yields everything an adapter needs. See docs/11-secrets.md.
/// </summary>
public sealed record RepositoryCredential(string Token, string? UserName)
{
    public string Serialize() => JsonSerializer.Serialize(this);

    /// <summary>
    /// Parses a stored blob. Falls back to treating the whole value as a bare token if it is not the
    /// expected JSON (forward/backward tolerance).
    /// </summary>
    public static RepositoryCredential Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new RepositoryCredential(string.Empty, null);

        if (raw.TrimStart().StartsWith('{'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<RepositoryCredential>(raw);
                if (parsed is not null && !string.IsNullOrEmpty(parsed.Token))
                    return parsed;
            }
            catch (JsonException)
            {
                // fall through to bare-token handling
            }
        }

        return new RepositoryCredential(raw, null);
    }
}
