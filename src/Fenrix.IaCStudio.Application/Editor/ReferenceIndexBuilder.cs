using Fenrix.IaCStudio.Application.Hcl;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Editor;

/// <summary>
/// Gathers the insertable HCL references available across an environment's config files — <c>var.</c>,
/// <c>local.</c>, <c>module.</c>, <c>data.</c>, and resource references — for the editor's reference-helper
/// palette. Symbols come from parsing every <c>.tf</c> file's outline (the live buffer overrides the on-disk
/// copy of the file being edited); resource/data references are made <em>schema-aware</em> by attaching the
/// attribute list from the cached provider schema (Phase 10). Pure and dependency-free. See docs/07-visual-builder.md,
/// docs/13-ui-design.md.
/// </summary>
public static class ReferenceIndexBuilder
{
    /// <summary>
    /// Builds the reference index from a map of <c>relativePath → file content</c> and the cached provider
    /// schema set (pass <see cref="ProviderSchemaSet.Empty"/> when none is cached — references still work,
    /// just without attribute suggestions).
    /// </summary>
    public static ReferenceIndex Build(IReadOnlyDictionary<string, string> files, ProviderSchemaSet schema)
    {
        var entries = new List<ReferenceEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(ReferenceEntry e)
        {
            if (seen.Add(e.Insert)) entries.Add(e);
        }

        foreach (var (path, content) in files)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;

            IReadOnlyList<HclBlockHandle> handles;
            try { handles = HclReader.ReadOutline(content); }
            catch { continue; }

            foreach (var h in handles)
            {
                switch (h.Type)
                {
                    case "variable" when h.Labels.Count >= 1:
                        Add(new ReferenceEntry(ReferenceKind.Variable, $"var.{h.Labels[0]}",
                            $"var.{h.Labels[0]}", path, []));
                        break;

                    case "output" when h.Labels.Count >= 1:
                        // Root outputs aren't referenceable in the same config, but listing them helps when
                        // authoring modules; surfaced as output.<name> for convenience.
                        Add(new ReferenceEntry(ReferenceKind.Output, $"output.{h.Labels[0]}",
                            $"output.{h.Labels[0]}", path, []));
                        break;

                    case "module" when h.Labels.Count >= 1:
                        Add(new ReferenceEntry(ReferenceKind.Module, $"module.{h.Labels[0]}",
                            $"module.{h.Labels[0]}", path, []));
                        break;

                    case "data" when h.Labels.Count >= 2:
                        Add(new ReferenceEntry(ReferenceKind.DataSource, $"data.{h.Labels[0]}.{h.Labels[1]}",
                            $"data.{h.Labels[0]}.{h.Labels[1]}", h.Labels[0],
                            AttributesFor(schema, h.Labels[0], isData: true)));
                        break;

                    case "resource" when h.Labels.Count >= 2:
                        Add(new ReferenceEntry(ReferenceKind.Resource, $"{h.Labels[0]}.{h.Labels[1]}",
                            $"{h.Labels[0]}.{h.Labels[1]}", h.Labels[0],
                            AttributesFor(schema, h.Labels[0], isData: false)));
                        break;
                }
            }

            // locals: expand each entry to local.<name>.
            foreach (var h in handles.Where(x => x.Type == "locals"))
            {
                IReadOnlyList<HclParsedArgument> args;
                try { args = HclReader.ReadArguments(content, h); }
                catch { continue; }
                foreach (var a in args)
                    Add(new ReferenceEntry(ReferenceKind.Local, $"local.{a.Name}", $"local.{a.Name}", path, []));
            }
        }

        entries.Sort(static (a, b) =>
        {
            var k = a.Kind.CompareTo(b.Kind);
            return k != 0 ? k : string.CompareOrdinal(a.Insert, b.Insert);
        });

        return new ReferenceIndex(entries);
    }

    /// <summary>Attribute names for a resource/data type from the cached schema (id first, then name-sorted).</summary>
    private static IReadOnlyList<string> AttributesFor(ProviderSchemaSet schema, string type, bool isData)
    {
        if (schema.IsEmpty) return [];

        foreach (var provider in schema.Providers)
        {
            var pool = isData ? provider.DataSourceSchemas : provider.ResourceSchemas;
            var match = pool.FirstOrDefault(r => string.Equals(r.Type, type, StringComparison.Ordinal));
            if (match is null) continue;

            var names = match.Block.Attributes
                .Select(a => a.Name)
                .OrderBy(n => n == "id" ? 0 : 1)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();
            return names;
        }

        return [];
    }
}
