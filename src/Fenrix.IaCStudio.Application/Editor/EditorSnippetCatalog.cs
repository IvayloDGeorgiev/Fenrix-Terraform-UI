namespace Fenrix.IaCStudio.Application.Editor;

/// <summary>
/// The canonical HCL scaffolds offered by the editor's snippet palette (resource, variable, output, provider,
/// module, backend, data, locals). Each body is already <c>terraform fmt</c>-clean (2-space indent, aligned
/// <c>=</c>), so inserting one and beautifying is a no-op. Pure and dependency-free — invoked directly from the
/// UI. See docs/07-visual-builder.md, docs/13-ui-design.md, docs/22-terraform-files-model.md.
/// </summary>
public static class EditorSnippetCatalog
{
    public static IReadOnlyList<EditorSnippet> All { get; } =
    [
        new("resource", "Resource", "Config",
            "A managed resource block.",
            """
            resource "resource_type" "name" {
              # required arguments
            }
            """),

        new("data", "Data source", "Config",
            "A read-only data source lookup.",
            """
            data "data_source_type" "name" {
              # lookup arguments
            }
            """),

        new("variable", "Variable", "Inputs",
            "An input variable with type, description, and default.",
            """
            variable "name" {
              type        = string
              description = "Describe this variable."
              default     = null
            }
            """),

        new("output", "Output", "Outputs",
            "An output value.",
            """
            output "name" {
              description = "Describe this output."
              value       = null
            }
            """),

        new("locals", "Locals", "Config",
            "A locals block for computed local values.",
            """
            locals {
              name = "value"
            }
            """),

        new("provider", "Provider", "Config",
            "A provider configuration block.",
            """
            provider "name" {
              # provider settings
            }
            """),

        new("module", "Module", "Config",
            "A module call with source and version.",
            """
            module "name" {
              source  = "./modules/name"
              version = ""

              # input variables
            }
            """),

        new("backend", "Backend (terraform)", "Config",
            "A terraform settings block with required_providers and a backend.",
            """
            terraform {
              required_version = ">= 1.5.0"

              required_providers {
                name = {
                  source  = "namespace/name"
                  version = "~> 1.0"
                }
              }

              backend "local" {
                path = "terraform.tfstate"
              }
            }
            """),
    ];

    /// <summary>Finds a snippet by its stable key, or null.</summary>
    public static EditorSnippet? Find(string key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
}
