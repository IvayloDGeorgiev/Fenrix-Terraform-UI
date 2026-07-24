namespace Fenrix.IaCStudio.Domain.Terraform;

/// <summary>
/// A Terraform <c>required_version</c> constraint expression — one or more comparisons combined with
/// AND (comma-separated), e.g. <c>"&gt;= 1.5.0, &lt; 2.0.0"</c> or <c>"~&gt; 1.15"</c>. Supported operators
/// mirror Terraform's version constraint syntax: <c>=</c>, <c>!=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>,
/// <c>&lt;=</c>, and the pessimistic <c>~&gt;</c>. A bare version (no operator) is treated as <c>=</c>.
/// See docs/05-terraform-engine.md and HashiCorp's version-constraint documentation.
/// </summary>
public sealed class TerraformVersionConstraint
{
    private readonly IReadOnlyList<Comparison> _comparisons;

    /// <summary>The original, human-readable constraint text.</summary>
    public string Expression { get; }

    private TerraformVersionConstraint(string expression, IReadOnlyList<Comparison> comparisons)
    {
        Expression = expression;
        _comparisons = comparisons;
    }

    public static TerraformVersionConstraint Parse(string expression)
    {
        if (!TryParse(expression, out var constraint))
            throw new FormatException($"'{expression}' is not a valid Terraform version constraint.");
        return constraint!;
    }

    public static bool TryParse(string? expression, out TerraformVersionConstraint? constraint)
    {
        constraint = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var comparisons = new List<Comparison>();
        foreach (var raw in expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseComparison(raw, out var comparison))
                return false;
            comparisons.Add(comparison!);
        }

        if (comparisons.Count == 0)
            return false;

        constraint = new TerraformVersionConstraint(expression.Trim(), comparisons);
        return true;
    }

    /// <summary>True when <paramref name="version"/> satisfies every comparison in the expression.</summary>
    public bool IsSatisfiedBy(TerraformVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        foreach (var c in _comparisons)
            if (!c.Matches(version))
                return false;
        return true;
    }

    public override string ToString() => Expression;

    private static bool TryParseComparison(string raw, out Comparison? comparison)
    {
        comparison = null;
        var text = raw.Trim();
        if (text.Length == 0)
            return false;

        // Longest operators first so "<=" isn't read as "<".
        ConstraintOperator op;
        if (text.StartsWith("~>", StringComparison.Ordinal)) { op = ConstraintOperator.Pessimistic; text = text[2..]; }
        else if (text.StartsWith(">=", StringComparison.Ordinal)) { op = ConstraintOperator.GreaterOrEqual; text = text[2..]; }
        else if (text.StartsWith("<=", StringComparison.Ordinal)) { op = ConstraintOperator.LessOrEqual; text = text[2..]; }
        else if (text.StartsWith("!=", StringComparison.Ordinal)) { op = ConstraintOperator.NotEqual; text = text[2..]; }
        else if (text.StartsWith('>')) { op = ConstraintOperator.Greater; text = text[1..]; }
        else if (text.StartsWith('<')) { op = ConstraintOperator.Less; text = text[1..]; }
        else if (text.StartsWith('=')) { op = ConstraintOperator.Equal; text = text[1..]; }
        else { op = ConstraintOperator.Equal; }

        text = text.Trim();
        if (!TerraformVersion.TryParse(text, out var version))
            return false;

        // Capture how many numeric components were written, which the pessimistic operator needs.
        var declared = DeclaredComponentCount(text);
        comparison = new Comparison(op, version!, declared);
        return true;
    }

    private static int DeclaredComponentCount(string versionText)
    {
        var core = versionText.TrimStart('v', 'V');
        var dash = core.IndexOf('-');
        if (dash >= 0) core = core[..dash];
        return core.Split('.', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private enum ConstraintOperator { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, Pessimistic }

    private sealed class Comparison(ConstraintOperator op, TerraformVersion version, int declaredComponents)
    {
        public bool Matches(TerraformVersion v) => op switch
        {
            ConstraintOperator.Equal => v.CompareTo(version) == 0,
            ConstraintOperator.NotEqual => v.CompareTo(version) != 0,
            ConstraintOperator.Greater => v > version,
            ConstraintOperator.GreaterOrEqual => v >= version,
            ConstraintOperator.Less => v < version,
            ConstraintOperator.LessOrEqual => v <= version,
            ConstraintOperator.Pessimistic => v >= version && v < PessimisticUpperBound(),
            _ => false
        };

        // "~> 1.15.0" allows the patch to float (< 1.16.0); "~> 1.15" allows the minor to float (< 2.0.0).
        private TerraformVersion PessimisticUpperBound() => declaredComponents switch
        {
            <= 1 => new TerraformVersion(version.Major + 1, 0, 0),
            2 => new TerraformVersion(version.Major + 1, 0, 0),
            _ => new TerraformVersion(version.Major, version.Minor + 1, 0)
        };
    }
}
