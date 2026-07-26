using Fenrix.IaCStudio.Application.Abstractions.Security;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Security;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Security;

/// <summary>
/// The <see cref="ISecretStore"/> facade that dispatches by <see cref="SecretReference.Provider"/> to the
/// matching backend. Phase 7 backs Fenrix-specific secrets (repository host tokens) with the Windows
/// Credential Manager; <see cref="SecretProvider.GitCredentialManager"/> resolves against the same store.
/// The tool-native cloud stores (Azure CLI/AWS/GCP) and DPAPI-backed key material arrive with Phase 8/8.5.
/// Fenrix persists only a <see cref="SecretReference"/> — never a value. See docs/11-secrets.md.
/// </summary>
public sealed class SecretStore(
    WindowsCredentialManagerStore windows,
    ILogger<SecretStore> logger) : ISecretStore
{
    private readonly WindowsCredentialManagerStore _windows = windows;
    private readonly ILogger<SecretStore> _logger = logger;

    public bool IsSupported(SecretProvider provider) => provider switch
    {
        SecretProvider.WindowsCredentialManager => WindowsCredentialManagerStore.IsSupported,
        SecretProvider.GitCredentialManager => WindowsCredentialManagerStore.IsSupported,
        _ => false
    };

    public Task StoreAsync(SecretReference reference, string secretValue, CancellationToken ct = default)
    {
        EnsureSupported(reference.Provider);
        _windows.Store(reference, secretValue);
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(SecretReference reference, CancellationToken ct = default)
    {
        if (!IsSupported(reference.Provider))
        {
            _logger.LogWarning("No secret backend for provider {Provider}; returning null.", reference.Provider);
            return Task.FromResult<string?>(null);
        }
        return Task.FromResult(_windows.Retrieve(reference));
    }

    public Task DeleteAsync(SecretReference reference, CancellationToken ct = default)
    {
        if (IsSupported(reference.Provider))
            _windows.Delete(reference);
        return Task.CompletedTask;
    }

    private void EnsureSupported(SecretProvider provider)
    {
        if (!IsSupported(provider))
            throw new PlatformNotSupportedException(
                $"Secret provider '{provider}' is not available on this machine. " +
                "Phase 7 stores repository tokens in the Windows Credential Manager.");
    }
}
