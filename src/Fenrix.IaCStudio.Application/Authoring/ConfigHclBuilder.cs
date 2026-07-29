using System.Text;
using Fenrix.IaCStudio.Application.Hcl;

namespace Fenrix.IaCStudio.Application.Authoring;

/// <summary>
/// Pure builders that turn form-authoring specs into <see cref="HclBlock"/>s (rendered by
/// <see cref="HclEmitter"/>). This is the config-side coverage of docs/22-terraform-files-model.md: providers,
/// versions/terraform settings, variables, outputs, locals, tfvars, backends, data sources, and module calls.
/// Deterministic and I/O-free, so the reference port asserts on exact output.
/// </summary>
public static class ConfigHclBuilder
{
    // ---- variables.tf ----

    public static HclBlock Variable(VariableSpec spec)
    {
        var args = new List<HclArgument>();
        if (!string.IsNullOrWhiteSpace(spec.Type))
            args.Add(new HclArgument("type", HclValue.Raw(spec.Type!)));            // type is an expression, unquoted
        if (spec.Default is not null)
            args.Add(new HclArgument("default", spec.Default));
        if (!string.IsNullOrWhiteSpace(spec.Description))
            args.Add(new HclArgument("description", new HclString(spec.Description!)));
        if (spec.Sensitive)
            args.Add(new HclArgument("sensitive", new HclBool(true)));
        if (spec.Nullable == false)
            args.Add(new HclArgument("nullable", new HclBool(false)));
        return new HclBlock("variable", [spec.Name], args, []);
    }

    // ---- outputs.tf ----

    public static HclBlock Output(OutputSpec spec)
    {
        var args = new List<HclArgument> { new("value", HclValue.Raw(spec.ValueExpression)) };
        if (!string.IsNullOrWhiteSpace(spec.Description))
            args.Add(new HclArgument("description", new HclString(spec.Description!)));
        if (spec.Sensitive)
            args.Add(new HclArgument("sensitive", new HclBool(true)));
        return new HclBlock("output", [spec.Name], args, []);
    }

    // ---- locals.tf ----

    public static HclBlock Locals(IReadOnlyList<LocalSpec> locals)
    {
        var args = locals.Select(l => new HclArgument(l.Name, HclValue.Raw(l.ValueExpression))).ToList();
        return new HclBlock("locals", [], args, []);
    }

    // ---- providers.tf ----

    public static HclBlock Provider(ProviderSpec spec)
    {
        var args = new List<HclArgument>();
        if (!string.IsNullOrWhiteSpace(spec.Alias))
            args.Add(new HclArgument("alias", new HclString(spec.Alias!)));
        args.AddRange(spec.Arguments);
        return new HclBlock("provider", [spec.Name], args, []);
    }

    // ---- versions.tf / terraform.tf ----

    public static HclBlock TerraformSettings(TerraformSettingsSpec spec)
    {
        var args = new List<HclArgument>();
        if (!string.IsNullOrWhiteSpace(spec.RequiredVersion))
            args.Add(new HclArgument("required_version", new HclString(spec.RequiredVersion!)));

        var blocks = new List<HclBlock>();
        if (spec.RequiredProviders.Count > 0)
            blocks.Add(RequiredProviders(spec.RequiredProviders));
        if (spec.Backend is not null)
            blocks.Add(Backend(spec.Backend));

        return new HclBlock("terraform", [], args, blocks);
    }

    public static HclBlock RequiredProviders(IReadOnlyList<RequiredProviderSpec> providers)
    {
        var args = new List<HclArgument>();
        foreach (var p in providers)
        {
            var entries = new List<KeyValuePair<string, HclValue>>
            {
                new("source", new HclString(p.Source))
            };
            if (!string.IsNullOrWhiteSpace(p.VersionConstraint))
                entries.Add(new KeyValuePair<string, HclValue>("version", new HclString(p.VersionConstraint!)));
            args.Add(new HclArgument(p.LocalName, new HclObject(entries)));
        }
        return new HclBlock("required_providers", [], args, []);
    }

    public static HclBlock Backend(BackendSpec spec) =>
        new("backend", [spec.Type], spec.Arguments, []);

    // ---- modules ----

    public static HclBlock Module(ModuleSpec spec)
    {
        var args = new List<HclArgument> { new("source", new HclString(spec.Source)) };
        if (!string.IsNullOrWhiteSpace(spec.Version))
            args.Add(new HclArgument("version", new HclString(spec.Version!)));
        args.AddRange(spec.Arguments);
        return new HclBlock("module", [spec.Name], args, []);
    }

    // ---- resources & data sources (schema-driven) ----

    public static HclBlock Resource(ResourceSpec spec) =>
        new(spec.IsDataSource ? "data" : "resource", [spec.Type, spec.Name], spec.Arguments, spec.NestedBlocks);

    // ---- *.tfvars (not a block — bare name = value lines) ----

    public static string Tfvars(IReadOnlyList<TfvarsEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
            sb.Append(e.Name).Append(" = ").Append(HclEmitter.EmitValue(e.Value)).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
}
