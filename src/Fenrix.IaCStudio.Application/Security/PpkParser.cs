using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace Fenrix.IaCStudio.Application.Security;

/// <summary>
/// A parsed PuTTY private-key file (<c>.ppk</c>, v2 or v3). Carries the non-secret public blob + metadata
/// always; the plaintext private section is available only for unencrypted files (used for best-effort
/// conversion to PEM).
/// </summary>
public sealed record PpkFile(
    int Version,
    string Algorithm,
    bool Encrypted,
    string? Comment,
    byte[] PublicBlob,
    byte[]? PrivatePlaintext);

/// <summary>
/// Minimal, dependency-free PuTTY <c>.ppk</c> reader. Extracts the public-key blob, algorithm and comment
/// (enough for the fingerprint + a reference), and — for unencrypted RSA keys — reconstructs a PEM so the key
/// is directly usable in a Terraform <c>connection</c> block. Encrypted PPKs and non-RSA conversion are out
/// of scope for this pass (the key is still imported and its public half shown). See docs/28-key-pair-management.md.
/// </summary>
public static class PpkParser
{
    public static bool LooksLikePpk(string text) =>
        text.StartsWith("PuTTY-User-Key-File-", StringComparison.Ordinal);

    public static PpkFile Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var version = 2;
        var algorithm = string.Empty;
        var encryption = "none";
        string? comment = null;
        var publicLines = new List<string>();
        var privateLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (TryHeader(line, "PuTTY-User-Key-File-2", out var v2)) { version = 2; algorithm = v2; }
            else if (TryHeader(line, "PuTTY-User-Key-File-3", out var v3)) { version = 3; algorithm = v3; }
            else if (TryHeader(line, "Encryption", out var enc)) encryption = enc.Trim();
            else if (TryHeader(line, "Comment", out var cmt)) comment = cmt.Trim();
            else if (TryHeader(line, "Public-Lines", out var pubN) && int.TryParse(pubN.Trim(), out var pn))
                CollectBase64(lines, ref i, pn, publicLines);
            else if (TryHeader(line, "Private-Lines", out var privN) && int.TryParse(privN.Trim(), out var qn))
                CollectBase64(lines, ref i, qn, privateLines);
        }

        var encrypted = !string.Equals(encryption, "none", StringComparison.OrdinalIgnoreCase);
        var publicBlob = DecodeBase64(publicLines);
        byte[]? privatePlain = (!encrypted && privateLines.Count > 0) ? DecodeBase64(privateLines) : null;
        return new PpkFile(version, algorithm.Trim(), encrypted, comment, publicBlob, privatePlain);
    }

    /// <summary>
    /// Best-effort conversion of an unencrypted RSA PPK to a PKCS#1 PEM private key. Returns null when the key
    /// is encrypted, not RSA, or malformed — the caller then stores the key verbatim instead.
    /// </summary>
    public static string? TryConvertToPem(PpkFile ppk)
    {
        if (ppk.Encrypted || ppk.PrivatePlaintext is null || ppk.Algorithm != "ssh-rsa")
            return null;

        try
        {
            // Public blob: string "ssh-rsa", mpint e, mpint n.
            var pubPos = 0;
            _ = ReadSshField(ppk.PublicBlob, ref pubPos);
            var e = ReadSshField(ppk.PublicBlob, ref pubPos);
            var n = ReadSshField(ppk.PublicBlob, ref pubPos);

            // Private blob (unencrypted): mpint d, mpint p, mpint q, mpint iqmp.
            var prvPos = 0;
            var d = ReadSshField(ppk.PrivatePlaintext, ref prvPos);
            var p = ReadSshField(ppk.PrivatePlaintext, ref prvPos);
            var q = ReadSshField(ppk.PrivatePlaintext, ref prvPos);
            var iqmp = ReadSshField(ppk.PrivatePlaintext, ref prvPos);

            var nBig = ToBig(n);
            var dBig = ToBig(d);
            var pBig = ToBig(p);
            var qBig = ToBig(q);

            var modulusLen = Unsigned(nBig).Length;
            var halfLen = (modulusLen + 1) / 2;

            var dp = dBig % (pBig - BigInteger.One);
            var dq = dBig % (qBig - BigInteger.One);

            var parameters = new RSAParameters
            {
                Modulus = FixedLength(nBig, modulusLen),
                Exponent = Unsigned(ToBig(e)),
                D = FixedLength(dBig, modulusLen),
                P = FixedLength(pBig, halfLen),
                Q = FixedLength(qBig, halfLen),
                DP = FixedLength(dp, halfLen),
                DQ = FixedLength(dq, halfLen),
                InverseQ = FixedLength(ToBig(iqmp), halfLen)
            };

            using var rsa = RSA.Create();
            rsa.ImportParameters(parameters);
            return rsa.ExportRSAPrivateKeyPem();
        }
        catch
        {
            return null;
        }
    }

    // ---- helpers ----

    private static bool TryHeader(string line, string key, out string value)
    {
        var prefix = key + ":";
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = line[prefix.Length..];
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static void CollectBase64(string[] lines, ref int i, int count, List<string> into)
    {
        for (var k = 0; k < count && i + 1 < lines.Length; k++)
            into.Add(lines[++i].Trim());
    }

    private static byte[] DecodeBase64(List<string> lines)
    {
        try { return Convert.FromBase64String(string.Concat(lines)); }
        catch { return []; }
    }

    private static byte[] ReadSshField(byte[] blob, ref int pos)
    {
        var len = (int)BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(pos));
        pos += 4;
        var slice = blob[pos..(pos + len)];
        pos += len;
        return slice;
    }

    private static BigInteger ToBig(byte[] unsignedBigEndian) =>
        new(unsignedBigEndian, isUnsigned: true, isBigEndian: true);

    private static byte[] Unsigned(BigInteger value) =>
        value.ToByteArray(isUnsigned: true, isBigEndian: true);

    private static byte[] FixedLength(BigInteger value, int length)
    {
        var raw = Unsigned(value);
        if (raw.Length == length) return raw;
        if (raw.Length > length) return raw[(raw.Length - length)..];
        var padded = new byte[length];
        Buffer.BlockCopy(raw, 0, padded, length - raw.Length, raw.Length);
        return padded;
    }
}
