using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Parses <c>terraform workspace list</c> output into a <see cref="WorkspaceSnapshot"/>. Each line is a
/// workspace name; the current workspace is prefixed with <c>* </c>. Blank lines are ignored. See
/// docs/05-terraform-engine.md.
/// </summary>
public static class WorkspaceListParser
{
    public static WorkspaceSnapshot Parse(string listOutput)
    {
        if (string.IsNullOrWhiteSpace(listOutput))
            return WorkspaceSnapshot.Empty;

        var names = new List<string>();
        string? current = null;

        foreach (var raw in listOutput.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var isCurrent = false;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                isCurrent = true;
                trimmed = trimmed[2..].Trim();
            }
            else
            {
                trimmed = trimmed.Trim();
            }

            if (trimmed.Length == 0)
                continue;

            names.Add(trimmed);
            if (isCurrent)
                current = trimmed;
        }

        return names.Count == 0 ? WorkspaceSnapshot.Empty : new WorkspaceSnapshot(names, current);
    }
}
