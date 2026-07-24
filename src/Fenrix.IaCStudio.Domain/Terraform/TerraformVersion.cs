using System.Globalization;

namespace Fenrix.IaCStudio.Domain.Terraform;

/// <summary>
/// A parsed Terraform version (<c>major.minor.patch</c> with an optional prerelease tag, e.g.
/// <c>1.15.0</c> or <c>1.16.0-beta1</c>). Comparison follows the usual precedence rules: numeric
/// core fields first, then a prerelease sorts <em>before</em> its release. See docs/05-terraform-engine.md.
/// </summary>
public sealed record TerraformVersion : IComparable<TerraformVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>The prerelease tag without the leading dash (e.g. <c>beta1</c>), or empty for a release.</summary>
    public string Prerelease { get; }

    public TerraformVersion(int major, int minor, int patch, string? prerelease = null)
    {
        if (major < 0 || minor < 0 || patch < 0)
            throw new ArgumentOutOfRangeException(nameof(major), "Version fields cannot be negative.");
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease?.Trim() ?? string.Empty;
    }

    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>
    /// Parses a version string. Accepts an optional leading <c>v</c>, a 1–3 part numeric core,
    /// and an optional <c>-prerelease</c> suffix. Missing minor/patch fields default to 0.
    /// </summary>
    public static TerraformVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
            throw new FormatException($"'{value}' is not a recognisable Terraform version.");
        return version!;
    }

    public static bool TryParse(string? value, out TerraformVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        var prerelease = string.Empty;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
        }

        // Strip build metadata if present (rarely used by Terraform, but be lenient).
        var plus = prerelease.IndexOf('+');
        if (plus >= 0)
            prerelease = prerelease[..plus];

        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 3)
            return false;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return false;
            numbers[i] = n;
        }

        version = new TerraformVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    public int CompareTo(TerraformVersion? other)
    {
        if (other is null) return 1;

        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        // A prerelease has lower precedence than the corresponding release.
        if (IsPrerelease && !other.IsPrerelease) return -1;
        if (!IsPrerelease && other.IsPrerelease) return 1;
        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    public static bool operator <(TerraformVersion a, TerraformVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(TerraformVersion a, TerraformVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(TerraformVersion a, TerraformVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TerraformVersion a, TerraformVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
}
