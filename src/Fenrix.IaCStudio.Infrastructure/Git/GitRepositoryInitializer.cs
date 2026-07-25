using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Git;
using Fenrix.IaCStudio.Contracts.Git;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Git;

/// <summary>
/// Initialises a Git repository in a directory using the resolved binary and the shared process
/// coordinator (so the <c>git init</c> is recorded in redacted history like any other Git command). Depends
/// only on discovery + the coordinator — never on the project service — so it can be used during project
/// creation without a DI cycle. See docs/08-git-engine.md.
/// </summary>
public sealed class GitRepositoryInitializer(
    IGitDiscovery discovery,
    GitProcessCoordinator coordinator,
    ILogger<GitRepositoryInitializer> logger) : IGitRepositoryInitializer
{
    private readonly IGitDiscovery _discovery = discovery;
    private readonly GitProcessCoordinator _coordinator = coordinator;
    private readonly ILogger<GitRepositoryInitializer> _logger = logger;

    public async Task<bool> InitializeAsync(string directory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        var install = await _discovery.ResolveAsync(null, ct);
        if (install is null)
        {
            _logger.LogWarning("Skipped git init for {Dir}: no Git binary found.", directory);
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var def = GitCommandCatalog.Init();
            var request = new GitCommandRequest(
                Guid.Empty, def.Kind, install.ExecutablePath, directory, def.Command, def.Arguments,
                new Dictionary<string, string>(0), def.Risk);
            var run = await _coordinator.RunAsync(request, output: null, captureLog: true, ct);
            if (!run.Succeeded)
                _logger.LogWarning("git init in {Dir} exited {Code}.", directory, run.Process.ExitCode);
            return run.Succeeded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "git init failed for {Dir}.", directory);
            return false;
        }
    }
}
