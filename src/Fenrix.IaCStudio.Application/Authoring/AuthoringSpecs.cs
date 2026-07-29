using Fenrix.IaCStudio.Application.Hcl;

namespace Fenrix.IaCStudio.Application.Authoring;

// Form-authoring input records for the config-side file types (docs/22-terraform-files-model.md). Each is a
// plain data shape the UI binds to; ConfigHclBuilder turns it into an HclBlock for the live preview and the
// generated file. Kept in Application (not Contracts) because they carry HclValue.

/// <summary>Input variable declaration (<c>variable "name" {}</c>).</summary>
public sealed record VariableSpec(
    string Name,
    string? Type,
    HclValue? Default,
    string? Description,
    bool Sensitive,
    bool? Nullable);

/// <summary>Output declaration (<c>output "name" {}</c>). The value is an HCL expression (raw).</summary>
public sealed record OutputSpec(
    string Name,
    string ValueExpression,
    string? Description,
    bool Sensitive);

/// <summary>A single local value (<c>name = expression</c> inside a <c>locals {}</c> block).</summary>
public sealed record LocalSpec(string Name, string ValueExpression);

/// <summary>Provider configuration (<c>provider "name" {}</c>), optionally aliased.</summary>
public sealed record ProviderSpec(
    string Name,
    string? Alias,
    IReadOnlyList<HclArgument> Arguments);

/// <summary>One entry of <c>required_providers</c> (local name → source + optional version constraint).</summary>
public sealed record RequiredProviderSpec(string LocalName, string Source, string? VersionConstraint);

/// <summary>Remote-state backend configuration (<c>backend "type" {}</c> inside <c>terraform {}</c>).</summary>
public sealed record BackendSpec(string Type, IReadOnlyList<HclArgument> Arguments);

/// <summary>
/// The <c>terraform {}</c> settings block: required Terraform version, required providers, and an optional
/// backend. See docs/22-terraform-files-model.md (versions.tf / terraform.tf).
/// </summary>
public sealed record TerraformSettingsSpec(
    string? RequiredVersion,
    IReadOnlyList<RequiredProviderSpec> RequiredProviders,
    BackendSpec? Backend);

/// <summary>A module call (<c>module "name" {}</c>) with its source, optional version, and inputs.</summary>
public sealed record ModuleSpec(
    string Name,
    string Source,
    string? Version,
    IReadOnlyList<HclArgument> Arguments);

/// <summary>
/// A schema-driven resource or data source. <see cref="IsDataSource"/> selects <c>data</c> vs <c>resource</c>;
/// arguments and nested blocks come from the schema form.
/// </summary>
public sealed record ResourceSpec(
    bool IsDataSource,
    string Type,
    string Name,
    IReadOnlyList<HclArgument> Arguments,
    IReadOnlyList<HclBlock> NestedBlocks);

/// <summary>One <c>name = value</c> line in a <c>*.tfvars</c> file.</summary>
public sealed record TfvarsEntry(string Name, HclValue Value);
