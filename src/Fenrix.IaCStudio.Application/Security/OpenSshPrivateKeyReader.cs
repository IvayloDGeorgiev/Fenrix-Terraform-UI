using System.Buffers.Binary;
using System.Text;

namespace Fenrix.IaCStudio.Application.Security;

/// <summary>
/// Reads the public-key blob embedded (in cleartext, even for passphrase-protected keys) in an OpenSSH
/// private-key file (<c>-----BEGIN OPENSSH PRIVATE KEY-----</c>). This lets Fenrix derive the public key +
/// fingerprint on import without ever decrypting the private half. Format: the "openssh-key-v1" container
/// (PROTOCOL.key). See docs/28-key-pair-management.md.
/// </summary>
public static class OpenSshPrivateKeyReader
{
    private const string Header = "-----BEGIN OPENSSH PRIVATE KEY-----";
    private const string Footer = "-----END OPENSSH PRIVATE KEY-----";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("openssh-key-v1\0");

    public static bool LooksLikeOpenSsh(string text) => text.Contains(Header, StringComparison.Ordinal);

    /// <summary>Extracts the first embedded public-key blob, or null if the file is not a valid v1 container.</summary>
    public static byte[]? TryReadPublicBlob(string text)
    {
        var start = text.IndexOf(Header, StringComparison.Ordinal);
        if (start < 0) return null;
        start += Header.Length;
        var end = text.IndexOf(Footer, start, StringComparison.Ordinal);
        if (end < 0) return null;

        byte[] body;
        try { body = Convert.FromBase64String(StripWhitespace(text[start..end])); }
        catch { return null; }

        try
        {
            var pos = 0;
            if (body.Length < Magic.Length || !body.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                return null;
            pos += Magic.Length;

            _ = ReadString(body, ref pos); // ciphername
            _ = ReadString(body, ref pos); // kdfname
            _ = ReadString(body, ref pos); // kdfoptions
            var keyCount = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(pos));
            pos += 4;
            if (keyCount == 0) return null;

            return ReadString(body, ref pos); // first public key blob
        }
        catch
        {
            return null;
        }
    }

    private static string StripWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    private static byte[] ReadString(byte[] blob, ref int pos)
    {
        var len = (int)BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(pos));
        pos += 4;
        var slice = blob[pos..(pos + len)];
        pos += len;
        return slice;
    }
}
