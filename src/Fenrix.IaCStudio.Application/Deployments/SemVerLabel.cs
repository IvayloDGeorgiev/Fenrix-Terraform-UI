using System.Globalization;

namespace Fenrix.IaCStudio.Application.Deployments;

/// <summary>
/// A tolerant parser + comparer for version labels (e.g. <c>1.0</c>, <c>1.5-rc</c>, <c>2.0.0-dev.3</c>).
/// Labels are free-form, but when they look like semver we order them by precedence so the version list and
/// matrix can sort meaningfully: numeric core compared field-by-field, and a prerelease (anything after
/// <c>-</c>) sorts <em>before</em> the same core without one (2.0.0-rc &lt; 2.0.0), matching SemVer 2.0.0 and
/// the existing Terraform constraint logic. Non-semver labels compare by ordinal string as a fallback. Pure
/// (no IO) — verified by a reference port. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed record SemVerLabel(int Major, int Minor, int Patch, string? Prerelease, string Original)
    : IComparable<SemVerLabel>
{
    public bool IsSemVer { get; init; }

    /// <summary>Parses a label. Always succeeds; <see cref="IsSemVer"/> tells whether a numeric core was found.</summary>
    public static SemVerLabel Parse(string? label)
    {
        var raw = (label ?? string.Empty).Trim();
        var core = raw;
        string? pre = null;

        // Strip a leading 'v' (v1.2.3) commonly used on tags.
        if (core.StartsWith('v') || core.StartsWith('V'))
            core = core[1..];

        var dash = core.IndexOf('-');
        if (dash >= 0)
        {
            pre = core[(dash + 1)..];
            core = core[..dash];
        }
        // Also treat a build-metadata '+' as terminating the core.
        var plus = core.IndexOf('+');
        if (plus >= 0)
            core = core[..plus];

        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int major = 0, minor = 0, patch = 0;
        var ok = parts.Length > 0;
        if (ok && !TryInt(parts[0], out major)) ok = false;
        if (ok && parts.Length > 1 && !TryInt(parts[1], out minor)) ok = false;
        if (ok && parts.Length > 2 && !TryInt(parts[2], out patch)) ok = false;

        return new SemVerLabel(major, minor, patch, string.IsNullOrEmpty(pre) ? null : pre, raw) { IsSemVer = ok };
    }

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>Semver precedence when both are semver; otherwise ordinal on the original text.</summary>
    public int CompareTo(SemVerLabel? other)
    {
        if (other is null) return 1;

        if (!IsSemVer || !other.IsSemVer)
            return string.CompareOrdinal(Original, other.Original);

        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // No prerelease outranks a prerelease of the same core (1.0.0 > 1.0.0-rc).
        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>Dot-separated identifier comparison: numeric identifiers compare numerically and rank below alphanumerics.</summary>
    private static int ComparePrerelease(string a, string b)
    {
        var ap = a.Split('.');
        var bp = b.Split('.');
        var n = Math.Max(ap.Length, bp.Length);
        for (var i = 0; i < n; i++)
        {
            if (i >= ap.Length) return -1; // shorter set of identifiers is smaller
            if (i >= bp.Length) return 1;

            var an = TryInt(ap[i], out var ai);
            var bn = TryInt(bp[i], out var bi);
            int c;
            if (an && bn) c = ai.CompareTo(bi);
            else if (an) c = -1;            // numeric identifiers rank below alphanumeric
            else if (bn) c = 1;
            else c = string.CompareOrdinal(ap[i], bp[i]);
            if (c != 0) return c;
        }
        return 0;
    }
}
