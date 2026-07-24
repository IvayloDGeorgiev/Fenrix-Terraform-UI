using System.Security.Cryptography;

namespace Fenrix.IaCStudio.Application.Files;

/// <summary>SHA-256 content hashing used as the dedup key for the version store.</summary>
public static class FileHashing
{
    /// <summary>Lowercase hex SHA-256 of the given bytes.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> content)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(content, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Lowercase hex SHA-256 of a file on disk.</summary>
    public static async Task<string> Sha256HexAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }
}
