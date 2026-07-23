using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Domain.Security;

/// <summary>
/// A pointer to a secret in secure OS/tool storage. Fenrix stores the reference,
/// never the value. See docs/11-secrets.md.
/// </summary>
public sealed class SecretReference
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public SecretProvider Provider { get; set; } = SecretProvider.WindowsCredentialManager;
    public string ReferenceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
