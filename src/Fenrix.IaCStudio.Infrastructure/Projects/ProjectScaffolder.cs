using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Contracts.Projects;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

/// <summary>
/// Creates the recommended new-project structure on disk: modules/, environments/&lt;env&gt;/ with
/// starter Terraform files, plus README and .gitignore. See docs/03-domain-model.md.
/// </summary>
public sealed class ProjectScaffolder(ILogger<ProjectScaffolder> logger) : IProjectScaffolder
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly ILogger<ProjectScaffolder> _logger = logger;

    public async Task ScaffoldAsync(string projectRoot, CreateProjectRequest request, CancellationToken ct = default)
    {
        Directory.CreateDirectory(projectRoot);

        // modules/
        foreach (var module in new[] { "networking", "compute", "database" })
            Directory.CreateDirectory(Path.Combine(projectRoot, "modules", module));

        var environments = request.Environments.Count > 0
            ? request.Environments
            : CreateProjectRequest.DefaultEnvironments();

        foreach (var env in environments)
        {
            var slug = Slug(env.Name);
            var envDir = Path.Combine(projectRoot, "environments", slug);
            Directory.CreateDirectory(envDir);

            await WriteAsync(Path.Combine(envDir, "providers.tf"), ProvidersTf(request.RequiredTerraformVersion), ct);
            await WriteAsync(Path.Combine(envDir, "main.tf"), MainTf(env.Name), ct);
            await WriteAsync(Path.Combine(envDir, "variables.tf"), VariablesTf(), ct);
            await WriteAsync(Path.Combine(envDir, "outputs.tf"), OutputsTf(), ct);
            await WriteAsync(Path.Combine(envDir, $"{slug}.tfvars"), TfVars(env.Name), ct);
            await WriteAsync(Path.Combine(envDir, "backend.hcl"), BackendHcl(slug), ct);
        }

        await WriteAsync(Path.Combine(projectRoot, "README.md"), Readme(request.Name), ct);

        if (request.InitializeGit)
            await WriteAsync(Path.Combine(projectRoot, ".gitignore"), GitIgnore(), ct);

        _logger.LogInformation("Scaffolded project {Name} at {Root} ({Count} environments)",
            request.Name, projectRoot, environments.Count);
    }

    private static async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Utf8NoBom, ct);
    }

    /// <summary>Lowercase, filesystem-safe environment directory name.</summary>
    internal static string Slug(string name)
    {
        var cleaned = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (cleaned.Contains("--"))
            cleaned = cleaned.Replace("--", "-");
        return cleaned.Trim('-') is { Length: > 0 } s ? s : "env";
    }

    private static string ProvidersTf(string? tfVersion)
    {
        var required = string.IsNullOrWhiteSpace(tfVersion) ? "" : $"  required_version = \">= {tfVersion}\"\n";
        return $$"""
        terraform {
        {{required}}  required_providers {
            # Add providers here, e.g.:
            # azurerm = {
            #   source  = "hashicorp/azurerm"
            #   version = "~> 4.0"
            # }
          }
        }
        """;
    }

    private static string MainTf(string envName) => $$"""
        # {{envName}} environment root configuration.
        # Compose modules from ../../modules here.
        """;

    private static string VariablesTf() => """
        # Input variables for this environment.
        # variable "location" {
        #   type    = string
        #   default = "westeurope"
        # }
        """;

    private static string OutputsTf() => """
        # Outputs surfaced after apply.
        # output "example" {
        #   value = null
        # }
        """;

    private static string TfVars(string envName) => $"""
        # Values for the {envName} environment.
        """;

    private static string BackendHcl(string slug) => $"""
        # Backend configuration for the {slug} environment.
        # Passed via: terraform init -backend-config=backend.hcl
        """;

    private static string Readme(string name) => $"""
        # {name}

        Infrastructure managed with Fenrix IaC Studio.

        ## Structure

        - `modules/` – reusable Terraform modules.
        - `environments/<env>/` – per-environment root configuration (Terraform runs here).

        Each environment is deployed independently and can target a different cloud account.
        """;

    private static string GitIgnore() => """
        # Terraform provider/module cache (machine-generated, large — never version-controlled).
        .terraform/

        # Local CLI config and crash logs (may hold credentials / are noise).
        crash.log
        crash.*.log
        .terraformrc
        terraform.rc

        # NOTE: Terraform config, plans (plans/), state (*.tfstate) and the provider lock
        # (.terraform.lock.hcl) are intentionally tracked. Plan and state files can contain sensitive
        # values in plaintext — keep this repository private and access-controlled.

        # Fenrix (local machine artifacts only; the manifest is intentionally tracked)
        .fenrix/artifacts/
        .fenrix/locks/
        """;
}
