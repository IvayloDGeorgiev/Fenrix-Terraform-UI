using System.Security.Cryptography;
using Fenrix.IaCStudio.Domain.Security;

namespace Fenrix.IaCStudio.Application.Security;

/// <summary>The format detected for a private key plus everything derivable about its public half.</summary>
public sealed record KeyMaterialInfo(KeyMaterialFormat Format, SshPublicKeyInfo Public);

/// <summary>
/// Inspects private-key file text, detects its encoding (PEM / OpenSSH / PPK), and derives the public key,
/// fingerprint, algorithm and size — <em>without decrypting the private half</em>. PEM RSA/ECDSA are derived
/// with <see cref="System.Security.Cryptography"/>; OpenSSH and PPK carry the public key in cleartext so it is
/// read directly. Ed25519-from-bare-PEM and encrypted keys degrade gracefully (stored, public shown when it
/// can be read). See docs/28-key-pair-management.md, docs/11-secrets.md.
/// </summary>
public static class SshPublicKeyReader
{
    public static KeyMaterialFormat DetectFormat(string text)
    {
        if (PpkParser.LooksLikePpk(text)) return KeyMaterialFormat.Ppk;
        if (OpenSshPrivateKeyReader.LooksLikeOpenSsh(text)) return KeyMaterialFormat.OpenSsh;
        if (text.Contains("PRIVATE KEY-----", StringComparison.Ordinal)) return KeyMaterialFormat.Pem;
        return KeyMaterialFormat.Unknown;
    }

    public static KeyMaterialInfo Read(string text)
    {
        var format = DetectFormat(text);
        var info = format switch
        {
            KeyMaterialFormat.Ppk => ReadPpk(text),
            KeyMaterialFormat.OpenSsh => ReadOpenSsh(text),
            KeyMaterialFormat.Pem => ReadPem(text),
            _ => Empty
        };
        return new KeyMaterialInfo(format, info);
    }

    private static readonly SshPublicKeyInfo Empty = new(KeyAlgorithm.Unknown, null, null, null, null);

    private static SshPublicKeyInfo ReadPpk(string text)
    {
        var ppk = PpkParser.Parse(text);
        return ppk.PublicBlob.Length == 0 ? Empty : SshPublicKey.FromBlob(ppk.PublicBlob, ppk.Comment);
    }

    private static SshPublicKeyInfo ReadOpenSsh(string text)
    {
        var blob = OpenSshPrivateKeyReader.TryReadPublicBlob(text);
        return blob is null ? Empty : SshPublicKey.FromBlob(blob, null);
    }

    private static SshPublicKeyInfo ReadPem(string text)
    {
        // Try RSA, then ECDSA. Encrypted or Ed25519 PEM will throw → degrade to header-based algorithm only.
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(text);
            var p = rsa.ExportParameters(false);
            var blob = SshPublicKey.BuildRsaBlob(p.Modulus!, p.Exponent!);
            return SshPublicKey.FromBlob(blob, null);
        }
        catch (CryptographicException) { }
        catch (ArgumentException) { }

        try
        {
            using var ec = ECDsa.Create();
            ec.ImportFromPem(text);
            var p = ec.ExportParameters(false);
            var curve = CurveName(ec.KeySize);
            if (curve is not null && p.Q.X is not null && p.Q.Y is not null)
            {
                var blob = SshPublicKey.BuildEcdsaBlob(curve, p.Q.X, p.Q.Y);
                return SshPublicKey.FromBlob(blob, null);
            }
        }
        catch (CryptographicException) { }
        catch (ArgumentException) { }

        return new SshPublicKeyInfo(AlgorithmFromHeader(text), null, null, null, null);
    }

    private static string? CurveName(int keySize) => keySize switch
    {
        256 => "nistp256",
        384 => "nistp384",
        521 => "nistp521",
        _ => null
    };

    private static KeyAlgorithm AlgorithmFromHeader(string text)
    {
        if (text.Contains("BEGIN RSA", StringComparison.Ordinal)) return KeyAlgorithm.Rsa;
        if (text.Contains("BEGIN EC", StringComparison.Ordinal)) return KeyAlgorithm.Ecdsa;
        return KeyAlgorithm.Unknown;
    }
}
