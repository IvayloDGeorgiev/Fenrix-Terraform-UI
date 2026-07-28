using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fenrix.IaCStudio.Domain.Security;

namespace Fenrix.IaCStudio.Application.Security;

/// <summary>The non-secret facts derived from a key's public half.</summary>
public sealed record SshPublicKeyInfo(
    KeyAlgorithm Algorithm,
    int? Bits,
    string? OpenSshLine,
    string? Fingerprint,
    string? Comment);

/// <summary>
/// Helpers for the SSH public-key wire format (RFC 4253/4716 style): reading/writing SSH strings and mpints,
/// building the single-line OpenSSH representation, and computing the OpenSSH SHA-256 fingerprint. Pure and
/// offline — no external dependency. See docs/28-key-pair-management.md.
/// </summary>
public static class SshPublicKey
{
    /// <summary>
    /// Interprets a raw SSH public-key blob (the base64-decoded bytes that follow the key type on an OpenSSH
    /// public line) and returns the algorithm, bit size (RSA), the reconstructed OpenSSH line, and fingerprint.
    /// </summary>
    public static SshPublicKeyInfo FromBlob(byte[] blob, string? comment)
    {
        var typeName = ReadFirstString(blob);
        var algorithm = MapAlgorithm(typeName);
        var bits = typeName == "ssh-rsa" ? TryGetRsaBits(blob) : BitsForType(typeName);
        var line = BuildLine(typeName, blob, comment);
        var fingerprint = ComputeFingerprint(blob);
        return new SshPublicKeyInfo(algorithm, bits, line, fingerprint, string.IsNullOrWhiteSpace(comment) ? null : comment);
    }

    /// <summary>The OpenSSH SHA-256 fingerprint of a public-key blob: <c>SHA256:base64(sha256(blob))</c> (no padding).</summary>
    public static string ComputeFingerprint(byte[] blob)
    {
        var hash = SHA256.HashData(blob);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    /// <summary>Builds the single-line OpenSSH public key: <c>&lt;type&gt; &lt;base64(blob)&gt; [comment]</c>.</summary>
    public static string BuildLine(string typeName, byte[] blob, string? comment)
    {
        var sb = new StringBuilder();
        sb.Append(typeName).Append(' ').Append(Convert.ToBase64String(blob));
        if (!string.IsNullOrWhiteSpace(comment))
            sb.Append(' ').Append(comment.Trim());
        return sb.ToString();
    }

    /// <summary>Builds an RSA public-key blob from a modulus and exponent (big-endian, unsigned).</summary>
    public static byte[] BuildRsaBlob(byte[] modulus, byte[] exponent)
    {
        using var ms = new MemoryStream();
        WriteString(ms, "ssh-rsa");
        WriteMpint(ms, exponent);
        WriteMpint(ms, modulus);
        return ms.ToArray();
    }

    /// <summary>Builds an ECDSA public-key blob for a NIST curve from the affine point coordinates.</summary>
    public static byte[] BuildEcdsaBlob(string curveName, byte[] qx, byte[] qy)
    {
        using var ms = new MemoryStream();
        var typeName = "ecdsa-sha2-" + curveName;
        WriteString(ms, typeName);
        WriteString(ms, curveName);
        var point = new byte[1 + qx.Length + qy.Length];
        point[0] = 0x04; // uncompressed
        Buffer.BlockCopy(qx, 0, point, 1, qx.Length);
        Buffer.BlockCopy(qy, 0, point, 1 + qx.Length, qy.Length);
        WriteBytes(ms, point);
        return ms.ToArray();
    }

    // ---- SSH wire primitives ----

    /// <summary>Writes an SSH string: a big-endian uint32 length followed by the raw bytes.</summary>
    public static void WriteBytes(Stream s, byte[] value)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)value.Length);
        s.Write(len);
        s.Write(value, 0, value.Length);
    }

    public static void WriteString(Stream s, string value) => WriteBytes(s, Encoding.ASCII.GetBytes(value));

    /// <summary>Writes an SSH mpint: minimal big-endian two's-complement, with a leading 0x00 if the MSB is set.</summary>
    public static void WriteMpint(Stream s, byte[] magnitude)
    {
        var i = 0;
        while (i < magnitude.Length - 1 && magnitude[i] == 0) i++; // strip leading zeros
        var trimmed = magnitude[i..];
        if (trimmed.Length == 1 && trimmed[0] == 0)
        {
            WriteBytes(s, []); // zero → empty string
            return;
        }
        if ((trimmed[0] & 0x80) != 0)
        {
            var padded = new byte[trimmed.Length + 1];
            Buffer.BlockCopy(trimmed, 0, padded, 1, trimmed.Length);
            WriteBytes(s, padded);
        }
        else
        {
            WriteBytes(s, trimmed);
        }
    }

    /// <summary>Reads the first SSH string (the key type) from a blob; empty on malformed input.</summary>
    public static string ReadFirstString(byte[] blob)
    {
        if (blob.Length < 4) return string.Empty;
        var len = BinaryPrimitives.ReadUInt32BigEndian(blob);
        if (len == 0 || 4 + len > (uint)blob.Length) return string.Empty;
        return Encoding.ASCII.GetString(blob, 4, (int)len);
    }

    // ---- interpretation ----

    private static KeyAlgorithm MapAlgorithm(string typeName) => typeName switch
    {
        "ssh-rsa" => KeyAlgorithm.Rsa,
        "ssh-ed25519" => KeyAlgorithm.Ed25519,
        _ when typeName.StartsWith("ecdsa-sha2-", StringComparison.Ordinal) => KeyAlgorithm.Ecdsa,
        _ => KeyAlgorithm.Unknown
    };

    private static int? BitsForType(string typeName) => typeName switch
    {
        "ssh-ed25519" => 256,
        "ecdsa-sha2-nistp256" => 256,
        "ecdsa-sha2-nistp384" => 384,
        "ecdsa-sha2-nistp521" => 521,
        _ => null
    };

    /// <summary>Reads the RSA modulus (third SSH field) from an ssh-rsa blob and returns its bit length.</summary>
    private static int? TryGetRsaBits(byte[] blob)
    {
        try
        {
            var pos = 0;
            _ = ReadField(blob, ref pos); // "ssh-rsa"
            _ = ReadField(blob, ref pos); // e
            var n = ReadField(blob, ref pos); // modulus
            var i = 0;
            while (i < n.Length && n[i] == 0) i++; // skip sign/leading zeros
            var significant = n.Length - i;
            return significant > 0 ? significant * 8 : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ReadField(byte[] blob, ref int pos)
    {
        var len = (int)BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(pos));
        pos += 4;
        var slice = blob[pos..(pos + len)];
        pos += len;
        return slice;
    }
}
