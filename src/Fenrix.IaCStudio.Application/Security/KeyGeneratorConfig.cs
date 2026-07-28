using System.Text;
using Fenrix.IaCStudio.Contracts.Security;
using Fenrix.IaCStudio.Domain.Security;

namespace Fenrix.IaCStudio.Application.Security;

/// <summary>
/// Emits the self-contained Terraform config that generates a key pair on the backend: a
/// <c>tls_private_key</c> plus outputs carrying the private key (sensitive), the OpenSSH public key and the
/// SHA-256 fingerprint. When registering in a cloud, an <c>aws_key_pair</c> is added so the key is created in
/// AWS in the same apply — no console round-trip. The generated private key is captured from the output and
/// written straight into the secure store. See docs/28-key-pair-management.md.
/// </summary>
public static class KeyGeneratorConfig
{
    // Output names the runner reads back from `output -json`.
    public const string PrivateKeyOutput = "private_key_pem";
    public const string PublicKeyOutput = "public_key_openssh";
    public const string FingerprintOutput = "public_key_fingerprint_sha256";
    public const string CloudKeyNameOutput = "cloud_key_name";

    public static string Build(GenerateKeyRequest request)
    {
        var sb = new StringBuilder();
        var register = request.RegisterInCloud;

        sb.AppendLine("terraform {");
        sb.AppendLine("  required_providers {");
        sb.AppendLine("    tls = {");
        sb.AppendLine("      source  = \"hashicorp/tls\"");
        sb.AppendLine("      version = \"~> 4.0\"");
        sb.AppendLine("    }");
        if (register)
        {
            sb.AppendLine("    aws = {");
            sb.AppendLine("      source  = \"hashicorp/aws\"");
            sb.AppendLine("      version = \"~> 5.0\"");
            sb.AppendLine("    }");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();

        if (register)
        {
            // Region + credentials are supplied via the composed AWS_* environment variables.
            sb.AppendLine("provider \"aws\" {}");
            sb.AppendLine();
        }

        sb.AppendLine("resource \"tls_private_key\" \"key\" {");
        sb.AppendLine($"  algorithm = \"{AlgorithmName(request.Algorithm)}\"");
        if (request.Algorithm == KeyAlgorithm.Rsa)
            sb.AppendLine($"  rsa_bits  = {(request.RsaBits > 0 ? request.RsaBits : 4096)}");
        else if (request.Algorithm == KeyAlgorithm.Ecdsa)
            sb.AppendLine($"  ecdsa_curve = \"{EcdsaCurve(request.EcdsaCurve)}\"");
        sb.AppendLine("}");
        sb.AppendLine();

        if (register)
        {
            sb.AppendLine("resource \"aws_key_pair\" \"key\" {");
            sb.AppendLine($"  key_name   = \"{Escape(request.Name)}\"");
            sb.AppendLine("  public_key = tls_private_key.key.public_key_openssh");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        AppendOutput(sb, PrivateKeyOutput, "tls_private_key.key.private_key_pem", sensitive: true);
        AppendOutput(sb, PublicKeyOutput, "tls_private_key.key.public_key_openssh", sensitive: false);
        AppendOutput(sb, FingerprintOutput, "tls_private_key.key.public_key_fingerprint_sha256", sensitive: false);
        if (register)
            AppendOutput(sb, CloudKeyNameOutput, "aws_key_pair.key.key_name", sensitive: false);

        return sb.ToString();
    }

    private static void AppendOutput(StringBuilder sb, string name, string valueExpr, bool sensitive)
    {
        sb.AppendLine($"output \"{name}\" {{");
        sb.AppendLine($"  value     = {valueExpr}");
        if (sensitive)
            sb.AppendLine("  sensitive = true");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string AlgorithmName(KeyAlgorithm algorithm) => algorithm switch
    {
        KeyAlgorithm.Ecdsa => "ECDSA",
        KeyAlgorithm.Ed25519 => "ED25519",
        _ => "RSA"
    };

    private static string EcdsaCurve(string? curve) => curve switch
    {
        "P384" => "P384",
        "P521" => "P521",
        _ => "P256"
    };

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
