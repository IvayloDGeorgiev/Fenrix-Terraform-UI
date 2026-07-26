using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Security;

namespace Fenrix.IaCStudio.Application.Abstractions.Security;

/// <summary>
/// Reads and writes secret <em>values</em> to secure OS storage (Windows Credential Manager / DPAPI). Fenrix
/// persists only a <see cref="SecretReference"/> in its database — never the value — and resolves the value
/// through this store just-in-time at execution time, discarding it afterwards. The concrete implementation
/// dispatches by <see cref="SecretReference.Provider"/> to the matching backend. See docs/11-secrets.md.
/// </summary>
public interface ISecretStore
{
    /// <summary>Whether this machine/OS can back the given provider (e.g. Credential Manager on Windows).</summary>
    bool IsSupported(SecretProvider provider);

    /// <summary>Writes (or overwrites) the secret value under the reference's key in secure storage.</summary>
    Task StoreAsync(SecretReference reference, string secretValue, CancellationToken ct = default);

    /// <summary>Reads the secret value for a reference, or null if it is not present.</summary>
    Task<string?> RetrieveAsync(SecretReference reference, CancellationToken ct = default);

    /// <summary>Removes the secret value for a reference from secure storage (idempotent).</summary>
    Task DeleteAsync(SecretReference reference, CancellationToken ct = default);
}
