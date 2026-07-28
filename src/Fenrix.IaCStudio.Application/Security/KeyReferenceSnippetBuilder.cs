using Fenrix.IaCStudio.Contracts.Security;

namespace Fenrix.IaCStudio.Application.Security;

/// <summary>
/// Builds ready-to-paste HCL (or plain-value) snippets that point a <c>connection</c>/<c>provisioner</c>/
/// <c>aws_key_pair</c> block at a managed key. The private key is referenced by its secure path via
/// <c>file(...)</c> — the value is never inlined. See docs/28-key-pair-management.md.
/// </summary>
public static class KeyReferenceSnippetBuilder
{
    public static KeyReferenceSnippet Build(KeyPairSummary key, string securePrivateKeyPath, KeyReferenceKind kind)
    {
        var path = HclPath(securePrivateKeyPath);
        return kind switch
        {
            KeyReferenceKind.Connection => new(kind, "connection block", Connection(path)),
            KeyReferenceKind.Provisioner => new(kind, "remote-exec provisioner", Provisioner(path)),
            KeyReferenceKind.AwsKeyPair => new(kind, "aws_key_pair resource", AwsKeyPair(key)),
            KeyReferenceKind.PublicKey => new(kind, "OpenSSH public key", key.PublicKeyOpenSsh ?? "(public key unavailable)"),
            KeyReferenceKind.SecurePath => new(kind, "secure private-key path", securePrivateKeyPath),
            _ => new(kind, "value", securePrivateKeyPath)
        };
    }

    private static string Connection(string hclPath) =>
        "connection {\n" +
        "  type        = \"ssh\"\n" +
        "  user        = \"ec2-user\"\n" +
        "  host        = self.public_ip\n" +
        "  private_key = file(" + hclPath + ")\n" +
        "}";

    private static string Provisioner(string hclPath) =>
        "provisioner \"remote-exec\" {\n" +
        "  inline = [\"echo connected\"]\n\n" +
        "  connection {\n" +
        "    type        = \"ssh\"\n" +
        "    user        = \"ec2-user\"\n" +
        "    host        = self.public_ip\n" +
        "    private_key = file(" + hclPath + ")\n" +
        "  }\n" +
        "}";

    private static string AwsKeyPair(KeyPairSummary key)
    {
        var name = string.IsNullOrWhiteSpace(key.CloudKeyName) ? key.Name : key.CloudKeyName!;
        var pub = key.PublicKeyOpenSsh ?? "<paste the OpenSSH public key here>";
        return
            "resource \"aws_key_pair\" \"" + Sanitize(key.Name) + "\" {\n" +
            "  key_name   = \"" + Escape(name) + "\"\n" +
            "  public_key = \"" + Escape(pub) + "\"\n" +
            "}";
    }

    private static string HclPath(string path) => "\"" + Escape(path.Replace("\\", "/")) + "\"";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
        var slug = new string(chars).Trim('_');
        return string.IsNullOrEmpty(slug) ? "managed_key" : slug;
    }
}
