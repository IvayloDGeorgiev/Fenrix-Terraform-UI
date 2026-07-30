using System.Text.RegularExpressions;
using Fenrix.IaCStudio.Application.Hcl;
using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Enterprise;

/// <summary>
/// Pure instantiation of a <see cref="ConfigTemplate"/>: substitutes <c>{{name}}</c> placeholders in the body
/// with typed values (String ⇒ quoted+escaped via the HCL emitter; Number/Bool/Expression ⇒ raw), leaving the
/// rest of the HCL untouched. Placeholders stand for a whole value expression and are written <em>unquoted</em>
/// in the body (e.g. <c>name = {{bucket}}</c>). No IO — covered by the reference port. See docs/29-enterprise.md.
/// </summary>
public static class TemplateInstantiator
{
    private static readonly Regex Placeholder = new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled);

    public sealed record Result(bool Ok, string Hcl, IReadOnlyList<string> MissingRequired, IReadOnlyList<string> Unknown);

    /// <summary>
    /// Substitutes placeholders using <paramref name="values"/> (by parameter name), typing each value via the
    /// parameter definitions. Missing required parameters (no value and no default) are reported and block the
    /// result; unknown placeholders (present in the body but not defined) are reported as a warning.
    /// </summary>
    public static Result Instantiate(
        ConfigTemplate template, IReadOnlyDictionary<string, string?> values)
    {
        var byName = template.Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var missing = new List<string>();
        var unknown = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var hcl = Placeholder.Replace(template.Body, match =>
        {
            var name = match.Groups[1].Value;
            seen.Add(name);

            if (!byName.TryGetValue(name, out var param))
            {
                unknown.Add(name);
                return match.Value; // leave the placeholder in place
            }

            var raw = values.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v : param.DefaultValue;
            if (string.IsNullOrEmpty(raw))
            {
                if (param.Required) missing.Add(name);
                return raw ?? string.Empty;
            }

            return Emit(param.Type, raw);
        });

        // Required parameters whose placeholder never appears in the body still count as satisfied by presence
        // of a value; those that are both unreferenced and unset are not required for a correct emit, so we only
        // flag required parameters that were referenced but left blank (collected above).
        var ok = missing.Count == 0;
        return new Result(ok, hcl, missing.Distinct().ToList(), unknown.Distinct().ToList());
    }

    private static string Emit(TemplateParameterType type, string raw) => type switch
    {
        TemplateParameterType.String => $"\"{HclEmitter.EscapeString(raw)}\"",
        _ => raw // Number / Bool / Expression are emitted verbatim
    };
}
